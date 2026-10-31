import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { HistoriqueSysteme, MepHistorique, SystemeCatalogue } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import SystemeFichePage from './SystemeFichePage';
import SystemesPage from './SystemesPage';

const bdd: SystemeCatalogue = {
  id: 'c-bdd',
  nom: 'BDD',
  description: 'Base de données de test',
  cible: 'pg-clients',
  actif: true,
  environnements: [
    { nom: 'Recette', cible: 'pg-clients-rec', production: false },
    { nom: 'Production', cible: 'pg-clients-prd', production: true },
  ],
  categories: ['Tables', 'Vues'],
  nbObjets: 3,
};

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(),
    objetsCatalogue: vi.fn(),
    categoriesParDefaut: vi.fn(),
    definirCategoriesParDefaut: vi.fn(async (c: string[]) => c),
    environnementsCatalogue: vi.fn(async () => ({})),
    categoriesCatalogue: vi.fn(async () => ({})),
    ajouterObjetCatalogue: vi.fn(async () => ({})),
    modifierObjetCatalogue: vi.fn(async () => ({})),
    supprimerObjetCatalogue: vi.fn(async () => undefined),
    historiqueSysteme: vi.fn(),
    activiteSystemes: vi.fn(async () => []),
    urlExportHistorique: (id: string) => `/api/catalogue/systemes/${id}/historique/excel`,
  },
}));

function rendre(url: string, estAdmin = true) {
  render(
    <MemoryRouter initialEntries={[url]}>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
        <TooltipProvider>
          <Routes>
            <Route path="/parametres/systemes" element={<SystemesPage />} />
            <Route path="/parametres/systemes/:id" element={<SystemeFichePage />} />
          </Routes>
        </TooltipProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

describe('Catalogue des systèmes : environnements et objets', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.catalogueSystemes).mockResolvedValue([bdd]);
    vi.mocked(api.objetsCatalogue).mockResolvedValue([
      {
        id: 'o1',
        categorie: 'Tables',
        type: 'Table',
        nom: 'clients',
        libelle: 'Fiches clients',
        precision: 'schéma public',
      },
      { id: 'o2', categorie: 'Tables', type: 'Table', nom: 'contrats', libelle: 'Contrats signés', precision: '' },
      { id: 'o3', categorie: 'Vues', type: 'Vue', nom: 'v_clients', libelle: '', precision: '' },
    ]);
    vi.mocked(api.categoriesParDefaut).mockResolvedValue(['Tables', 'Vues', 'Index', 'Packages']);
  });

  it('la liste montre les environnements et le nombre d’objets ; les catégories par défaut se gèrent', async () => {
    rendre('/parametres/systemes');
    const ligne = (await screen.findByRole('rowheader', { name: 'BDD' })).closest('tr')!;
    expect(within(ligne).getByText('Production')).toBeInTheDocument();
    expect(within(ligne).getByRole('link', { name: '3 dans 2 catégories' })).toHaveAttribute(
      'href',
      '/parametres/systemes/c-bdd',
    );

    await userEvent.click(screen.getByRole('button', { name: 'Catégories par défaut' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Catégories d’objets par défaut' });
    await within(dialogue).findByText('Packages');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Retirer Packages' }));
    await userEvent.type(within(dialogue).getByLabelText('Nouvelle catégorie'), 'Écrans{Enter}');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.definirCategoriesParDefaut).toHaveBeenCalledWith(['Tables', 'Vues', 'Index', 'Écrans']),
    );
  });

  it('fiche : environnements, catégories avec leurs éléments, ajout d’un élément', async () => {
    rendre('/parametres/systemes/c-bdd');
    expect(await screen.findByRole('heading', { name: /BDD/, level: 1 })).toBeInTheDocument();
    expect(screen.getByText('pg-clients-prd')).toBeInTheDocument();
    expect(screen.getByText(/2 autres catégories par défaut disponibles/)).toBeInTheDocument();
    const tables = screen.getByRole('region', { name: 'Catégorie Tables' });
    expect(within(tables).getByText('clients')).toBeInTheDocument();
    expect(within(tables).getByText(/2 éléments connus/)).toBeInTheDocument();

    await userEvent.click(within(tables).getByRole('button', { name: 'Ajouter un élément' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Ajouter un élément à Tables' });
    await userEvent.type(within(dialogue).getByLabelText('Nom *'), 'adresses');
    await userEvent.type(within(dialogue).getByLabelText('Type'), 'Table');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.ajouterObjetCatalogue).toHaveBeenCalledWith('c-bdd', {
        categorie: 'Tables',
        type: 'Table',
        nom: 'adresses',
        libelle: '',
        precision: '',
      }),
    );
  });

  it('ajouter une catégorie propose les catégories par défaut pas encore utilisées ; ajouter un environnement', async () => {
    rendre('/parametres/systemes/c-bdd');
    await userEvent.click(await screen.findByRole('button', { name: 'Ajouter une catégorie' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Ajouter une catégorie' });
    const suggestions = within(dialogue).getByRole('group', { name: 'Catégories par défaut' });
    expect(within(suggestions).queryByRole('button', { name: 'Tables' })).toBeNull();
    await userEvent.click(within(suggestions).getByRole('button', { name: 'Index' }));
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.categoriesCatalogue).toHaveBeenCalledWith('c-bdd', [
        { nom: 'Tables' },
        { nom: 'Vues' },
        { nom: 'Index' },
      ]),
    );

    await userEvent.click(screen.getByRole('button', { name: 'Ajouter un environnement' }));
    const env = await screen.findByRole('dialog', { name: 'Ajouter un environnement' });
    await userEvent.type(within(env).getByLabelText('Nom *'), 'Prod-secours');
    // La production se déduit du nom tant qu'on ne touche pas la case.
    expect(within(env).getByRole('checkbox', { name: /Environnement de production/ })).toBeChecked();
    await userEvent.click(within(env).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.environnementsCatalogue).toHaveBeenCalledWith('c-bdd', [
        ...bdd.environnements,
        { nom: 'Prod-secours', cible: '', production: true },
      ]),
    );
  });

  it('lecture seule hors administrateurs', async () => {
    rendre('/parametres/systemes/c-bdd', false);
    expect(await screen.findByText(/seuls les administrateurs peuvent modifier la fiche/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ajouter une catégorie' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Ajouter un environnement' })).toBeNull();
  });
});

