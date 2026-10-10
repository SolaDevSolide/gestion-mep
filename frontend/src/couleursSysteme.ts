import type { CSSProperties } from 'react';

/** Systèmes connus du catalogue de démonstration : couleur stable d'après le nom. */
const CONNUS: Record<string, number> = { BDD: 0, API: 1, WEB: 2, LDAP: 3, MDM: 4, BATCH: 5 };
const NB_COULEURS = 8;

/** Indice de couleur (0 à 7) d'un système : le nom (en majuscules) pour les systèmes connus, sinon un hachage stable. */
export function indiceCouleur(nom: string): number {
  const cle = nom.trim().toUpperCase();
  if (cle in CONNUS) return CONNUS[cle];
  let h = 0;
  for (let i = 0; i < cle.length; i++) h = (h * 31 + cle.charCodeAt(i)) >>> 0;
  return h % NB_COULEURS;
}

/** Variables CSS (thème clair et sombre) pour colorer un système : `color`, `background`, `borderColor`. */
export function styleSysteme(nom: string): CSSProperties {
  const i = indiceCouleur(nom);
  return { color: `var(--sys-${i})`, backgroundColor: `var(--sys-${i}-bg)`, borderColor: `var(--sys-${i})` };
}

/** Couleur pleine seule (pastille). */
export const couleurSysteme = (nom: string): string => `var(--sys-${indiceCouleur(nom)})`;
