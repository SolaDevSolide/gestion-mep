import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../api';
import { api } from '../api';
import { jetonApi, jours, PORTEES_JETONS } from '../test/fabrique';
import { ToastProvider } from '../ui/Toast';
import { JetonsPersonnels } from './JetonsPersonnels';

vi.mock('../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: { mesJetons: vi.fn(), porteesJetons: vi.fn(), creerJeton: vi.fn(), revoquerMonJeton: vi.fn() },
}));

function rendre() {
  render(
    <ToastProvider>
      <MemoryRouter>
        <JetonsPersonnels />
      </MemoryRouter>
    </ToastProvider>,
  );
}

const SECRET = ['gmep', 'AbCdEfGh0123456789'].join('_');

describe('Mon profil › Jetons d’accès personnels', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Cases à cocher de Radix : elles mesurent leur taille.
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    vi.mocked(api.porteesJetons).mockResolvedValue(PORTEES_JETONS);
  });

  it('liste ses jetons : actifs en tête, rappel avant expiration, révoqués et expirés récents atténués', async () => {
    vi.mocked(api.mesJetons).mockResolvedValue([
      jetonApi({ id: 'vieux', nom: 'Script recette', etat: 'Expire', expireLe: jours(-13), portees: ['execution'] }),
      jetonApi({ id: 'oublie', nom: 'Très ancien', etat: 'Revoque', revoqueLe: jours(-60) }),
      jetonApi({
        id: 'bientot',
        nom: 'Export catalogue',
        expireLe: jours(5.5),
        portees: ['meps:lecture', 'catalogue:lecture'],
        derniereUtilisationLe: jours(-2 / 24),
        derniereAdresseIp: '10.20.4.17',
      }),
    ]);
    rendre();
    const lignes = await screen.findAllByRole('row');
    // En-tête, puis l'actif, puis l'expiré ; le révoqué il y a 60 jours n'est plus montré.
    expect(lignes).toHaveLength(3);
    expect(within(lignes[1]).getByText('Export catalogue')).toBeInTheDocument();
    expect(within(lignes[1]).getByText('dans 6 jours')).toBeInTheDocument();
    expect(within(lignes[1]).getByText('il y a 2 h')).toBeInTheDocument();
    expect(within(lignes[1]).getByText('10.20.4.17')).toBeInTheDocument();
    expect(within(lignes[1]).getByRole('button', { name: 'Recréer « Export catalogue »' })).toBeInTheDocument();
    expect(within(lignes[2]).getByText(/Expiré le/)).toBeInTheDocument();
    expect(within(lignes[2]).queryByRole('button', { name: /Révoquer/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Très ancien')).not.toBeInTheDocument();
  });

  it('affiche un état vide quand il n’y a aucun jeton', async () => {
    vi.mocked(api.mesJetons).mockResolvedValueOnce([]);
    rendre();
    expect(await screen.findByText('Aucun jeton')).toBeInTheDocument();
  });

  it('signale une erreur de chargement', async () => {
    vi.mocked(api.mesJetons).mockRejectedValueOnce(new Error('Serveur indisponible'));
    rendre();
    expect(await screen.findByText(/Impossible de charger vos jetons/)).toBeInTheDocument();
  });

  it('crée un jeton puis le montre une seule fois, à copier ; fermer sans copier demande confirmation', async () => {
    vi.mocked(api.mesJetons).mockResolvedValue([]);
    vi.mocked(api.creerJeton).mockResolvedValue({
      jeton: jetonApi({ nom: 'Grafana', portees: ['meps:lecture', 'catalogue:lecture'] }),
      secret: SECRET,
    });
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Nouveau jeton' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouveau jeton personnel' });
    // Pas de portée admin pour un jeton personnel ; meps:lecture cochée par défaut.
    expect(within(dialogue).queryByText('admin', { selector: 'code' })).toBeInTheDocument();
    expect(within(dialogue).queryByRole('checkbox', { name: /Administration/ })).not.toBeInTheDocument();
    expect(within(dialogue).getByRole('checkbox', { name: /Lire les MEP/ })).toBeChecked();
    expect(within(dialogue).getByText(/la portée n’ajoutera rien/)).toBeInTheDocument();

    await user.click(within(dialogue).getByRole('button', { name: 'Créer le jeton' }));
    expect(await within(dialogue).findByText('Le nom est obligatoire.')).toBeInTheDocument();
    expect(api.creerJeton).not.toHaveBeenCalled();

    await user.type(within(dialogue).getByLabelText('Nom *'), 'Grafana');
    await user.click(within(dialogue).getByRole('checkbox', { name: /Lire le catalogue/ }));
    await user.selectOptions(within(dialogue).getByLabelText('Expiration'), '180');
    await user.click(within(dialogue).getByRole('button', { name: 'Créer le jeton' }));
    expect(api.creerJeton).toHaveBeenCalledWith({
      nom: 'Grafana',
      portees: ['meps:lecture', 'catalogue:lecture'],
      dureeJours: 180,
    });

    const cree = await screen.findByRole('dialog', { name: 'Jeton créé' });
    expect(within(cree).getByText(SECRET)).toBeInTheDocument();
    expect(within(cree).getByText(/il ne sera plus jamais affiché/)).toBeInTheDocument();
    expect(within(cree).getByText(/Authorization: Bearer/)).toBeInTheDocument();

    // Fermer sans avoir copié : confirmation.
    await user.click(within(cree).getByRole('button', { name: 'J’ai copié le jeton' }));
    const confirmation = await screen.findByRole('dialog', { name: 'Fermer sans copier le jeton ?' });
    await user.click(within(confirmation).getByRole('button', { name: 'Annuler' }));

    // Après la copie, la fermeture est directe.
    await user.click(within(cree).getByRole('button', { name: 'Copier' }));
    expect(await navigator.clipboard.readText()).toBe(SECRET);
    await user.click(within(cree).getByRole('button', { name: 'J’ai copié le jeton' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('« Recréer » reprend le nom et les portées ; une date d’expiration au-delà d’un an est refusée', async () => {
    vi.mocked(api.mesJetons).mockResolvedValue([
      jetonApi({ nom: 'Export catalogue', expireLe: jours(3), portees: ['catalogue:lecture'] }),
    ]);
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Recréer « Export catalogue »' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Recréer « Export catalogue »' });
    expect(within(dialogue).getByLabelText('Nom *')).toHaveValue('Export catalogue');
    expect(within(dialogue).getByRole('checkbox', { name: /Lire le catalogue/ })).toBeChecked();
    expect(within(dialogue).getByRole('checkbox', { name: /Lire les MEP/ })).not.toBeChecked();
    await user.selectOptions(within(dialogue).getByLabelText('Expiration'), 'date');
    const date = within(dialogue).getByLabelText('Expire le');
    await user.clear(date);
    await user.type(date, '2099-01-01');
    expect(within(dialogue).getByText('Choisissez une date entre demain et dans un an.')).toBeInTheDocument();
  });

  it('révoque un jeton après confirmation', async () => {
    vi.mocked(api.mesJetons).mockResolvedValue([
      jetonApi({ id: 'j9', nom: 'Rapport', derniereUtilisationLe: jours(-1.2), derniereAdresseIp: '10.1.1.1' }),
    ]);
    vi.mocked(api.revoquerMonJeton).mockResolvedValue(jetonApi({ id: 'j9', etat: 'Revoque', revoqueLe: jours(0) }));
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Révoquer « Rapport »' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Révoquer « Rapport » ?' });
    expect(within(dialogue).getByText(/recevront aussitôt/)).toBeInTheDocument();
    expect(within(dialogue).getByText('10.1.1.1')).toBeInTheDocument();
    await user.click(within(dialogue).getByRole('button', { name: 'Révoquer' }));
    expect(api.revoquerMonJeton).toHaveBeenCalledWith('j9');
    await waitFor(() => expect(api.mesJetons).toHaveBeenCalledTimes(2));
  });
});
