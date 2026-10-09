import type { Mep } from '../types';

export type IdSection = 'intervention' | 'personnes' | 'systemes' | 'livrables' | 'sauvegarde' | 'validation';

const rempli = (v: string | null | undefined) => !!v && v.trim() !== '';

/** Complétude (indicative) de chaque section de la préparation. */
export function completude(mep: Mep): Record<IdSection, boolean> {
  const s = mep.sauvegarde;
  return {
    intervention:
      rempli(mep.outil) && rempli(mep.versionCible) && rempli(mep.objet) && !!mep.dateMep && !!mep.heureDebut && !!mep.heureFin &&
      !!mep.piloteId && !!mep.echeanceRepli,
    personnes: mep.participants.length > 0 && !!mep.piloteId,
    systemes: mep.systemes.length > 0,
    livrables: mep.livrables.length > 0,
    sauvegarde: (s.structure || s.donnees || s.configuration) && !!s.responsableId && s.restaurationTestee,
    validation: mep.procedureRelue !== null,
  };
}
