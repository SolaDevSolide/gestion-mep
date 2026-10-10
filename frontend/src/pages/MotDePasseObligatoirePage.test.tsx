import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import App from '../App';
import { AuthContext } from '../auth';
import { TooltipProvider } from '../ui/Tooltip';
import { utilisateur } from '../test/fabrique';
import type { Utilisateur } from '../types';

vi.mock('../api', () => ({ api: { changerMotDePasse: vi.fn(), meps: vi.fn() } }));

function rendre(u: Utilisateur) {
  const majUtilisateur = vi.fn();
  vi.mocked(api.meps).mockResolvedValue([]);
  render(
    <AuthContext.Provider
      value={{ utilisateur: u, chargement: false, connecter: vi.fn(), deconnecter: vi.fn(), majUtilisateur }}
    >
      <TooltipProvider>
        <MemoryRouter initialEntries={['/']}>
          <App />
        </MemoryRouter>
      </TooltipProvider>
    </AuthContext.Provider>,
  );
  return { majUtilisateur };
}

describe('Mot de passe provisoire', () => {
  beforeEach(() => vi.clearAllMocks());

  it('n’affiche que la page de changement tant que le mot de passe est provisoire', async () => {
    rendre(utilisateur({ doitChangerMotDePasse: true }));
    expect(screen.getByRole('heading', { name: 'Choisissez votre mot de passe' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Navigation principale' })).not.toBeInTheDocument();
  });

  it('exige 10 caractères et une confirmation identique', async () => {
    rendre(utilisateur({ doitChangerMotDePasse: true }));
    const bouton = screen.getByRole('button', { name: 'Changer le mot de passe' });
    await userEvent.type(screen.getByLabelText('Mot de passe provisoire'), 'prov');
    await userEvent.type(screen.getByLabelText('Nouveau mot de passe'), 'court');
    expect(screen.getByText('Au moins 10 caractères.')).toBeInTheDocument();
    expect(bouton).toBeDisabled();
    await userEvent.clear(screen.getByLabelText('Nouveau mot de passe'));
    await userEvent.type(screen.getByLabelText('Nouveau mot de passe'), 'assez-long-1');
    await userEvent.type(screen.getByLabelText(/Confirmation/), 'autre-chose-1');
    expect(screen.getByText('La confirmation ne correspond pas.')).toBeInTheDocument();
    expect(bouton).toBeDisabled();
  });

  it('envoie le changement puis met à jour l’utilisateur', async () => {
    const maj = utilisateur({ doitChangerMotDePasse: false });
    vi.mocked(api.changerMotDePasse).mockResolvedValue(maj);
    const { majUtilisateur } = rendre(utilisateur({ doitChangerMotDePasse: true }));
    await userEvent.type(screen.getByLabelText('Mot de passe provisoire'), 'prov');
    await userEvent.type(screen.getByLabelText('Nouveau mot de passe'), 'assez-long-1');
    await userEvent.type(screen.getByLabelText(/Confirmation/), 'assez-long-1');
    await userEvent.click(screen.getByRole('button', { name: 'Changer le mot de passe' }));
    await waitFor(() =>
      expect(api.changerMotDePasse).toHaveBeenCalledWith({ ancien: 'prov', nouveau: 'assez-long-1' }),
    );
    await waitFor(() => expect(majUtilisateur).toHaveBeenCalledWith(maj));
  });

  it('affiche l’erreur du serveur', async () => {
    vi.mocked(api.changerMotDePasse).mockRejectedValue(new Error('Mot de passe actuel incorrect.'));
    rendre(utilisateur({ doitChangerMotDePasse: true }));
    await userEvent.type(screen.getByLabelText('Mot de passe provisoire'), 'faux');
    await userEvent.type(screen.getByLabelText('Nouveau mot de passe'), 'assez-long-1');
    await userEvent.type(screen.getByLabelText(/Confirmation/), 'assez-long-1');
    await userEvent.click(screen.getByRole('button', { name: 'Changer le mot de passe' }));
    expect(await screen.findByText('Mot de passe actuel incorrect.')).toBeInTheDocument();
  });
});
