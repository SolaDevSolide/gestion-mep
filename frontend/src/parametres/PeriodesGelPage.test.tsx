import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import type * as moduleApi from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { PageCalendrier, PeriodeGel } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import PeriodesGelPage, { decrireQuand, prochaines, saisieDe } from './PeriodesGelPage';

vi.mock('../api', async (importOriginal) => ({
  ApiError: (await importOriginal<typeof moduleApi>()).ApiError,
  api: {
    periodesGel: vi.fn(),
    calendrier: vi.fn(),
    catalogueSystemes: vi.fn(async () => []),
    creerPeriodeGel: vi.fn(async () => ({})),
    modifierPeriodeGel: vi.fn(async () => ({})),
    supprimerPeriodeGel: vi.fn(async () => undefined),
  },
}));

const periode = (p: Partial<PeriodeGel> = {}): PeriodeGel => ({
  id: 'g1',
  nom: 'Clôture comptable',
  debut: '2026-11-25T23:00:00Z',
  fin: '2026-11-30T23:00:00Z',
  motif: 'Arrêtés de fin de mois',
  systemeCatalogueIds: [],
  systemes: [],
  environnements: [],
  productionSeulement: true,
  recurrence: 'FinDeMois',
  finRecurrence: null,
  fuseauHoraire: 'Europe/Paris',
  version: 3,
  creeeLe: '2026-10-01T08:00:00Z',
  modifieeLe: '2026-10-01T08:00:00Z',
  ...p,
});

const page = (p: Partial<PageCalendrier> = {}): PageCalendrier =>
  ({
    du: '2026-11-02',
    au: '2027-11-02',
    fuseauHoraire: 'Europe/Paris',
    meps: [],
    gels: [],
    suite: null,
    ...p,
  }) as never;

function rendre(estAdmin = true) {
  render(
    <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
      <TooltipProvider>
        <PeriodesGelPage />
      </TooltipProvider>
    </AuthContext.Provider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.calendrier).mockResolvedValue(page());
});

describe('périodes de gel : textes et saisie', () => {
  it('décrit chaque récurrence dans le fuseau de la période', () => {
    expect(decrireQuand(periode())).toBe('Chaque fin de mois, comme du 26/11 00:00 au 01/12 00:00');
    expect(decrireQuand(periode({ recurrence: 'Aucune' }))).toBe('26/11/2026 00:00 → 01/12/2026 00:00');
    expect(decrireQuand(periode({ recurrence: 'Hebdomadaire', finRecurrence: '2027-06-30' }))).toBe(
      'Chaque semaine, du jeudi 00:00 au mardi 00:00, jusqu’au 30/06/2027',
    );
  });

  it('envoie les heures saisies dans le fuseau, sans fin de récurrence pour une période ponctuelle', () => {
    const b = {
      nom: '  Fin d’année ',
      motif: 'Congés',
      recurrence: 'Aucune' as const,
      debut: '2026-12-18T18:00',
      fin: '2027-01-04T08:00',
      finRecurrence: '2027-12-31',
      tousSystemes: true,
      systemes: ['s1'],
      environnements: ['Préproduction'],
      productionSeulement: false,
    };
    expect(saisieDe(b, 'Europe/Paris', 4)).toEqual({
      nom: 'Fin d’année',
      motif: 'Congés',
      debut: '2026-12-18T17:00:00.000Z',
      fin: '2027-01-04T07:00:00.000Z',
      recurrence: 'Aucune',
      finRecurrence: null,
      systemeCatalogueIds: [],
      environnements: ['Préproduction'],
      productionSeulement: false,
      version: 4,
    });
  });

  it('retient la prochaine occurrence non passée et ses MEP', () => {
    const o = (debut: string, fin: string) => ({ ...periode(), periodeGelId: 'g1', globale: true, debut, fin });
    const p = page({
      gels: [o('2026-10-25T23:00:00Z', '2026-10-31T23:00:00Z'), o('2026-11-25T23:00:00Z', '2026-11-30T23:00:00Z')],
      meps: [{ id: 'm', gels: [o('2026-11-25T23:00:00Z', '2026-11-30T23:00:00Z')] }] as never,
    });
    const x = prochaines(p, Date.parse('2026-11-02T00:00:00Z')).get('g1');
    expect(x?.occurrence.debut).toBe('2026-11-25T23:00:00Z');
    expect(x?.meps).toBe(1);
  });
});

describe('PeriodesGelPage', () => {
  it('liste les périodes, avec la prochaine occurrence', async () => {
    vi.mocked(api.periodesGel).mockResolvedValue([periode()]);
    rendre();
    const tableau = await screen.findByRole('table');
    expect(within(tableau).getByText('Clôture comptable')).toBeInTheDocument();
    expect(within(tableau).getByText('Récurrente')).toBeInTheDocument();
    expect(within(tableau).getByText('Production')).toBeInTheDocument();
    expect(within(tableau).getByText('Aucune dans l’année')).toBeInTheDocument();
  });

  it('propose d’en créer une quand il n’y en a pas', async () => {
    vi.mocked(api.periodesGel).mockResolvedValue([]);
    rendre();
    expect(await screen.findByText('Aucune période de gel')).toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: 'Nouvelle période' })[0]);
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('est en lecture seule pour un non-administrateur', async () => {
    vi.mocked(api.periodesGel).mockResolvedValue([periode()]);
    rendre(false);
    await screen.findByRole('table');
    expect(screen.getByText('Lecture seule.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Modifier/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nouvelle période' })).not.toBeInTheDocument();
  });

  it('dit quand le chargement échoue', async () => {
    vi.mocked(api.periodesGel).mockRejectedValue(new Error('Serveur injoignable'));
    rendre();
    expect(await screen.findByText('Impossible de charger les périodes de gel')).toBeInTheDocument();
  });
});
