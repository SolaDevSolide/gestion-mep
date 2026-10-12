import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { Utilisateur } from '../types';
import { BandeauDemo, URL_APRES_REINITIALISATION } from './BandeauDemo';

vi.mock('../api', () => ({ api: { demo: vi.fn(), reinitialiserDemo: vi.fn() } }));

const actif = { actif: true, comptes: [] };

const assign = vi.fn();

function rendre(u: Utilisateur | null) {
  render(
    <AuthContext.Provider
      value={{ utilisateur: u, chargement: false, connecter: vi.fn(), deconnecter: vi.fn(), majUtilisateur: vi.fn() }}
    >
      <BandeauDemo recharger={assign} />
    </AuthContext.Provider>,
  );
}

describe('Bandeau du mode démonstration', () => {
  beforeEach(() => vi.clearAllMocks());

  afterEach(() => {
    document.documentElement.style.removeProperty('--bandeau-demo');
  });

  it('s’affiche, accessible, quand la démonstration est active (même sans session)', async () => {
    vi.mocked(api.demo).mockResolvedValue(actif);
    rendre(null);
    const bandeau = await screen.findByRole('status', { name: 'Mode démonstration' });
    expect(bandeau).toHaveTextContent('Mode démonstration — données fictives, réinitialisables');
    expect(document.documentElement.style.getPropertyValue('--bandeau-demo')).toBe('2.25rem');
    expect(screen.queryByRole('button', { name: /Réinitialiser/ })).toBeNull();
  });

  it('n’affiche rien hors démonstration', async () => {
    vi.mocked(api.demo).mockResolvedValue(null);
    rendre(utilisateur({ estAdmin: true }));
    await waitFor(() => expect(api.demo).toHaveBeenCalled());
    expect(screen.queryByRole('status', { name: 'Mode démonstration' })).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--bandeau-demo')).toBe('');
  });

  it('ne propose pas la réinitialisation à un non-administrateur', async () => {
    vi.mocked(api.demo).mockResolvedValue(actif);
    rendre(utilisateur({ estAdmin: false }));
    await screen.findByRole('status', { name: 'Mode démonstration' });
    expect(screen.queryByRole('button', { name: /Réinitialiser/ })).toBeNull();
  });

  it('réinitialise après confirmation dans la page, puis recharge sur l’écran de connexion', async () => {
    vi.mocked(api.demo).mockResolvedValue(actif);
    vi.mocked(api.reinitialiserDemo).mockResolvedValue({ meps: 8, fichiersSupprimes: 0 });
    const confirmer = vi.spyOn(window, 'confirm');
    rendre(utilisateur({ estAdmin: true }));
    await userEvent.click(await screen.findByRole('button', { name: /Réinitialiser/ }));
    const dialogue = await screen.findByRole('dialog', { name: 'Réinitialiser la démonstration ?' });
    expect(dialogue).toHaveTextContent('Tout le monde est déconnecté');
    expect(api.reinitialiserDemo).not.toHaveBeenCalled();

    // Annuler ne fait rien.
    await userEvent.click(screen.getByRole('button', { name: 'Annuler' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(api.reinitialiserDemo).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: /Réinitialiser/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Réinitialiser' }));
    await waitFor(() => expect(api.reinitialiserDemo).toHaveBeenCalledTimes(1));
    expect(assign).toHaveBeenCalledWith(URL_APRES_REINITIALISATION);
    expect(confirmer).not.toHaveBeenCalled();
  });

  it('garde la boîte ouverte et affiche l’erreur si le serveur refuse', async () => {
    vi.mocked(api.demo).mockResolvedValue(actif);
    vi.mocked(api.reinitialiserDemo).mockRejectedValue(new Error('Réservé aux administrateurs.'));
    rendre(utilisateur({ estAdmin: true }));
    await userEvent.click(await screen.findByRole('button', { name: /Réinitialiser/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Réinitialiser' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Réservé aux administrateurs.');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
  });
});
