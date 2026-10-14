import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { SystemeCatalogue } from '../types';
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
