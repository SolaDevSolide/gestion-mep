import type { Point } from './rangement';

/**
 * Mouvement du graphe au repli ou au dépli d'une section (ou quand des étapes arrivent ou partent) : un seul mouvement, du
 * rangement affiché au nouveau rangement, calculé avant la première image. Chaque nœud va de sa boîte de départ à sa boîte
 * d'arrivée ; l'intérieur d'un cadre qui s'ouvre ou se ferme se dévoile ou se cache avec lui (à sa taille réelle, découpé
 * par le cadre, rangée par rangée) ; les liens suivent leurs nœuds pendant tout le mouvement (ils ne disparaissent pas).
 */

/** Boîte d'un nœud en coordonnées du graphe (absolues). */
export interface Boite {
  x: number;
  y: number;
  largeur: number;
  hauteur: number;
}

/** Lien dessiné pendant le mouvement : tracé à l'instant, opacité, couleur (réussi en exécution, sinon neutre). */
export interface LienMouvement {
  id: string;
  d: string;
  opacite: number;
  ton: 'ok' | 'neutre';
  /** Lien à l'intérieur d'un cadre qui s'ouvre ou se ferme : dessiné seulement dans le cadre (comme son contenu). */
  dans?: Boite;
}

/** État d'un nœud à un bout du mouvement : sa boîte, son opacité et l'échelle de son contenu. */
export interface EtatNoeud {
  boite: Boite;
  opacite: number;
  echelle: number;
}

const borne = (v: number) => Math.min(1, Math.max(0, v));
const entre = (a: number, b: number, k: number) => a + (b - a) * k;

/**
 * Durée du mouvement : assez longue pour qu'on voie le cadre s'ouvrir ou se fermer et les voisins glisser (une quinzaine
 * d'images à 30 images par seconde), assez courte pour ne pas faire attendre.
 */
export const DUREE_MOUVEMENT_MS = 480;

/**
 * Avancement lissé du mouvement : démarre et arrive en douceur (sinusoïde), sans faire l'essentiel du chemin dans les
 * premières images (le changement se lit comme un mouvement, pas comme un saut).
 */
export const adoucir = (t: number) => (1 - Math.cos(Math.PI * borne(t))) / 2;

/** Opacité d'un nœud qui paraît, selon l'avancement `k` : il est entièrement visible aux trois cinquièmes du mouvement. */
export const rampeApparition = (k: number) => borne(k / 0.6);
/** Part effacée d'un nœud qui part, selon l'avancement `k` : il a disparu à la moitié du mouvement. */
export const rampeDisparition = (k: number) => borne(k / 0.5);

/** Sens d'un cadre de section pendant le mouvement : il s'ouvre (dépli) ou se ferme (repli). */
export type SensCadre = 'ouvre' | 'ferme';

/**
 * Opacité du contenu d'un cadre qui s'ouvre ou se ferme, rangée par rangée : `ry`, sa place verticale dans le cadre (0 en
 * haut, 1 en bas). À l'ouverture, le haut paraît aussitôt (le cadre n'est jamais vide) et le bas vers la moitié du
 * mouvement ; à la fermeture, le bas s'efface d'abord et tout a disparu aux trois quarts du mouvement, avant que le cadre
 * reprenne la taille de la section repliée.
 */
export function revelation(ry: number, k: number, sens: SensCadre): number {
  const y = borne(ry);
  if (sens === 'ouvre') return borne((k - 0.2 * y) / 0.25);
  return 1 - borne((k - 0.35 * (1 - y)) / 0.4);
}

/** Place d'un nœud dans son cadre, rapportée au milieu du bord haut du cadre (le cadre s'ouvre depuis le haut, au centre). */
export interface Ancrage {
  dx: number;
  dy: number;
  /** Place verticale relative dans le cadre (0 en haut, 1 en bas) : ordre d'apparition. */
  ry: number;
}

