import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import ListeMepPage from './ListeMepPage';

vi.mock('../api', () => ({ api: { meps: vi.fn() } }));

describe('ListeMepPage', () => {
  it('ne réutilise pas un bouton de l’en-tête d’une vue à l’autre (pas de transition de couleur)', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    render(
      <MemoryRouter>
        <ListeMepPage />
      </MemoryRouter>,
    );
    const depuisModele = screen.getByRole('button', { name: /Nouvelle MEP depuis un modèle/ });
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    const nouveauModele = (await screen.findAllByRole('button', { name: /Nouveau modèle/ }))[0];
    // Même position dans l'en-tête, mais un autre élément : sans clé, React lui changeait seulement sa variante.
    expect(nouveauModele).not.toBe(depuisModele);
    expect(depuisModele).not.toBeInTheDocument();
  });
});
