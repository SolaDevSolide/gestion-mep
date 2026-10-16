import { render, screen, within } from '@testing-library/react';
import { api } from '../api';
import type { TestsUnitaires } from '../types';
import { TestsUnitairesEncadre } from './TestsUnitairesEncadre';

vi.mock('../api', () => ({ api: { testsUnitaires: vi.fn() } }));

const suivi: TestsUnitaires = {
  faits: 2,
  nonFaits: 1,
  verifications: 1,
  tests: [
    {
      etapeId: 'e1',
      code: 'E0001',
      plan: 'Deploiement',
      action: 'Migration du schéma',
      etat: 'Reussie',
      fait: true,
      resultatAttendu: 'Aucune erreur',
      resultatConstate: '12 migrations appliquées',
      executant: 'Alice',
      finReelle: '2026-10-09T20:10:00Z',
    },
    {
      etapeId: 'e2',
      code: 'E0002',
      plan: 'Deploiement',
      action: 'Déploiement de l’API',
      etat: 'Echec',
      fait: true,
      resultatAttendu: 'Service actif',
      resultatConstate: 'Erreur 502',
      executant: 'Bob',
      finReelle: '2026-10-09T20:20:00Z',
    },
    {
      etapeId: 'e3',
      code: 'E0004',
      plan: 'Deploiement',
      action: 'Publication',
      etat: 'AFaire',
      fait: false,
      resultatAttendu: 'Portail en 2.0',
      resultatConstate: '',
      executant: null,
      finReelle: null,
    },
  ],
};

describe('Encadré des tests unitaires', () => {
  it('compte les tests faits et non faits et donne leurs résultats constatés', async () => {
    vi.mocked(api.testsUnitaires).mockResolvedValue(suivi);
    render(<TestsUnitairesEncadre mepId="m1" tick={0} />);
    const encadre = await screen.findByRole('region', { name: 'Tests unitaires' });
    expect(encadre).toHaveTextContent('2 faits · 1 non fait · 1 simple vérification');
    expect(api.testsUnitaires).toHaveBeenCalledWith('m1');

    const e1 = within(encadre).getByRole('listitem', { name: 'Test E0001' });
    expect(e1).toHaveTextContent('Constaté : 12 migrations appliquées · Alice');
    expect(e1).toHaveTextContent('Réussi');
    expect(within(encadre).getByRole('listitem', { name: 'Test E0002' })).toHaveTextContent('Échec');
    const e4 = within(encadre).getByRole('listitem', { name: 'Test E0004' });
    expect(e4).toHaveTextContent('Non fait');
    expect(e4).not.toHaveTextContent('Constaté');
  });

  it('rien à afficher sans aucune étape suivie', async () => {
    vi.mocked(api.testsUnitaires).mockResolvedValue({ faits: 0, nonFaits: 0, verifications: 0, tests: [] });
    const { container } = render(<TestsUnitairesEncadre mepId="m1" tick={0} />);
    // Sa place est tenue le temps du chargement, puis rien.
    await vi.waitFor(() => expect(container).toBeEmptyDOMElement());
  });
});
