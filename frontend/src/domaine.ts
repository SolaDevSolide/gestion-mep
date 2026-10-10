import type { Etape, EtapeTest, FicheRecette, Mep, Participant, Phase, ResponsableTechnique, TypePlan } from './types';
import type { Segment } from './ui/ProgressBar';

/** Ordre du graphe : par ligne, puis par position dans la ligne. */
export const parPosition = (a: Pick<Etape, 'ligne' | 'ordre'>, b: Pick<Etape, 'ligne' | 'ordre'>): number =>
  a.ligne - b.ligne || a.ordre - b.ordre;

export const etapesDuPlan = (mep: Mep, plan: TypePlan): Etape[] =>
  mep.etapes.filter((e) => e.plan === plan).sort(parPosition);

/** Plan sur lequel on agit selon la phase (null si aucune exécution possible). */
export function planActif(phase: Phase): TypePlan | null {
  if (phase === 'Execution') return 'Deploiement';
  if (phase === 'RetourArriere') return 'RetourArriere';
  return null;
}

/** Exécutant d’une étape : la personne qui l’a démarrée (ou a pris la main) ; aucun avant le démarrage. */
export const executantEffectif = (e: Pick<Etape, 'executantId'>): string | null => e.executantId;

/** Badge « À vous » : seulement sur une étape à faire ou en cours, jamais sur une étape terminée ni dans une MEP clôturée. */
export const estAVous = (mep: Pick<Mep, 'droits' | 'phase'>, e: Pick<Etape, 'executantId' | 'etat'>): boolean => {
  const moi = mep.droits.participantId;
  return moi !== null && executantEffectif(e) === moi && e.etat !== 'Reussie' && mep.phase !== 'Cloturee';
};

export const participantPar = (
  mep: Pick<Mep, 'participants'>,
  id: string | null | undefined,
): Participant | undefined => (id ? mep.participants.find((p) => p.id === id) : undefined);

export const nomParticipant = (mep: Pick<Mep, 'participants'>, id: string | null | undefined): string | null =>
  participantPar(mep, id)?.nom ?? null;

/** Étapes préalables non encore réussies : ce qui bloque l'étape. */
export function bloqueurs(e: Etape, etapes: Etape[]): Etape[] {
  return e.dependances
    .map((id) => etapes.find((x) => x.id === id))
    .filter((x): x is Etape => !!x && x.etat !== 'Reussie');
}

/** Étape à mettre en avant : celle que j’exécute, sinon la première « Disponible », sinon la première non terminée. */
export function etapeParDefaut(etapes: Etape[], participantId: string | null): Etape | undefined {
  const triees = [...etapes].sort(parPosition);
  const miennes = (e: Etape) => participantId !== null && executantEffectif(e) === participantId;
  return (
    triees.find((e) => e.etat === 'EnCours' && miennes(e)) ??
    triees.find((e) => e.disponibilite.statut === 'Disponible') ??
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
  mep.etapes.filter((e) => e.plan === 'Deploiement' && e.type === 'Sauvegarde').sort(parPosition)[0];

export const fichesRecetteTriees = (mep: Pick<Mep, 'fichesRecette'>): FicheRecette[] =>
  [...mep.fichesRecette]
    .sort((a, b) => a.ordre - b.ordre)
    .map((f) => ({ ...f, etapes: [...f.etapes].sort((a, b) => a.ordre - b.ordre) }));

/** Étapes de test de toutes les fiches. */
export const etapesTestRecette = (mep: Pick<Mep, 'fichesRecette'>): EtapeTest[] =>
  mep.fichesRecette.flatMap((f) => f.etapes);

export interface BilanRecette {
  total: number;
  conformes: number;
  echecs: number;
  restantes: number;
}
export function bilanRecette(mep: Pick<Mep, 'fichesRecette'>): BilanRecette {
  const e = etapesTestRecette(mep);
  const conformes = e.filter((t) => t.resultat === 'Conforme').length;
  const echecs = e.filter((t) => t.resultat === 'Echec').length;
  return { total: e.length, conformes, echecs, restantes: e.length - conformes - echecs };
}

/** Responsables techniques d'un système : participants désignés pour ce système (contact du jour J en recette). */
export function responsablesDuSysteme(
  mep: Pick<Mep, 'responsablesTechniques' | 'participants'>,
  systemeId: string | null,
): { participant: Participant; contactJourJ: string; disponibilite: ResponsableTechnique['disponibilite'] }[] {
  if (!systemeId) return [];
  return mep.responsablesTechniques
    .filter((r) => r.systemeIds.includes(systemeId))
    .flatMap((r) => {
      const participant = mep.participants.find((p) => p.id === r.participantId);
      return participant
        ? [{ participant, contactJourJ: r.contactJourJ || participant.contact, disponibilite: r.disponibilite }]
        : [];
    });
}

/** Anomalies du plan qui ne relèvent pas du circuit d'approbation (elles bloquent la décision d'un approbateur). */
const ESTAPPROBATION = /^Il faut \d+ approbation|a refusé :|ne peut pas approuver/;
export const anomaliesHorsApprobation = (mep: Pick<Mep, 'erreursPlan'>): string[] =>
  mep.erreursPlan.filter((e) => !ESTAPPROBATION.test(e));

/** Le déploiement est entièrement réussi (condition du passage en recette). */
export const deploiementReussi = (mep: Mep): boolean => {
  const e = etapesDuPlan(mep, 'Deploiement');
  return e.length > 0 && e.every((x) => x.etat === 'Reussie');
};
