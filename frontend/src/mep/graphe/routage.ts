import type { Point } from './rangement';

/**
 * Dessin des liens routés par ELK : coudes arrondis et petits sauts aux croisements (un segment horizontal saute par-dessus
 * les segments verticaux des autres liens), pour suivre un lien d'un bout à l'autre même quand il en croise d'autres.
 */
export const RAYON_COUDE = 8;
export const RAYON_SAUT = 5;

interface Segment {
  a: Point;
  b: Point;
}

const segments = (pts: Point[]): Segment[] => pts.slice(1).map((b, i) => ({ a: pts[i], b }));
const horizontal = (s: Segment) => Math.abs(s.a.y - s.b.y) < 0.5 && Math.abs(s.a.x - s.b.x) >= 0.5;
const vertical = (s: Segment) => Math.abs(s.a.x - s.b.x) < 0.5 && Math.abs(s.a.y - s.b.y) >= 0.5;

/** Points utiles : sans doublons ni points alignés au milieu d'un segment droit. */
export function simplifier(pts: Point[]): Point[] {
  const sansDoublons = pts.filter((p, i) => i === 0 || Math.hypot(p.x - pts[i - 1].x, p.y - pts[i - 1].y) > 0.5);
  return sansDoublons.filter((p, i, t) => {
    if (i === 0 || i === t.length - 1) return true;
    const a = t[i - 1];
    const b = t[i + 1];
    return !(
      (Math.abs(a.x - p.x) < 0.5 && Math.abs(p.x - b.x) < 0.5) ||
      (Math.abs(a.y - p.y) < 0.5 && Math.abs(p.y - b.y) < 0.5)
    );
  });
}

/**
 * Sauts de chaque lien : pour chaque segment horizontal (par son indice dans le tracé simplifié), les abscisses où il croise un
 * segment vertical d'un autre lien, loin des coudes et des extrémités.
 */
export function sautsDesLiens(routes: ReadonlyMap<string, Point[]>): Map<string, Map<number, number[]>> {
  const verticaux: { id: string; s: Segment }[] = [];
  for (const [id, pts] of routes) for (const s of segments(simplifier(pts))) if (vertical(s)) verticaux.push({ id, s });
  const marge = RAYON_COUDE + RAYON_SAUT + 1;
  const res = new Map<string, Map<number, number[]>>();
  for (const [id, pts] of routes) {
    const parSegment = new Map<number, number[]>();
    segments(simplifier(pts)).forEach((h, i) => {
      if (!horizontal(h)) return;
      const [x1, x2] = h.a.x < h.b.x ? [h.a.x, h.b.x] : [h.b.x, h.a.x];
      const xs: number[] = [];
      for (const v of verticaux) {
        if (v.id === id) continue;
        const [y1, y2] = v.s.a.y < v.s.b.y ? [v.s.a.y, v.s.b.y] : [v.s.b.y, v.s.a.y];
        const x = v.s.a.x;
        if (x > x1 + marge && x < x2 - marge && h.a.y > y1 + 1 && h.a.y < y2 - 1) xs.push(x);
      }
      // Deux sauts trop proches n'en font qu'un.
      const tries = [...new Set(xs)]
        .sort((a, b) => a - b)
        .filter((x, k, t) => k === 0 || x - t[k - 1] > 2 * RAYON_SAUT + 2);
      if (tries.length) parSegment.set(i, tries);
    });
    if (parSegment.size) res.set(id, parSegment);
  }
  return res;
}

/** Chemin SVG d'un lien orthogonal : coudes arrondis, sauts sur les segments horizontaux. */
export function cheminLien(points: Point[], sauts: ReadonlyMap<number, number[]> = new Map()): string {
  const pts = simplifier(points);
  if (pts.length < 2) return '';
  const p0 = pts[0];
  let d = `M ${p0.x} ${p0.y}`;
  const segs = segments(pts);
  segs.forEach((s, i) => {
    const suivant = segs[i + 1];
    // Fin du segment : raccourcie pour l'arrondi du coude suivant.
    const longueur = Math.hypot(s.b.x - s.a.x, s.b.y - s.a.y);
    const r = suivant
      ? Math.min(RAYON_COUDE, longueur / 2, Math.hypot(suivant.b.x - suivant.a.x, suivant.b.y - suivant.a.y) / 2)
      : 0;
    const ux = Math.sign(s.b.x - s.a.x);
    const uy = Math.sign(s.b.y - s.a.y);
    for (const x of horizontal(s) ? (sauts.get(i) ?? []).slice().sort((a, b) => (a - b) * ux) : []) {
      d += ` L ${x - ux * RAYON_SAUT} ${s.a.y} A ${RAYON_SAUT} ${RAYON_SAUT} 0 0 ${ux > 0 ? 1 : 0} ${x + ux * RAYON_SAUT} ${s.a.y}`;
    }
    const fin = { x: s.b.x - ux * r, y: s.b.y - uy * r };
    d += ` L ${fin.x} ${fin.y}`;
    if (suivant && r > 0) {
      const vx = Math.sign(suivant.b.x - suivant.a.x);
      const vy = Math.sign(suivant.b.y - suivant.a.y);
      d += ` Q ${s.b.x} ${s.b.y} ${s.b.x + vx * r} ${s.b.y + vy * r}`;
    }
  });
  return d;
}

