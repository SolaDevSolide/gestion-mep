import { act, renderHook } from '@testing-library/react';
import { useDraft } from './hooks';

describe('useDraft', () => {
  it('suit la source tant que rien n’est saisi', () => {
    const { result, rerender } = renderHook(({ s }) => useDraft(s), { initialProps: { s: { titre: 'A' } } });
    rerender({ s: { titre: 'B' } });
    expect(result.current.value).toEqual({ titre: 'B' });
    expect(result.current.dirty).toBe(false);
    expect(result.current.conflit).toBe(false);
  });

  it('signale un conflit si la source change pendant la saisie', () => {
    const { result, rerender } = renderHook(({ s }) => useDraft(s), { initialProps: { s: { titre: 'A', note: '' } } });
    act(() => result.current.update({ note: 'ma note' }));
    expect(result.current.conflit).toBe(false);

    rerender({ s: { titre: 'Titre d’un collègue', note: '' } });
    expect(result.current.value).toEqual({ titre: 'A', note: 'ma note' });
    expect(result.current.conflit).toBe(true);

    // Une nouvelle frappe ne fait pas disparaître le conflit.
    act(() => result.current.update({ note: 'ma note !' }));
    expect(result.current.conflit).toBe(true);

    act(() => result.current.garderSaisie());
    expect(result.current.conflit).toBe(false);
    expect(result.current.value).toEqual({ titre: 'A', note: 'ma note !' });
  });

  it('abandonner la saisie reprend la version à jour', () => {
    const { result, rerender } = renderHook(({ s }) => useDraft(s), { initialProps: { s: { titre: 'A' } } });
    act(() => result.current.update({ titre: 'mien' }));
    rerender({ s: { titre: 'B' } });
    act(() => result.current.reset());
    expect(result.current.value).toEqual({ titre: 'B' });
    expect(result.current.conflit).toBe(false);
  });
});
