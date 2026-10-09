import type { Etape, Mep, Participant, Phase, TestRecette, TypePlan } from './types';
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

/** Badge « À vous » : seulement sur une étape à faire ou en cours, jamais sur une étape terminée ni dans une MEP clôturée. */
export const estAVous = (mep: Pick<Mep, 'droits' | 'phase'>, e: Pick<Etape, 'executantId' | 'responsableId' | 'etat'>): boolean => {
  const moi = mep.droits.participantId;
  return moi !== null && executantEffectif(e) === moi && e.etat !== 'Reussie' && mep.phase !== 'Cloturee';
};

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

/** Étape de déploiement qui réalise la sauvegarde (mode « pendant la MEP »). */
export const etapeSauvegarde = (mep: Pick<Mep, 'etapes'>): Etape | undefined =>
  mep.etapes.filter((e) => e.plan === 'Deploiement' && e.type === 'Sauvegarde').sort((a, b) => a.ordre - b.ordre)[0];

export const testsRecetteTries = (mep: Pick<Mep, 'testsRecette'>): TestRecette[] => [...mep.testsRecette].sort((a, b) => a.ordre - b.ordre);

/** Anomalies du plan qui ne relèvent pas du circuit d'approbation (elles bloquent la décision d'un approbateur). */
const ESTAPPROBATION = /^Il faut \d+ approbation|a refusé :|ne peut pas approuver/;
export const anomaliesHorsApprobation = (mep: Pick<Mep, 'erreursPlan'>): string[] => mep.erreursPlan.filter((e) => !ESTAPPROBATION.test(e));

/** Le déploiement est entièrement réussi (condition du passage en recette). */
export const deploiementReussi = (mep: Mep): boolean => {
  const e = etapesDuPlan(mep, 'Deploiement');
  return e.length > 0 && e.every((x) => x.etat === 'Reussie');
};
