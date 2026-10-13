import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import { ToastProvider } from '../ui/Toast';
import { TooltipProvider } from '../ui/Tooltip';
import type { Utilisateur } from '../types';
import AdminPage from './AdminPage';

vi.mock('../api', () => ({
  api: {
    adminUtilisateurs: vi.fn(),
    adminCreerUtilisateur: vi.fn(),
    adminReinitialiserMotDePasse: vi.fn(),
    adminJournal: vi.fn(),
    parametres: vi.fn(),
    modifierParametres: vi.fn(),
    application: vi.fn(),
    verifierMisesAJour: vi.fn(),
    installerMiseAJour: vi.fn(),
  },
}));

function rendre(u: Utilisateur, url = '/admin') {
  render(
    <AuthContext.Provider
      value={{ utilisateur: u, chargement: false, connecter: vi.fn(), deconnecter: vi.fn(), majUtilisateur: vi.fn() }}
    >
      <TooltipProvider>
        <ToastProvider>
          <MemoryRouter initialEntries={[url]}>
            <Routes>
              <Route path="/admin" element={<AdminPage />} />
              <Route path="/" element={<p>Accueil</p>} />
            </Routes>
          </MemoryRouter>
        </ToastProvider>
      </TooltipProvider>
    </AuthContext.Provider>,
  );
}

describe('Console d’administration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.adminUtilisateurs).mockResolvedValue([
      utilisateur({ id: 'u1', login: 'admin', nomAffiche: 'Admin', estAdmin: true }),
      utilisateur({ id: 'u2', login: 'bob', nomAffiche: 'Bob', actif: false }),
    ]);
  });

  it('redirige un non-administrateur vers l’accueil', async () => {
    rendre(utilisateur({ estAdmin: false }));
    expect(await screen.findByText('Accueil')).toBeInTheDocument();
    expect(api.adminUtilisateurs).not.toHaveBeenCalled();
  });

  it('liste les comptes, y compris désactivés', async () => {
    rendre(utilisateur({ estAdmin: true }));
    expect(await screen.findByText('Bob')).toBeInTheDocument();
    expect(screen.getByText('Désactivé')).toBeInTheDocument();
  });

  it('crée un compte et affiche le mot de passe provisoire', async () => {
    const cree = utilisateur({ id: 'u3', login: 'dora', nomAffiche: 'Dora', doitChangerMotDePasse: true });
    vi.mocked(api.adminCreerUtilisateur).mockResolvedValue({
      utilisateur: cree,
      motDePasseProvisoire: 'Zx9-provisoire',
    });
    rendre(utilisateur({ estAdmin: true }));
    await screen.findByText('Bob');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau compte' }));
    await userEvent.type(screen.getByLabelText(/Identifiant/), 'dora');
    await userEvent.type(screen.getByLabelText(/Nom affiché/), 'Dora');
    await userEvent.click(screen.getByRole('button', { name: 'Créer le compte' }));
    await waitFor(() =>
      expect(api.adminCreerUtilisateur).toHaveBeenCalledWith({
        login: 'dora',
        nomAffiche: 'Dora',
        estAdmin: false,
        role: '',
        contact: '',
      }),
    );
    expect(await screen.findByText('Zx9-provisoire')).toBeInTheDocument();
    expect(screen.getByText(/canal sûr/)).toBeInTheDocument();
  });

  it('affiche l’erreur du serveur dans le dialogue de création', async () => {
    vi.mocked(api.adminCreerUtilisateur).mockRejectedValue(new Error('Cet identifiant est déjà pris.'));
    rendre(utilisateur({ estAdmin: true }));
    await screen.findByText('Bob');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau compte' }));
    await userEvent.type(screen.getByLabelText(/Identifiant/), 'bob');
    await userEvent.type(screen.getByLabelText(/Nom affiché/), 'Bob 2');
    await userEvent.click(screen.getByRole('button', { name: 'Créer le compte' }));
    expect(await screen.findByText('Cet identifiant est déjà pris.')).toBeInTheDocument();
  });

  it('ouvre l’onglet Version depuis le lien et vérifie les mises à jour à la demande', async () => {
    const etat = {
      active: true,
      depot: 'SolaDevSolide/gestion-mep',
      versionCourante: '1.0.0',
      derniereVersion: '1.0.0',
      publieeLe: null,
      notesUrl: null,
      verifieeLe: null,
      erreur: null,
      disponible: false,
      installateur: { present: false, statut: null, version: null, message: null, le: null },
    };
    vi.mocked(api.application).mockResolvedValue({
      version: '1.0.0',
      lienSignalement: null,
      miseAJour: etat,
      installation: 'docker',
    });
    vi.mocked(api.verifierMisesAJour).mockResolvedValue({ ...etat, derniereVersion: '1.2.0', disponible: true });
    rendre(utilisateur({ estAdmin: true }), '/admin?onglet=version');
    expect(await screen.findByText('L’application est à jour.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Vérifier maintenant' }));
    expect(await screen.findByText('La version 1.2.0 est disponible')).toBeInTheDocument();
  });

  it('installe la nouvelle version par le service de mise à jour, après confirmation', async () => {
    const installateur = { present: true, statut: 'pret', version: null, message: 'Prêt.', le: null };
    const etat = {
      active: true,
      depot: 'SolaDevSolide/gestion-mep',
      versionCourante: '1.0.0',
      derniereVersion: '1.2.0',
      publieeLe: null,
      notesUrl: null,
      verifieeLe: null,
      erreur: null,
      disponible: true,
      installateur,
    };
    vi.mocked(api.application).mockResolvedValue({
      version: '1.0.0',
      lienSignalement: null,
      miseAJour: etat,
      installation: 'docker',
    });
    vi.mocked(api.installerMiseAJour).mockResolvedValue({
      ...etat,
      installateur: { ...installateur, statut: 'demandee', message: 'Demande transmise au service de mise à jour.' },
    });
    rendre(utilisateur({ estAdmin: true }), '/admin?onglet=version');
    await userEvent.click(await screen.findByRole('button', { name: 'Mettre à jour vers 1.2.0' }));
    await userEvent.click(screen.getByRole('button', { name: 'Mettre à jour' }));
    await waitFor(() => expect(api.installerMiseAJour).toHaveBeenCalledWith('1.2.0'));
    expect(await screen.findByText('Mise à jour en cours')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mettre à jour vers 1.2.0' })).toBeNull();
  });
});
