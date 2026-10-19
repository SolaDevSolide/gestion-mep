import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../../api';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { PresenceProvider } from '../../presence';
import { etape, mep } from '../../test/fabrique';
import type { Etape, Evenement, Mep, Presence } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import VueEnsembleTab from './VueEnsembleTab';

vi.mock('../../api', async (importOriginal) => ({
  ApiError: (await importOriginal<typeof moduleApi>()).ApiError,
  api: {
    catalogueSystemes: vi.fn(async () => []),
    resolutions: vi.fn(async () => []),
    journal: vi.fn(async () => []),
  },
}));
// Le graphe (React Flow) a sa propre couverture : ici, ce qu'il reçoit et ce que la vue place autour de lui.
vi.mock('../graphe/GrapheLibre', () => ({
  GrapheLibre: (p: {
    plan: string;
    etapes: unknown[];
    plein: boolean;
    onPlein: (v: boolean) => void;
    enTete: ReactNode;
    bandeau?: ReactNode;
    lateral: ReactNode;
  }) => (
    <div>
      <p>
        Graphe du plan {p.plan} : {p.etapes.length} étapes{p.plein ? ', plein écran' : ''}
      </p>
      <button type="button" onClick={() => p.onPlein(!p.plein)}>
        Plein écran
      </button>
      {p.enTete}
      {p.bandeau}
      {p.lateral}
    </div>
  ),
}));

const GO = Date.parse('2026-10-16T13:00:00Z');
const min = (n: number) => new Date(GO + n * 60_000).toISOString();

/**
 * Même plan que calculs.test.ts : A (20 min, réussie) puis, en parallèle, B (30 min, en cours depuis 20, Claire) et
 * C (15 min, en échec, Bob) ; D après C, E après B et D. Il est GO + 40 min.
 */
function plan(): Etape[] {
  return [
    etape({
      id: 'a',
      code: 'E0001',
      action: 'Sauvegarde',
      dureeEstimeeMinutes: 20,
      etat: 'Reussie',
      debutReel: min(0),
      finReelle: min(20),
    }),
    etape({
      id: 'b',
      code: 'E0002',
      action: 'Migration du schéma',
      dureeEstimeeMinutes: 30,
      etat: 'EnCours',
      debutReel: min(20),
      dependances: ['a'],
      executantId: 'p1',
    }),
    etape({
      id: 'c',
      code: 'E0003',
      action: 'Déploiement de l’API',
      dureeEstimeeMinutes: 15,
      etat: 'Echec',
      debutReel: min(20),
      finReelle: min(30),
      preuve: 'HTTP 502 au démarrage',
      dependances: ['a'],
      executantId: 'p2',
    }),
    etape({ id: 'd', code: 'E0004', action: 'Purge du cache', dureeEstimeeMinutes: 10, dependances: ['c'] }),
    etape({ id: 'e', code: 'E0005', action: 'Contrôle final', dureeEstimeeMinutes: 10, dependances: ['b', 'd'] }),
  ];
}

const lancee = (p: Partial<Mep> = {}) =>
  mep({ phase: 'Execution', etapes: plan(), demarrageAutorise: { par: 'Alice', le: min(0) }, ...p } as Partial<Mep>);

const evenement = (id: number, type: string, detail: string, quand = min(30)): Evenement => ({
  id,
  quand,
  auteur: 'Bob',
  type,
  detail,
  auteurLogin: 'bob',
});

