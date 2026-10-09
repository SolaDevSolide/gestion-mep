import type { Mep } from '../types';
import { etapeSauvegarde } from '../domaine';

export type IdSection = 'intervention' | 'personnes' | 'systemes' | 'livrables' | 'sauvegarde' | 'recette' | 'validation' | 'pieces';

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
    sauvegarde:
      (s.structure || s.donnees || s.configuration) && s.restaurationTestee &&
      (s.mode === 'PendantMep' ? !!etapeSauvegarde(mep) : !!s.responsableId),
    recette: mep.testsRecette.length > 0 && mep.testsRecette.every((t) => rempli(t.libelle) && rempli(t.resultatAttendu) && !!t.responsableId),
    validation:
      mep.procedureRelue !== null &&
      mep.approbations.every((a) => a.decision !== 'Refusee') &&
      mep.approbations.filter((a) => a.decision === 'Approuvee').length >= mep.approbationsMinimum,
    pieces: true,
  };
}
