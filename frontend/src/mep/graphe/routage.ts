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
