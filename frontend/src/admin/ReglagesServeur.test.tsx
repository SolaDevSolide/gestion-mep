import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import type { Reglage } from '../types';
import { ToastProvider } from '../ui/Toast';
import ReglagesServeur from './ReglagesServeur';

vi.mock('../api', () => ({
  api: { reglages: vi.fn(), definirReglage: vi.fn(), retablirReglage: vi.fn(), stockagePiecesJointes: vi.fn() },
}));

const base: Omit<Reglage, 'nom' | 'variable' | 'libelle' | 'type' | 'valeur' | 'origine' | 'defaut'> = {
  groupe: 'Adresse et réseau',
  aide: '',
  min: null,
  max: null,
  valeurFichier: null,
  modifieLe: null,
  modifiePar: null,
  alertes: [],
};

const urlPublique: Reglage = {
  ...base,
  nom: 'url-publique',
  variable: 'URL_PUBLIQUE',
  libelle: 'Adresse publique',
  type: 'Adresse',
  valeur: '',
  origine: 'defaut',
  defaut: '',
};

const https: Reglage = {
  ...base,
  nom: 'https-obligatoire',
  variable: 'HTTPS_OBLIGATOIRE',
  libelle: 'HTTPS obligatoire',
  type: 'Booleen',
  valeur: 'false',
  origine: 'console',
  defaut: 'true',
  modifieLe: '2026-10-11T21:00:00Z',
  modifiePar: 'Alice',
  alertes: ['Mode d’essai : la session fonctionne en http simple.'],
};

function rendre() {
  render(
    <ToastProvider>
      <ReglagesServeur />
    </ToastProvider>,
  );
}

describe('Réglages du serveur', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.reglages).mockResolvedValue([urlPublique, https]);
    vi.mocked(api.stockagePiecesJointes).mockResolvedValue({
      pieces: 12,
      tailleFichiers: 40 * 1024 * 1024,
      tailleStockee: 10 * 1024 * 1024,
      compressees: 8,
      nonExaminees: 3,
    });
  });

  it('rappelle la place occupée par les pièces jointes et le gain de la compression', async () => {
    const compression: Reglage = {
      ...base,
      groupe: 'Pièces jointes',
      nom: 'pieces-compression',
      variable: 'PIECES_COMPRESSION',
      libelle: 'Compresser les fichiers déposés',
      type: 'Booleen',
      valeur: 'true',
      origine: 'defaut',
      defaut: 'true',
    };
    vi.mocked(api.reglages).mockResolvedValue([urlPublique, compression]);
    rendre();
    expect(
      await screen.findByText(
        /12 pièces jointes : 40 Mo de fichiers, 10 Mo sur le serveur \(−75 % grâce à la compression\)/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/3 déposées avant la compression/)).toBeInTheDocument();
    expect(screen.getByText(/pieces-jointes compresser/)).toBeInTheDocument();
  });

  it('enregistre l’adresse publique et affiche sa nouvelle origine', async () => {
    vi.mocked(api.definirReglage).mockResolvedValue({
      ...urlPublique,
      valeur: 'https://mep.exemple.fr',
      origine: 'console',
      modifieLe: '2026-10-11T21:05:00Z',
      modifiePar: 'Alice',
    });
    rendre();
    const champ = await screen.findByLabelText('Adresse publique');
    expect(screen.getByText('Valeur par défaut')).toBeInTheDocument();
    await userEvent.type(champ, 'https://mep.exemple.fr');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(api.definirReglage).toHaveBeenCalledWith('url-publique', 'https://mep.exemple.fr'));
    expect(await screen.findAllByText(/Modifié le .* par Alice/)).toHaveLength(2);
  });

  it('signale le mode d’essai et permet de rétablir la valeur', async () => {
    vi.mocked(api.retablirReglage).mockResolvedValue({ ...https, valeur: 'true', origine: 'defaut', alertes: [] });
    rendre();
    expect(await screen.findByText(/Mode d’essai/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Rétablir' }));
    await waitFor(() => expect(api.retablirReglage).toHaveBeenCalledWith('https-obligatoire'));
    await waitFor(() => expect(screen.queryByText(/Mode d’essai/)).not.toBeInTheDocument());
  });
});
