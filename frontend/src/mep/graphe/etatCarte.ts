import { Check, CircleDot, Clock, Loader2, Siren, Undo2, Wrench, XCircle, type LucideIcon } from 'lucide-react';
import type { Ton } from '../../format';
import type { Etape } from '../../types';

export interface EtatCarte {
  libelle: string;
  ton: Ton;
  Icone: LucideIcon;
  anime?: boolean;
  /** Fond teinté de la fiche (étape commencée). */
  teinte: boolean;
  /** Explication (infobulle), ex. raison de l'attente. */
  detail?: string;
}

/** État d'exécution affiché sur une fiche : Réussie, En cours, Échec, Disponible, En attente (et cas du retour arrière). */
export function etatCarte(e: Pick<Etape, 'etat' | 'disponibilite'>): EtatCarte {
  const s = e.disponibilite.statut;
  switch (e.etat) {
    case 'Reussie':
      return { libelle: 'Réussie', ton: 'ok', Icone: Check, teinte: true };
    case 'EnCours':
      return { libelle: 'En cours', ton: 'run', Icone: Loader2, anime: true, teinte: true };
    case 'Echec':
      return s === 'ResolutionEnCours'
        ? { libelle: 'Résolution en cours', ton: 'warn', Icone: Wrench, teinte: true }
        : { libelle: 'Échec', ton: 'danger', Icone: XCircle, teinte: true };
    default:
      if (s === 'Disponible') return { libelle: 'Disponible', ton: 'run', Icone: CircleDot, teinte: false };
      if (s === 'RetourArriere')
        return {
          libelle: 'Arrêtée',
          ton: 'rollback',
          Icone: Siren,
          teinte: false,
          detail: 'Retour arrière : ne plus exécuter.',
        };
      if (s === 'SiRetourArriere') return { libelle: 'Si retour arrière', ton: 'later', Icone: Undo2, teinte: false };
      return { libelle: 'En attente', ton: 'wait', Icone: Clock, teinte: false, detail: e.disponibilite.libelle };
  }
}

/** Bordure pleine par ton (classes statiques pour Tailwind). */
export const BORDURE_TON: Record<Ton, string> = {
  ok: 'border-ok-solid',
  run: 'border-run-solid',
  wait: 'border-line-strong',
  done: 'border-done-solid',
  danger: 'border-danger-solid',
  warn: 'border-warn-solid',
  later: 'border-later-bd border-dashed',
  rollback: 'border-rollback-solid',
};

/** Texte coloré par ton. */
export const TEXTE_TON: Record<Ton, string> = {
  ok: 'text-ok-solid border-ok-solid',
  run: 'text-run-solid border-run-solid',
  wait: 'text-wait-solid border-wait-solid',
  done: 'text-done-solid border-done-solid',
  danger: 'text-danger-solid border-danger-solid',
  warn: 'text-warn-solid border-warn-solid',
  later: 'text-later-fg border-later-bd',
  rollback: 'text-rollback-solid border-rollback-solid',
};
