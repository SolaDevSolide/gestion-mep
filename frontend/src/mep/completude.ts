import type { Mep } from '../types';

export type IdSection =
  'intervention' | 'personnes' | 'systemes' | 'techniques' | 'livrables' | 'sauvegarde' | 'validation' | 'pieces';

const rempli = (v: string | null | undefined) => !!v && v.trim() !== '';

/**
 * Complétude de chaque section de la préparation, alignée sur les anomalies de « Vérifier le plan » (erreursPlan) :
 * un champ que la vérification ne réclame pas (heures, indisponibilité, restauration testée…) ne rend pas une section orange.
 * La recette a son propre onglet : elle n'apparaît plus ici.
 */
export function completude(mep: Mep): Record<IdSection, boolean> {
  const aucune = (motif: RegExp) => !mep.erreursPlan.some((e) => motif.test(e));
  return {
    intervention:
      rempli(mep.outil) &&
      aucune(/échéance de retour arrière est passée|^Renseigner le pilote|^Le pilote .* n'est relié/),
    personnes: mep.participants.length > 0 && aucune(/n'est relié à aucun compte/),
    systemes: mep.systemes.length > 0,
    // Les avertissements ne bloquent rien, mais un point ouvert garde la section en orange pour inviter à le traiter.
    techniques:
      mep.responsablesTechniques.length > 0 &&
      mep.avertissements.every((a) => /^Aucune recette|^Aucun système n'est sauvegardé/.test(a)),
    livrables: mep.livrables.length > 0,
    // Chaque système doit dire s'il est sauvegardé (erreurs « Sauvegarde : indiquer… », « …non requise : justifier », « …pendant la MEP : … »).
    sauvegarde: mep.systemes.every((s) => s.sauvegarde.statut !== 'ARenseigner') && aucune(/^Sauvegarde/),
    validation: aucune(/relue|approbation|a refusé :|ne peut pas approuver/),
    pieces: true,
  };
}
