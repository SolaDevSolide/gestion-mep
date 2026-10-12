import { act, render, screen } from '@testing-library/react';
import { Apparition } from './Apparition';

describe('Apparition', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('n’affiche rien pendant le délai de grâce, puis un indicateur si le chargement dure', () => {
    render(
      <Apparition pret={false} delai={300}>
        <p>Contenu</p>
      </Apparition>,
    );
    expect(screen.queryByText('Contenu')).not.toBeInTheDocument();
    expect(screen.queryByText('Chargement…')).not.toBeInTheDocument();
    act(() => vi.advanceTimersByTime(300));
    expect(screen.getByText('Chargement…')).toBeInTheDocument();
  });

  it('affiche le contenu complet dès qu’il est prêt, sans indicateur', () => {
    const { rerender } = render(
      <Apparition pret={false} delai={300}>
        <p>Contenu</p>
      </Apparition>,
    );
    act(() => vi.advanceTimersByTime(100));
    rerender(
      <Apparition pret delai={300}>
        <p>Contenu</p>
      </Apparition>,
    );
    expect(screen.getByText('Contenu')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(500));
    expect(screen.queryByText('Chargement…')).not.toBeInTheDocument();
  });
});
