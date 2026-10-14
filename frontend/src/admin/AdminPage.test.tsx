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
    adminRelier: vi.fn(),
    adminDelier: vi.fn(),
    equipes: vi.fn(),
    adminCreerEquipe: vi.fn(),
    adminModifierEquipe: vi.fn(),
    adminSupprimerEquipe: vi.fn(),
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
    vi.mocked(api.equipes).mockResolvedValue([]);
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

  it('relie un autre compte de la même personne, en le désactivant si demandé', async () => {
    const jean = utilisateur({ id: 'j', login: 'jean', nomAffiche: 'Jean Dupont' });
    const sso = utilisateur({ id: 's', login: 'jean.sso', nomAffiche: 'J. Dupont (SSO)' });
    vi.mocked(api.adminUtilisateurs).mockResolvedValue([jean, sso]);
    vi.mocked(api.adminRelier).mockResolvedValue({ ...sso, comptePrincipalId: 'j', nomPersonne: 'Jean Dupont' });
    rendre(utilisateur({ id: 'admin', estAdmin: true }));
    await userEvent.click(await screen.findByRole('button', { name: 'Relier un compte à Jean Dupont' }));
    const relier = screen.getByRole('button', { name: 'Relier' });
    expect(relier).toBeDisabled();
    await userEvent.selectOptions(screen.getByLabelText('Autre compte de la même personne'), 's');
    await userEvent.click(screen.getByRole('checkbox', { name: /Désactiver ce compte/ }));
    await userEvent.click(relier);
    await waitFor(() =>
      expect(api.adminRelier).toHaveBeenCalledWith('s', { comptePrincipalId: 'j', desactiver: true }),
    );
  });

  it('regroupe les comptes reliés sous le compte principal et défait la liaison', async () => {
    const jean = utilisateur({ id: 'j', login: 'jean', nomAffiche: 'Jean Dupont' });
    const sso = utilisateur({
      id: 's',
      login: 'jean.sso',
      nomAffiche: 'J. Dupont (SSO)',
      nomPersonne: 'Jean Dupont',
      comptePrincipalId: 'j',
    });
    const alice = utilisateur({ id: 'a', login: 'alice', nomAffiche: 'Alice' });
    vi.mocked(api.adminUtilisateurs).mockResolvedValue([alice, sso, jean]);
    vi.mocked(api.adminDelier).mockResolvedValue({ ...sso, comptePrincipalId: null, nomPersonne: sso.nomAffiche });
    rendre(utilisateur({ id: 'admin', estAdmin: true }));
    expect(await screen.findByText('Relié à Jean Dupont : affiché sous ce nom')).toBeInTheDocument();
    const lignes = screen.getAllByRole('row').map((r) => r.textContent ?? '');
    expect(lignes.findIndex((l) => l.includes('jean.sso'))).toBe(
      lignes.findIndex((l) => l.startsWith('Jean Dupontjean')) + 1,
    );

    // La recherche d'un compte relié montre toute la personne.
    await userEvent.type(screen.getByLabelText('Rechercher un compte'), 'sso');
    expect(screen.getByText('Jean Dupont')).toBeInTheDocument();
    expect(screen.queryByText('Alice')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Défaire la liaison de J. Dupont (SSO)' }));
    await userEvent.click(screen.getByRole('button', { name: 'Défaire la liaison' }));
    await waitFor(() => expect(api.adminDelier).toHaveBeenCalledWith('s'));
  });

  it('filtre les comptes par équipe, au niveau de la personne', async () => {
    vi.mocked(api.equipes).mockResolvedValue([
      {
        id: 'e1',
        nom: 'Exploitation',
        description: '',
        membreIds: ['j'],
        creeeLe: '2026-10-01T08:00:00Z',
        contactSupport: '',
        signature: '',
      },
    ]);
    vi.mocked(api.adminUtilisateurs).mockResolvedValue([
      utilisateur({ id: 'j', login: 'jean', nomAffiche: 'Jean Dupont', equipeIds: ['e1'] }),
      utilisateur({ id: 's', login: 'jean.sso', nomAffiche: 'J. Dupont (SSO)', comptePrincipalId: 'j' }),
      utilisateur({ id: 'a', login: 'alice', nomAffiche: 'Alice' }),
    ]);
    rendre(utilisateur({ id: 'admin', estAdmin: true }));
    await screen.findByText('Alice');
    await userEvent.selectOptions(screen.getByLabelText('Filtrer par équipe'), 'e1');
    expect(screen.getByText('J. Dupont (SSO)')).toBeInTheDocument();
    expect(screen.queryByText('Alice')).toBeNull();
  });

  it('crée une équipe avec ses membres depuis l’onglet Équipes', async () => {
    vi.mocked(api.adminCreerEquipe).mockResolvedValue({
      id: 'e1',
      nom: 'Exploitation',
      description: '',
      membreIds: [],
      creeeLe: '2026-10-01T08:00:00Z',
      contactSupport: '',
      signature: '',
    });
    rendre(utilisateur({ estAdmin: true }), '/admin?onglet=equipes');
    expect(await screen.findByText('Aucune équipe')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Nouvelle équipe' }));
    await userEvent.type(screen.getByLabelText(/Nom/), 'Exploitation');
    await userEvent.type(screen.getByLabelText('Description'), 'Équipe de nuit');
    await userEvent.click(screen.getByRole('button', { name: 'Créer l’équipe' }));
    await waitFor(() =>
      expect(api.adminCreerEquipe).toHaveBeenCalledWith({
        nom: 'Exploitation',
        description: 'Équipe de nuit',
        membreIds: [],
      }),
    );
  });

  it('liste les équipes avec leurs membres, y compris désactivés, et supprime après confirmation', async () => {
    vi.mocked(api.equipes).mockResolvedValue([
      {
        id: 'e1',
        nom: 'Exploitation',
        description: 'Nuit',
        membreIds: ['u2'],
        creeeLe: '2026-10-01T08:00:00Z',
        contactSupport: '',
        signature: '',
      },
    ]);
    vi.mocked(api.adminSupprimerEquipe).mockResolvedValue(undefined);
    rendre(utilisateur({ estAdmin: true }), '/admin?onglet=equipes');
    expect(await screen.findByText(/désactivé/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer Exploitation' }));
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer' }));
    await waitFor(() => expect(api.adminSupprimerEquipe).toHaveBeenCalledWith('e1'));
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
