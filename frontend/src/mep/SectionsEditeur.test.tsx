import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import { simulerReactFlow } from '../test/reactFlow';
import type { Etape, Mep, Verrou } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EtapesEditeur } from './EtapesEditeur';
import { ExecutionVue } from './ExecutionVue';
import { apiGraphe, apiVerrou } from './graphe/apiGraphe';
import { changerModeEspace } from './graphe/EspaceEtape';
import type { Section } from './graphe/sections';
import { VerrousContext } from './graphe/verrouEtape';

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(async () => []),
    resolutions: vi.fn(async () => []),
    etapesModeles: vi.fn(async () => []),
    modifierEtape: vi.fn(async () => ({})),
  },
}));

vi.mock('./graphe/apiGraphe', () => ({
  apiGraphe: {
    creerEtape: vi.fn(async () => ({ etapes: [] })),
    placerSection: vi.fn(async () => ({})),
    creerSection: vi.fn(async () => ({ sectionId: 'nouvelle', mep: {} })),
    modifierSection: vi.fn(async () => ({})),
    supprimerSection: vi.fn(async () => ({})),
    creerCercle: vi.fn(async () => ({})),
    modifierCercle: vi.fn(async () => ({})),
    supprimerCercle: vi.fn(async () => ({})),
    lier: vi.fn(async () => ({})),
    delier: vi.fn(async () => ({})),
  },
  apiVerrou: { prendre: vi.fn(), relacher: vi.fn(async () => undefined), liste: vi.fn(async () => []) },
}));

beforeAll(() => simulerReactFlow());

const droits = {
  estPilote: true,
  estApprobateur: false,
  estResponsableTechnique: false,
  peutModifierPlan: true,
  peutEditerContenu: true,
  participantId: 'p3',
};

const section = (p: Partial<Section> & { id: string; titre: string }): Section => ({
  rang: 0,
  plan: 'Deploiement',
  description: '',
  systemeIds: ['s1'],
  auteurId: 'u-bob',
  auteur: 'Bob',
  creeLe: '2026-10-15T08:00:00Z',
  placee: true,
  revision: 1,
  ports: [],
  ...p,
});

const dansSection = (e: Etape, sectionId: string, derivees: string[] = []) =>
  ({ ...e, sectionId, portsAttendus: [], dependancesDerivees: derivees }) as Etape;

function planAvecSections(p: Partial<Mep> = {}): Mep {
  const bascule = section({
    id: 'sb',
    titre: 'Bascule BDD',
    ports: [
      { id: 'in', sens: 'Entree', titre: 'Début', description: '', ordre: 0, etapes: ['e2'], sources: ['e1'] },
      { id: 'out', sens: 'Sortie', titre: 'Fin', description: '', ordre: 0, etapes: ['e3'], sources: [] },
    ],
  });
  const bac = section({ id: 'sx', titre: 'Purge des caches', placee: false });
  return {
    ...mep({
      phase: 'Preparation',
      droits,
      etapes: [
        etape({ id: 'e1', code: 'E0001', action: 'Sauvegarde', ligne: 0 }),
        dansSection(etape({ id: 'e2', code: 'E0002', action: 'Arrêt', ligne: 1, dependances: ['e1'] }), 'sb', ['e1']),
        dansSection(etape({ id: 'e3', code: 'E0003', action: 'Bascule', ligne: 2, dependances: ['e2'] }), 'sb'),
        dansSection(etape({ id: 'e4', code: 'E0004', action: 'Purge CDN', ligne: 0, ordre: 1 }), 'sx'),
      ],
      ...p,
    }),
    sections: [bascule, bac],
  } as Mep;
}

function rendre(
  m: Mep,
  adresse = '/meps/m1',
  verrous: Verrou[] | null = null,
  Vue = () => <EtapesEditeur plan="Deploiement" />,
) {
  const run = vi.fn(async (action: () => Promise<unknown>) => {
    await action();
    return true;
  });
  const arbre = (mm: Mep, vv: Verrou[] | null) => (
    <MemoryRouter initialEntries={[adresse]}>
      <TooltipProvider>
        <VerrousContext.Provider value={{ session: 'onglet-a', verrous: vv }}>
          <MepContext.Provider value={{ mep: mm, run }}>
            <Routes>
              <Route path="/meps/:id" element={<Vue />} />
            </Routes>
          </MepContext.Provider>
        </VerrousContext.Provider>
      </TooltipProvider>
    </MemoryRouter>
  );
  const r = render(arbre(m, verrous));
  return { run, rerendre: (mm: Mep, vv: Verrou[] | null) => r.rerender(arbre(mm, vv)) };
}

