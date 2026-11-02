import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../api';
import { api } from '../api';
import { jetonApi, jours, PORTEES_JETONS } from '../test/fabrique';
import type { Reglage } from '../types';
import { ToastProvider } from '../ui/Toast';
import JetonsTab from './JetonsTab';

vi.mock('../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: {
    jetons: vi.fn(),
    porteesJetons: vi.fn(),
    reglages: vi.fn(),
    creerJetonService: vi.fn(),
    revoquerJeton: vi.fn(),
  },
}));

const SECRET = ['gmep', 'Xw4sZZZZ0123456789'].join('_');

const ci = jetonApi({
  id: 's1',
  nom: 'Chaîne CI GitLab',
  type: 'Service',
  portees: ['meps:lecture', 'execution'],
  compteId: 'c-ci',
  compteLogin: 'svc-chaine-ci-gitlab',
  compteNom: 'Chaîne CI GitLab',
  expireLe: jours(300),
  derniereUtilisationLe: jours(-4 / 1440),
  derniereAdresseIp: '10.30.1.8',
});
const cmdb = jetonApi({
  id: 's2',
  nom: 'Import CMDB',
  type: 'Service',
  portees: ['catalogue:ecriture', 'admin'],
  compteId: 'c-cmdb',
  compteLogin: 'svc-import-cmdb',
  compteNom: 'Import CMDB',
  expireLe: jours(27.5),
  requetesParMinute: 1200,
});
const bob = jetonApi({ id: 'p1', nom: 'Collection Postman', compteId: 'u-bob', compteLogin: 'bob', compteNom: 'Bob' });
const alice = jetonApi({ id: 'p2', nom: 'Rapport hebdo', derniereUtilisationLe: jours(-0.1) });

function rendre() {
  render(
    <ToastProvider>
      <MemoryRouter>
        <JetonsTab />
      </MemoryRouter>
    </ToastProvider>,
  );
}

