import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AvisRangement } from './AvisRangement';

describe('AvisRangement', () => {
  it('rangement réussi : rien', () => {
    const { container } = render(<AvisRangement cause={null} onReessayer={() => undefined} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('échec : message non bloquant et « Réessayer »', async () => {
    const reessayer = vi.fn();
    render(<AvisRangement cause="delai" onReessayer={reessayer} />);
    expect(screen.getByRole('status')).toHaveTextContent('Rangement automatique indisponible');
    await userEvent.click(screen.getByRole('button', { name: 'Réessayer' }));
    expect(reessayer).toHaveBeenCalledTimes(1);
  });

  it('se masque jusqu’au prochain échec', async () => {
    const { rerender } = render(<AvisRangement cause="chargement" onReessayer={() => undefined} />);
    await userEvent.click(screen.getByRole('button', { name: 'Masquer ce message' }));
    expect(screen.queryByRole('status')).toBeNull();
    rerender(<AvisRangement cause={null} onReessayer={() => undefined} />);
    rerender(<AvisRangement cause="chargement" onReessayer={() => undefined} />);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('nouvelle version déployée : laisse la place au message « nouvelle version »', () => {
    const { container } = render(<AvisRangement cause="version" onReessayer={() => undefined} />);
    expect(container).toBeEmptyDOMElement();
  });
});
