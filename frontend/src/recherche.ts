/**
 * Recherche « intelligente » (sélecteur de résolutions types) : fonctions pures, sans React.
 *
 * - insensible à la casse et aux accents ;
 * - plusieurs mots : tous doivent être trouvés (dans le titre ou dans un des autres textes) ;
 * - classement : éléments prioritaires (même système que l'étape) d'abord, puis correspondance dans le titre, puis le reste.
 */

/** Minuscules sans accents (é → e, Œ → oe…). */
export function normaliser(s: string | null | undefined): string {
  return (s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/œ/gi, 'oe').replace(/æ/gi, 'ae').toLowerCase();
}

/** Mots de la recherche, normalisés, sans doublon. */
export function termes(recherche: string): string[] {
  return [
    ...new Set(
      normaliser(recherche)
        .split(/[\s,;]+/)
        .filter(Boolean),
    ),
  ];
}

export interface DocumentRecherche {
  titre: string;
  /** Autres champs où chercher (symptômes, diagnostic, nom du système…). */
  textes: string[];
  /** Mis en tête du classement (ex. même système que l'étape). */
  prioritaire?: boolean;
}

/** Tous les termes sont trouvés quelque part dans le document. */
export function correspond(doc: DocumentRecherche, ts: string[]): boolean {
  if (ts.length === 0) return true;
  const tout = normaliser([doc.titre, ...doc.textes].join('\n'));
  return ts.every((t) => tout.includes(t));
}

/** Pertinence : priorité, puis nombre de termes trouvés dans le titre (bonus si le titre commence par un terme). */
export function score(doc: DocumentRecherche, ts: string[]): number {
  const titre = normaliser(doc.titre);
  const dansTitre = ts.filter((t) => titre.includes(t)).length;
  const debut = ts.some((t) => titre.startsWith(t) || titre.includes(` ${t}`)) ? 1 : 0;
  return (doc.prioritaire ? 1000 : 0) + (ts.length > 0 && dansTitre === ts.length ? 100 : 0) + dansTitre * 10 + debut;
}

/** Filtre et classe les éléments ; à pertinence égale, ordre alphabétique du titre. */
export function classer<T>(elements: T[], recherche: string, versDocument: (e: T) => DocumentRecherche): T[] {
  const ts = termes(recherche);
  return elements
    .map((e) => ({ e, d: versDocument(e) }))
    .filter(({ d }) => correspond(d, ts))
    .map((x) => ({ ...x, s: score(x.d, ts) }))
    .sort((a, b) => b.s - a.s || a.d.titre.localeCompare(b.d.titre, 'fr'))
    .map(({ e }) => e);
}

export interface Segment {
  texte: string;
  trouve: boolean;
}

/** Découpe un texte en segments, en marquant les occurrences des termes (comparaison sans casse ni accents). */
export function surligner(texte: string, ts: string[]): Segment[] {
  if (!texte) return [];
  // Texte normalisé caractère par caractère, avec l'index d'origine de chaque caractère normalisé.
  let norm = '';
  const origine: number[] = [];
  for (let i = 0; i < texte.length; i++) {
    const n = normaliser(texte[i]);
    for (const c of n) {
      norm += c;
      origine.push(i);
    }
  }
  const marque = Array.from({ length: texte.length }, () => false);
  for (const t of ts) {
    if (!t) continue;
    let p = norm.indexOf(t);
    while (p >= 0) {
      for (let k = p; k < p + t.length; k++) marque[origine[k]] = true;
      p = norm.indexOf(t, p + t.length);
    }
  }
  const segments: Segment[] = [];
  for (let i = 0; i < texte.length; i++) {
    const dernier = segments[segments.length - 1];
    if (dernier && dernier.trouve === marque[i]) dernier.texte += texte[i];
    else segments.push({ texte: texte[i], trouve: marque[i] });
  }
  return segments;
}

/**
 * Résolution multi-systèmes : elle s'applique à un ensemble de systèmes si elle n'en cible aucun (tous les systèmes)
 * ou au moins un de l'ensemble.
 */
export const concerne = (systemesResolution: string[], systemes: string[]): boolean =>
  systemesResolution.length === 0 || systemesResolution.some((s) => systemes.includes(s));

/** « Même système » que l'étape : au moins un système en commun (une résolution « tous systèmes » ne compte pas). */
export const memeSysteme = (systemesResolution: string[], systemesEtape: string[]): boolean =>
  systemesResolution.some((s) => systemesEtape.includes(s));
