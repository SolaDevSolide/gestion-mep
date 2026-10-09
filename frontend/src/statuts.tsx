import {
  CheckCircle2, Check, CircleDot, ClipboardCheck, ClipboardList, Clock, Loader2, OctagonAlert, Play, Siren, TriangleAlert, Undo2, Wrench, XCircle,
  type LucideIcon,
} from 'lucide-react';
import { LIBELLE_DISPONIBILITE, tonDisponibilite, type Ton } from './format';
import type { Etape, IssueMep, Phase } from './types';

export interface MetaStatut { ton: Ton; libelle: string; Icone: LucideIcon; anime?: boolean }

/** État d'affichage d'une étape : couleur + icône + libellé (jamais la couleur seule). */
export function metaEtape(e: Pick<Etape, 'etat' | 'disponibilite'>): MetaStatut {
  const s = e.disponibilite.statut;
  if (e.etat === 'Reussie' || s === 'Terminee') return { ton: 'done', libelle: 'Terminée', Icone: Check };
  if (s === 'ResolutionEnCours') return { ton: 'warn', libelle: 'Résolution en cours', Icone: Wrench };
  if (e.etat === 'Echec') return { ton: 'danger', libelle: 'Échec', Icone: XCircle };
  if (s === 'RetourArriere') return { ton: 'rollback', libelle: 'RETOUR ARRIÈRE', Icone: Siren };
  if (e.etat === 'EnCours' || s === 'EnCours') return { ton: 'run', libelle: 'En cours', Icone: Loader2, anime: true };
  switch (s) {
    case 'Disponible': return { ton: 'ok', libelle: 'Disponible', Icone: CircleDot };
    case 'Attend': return { ton: 'wait', libelle: e.disponibilite.libelle || 'Attend', Icone: Clock };
    case 'SiRetourArriere': return { ton: 'later', libelle: 'Si retour arrière', Icone: Undo2 };
    case 'ArretResolution': return { ton: 'danger', libelle: 'Arrêt / résolution', Icone: OctagonAlert };
    default: return { ton: tonDisponibilite(s), libelle: e.disponibilite.libelle || LIBELLE_DISPONIBILITE[s], Icone: TriangleAlert };
  }
}

export function metaPhase(phase: Phase, issue: IssueMep | null): MetaStatut {
  switch (phase) {
    case 'Preparation': return { ton: 'wait', libelle: 'Préparation', Icone: ClipboardList };
    case 'Execution': return { ton: 'run', libelle: 'Exécution', Icone: Play };
    case 'Recette': return { ton: 'warn', libelle: 'Recette', Icone: ClipboardCheck };
    case 'RetourArriere': return { ton: 'rollback', libelle: 'Retour arrière', Icone: Siren };
    case 'Cloturee':
      return issue === 'Repliee'
        ? { ton: 'later', libelle: 'Clôturée : repliée', Icone: Undo2 }
        : { ton: 'done', libelle: 'Clôturée : réussie', Icone: CheckCircle2 };
  }
}
