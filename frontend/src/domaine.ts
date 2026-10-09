import type { Etape, Mep, Participant, Phase, TypePlan } from './types';
import type { Segment } from './ui/ProgressBar';

export const etapesDuPlan = (mep: Mep, plan: TypePlan): Etape[] =>
  mep.etapes.filter((e) => e.plan === plan).sort((a, b) => a.ordre - b.ordre);

/** Plan sur lequel on agit selon la phase (null si aucune exécution possible). */
export function planActif(phase: Phase): TypePlan | null {
  if (phase === 'Execution') return 'Deploiement';
  if (phase === 'RetourArriere') return 'RetourArriere';
  return null;
}

export const executantEffectif = (e: Pick<Etape, 'executantId' | 'responsableId'>): string | null => e.executantId ?? e.responsableId;

export const participantPar = (mep: Pick<Mep, 'participants'>, id: string | null | undefined): Participant | undefined =>
  id ? mep.participants.find((p) => p.id === id) : undefined;

export const nomParticipant = (mep: Pick<Mep, 'participants'>, id: string | null | undefined): string | null =>
  participantPar(mep, id)?.nom ?? null;

/** Étapes préalables non encore réussies : ce qui bloque l'étape. */
export function bloqueurs(e: Etape, etapes: Etape[]): Etape[] {
  return e.dependances
    .map((id) => etapes.find((x) => x.id === id))
    .filter((x): x is Etape => !!x && x.etat !== 'Reussie');
}

/** Étape à mettre en avant : la première « Disponible » dont je suis l'exécutant, sinon la première non terminée. */
export function etapeParDefaut(etapes: Etape[], participantId: string | null): Etape | undefined {
  const triees = [...etapes].sort((a, b) => a.ordre - b.ordre);
  const miennes = (e: Etape) => participantId !== null && executantEffectif(e) === participantId;
  return (
    triees.find((e) => e.etat === 'EnCours' && miennes(e)) ??
    triees.find((e) => e.disponibilite.statut === 'Disponible' && miennes(e)) ??
    triees.find((e) => e.etat !== 'Reussie') ??
    triees[0]
  );
}

export function segmentsProgression(etapes: Etape[]): Segment[] {
  const reussies = etapes.filter((e) => e.etat === 'Reussie').length;
  const enCours = etapes.filter((e) => e.etat === 'EnCours').length;
  const echecs = etapes.filter((e) => e.etat === 'Echec').length;
  return [
    { valeur: reussies, ton: 'ok', libelle: 'Réussies' },
    { valeur: enCours, ton: 'run', libelle: 'En cours' },
    { valeur: echecs, ton: 'danger', libelle: 'Échecs' },
    { valeur: etapes.length - reussies - enCours - echecs, ton: 'wait', libelle: 'Restantes' },
  ];
}

export function segmentsResume(nbEtapes: number, nbReussies: number, nbEchecs: number): Segment[] {
  return [
    { valeur: nbReussies, ton: 'ok', libelle: 'Réussies' },
    { valeur: nbEchecs, ton: 'danger', libelle: 'Échecs' },
    { valeur: Math.max(0, nbEtapes - nbReussies - nbEchecs), ton: 'wait', libelle: 'Restantes' },
  ];
}
