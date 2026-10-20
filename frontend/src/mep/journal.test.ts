import { act, renderHook, waitFor } from '@testing-library/react';
import { api } from '../api';
import type { Evenement } from '../types';
import { oublierReferentiels } from '../referentiels';
import { lireJournal, useJournal } from './journal';
import { useJournalMep } from './preparation/donnees';

vi.mock('../api', () => ({ api: { journal: vi.fn() } }));

const evt = (id: number, type = 'etape.reussie'): Evenement => ({
  id,
  quand: '2026-10-09T20:00:00Z',
  auteur: 'Alice',
  auteurLogin: 'alice',
  type,
  detail: `Événement ${id}`,
});
const ids = (e: Evenement[] | null) => (e ?? []).map((x) => x.id);

describe('useJournal', () => {
  it('lit la première page, puis seulement les nouveaux événements à chaque version, et les pages plus anciennes', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([evt(10), evt(9), evt(8)]);
    const { result, rerender } = renderHook(({ v }) => useJournal('m1', v, {}, 3), { initialProps: { v: 1 } });
    expect(result.current.evenements).toBeNull();
    await waitFor(() => expect(ids(result.current.evenements)).toEqual([10, 9, 8]));
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', recherche: '', limite: 3 });
    expect(result.current.suite).toBe(true);

    // Nouvelle version : seulement ce qui suit le dernier événement lu, sans indicateur de chargement.
    vi.mocked(api.journal).mockResolvedValueOnce([evt(12), evt(11)]);
    rerender({ v: 2 });
    await waitFor(() => expect(ids(result.current.evenements)).toEqual([12, 11, 10, 9, 8]));
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', recherche: '', apres: 10, limite: 500 }, true);

    // Afficher plus : la page avant le plus ancien ; une page incomplète est la dernière.
    vi.mocked(api.journal).mockResolvedValueOnce([evt(7)]);
    act(() => result.current.afficherPlus());
    expect(result.current.chargementSuite).toBe(true);
    await waitFor(() => expect(ids(result.current.evenements)).toEqual([12, 11, 10, 9, 8, 7]));
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', recherche: '', avant: 8, limite: 3 });
    expect(result.current.suite).toBe(false);
    expect(result.current.chargementSuite).toBe(false);
    expect(api.journal).toHaveBeenCalledTimes(3);
  });

  it('applique les filtres côté serveur et garde l’affichage précédent le temps de la réponse', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([evt(2), evt(1, 'mep.creee')]);
    const { result, rerender } = renderHook(({ type }) => useJournal('m1', 1, { type, recherche: ' E0001 ' }), {
      initialProps: { type: 'tous' },
    });
    await waitFor(() => expect(ids(result.current.evenements)).toEqual([2, 1]));
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', recherche: 'E0001', limite: 100 });

    let repondre: (e: Evenement[]) => void = () => undefined;
    vi.mocked(api.journal).mockReturnValueOnce(new Promise((r) => (repondre = r)));
    rerender({ type: 'etape' });
    await waitFor(() =>
      expect(api.journal).toHaveBeenLastCalledWith('m1', { type: 'etape', recherche: 'E0001', limite: 100 }),
    );
    expect(ids(result.current.evenements)).toEqual([2, 1]);
    expect(result.current.aJour).toBe(false);
    act(() => repondre([evt(2)]));
    await waitFor(() => expect(result.current.aJour).toBe(true));
    expect(ids(result.current.evenements)).toEqual([2]);
  });

  it('repart de zéro pour une autre MEP', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([evt(5)]);
    const { result, rerender } = renderHook(({ id }) => useJournal(id, 1), { initialProps: { id: 'm1' } });
    await waitFor(() => expect(ids(result.current.evenements)).toEqual([5]));
    vi.mocked(api.journal).mockReturnValueOnce(new Promise(() => undefined));
    rerender({ id: 'm2' });
    expect(result.current.evenements).toBeNull();
  });
});

describe('journal partagé (useJournalMep)', () => {
  beforeEach(() => {
    oublierReferentiels();
    vi.mocked(api.journal).mockReset();
  });

  it('lit une fois pour tous les composants, puis seulement les nouveaux événements de la famille à chaque version', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([evt(4, 'approbation.donnee'), evt(2, 'approbation.demandee')]);
    const { result, rerender } = renderHook(
      ({ v }) => ({
        a: useJournalMep({ id: 'm1', version: v }, 'approbation'),
        b: useJournalMep({ id: 'm1', version: v }, 'approbation'),
      }),
      { initialProps: { v: 1 } },
    );
    await waitFor(() => expect(ids(result.current.a.donnees)).toEqual([4, 2]));
    expect(ids(result.current.b.donnees)).toEqual([4, 2]);
    expect(api.journal).toHaveBeenCalledTimes(1);
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: 'approbation', limite: 100 });

    vi.mocked(api.journal).mockResolvedValueOnce([evt(7, 'approbation.donnee')]);
    rerender({ v: 2 });
    await waitFor(() => expect(ids(result.current.a.donnees)).toEqual([7, 4, 2]));
    expect(api.journal).toHaveBeenCalledTimes(2);
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: 'approbation', apres: 4, limite: 500 }, true);
  });

  it('montre une erreur sans réponse, puis « Réessayer » relit la première page', async () => {
    vi.mocked(api.journal).mockRejectedValueOnce(new Error('hors ligne'));
    const { result } = renderHook(() => useJournalMep({ id: 'm1', version: 1 }));
    await waitFor(() => expect(result.current.erreur).not.toBeNull());
    expect(result.current.donnees).toBeNull();
    vi.mocked(api.journal).mockResolvedValueOnce([evt(3)]);
    act(() => result.current.recharger());
    await waitFor(() => expect(ids(result.current.donnees)).toEqual([3]));
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', limite: 100 });
  });
});

describe('lireJournal', () => {
  beforeEach(() => vi.mocked(api.journal).mockReset());

  it('relit la première page quand trop d’événements sont arrivés d’un coup', async () => {
    vi.mocked(api.journal)
      .mockResolvedValueOnce(Array.from({ length: 500 }, (_, i) => evt(1000 - i)))
      .mockResolvedValueOnce([evt(1000), evt(999)]);
    expect(ids(await lireJournal('m1', '', [evt(5)]))).toEqual([1000, 999]);
    expect(api.journal).toHaveBeenLastCalledWith('m1', { type: '', limite: 100 });
  });
});
