import { act, renderHook } from '@testing-library/react';
import { useAsync, useDraft } from './hooks';

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

describe('useAsync : chargement et rechargement', () => {
  it('garde les données affichées pendant un rechargement, signalé par `rechargement`', async () => {
    let resoudre: (v: string) => void = () => undefined;
    const charger = vi.fn(() => new Promise<string>((r) => (resoudre = r)));
    const { result } = renderHook(() => useAsync(charger, []));
    expect(result.current.chargement).toBe(true);
    expect(result.current.rechargement).toBe(false);
    await act(async () => resoudre('v1'));
    expect(result.current).toMatchObject({ donnees: 'v1', chargement: false, rechargement: false });
    act(() => result.current.recharger());
    expect(result.current).toMatchObject({ donnees: 'v1', chargement: true, rechargement: true });
    await act(async () => resoudre('v2'));
    expect(result.current).toMatchObject({ donnees: 'v2', chargement: false, rechargement: false });
  });
});
