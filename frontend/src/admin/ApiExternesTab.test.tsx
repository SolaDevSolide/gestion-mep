import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../api';
import { api } from '../api';
import type { AppelResume, CatalogueConnexions, ConnexionApiResume } from '../types';
import { ToastProvider } from '../ui/Toast';
import ApiExternesTab from './ApiExternesTab';

vi.mock('../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: {
    connexionsApi: vi.fn(),
    catalogueConnexions: vi.fn(),
    appelsApi: vi.fn(),
    appelApi: vi.fn(),
    activerConnexionApi: vi.fn(),
    supprimerConnexionApi: vi.fn(),
    application: vi.fn(async () => ({
      version: '1.0',
      lienSignalement: null,
      miseAJour: null,
      installation: 'docker',
    })),
  },
}));

const catalogue: CatalogueConnexions = {
  declencheurs: [
    { cle: 'gonogo.go', libelle: 'Go donné', groupe: 'Préparation', frequent: false },
    { cle: 'etape.echec', libelle: 'Étape en échec', groupe: 'Exécution', frequent: false },
  ],
  modeles: [],
  variables: [],
  version: '1.0',
  reglages: [],
};

const appelEchoue: AppelResume = {
  id: 'a1',
  connexionId: 'c1',
  connexion: 'Canal Teams',
  mepId: 'm1',
  mepNom: 'Paiement mobile 4.2',
  typeEvenement: 'etape.echec',
  libelleEvenement: 'Étape en échec',
  resume: 'E0005 en échec',
  origine: 'Automatique',
  demandePar: '',
  etat: 'Echec',
  code: 503,
  tentatives: 3,
  tentativesMax: 3,
  dureeMs: 120,
  creeLe: '2026-10-29T08:00:00Z',
  termineLe: '2026-10-29T08:12:30Z',
  prochaineTentative: null,
  erreur: 'Service indisponible',
};

function connexion(p: Partial<ConnexionApiResume> = {}): ConnexionApiResume {
  return {
    id: 'c1',
    nom: 'Canal Teams',
    description: '',
    active: true,
    modele: 'Teams',
    methode: 'Post',
    adresse: 'https://exemple.webhook.office.com',
    declencheurs: ['gonogo.go', 'etape.echec'],
    envoiManuel: true,
    libelleEnvoiManuel: 'Prévenir le canal',
    etat: 'Active',
    dernierAppel: null,
    dernierEssai: null,
    ...p,
  };
}

function rendre() {
  render(
    <MemoryRouter>
      <ToastProvider>
        <ApiExternesTab />
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('Onglet API externes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.catalogueConnexions).mockResolvedValue(catalogue);
    vi.mocked(api.appelsApi).mockResolvedValue({ appels: [], suivant: null, echecs: 0 });
  });

  it('liste les connexions avec leurs déclencheurs et signale celles en erreur', async () => {
    vi.mocked(api.connexionsApi).mockResolvedValue([
      connexion({ etat: 'EnErreur', dernierAppel: appelEchoue }),
      connexion({ id: 'c2', nom: 'Supervision', modele: 'WebhookJson', declencheurs: [], envoiManuel: false }),
    ]);
    rendre();
    expect(await screen.findByRole('link', { name: 'Canal Teams' })).toHaveAttribute('href', '/admin/api/c1');
    expect(screen.getByText('Go donné')).toBeInTheDocument();
    expect(screen.getByText('Envoi depuis une MEP')).toBeInTheDocument();
    const alerte = screen.getByRole('alert');
    expect(within(alerte).getByText('1 connexion en erreur')).toBeInTheDocument();
    expect(alerte).toHaveTextContent('« Canal Teams » échoue');
    expect(alerte).toHaveTextContent('Service indisponible');
  });

  it('sans connexion : état vide et bouton de création', async () => {
    vi.mocked(api.connexionsApi).mockResolvedValue([]);
    rendre();
    expect(await screen.findByText('Aucune connexion')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Nouvelle connexion' })).toHaveLength(2);
  });

  it('liste en erreur : carte d’erreur avec « Réessayer »', async () => {
    vi.mocked(api.connexionsApi).mockRejectedValueOnce(new Error('Serveur injoignable'));
    rendre();
    expect(await screen.findByText('Impossible de charger les connexions')).toBeInTheDocument();
    vi.mocked(api.connexionsApi).mockResolvedValue([connexion()]);
    await userEvent.click(screen.getByRole('button', { name: /Réessayer/ }));
    expect(await screen.findByRole('link', { name: 'Canal Teams' })).toBeInTheDocument();
  });

  it('un échec des derniers appels laisse la liste utilisable', async () => {
    vi.mocked(api.connexionsApi).mockResolvedValue([connexion()]);
    vi.mocked(api.appelsApi).mockRejectedValue(new Error('Délai dépassé'));
    rendre();
    expect(await screen.findByText('Impossible de charger les derniers appels')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Canal Teams' })).toBeInTheDocument();
    expect(screen.queryByText('Impossible de charger les connexions')).not.toBeInTheDocument();
  });

  it('le filtre « Échecs » recharge les appels en échec', async () => {
    vi.mocked(api.connexionsApi).mockResolvedValue([connexion()]);
    rendre();
    await screen.findByRole('link', { name: 'Canal Teams' });
    await userEvent.click(screen.getByRole('radio', { name: /Échecs/ }));
    await waitFor(() => expect(api.appelsApi).toHaveBeenLastCalledWith({ etat: 'Echec', limite: 20 }));
  });
});