function rendre(m: Mep, autres: Presence[] = []) {
  render(
    <MemoryRouter initialEntries={['/meps/m1?onglet=ensemble']}>
      <TooltipProvider>
        <PresenceProvider value={{ autres, declarer: vi.fn() }}>
          <MepContext.Provider value={{ mep: m, run: vi.fn() }}>
            <VueEnsembleTab />
          </MepContext.Provider>
        </PresenceProvider>
      </TooltipProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  // Seule l'horloge est simulée : les minuteries restent réelles pour userEvent.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(GO + 40 * 60_000);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('VueEnsembleTab : vue d’ensemble d’une MEP lancée (#61)', () => {
  it('chiffres clés : étapes réussies, temps passé, échecs et étapes bloquées', async () => {
    rendre(lancee());
    const cles = await screen.findByRole('region', { name: 'Chiffres clés' });
    expect(cles).toHaveTextContent('1/5 étape réussie · 20 %');
    expect(cles).toHaveTextContent('Temps passé40 min');
    expect(cles).toHaveTextContent('1 h prévues');
    expect(cles).toHaveTextContent('Fin estimée');
    expect(cles).toHaveTextContent('1 échec');
    expect(cles).toHaveTextContent('2 étapes bloquées');
    expect(screen.getByText('Graphe du plan Deploiement : 5 étapes')).toBeInTheDocument();
    expect(screen.getByText(/^En direct · /)).toBeInTheDocument();
  });

  it('légende du plan coloré : le vocabulaire des états (« Réussie »)', async () => {
    rendre(lancee());
    const legende = await screen.findByRole('list', { name: 'Légende' });
    expect(
      within(legende)
        .getAllByRole('listitem')
        .map((l) => l.textContent),
    ).toEqual(['Réussie', 'En cours', 'Échec', 'Résolution en cours', 'À venir', 'Bloquée']);
  });

  it('blocages et échecs : compteur sur fond plein lisible, preuve, exécutant, étapes bloquées', async () => {
    rendre(lancee());
    const blocages = await screen.findByRole('region', { name: 'Blocages et échecs' });
    const compteur = within(blocages).getByText('1');
    // Texte du compteur : le jeton apparié au fond plein, jamais un blanc écrit en dur.
    expect(compteur).toHaveClass('bg-danger-solid', 'text-danger-solid-fg');
    expect(blocages).toHaveTextContent('E0003');
    expect(blocages).toHaveTextContent('Déploiement de l’API');
    expect(blocages).toHaveTextContent('HTTP 502 au démarrage');
    expect(blocages).toHaveTextContent('Bob');
    expect(blocages).toHaveTextContent('Bloque 2 étapes : E0004, E0005');
  });

  it('rien de bloquant : compteur neutre', async () => {
    rendre(lancee({ etapes: plan().filter((e) => e.id === 'a' || e.id === 'b') }));
    const blocages = await screen.findByRole('region', { name: 'Blocages et échecs' });
    expect(blocages).toHaveTextContent('Rien de bloquant.');
    expect(within(blocages).getByText('0')).toHaveClass('bg-surface-2', 'text-fg-muted');
  });

  it('une étape qui dépasse sa durée est signalée', async () => {
    vi.setSystemTime(GO + 55 * 60_000);
    rendre(lancee({ etapes: plan().filter((e) => e.id === 'a' || e.id === 'b') }));
    const blocages = await screen.findByRole('region', { name: 'Blocages et échecs' });
    expect(blocages).toHaveTextContent('Dépasse sa durée');
    expect(blocages).toHaveTextContent('Claire · 35 min pour 30 prévues');
  });

  it('en ce moment : qui exécute quoi, le pilote, et les visiteurs avec l’endroit où ils sont', async () => {
    const visiteur: Presence = { utilisateurId: 'u9', nom: 'Dave', elements: [] } as unknown as Presence;
    rendre(lancee(), [visiteur]);
    const moment = await screen.findByRole('region', { name: 'En ce moment' });
    const lignes = within(moment).getAllByRole('listitem');
    const claire = lignes.find((l) => l.textContent?.includes('Claire'))!;
    expect(claire).toHaveTextContent('E0002 Migration du schéma · 20 min');
    const alice = lignes.find((l) => l.textContent?.includes('Alice'))!;
    expect(alice).toHaveTextContent('Pilote');
    expect(moment).toHaveTextContent('Dave · consulte la MEP');
  });

  it('événements marquants seulement, les plus récents d’abord ; lien vers tout le journal', async () => {
    vi.mocked(api.journal).mockResolvedValue([
      evenement(3, 'etape.echec', 'E0003 en échec : HTTP 502'),
      evenement(2, 'etape.commentaire', 'Commentaire ajouté sur E0002'),
      evenement(1, 'etape.reussie', 'E0001 réussie', min(20)),
    ]);
    rendre(lancee());
    const evenements = await screen.findByRole('region', { name: 'Événements marquants' });
    expect(await within(evenements).findByText('E0003 en échec : HTTP 502')).toBeInTheDocument();
    const items = within(evenements).getAllByRole('listitem');
    expect(items.map((i) => i.lastChild?.textContent)).toEqual(['E0003 en échec : HTTP 502', 'E0001 réussie']);
    expect(within(evenements).queryByText('Commentaire ajouté sur E0002')).toBeNull();
    expect(within(evenements).getByRole('link', { name: 'Tout le journal' })).toHaveAttribute(
      'href',
      '/meps/m1?onglet=journal',
    );
  });

  it('journal non chargé : erreur avec « Réessayer », le reste de la vue s’affiche', async () => {
    vi.mocked(api.journal).mockRejectedValue(new Error('Serveur injoignable'));
    rendre(lancee());
    const evenements = await screen.findByRole('region', { name: 'Événements marquants' });
    expect(await within(evenements).findByText(/^Derniers événements non chargés/)).toBeInTheDocument();
    expect(within(evenements).getByRole('button', { name: /Réessayer/ })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Chiffres clés' })).toBeInTheDocument();
  });

  it('plein écran : les chiffres clés passent dans le bandeau du graphe, avec la phase', async () => {
    rendre(lancee());
    await screen.findByRole('region', { name: 'Chiffres clés' });
    await userEvent.click(screen.getByRole('button', { name: 'Plein écran' }));
    expect(screen.getByText('Graphe du plan Deploiement : 5 étapes, plein écran')).toBeInTheDocument();
    expect(screen.getAllByRole('region', { name: 'Chiffres clés' })).toHaveLength(1);
    expect(screen.getByText('Portail')).toBeInTheDocument();
  });
});

describe('VueEnsembleTab : avant le Go, retour arrière, clôture', () => {
  it('en préparation : la vue s’animera au Go, pas d’estimation', async () => {
    rendre(
      mep({
        phase: 'Preparation',
        etapes: plan().map((e) => Object.assign(e, { etat: 'AFaire', debutReel: null, finReelle: null })),
      }),
    );
    expect(await screen.findByText(/MEP en préparation : la vue d’ensemble s’anime au Go/)).toBeInTheDocument();
    const cles = screen.getByRole('region', { name: 'Chiffres clés' });
    expect(cles).toHaveTextContent('0/5 étape réussie · 0 %');
    expect(cles).toHaveTextContent('avant le Go');
    expect(cles).toHaveTextContent('pas d’estimation');
    expect(screen.queryByText(/^En direct · /)).toBeNull();
  });

  it('retour arrière : la vue suit le plan de retour arrière, avec son motif (sans double point)', async () => {
    const retour = etape({ id: 'r1', code: 'R0001', plan: 'RetourArriere', action: 'Restaurer la sauvegarde' });
    rendre(
      lancee({
        phase: 'RetourArriere',
        retourArriere: { le: min(35), par: 'Alice', motif: 'API indisponible.' },
        etapes: [...plan(), retour],
      }),
    );
    expect(
      await screen.findByText('Retour arrière : API indisponible. La vue suit le plan de retour arrière.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Graphe du plan RetourArriere : 1 étapes')).toBeInTheDocument();
  });

  it('clôturée : renvoi vers le rapport de fin', async () => {
    rendre(
      lancee({
        phase: 'Cloturee',
        issue: 'Reussie',
        etapes: plan().map((e) => Object.assign(e, { etat: 'Reussie', finReelle: e.finReelle ?? min(60) })),
      }),
    );
    expect(await screen.findByRole('link', { name: 'rapport de fin' })).toHaveAttribute('href', '/meps/m1/rapport');
    const cles = screen.getByRole('region', { name: 'Chiffres clés' });
    expect(cles).toHaveTextContent('5/5 étapes réussies · 100 %');
    expect(cles).toHaveTextContent('Fin réelle');
    expect(screen.queryByText(/^En direct · /)).toBeNull();
  });
});
