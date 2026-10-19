import type { Edge, Node } from '@xyflow/react';
import type { Etape } from '../../types';
import type { DonneesLien } from './LienGraphe';
import type { ModeleGraphe } from './modeleGraphe';
import type { DonneesNoeud } from './NoeudsGraphe';
import { CARTE, GRILLE, type Rangement } from './rangement';

/** Nœud et lien du graphe libre tels que React Flow les reçoit. */
export type NoeudRf = Node<DonneesNoeud>;
export type LienRf = Edge<DonneesLien>;

/** Zoom permis dans le graphe. */
export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 1.6;

/** Position de repli tant que le rangement n'est pas calculé : grille par ligne et ordre. */
function placeProvisoire(e: Etape | undefined, i: number) {
  return e
    ? { x: e.ordre * (CARTE.largeur + GRILLE * 2), y: e.ligne * (CARTE.hauteur + GRILLE * 3) }
    : { x: i * GRILLE, y: i * GRILLE };
}

/**
 * Superposition (React Flow en mode manuel) : cadres des sections dépliées, puis liens (ceux mis en avant au-dessus des autres,
 * tronçons partagés compris), puis cartes, titres et cercles (un lien ne passe jamais sur une carte), et la carte glissée
 * au-dessus de tout.
 */
export const Z = { cadre: 0, lien: 1, lienEnAvant: 2, noeud: 3, glisse: 10 };

/** Nœuds qu'on peut glisser : cartes (liens ou ordre) et sections (ordre seulement). */
export const GLISSABLE = new Set(['etape', 'section', 'cadre']);

/**
 * Nœuds React Flow du modèle, à leur place dans le rangement (sinon à leur place affichée, sinon à une place provisoire) ;
 * les mesures déjà prises par React Flow sont reprises pour le même genre de nœud.
 */
export function noeudsDepuis(
  modele: ModeleGraphe,
  rangement: Rangement | null,
  etapes: ReadonlyMap<string, Etape>,
  edition: boolean,
  precedents: ReadonlyMap<string, NoeudRf>,
): NoeudRf[] {
  return modele.noeuds.map((n, i) => {
    const place = rangement?.places.get(n.id);
    const avant = precedents.get(n.id);
    const position = place ? { x: place.x, y: place.y } : (avant?.position ?? placeProvisoire(etapes.get(n.id), i));
    const style =
      n.nature === 'cadre'
        ? { width: place?.largeur ?? 480, height: place?.hauteur ?? 240 }
        : n.nature === 'section'
          ? { width: place?.largeur ?? 288 }
          : undefined;
    return {
      id: n.id,
      type: n.nature,
      position,
      data: { etapeId: n.etapeId, sectionId: n.sectionId, cercleId: n.cercleId },
      ...(n.parent ? { parentId: n.parent } : {}),
      // Dans le plan, l'intérieur d'une section dépliée est en lecture seule : ses cartes ne se glissent pas.
      draggable: edition && GLISSABLE.has(n.nature) && !n.parent,
      selectable: false,
      connectable: edition,
      focusable: false,
      zIndex: n.nature === 'cadre' ? Z.cadre : Z.noeud,
      ...(style ? { style } : {}),
      // Mesure reprise seulement pour le même genre de nœud (une section repliée n'a pas la taille de son cadre déplié).
      ...(avant?.measured && avant.type === n.nature ? { measured: avant.measured } : {}),
    } satisfies NoeudRf;
  });
}
