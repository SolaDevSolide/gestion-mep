import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import ConnexionPage from './ConnexionPage';

vi.mock('../api', () => ({ api: { optionsConnexion: vi.fn() } }));

const auth = {
  utilisateur: null,
  chargement: false,
  connecter: vi.fn(),
  deconnecter: vi.fn(),
  majUtilisateur: vi.fn(),
};

function rendre(url = '/connexion') {
  render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={[url]}>
        <ConnexionPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

describe('Page de connexion', () => {
  beforeEach(() => vi.clearAllMocks());

  it('propose la connexion unique quand elle est active', async () => {
    vi.mocked(api.optionsConnexion).mockResolvedValue({ ssoActif: true, ssoLibelle: 'Se connecter avec Microsoft' });
    rendre();
    expect(await screen.findByRole('button', { name: 'Se connecter avec Microsoft' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Se connecter' })).toBeInTheDocument();
  });

  it('n’affiche pas le bouton quand elle est inactive', async () => {
    vi.mocked(api.optionsConnexion).mockResolvedValue({ ssoActif: false, ssoLibelle: null });
    rendre();
    await waitFor(() => expect(api.optionsConnexion).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: /Microsoft/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Se connecter' })).toBeInTheDocument();
  });

  it('n’affiche pas le bouton quand les options ne se chargent pas', async () => {
    vi.mocked(api.optionsConnexion).mockRejectedValue(new Error('x'));
    rendre();
    await waitFor(() => expect(api.optionsConnexion).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: /Microsoft/ })).toBeNull();
  });

  it('explique l’erreur renvoyée par le serveur', async () => {
    vi.mocked(api.optionsConnexion).mockResolvedValue({ ssoActif: true, ssoLibelle: null });
    rendre('/connexion?erreur=login-existant');
    expect(await screen.findByText(/Un compte local porte déjà cet identifiant/)).toBeInTheDocument();
  });

  it('affiche un message générique pour un code inconnu', async () => {
    vi.mocked(api.optionsConnexion).mockResolvedValue({ ssoActif: false, ssoLibelle: null });
    rendre('/connexion?erreur=bizarre');
    expect(await screen.findByText(/La connexion a échoué/)).toBeInTheDocument();
  });
});
