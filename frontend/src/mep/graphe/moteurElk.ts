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
 * Moteur ELK dans un fil de calcul à part (Web Worker) : un gros plan ne fige plus l'interface. Dans les tests (jsdom, sans
 * Web Worker), ELK tourne dans la page ; cette branche n'existe pas dans l'interface construite.
 */
async function creerMoteur(): Promise<Moteur> {
  if (import.meta.env.MODE === 'test') {
    const { default: ELK } = await import('elkjs/lib/elk.bundled.js');
    const elk = new ELK();
    return { ranger: (g) => elk.layout(g), arreter: () => undefined };
  }
  const [{ default: ELK }, { default: TravailleurElk }] = await Promise.all([
    import('elkjs/lib/elk-api.js'),
    import('elkjs/lib/elk-worker.min.js?worker'),
  ]);
  // Calculs en cours : rejetés si le fil de calcul s'arrête (fichier introuvable, erreur non rattrapée) ou est arrêté.
  const enCours = new Set<(e: Error) => void>();
  const echouer = (e: Error) => {
    for (const rejeter of enCours) rejeter(e);
    enCours.clear();
  };
  const travailleur = new TravailleurElk({ name: 'rangement-elk' });
  travailleur.addEventListener('error', (ev) => {
    ev.preventDefault();
    echouer(new RangementIndisponible('chargement', ev.message));
  });
  const elk = new ELK({ workerFactory: () => travailleur });
  return {
    ranger: (g) =>
      new Promise<ElkNode>((resoudre, rejeter) => {
        enCours.add(rejeter);
        elk
          .layout(g)
          .then(resoudre, (e: unknown) => rejeter(new RangementIndisponible('calcul', e)))
          .finally(() => enCours.delete(rejeter));
      }),
    arreter: () => {
      elk.terminateWorker();
      echouer(new RangementIndisponible('chargement', 'moteur arrêté'));
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
 * moteur est oublié pour que l'essai suivant reparte de zéro.
 */
export async function calculerRangement(graphe: ElkNode, delai = DELAI_RANGEMENT_MS): Promise<ElkNode> {
  let minuteur: number | undefined;
  try {
    const m = await chargerMoteur();
    const depasse = new Promise<never>((_, rejeter) => {
      minuteur = window.setTimeout(() => rejeter(new RangementIndisponible('delai')), delai);
    });
    return await Promise.race([m.ranger(graphe), depasse]);
  } catch (e) {
    oublierMoteur();
    throw e instanceof RangementIndisponible ? e : new RangementIndisponible('calcul', e);
  } finally {
    window.clearTimeout(minuteur);
  }
}