export function ancrageHaut(boite: Boite, cadre: Boite): Ancrage {
  return {
    dx: boite.x - (cadre.x + cadre.largeur / 2),
    dy: boite.y - cadre.y,
    ry: cadre.hauteur > 0 ? (boite.y + boite.hauteur / 2 - cadre.y) / cadre.hauteur : 0,
  };
}

/** Boîte d'un nœud de taille `largeur` × `hauteur` ancré (`ancrageHaut`) dans un cadre, à sa taille réelle. */
export function placeAncree(a: Ancrage, cadre: Boite, largeur: number, hauteur: number): Boite {
  return { x: cadre.x + cadre.largeur / 2 + a.dx, y: cadre.y + a.dy, largeur, hauteur };
}

/**
 * Découpe d'un nœud par le cadre qui le contient (`clip-path`, en coordonnées du nœud) : seule la partie à l'intérieur du
 * cadre se voit, le contenu se dévoile à mesure que le cadre s'ouvre. Null : le nœud est entièrement dans le cadre.
 */
export function decoupe(n: Boite, cadre: Boite): string | null {
  const haut = Math.max(0, cadre.y - n.y);
  const gauche = Math.max(0, cadre.x - n.x);
  const droite = Math.max(0, n.x + n.largeur - (cadre.x + cadre.largeur));
  const bas = Math.max(0, n.y + n.hauteur - (cadre.y + cadre.hauteur));
  if (haut + gauche + droite + bas < 0.5) return null;
  const px = (v: number) => `${Math.round(v * 10) / 10}px`;
  return `inset(${px(haut)} ${px(droite)} ${px(bas)} ${px(gauche)})`;
}

export function interpolerBoite(a: Boite, b: Boite, k: number): Boite {
  return {
    x: entre(a.x, b.x, k),
    y: entre(a.y, b.y, k),
    largeur: entre(a.largeur, b.largeur, k),
    hauteur: entre(a.hauteur, b.hauteur, k),
  };
}

/** État à l'avancement `k` : boîte et échelle interpolées, opacité selon qu'il paraît, part ou reste. */
export function etatA(depart: EtatNoeud, arrivee: EtatNoeud, k: number): EtatNoeud {
  const opacite =
    arrivee.opacite > depart.opacite
      ? entre(depart.opacite, arrivee.opacite, rampeApparition(k))
      : arrivee.opacite < depart.opacite
        ? entre(depart.opacite, arrivee.opacite, rampeDisparition(k))
        : arrivee.opacite;
  return {
    boite: interpolerBoite(depart.boite, arrivee.boite, k),
    opacite,
    echelle: entre(depart.echelle, arrivee.echelle, k),
  };
}

/**
 * Place d'un nœud dans un cadre : `contenu` (étapes, cercles d'une vue de section) grandit ou rapetisse avec le cadre,
 * `bord` (cercles posés sur la bordure) reste sur la bordure à sa taille, `titre` reste attaché au coin du cadre.
 */
export type PlaceDansCadre = 'contenu' | 'bord' | 'titre';

/**
 * Nœud à l'intérieur d'un cadre qui change de taille, vu à une autre taille du cadre : même place relative, en proportion
 * (il reste dans le cadre), contenu à l'échelle (la plus petite des deux proportions, pour ne pas déborder).
 */
