import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../../api';
import type * as moduleApi from '../../api';
import { FILTRE_VIDE } from '../../rechercheMeps';
import type { ElementCalendrier, OccurrenceGel, PageCalendrier } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { CalendrierMeps } from './CalendrierMeps';

vi.mock('../../api', async (importOriginal) => ({
  ApiError: (await importOriginal<typeof moduleApi>()).ApiError,
  api: { calendrier: vi.fn(), abonnementCalendrier: vi.fn(async () => ({ actif: false, url: null })) },
}));

const gel: OccurrenceGel = {
  periodeGelId: 'g1',
  nom: 'Clôture comptable',
  motif: '',
  debut: '2026-11-25T23:00:00Z',
  fin: '2026-11-30T23:00:00Z',
  fuseauHoraire: 'Europe/Paris',
  globale: true,
  derogation: null,
};

const element = (p: Partial<ElementCalendrier> = {}): ElementCalendrier => ({
  id: 'm1',
  outil: 'Facturation',
  versionCible: '4.2.0',
  objet: '',
  type: 'Complete',
  phase: 'Preparation',
  etat: 'Preparation',
  debut: '2026-11-27T20:00:00Z',
  fin: '2026-11-27T22:30:00Z',
  journeeEntiere: false,
  prevu: true,
  enCours: false,
  fuseauHoraire: 'Europe/Paris',
  pilote: 'Alice Martin',
  toucheProduction: true,
  systemes: [],
  equipeIds: [],
  gels: [gel],
  conflits: [],
  ...p,
});

const page = (p: Partial<PageCalendrier> = {}): PageCalendrier => ({
  meps: [element()],
  gels: [gel],
  total: 1,
  suivant: null,
  du: '2026-10-26',
  au: '2026-12-06',
  fuseauHoraire: 'Europe/Paris',
  ...p,
});

function rendre(vue: 'mois' | 'semaine' | 'liste', changer = vi.fn()) {
  render(
    <MemoryRouter>
      <TooltipProvider>
        <CalendrierMeps f={FILTRE_VIDE} vue={vue} ancre="2026-11-27" jourChoisi="2026-11-27" changer={changer} />
      </TooltipProvider>
    </MemoryRouter>,
  );
  return changer;
}

beforeEach(() => vi.clearAllMocks());

describe('CalendrierMeps', () => {
  it('lit le mois affiché et montre la MEP et le gel du jour choisi', async () => {
    vi.mocked(api.calendrier).mockResolvedValue(page());
    rendre('mois');
    expect(screen.getByRole('heading', { name: 'Novembre 2026' })).toBeInTheDocument();
    expect(await screen.findAllByRole('link', { name: /Facturation 4\.2\.0/ })).not.toHaveLength(0);
    const params = vi.mocked(api.calendrier).mock.calls[0][0];
    expect(params.get('du')).toBe('2026-10-26');
    expect(params.get('au')).toBe('2026-12-06');
    expect(screen.getAllByText(/Clôture comptable/).length).toBeGreaterThan(0);
  });

  it('passe d’un mois à l’autre et change de vue', async () => {
    vi.mocked(api.calendrier).mockResolvedValue(page());
    const changer = rendre('mois');
    await screen.findAllByRole('link', { name: /Facturation/ });
    await userEvent.click(screen.getByRole('button', { name: 'Période suivante' }));
    expect(changer).toHaveBeenCalledWith({ ancre: '2026-12-01' });
    await userEvent.click(within(screen.getByRole('radiogroup', { name: 'Vue du calendrier' })).getByText('Semaine'));
    expect(changer).toHaveBeenCalledWith({ vue: 'semaine' });
  });

  it('liste les MEP avec leur horaire en vue liste', async () => {
    vi.mocked(api.calendrier).mockResolvedValue(page());
    rendre('liste');
    expect(await screen.findByText('21:00 → 23:30')).toBeInTheDocument();
  });

  it('dit qu’aucune MEP ne correspond', async () => {
    vi.mocked(api.calendrier).mockResolvedValue(page({ meps: [], gels: [], total: 0 }));
    rendre('semaine');
    expect(await screen.findByText('Aucune MEP ne correspond aux filtres sur cette période.')).toBeInTheDocument();
  });

  it('dit quand le calendrier ne se charge pas', async () => {
    vi.mocked(api.calendrier).mockRejectedValue(new Error('Serveur injoignable'));
    rendre('mois');
    expect(await screen.findByText('Impossible de charger le calendrier')).toBeInTheDocument();
  });
});
