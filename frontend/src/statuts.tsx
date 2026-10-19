import {
  CheckCircle2,
  Check,
  CircleDot,
  ClipboardCheck,
  ClipboardList,
  Clock,
  Loader2,
  OctagonAlert,
  Play,
  Siren,
  TriangleAlert,
  Undo2,
  Wrench,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { LIBELLE_DISPONIBILITE, tonDisponibilite, type Ton } from './format';
import type { Etape, EtatMep, Phase } from './types';

export interface MetaStatut {
  ton: Ton;
  libelle: string;
  Icone: LucideIcon;
  anime?: boolean;
}

/** État d'affichage d'une étape : couleur + icône + libellé (jamais la couleur seule). */
export function metaEtape(e: Pick<Etape, 'etat' | 'disponibilite'>): MetaStatut {
  const s = e.disponibilite.statut;
  if (e.etat === 'Reussie' || s === 'Terminee') return { ton: 'done', libelle: 'Terminée', Icone: Check };
  if (s === 'ResolutionEnCours') return { ton: 'warn', libelle: 'Résolution en cours', Icone: Wrench };
  if (e.etat === 'Echec') return { ton: 'danger', libelle: 'Échec', Icone: XCircle };
  if (s === 'RetourArriere') return { ton: 'rollback', libelle: 'RETOUR ARRIÈRE', Icone: Siren };
  if (e.etat === 'EnCours' || s === 'EnCours') return { ton: 'run', libelle: 'En cours', Icone: Loader2, anime: true };
  switch (s) {
    case 'Disponible':
      return { ton: 'ok', libelle: 'Disponible', Icone: CircleDot };
    case 'Attend':
      return { ton: 'wait', libelle: e.disponibilite.libelle || 'Attend', Icone: Clock };
    case 'SiRetourArriere':
      return { ton: 'later', libelle: 'Si retour arrière', Icone: Undo2 };
    case 'ArretResolution':
      return { ton: 'danger', libelle: 'Arrêt / résolution', Icone: OctagonAlert };
    default:
      return {
        ton: tonDisponibilite(s),
        libelle: e.disponibilite.libelle || LIBELLE_DISPONIBILITE[s],
        Icone: TriangleAlert,
      };
  }
}

/**
 * Phase d'une MEP, puis son issue une fois clôturée (en-tête, cartes, suivi, rapport) : `etat` vient de l'API (même règle
 * que la recherche, #91). « Réussie avec incidents » : clôturée réussie alors qu'une étape a échoué en route.
 */
export function metaPhase(phase: Phase, etat: EtatMep): MetaStatut {
  switch (phase) {
    case 'Preparation':
      return { ton: 'wait', libelle: 'Préparation', Icone: ClipboardList };
    case 'Execution':
      return { ton: 'run', libelle: 'Exécution', Icone: Play };
    case 'Recette':
      return { ton: 'warn', libelle: 'Recette', Icone: ClipboardCheck };
    case 'RetourArriere':
      return { ton: 'rollback', libelle: 'Retour arrière', Icone: Siren };
    case 'Cloturee':
      if (etat === 'Repliee') return { ton: 'later', libelle: 'Clôturée : repliée', Icone: Undo2 };
      if (etat === 'AvecIncidents')
        return { ton: 'warn', libelle: 'Clôturée : réussie avec incidents', Icone: TriangleAlert };
      return { ton: 'done', libelle: 'Clôturée : réussie', Icone: CheckCircle2 };
  }
}

/**
 * État d'une MEP dans la recherche et l'historique (#59) : la phase, puis l'issue une fois clôturée. « Avec incidents » :
 * clôturée réussie alors qu'une étape a échoué en route. `court` : libellé des puces et des tableaux.
 */
export function metaEtatMep(e: EtatMep): MetaStatut & { court: string } {
  switch (e) {
    case 'Preparation':
      return { ton: 'wait', libelle: 'Préparation', court: 'Préparation', Icone: ClipboardList };
    case 'Execution':
      return { ton: 'run', libelle: 'Exécution', court: 'Exécution', Icone: Play };
    case 'Recette':
      return { ton: 'warn', libelle: 'Recette', court: 'Recette', Icone: ClipboardCheck };
    case 'RetourArriere':
      return { ton: 'rollback', libelle: 'Retour arrière en cours', court: 'Retour arrière', Icone: Siren };
    case 'Reussie':
      return { ton: 'done', libelle: 'Réussie', court: 'Réussie', Icone: CheckCircle2 };
    case 'AvecIncidents':
      return { ton: 'warn', libelle: 'Réussie avec incidents', court: 'Avec incidents', Icone: TriangleAlert };
    case 'Repliee':
      return { ton: 'later', libelle: 'Repliée (retour arrière)', court: 'Repliée', Icone: Undo2 };
  }
}
