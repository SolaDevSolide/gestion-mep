import type { Mep } from '../types';

export type IdSection = 'intervention' | 'personnes' | 'techniques' | 'systemes' | 'livrables' | 'sauvegarde' | 'recette' | 'validation' | 'pieces';

const rempli = (v: string | null | undefined) => !!v && v.trim() !== '';

/**
 * Complétude de chaque section de la préparation, alignée sur les anomalies de « Vérifier le plan » (erreursPlan) :
 * un champ que la vérification ne réclame pas (heures, indisponibilité, restauration testée…) ne rend pas une section orange.
 */
export function completude(mep: Mep): Record<IdSection, boolean> {
  const aucune = (motif: RegExp) => !mep.erreursPlan.some((e) => motif.test(e));
  return {
    intervention: rempli(mep.outil) && aucune(/échéance de retour arrière|^Renseigner le pilote|^Le pilote .* n'est relié/),
    personnes: mep.participants.length > 0 && aucune(/n'est relié à aucun compte/),
    // Les avertissements ne bloquent rien, mais un point ouvert garde la section en orange pour inviter à le traiter.
    techniques: mep.responsablesTechniques.length > 0 && mep.avertissements.length === 0,
    systemes: mep.systemes.length > 0,
    livrables: mep.livrables.length > 0,
    sauvegarde: aucune(/^Sauvegarde pendant/),
    recette: aucune(/^Ajouter au moins un test de recette|^Test de recette/),
    validation: aucune(/relue|approbation|a refusé :|ne peut pas approuver/),
    pieces: true,
  };
}