describe('Console › Jetons d’API', () => {
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
    vi.mocked(api.jetons).mockResolvedValue([ci, cmdb, bob, alice]);
    vi.mocked(api.porteesJetons).mockResolvedValue(PORTEES_JETONS);
    vi.mocked(api.reglages).mockResolvedValue([{ nom: 'jetons-par-minute', valeur: '600' } as Reglage]);
  });

  it('sépare les jetons de service (compte technique, débit, rappel 30 jours avant) des jetons personnels', async () => {
    rendre();
    const tables = await screen.findAllByRole('table');
    expect(tables).toHaveLength(2);
    const [service, personnels] = tables;
    const ligneCi = within(service).getByText('Chaîne CI GitLab').closest('tr')!;
    expect(within(ligneCi).getByText('svc-chaine-ci-gitlab')).toBeInTheDocument();
    expect(within(ligneCi).getByText('Technique')).toBeInTheDocument();
    expect(within(ligneCi).getByText('il y a 4 min')).toBeInTheDocument();
    expect(within(ligneCi).getByText('Par défaut')).toBeInTheDocument();
    expect(within(ligneCi).queryByRole('button', { name: /Recréer/ })).not.toBeInTheDocument();
    const ligneCmdb = within(service).getByText('Import CMDB').closest('tr')!;
    expect(within(ligneCmdb).getByText('dans 28 jours')).toBeInTheDocument();
    expect(within(ligneCmdb).getByText('1 200 / min')).toBeInTheDocument();
    expect(within(ligneCmdb).getByRole('button', { name: 'Recréer « Import CMDB »' })).toBeInTheDocument();
    expect(within(personnels).getByText('Collection Postman')).toBeInTheDocument();
    expect(within(personnels).getByText('Rapport hebdo')).toBeInTheDocument();
    expect(screen.getByText('600 appels / minute')).toBeInTheDocument();
  });

  it('filtre les jetons personnels par compte', async () => {
    const user = userEvent.setup();
    rendre();
    await user.type(await screen.findByLabelText('Filtrer par compte'), 'bob');
    expect(screen.getByText('Collection Postman')).toBeInTheDocument();
    expect(screen.queryByText('Rapport hebdo')).not.toBeInTheDocument();
    await user.clear(screen.getByLabelText('Filtrer par compte'));
    await user.type(screen.getByLabelText('Filtrer par compte'), 'zoé');
    expect(screen.getByText('Aucun jeton pour ce compte')).toBeInTheDocument();
  });

  it('crée un jeton de service : compte technique à créer, portée admin, débit propre', async () => {
    vi.mocked(api.creerJetonService).mockResolvedValue({
      jeton: { ...ci, id: 's3', nom: 'Supervision Centreon', compteLogin: 'svc-supervision-centreon' },
      secret: SECRET,
    });
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Nouveau jeton de service' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouveau jeton de service' });
    await user.type(within(dialogue).getByLabelText('Outil *'), 'Supervision Centreon');
    expect(within(dialogue).getByText('svc-supervision-centreon')).toBeInTheDocument();
    expect(within(dialogue).getByLabelText('Expiration')).toHaveValue('365');
    expect(within(dialogue).getByRole('option', { name: 'Par défaut (600 / min)' })).toBeInTheDocument();
    await user.click(within(dialogue).getByRole('checkbox', { name: /Lire les MEP/ }));
    await user.click(within(dialogue).getByRole('checkbox', { name: /Administration/ }));
    await user.selectOptions(within(dialogue).getByLabelText('Limite de débit'), '3000');
    await user.click(within(dialogue).getByRole('button', { name: 'Créer le jeton' }));
    expect(api.creerJetonService).toHaveBeenCalledWith({
      nom: 'Supervision Centreon',
      portees: ['meps:lecture', 'admin'],
      dureeJours: 365,
      requetesParMinute: 3000,
    });
    const cree = await screen.findByRole('dialog', { name: 'Jeton créé' });
    expect(within(cree).getByText(SECRET)).toBeInTheDocument();
    expect(within(cree).getByText('svc-supervision-centreon')).toBeInTheDocument();
  });

  it('« Recréer » garde le compte technique, les portées et le débit ; l’erreur du serveur reste dans le dialogue', async () => {
    vi.mocked(api.creerJetonService).mockRejectedValueOnce(new Error('Portée inconnue.'));
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Recréer « Import CMDB »' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Recréer « Import CMDB »' });
    expect(within(dialogue).getByRole('radio', { name: /Utiliser un compte technique existant/ })).toBeChecked();
    expect(within(dialogue).getByLabelText('Compte technique existant')).toHaveValue('c-cmdb');
    await user.click(within(dialogue).getByRole('button', { name: 'Créer le jeton' }));
    expect(api.creerJetonService).toHaveBeenCalledWith({
      nom: 'Import CMDB',
      portees: ['catalogue:ecriture', 'admin'],
      dureeJours: 365,
      compteTechniqueId: 'c-cmdb',
      requetesParMinute: 1200,
    });
    expect(await within(dialogue).findByText('Portée inconnue.')).toBeInTheDocument();
  });

  it('révoque le jeton personnel d’un autre compte', async () => {
    vi.mocked(api.revoquerJeton).mockResolvedValue({ ...bob, etat: 'Revoque' });
    const user = userEvent.setup();
    rendre();
    await user.click(await screen.findByRole('button', { name: 'Révoquer « Collection Postman »' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Révoquer « Collection Postman » ?' });
    expect(within(dialogue).getByText('Bob')).toBeInTheDocument();
    await user.click(within(dialogue).getByRole('button', { name: 'Révoquer' }));
    expect(api.revoquerJeton).toHaveBeenCalledWith('p1');
    await waitFor(() => expect(api.jetons).toHaveBeenCalledTimes(2));
  });

  it('montre un état vide sans jeton, et l’erreur de chargement', async () => {
    vi.mocked(api.jetons).mockResolvedValueOnce([]);
    rendre();
    expect(await screen.findByText('Aucun jeton de service')).toBeInTheDocument();
    expect(screen.getByText('Aucun jeton personnel')).toBeInTheDocument();
  });

  it('signale une erreur de chargement', async () => {
    vi.mocked(api.jetons).mockRejectedValueOnce(new Error('Serveur indisponible'));
    rendre();
    expect(await screen.findByText(/Impossible de charger les jetons/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nouveau jeton de service' })).not.toBeInTheDocument();
  });
});
