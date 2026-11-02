import type { ElkNode } from 'elkjs/lib/elk-api';

/** Délai maximal d'un rangement : au-delà, le moteur est arrêté et le graphe garde ses places. */
export const DELAI_RANGEMENT_MS = 15_000;

/**
 * Pourquoi le rangement automatique n'a pas pu se faire :
 * - `version` : le code du moteur a disparu du serveur (nouvelle version déployée) ; c'est le message « nouvelle version »
 *   qui s'affiche (`NouvelleVersion`, sur l'événement `vite:preloadError`) ;
 * - `chargement` : le moteur n'a pas pu démarrer (fichier introuvable, réseau, fil de calcul arrêté) ;
 * - `delai` : le calcul a dépassé `DELAI_RANGEMENT_MS` ;
 * - `calcul` : le moteur a renvoyé une erreur.
 */
export type CauseIndisponible = 'version' | 'chargement' | 'delai' | 'calcul';

export class RangementIndisponible extends Error {
  readonly raison: CauseIndisponible;
  constructor(raison: CauseIndisponible, cause?: unknown) {
    super(`Rangement automatique indisponible (${raison}).`, { cause });
    this.name = 'RangementIndisponible';
    this.raison = raison;
  }
}

interface Moteur {
  ranger: (graphe: ElkNode) => Promise<ElkNode>;
  arreter: () => void;
}

/**
 * Calcul interrompu parce que le moteur a été arrêté pendant qu'il attendait ou tournait : un autre rangement (souvent
 * celui d'un état déjà dépassé) a échoué et fait remplacer le moteur. Ce graphe-ci n'y est pour rien : il est recalculé.
 */
class MoteurArrete extends Error {
  constructor() {
    super('moteur arrêté');
    this.name = 'MoteurArrete';
  }
}

/** Calculs en cours d'un moteur : tous rejetés quand il s'arrête (`echouer`). */
function suivreCalculs(ranger: (g: ElkNode) => Promise<ElkNode>) {
  const enCours = new Set<(e: Error) => void>();
  const echouer = (e: Error) => {
    for (const rejeter of enCours) rejeter(e);
    enCours.clear();
  };
  return {
    echouer,
    ranger: (g: ElkNode) =>
      new Promise<ElkNode>((resoudre, rejeter) => {
        enCours.add(rejeter);
        ranger(g)
          .then(resoudre, rejeter)
          .finally(() => enCours.delete(rejeter));
      }),
  };
}

/**
 * Moteur ELK dans un fil de calcul à part (Web Worker) : un gros plan ne fige plus l'interface. Dans les tests (jsdom, sans
 * Web Worker), ELK tourne dans la page ; cette branche n'existe pas dans l'interface construite.
 */
async function creerMoteur(): Promise<Moteur> {
  if (import.meta.env.MODE === 'test') {
    const { default: ELK } = await import('elkjs/lib/elk.bundled.js');
    const elk = new ELK();
    const calculs = suivreCalculs((g) =>
      elk.layout(g).catch((e: unknown) => Promise.reject(new RangementIndisponible('calcul', e))),
    );
    return { ranger: calculs.ranger, arreter: () => calculs.echouer(new MoteurArrete()) };
  }
  const [{ default: ELK }, { default: TravailleurElk }] = await Promise.all([
    import('elkjs/lib/elk-api.js'),
    import('elkjs/lib/elk-worker.min.js?worker'),
  ]);
  const travailleur = new TravailleurElk({ name: 'rangement-elk' });
  const elk = new ELK({ workerFactory: () => travailleur });
  // Calculs en cours : rejetés si le fil de calcul s'arrête (fichier introuvable, erreur non rattrapée) ou est arrêté.
  const calculs = suivreCalculs((g) =>
    elk.layout(g).catch((e: unknown) => Promise.reject(new RangementIndisponible('calcul', e))),
  );
  travailleur.addEventListener('error', (ev) => {
    ev.preventDefault();
    calculs.echouer(new RangementIndisponible('chargement', ev.message));
  });
  return {
    ranger: calculs.ranger,
    arreter: () => {
      elk.terminateWorker();
      calculs.echouer(new MoteurArrete());
    },
  };
}

let moteur: Promise<Moteur> | null = null;

/** ELK est chargé à la première utilisation (bibliothèque volumineuse, inutile hors du graphe). */
function chargerMoteur(): Promise<Moteur> {
  if (moteur) return moteur;
  // Fichier disparu après un déploiement : Vite le signale par `vite:preloadError` avant de rejeter l'import.
  let versionPerimee = false;
  const surPreload = () => {
    versionPerimee = true;
  };
  window.addEventListener('vite:preloadError', surPreload);
  moteur = creerMoteur()
    .catch((e: unknown) => {
      throw e instanceof RangementIndisponible
        ? e
        : new RangementIndisponible(versionPerimee ? 'version' : 'chargement', e);
    })
    .finally(() => window.removeEventListener('vite:preloadError', surPreload));
  return moteur;
}

/**
 * Oublie le moteur (et arrête son fil de calcul) : le rangement suivant repart d'un moteur neuf. Après un échec, sans
 * cela, l'import raté restait en mémoire et tous les rangements suivants échouaient sans bruit.
 */
export function oublierMoteur() {
  const ancien = moteur;
  moteur = null;
  void ancien?.then(
    (m) => m.arreter(),
    () => undefined,
  );
}

/**
 * Calcule le rangement ELK du graphe. Échec (moteur introuvable, erreur, délai dépassé) : `RangementIndisponible`, et le
 * moteur est oublié pour que l'essai suivant reparte de zéro. Un calcul interrompu par l'échec d'un autre (moteur arrêté
 * entre-temps) est refait une fois sur un moteur neuf : sans cela, un graphe valide restait affiché comme indisponible.
 */
export async function calculerRangement(graphe: ElkNode, delai = DELAI_RANGEMENT_MS): Promise<ElkNode> {
  try {
    return await essayer(graphe, delai);
  } catch (e) {
    if (!(e instanceof MoteurArrete)) throw e;
  }
  try {
    return await essayer(graphe, delai);
  } catch (e) {
    throw e instanceof MoteurArrete ? new RangementIndisponible('chargement', e) : e;
  }
}

async function essayer(graphe: ElkNode, delai: number): Promise<ElkNode> {
  let minuteur: number | undefined;
  try {
    const m = await chargerMoteur();
    const depasse = new Promise<never>((_, rejeter) => {
      minuteur = window.setTimeout(() => rejeter(new RangementIndisponible('delai')), delai);
    });
    return await Promise.race([m.ranger(graphe), depasse]);
  } catch (e) {
    // Interrompu par l'arrêt du moteur : un autre calcul l'a déjà remplacé, rien à oublier de plus.
    if (e instanceof MoteurArrete) throw e;
    oublierMoteur();
    throw e instanceof RangementIndisponible ? e : new RangementIndisponible('calcul', e);
  } finally {
    window.clearTimeout(minuteur);
  }
}
