import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type * as moduleApi from '../../api';
import { api } from '../../api';
import type { CatalogueConnexions, ConnexionApi } from '../../types';
import { ToastProvider } from '../../ui/Toast';
import { TooltipProvider } from '../../ui/Tooltip';
import ConnexionEditeurPage from './ConnexionEditeurPage';

vi.mock('../../auth', () => ({ useAuth: () => ({ utilisateur: { id: 'u1', estAdmin: true } }) }));

vi.mock('../../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: {
    connexionApi: vi.fn(),
    catalogueConnexions: vi.fn(),
    equipes: vi.fn(async () => []),
    catalogueSystemes: vi.fn(async () => []),
    meps: vi.fn(async () => []),
    apercuConnexion: vi.fn(() => new Promise(() => undefined)),
    estimerConnexion: vi.fn(async () => ({ appels: 0, meps: 0, jours: 30 })),
    modifierConnexionApi: vi.fn(),
    creerConnexionApi: vi.fn(),
    activerConnexionApi: vi.fn(),
    essayerConnexion: vi.fn(),
  },
}));

const catalogue: CatalogueConnexions = {
  declencheurs: [{ cle: 'etape.echec', libelle: 'Étape en échec', groupe: 'Exécution', frequent: false }],
  modeles: [],
  variables: [
    { cle: 'evenement', libelle: 'Événement', groupe: 'Événement', description: '', liste: false },
    { cle: 'pilote', libelle: 'Pilote', groupe: 'MEP', description: '', liste: false },
  ],
  version: '1.0',
  reglages: [],
};

const connexion: ConnexionApi = {
  id: 'c1',
  nom: 'Supervision',
  description: '',
  active: true,
  version: 3,
  modele: 'WebhookJson',
  methode: 'Post',
  url: 'https://supervision.example.com/evenements',
  urlSecrete: false,
  adresseSecrete: null,
  authentification: 'Aucune',
  utilisateur: '',
  nomEntete: '',
  tokenUrl: '',
  clientId: '',
  scope: '',
  secret: null,
  entetes: [],
  signature: false,
  secretSignature: null,
  format: 'Json',
  corps: '{"evenement": "{{evenement}}"}',
  champs: [],
  delaiSecondes: 10,
  tentatives: 3,
  declencheurs: ['etape.echec'],
  filtres: {
    equipeIds: [],
    systemeCatalogueIds: [],
    environnements: [],
    niveauxRisque: [],
    criticitesEtape: [],
    typesMep: [],
  },
  envoiManuel: false,
  libelleEnvoiManuel: '',
  creeeLe: '2026-10-01T08:00:00Z',
  modifieeLe: '2026-10-01T08:00:00Z',
  modifieePar: 'Alice',
  etat: 'Active',
  dernierAppel: null,
};

function rendre() {
  render(
    <MemoryRouter initialEntries={['/admin/api/c1']}>
      <TooltipProvider>
        <ToastProvider>
          <Routes>
            <Route path="/admin/api/:id" element={<ConnexionEditeurPage />} />
          </Routes>
        </ToastProvider>
      </TooltipProvider>
    </MemoryRouter>,
  );
}

describe('Éditeur de connexion', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.connexionApi).mockResolvedValue(connexion);
    vi.mocked(api.catalogueConnexions).mockResolvedValue(catalogue);
  });

  it('un JSON invalide est signalé à sa ligne et bloque l’enregistrement', async () => {
    rendre();
    const message = await screen.findByRole('combobox', { name: 'Message' });
    expect(screen.queryByText(/JSON invalide/)).not.toBeInTheDocument();
    await userEvent.type(message, ',');
    expect(await screen.findByText(/JSON invalide, ligne 1/)).toBeInTheDocument();
    expect(message).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Mettre en forme le JSON' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(await screen.findByText(/Le message est invalide : corrigez-le avant d’enregistrer/)).toBeInTheDocument();
    expect(api.modifierConnexionApi).not.toHaveBeenCalled();
  });

  it('une variable inconnue propose la correction et bloque l’enregistrement', async () => {
    rendre();
    const message = await screen.findByRole('combobox', { name: 'Message' });
    await userEvent.clear(message);
    await userEvent.type(message, '{{"p": "{{{{pilot}}"}');
    await userEvent.keyboard('{Escape}');
    expect(await screen.findByText(/Vouliez-vous dire/)).toHaveTextContent('{{pilote}}');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierConnexionApi).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Corriger' }));
    expect(message).toHaveValue('{"p": "{{pilote}}"}');
  });
});
