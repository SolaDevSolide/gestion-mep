import type { Etape } from '../../types';
import type { Deplacement } from './apiGraphe';
import { lienAutorise, POIGNEE_BAS, POIGNEE_HAUT, type LienModele } from './modeleGraphe';
import { infoSection, trouverCercle, type Section } from './sections';

/**
 * Interprétation des gestes du graphe libre, sans React : tracé d'un lien entre deux poignées, dépôt d'une carte glissée.
 */
/** `vers` attendra `de` (étapes ou cercles, voir le tableau des liens possibles du serveur), ou refus expliqué. */
export type ActionLien = { type: 'lier'; de: string; vers: string } | { type: 'refus'; raison: string };

export interface Connexion {
  source: string | null;
  sourceHandle?: string | null;
  target: string | null;
  targetHandle?: string | null;
}

/** Bout d'une connexion : une carte ou un cercle (d'une section repliée, dépliée, ou de la vue de la section). */
type Bout = { carte: string } | { cercle: string; section: string; sens: 'Entree' | 'Sortie' } | null;

function bout(sections: Section[], noeud: string | null, poignee: string | null | undefined): Bout {
  if (!noeud) return null;
  if (noeud.startsWith('c:')) {
    const t = trouverCercle(sections, noeud.slice(2));
    return t ? { cercle: t.cercle.id, section: t.section.id, sens: t.cercle.sens } : null;
  }
  if (noeud.startsWith('s:')) {
    if (!poignee || poignee === POIGNEE_HAUT || poignee === POIGNEE_BAS) return null;
    const t = trouverCercle(sections, poignee);
    return t ? { cercle: t.cercle.id, section: t.section.id, sens: t.cercle.sens } : null;
  }
  return { carte: noeud };
}

/** Ce que fait un lien tracé de la poignée `source` (bas) vers la poignée `target` (haut). */
export function interpreterConnexion(c: Connexion, etapes: Etape[], sections: Section[]): ActionLien | null {
  const a = bout(sections, c.source, c.sourceHandle);
  const b = bout(sections, c.target, c.targetHandle);
  if (!a || !b) return null;
  const sectionDe = (id: string) => {
    const e = etapes.find((x) => x.id === id);
    return e ? infoSection(e).sectionId : null;
  };
  if ('carte' in a && 'carte' in b) {
    const r = lienAutorise(etapes, a.carte, b.carte);
    if (r === 'ok') return { type: 'lier', de: a.carte, vers: b.carte };
    return {
      type: 'refus',
      raison:
        r === 'existe'
          ? 'Ce lien existe déjà.'
          : r === 'cycle'
            ? 'Lien impossible : il créerait une boucle (l’étape attend déjà l’autre).'
            : 'Une étape ne peut pas s’attendre elle-même.',
    };
  }
  if ('carte' in a && !('carte' in b)) {
    // Étape → cercle d'entrée (de l'extérieur) ou étape de la section → son cercle de sortie.
    const dedans = sectionDe(a.carte) === b.section;
    if ((b.sens === 'Entree' && !dedans) || (b.sens === 'Sortie' && dedans))
      return { type: 'lier', de: a.carte, vers: b.cercle };
    return {
      type: 'refus',
      raison: dedans
        ? 'Une étape de la section se relie à un cercle de sortie.'
        : 'Une étape hors de la section se relie à un cercle d’entrée.',
    };
  }
  if (!('carte' in a) && 'carte' in b) {
    // Cercle d'entrée → étape de la section ; cercle de sortie → étape qui l'attend, hors de la section.
    const dedans = sectionDe(b.carte) === a.section;
    if ((a.sens === 'Entree' && dedans) || (a.sens === 'Sortie' && !dedans))
      return { type: 'lier', de: a.cercle, vers: b.carte };
    return {
      type: 'refus',
      raison: dedans
        ? 'Une étape de la section attend un cercle d’entrée.'
        : 'Une étape hors de la section attend un cercle de sortie.',
    };
  }
  if (!('carte' in a) && !('carte' in b)) {
    if (a.sens === 'Sortie' && b.sens === 'Entree' && a.section !== b.section)
      return { type: 'lier', de: a.cercle, vers: b.cercle };
    return { type: 'refus', raison: 'Un cercle de sortie se relie au cercle d’entrée d’une autre section.' };
  }
  return null;
}

/** Cible survolée pendant le glisser d'une carte : une carte (après elle) ou un lien direct (insertion). */
export type CibleDepot = { type: 'carte'; id: string } | { type: 'lien'; lien: LienModele };

/**
 * Déplacement demandé au serveur pour un dépôt, ou null s'il n'aurait aucun effet. La section d'arrivée est déduite de
 * la place par le serveur (après une carte d'une section, sur un lien intérieur à une section…).
 */
export function deplacementPour(id: string, cible: CibleDepot): Deplacement | null {
  if (cible.type === 'carte') return cible.id === id ? null : { apres: cible.id };
  const l = cible.lien.logique;
  if (!l || l.de === id || l.vers === id) return null;
  return { surLien: l };
}