/** Jour d'il y a `n` jours, au format des MEP (« 2026-07-14 »). */
function ilYaJours(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const ligne = (p: Partial<MepHistorique> & Pick<MepHistorique, 'id' | 'outil' | 'etat' | 'jour'>): MepHistorique => ({
  versionCible: '',
  objet: '',
  type: 'Complete',
  go: `${p.jour}T20:00:00Z`,
  clotureeLe: null,
  pilote: 'Alice',
  environnements: [{ nom: 'Production', production: true }],
  autresSystemes: [],
  echecs: [],
  rapport: false,
  ...p,
});

const historique = (meps: MepHistorique[], resume: Partial<HistoriqueSysteme['resume']> = {}): HistoriqueSysteme => ({
  systemeId: bdd.id,
  nom: bdd.nom,
  resume: {
    total: meps.length,
    douzeMois: meps.length,
    enCours: 0,
    cloturees: 0,
    reussies: 0,
    avecIncidents: 0,
    repliees: 0,
    echecs: 0,
    echecsRepris: 0,
    enEchec: 0,
    annulees: 0,
    derniereMepId: meps[0]?.id ?? null,
    ...resume,
  },
  meps,
});

describe('Fiche d’un système : historique des MEP (#59)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.catalogueSystemes).mockResolvedValue([bdd]);
    vi.mocked(api.objetsCatalogue).mockResolvedValue([]);
    vi.mocked(api.categoriesParDefaut).mockResolvedValue([]);
  });

  it('l’onglet montre la synthèse, la frise, les MEP par mois et les échecs sur le système', async () => {
    const user = userEvent.setup();
    vi.mocked(api.historiqueSysteme).mockResolvedValue(
      historique(
        [
          ligne({ id: 'p', outil: 'Portail', etat: 'Preparation', jour: ilYaJours(-3), go: null }),
          ligne({
            id: 'b',
            outil: 'Batch de nuit',
            versionCible: '4.2',
            etat: 'AvecIncidents',
            jour: ilYaJours(40),
            rapport: true,
            environnements: [
              { nom: 'Recette', production: false },
              { nom: 'Production', production: true },
            ],
            autresSystemes: [{ catalogueId: 'c-batch', nom: 'BATCH', environnements: [], echecs: 0 }],
            echecs: [
              {
                plan: 'Deploiement',
                code: 'E0002',
                action: 'Migrer le schéma',
                echecLe: `${ilYaJours(40)}T21:00:00Z`,
                motif: 'Verrou sur la table',
                repris: true,
                resolution: 'Lever le verrou',
                statutResolution: 'Reussie',
              },
            ],
          }),
          ligne({ id: 'r', outil: 'Rendez-vous', etat: 'Repliee', jour: ilYaJours(80), rapport: true }),
        ],
        { enCours: 1, cloturees: 2, avecIncidents: 1, repliees: 1, echecs: 1, echecsRepris: 1, derniereMepId: 'b' },
      ),
    );
    rendre('/parametres/systemes/c-bdd?onglet=historique');
    expect(await screen.findByRole('tab', { name: 'Historique des MEP', selected: true })).toBeInTheDocument();
    expect(await screen.findByText('Clôturées avec problème')).toBeInTheDocument();
    expect(screen.getByText('100 %')).toBeInTheDocument();
    expect(screen.getByText(/1 repris · 0 non repris/)).toBeInTheDocument();
    // Frise : les deux MEP lancées, pas celle à venir.
    expect(
      within(screen.getByRole('list', { name: 'MEP des douze derniers mois' })).getAllByRole('listitem'),
    ).toHaveLength(2);
    // À venir en tête, puis les mois.
    const sections = screen.getAllByRole('region');
    expect(sections[0]).toHaveAccessibleName('À venir : en préparation');
    const batch = sections.find((r) => within(r).queryByText('Batch de nuit'))!;
    expect(within(batch).getByText(/Repris après la résolution « Lever le verrou »/)).toBeInTheDocument();
    expect(within(batch).getByRole('link', { name: 'Rapport de fin' })).toHaveAttribute('href', '/meps/b/rapport');
    expect(within(batch).getByRole('button', { name: 'BATCH : historique et MEP de ce système' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ouvrir dans la liste des MEP' })).toHaveAttribute(
      'href',
      '/?systeme=c-bdd',
    );

    // Filtre d'environnement : seule la MEP passée en recette reste ; le lien vers la liste suit.
    await user.click(screen.getByRole('radio', { name: /Recette \(1\)/ }));
    expect(screen.queryByText('Rendez-vous')).toBeNull();
    expect(screen.getByText('Batch de nuit')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ouvrir dans la liste des MEP' })).toHaveAttribute(
      'href',
      '/?systeme=c-bdd&environnement=Recette',
    );
    await user.click(screen.getByRole('radio', { name: /Tous/ }));
    await user.click(screen.getByRole('checkbox', { name: 'Avec un échec sur BDD' }));
    expect(screen.queryByText('Rendez-vous')).toBeNull();
    expect(screen.getByRole('link', { name: 'Ouvrir dans la liste des MEP' })).toHaveAttribute(
      'href',
      '/?systeme=c-bdd&echec=1',
    );

    // Retour à la fiche : l'onglet par défaut n'apparaît pas dans l'adresse.
    await user.click(screen.getByRole('tab', { name: 'Fiche' }));
    expect(await screen.findByRole('heading', { name: /Environnements/ })).toBeInTheDocument();
  });

  it('aucune MEP : état vide ; sans onglet demandé, la fiche s’affiche', async () => {
    const user = userEvent.setup();
    vi.mocked(api.historiqueSysteme).mockResolvedValue(historique([]));
    rendre('/parametres/systemes/c-bdd');
    expect(await screen.findByRole('tab', { name: 'Fiche', selected: true })).toBeInTheDocument();
    expect(api.historiqueSysteme).not.toHaveBeenCalled();
    await user.click(screen.getByRole('tab', { name: 'Historique des MEP' }));
    expect(await screen.findByText('Aucune MEP n’a encore touché BDD')).toBeInTheDocument();
  });

  it('le catalogue montre la dernière MEP de chaque système', async () => {
    vi.mocked(api.activiteSystemes).mockResolvedValue([
      {
        systemeId: 'c-bdd',
        nbMep: 3,
        derniereMepId: 'b',
        derniereOutil: 'Batch de nuit',
        derniereVersion: '4.2',
        derniereJour: '2026-09-08',
        derniereEtat: 'AvecIncidents',
      },
    ]);
    rendre('/parametres/systemes');
    expect(await screen.findByRole('columnheader', { name: 'Dernière MEP' })).toBeInTheDocument();
    const lien = await screen.findByRole('link', { name: /Historique des MEP de BDD : dernière le/ });
    expect(lien).toHaveAttribute('href', '/parametres/systemes/c-bdd?onglet=historique');
    expect(lien).toHaveTextContent('Batch de nuit → 4.2 · 3 MEP');
  });
});