export function dansCadre(
  boite: Boite,
  cadre: Boite,
  autreCadre: Boite,
  place: PlaceDansCadre = 'contenu',
): { boite: Boite; echelle: number } {
  const sx = cadre.largeur > 0 ? autreCadre.largeur / cadre.largeur : 1;
  const sy = cadre.hauteur > 0 ? autreCadre.hauteur / cadre.hauteur : 1;
  if (place === 'titre')
    return {
      boite: { ...boite, x: autreCadre.x + (boite.x - cadre.x), y: autreCadre.y + (boite.y - cadre.y) },
      echelle: 1,
    };
  if (place === 'bord') {
    const cx = autreCadre.x + (boite.x + boite.largeur / 2 - cadre.x) * sx;
    const cy = autreCadre.y + (boite.y + boite.hauteur / 2 - cadre.y) * sy;
    return { boite: { ...boite, x: cx - boite.largeur / 2, y: cy - boite.hauteur / 2 }, echelle: 1 };
  }
  const echelle = Math.min(sx, sy);
  return {
    boite: {
      x: autreCadre.x + (boite.x - cadre.x) * sx,
      y: autreCadre.y + (boite.y - cadre.y) * sy,
      largeur: boite.largeur * echelle,
      hauteur: boite.hauteur * echelle,
    },
    echelle,
  };
}

/** Point de `boite` mis à la même place relative dans `autre` (un bout de lien sur le bord d'un nœud reste sur ce bord). */
function rapporter(p: Point, boite: Boite, autre: Boite): Point {
  const u = boite.largeur > 0 ? (p.x - boite.x) / boite.largeur : 0;
  const v = boite.hauteur > 0 ? (p.y - boite.y) / boite.hauteur : 0;
  return { x: autre.x + u * autre.largeur, y: autre.y + v * autre.hauteur };
}

/**
 * Tracé d'un lien pendant le mouvement : son tracé de référence (rangement d'arrivée, ou de départ pour un lien qui part),
 * dont les bouts suivent leurs nœuds (`ref*` : boîtes de référence, `*` : boîtes à l'instant) ; les points intermédiaires
 * se déplacent comme les deux bouts, en proportion de leur distance le long du tracé. À l'arrivée, c'est le tracé d'ELK.
 */
export function routeSuivant(
  route: readonly Point[],
  refSource: Boite,
  source: Boite,
  refCible: Boite,
  cible: Boite,
): Point[] {
  if (route.length === 0) return [];
  const debut = route[0];
  const fin = route[route.length - 1];
  const a = rapporter(debut, refSource, source);
  const b = rapporter(fin, refCible, cible);
  const da = { x: a.x - debut.x, y: a.y - debut.y };
  const db = { x: b.x - fin.x, y: b.y - fin.y };
  const longueurs = [0];
  for (let i = 1; i < route.length; i++)
    longueurs.push(longueurs[i - 1] + Math.hypot(route[i].x - route[i - 1].x, route[i].y - route[i - 1].y));
  const total = longueurs[longueurs.length - 1] || 1;
  return route.map((p, i) => {
    const w = longueurs[i] / total;
    return { x: p.x + da.x * (1 - w) + db.x * w, y: p.y + da.y * (1 - w) + db.y * w };
  });
}

/** Chemin SVG d'une ligne brisée aux coudes arrondis (segments dans toutes les directions, pas seulement à angle droit). */
export function cheminArrondi(points: readonly Point[], rayon = 8): string {
  const pts = points.filter((p, i) => i === 0 || Math.hypot(p.x - points[i - 1].x, p.y - points[i - 1].y) > 0.01);
  if (pts.length < 2) return '';
  let d = `M ${pts[0].x} ${pts[0].y}`;
  for (let i = 1; i < pts.length - 1; i++) {
    const p = pts[i];
    const a = pts[i - 1];
    const b = pts[i + 1];
    const la = Math.hypot(p.x - a.x, p.y - a.y);
    const lb = Math.hypot(b.x - p.x, b.y - p.y);
    const r = Math.min(rayon, la / 2, lb / 2);
    const e = { x: p.x - ((p.x - a.x) / la) * r, y: p.y - ((p.y - a.y) / la) * r };
    const s = { x: p.x + ((b.x - p.x) / lb) * r, y: p.y + ((b.y - p.y) / lb) * r };
    d += ` L ${e.x} ${e.y} Q ${p.x} ${p.y} ${s.x} ${s.y}`;
  }
  const z = pts[pts.length - 1];
  return `${d} L ${z.x} ${z.y}`;
}
