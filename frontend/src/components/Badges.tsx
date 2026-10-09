import { LIBELLE_CRITICITE, LIBELLE_DISPONIBILITE, LIBELLE_ETAT, LIBELLE_PHASE, tonDisponibilite } from '../format';
import type { Criticite, Disponibilite, EtatEtape, Phase } from '../types';

export function BadgePhase({ phase }: { phase: Phase }) {
  return <span className={`badge badge-phase-${phase.toLowerCase()}`}>{LIBELLE_PHASE[phase]}</span>;
}

export function BadgeEtat({ etat }: { etat: EtatEtape }) {
  return <span className={`badge badge-etat-${etat.toLowerCase()}`}>{LIBELLE_ETAT[etat]}</span>;
}

export function BadgeCriticite({ criticite }: { criticite: Criticite }) {
  return <span className={`badge badge-crit-${criticite.toLowerCase()}`}>{LIBELLE_CRITICITE[criticite]}</span>;
}

export function BadgeDisponibilite({ dispo, grand = false }: { dispo: Disponibilite; grand?: boolean }) {
  const libelle = dispo.libelle || LIBELLE_DISPONIBILITE[dispo.statut];
  return (
    <span className={`dispo dispo-${tonDisponibilite(dispo.statut)}${grand ? ' dispo-grand' : ''}`} data-statut={dispo.statut}>
      {libelle}
    </span>
  );
}
