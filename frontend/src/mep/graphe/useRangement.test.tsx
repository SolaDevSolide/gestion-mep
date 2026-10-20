import { act, renderHook } from '@testing-library/react';
import type { ModeleGraphe } from './modeleGraphe';
import type * as ModuleMoteur from './moteurElk';
import { RangementIndisponible } from './moteurElk';
import type { Rangement, Taille } from './rangement';
import { SECOURS_MESURES_MS, useRangement } from './useRangement';

const appels: ModeleGraphe[] = [];
// Rangements qui échoueront (dans l'ordre des appels), avec leur cause.
const echecs: RangementIndisponible[] = [];
vi.mock('./rangementBordure', () => ({
  rangerBordure: (modele: ModeleGraphe) => {
    appels.push(modele);
    const echec = echecs.shift();
    if (echec) return Promise.reject(echec);
    return Promise.resolve<Rangement>({ places: new Map(), cercles: new Map(), routes: new Map() });
  },
}));
const { oublierMoteur } = vi.hoisted(() => ({ oublierMoteur: vi.fn() }));
vi.mock('./moteurElk', async (original) => ({ ...(await original<typeof ModuleMoteur>()), oublierMoteur }));

const replie: ModeleGraphe = {
  noeuds: [
    { id: 'e1', nature: 'etape', parent: null },
    { id: 's:a', nature: 'section', parent: null },
  ],
  liens: [],
};
// Section dépliée : deux étapes apparaissent, pas encore mesurées.
const deplie: ModeleGraphe = {
  noeuds: [
    { id: 'e1', nature: 'etape', parent: null },
    { id: 's:a', nature: 'cadre', parent: null },
    { id: 't:a', nature: 'titre', parent: 's:a' },
    { id: 'e2', nature: 'etape', parent: 's:a' },
    { id: 'e3', nature: 'etape', parent: 's:a' },
  ],
  liens: [],
};
const carte: Taille = { largeur: 212, hauteur: 104 };
const mesurees = new Map<string, Taille>([
  ['e1', carte],
  ['s:a', { largeur: 288, hauteur: 136 }],
]);

async function vider() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('useRangement : repli et dépli rangés une fois les nouveaux nœuds mesurés', () => {
  beforeEach(() => {
    appels.length = 0;
    vi.useFakeTimers();
  });
  afterEach(() => vi.useRealTimers());

  it('graphe affiché : attend les mesures, puis fait un seul rangement définitif', async () => {
    const { result, rerender } = renderHook(({ modele, tailles }) => useRangement(modele, tailles, false, 0, true), {
      initialProps: { modele: replie, tailles: mesurees },
    });
    await vider();
    expect(result.current.range?.modele).toBe(replie);
    expect(appels).toHaveLength(1);

    // Dépli : les étapes de la section ne sont pas mesurées, rien n'est rangé, le rangement affiché reste.
    rerender({ modele: deplie, tailles: mesurees });
    await vider();
    expect(appels).toHaveLength(1);
    expect(result.current.range?.modele).toBe(replie);
    expect(result.current.mesuresAttendues).toBe(true);

    // Mesures arrivées : un seul rangement, avec les vraies tailles.
    const toutes = new Map(mesurees).set('e2', carte).set('e3', carte);
    rerender({ modele: deplie, tailles: toutes });
    await vider();
    expect(appels).toHaveLength(2);
    expect(result.current.range?.modele).toBe(deplie);
    expect(result.current.mesuresAttendues).toBe(false);
  });

  it('premier affichage (pas encore prêt) : range tout de suite, même sans mesures', async () => {
    const { result } = renderHook(() => useRangement(deplie, new Map(), false, 0, false));
    await vider();
    expect(result.current.range?.modele).toBe(deplie);
  });

  it('secours seulement : une mesure qui ne vient jamais n’empêche pas le rangement', async () => {
    const { result, rerender } = renderHook(({ modele }) => useRangement(modele, mesurees, false, 0, true), {
      initialProps: { modele: replie },
    });
    await vider();
    rerender({ modele: deplie });
    await vider();
    expect(result.current.range?.modele).toBe(replie);
    await act(async () => {
      vi.advanceTimersByTime(SECOURS_MESURES_MS);
    });
    await vider();
    expect(result.current.range?.modele).toBe(deplie);
  });
});

describe('useRangement : échec du rangement', () => {
  beforeEach(() => {
    appels.length = 0;
    echecs.length = 0;
    oublierMoteur.mockClear();
  });

  it('un échec se voit (cause), « Réessayer » repart d’un moteur neuf et un rangement réussi l’efface', async () => {
    echecs.push(new RangementIndisponible('delai'));
    const { result } = renderHook(() => useRangement(replie, mesurees, false, 0, false));
    await vider();
    expect(result.current.range).toBeNull();
    expect(result.current.indisponible).toBe('delai');

    act(() => result.current.reessayer());
    await vider();
    expect(oublierMoteur).toHaveBeenCalledTimes(1);
    expect(appels).toHaveLength(2);
    expect(result.current.range?.modele).toBe(replie);
    expect(result.current.indisponible).toBeNull();
  });

  it('une autre erreur compte comme un échec de calcul ; le rangement affiché reste', async () => {
    const { result, rerender } = renderHook(({ modele }) => useRangement(modele, mesurees, false, 0, false), {
      initialProps: { modele: replie },
    });
    await vider();
    expect(result.current.range?.modele).toBe(replie);
    echecs.push(new Error('inattendue') as RangementIndisponible);
    rerender({ modele: deplie });
    await vider();
    expect(result.current.indisponible).toBe('calcul');
    expect(result.current.range?.modele).toBe(replie);
  });
});
