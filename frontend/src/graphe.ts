/**
 * Graphe des étapes par lignes (spécification jalon 4, §6 et §8). Fonctions pures, sans React.
 *
 * - les étapes sont rangées par `ligne` (de haut en bas) puis par `ordre` (position dans la ligne) ;
 * - un lien descend toujours : le préalable est sur une ligne strictement au-dessus ;
 * - les liens de plusieurs étapes vers le même ensemble d'étapes forment une « jonction » :
 *   ils se rejoignent sur une barre horizontale puis repartent vers chaque cible.
 */

export interface Lien {
  de: string;
  vers: string;
}

export interface NoeudGraphe {
  id: string;
  ligne: number;
  ordre: number;
  dependances: string[];
}

/** Lignes du graphe : lignes[r] = étapes de la ligne r, dans l'ordre d'affichage. */
export function construireLignes<T extends Pick<NoeudGraphe, 'ligne' | 'ordre'>>(etapes: T[]): T[][] {
  const triees = [...etapes].sort((a, b) => a.ligne - b.ligne || a.ordre - b.ordre);
  const lignes: T[][] = [];
  let courante: number | null = null;
  for (const e of triees) {
    if (e.ligne !== courante) {
      lignes.push([]);
      courante = e.ligne;
    }
    lignes[lignes.length - 1].push(e);
  }
  return lignes;
}

/** Index de ligne affiché de chaque étape (0 = ligne du haut), robuste à d'éventuels trous de numérotation. */
export function indexLignes(lignes: { id: string }[][]): Map<string, number> {
  const m = new Map<string, number>();
  lignes.forEach((l, r) => l.forEach((e) => m.set(e.id, r)));
  return m;
}

/** Liens explicites du plan (préalable → étape), en ignorant les préalables inconnus. */
export function liensDuPlan(etapes: Pick<NoeudGraphe, 'id' | 'dependances'>[]): Lien[] {
  const ids = new Set(etapes.map((e) => e.id));
  const liens: Lien[] = [];
  for (const e of etapes)
    for (const d of new Set(e.dependances)) if (ids.has(d) && d !== e.id) liens.push({ de: d, vers: e.id });
  return liens;
}

/** Un lien est possible s'il descend (préalable sur une ligne strictement au-dessus) et n'existe pas déjà. */
export function lienPossible(
  de: string,
  vers: string,
  lignes: Map<string, number>,
  liens: Lien[],
): 'ok' | 'remonte' | 'existe' {
  const a = lignes.get(de);
  const b = lignes.get(vers);
  if (de === vers || a === undefined || b === undefined || a >= b) return 'remonte';
  if (liens.some((l) => l.de === de && l.vers === vers)) return 'existe';
  return 'ok';
}

export interface GroupeLiens {
  /** Clé stable : sources et cibles triées. */
  cle: string;
  sources: string[];
  cibles: string[];
}

/** Regroupe les sources qui attendent exactement le même ensemble de cibles (une jonction par ensemble). */
export function groupesDeLiens(liens: Lien[]): GroupeLiens[] {
  const ciblesPar = new Map<string, string[]>();
  for (const l of liens) ciblesPar.set(l.de, [...(ciblesPar.get(l.de) ?? []), l.vers]);
  const groupes = new Map<string, { sources: string[]; cibles: string[] }>();
  for (const [source, cibles] of ciblesPar) {
    const triees = [...new Set(cibles)].sort();
    const k = triees.join('|');
    const g = groupes.get(k) ?? { sources: [], cibles: triees };
    g.sources.push(source);
    groupes.set(k, g);
  }
  return [...groupes.values()].map((g) => {
    const sources = [...g.sources].sort();
    return { cle: `${sources.join('|')}>${g.cibles.join('|')}`, sources, cibles: g.cibles };
  });
}

/** Paires (source, cible) d'un groupe. */
export const pairesDuGroupe = (g: GroupeLiens): Lien[] =>
  g.sources.flatMap((de) => g.cibles.map((vers) => ({ de, vers })));

export interface Point {
  x: number;
  y: number;
}

/** Position mesurée d'une fiche : centre horizontal, bord haut et bord bas (repère du graphe). */
export interface Ancrage {
  x: number;
  haut: number;
  bas: number;
}