const verrou = (session: string, nom: string, etapeId = 'e1'): Verrou => ({
  etapeId,
  utilisateurId: `u-${nom}`,
  nom,
  session,
  prisLe: '',
  expireLe: '',
  reprisDeUtilisateurId: null,
  reprisDeNom: null,
  reprisDeSession: null,
});

describe('Sections du plan', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    changerModeEspace('plein');
    vi.mocked(apiVerrou.prendre).mockImplementation(async (_m, etapeId, session) => ({
      pris: true,
      verrou: verrou(session, 'Moi', etapeId),
    }));
  });

  it('section d’un autre repliée par défaut : titre, systèmes, nombre d’étapes, auteur, cercles ; un clic la déplie', async () => {
    rendre(planAvecSections());
    const cadre = await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' });
    expect(cadre).toHaveTextContent('Bascule BDD');
    expect(cadre).toHaveTextContent('2 étapes · par Bob');
    expect(within(cadre).getByText('Début')).toBeInTheDocument();
    expect(within(cadre).getByText('Fin')).toBeInTheDocument();
    // Étapes de la section repliée : pas de carte dans le plan ; la section au bac non plus.
    expect(screen.queryByRole('button', { name: /^E0002/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /^E0004/ })).toBeNull();
    expect(await screen.findByRole('group', { name: 'Lien E0001 vers section Bascule BDD' })).toBeInTheDocument();

    await userEvent.click(within(cadre).getByRole('button', { name: 'Déplier la section Bascule BDD' }));
    expect(await screen.findByRole('group', { name: 'Section Bascule BDD, dépliée' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^E0002 Arrêt/ })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Entrée Début' })).toBeInTheDocument();
    expect(JSON.parse(window.localStorage.getItem('mep.m1.sections.depliees') ?? '{}')).toEqual({ sb: true });
  });

  it('« Tout déplier / Tout replier »', async () => {
    rendre(planAvecSections());
    await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' });
    await userEvent.click(screen.getByRole('button', { name: 'Tout déplier' }));
    expect(await screen.findByRole('group', { name: 'Section Bascule BDD, dépliée' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Tout replier' }));
    expect(await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' })).toBeInTheDocument();
  });

  it('bac : sections pas encore placées, « Placer dans le graphe », « Nouvelle section »', async () => {
    rendre(planAvecSections());
    const bac = screen.getByRole('complementary', { name: 'Bac des sections' });
    expect(bac).toHaveTextContent('Sections à placer (1)');
    expect(bac).toHaveTextContent('Purge des caches');
    expect(bac).toHaveTextContent('1 étape · par Bob');
    await userEvent.click(within(bac).getByRole('button', { name: 'Placer dans le graphe' }));
    expect(apiGraphe.placerSection).toHaveBeenCalledWith('m1', 'sx', true);

    await userEvent.click(within(bac).getByRole('button', { name: 'Nouvelle section' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouvelle section' });
    await userEvent.type(within(dialogue).getByLabelText('Titre'), 'Migration');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Créer la section' }));
    expect(apiGraphe.creerSection).toHaveBeenCalledWith('m1', 'Deploiement', 'Migration', [], false);
  });

  it('vue d’édition d’une section : fil d’Ariane, titre, cercles, ses seules étapes ; création dans la section', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb');
    const fil = screen.getByRole('navigation', { name: 'Fil d’Ariane' });
    expect(fil).toHaveTextContent('Portail');
    expect(fil).toHaveTextContent('Plan · Étapes');
    expect(fil).toHaveTextContent('Bascule BDD');
    expect(screen.getByLabelText('Titre de la section')).toHaveValue('Bascule BDD');
    expect(await screen.findByRole('button', { name: /^E0002 Arrêt/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^E0001/ })).toBeNull();
    expect(screen.queryByRole('complementary', { name: 'Bac des sections' })).toBeNull();

    // Les cercles se nomment d'un clic ; « + » en ajoute.
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une entrée' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouvelle entrée' });
    await userEvent.type(within(dialogue).getByLabelText('Titre'), 'Validation');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(apiGraphe.creerCercle).toHaveBeenCalledWith('m1', 'sb', 'Entree', 'Validation', '');

    await userEvent.click(screen.getByRole('button', { name: /^Étape$/ }));
    expect(apiGraphe.creerEtape).toHaveBeenCalledWith('m1', 'Deploiement', {}, 'sb');

    // Le titre s'enregistre en quittant le champ.
    const titre = screen.getByLabelText('Titre de la section');
    await userEvent.clear(titre);
    await userEvent.type(titre, 'Bascule base{Enter}');
    expect(apiGraphe.modifierSection).toHaveBeenCalledWith(
      'm1',
      'sb',
      expect.objectContaining({ revision: 1, titre: 'Bascule base' }),
    );
  });

  it('vue d’une section : remettre dans le bac, supprimer (ses étapes restent)', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb');
    await userEvent.click(screen.getByRole('button', { name: 'Remettre dans le bac' }));
    expect(apiGraphe.placerSection).toHaveBeenCalledWith('m1', 'sb', false);
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer la section' }));
    const confirmation = await screen.findByRole('dialog', { name: /Supprimer la section/ });
    expect(confirmation).toHaveTextContent('Ses étapes restent dans le plan');
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Supprimer la section' }));
    await waitFor(() => expect(apiGraphe.supprimerSection).toHaveBeenCalledWith('m1', 'sb'));
  });

  it('verrou : « Alice est dessus », lecture seule, « Prendre la main »', async () => {
    vi.mocked(apiVerrou.prendre).mockImplementation(async (_m, etapeId, session, prendreLaMain) =>
      prendreLaMain
        ? { pris: true, verrou: verrou(session, 'Moi', etapeId) }
        : { pris: false, verrou: verrou('onglet-alice', 'Alice Martin', etapeId) },
    );
    rendre(planAvecSections());
    fireEvent.click(await screen.findByRole('button', { name: /^E0001/ }));
    expect(await screen.findByText(/Alice est dessus : lecture seule/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Action')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Prendre la main' }));
    expect(apiVerrou.prendre).toHaveBeenLastCalledWith('m1', 'e1', 'onglet-a', true);
    expect(await screen.findByLabelText('Action')).toHaveValue('Sauvegarde');
  });

  it('verrou : « Bob a pris la main » pendant la saisie, qui reste affichée pour être copiée', async () => {
    const m = planAvecSections();
    const { rerendre } = rendre(m);
    fireEvent.click(await screen.findByRole('button', { name: /^E0001/ }));
    await waitFor(() => expect(apiVerrou.prendre).toHaveBeenCalledWith('m1', 'e1', 'onglet-a', false));
    await userEvent.type(screen.getByLabelText('Action'), ' complète');
    await act(async () => rerendre(m, [verrou('onglet-bob', 'Bob')]));
    expect(await screen.findByText(/Bob a pris la main/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Action')).toBeNull();
    const brouillon = screen.getByRole('region', { name: 'Votre saisie non enregistrée' });
    expect(brouillon).toHaveTextContent('Sauvegarde complète');
  });
});

describe('Sections en exécution', () => {
  it('section repliée avec son avancement « 1/2, 1 en échec » ; un clic la déplie', async () => {
    const m = planAvecSections({ phase: 'Execution', droits: { ...droits, peutEditerContenu: false } });
    m.etapes = m.etapes.map((e) =>
      e.id === 'e2' ? { ...e, etat: 'Reussie' } : e.id === 'e3' ? { ...e, etat: 'Echec' } : e,
    );
    rendre(m, '/meps/m1', null, () => <ExecutionVue plan="Deploiement" />);
    const cadre = await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' });
    expect(cadre).toHaveTextContent('1/2, 1 en échec');
    await userEvent.click(within(cadre).getByRole('button', { name: 'Déplier la section Bascule BDD' }));
    expect(await screen.findByRole('button', { name: /^E0003/ })).toBeInTheDocument();
  });
});