/** Milieu du plus long segment : place du « + » et de l'étiquette d'un lien. */
export function milieuLien(points: Point[]): Point {
  const segs = segments(simplifier(points));
  let meilleur = segs[0];
  for (const s of segs)
    if (
      meilleur &&
      Math.hypot(s.b.x - s.a.x, s.b.y - s.a.y) > Math.hypot(meilleur.b.x - meilleur.a.x, meilleur.b.y - meilleur.a.y)
    )
      meilleur = s;
  if (!meilleur) return points[0] ?? { x: 0, y: 0 };
  return { x: (meilleur.a.x + meilleur.b.x) / 2, y: (meilleur.a.y + meilleur.b.y) / 2 };
}

/** Distance d'un point à un segment. */
function distanceSegment(p: Point, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const l2 = dx * dx + dy * dy;
  const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2));
  return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
}

/**
 * Liens qui passent à moins de `tolerance` du point (coordonnées du graphe), du plus proche au plus lointain. Sur un tronçon
 * partagé (liens fusionnés par une même source ou une même cible), tous les liens qui l'empruntent sont rendus.
 */
export function liensAuPoint(routes: ReadonlyMap<string, Point[]>, p: Point, tolerance: number): string[] {
  const res: { id: string; d: number }[] = [];
  for (const [id, pts] of routes) {
    let d = Infinity;
    for (let i = 1; i < pts.length; i++) d = Math.min(d, distanceSegment(p, pts[i - 1], pts[i]));
    if (d <= tolerance) res.push({ id, d });
  }
  return res.sort((a, b) => a.d - b.d || a.id.localeCompare(b.id)).map((x) => x.id);
}

/**
 * Croisements et superpositions entre les tracés de liens distincts : deux segments qui se coupent (hors extrémités
 * communes), et deux segments colinéaires qui se recouvrent alors que les liens n'ont ni la même source ni la même cible.
 */
export function compterCroisements(
  routes: ReadonlyMap<string, Point[]>,
  bouts: ReadonlyMap<string, { source: string; cible: string }>,
): { croisements: number; superpositions: number } {
  const segs: { id: string; a: Point; b: Point }[] = [];
  for (const [id, pts] of routes) for (const s of segments(simplifier(pts))) segs.push({ id, a: s.a, b: s.b });
  let croisements = 0;
  let superpositions = 0;
  const e = 0.5;
  for (let i = 0; i < segs.length; i++)
    for (let j = i + 1; j < segs.length; j++) {
      const s = segs[i];
      const t = segs[j];
      if (s.id === t.id) continue;
      const hs = Math.abs(s.a.y - s.b.y) < e;
      const ht = Math.abs(t.a.y - t.b.y) < e;
      const [sx1, sx2] = [Math.min(s.a.x, s.b.x), Math.max(s.a.x, s.b.x)];
      const [sy1, sy2] = [Math.min(s.a.y, s.b.y), Math.max(s.a.y, s.b.y)];
      const [tx1, tx2] = [Math.min(t.a.x, t.b.x), Math.max(t.a.x, t.b.x)];
      const [ty1, ty2] = [Math.min(t.a.y, t.b.y), Math.max(t.a.y, t.b.y)];
      if (hs !== ht) {
        // Un horizontal, un vertical : croisement strict (pas à un bout).
        const [h, v] = hs ? [s, t] : [t, s];
        const [hx1, hx2] = [Math.min(h.a.x, h.b.x), Math.max(h.a.x, h.b.x)];
        const [vy1, vy2] = [Math.min(v.a.y, v.b.y), Math.max(v.a.y, v.b.y)];
        if (v.a.x > hx1 + e && v.a.x < hx2 - e && h.a.y > vy1 + e && h.a.y < vy2 - e) croisements++;
        continue;
      }
      const bs = bouts.get(s.id);
      const bt = bouts.get(t.id);
      if (bs && bt && (bs.source === bt.source || bs.cible === bt.cible)) continue;
      const recouvre = hs
        ? Math.abs(s.a.y - t.a.y) < e && Math.min(sx2, tx2) - Math.max(sx1, tx1) > 2
        : Math.abs(s.a.x - t.a.x) < e && Math.min(sy2, ty2) - Math.max(sy1, ty1) > 2;
      if (recouvre) superpositions++;
    }
  return { croisements, superpositions };
}
