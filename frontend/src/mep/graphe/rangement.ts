import type { ELK as MoteurElk, ElkExtendedEdge, ElkNode, ElkPort } from 'elkjs/lib/elk-api';
import type { LienModele, ModeleGraphe, NoeudModele } from './modeleGraphe';

/**
 * Rangement automatique du graphe libre (ELK, algorithme « layered », de haut en bas), aligné sur une grille.
 *
 * Rangement stable (protection 3) : les nœuds et les liens entrent dans l'ordre du modèle (ligne, ordre, code) et ELK
 * le respecte (`considerModelOrder`, `forceNodeModelOrder`) ; un ajout ailleurs ne réordonne pas le reste.
 */
export const GRILLE = 24;
export const CARTE = { largeur: 212, hauteur: 104 };
export const CERCLE = { largeur: 132, hauteur: 32 };
/** Largeur réservée à chaque cercle sur le bord d'une section repliée. */
export const PAS_CERCLE = 140;
/** En-tête d'un cadre de section déplié (titre, systèmes, boutons). */
export const ENTETE_CADRE = 64;

export interface Taille {
  largeur: number;
  hauteur: number;
}

export interface Place {
  /** Position relative au parent (cadre déplié) ou au graphe. */
  x: number;
  y: number;
  largeur: number;
  hauteur: number;
}

export interface Rangement {
  places: Map<string, Place>;
  /** Abscisse (relative à la section repliée) de chaque cercle sur son bord. */
  cercles: Map<string, number>;
}

const OPTIONS_RACINE: Record<string, string> = {
  'elk.algorithm': 'layered',
  'elk.direction': 'DOWN',
  'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
  'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
  'elk.layered.crossingMinimization.forceNodeModelOrder': 'true',
  'elk.layered.considerModelOrder.components': 'MODEL_ORDER',
  'elk.layered.layering.strategy': 'LONGEST_PATH_SOURCE',
  'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
  'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
  'elk.spacing.nodeNode': String(GRILLE * 2),
  'elk.layered.spacing.nodeNodeBetweenLayers': String(GRILLE * 3),
  'elk.spacing.componentComponent': String(GRILLE * 3),
  'elk.padding': `[top=${GRILLE},left=${GRILLE},bottom=${GRILLE},right=${GRILLE}]`,
};

const OPTIONS_CADRE: Record<string, string> = {
  'elk.padding': `[top=${ENTETE_CADRE + GRILLE / 2},left=${GRILLE},bottom=${GRILLE},right=${GRILLE}]`,
  'elk.spacing.nodeNode': String(GRILLE * 2),
  'elk.layered.spacing.nodeNodeBetweenLayers': String(GRILLE * 2),
};

export const aligner = (v: number) => Math.round(v / GRILLE) * GRILLE;

/** Taille d'une section repliée : assez large pour ses cercles. */
export function tailleSectionRepliee(n: Pick<NoeudModele, 'entrees' | 'sorties'>, mesuree?: Taille): Taille {
  const cercles = Math.max(n.entrees?.length ?? 0, n.sorties?.length ?? 0);
  return {
    largeur: Math.max(mesuree?.largeur ?? 0, 288, cercles * PAS_CERCLE + GRILLE),
    hauteur: mesuree?.hauteur ?? 120,
  };
}

function tailleDe(n: NoeudModele, tailles: ReadonlyMap<string, Taille>): Taille {
  const m = tailles.get(n.id);
  if (n.nature === 'section') return tailleSectionRepliee(n, m);
  if (n.nature === 'cercle') return CERCLE;
  return m ?? CARTE;
}