export interface TraceJonction {
  cle: string;
  /** Traits sans flèche (descentes des sources + barre), absent pour un lien simple. */
  traits: string | null;
  /** Un chemin fléché par cible. */
  fleches: string[];
  /** Points marqués sur la barre à chaque arrivée de source (jonction seulement). */
  points: Point[];
  /** Ordonnée de la barre. */
  barre: number;
}

const r1 = (n: number) => Math.round(n * 10) / 10;

/** Chemin droit à coudes arrondis : descend, longe la barre en y, puis redescend vers b. */
export function cheminCoude(a: Point, b: Point, y: number): string {
  if (Math.abs(a.x - b.x) < 1) return `M${r1(a.x)},${r1(a.y)} V${r1(b.y)}`;
  const sens = b.x > a.x ? 1 : -1;
  const r = Math.max(0, Math.min(8, Math.abs(b.x - a.x) / 2, Math.abs(y - a.y), Math.abs(b.y - y)));
  return `M${r1(a.x)},${r1(a.y)} V${r1(y - r)} Q${r1(a.x)},${r1(y)} ${r1(a.x + sens * r)},${r1(y)} H${r1(b.x - sens * r)} Q${r1(b.x)},${r1(y)} ${r1(b.x)},${r1(y + r)} V${r1(b.y)}`;
}

/**
 * Tracé des liens : une barre par groupe, à mi-hauteur entre la source la plus basse et la cible la plus haute ;
 * les barres reliant les mêmes niveaux sont décalées (0, +écart, −écart, +2·écart…) pour rester lisibles.
 */
export function tracerJonctions(
  groupes: GroupeLiens[],
  ancrages: Record<string, Ancrage | undefined>,
  ecart = 8,
): TraceJonction[] {
  const decalages = new Map<string, number>();
  const traces: TraceJonction[] = [];
  for (const g of groupes) {
    const S = g.sources
      .map((id) => ancrages[id])
      .filter((a): a is Ancrage => !!a)
      .map((a) => ({ x: a.x, y: a.bas }));
    const T = g.cibles
      .map((id) => ancrages[id])
      .filter((a): a is Ancrage => !!a)
      .map((a) => ({ x: a.x, y: a.haut }));
    if (S.length === 0 || T.length === 0) continue;
    const bas = Math.max(...S.map((s) => s.y));
    const haut = Math.min(...T.map((t) => t.y));
    const cleNiveau = `${Math.round(bas)}:${Math.round(haut)}`;
    const n = (decalages.get(cleNiveau) ?? -1) + 1;
    decalages.set(cleNiveau, n);
    const barre = (bas + haut) / 2 + (n % 2 ? 1 : -1) * Math.ceil(n / 2) * ecart;

    if (S.length === 1 && T.length === 1) {
      traces.push({
        cle: g.cle,
        traits: null,
        // Départ au bord bas de la fiche (centre du rond) ; le chemin s'arrête 2 px avant le bord haut, la pointe de la
        // flèche (refX 8 sur 10) atteint le bord.
        fleches: [cheminCoude(S[0], { x: T[0].x, y: T[0].y - 2 }, barre)],
        points: [],
        barre,
      });
      continue;
    }
    const xs = [...S, ...T].map((p) => p.x);
    const x1 = Math.min(...xs);
    const x2 = Math.max(...xs);
    const traits = `${S.map((s) => `M${r1(s.x)},${r1(s.y)} V${r1(barre)}`).join(' ')} M${r1(x1)},${r1(barre)} H${r1(x2)}`;
    const fleches = T.map((t) => `M${r1(t.x)},${r1(barre)} V${r1(t.y - 2)}`);
    traces.push({ cle: g.cle, traits, fleches, points: S.map((s) => ({ x: r1(s.x), y: r1(barre) })), barre });
  }
  return traces;
}

/** Courbe du lien en cours de tracé (rond tiré à la souris). */
export function courbeTemporaire(a: Point, b: Point): string {
  const dy = Math.max(30, (b.y - a.y) / 2);
  return `M${r1(a.x)},${r1(a.y)} C${r1(a.x)},${r1(a.y + dy)} ${r1(b.x)},${r1(b.y - dy)} ${r1(b.x)},${r1(b.y)}`;
}
