import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import type { SsoReglages } from '../types';
import { ToastProvider } from '../ui/Toast';
import SsoTab from './SsoTab';

vi.mock('../api', () => ({
  api: {
    ssoReglages: vi.fn(),
    ssoEnregistrer: vi.fn(),
    ssoActiver: vi.fn(),
    ssoDesactiver: vi.fn(),
    ssoTester: vi.fn(),
    ssoIdentites: vi.fn(),
    ssoDelier: vi.fn(),
  },
}));

const reglages: SsoReglages = {
  active: false,
  fournisseur: 'Microsoft',
  annuaire: 'tenant-1',
  autorite: '',
  clientId: 'client-1',
  secretEnregistre: true,
  secretLisible: true,
  secretExpireLe: null,
  creationAutomatique: false,
  roleAdmin: '',
  libelle: '',
  libelleEffectif: 'Se connecter avec Microsoft',
  urlRetour: 'https://mep.exemple.fr/api/auth/sso/retour',
  modifieeLe: null,
  anomalies: [],
};

function rendre() {
  render(
    <ToastProvider>
      <SsoTab />
    </ToastProvider>,
  );
}

describe('Onglet Connexion unique', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.ssoReglages).mockResolvedValue(reglages);
    vi.mocked(api.ssoIdentites).mockResolvedValue([]);
  });

  it('enregistre sans envoyer le secret quand le champ est laissé vide', async () => {
    vi.mocked(api.ssoEnregistrer).mockResolvedValue({ ...reglages, creationAutomatique: true });
    rendre();
    await userEvent.click(await screen.findByRole('checkbox', { name: /Créer le compte à la première connexion/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(api.ssoEnregistrer).toHaveBeenCalled());
    const envoye = vi.mocked(api.ssoEnregistrer).mock.calls[0][0];
    expect(envoye).toMatchObject({
      fournisseur: 'Microsoft',
      annuaire: 'tenant-1',
      clientId: 'client-1',
      creationAutomatique: true,
    });
    expect(envoye).not.toHaveProperty('secret');
  });

  it('envoie le secret saisi et affiche l’erreur du serveur', async () => {
    vi.mocked(api.ssoEnregistrer).mockRejectedValue(new Error('ID d’annuaire invalide.'));
    rendre();
    await userEvent.type(await screen.findByLabelText('Secret client'), 'nouveau-secret');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(await screen.findByText('ID d’annuaire invalide.')).toBeInTheDocument();
    expect(vi.mocked(api.ssoEnregistrer).mock.calls[0][0]).toMatchObject({ secret: 'nouveau-secret' });
  });

  it('affiche le détail de chaque vérification du test', async () => {
    vi.mocked(api.ssoTester).mockResolvedValue({
      ok: false,
      verifications: [
        { libelle: 'Découverte', ok: true, detail: 'Document trouvé' },
        { libelle: 'Secret', ok: false, detail: 'Secret refusé' },
      ],
    });
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Tester' }));
    expect(await screen.findByText('Le test a échoué')).toBeInTheDocument();
    expect(screen.getByText(/Document trouvé/)).toBeInTheDocument();
    expect(screen.getByText(/Secret refusé/)).toBeInTheDocument();
  });

  it('bloque l’activation tant qu’il reste des anomalies', async () => {
    vi.mocked(api.ssoReglages).mockResolvedValue({ ...reglages, anomalies: ['Le secret est absent.'] });
    rendre();
    expect(await screen.findByText('Le secret est absent.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Activer' })).toBeDisabled();
  });

  it('liste les comptes liés et les délie après confirmation', async () => {
    vi.mocked(api.ssoIdentites).mockResolvedValue([
      {
        id: 'i1',
        utilisateurId: 'u1',
        login: 'alice',
        nomAffiche: 'Alice',
        identifiant: 'oid-123',
        creeeLe: '2026-01-01T10:00:00Z',
        derniereConnexion: null,
      },
    ]);
    vi.mocked(api.ssoDelier).mockResolvedValue(undefined);
    rendre();
    expect(await screen.findByText('oid-123')).toBeInTheDocument();
    expect(screen.getByText('jamais')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Délier le compte alice' }));
    await userEvent.click(screen.getAllByRole('button', { name: 'Délier' }).at(-1)!);
    await waitFor(() => expect(api.ssoDelier).toHaveBeenCalledWith('i1'));
  });
});
