import type { ObjetCatalogue, ObjetImpacte } from '../../types';

/** Texte sans accents ni casse, pour comparer et rechercher. */
export function normaliser(texte: string): string {
  return texte.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();
}

const estSigle = (mot: string) => mot.length > 1 && mot === mot.toUpperCase() && /[A-Z]/.test(mot);

/**
 * Nom d'une catégorie accordé au nombre, en minuscules : « 1 table », « 2 tables », « 1 fichier d'échange »,
 * « 1 procédure stockée ». Les sigles (API) gardent leur casse.
 */
export function nommerCategorie(categorie: string, n: number): string {
  const mots = categorie.trim().split(/\s+/);
  const minuscule = (m: string) => (estSigle(m) ? m : m.toLowerCase());
  const pluriel = /s$/i.test(mots[0]) && !/ss$/i.test(mots[0]);
  if (n <= 1) {
    // « Procédures stockées » → « procédure stockée » ; « Base de données » reste tel quel.
    return mots
      .map((m) => (pluriel && /[^s]s$/i.test(m) && m.length > 3 && !estSigle(m) ? m.slice(0, -1) : m))
      .map(minuscule)
      .join(' ');
  }
  if (pluriel || estSigle(mots[0]) || /[sxz]$/i.test(mots[0])) return mots.map(minuscule).join(' ');
  const premier = /eau$/i.test(mots[0]) ? `${mots[0]}x` : `${mots[0]}s`;
  return [premier, ...mots.slice(1)].map(minuscule).join(' ');
}

/** « 2 tables, 1 vue » : nombre d'objets par catégorie, dans l'ordre donné. */
export function resumeObjets(categories: { nom: string; objets: unknown[] }[]): string {
  return categories
    .filter((c) => c.objets.length > 0)
    .map((c) => `${c.objets.length} ${nommerCategorie(c.nom, c.objets.length)}`)
    .join(', ');
}

/**
 * Objets d'un système rangés par catégorie : catégories dans l'ordre de la fiche du catalogue (puis les autres, par
 * nom), objets par nom.
 */
export function parCategorie(
  objets: ObjetImpacte[],
  ordreFiche: string[] = [],
): { nom: string; objets: ObjetImpacte[] }[] {
  const groupes = new Map<string, ObjetImpacte[]>();
  for (const o of objets) groupes.set(o.categorie, [...(groupes.get(o.categorie) ?? []), o]);
  const rang = (c: string) => {
    const i = ordreFiche.indexOf(c);
    return i < 0 ? Number.MAX_SAFE_INTEGER : i;
  };
  return [...groupes.entries()]
    .sort(([a], [b]) => rang(a) - rang(b) || a.localeCompare(b, 'fr'))
    .map(([nom, liste]) => ({ nom, objets: [...liste].sort((x, y) => x.nom.localeCompare(y.nom, 'fr')) }));
}

/**
 * Recherche multi-termes : chaque mot doit se trouver dans le nom, le libellé, le type ou la précision (sans tenir
 * compte des accents ni de la casse). « FACT » trouve aussi EDTPDF, qui s'exécute « après FACT03 ».
 */
export function correspond(
  objet: Pick<ObjetCatalogue, 'nom' | 'libelle' | 'type' | 'precision'>,
  recherche: string,
): boolean {
  const termes = normaliser(recherche).split(/\s+/).filter(Boolean);
  if (termes.length === 0) return true;
  const texte = normaliser([objet.nom, objet.libelle, objet.type, objet.precision].join(' '));
  return termes.every((t) => texte.includes(t));
}
