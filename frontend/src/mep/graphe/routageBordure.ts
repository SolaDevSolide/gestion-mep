import type { LienModele } from './modeleGraphe';
import type { Point } from './rangement';
import { RAYON_COUDE, RAYON_SAUT, simplifier } from './routage';

/**
 * Compléments de routage.ts pour le rangement « cercles sur les bordures » : sauts aux seuls vrais croisements (pas sur un
 * tronc commun ni sur un coude), et contrôle qu'aucun tronçon partagé ne suggère une relation absente.
 */

const cle = (p: Point) => `${Math.round(p.x)},${Math.round(p.y)}`;

/**
 * Sauts de chaque lien (même forme que `sautsDesLiens`) : pour chaque segment horizontal (indice dans le tracé simplifié),
 * les abscisses où il croise le segment vertical d'un autre lien, loin des coudes, sauf là où un tracé a un coude ou une
 * séparation (jonction d'une fusion : ce n'est pas un croisement).
 */
export function sautsSansJonctions(routes: ReadonlyMap<string, Point[]>): Map<string, Map<number, number[]>> {
  const verticaux: { id: string; a: Point; b: Point }[] = [];
  const jonctions = new Set<string>();
  const simples = new Map<string, Point[]>();
  for (const [id, pts] of routes) {
    const p = simplifier(pts);
    simples.set(id, p);
    for (const q of p) jonctions.add(cle(q));
    for (let i = 1; i < p.length; i++)
      if (Math.abs(p[i].x - p[i - 1].x) < 0.5 && Math.abs(p[i].y - p[i - 1].y) >= 0.5)
        verticaux.push({ id, a: p[i - 1], b: p[i] });
  }
  const marge = RAYON_COUDE + RAYON_SAUT + 1;
  const res = new Map<string, Map<number, number[]>>();
  for (const [id, p] of simples) {
    const parSegment = new Map<number, number[]>();
    for (let i = 1; i < p.length; i++) {
      const a = p[i - 1];
      const b = p[i];
      if (Math.abs(a.y - b.y) >= 0.5 || Math.abs(a.x - b.x) < 0.5) continue;
      const [x1, x2] = a.x < b.x ? [a.x, b.x] : [b.x, a.x];
      const xs: number[] = [];
      for (const v of verticaux) {
        if (v.id === id) continue;
        const [y1, y2] = v.a.y < v.b.y ? [v.a.y, v.b.y] : [v.b.y, v.a.y];
        const x = v.a.x;
        if (x > x1 + marge && x < x2 - marge && a.y > y1 + 1 && a.y < y2 - 1 && !jonctions.has(cle({ x, y: a.y })))
          xs.push(x);
      }
      const tries = [...new Set(xs)]
        .sort((u, w) => u - w)
        .filter((x, k, t) => k === 0 || x - t[k - 1] > 2 * RAYON_SAUT + 2);
      if (tries.length) parSegment.set(i - 1, tries);
    }
    if (parSegment.size) res.set(id, parSegment);
  }
  return res;
}

/**
 * Relations qu'on lirait à tort sur le tracé : deux liens sans bout commun partagent un tronçon (plus de 2 px), alors on
 * peut suivre le trait de la source de l'un jusqu'à la cible de l'autre ; comptées si ce lien n'existe pas.
 */
export function faussesRelations(routes: ReadonlyMap<string, Point[]>, liens: readonly LienModele[]): string[] {
  const parId = new Map(liens.map((l) => [l.id, l]));
  const existe = new Set(liens.map((l) => `${l.source}>${l.cible}`));
  const segs: { id: string; a: Point; b: Point }[] = [];
  for (const [id, pts] of routes) {
    const p = simplifier(pts);
    for (let i = 1; i < p.length; i++) segs.push({ id, a: p[i - 1], b: p[i] });
  }
  const res = new Set<string>();
  const e = 0.5;
  for (let i = 0; i < segs.length; i++)
    for (let j = i + 1; j < segs.length; j++) {
      const s = segs[i];
      const t = segs[j];
      const ls = parId.get(s.id);
      const lt = parId.get(t.id);
      if (!ls || !lt || s.id === t.id) continue;
      if (`${ls.source}|${ls.sourcePoignee}` === `${lt.source}|${lt.sourcePoignee}`) continue;
      if (`${ls.cible}|${ls.ciblePoignee}` === `${lt.cible}|${lt.ciblePoignee}`) continue;
      const h = (x: { a: Point; b: Point }) => Math.abs(x.a.y - x.b.y) < e;
      const v = (x: { a: Point; b: Point }) => Math.abs(x.a.x - x.b.x) < e;
      const recouvre =
        (h(s) &&
          h(t) &&
          Math.abs(s.a.y - t.a.y) < e &&
          Math.min(Math.max(s.a.x, s.b.x), Math.max(t.a.x, t.b.x)) -
            Math.max(Math.min(s.a.x, s.b.x), Math.min(t.a.x, t.b.x)) >
            2) ||
        (v(s) &&
          v(t) &&
          Math.abs(s.a.x - t.a.x) < e &&
          Math.min(Math.max(s.a.y, s.b.y), Math.max(t.a.y, t.b.y)) -
            Math.max(Math.min(s.a.y, s.b.y), Math.min(t.a.y, t.b.y)) >
            2);
      if (!recouvre) continue;
      for (const [a, b] of [
        [ls.source, lt.cible],
        [lt.source, ls.cible],
      ])
        if (!existe.has(`${a}>${b}`)) res.add(`${a}>${b}`);
    }
  return [...res].sort();
}
