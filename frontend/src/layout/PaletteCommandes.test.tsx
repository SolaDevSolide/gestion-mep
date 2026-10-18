import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { mepResume, utilisateur } from '../test/fabrique';
import { ThemeProvider } from '../theme';
import { PaletteCommandes } from './PaletteCommandes';

vi.mock('../api', () => ({
  api: {
    meps: vi.fn(),
    catalogueSystemes: vi.fn(),
    utilisateurs: vi.fn(),
  },
}));

function Adresse() {
  const l = useLocation();
  return <output aria-label="Adresse">{`${l.pathname}${l.search}`}</output>;
}

function rendre() {
  render(
    <MemoryRouter initialEntries={['/profil']}>
      <ThemeProvider>
        <AuthContext.Provider value={{ utilisateur: utilisateur() } as never}>
          <Routes>
            <Route
              path="*"
              element={
                <>
                  <PaletteCommandes open onOpenChange={() => undefined} />
                  <Adresse />
                </>
              }
            />
          </Routes>
        </AuthContext.Provider>
      </ThemeProvider>
    </MemoryRouter>,
  );
}

describe('Palette de commandes : recherche, systèmes et personnes (#59)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    Element.prototype.scrollIntoView ??= () => undefined;
    vi.mocked(api.meps).mockResolvedValue([mepResume({ id: 'm1', outil: 'Portail clients' })]);
    vi.mocked(api.catalogueSystemes).mockResolvedValue([
      {
        id: 'c-bdd',
        nom: 'BDD',
        description: 'Base clients',
        cible: '',
        actif: true,
        environnements: [],
        categories: [],
        nbObjets: 0,
      },
      {
        id: 'c-old',
        nom: 'BDD2',
        description: '',
        cible: '',
        actif: false,
        environnements: [],
        categories: [],
        nbObjets: 0,
      },
    ]);
    vi.mocked(api.utilisateurs).mockResolvedValue([
      utilisateur({ id: 'u-alice', login: 'alice', nomAffiche: 'Alice Martin' }),
      utilisateur({ id: 'u-alice-adm', login: 'alice.adm', nomAffiche: 'Alice (admin)', comptePrincipalId: 'u-alice' }),
    ]);
  });

  it('sans saisie, ni recherche ni systèmes ni personnes', async () => {
    rendre();
    expect(await screen.findByRole('option', { name: /Portail clients/ })).toBeInTheDocument();
    expect(screen.queryByText('Systèmes')).toBeNull();
    expect(screen.queryByText('Personnes')).toBeNull();
    expect(screen.queryByRole('option', { name: /Rechercher/ })).toBeNull();
  });

  it('rechercher le texte saisi dans la liste des MEP', async () => {
    const user = userEvent.setup();
    rendre();
    await user.type(await screen.findByRole('combobox'), 'stocks');
    await user.click(screen.getByRole('option', { name: /Rechercher « stocks » dans les MEP/ }));
    expect(screen.getByRole('status', { name: 'Adresse', hidden: true })).toHaveTextContent('/?recherche=stocks');
  });

  it('un système actif du catalogue ouvre son historique', async () => {
    const user = userEvent.setup();
    rendre();
    await user.type(await screen.findByRole('combobox'), 'bdd');
    expect(screen.getByText('Systèmes')).toBeInTheDocument();
    // Le système inactif n'est pas proposé.
    expect(screen.queryByRole('option', { name: /BDD2/ })).toBeNull();
    await user.click(screen.getByRole('option', { name: /^BDD\s*Historique des MEP/ }));
    expect(screen.getByRole('status', { name: 'Adresse', hidden: true })).toHaveTextContent(
      '/parametres/systemes/c-bdd?onglet=historique',
    );
  });

  it('une personne, retrouvée par l’un de ses comptes, ouvre ses MEP', async () => {
    const user = userEvent.setup();
    rendre();
    await user.type(await screen.findByRole('combobox'), 'alice.adm');
    await user.click(await screen.findByRole('option', { name: /Alice Martin\s*Ses MEP/ }));
    expect(screen.getByRole('status', { name: 'Adresse', hidden: true })).toHaveTextContent('/?personne=u-alice');
  });
});