describe('Catalogue des systèmes : systèmes gérés par Noosphere', () => {
  const billing: SystemeCatalogue = {
    ...bdd,
    id: 'c-bil',
    nom: 'Billing',
    description: 'Issues invoices',
    source: {
      liaisonId: 'l1',
      applicationId: 'billing',
      url: 'https://docs.alize.example/applications/billing',
      domaine: 'Finance',
      equipe: 'Équipe Finance',
      cycle: 'live',
      derniereSynchro: '2026-10-31T11:44:00Z',
      absente: false,
    },
  };
  const archive: SystemeCatalogue = {
    ...billing,
    id: 'c-arc',
    nom: 'Invoice archive',
    actif: false,
    source: { ...billing.source!, applicationId: 'archive', cycle: 'retired' },
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.catalogueSystemes).mockResolvedValue([billing, archive, bdd]);
    vi.mocked(api.objetsCatalogue).mockResolvedValue([]);
    vi.mocked(api.categoriesParDefaut).mockResolvedValue([]);
  });

  it('la liste marque les systèmes gérés, filtre par source et ne propose pas de les supprimer', async () => {
    rendre('/parametres/systemes');
    const ligne = (await screen.findByRole('rowheader', { name: /Billing/ })).closest('tr')!;
    expect(within(ligne).getByText('Noosphere')).toBeInTheDocument();
    expect(within(ligne).getByText('Finance · Équipe Finance')).toBeInTheDocument();
    expect(within(ligne).getByRole('button', { name: 'Supprimer Billing' })).toBeDisabled();
    expect(screen.getByText('Inactif · retirée dans Noosphere')).toBeInTheDocument();
    expect(screen.getByText(/2 systèmes gérés par Noosphere · dernière synchronisation/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Locaux · 1' }));
    expect(screen.queryByRole('rowheader', { name: /Billing/ })).not.toBeInTheDocument();
    expect(screen.getByRole('rowheader', { name: 'BDD' })).toBeInTheDocument();
  });

  it('la fiche montre la source et verrouille nom et description', async () => {
    rendre('/parametres/systemes/c-bil');
    expect(await screen.findByText('Géré par Noosphere')).toBeInTheDocument();
    expect(screen.getByText('Nom et description viennent de Noosphere')).toBeInTheDocument();
    expect(screen.getByText('Dans Noosphere')).toBeInTheDocument();
    expect(screen.getByText('En service')).toBeInTheDocument();
    for (const lien of screen.getAllByRole('link', { name: /Ouvrir (la fiche )?dans Noosphere/ }))
      expect(lien).toHaveAttribute('href', 'https://docs.alize.example/applications/billing');
    await userEvent.click(screen.getByRole('button', { name: 'Modifier' }));
    const dialogue = await screen.findByRole('dialog');
    expect(within(dialogue).getByLabelText('Nom')).toHaveAttribute('readonly');
    expect(within(dialogue).getByLabelText('Description')).toHaveAttribute('readonly');
    expect(within(dialogue).getByRole('link', { name: 'Ouvrir Billing dans Noosphere' })).toBeInTheDocument();
    expect(within(dialogue).getByLabelText('Cible exacte')).not.toHaveAttribute('readonly');
  });
});
