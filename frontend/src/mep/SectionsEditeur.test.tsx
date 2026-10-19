import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import { simulerReactFlow } from '../test/reactFlow';
import { PresenceProvider } from '../presence';
import type { Etape, Mep, Presence, Verrou } from '../types';
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
    placerEtape: vi.fn(async () => ({})),
    retirerDuPlan: vi.fn(async () => ({})),
    creerSection: vi.fn(async () => ({ sectionId: 'nouvelle', mep: {} })),
    modifierSection: vi.fn(async () => ({})),
    supprimerSection: vi.fn(async () => ({})),
    creerCercle: vi.fn(async () => ({})),
    modifierCercle: vi.fn(async () => ({})),
    supprimerCercle: vi.fn(async () => ({})),
    lier: vi.fn(async () => ({})),
    delier: vi.fn(async () => ({})),
    liensRedondants: vi.fn(async () => []),
    optimiserLiens: vi.fn(async () => ({})),
  },
  apiVerrou: { prendre: vi.fn(), relacher: vi.fn(async () => undefined), liste: vi.fn(async () => []) },
}));

beforeAll(() => simulerReactFlow());

const droits = {
  ...mep().droits,
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

const declarer = vi.fn();

function rendre(
  m: Mep,
  adresse = '/meps/m1',
  verrous: Verrou[] | null = null,
  Vue = () => <EtapesEditeur plan="Deploiement" />,
  autres: Presence[] = [],
) {
  const run = vi.fn(async (action: () => Promise<unknown>) => {
    await action();
    return true;
  });
  const arbre = (mm: Mep, vv: Verrou[] | null) => (
    <MemoryRouter initialEntries={[adresse]}>
      <TooltipProvider>
        <PresenceProvider value={{ autres, declarer }}>
          <VerrousContext.Provider value={{ session: 'onglet-a', verrous: vv }}>
            <MepContext.Provider value={{ mep: mm, run }}>
              <Routes>
                <Route path="/meps/:id" element={<Vue />} />
              </Routes>
            </MepContext.Provider>
          </VerrousContext.Provider>
        </PresenceProvider>
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
    const bac = screen.getByRole('complementary', { name: 'Bac des éléments à placer' });
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

  it('bac : étapes à placer hors du graphe, « Placer à part », « Retirer du plan » sur la fiche', async () => {
    const m = planAvecSections();
    rendre({
      ...m,
      etapes: [...m.etapes, etape({ id: 'e5', code: 'E0005', action: 'Vider le cache', ligne: 3, aPlacer: true })],
    });
    const bac = screen.getByRole('complementary', { name: 'Bac des éléments à placer' });
    const liste = within(bac).getByRole('list', { name: 'Étapes à placer' });
    expect(liste).toHaveTextContent('E0005');
    expect(liste).toHaveTextContent('Vider le cache');
    // Pas de carte dans le graphe tant qu'elle est au bac.
    await screen.findByRole('button', { name: /^E0001 Sauvegarde/ });
    expect(screen.queryByRole('button', { name: /^E0005/ })).toBeNull();
    await userEvent.click(within(liste).getByRole('button', { name: 'Placer E0005 à part dans le graphe' }));
    expect(apiGraphe.placerEtape).toHaveBeenCalledWith('m1', 'e5', {}, null);

    // « Retirer du plan » dans l'édition d'une étape placée.
    fireEvent.click(screen.getByRole('button', { name: /^E0001 Sauvegarde/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Retirer du plan' }));
    expect(apiGraphe.retirerDuPlan).toHaveBeenCalledWith('m1', 'e1');
  });

  it('« Retirer du plan » sur une section : elle retourne dans le bac', async () => {
    rendre(planAvecSections());
    await userEvent.click(await screen.findByRole('button', { name: 'Retirer la section Bascule BDD du plan' }));
    expect(apiGraphe.placerSection).toHaveBeenCalledWith('m1', 'sb', false);
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
    // Le bac de la vue d'une section ne liste que ses étapes à placer, pas les sections.
    const bacSection = screen.getByRole('complementary', { name: 'Bac des éléments à placer' });
    expect(within(bacSection).queryByRole('list', { name: 'Sections à placer' })).toBeNull();

    // Les cercles se nomment d'un clic ; « + » en ajoute.
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une entrée' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouvelle entrée' });
    await userEvent.type(within(dialogue).getByLabelText('Titre'), 'Validation');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(apiGraphe.creerCercle).toHaveBeenCalledWith('m1', 'sb', 'Entree', 'Validation', '');

    await userEvent.click(screen.getByRole('button', { name: /^Étape$/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Étape vierge' }));
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

  it('dans le plan, l’intérieur d’une section dépliée se lit seulement ; on le modifie dans la vue de la section', async () => {
    window.localStorage.setItem('mep.m1.sections.depliees', JSON.stringify({ sb: true }));
    rendre(planAvecSections());
    expect(await screen.findByRole('group', { name: 'Section Bascule BDD, dépliée' })).toBeInTheDocument();
    // Ni « + » sur les cartes de la section, ni poignée active ; les cartes du plan gardent les leurs.
    expect(screen.getByRole('button', { name: 'Ajouter une étape après E0001' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ajouter une étape après E0002' })).toBeNull();
    const carte = screen.getByRole('button', { name: /^E0002 Arrêt/ });
    const noeud = carte.closest('.react-flow__node') as HTMLElement;
    expect(noeud.querySelector('.react-flow__handle.connectable')).toBeNull();
    // Le cercle d'entrée reste reliable depuis le plan, par son côté extérieur seulement.
    const entree = screen.getByRole('img', { name: 'Entrée Début' }).closest('.react-flow__node') as HTMLElement;
    expect(entree.querySelector('.react-flow__handle-top.connectable')).not.toBeNull();
    expect(entree.querySelector('.react-flow__handle-bottom.connectable')).toBeNull();

    // La fiche s'ouvre en lecture, sans verrou ; un bouton mène à la vue de la section.
    fireEvent.click(carte);
    expect(await screen.findByText(/Étape de la section « Bascule BDD »/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Action')).toBeNull();
    expect(apiVerrou.prendre).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Modifier dans la section' }));
    // La même fiche, maintenant modifiable, au-dessus de la vue de la section (masquée aux lecteurs d'écran tant qu'elle
    // est ouverte en plein).
    expect(await screen.findByLabelText('Action')).toHaveValue('Arrêt');
    expect(screen.getByRole('button', { name: 'Retour au plan', hidden: true })).toBeInTheDocument();
  });

  it('vue d’une section dans le cadre du plan (plein écran compris) : « Retour au plan », Échap', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb');
    const bandeau = screen.getByRole('region', { name: 'Section « Bascule BDD »' });
    expect(bandeau.closest('.graphe-libre')).not.toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Plan en plein écran' }));
    const plein = screen.getByRole('region', { name: 'Plan en plein écran' });
    expect(within(plein).getByRole('region', { name: 'Section « Bascule BDD »' })).toBeInTheDocument();
    expect(within(plein).getByRole('navigation', { name: 'Fil d’Ariane' })).toBeInTheDocument();
    // Échap : retour au plan, toujours en plein écran.
    fireEvent.keyDown(document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Section « Bascule BDD »' })).toBeNull());
    expect(screen.getByRole('region', { name: 'Plan en plein écran' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /^E0001/ })).toBeInTheDocument();
    // Échap encore : sortie du plein écran.
    fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Plan en plein écran' })).toBeNull());
  });

  it('« Retour au plan » depuis la vue d’une section', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb');
    await userEvent.click(screen.getByRole('button', { name: 'Retour au plan' }));
    expect(await screen.findByRole('button', { name: /^E0001/ })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Fil d’Ariane' })).toBeNull();
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
  it('présence localisée : avatars sur la section et sur la carte où chacun se trouve, prise de main marquée', async () => {
    const autres: Presence[] = [
      { utilisateurId: 'u-bob', nom: 'Bob', elements: ['ici:section:sb'] },
      { utilisateurId: 'u-claire', nom: 'Claire', elements: ['ici:etape:e1', 'etape:e1'] },
    ];
    const prise = { ...verrou('onglet-claire', 'Claire', 'e1'), utilisateurId: 'u-claire', reprisDeNom: 'Alice' };
    rendre(planAvecSections(), '/meps/m1', [prise], undefined, autres);
    const section = await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' });
    expect(within(section).getByRole('group', { name: 'Bob est dans la section « Bascule BDD »' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Claire a pris la main sur E0001 à Alice' })).toBeInTheDocument();
  });

  it('les étapes ouvertes et la vue d’une section déclarent où l’on est (même en lecture)', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb&etape=e2');
    await screen.findByRole('button', { name: 'Retour au plan', hidden: true });
    await waitFor(() => {
      const cles = declarer.mock.calls.map((c) => c[1]);
      expect(cles).toContain('ici:section:sb');
      expect(cles).toContain('ici:etape:e2');
    });
  });

  it('double-clic sur une section repliée : sa vue d’édition (sans la déplier)', async () => {
    rendre(planAvecSections());
    const section = await screen.findByRole('group', { name: 'Section Bascule BDD, repliée' });
    const noeud = section.closest('.react-flow__node') ?? section;
    fireEvent.click(noeud, { detail: 1 });
    fireEvent.click(noeud, { detail: 2 });
    fireEvent.doubleClick(noeud);
    expect(await screen.findByRole('button', { name: 'Retour au plan' })).toBeInTheDocument();
    expect(window.localStorage.getItem('mep.m1.sections.depliees')).toBeNull();
  });

  it('« Optimiser les liens » : liste des liens inutiles, puis une seule opération à la confirmation', async () => {
    vi.mocked(apiGraphe.liensRedondants).mockResolvedValueOnce([{ de: 'e1', vers: 'e3', libelle: 'E0001 → E0003' }]);
    rendre(planAvecSections());
    await userEvent.click(await screen.findByRole('button', { name: 'Optimiser les liens' }));
    const panneau = await screen.findByRole('region', { name: 'Optimiser les liens' });
    expect(panneau).toHaveTextContent('1 lien inutile');
    expect(within(panneau).getByRole('list', { name: 'Liens qui seront retirés' })).toHaveTextContent('E0001 → E0003');
    await userEvent.click(within(panneau).getByRole('button', { name: 'Confirmer' }));
    expect(apiGraphe.liensRedondants).toHaveBeenLastCalledWith('m1', 'Deploiement', null);
    expect(apiGraphe.optimiserLiens).toHaveBeenCalledWith(
      'm1',
      'Deploiement',
      [{ de: 'e1', vers: 'e3', libelle: 'E0001 → E0003' }],
      null,
    );
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Optimiser les liens' })).toBeNull());
  });

  it('« Optimiser les liens » dans la vue d’une section : seulement les liens de la section', async () => {
    vi.mocked(apiGraphe.liensRedondants).mockResolvedValueOnce([
      { de: 'in', vers: 'e3', libelle: 'Début (Bascule BDD) → E0003' },
    ]);
    rendre(planAvecSections(), '/meps/m1?section=sb');
    expect(await screen.findByRole('button', { name: 'Retour au plan' })).toBeInTheDocument();
    const bouton = screen.getByRole('button', { name: 'Optimiser les liens' });
    expect(bouton).toHaveAttribute('title', expect.stringContaining('cette section'));
    await userEvent.click(bouton);
    expect(apiGraphe.liensRedondants).toHaveBeenLastCalledWith('m1', 'Deploiement', 'sb');
    const panneau = await screen.findByRole('region', { name: 'Optimiser les liens' });
    await userEvent.click(within(panneau).getByRole('button', { name: 'Confirmer' }));
    expect(apiGraphe.optimiserLiens).toHaveBeenLastCalledWith(
      'm1',
      'Deploiement',
      [{ de: 'in', vers: 'e3', libelle: 'Début (Bascule BDD) → E0003' }],
      'sb',
    );
  });

  it('« Optimiser les liens » sans lien inutile : rien à confirmer', async () => {
    rendre(planAvecSections());
    await userEvent.click(await screen.findByRole('button', { name: 'Optimiser les liens' }));
    expect(await screen.findByText(/Aucun lien inutile/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Optimiser les liens' })).toBeNull();
  });
});

describe('Vue compacte : fenêtre de moins de 800 px de haut', () => {
  let largeur = 1366;
  beforeEach(() => {
    largeur = 1366;
    // Portable 1366 × 768 : fenêtre utile d'environ 1366 × 641.
    vi.stubGlobal('matchMedia', (requete: string) => ({
      matches: requete.includes('max-height: 799') || (requete.includes('max-width: 1439') && largeur < 1440),
      media: requete,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }));
    vi.stubGlobal('scrollTo', vi.fn());
    // Espace d'une étape en panneau (préférence par défaut) : en vue compacte, il devient un tiroir.
    changerModeEspace('panneau');
  });
  afterEach(() => vi.unstubAllGlobals());

  it('bandeau de section d’une ligne : « Plan », titre, entrées et sorties ; le reste dans « Détails »', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb');
    const bandeau = screen.getByRole('region', { name: 'Section « Bascule BDD »' });
    expect(within(bandeau).getByRole('button', { name: 'Retour au plan' })).toHaveTextContent('Plan');
    expect(within(bandeau).getByLabelText('Titre de la section')).toHaveValue('Bascule BDD');
    expect(bandeau).toHaveTextContent('1 entrée');
    expect(bandeau).toHaveTextContent('1 sortie');
    // Description, systèmes, cercles, auteur et actions : dans « Détails ».
    expect(screen.queryByLabelText('Description de la section')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remettre dans le bac' })).toBeNull();
    await userEvent.click(within(bandeau).getByRole('button', { name: 'Détails de la section' }));
    const details = await screen.findByRole('dialog');
    expect(within(details).getByLabelText('Description de la section')).toBeInTheDocument();
    expect(details).toHaveTextContent('Créée par Bob');
    expect(within(details).getByRole('button', { name: /Début/ })).toBeInTheDocument();
    await userEvent.click(within(details).getByRole('button', { name: 'Remettre dans le bac' }));
    expect(apiGraphe.placerSection).toHaveBeenCalledWith('m1', 'sb', false);
  });

  it('titre modifié sur place : la saisie reste si la section change entre-temps, Échap l’abandonne', async () => {
    const { rerendre } = rendre(planAvecSections(), '/meps/m1?section=sb');
    const titre = screen.getByLabelText('Titre de la section');
    await userEvent.clear(titre);
    await userEvent.type(titre, 'Bascule base');
    // Un collègue change la description : la saisie en cours n'est pas écrasée.
    const m = planAvecSections();
    m.sections = m.sections.map((x) => (x.id === 'sb' ? { ...x, description: 'Nouvelle', revision: 2 } : x));
    rerendre(m, null);
    expect(screen.getByLabelText('Titre de la section')).toHaveValue('Bascule base');
    await userEvent.type(screen.getByLabelText('Titre de la section'), '{Escape}');
    expect(screen.getByLabelText('Titre de la section')).toHaveValue('Bascule BDD');
    expect(apiGraphe.modifierSection).not.toHaveBeenCalled();
  });

  it('barre d’outils : résumé du plan ; export et aide dans le menu « ⋯ »', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb', null, () => (
      <EtapesEditeur plan="Deploiement" exporter={{ url: '/api/export', title: 'Une ligne par étape' }} />
    ));
    const barre = screen.getByRole('toolbar', { name: 'Outils du graphe' });
    expect(barre).toHaveTextContent('2 étapes');
    expect(screen.queryByRole('button', { name: 'Exporter en Excel' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Aide du graphe' })).toBeNull();
    await userEvent.click(within(barre).getByRole('button', { name: 'Plus d’actions du graphe' }));
    expect(await screen.findByRole('menuitem', { name: 'Exporter en Excel' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Aide du graphe' }));
    expect(await screen.findByRole('dialog', { name: 'Aide du graphe' })).toHaveTextContent('Aller à…');
  });

  it('étape ouverte en tiroir dans le cadre du graphe, sous le bandeau ; bac en rail', async () => {
    rendre(planAvecSections(), '/meps/m1?section=sb&etape=e2');
    await waitFor(() =>
      expect(document.querySelector('.graphe-libre [data-emplacement-tiroir] [data-tiroir-etape]')).not.toBeNull(),
    );
    expect(await screen.findByLabelText('Action')).toHaveValue('Arrêt');
    // Bac replié en rail (fenêtre de moins de 1440 px de large).
    const bac = screen.getByRole('complementary', { name: 'Bac des éléments à placer' });
    expect(bac).toHaveTextContent('À placer (0)');
  });

  it('écran large : le bac reste ouvert', () => {
    largeur = 1600;
    rendre(planAvecSections(), '/meps/m1?section=sb');
    const bac = screen.getByRole('complementary', { name: 'Bac des éléments à placer' });
    expect(within(bac).getByRole('heading', { name: /Étapes à placer/ })).toBeInTheDocument();
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
