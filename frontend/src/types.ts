// Types alignés sur docs/api.md

export type Phase = 'Preparation' | 'Execution' | 'RetourArriere' | 'Cloturee';
export type IssueMep = 'Reussie' | 'Repliee';
export type TypePlan = 'Deploiement' | 'RetourArriere';
export type ResultatControle = 'Conforme' | 'Echec';
export type StatutResolution = 'EnCours' | 'Reussie' | 'Echouee';
export type EtatEtape = 'AFaire' | 'EnCours' | 'Reussie' | 'Echec';
export type Criticite = 'Faible' | 'Moderee' | 'Elevee' | 'Bloquante';
export type StatutDisponibilite =
  | 'PlanInvalide'
  | 'EcheanceARenseigner'
  | 'RetourArriere'
  | 'FicheACompleter'
  | 'InstructionsACompleter'
  | 'Attend'
  | 'Terminee'
  | 'EnCours'
  | 'ArretResolution'
  | 'ResolutionEnCours'
  | 'SiRetourArriere'
  | 'Disponible';

export interface Utilisateur {
  id: string;
  login: string;
  nomAffiche: string;
  estAdmin: boolean;
}

export interface MepResume {
  id: string;
  outil: string;
  versionCible: string;
  objet: string;
  dateMep: string | null;
  phase: Phase;
  echeanceRepli: string | null;
  issue?: IssueMep | null;
  pilote: string | null;
  nbEtapes: number;
  nbReussies: number;
  nbEchecs: number;
}

export interface Sauvegarde {
  structure: boolean;
  donnees: boolean;
  configuration: boolean;
  responsableId: string | null;
  realiseeLe: string | null;
  procedure: string;
  copies: string;
  restaurationTestee: boolean;
  dateTestRestauration: string | null;
  preuveTestRestauration: string;
}

export interface Participant {
  id: string;
  nom: string;
  role: string;
  contact: string;
  utilisateurId: string | null;
  utilisateurNom: string | null;
}

export interface Systeme {
  id: string;
  nom: string;
  cible: string;
  impact: string;
  catalogueId: string | null;
}

export interface Livrable {
  id: string;
  objet: string;
  version: string;
  emplacement: string;
  systemeId: string | null;
}

export interface Disponibilite {
  statut: StatutDisponibilite;
  libelle: string;
}

export interface Etape {
  id: string;
  plan: TypePlan;
  code: string;
  ordre: number;
  action: string;
  systemeId: string | null;
  responsableId: string | null;
  criticite: Criticite | null;
  etat: EtatEtape;
  instructions: string;
  resultatAttendu: string;
  preuve: string;
  debutReel: string | null;
  finReelle: string | null;
  revision: number;
  executantId: string | null;
  dependances: string[];
  resolutionIds: string[];
  disponibilite: Disponibilite;
}

export interface Validation {
  par: string;
  le: string;
}

export interface Droits {
  estPilote: boolean;
  peutModifierPlan: boolean;
  participantId: string | null;
}

export interface Mep {
  id: string;
  version: number;
  outil: string;
  versionActuelle: string;
  versionCible: string;
  objet: string;
  impacts: string;
  dateMep: string | null;
  heureDebut: string | null;
  heureFin: string | null;
  fuseauHoraire: string;
  indisponibiliteMinutes: number | null;
  piloteId: string | null;
  suppleantId: string | null;
  phase: Phase;
  issue: IssueMep | null;
  retourArriere: RetourArriereInfo | null;
  cloture: ClotureInfo | null;
  planRepli: PlanRepli;
  echeanceRepli: string | null;
  sauvegarde: Sauvegarde;
  procedureRelue: Validation | null;
  demarrageAutorise: Validation | null;
  participants: Participant[];
  systemes: Systeme[];
  livrables: Livrable[];
  etapes: Etape[];
  resolutions: ExecutionResolution[];
  erreursPlan: string[];
  droits: Droits;
}

export interface RetourArriereInfo { le: string; par: string | null; motif: string }
export interface ClotureInfo { le: string; par: string }
export interface PlanRepli {
  quandDeclencher: string;
  dureeTesteeMinutes: number | null;
  versionARestaurer: string;
  donneesDepuisSauvegarde: string;
  siRepliEchoue: string;
}
export type PlanRepliSaisie = PlanRepli & { version: number };

export interface CorrectionExecutee {
  ordre: number;
  instruction: string;
  resultatAttendu: string;
  resultat: ResultatControle | null;
  observation: string;
  par: string | null;
  le: string | null;
}

export interface ExecutionResolution {
  id: string;
  etapeId: string;
  resolutionTypeId: string;
  titre: string;
  symptomes: string;
  diagnostic: string;
  corrections: CorrectionExecutee[];
  verificationInstruction: string;
  verificationAttendu: string;
  verificationResultat: ResultatControle | null;
  verificationObservation: string;
  statut: StatutResolution;
  demarreePar: string;
  demarreeLe: string;
  termineeLe: string | null;
}

export interface SystemeCatalogue { id: string; nom: string; description: string; cible: string; actif: boolean }
export type SystemeCatalogueSaisie = Omit<SystemeCatalogue, 'id'>;

export interface CorrectionType { ordre: number; instruction: string; resultatAttendu: string }
export interface ResolutionType {
  id: string;
  titre: string;
  symptomes: string;
  diagnostic: string;
  systemeCatalogueId: string | null;
  corrections: CorrectionType[];
  verificationInstruction: string;
  verificationAttendu: string;
  actif: boolean;
  version: number;
  modifieeLe: string;
}
export type ResolutionTypeSaisie = Omit<ResolutionType, 'id' | 'actif' | 'modifieeLe'>;

export interface Evenement {
  id: string;
  quand: string;
  auteur: string;
  type: string;
  detail: string;
}

export type MepIdentiteSaisie = Pick<
  Mep,
  | 'version' | 'outil' | 'versionActuelle' | 'versionCible' | 'objet' | 'impacts' | 'dateMep'
  | 'heureDebut' | 'heureFin' | 'fuseauHoraire' | 'indisponibiliteMinutes' | 'piloteId'
  | 'suppleantId' | 'echeanceRepli'
>;

export type SauvegardeSaisie = Sauvegarde & { version: number };

export interface ParticipantSaisie { nom: string; role: string; contact: string; utilisateurId: string | null }
export interface SystemeSaisie { nom: string; cible: string; impact: string }
export interface LivrableSaisie { objet: string; version: string; emplacement: string; systemeId: string | null }

export interface EtapeSaisie {
  revision: number;
  action: string;
  systemeId: string | null;
  responsableId: string | null;
  criticite: Criticite | null;
  instructions: string;
  resultatAttendu: string;
  dependances: string[];
  resolutionIds: string[];
}
