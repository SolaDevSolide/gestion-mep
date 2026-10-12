import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import ConnexionPage from './ConnexionPage';

vi.mock('../api', () => ({ api: { optionsConnexion: vi.fn(), demo: vi.fn(), connexionDemo: vi.fn() } }));

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
        <Routes>
          <Route path="/connexion" element={<ConnexionPage />} />
          <Route path="/" element={<p>Accueil</p>} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

describe('Page de connexion', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.demo).mockResolvedValue(null);
  });

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

const comptes = [
  {
    login: 'alice',
    nomAffiche: 'Alice',
    role: 'Pilote / DBA',
    estAdmin: true,
    description: 'Administratrice, pilote de la plupart des MEP.',
  },
  { login: 'bob', nomAffiche: 'Bob', role: 'Développeur API', estAdmin: false, description: 'Suppléant du pilote.' },
];

describe('Page de connexion : mode démonstration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.optionsConnexion).mockResolvedValue({ ssoActif: false, ssoLibelle: null });
  });

  it('propose les comptes de démonstration, en plus du formulaire, quand la démonstration est active', async () => {
    vi.mocked(api.demo).mockResolvedValue({ actif: true, comptes });
    rendre();
    const liste = await screen.findByRole('region', { name: /Connexion rapide/ });
    expect(liste).toHaveTextContent('Alice');
    expect(liste).toHaveTextContent('Administrateur');
    expect(liste).toHaveTextContent('Pilote / DBA');
    expect(liste).toHaveTextContent('Suppléant du pilote.');
    expect(screen.getByRole('button', { name: 'Se connecter en tant que Bob (bob)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Se connecter' })).toBeInTheDocument();
  });

  it('n’affiche aucune connexion rapide hors démonstration', async () => {
    vi.mocked(api.demo).mockResolvedValue(null);
    rendre();
    await waitFor(() => expect(api.demo).toHaveBeenCalled());
    expect(screen.queryByRole('region', { name: /Connexion rapide/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /Se connecter en tant que/ })).toBeNull();
  });

  it('un clic sur un compte connecte directement et ouvre l’accueil', async () => {
    vi.mocked(api.demo).mockResolvedValue({ actif: true, comptes });
    const bob = {
      id: 'b',
      login: 'bob',
      nomAffiche: 'Bob',
      estAdmin: false,
      role: '',
      contact: '',
      expertises: [],
      actif: true,
      doitChangerMotDePasse: false,
    };
    vi.mocked(api.connexionDemo).mockResolvedValue(bob);
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Se connecter en tant que Bob (bob)' }));
    expect(api.connexionDemo).toHaveBeenCalledWith('bob');
    expect(auth.majUtilisateur).toHaveBeenCalledWith(bob);
    expect(await screen.findByText('Accueil')).toBeInTheDocument();
  });

  it('affiche le refus du serveur', async () => {
    vi.mocked(api.demo).mockResolvedValue({ actif: true, comptes });
    vi.mocked(api.connexionDemo).mockRejectedValue(
      new Error('La connexion rapide est réservée aux comptes de démonstration.'),
    );
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Se connecter en tant que Alice (alice)' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('réservée aux comptes de démonstration');
  });

  it('confirme la réinitialisation après le rechargement', async () => {
    vi.mocked(api.demo).mockResolvedValue({ actif: true, comptes });
    rendre('/connexion?demo=reinitialisee');
    expect(await screen.findByText(/La démonstration a été réinitialisée/)).toBeInTheDocument();
  });
});
