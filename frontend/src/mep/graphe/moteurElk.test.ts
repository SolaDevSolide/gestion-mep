import type { ElkNode } from 'elkjs/lib/elk-api';
import { calculerRangement, DELAI_RANGEMENT_MS, oublierMoteur, RangementIndisponible } from './moteurElk';

// Sans Web Worker (jsdom), le moteur tourne dans la page : ELK simulé, démarrage et calcul pilotés par chaque test.
const elk = vi.hoisted(() => ({
  demarrages: 0,
  echecDemarrage: false,
  /** Ce que fait le démarrage en plus (ex. : Vite qui signale un fichier disparu). */
  auDemarrage: null as (() => void) | null,
  layout: vi.fn<(g: ElkNode) => Promise<ElkNode>>(),
}));
vi.mock('elkjs/lib/elk.bundled.js', () => ({
  default: class {
    constructor() {
      elk.demarrages += 1;
      elk.auDemarrage?.();
      if (elk.echecDemarrage) throw new Error('fichier introuvable');
    }
    layout(g: ElkNode) {
      return elk.layout(g);
    }
  },
}));

const graphe: ElkNode = { id: 'racine', children: [] };

async function raison(p: Promise<unknown>) {
  try {
    await p;
    return null;
  } catch (e) {
    return e instanceof RangementIndisponible ? e.raison : 'autre';
  }
}

describe('moteurElk : un échec ne reste pas en mémoire', () => {
  beforeEach(() => {
    oublierMoteur();
    elk.demarrages = 0;
    elk.echecDemarrage = false;
    elk.auDemarrage = null;
    elk.layout.mockReset();
    elk.layout.mockImplementation((g) => Promise.resolve(g));
  });
  afterEach(() => vi.useRealTimers());

  it('garde le même moteur d’un rangement réussi à l’autre', async () => {
    await calculerRangement(graphe);
    await calculerRangement(graphe);
    expect(elk.demarrages).toBe(1);
  });

  it('démarrage raté : « chargement », puis l’essai suivant repart d’un moteur neuf', async () => {
    elk.echecDemarrage = true;
    expect(await raison(calculerRangement(graphe))).toBe('chargement');
    elk.echecDemarrage = false;
    await expect(calculerRangement(graphe)).resolves.toBe(graphe);
    expect(elk.demarrages).toBe(2);
  });

  it('calcul en erreur : « calcul », et le moteur est remplacé', async () => {
    elk.layout.mockRejectedValueOnce(new Error('graphe invalide'));
    expect(await raison(calculerRangement(graphe))).toBe('calcul');
    await calculerRangement(graphe);
    expect(elk.demarrages).toBe(2);
  });

  it('plus de 15 s : « delai », le moteur bloqué est abandonné', async () => {
    vi.useFakeTimers();
    elk.layout.mockImplementationOnce(() => new Promise<ElkNode>(() => undefined));
    const p = raison(calculerRangement(graphe));
    await vi.advanceTimersByTimeAsync(DELAI_RANGEMENT_MS - 1);
    expect(elk.layout).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(await p).toBe('delai');
    await calculerRangement(graphe);
    expect(elk.demarrages).toBe(2);
  });

  it('fichier du moteur disparu après un déploiement (vite:preloadError) : « version »', async () => {
    elk.echecDemarrage = true;
    // Vite signale l'import raté juste avant de le rejeter.
    elk.auDemarrage = () => window.dispatchEvent(new Event('vite:preloadError'));
    expect(await raison(calculerRangement(graphe))).toBe('version');
    // Un événement venu d'ailleurs, hors du chargement du moteur, ne compte pas.
    elk.auDemarrage = null;
    window.dispatchEvent(new Event('vite:preloadError'));
    expect(await raison(calculerRangement(graphe))).toBe('chargement');
  });
});
