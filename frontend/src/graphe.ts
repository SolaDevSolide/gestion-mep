/**
 * Placement de l'arbre d'exécution (façon graphe GitHub Actions).
 * Fonctions pures : aucune dépendance à React.
 *
 * - niveau d'une étape = longueur du plus long chemin depuis une étape sans préalable ;
 * - les étapes d'un même niveau sont empilées dans la même colonne ;
 * - les dépendances inconnues, auto-dépendances et arêtes fermant un cycle sont ignorées.
 */

export interface NoeudEntree {
  id: string;
  dependances: string[];
}

export interface Lien {
  de: string;
  vers: string;
}

export interface Graphe {
  /** Niveau de chaque étape (0 = sans préalable). */
  niveaux: Record<string, number>;
  /** Colonnes ordonnées : colonnes[n] = ids du niveau n, dans l'ordre d'affichage. */
  colonnes: string[][];
  /** Liens retenus (préalable → dépendante), cycles exclus. */
  liens: Lien[];
  /** Liens écartés car ils fermaient un cycle. */
  liensIgnores: Lien[];
  aCycle: boolean;
}

export function calculerGraphe(noeuds: NoeudEntree[]): Graphe {
  const ids = new Set(noeuds.map((n) => n.id));
  const index = new Map(noeuds.map((n, i) => [n.id, i]));
  const deps = new Map<string, string[]>();
  for (const n of noeuds) {
    const uniques = [...new Set(n.dependances)].filter((d) => ids.has(d) && d !== n.id);
    deps.set(n.id, uniques);
  }

  // Détection des cycles : parcours en profondeur, les arêtes vers un nœud « en cours » sont écartées.
  const etat = new Map<string, 0 | 1 | 2>();
  const liensIgnores: Lien[] = [];
  const retenues = new Map<string, string[]>();
  const visiter = (racine: string) => {
    const pile: { id: string; i: number }[] = [{ id: racine, i: 0 }];
    etat.set(racine, 1);
    retenues.set(racine, []);
    while (pile.length > 0) {
      const haut = pile[pile.length - 1];
      const liste = deps.get(haut.id) ?? [];
      if (haut.i >= liste.length) {
        etat.set(haut.id, 2);
        pile.pop();
        continue;
      }
      const d = liste[haut.i++];
      const e = etat.get(d) ?? 0;
      if (e === 1) {
        liensIgnores.push({ de: d, vers: haut.id });
      } else {
        retenues.get(haut.id)!.push(d);
        if (e === 0) {
          etat.set(d, 1);
          retenues.set(d, []);
          pile.push({ id: d, i: 0 });
        }
      }
    }
  };
  for (const n of noeuds) if (!etat.has(n.id)) visiter(n.id);

  // Niveaux (graphe acyclique garanti par l'étape précédente).
  const niveaux: Record<string, number> = {};
  const niveauDe = (id: string): number => {
    if (id in niveaux) return niveaux[id];
    const pred = retenues.get(id) ?? [];
    // Pose provisoire pour se prémunir de toute récursion inattendue.
    niveaux[id] = 0;
    const n = pred.length === 0 ? 0 : 1 + Math.max(...pred.map(niveauDe));
    niveaux[id] = n;
    return n;
  };
  for (const n of noeuds) niveauDe(n.id);

  const nbColonnes = noeuds.length === 0 ? 0 : Math.max(...Object.values(niveaux)) + 1;
  const colonnes: string[][] = Array.from({ length: nbColonnes }, () => []);
  for (const n of noeuds) colonnes[niveaux[n.id]].push(n.id);

  // Ordre dans la colonne : barycentre des préalables (limite les croisements), à égalité l'ordre d'origine.
  const position = new Map<string, number>();
  colonnes[0]?.forEach((id, i) => position.set(id, i));
  for (let c = 1; c < colonnes.length; c++) {
    const cle = (id: string) => {
      const pred = (retenues.get(id) ?? []).filter((p) => position.has(p));
      return pred.length === 0 ? Number.POSITIVE_INFINITY : pred.reduce((s, p) => s + position.get(p)!, 0) / pred.length;
    };
    colonnes[c].sort((a, b) => {
      const d = cle(a) - cle(b);
      if (Number.isNaN(d) || d === 0) return index.get(a)! - index.get(b)!;
      return d;
    });
    colonnes[c].forEach((id, i) => position.set(id, i));
  }

  const liens: Lien[] = [];
  for (const n of noeuds) for (const d of retenues.get(n.id) ?? []) liens.push({ de: d, vers: n.id });

  return { niveaux, colonnes, liens, liensIgnores, aCycle: liensIgnores.length > 0 };
}

export interface Dimensions {
  largeurNoeud: number;
  hauteurNoeud: number;
  espaceX: number;
  espaceY: number;
  marge: number;
}

export const DIMENSIONS_PAR_DEFAUT: Dimensions = { largeurNoeud: 228, hauteurNoeud: 80, espaceX: 72, espaceY: 14, marge: 8 };

export interface NoeudPlace {
  id: string;
  niveau: number;
  x: number;
  y: number;
}

export interface LienPlace extends Lien {
  chemin: string;
}

export interface Disposition {
  noeuds: NoeudPlace[];
  liens: LienPlace[];
  largeur: number;
  hauteur: number;
}

/** Coordonnées des nœuds et courbes de Bézier des liens (de la droite d'un préalable à la gauche d'une dépendante). */
export function placerGraphe(g: Graphe, dim: Dimensions = DIMENSIONS_PAR_DEFAUT): Disposition {
  const { largeurNoeud: w, hauteurNoeud: h, espaceX, espaceY, marge } = dim;
  const noeuds: NoeudPlace[] = [];
  const pos = new Map<string, NoeudPlace>();
  let hauteurMax = 0;
  g.colonnes.forEach((col, c) => {
    col.forEach((id, r) => {
      const n = { id, niveau: c, x: marge + c * (w + espaceX), y: marge + r * (h + espaceY) };
      noeuds.push(n);
      pos.set(id, n);
    });
    hauteurMax = Math.max(hauteurMax, col.length);
  });
  const liens: LienPlace[] = g.liens.map((l) => {
    const a = pos.get(l.de)!;
    const b = pos.get(l.vers)!;
    const x1 = a.x + w;
    const y1 = a.y + h / 2;
    const x2 = b.x;
    const y2 = b.y + h / 2;
    const dx = Math.max(24, (x2 - x1) / 2);
    return { ...l, chemin: `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}` };
  });
  const largeur = g.colonnes.length === 0 ? 0 : marge * 2 + g.colonnes.length * w + (g.colonnes.length - 1) * espaceX;
  const hauteur = hauteurMax === 0 ? 0 : marge * 2 + hauteurMax * h + (hauteurMax - 1) * espaceY;
  return { noeuds, liens, largeur, hauteur };
}
