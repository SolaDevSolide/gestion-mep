import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import type { MepResume } from '../types';
import { telecharger } from '../telechargement';
import ListeMepPage from './ListeMepPage';

vi.mock('../api', async (importOriginal) => {
  const { api: vraie } = await importOriginal<{ api: typeof api }>();
  return { api: { meps: vi.fn(), equipes: vi.fn(), creerMep: vi.fn(), urlExportMeps: vraie.urlExportMeps } };
});
vi.mock('../telechargement', () => ({ telecharger: vi.fn() }));

const resume = (p: Partial<MepResume>): MepResume => ({
  id: 'm',
  outil: 'Outil',
  versionCible: '',
  objet: '',
  dateMep: null,
  phase: 'Preparation',
  echeanceRepli: null,
  pilote: null,
  nbEtapes: 0,
  nbReussies: 0,
  nbEchecs: 0,
  toucheProduction: false,
  estModele: false,
  creeLe: '2026-10-01T08:00:00Z',
  issue: null,
  retourArriereActive: true,
  type: 'Complete',
  equipeIds: [],
  personnes: [],
  ...p,
});

function rendre() {
  render(
    <MemoryRouter>
      <ListeMepPage />
    </MemoryRouter>,
  );
}

describe('ListeMepPage', () => {
  beforeEach(() => {
    vi.mocked(api.equipes).mockResolvedValue([]);
  });

  it('ne réutilise pas un bouton de l’en-tête d’une vue à l’autre (pas de transition de couleur)', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    rendre();
    const depuisModele = screen.getByRole('button', { name: /Nouvelle MEP depuis un modèle/ });
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    const nouveauModele = (await screen.findAllByRole('button', { name: /Nouveau modèle/ }))[0];
    // Même position dans l'en-tête, mais un autre élément : sans clé, React lui changeait seulement sa variante.
    expect(nouveauModele).not.toBe(depuisModele);
    expect(depuisModele).not.toBeInTheDocument();
  });

  it('filtre les MEP par équipe et trouve une personne par l’un de ses comptes reliés', async () => {
    vi.mocked(api.equipes).mockResolvedValue([
      {
        id: 'exploit',
        nom: 'Exploitation',
        description: '',
        membreIds: [],
        creeeLe: '2026-10-01T08:00:00Z',
        contactSupport: '',
        signature: '',
      },
    ]);
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', equipeIds: ['exploit'], personnes: ['Jean Dupont', 'jean.dupont@ent.fr'] }),
      resume({ id: 'b', outil: 'Facturation', personnes: ['Claire'] }),
    ]);
    rendre();
    expect(await screen.findByText('Facturation')).toBeInTheDocument();

    await userEvent.selectOptions(await screen.findByLabelText('Filtrer par équipe'), 'exploit');
    expect(screen.getByText('Portail')).toBeInTheDocument();
    expect(screen.queryByText('Facturation')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filtrer par équipe'), '');
    await userEvent.type(screen.getByLabelText('Rechercher une MEP'), 'jean.dupont');
    expect(screen.getByText('Portail')).toBeInTheDocument();
    expect(screen.queryByText('Facturation')).not.toBeInTheDocument();
  });

  it('exporte en Excel la liste affichée, avec ses filtres', async () => {
    vi.mocked(api.meps).mockResolvedValue([resume({ id: 'a', outil: 'Portail', phase: 'Execution' })]);
    rendre();
    expect(await screen.findByText('Portail')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel');

    await userEvent.type(screen.getByLabelText('Rechercher une MEP'), ' paie élec ');
    await userEvent.selectOptions(screen.getByLabelText('Filtrer par phase'), 'Execution');
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel?phase=Execution&recherche=paie+%C3%A9lec');

    // Modèles : la phase ne s'applique pas.
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel?modele=true&recherche=paie+%C3%A9lec');
  });

  it('signale les MEP qui touchent la production', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', toucheProduction: true }),
      resume({ id: 'b', outil: 'Recette seule' }),
    ]);
    rendre();
    const portail = (await screen.findByText('Portail')).closest('li') ?? document.body;
    expect(portail).toHaveTextContent(/Production/);
    const recette = screen.getByText('Recette seule').closest('li') ?? document.body;
    expect(recette).not.toHaveTextContent(/Production/);
  });
});

describe('ListeMepPage : MEP simplifiée', () => {
  beforeEach(() => {
    vi.mocked(api.equipes).mockResolvedValue([]);
    vi.mocked(api.creerMep).mockReset();
  });

  it('crée une MEP du type choisi (complète par défaut)', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    vi.mocked(api.creerMep).mockResolvedValue({ id: 'n' } as never);
    rendre();
    await userEvent.click((await screen.findAllByRole('button', { name: /^Nouvelle MEP$/ }))[0]);
    expect(screen.getByRole('radio', { name: /MEP complète/ })).toBeChecked();
    await userEvent.click(screen.getByRole('radio', { name: /MEP simplifiée/ }));
    await userEvent.type(screen.getByLabelText('Outil ou application'), 'Correctif');
    await userEvent.click(screen.getByRole('button', { name: 'Créer la MEP' }));
    expect(api.creerMep).toHaveBeenCalledWith('Correctif', '', false, 'Simplifiee');
  });

  it('pas de choix de type pour un modèle', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    rendre();
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    await userEvent.click((await screen.findAllByRole('button', { name: /Nouveau modèle/ }))[0]);
    expect(screen.getByRole('dialog', { name: 'Nouveau modèle' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /MEP simplifiée/ })).not.toBeInTheDocument();
  });

  it('repère les MEP simplifiées dans la liste, sans version cible', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', versionCible: '2.0' }),
      resume({ id: 'b', outil: 'Correctif', type: 'Simplifiee', versionCible: '9.9' }),
    ]);
    rendre();
    expect(await screen.findByText('Correctif')).toBeInTheDocument();
    expect(screen.getAllByText('MEP simplifiée')).toHaveLength(1);
    expect(screen.getByText('2.0')).toBeInTheDocument();
    expect(screen.queryByText('9.9')).not.toBeInTheDocument();
  });
});
