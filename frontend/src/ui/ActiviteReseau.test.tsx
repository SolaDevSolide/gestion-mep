import { act, render, screen } from '@testing-library/react';
import { request } from '../api';
import { ActiviteReseau } from './ActiviteReseau';

describe('ActiviteReseau', () => {
  let resoudre: (r: Response) => void = () => undefined;
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>((r) => (resoudre = r))),
    );
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('un trait discret seulement si une requête se fait attendre, puis plus rien', async () => {
    render(<ActiviteReseau delai={250} />);
    let fin: Promise<unknown> = Promise.resolve();
    act(() => {
      fin = request('GET', '/meps');
    });
    act(() => vi.advanceTimersByTime(200));
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    act(() => vi.advanceTimersByTime(100));
    expect(screen.getByRole('progressbar', { name: 'Chargement en cours' })).toBeInTheDocument();
    await act(async () => {
      resoudre(new Response('[]', { status: 200 }));
      await fin;
    });
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('ignore les requêtes silencieuses (présence, verrou, sondage)', () => {
    render(<ActiviteReseau delai={250} />);
    act(() => {
      void request('PUT', '/meps/m1/presence', {}, { silencieux: true });
    });
    act(() => vi.advanceTimersByTime(1000));
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });
});
