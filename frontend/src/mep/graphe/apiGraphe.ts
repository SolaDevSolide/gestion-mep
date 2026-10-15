import { request } from '../../api';
import type { Mep, PriseVerrou, SectionCreee, SensCercle, TypePlan, Verrou } from '../../types';

/**
 * Opérations du graphe libre et des sections : appels serveur isolés ici. Chaque écriture renvoie la MEP à jour (le
 * serveur recompile les cercles en préalables effectifs et recalcule lignes et ordres) et la diffuse sur le flux.
 */

/** Où créer (ou poser) une étape. */
export type PlaceEtape =
  /** La nouvelle étape attend cette carte ou ce cercle (sans toucher aux autres liens). */
  | { apres: string }
  /** Insertion sur un lien : `vers` n'attend plus `de` directement, il attend la nouvelle étape, qui attend `de`. */
  | { entre: { de: string; vers: string } }
  /** Mêmes préalables et mêmes suivants que la carte. */
  | { paralleleA: string }
  /** Étape isolée. */
  | Record<string, never>;

/** Où déplacer une carte : sur un lien (insertion) ou après une carte ; le serveur en déduit la section d'arrivée. */
export type Deplacement = { surLien: { de: string; vers: string } } | { apres: string };

const json = <T>(methode: string, chemin: string, corps?: unknown) =>
  request<T>(methode, chemin, corps ?? (methode === 'GET' || methode === 'DELETE' ? undefined : {}));

export const apiGraphe = {
  /** Création rapide ; `sectionId` absent : section déduite de la place (vue d'une section : la section). */
  creerEtape: (mepId: string, plan: TypePlan, place: PlaceEtape, sectionId: string | null = null) =>
    json<Mep>('POST', `/meps/${mepId}/etapes`, { plan, ...place, ...(sectionId ? { sectionId } : {}) }),
  insererModele: (
    mepId: string,
    modeleId: string,
    plan: TypePlan,
    place: PlaceEtape,
    sectionId: string | null = null,
  ) =>
    json<Mep>('POST', `/meps/${mepId}/etapes/depuis-modele`, {
      modeleId,
      plan,
      ...place,
      ...(sectionId ? { sectionId } : {}),
    }),
  /** La carte quitte sa place (ses suivants attendent ses préalables) puis s'insère à la nouvelle. */
  deplacerEtape: (mepId: string, etapeId: string, cible: Deplacement) =>
    json<Mep>('POST', `/meps/${mepId}/etapes/${etapeId}/deplacer`, cible),

  /** `vers` attendra `de` : étapes, ou cercles de section (source d'une entrée, étape d'une section, sortie attendue). */
  lier: (mepId: string, de: string, vers: string) => json<Mep>('POST', `/meps/${mepId}/liens`, { de, vers }),
  delier: (mepId: string, de: string, vers: string) => json<Mep>('POST', `/meps/${mepId}/liens/retirer`, { de, vers }),

  /** Nouvelle section (dans le bac sauf `placee`), avec ses cercles « Début » et « Fin ». */
  creerSection: (mepId: string, plan: TypePlan, titre: string, systemeIds: string[], placee = false) =>
    json<SectionCreee>('POST', `/meps/${mepId}/sections`, { plan, titre, systemeIds, placee }),
  modifierSection: (
    mepId: string,
    sectionId: string,
    s: { revision: number; titre: string; description: string; systemeIds: string[] },
  ) => json<Mep>('PUT', `/meps/${mepId}/sections/${sectionId}`, s),
  /** Placer dans le plan (depuis le bac) ou remettre dans le bac. */
  placerSection: (mepId: string, sectionId: string, placee: boolean) =>
    json<Mep>('PUT', `/meps/${mepId}/sections/${sectionId}/placement`, { placee }),
  /** Ses étapes restent dans le plan, avec leurs préalables effectifs. */
  supprimerSection: (mepId: string, sectionId: string) => json<Mep>('DELETE', `/meps/${mepId}/sections/${sectionId}`),

  creerCercle: (mepId: string, sectionId: string, sens: SensCercle, titre: string, description = '') =>
    json<Mep>('POST', `/meps/${mepId}/sections/${sectionId}/ports`, { sens, titre, description }),
  modifierCercle: (mepId: string, sectionId: string, cercleId: string, titre: string, description: string) =>
    json<Mep>('PUT', `/meps/${mepId}/sections/${sectionId}/ports/${cercleId}`, { titre, description }),
  supprimerCercle: (mepId: string, sectionId: string, cercleId: string) =>
    json<Mep>('DELETE', `/meps/${mepId}/sections/${sectionId}/ports/${cercleId}`),
  /**
   * Ordre gauche-droite du graphe, pour tout le monde : `ids`, étapes et sections du plan dans l'ordre voulu (les absents
   * suivent). Les liens ne changent pas.
   */
  ordonner: (mepId: string, plan: TypePlan, ids: string[]) => json<Mep>('PUT', `/meps/${mepId}/ordre`, { plan, ids }),
  ordonnerCercles: (mepId: string, sectionId: string, sens: SensCercle, ports: string[]) =>
    json<Mep>('PUT', `/meps/${mepId}/sections/${sectionId}/ports/ordre`, { sens, ports }),
};

/** Verrou d'édition par étape : pris à l'ouverture du panneau, renouvelé tant qu'il est ouvert, relâché à la fermeture. */
export const apiVerrou = {
  /** Prend ou renouvelle le verrou ; `prendreLaMain` : sans confirmation, l'autre personne passe en lecture seule. */
  prendre: (mepId: string, etapeId: string, session: string, prendreLaMain = false) =>
    json<PriseVerrou>('POST', `/meps/${mepId}/etapes/${etapeId}/verrou`, {
      session,
      ...(prendreLaMain ? { prendreLaMain } : {}),
    }),
  relacher: (mepId: string, etapeId: string, session: string) =>
    json<void>('DELETE', `/meps/${mepId}/etapes/${etapeId}/verrou?session=${encodeURIComponent(session)}`),
  liste: (mepId: string) => json<Verrou[]>('GET', `/meps/${mepId}/verrous`),
};
