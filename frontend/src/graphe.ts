/**
 * Lignes du plan, sans React. Le graphe est libre (seuls les liens comptent, voir mep/graphe/modeleGraphe) ; le serveur
 * recalcule `ligne` (profondeur dans le graphe) et `ordre` après chaque opération, pour les exports, la CLI et comme
 * ordre stable du rangement.
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
