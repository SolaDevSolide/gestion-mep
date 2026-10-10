import type { Etape, ExecutionResolution, Mep } from '../types';

/** Phases où l'exécutant, le pilote ou un administrateur peut commenter une étape. */
const PHASES_COMMENTAIRE = ['Execution', 'Recette', 'RetourArriere'];

/** Résolutions appliquées à une étape, dans l'ordre de démarrage. */
export interface ResolutionsEtape {
  /** Toutes les résolutions de l'étape, de la plus ancienne à la plus récente. */
  toutes: ExecutionResolution[];
  /** Résolution en cours de déroulé, s'il y en a une. */
  enCours: ExecutionResolution | undefined;
  /** Dernière résolution démarrée. */
  derniere: ExecutionResolution | undefined;
  /** Résolutions terminées (réussies ou échouées). */
  historique: ExecutionResolution[];
}

/** Regroupe les résolutions d'une étape (en cours, dernière, historique). */
export function resolutionsDeLEtape(mep: Mep, etape: Etape): ResolutionsEtape {
  const toutes = mep.resolutions.filter((r) => r.etapeId === etape.id).sort((a, b) => a.demarreeLe.localeCompare(b.demarreeLe));
  return {
    toutes,
    enCours: toutes.find((r) => r.statut === 'EnCours'),
    derniere: toutes[toutes.length - 1],
    historique: toutes.filter((r) => r.statut !== 'EnCours'),
  };
}

/** Droits et états dérivés d'une étape pour l'utilisateur courant. */
export interface DroitsEtape {
  /** L'utilisateur est l'exécutant de l'étape. */
  estExecutant: boolean;
  /** La phase de la MEP correspond au plan de l'étape (déploiement en exécution, retour arrière en retour arrière). */
  planAgissant: boolean;
  /** Point de non-retour à faire, sans décision du pilote. */
  nonRetourAttendu: boolean;
  peutDemarrer: boolean;
  peutTerminer: boolean;
  /** Appliquer une résolution ou en contrôler les corrections. */
  peutAgirResolution: boolean;
  peutPrendreLaMain: boolean;
  peutCommenter: boolean;
  enEchec: boolean;
  /** Dernière résolution réussie : l'étape est de nouveau à faire. */
  resolutionReussie: boolean;
  /** Dernière résolution échouée : l'étape reste en échec. */
  resolutionEchouee: boolean;
}

/**
 * Calcule les droits de l'utilisateur sur une étape en exécution.
 * Démarrer : tout participant relié à un compte, le pilote ou un administrateur.
 * Prise de main : participants (dont pilote et suppléants) et administrateurs, sur une étape démarrée par un autre.
 */
export function droitsEtape(mep: Mep, etape: Etape, admin: boolean): DroitsEtape {
  const moi = mep.droits.participantId;
  const pilote = mep.droits.estPilote;
  const statut = etape.disponibilite.statut;
  const estExecutant = moi !== null && etape.executantId === moi;
  const enEchec = etape.etat === 'Echec';
  const { enCours, derniere } = resolutionsDeLEtape(mep, etape);

  const planAgissant = (mep.phase === 'Execution' && etape.plan === 'Deploiement') || (mep.phase === 'RetourArriere' && etape.plan === 'RetourArriere');
  const nonRetourAttendu = etape.pointDeNonRetour && etape.etat === 'AFaire' && etape.nonRetourAutorise === null;

  return {
    estExecutant,
    planAgissant,
    nonRetourAttendu,
    peutDemarrer: (moi !== null || pilote || admin) && statut === 'Disponible' && etape.etat === 'AFaire' && !nonRetourAttendu,
    peutTerminer: etape.etat === 'EnCours' && (estExecutant || pilote),
    peutAgirResolution: estExecutant || pilote,
    peutPrendreLaMain: planAgissant && (moi !== null || admin) && !estExecutant && etape.executantId !== null && (etape.etat === 'EnCours' || etape.etat === 'Echec'),
    peutCommenter: (estExecutant || pilote || admin) && PHASES_COMMENTAIRE.includes(mep.phase),
    enEchec,
    resolutionReussie: !enCours && derniere?.statut === 'Reussie' && etape.etat === 'AFaire',
    resolutionEchouee: !enCours && derniere?.statut === 'Echouee' && enEchec,
  };
}