/** Graphe ELK du modèle : nœuds dans l'ordre du modèle, cadres dépliés composés, cercles des sections repliées en ports. */
export function grapheElk(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): ElkNode {
  const enfants = new Map<string, ElkNode[]>();
  const racine: ElkNode[] = [];
  const ports = new Map<string, string>();
  for (const n of modele.noeuds) {
    const t = tailleDe(n, tailles);
    const noeud: ElkNode = { id: n.id };
    if (n.nature === 'cadre') {
      noeud.layoutOptions = OPTIONS_CADRE;
      noeud.children = [];
      enfants.set(n.id, noeud.children);
    } else {
      noeud.width = t.largeur;
      noeud.height = t.hauteur;
      // Cartes d'une même rangée alignées par le haut (hauteurs différentes).
      noeud.layoutOptions = { 'elk.alignment': 'TOP' };
    }
    if (n.nature === 'section') {
      const p: ElkPort[] = [];
      for (const c of n.entrees ?? []) {
        ports.set(`${n.id}|${c.id}`, `p:${c.id}`);
        p.push({ id: `p:${c.id}`, width: 1, height: 1, layoutOptions: { 'elk.port.side': 'NORTH' } });
      }
      for (const c of n.sorties ?? []) {
        ports.set(`${n.id}|${c.id}`, `p:${c.id}`);
        p.push({ id: `p:${c.id}`, width: 1, height: 1, layoutOptions: { 'elk.port.side': 'SOUTH' } });
      }
      noeud.ports = p;
      noeud.layoutOptions = { 'elk.portConstraints': 'FIXED_ORDER', 'elk.alignment': 'TOP' };
    }
    if (n.nature === 'cercle' && n.parent)
      noeud.layoutOptions = { 'elk.layered.layering.layerConstraint': n.sens === 'Entree' ? 'FIRST' : 'LAST' };
    if (n.nature === 'cercle' && !n.parent)
      noeud.layoutOptions = {
        'elk.layered.layering.layerConstraint': n.sens === 'Entree' ? 'FIRST_SEPARATE' : 'LAST_SEPARATE',
      };
    const liste = n.parent ? enfants.get(n.parent) : racine;
    (liste ?? racine).push(noeud);
  }
  const bout = (noeud: string, poignee: string) => ports.get(`${noeud}|${poignee}`) ?? noeud;
  const edges: ElkExtendedEdge[] = modele.liens.map((l: LienModele) => ({
    id: l.id,
    sources: [bout(l.source, l.sourcePoignee)],
    targets: [bout(l.cible, l.ciblePoignee)],
  }));
  return { id: 'racine', layoutOptions: OPTIONS_RACINE, children: racine, edges };
}

let moteur: Promise<MoteurElk> | null = null;

/** ELK est chargé à la première utilisation (bibliothèque volumineuse, inutile hors du graphe). */
function chargerMoteur(): Promise<MoteurElk> {
  moteur ??= import('elkjs/lib/elk.bundled.js').then(({ default: ELK }) => new ELK());
  return moteur;
}

/** Range le modèle : positions alignées sur la grille, relatives au parent comme le veut React Flow. */
export async function ranger(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): Promise<Rangement> {
  const elk = await chargerMoteur();
  const resultat = await elk.layout(grapheElk(modele, tailles));
  const places = new Map<string, Place>();
  const cercles = new Map<string, number>();
  const parcourir = (n: ElkNode) => {
    for (const c of n.children ?? []) {
      places.set(c.id, {
        x: aligner(c.x ?? 0),
        y: aligner(c.y ?? 0),
        largeur: c.children ? aligner(c.width ?? 0) : (c.width ?? 0),
        hauteur: c.children ? aligner(c.height ?? 0) : (c.height ?? 0),
      });
      for (const p of c.ports ?? []) cercles.set(p.id.slice(2), (p.x ?? 0) + (p.width ?? 0) / 2);
      parcourir(c);
    }
  };
  parcourir(resultat);
  return { places, cercles };
}

/**
 * Ancrage de la vue (protection 2) : décalage à appliquer au déplacement de la vue pour que le nœud `id` reste au même
 * endroit de l'écran après un rangement. Positions absolues (repère du graphe).
 */
export function decalageAncrage(
  avant: ReadonlyMap<string, { x: number; y: number }>,
  apres: ReadonlyMap<string, { x: number; y: number }>,
  id: string | null,
  zoom: number,
): { dx: number; dy: number } {
  const a = id ? avant.get(id) : undefined;
  const b = id ? apres.get(id) : undefined;
  if (!a || !b) return { dx: 0, dy: 0 };
  return { dx: -(b.x - a.x) * zoom, dy: -(b.y - a.y) * zoom };
}

/** Positions absolues (repère du graphe) d'un rangement aux positions relatives. */
export function positionsAbsolues(
  r: Rangement,
  parents: ReadonlyMap<string, string | null>,
): Map<string, { x: number; y: number }> {
  const abs = new Map<string, { x: number; y: number }>();
  const calcul = (id: string): { x: number; y: number } | undefined => {
    const deja = abs.get(id);
    if (deja) return deja;
    const p = r.places.get(id);
    if (!p) return undefined;
    const parent = parents.get(id);
    const base = parent ? calcul(parent) : undefined;
    const v = { x: p.x + (base?.x ?? 0), y: p.y + (base?.y ?? 0) };
    abs.set(id, v);
    return v;
  };
  for (const id of r.places.keys()) calcul(id);
  return abs;
}

/**
 * Nœud d'ancrage par défaut : le plus proche du centre de l'écran (positions absolues, centre en coordonnées du graphe).
 */
export function noeudAuCentre(
  positions: ReadonlyMap<string, { x: number; y: number }>,
  tailles: ReadonlyMap<string, Taille>,
  centre: { x: number; y: number },
): string | null {
  let meilleur: string | null = null;
  let distance = Infinity;
  for (const [id, p] of positions) {
    const t = tailles.get(id) ?? CARTE;
    const d = Math.hypot(p.x + t.largeur / 2 - centre.x, p.y + t.hauteur / 2 - centre.y);
    if (d < distance) {
      distance = d;
      meilleur = id;
    }
  }
  return meilleur;
}
