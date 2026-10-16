import type { ModeleGraphe, NoeudModele } from './modeleGraphe';
import { tailleSectionRepliee, type Taille } from './rangement';

/** Préfixe des nœuds de mesure : copies invisibles d'un nœud pas encore mesuré, rendues pour connaître sa taille. */
export const PREFIXE_MESURE = 'mesure:';

/**
 * Nœuds du modèle dont la taille dépend de leur contenu (étapes, sections repliées) et pas encore mesurés. Tant qu'il en
 * reste, un rangement serait provisoire : au repli ou au dépli d'une section, l'affichage attend ces mesures.
 */
export function mesuresManquantes(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): NoeudModele[] {
  return modele.noeuds.filter((n) => (n.nature === 'etape' || n.nature === 'section') && !tailles.has(n.id));
}

/**
 * Nœud affiché à l'intérieur duquel mesurer une copie invisible de `n` : son cadre s'il est affiché, sinon le nœud
 * lui-même (section repliée mesurée dans son cadre encore déplié), sinon le premier nœud affiché. Posée à l'intérieur
 * d'un nœud existant, la copie ne change ni l'emprise du graphe ni la mini-carte.
 */
export function hoteMesure(n: NoeudModele, affiches: ReadonlySet<string>, premier: string | null): string | null {
  if (n.parent && affiches.has(n.parent)) return n.parent;
  if (affiches.has(n.id)) return n.id;
  return premier;
}

/**
 * Taille à retenir pour le rangement, d'après une mesure de React Flow et le nœud tel qu'il était affiché à cet instant
 * (`genre` : son type à l'écran, `hauteurImposee` : sa hauteur fixée par une animation), ou null pour l'ignorer.
 * - Étape : sa taille dépend seulement de son contenu, toute mesure est bonne.
 * - Section repliée : sa largeur est fixée par le rangement (`tailleSectionRepliee`), seule sa hauteur se mesure, et
 *   seulement quand elle est affichée à cette largeur, sans hauteur imposée. Une mesure prise pendant un mouvement (largeur
 *   ou hauteur en cours d'animation, mouvement interrompu par un autre clic) serait sinon reprise par le rangement, puis
 *   imposée au nœud, puis mesurée à nouveau : la section garderait une mauvaise forme jusqu'au rechargement.
 * - Autres nœuds (cadres, titres, cercles) : taille calculée, pas mesurée.
 */
export function tailleRetenue(
  genre: string | undefined,
  modele: NoeudModele | undefined,
  hauteurImposee: boolean,
  d: { width: number; height: number },
): Taille | null {
  if (!modele || genre !== modele.nature) return null;
  if (genre === 'etape') return { largeur: d.width, hauteur: d.height };
  if (genre !== 'section' || hauteurImposee) return null;
  const largeur = tailleSectionRepliee(modele).largeur;
  return Math.abs(d.width - largeur) <= 1 ? { largeur, hauteur: d.height } : null;
}

export interface Vue {
  x: number;
  y: number;
  zoom: number;
}

/**
 * Vue qui montre en entier une boîte (section qu'on vient de déplier), avec un déplacement minimal à partir de `vue` ;
 * dézoom seulement si elle ne tient pas.
 */
export function vueMontrant(
  vue: Vue,
  boite: { x: number; y: number; largeur: number; hauteur: number },
  zone: { largeur: number; hauteur: number },
  marge: number,
  zoomMin: number,
): Vue {
  const { largeur: w, hauteur: h } = boite;
  if (w * vue.zoom > zone.largeur - 2 * marge || h * vue.zoom > zone.hauteur - 2 * marge) {
    const zoom = Math.max(zoomMin, Math.min(vue.zoom, (zone.largeur - 2 * marge) / w, (zone.hauteur - 2 * marge) / h));
    return {
      x: zone.largeur / 2 - (boite.x + w / 2) * zoom,
      y: h * zoom > zone.hauteur - 2 * marge ? marge - boite.y * zoom : zone.hauteur / 2 - (boite.y + h / 2) * zoom,
      zoom,
    };
  }
  const gauche = boite.x * vue.zoom + vue.x;
  const haut = boite.y * vue.zoom + vue.y;
  let dx = 0;
  let dy = 0;
  if (gauche < marge) dx = marge - gauche;
  else if (gauche + w * vue.zoom > zone.largeur - marge) dx = zone.largeur - marge - (gauche + w * vue.zoom);
  if (haut < marge) dy = marge - haut;
  else if (haut + h * vue.zoom > zone.hauteur - marge) dy = zone.hauteur - marge - (haut + h * vue.zoom);
  return { x: vue.x + dx, y: vue.y + dy, zoom: vue.zoom };
}
