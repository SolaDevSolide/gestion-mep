import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import type { MepResume } from '../types';
import ListeMepPage from './ListeMepPage';

vi.mock('../api', () => ({ api: { meps: vi.fn(), equipes: vi.fn() } }));

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
