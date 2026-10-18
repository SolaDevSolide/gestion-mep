import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { appliquerEmpreinte } from '../charte/appliquer';
import type { EtatCharteServeur } from '../types';
import { ToastProvider } from '../ui/Toast';
import CharteTab from './CharteTab';

vi.mock('../api', () => ({
  api: { charte: vi.fn(), enregistrerCharte: vi.fn(), retablirCharte: vi.fn(), verifierCharte: vi.fn() },
}));
vi.mock('../charte/appliquer', () => ({ appliquerEmpreinte: vi.fn() }));

const fichierAbsent: EtatCharteServeur['fichier'] = {
  chemin: '/etc/gestion-mep/charte.json',
  present: false,
  erreur: null,
  erreurs: [],
  avertissements: [],
  charte: null,
};

const defaut: EtatCharteServeur = {
  charte: {
    version: 1,
    nom: 'Charte par défaut',
    couleurs: { primaire: '#4F46E5', secondaire: '#0D0E1A', tertiaire: 'primaire' },
    menuLateral: 'pages',
    gris: 'froids',
  },
  origine: 'defaut',
  empreinte: 'e3b0c44298fc',
  modifieLe: null,
  modifiePar: null,
  fichier: fichierAbsent,
  avertissements: [],
  commande: 'docker compose exec app gestion-mep charte exporter',
};

const horizon: EtatCharteServeur = {
  ...defaut,
  charte: {
    ...defaut.charte,
    nom: 'Groupe Horizon',
    couleurs: { primaire: '#0F766E', secondaire: '#1B2A41', tertiaire: 'primaire' },
  },
  origine: 'console',
  empreinte: '40f78de39f99',
  modifieLe: '2026-10-17T09:02:00Z',
  modifiePar: 'Alice',
};

function rendre() {
  render(
    <ToastProvider>
      <CharteTab />
    </ToastProvider>,
  );
}

async function choisir(bouton: string, hex: string) {
  await userEvent.click(screen.getByRole('button', { name: bouton }));
  const champ = await screen.findByLabelText('Code hexadécimal');
  await userEvent.clear(champ);
  await userEvent.type(champ, hex);
  await userEvent.keyboard('{Escape}');
}

describe('Console › Charte visuelle', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    vi.mocked(api.charte).mockResolvedValue(defaut);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('enregistre la couleur choisie et applique la nouvelle charte dans l’onglet', async () => {
    vi.mocked(api.enregistrerCharte).mockResolvedValue(horizon);
    rendre();
    expect(await screen.findByText('Valeur par défaut')).toBeInTheDocument();
    expect(screen.getByText('Tout est lisible')).toBeInTheDocument();
    expect(screen.queryByText('Modifications non enregistrées.')).toBeNull();

    await choisir('Choisir la couleur primaire', '0F766E');
    expect(screen.getByText('Modifications non enregistrées.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(api.enregistrerCharte).toHaveBeenCalledTimes(1));
    expect(vi.mocked(api.enregistrerCharte).mock.calls[0][0]).toMatchObject({
      version: 1,
      couleurs: { primaire: '#0F766E', secondaire: '#0D0E1A', tertiaire: 'primaire' },
    });
    expect(appliquerEmpreinte).toHaveBeenCalledWith('40f78de39f99');
    expect(await screen.findByText(/Modifiée le .* par Alice/)).toBeInTheDocument();
    expect(screen.queryByText('Modifications non enregistrées.')).toBeNull();
  });

  it('signale un contraste trop faible sans bloquer, et « Corriger » propose une couleur qui passe', async () => {
    rendre();
    await screen.findByText('Valeur par défaut');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Identique à la primaire' }));
    await choisir('Choisir la couleur tertiaire', 'F0A6F5');
    // Sur la ligne de la tertiaire et en tête du contrôle de lisibilité.
    expect(screen.getAllByText('1 à revoir')).toHaveLength(2);
    const ligne = screen.getByText('Cadre des sections sur le fond du plan').closest('li') as HTMLElement;
    await userEvent.click(within(ligne).getByRole('button', { name: /Corriger/ }));
    expect(screen.getByText('Tout est lisible')).toBeInTheDocument();
    // Rien n'est bloqué : l'enregistrement reste possible avec ou sans correction.
    expect(screen.getByRole('button', { name: 'Enregistrer' })).toBeEnabled();
  });

  it('« Charte par défaut » efface la charte de la console après confirmation', async () => {
    vi.mocked(api.charte).mockResolvedValue(horizon);
    vi.mocked(api.retablirCharte).mockResolvedValue(defaut);
    rendre();
    await screen.findByText(/Modifiée le/);
    await userEvent.click(screen.getByRole('button', { name: 'Charte par défaut' }));
    const dialogue = await screen.findByRole('dialog');
    expect(dialogue).toHaveTextContent('les couleurs par défaut s’appliquent de nouveau, pour tous');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Revenir à la charte par défaut' }));
    await waitFor(() => expect(api.retablirCharte).toHaveBeenCalledTimes(1));
    expect(appliquerEmpreinte).toHaveBeenCalledWith('e3b0c44298fc');
    expect(await screen.findByText('Valeur par défaut')).toBeInTheDocument();
  });

  it('importe un fichier vérifié par le serveur, sans rien enregistrer avant « Enregistrer »', async () => {
    vi.mocked(api.verifierCharte).mockResolvedValue({
      valide: true,
      erreurs: [],
      avertissements: [
        { code: 'cle-inconnue', champ: 'police', message: 'Clé inconnue ignorée : « police » (ligne 4).', ligne: 4 },
      ],
      charte: { ...horizon.charte, menuLateral: 'secondaire' },
    });
    rendre();
    await screen.findByText('Valeur par défaut');
    await userEvent.click(screen.getByRole('button', { name: 'Importer…' }));
    const dialogue = await screen.findByRole('dialog');
    const fichier = new File(['{"couleurs":{"primaire":"#0F766E"},"police":"Marianne"}'], 'horizon.json', {
      type: 'application/json',
    });
    await userEvent.upload(within(dialogue).getByLabelText('Fichier charte.json'), fichier);
    expect(await within(dialogue).findByText('Fichier valide')).toBeInTheDocument();
    expect(api.verifierCharte).toHaveBeenCalledWith('{"couleurs":{"primaire":"#0F766E"},"police":"Marianne"}');
    expect(within(dialogue).getByText(/Clé inconnue ignorée : « police » \(ligne 4\)/)).toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Reprendre ces couleurs' }));

    expect(screen.getByText('Modifications non enregistrées.')).toBeInTheDocument();
    expect(screen.getAllByText('#0f766e', { selector: 'code' }).length).toBeGreaterThan(0);
    expect(screen.getByRole('radio', { name: 'Secondaire' })).toHaveAttribute('aria-checked', 'true');
    expect(api.enregistrerCharte).not.toHaveBeenCalled();
  });

  it('montre un fichier charte.json invalide, ignoré', async () => {
    vi.mocked(api.charte).mockResolvedValue({
      ...defaut,
      fichier: {
        ...fichierAbsent,
        present: true,
        erreur: 'menuLateral : « pages », « secondaire » attendu (ligne 4).',
      },
    });
    rendre();
    const alerte = await screen.findByRole('alert');
    expect(alerte).toHaveTextContent('Fichier charte.json invalide, ignoré');
    expect(alerte).toHaveTextContent('La charte par défaut s’applique');
    expect(screen.getByText('Fichier ignoré (invalide)')).toBeInTheDocument();
  });
});
