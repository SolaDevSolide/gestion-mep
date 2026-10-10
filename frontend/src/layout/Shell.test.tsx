import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthContext } from '../auth';
import { ThemeProvider } from '../theme';
import { TooltipProvider } from '../ui/Tooltip';
import { utilisateur } from '../test/fabrique';
import type { Utilisateur } from '../types';
import { Shell } from './Shell';

vi.mock('../api', () => ({ api: { meps: vi.fn().mockResolvedValue([]) } }));

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
