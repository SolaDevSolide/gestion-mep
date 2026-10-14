/**
 * Bibliothèques (étapes modèles, recettes, résolutions types) : fonctions pures, sans React.
 *
 * On arrive sur le choix du système (catalogue, ordre alphabétique), puis la bibliothèque du système. La recherche
 * accepte plusieurs mots : un mot égal au nom d'un système du catalogue limite à ce système, les autres doivent tous
 * figurer dans les textes de l'élément (nom ou type d'une étape, titre d'une recette, titre ou symptômes d'une
 * résolution). Les éléments sont triés par nombre d'utilisations, puis par ordre de création (le plus ancien d'abord).
 */
import { normaliser, termes } from './recherche';
import type { ModeleEtape, TypeEtape } from './types';

/** Ce que la recherche a compris : systèmes reconnus (noms du catalogue) et autres mots (normalisés). */
export interface AnalyseRecherche {
  systemes: string[];
  mots: string[];
}

/** « BDD sauvegarde » → systèmes [BDD], mots [sauvegarde] (casse et accents ignorés). */
export function analyserRecherche(recherche: string, nomsSystemes: string[]): AnalyseRecherche {
  const parNorme = new Map(nomsSystemes.map((s) => [normaliser(s), s]));
  const ts = termes(recherche);
  return {
    systemes: [...new Set(ts.filter((t) => parNorme.has(t)).map((t) => parNorme.get(t)!))],
    mots: ts.filter((t) => !parNorme.has(t)),
  };
}

export const rechercheVide = (a: AnalyseRecherche) => a.systemes.length === 0 && a.mots.length === 0;

/**
 * L'élément touche tous les systèmes cités (un élément sans système précis vaut pour tous) et contient tous les autres
 * mots dans l'un de ses textes.
 */
export function correspondRecherche(
  a: AnalyseRecherche,
  el: { systemes: string[]; tousSystemes?: boolean; textes: string[] },
): boolean {
  const texte = normaliser(el.textes.join('\n'));
  return (
    (el.tousSystemes || a.systemes.every((s) => el.systemes.includes(s))) && a.mots.every((m) => texte.includes(m))
  );
}

/** Tri des bibliothèques : nombre d'utilisations (décroissant), puis ordre de création (le plus ancien d'abord). */
export function trierParUsage<T extends { utilisations: number }>(liste: T[], creation: (t: T) => string): T[] {
  return [...liste].sort(
    (a, b) => b.utilisations - a.utilisations || Date.parse(creation(a)) - Date.parse(creation(b)),
  );
}

/** Tri alphabétique des systèmes (choix du système). */
export const trierAlpha = <T extends { nom: string }>(l: T[]) =>
  [...l].sort((a, b) => a.nom.localeCompare(b.nom, 'fr'));

/** Types d'étape proposés au filtre et dans les formulaires : la liste s'allongera avec les futurs types. */
export const TYPES_ETAPE: { valeur: TypeEtape; libelle: string }[] = [
  { valeur: 'Standard', libelle: 'Normale' },
  { valeur: 'Sauvegarde', libelle: 'Sauvegarde' },
];

export const libelleType = (t: TypeEtape) => TYPES_ETAPE.find((x) => x.valeur === t)?.libelle ?? t;

/** « 3 étapes modèles », « 1 étape modèle », « 0 étape modèle ». */
export const nombre = (n: number, [un, plusieurs]: readonly [string, string]) => `${n} ${n > 1 ? plusieurs : un}`;

/**
 * Étapes modèles d'une bibliothèque, triées par usage : système courant (id du catalogue), systèmes proposés (au moins un
 * en commun), type d'étape, et recherche (systèmes cités ; autres mots dans le nom ou le type).
 */
export function filtrerEtapesModeles(
  modeles: ModeleEtape[],
  nomSysteme: (id: string) => string,
  analyse: AnalyseRecherche,
  opts: { systemeId?: string | null; type?: TypeEtape | ''; parmi?: string[] } = {},
): ModeleEtape[] {
  return trierParUsage(
    modeles.filter(
      (m) =>
        (!opts.systemeId || m.systemeCatalogueIds.includes(opts.systemeId)) &&
        (!opts.parmi || m.systemeCatalogueIds.some((s) => opts.parmi!.includes(s))) &&
        (!opts.type || m.type === opts.type) &&
        correspondRecherche(analyse, {
          systemes: m.systemeCatalogueIds.map(nomSysteme),
          textes: [m.action, libelleType(m.type)],
        }),
    ),
    (m) => m.creeLe,
  );
}

/** Répartition par type des étapes modèles d'un système : « 4 normales · 2 sauvegardes ». */
export function repartitionTypes(modeles: ModeleEtape[]): string {
  if (modeles.length === 0) return 'Aucune étape modèle';
  return TYPES_ETAPE.map((t) => {
    const n = modeles.filter((m) => m.type === t.valeur).length;
    return n > 0 ? `${n} ${t.libelle.toLowerCase()}${n > 1 ? 's' : ''}` : '';
  })
    .filter(Boolean)
    .join(' · ');
}
