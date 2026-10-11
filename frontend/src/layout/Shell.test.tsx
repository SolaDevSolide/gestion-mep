import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthContext } from '../auth';
import { ThemeProvider } from '../theme';
import { TooltipProvider } from '../ui/Tooltip';
import { utilisateur } from '../test/fabrique';
import type { Utilisateur } from '../types';
import { Shell } from './Shell';

vi.mock('../api', () => ({ api: { meps: vi.fn().mockResolvedValue([]), application: vi.fn() } }));

const miseAJour = {
  active: true,
  depot: 'SolaDevSolide/gestion-mep',
  versionCourante: '1.0.0',
  derniereVersion: '1.1.0',
  publieeLe: null,
  notesUrl: null,
  verifieeLe: null,
  erreur: null,
  disponible: true,
  installateur: { present: false, statut: null, version: null, message: null, le: null },
};

beforeEach(() => {
  vi.mocked(api.application).mockResolvedValue({
    version: '1.0.0',
    lienSignalement: 'https://github.com/SolaDevSolide/gestion-mep/issues/new/choose',
    miseAJour: null,
  });
});

function rendre(u: Utilisateur) {
  render(
    <AuthContext.Provider
      value={{ utilisateur: u, chargement: false, connecter: vi.fn(), deconnecter: vi.fn(), majUtilisateur: vi.fn() }}
    >
      <ThemeProvider>
        <TooltipProvider>
          <MemoryRouter>
            <Routes>
              <Route element={<Shell />}>
                <Route path="/" element={<p>Contenu</p>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </TooltipProvider>
      </ThemeProvider>
    </AuthContext.Provider>,
  );
}

describe('Menu : administration', () => {
  it('masque le lien de la console à un non-administrateur', () => {
    rendre(utilisateur({ estAdmin: false }));
    expect(screen.queryByRole('link', { name: /Console d’administration/ })).not.toBeInTheDocument();
  });

  it('affiche le lien de la console à un administrateur', () => {
    rendre(utilisateur({ estAdmin: true }));
    expect(screen.getByRole('link', { name: /Console d’administration/ })).toHaveAttribute('href', '/admin');
  });
});

describe('Menu : version et signalement', () => {
  it('affiche la version et le lien de signalement dans le menu utilisateur', async () => {
    rendre(utilisateur({ estAdmin: false }));
    await userEvent.click(screen.getByRole('button', { name: 'Menu utilisateur' }));
    expect(await screen.findByText('Version 1.0.0')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Signaler un problème ou une idée/ })).toBeInTheDocument();
  });

  it('signale une nouvelle version à un administrateur', async () => {
    vi.mocked(api.application).mockResolvedValue({ version: '1.0.0', lienSignalement: null, miseAJour });
    rendre(utilisateur({ estAdmin: true }));
    expect(await screen.findByRole('link', { name: /Mise à jour 1.1.0 disponible/ })).toHaveAttribute(
      'href',
      '/admin?onglet=version',
    );
  });
});
