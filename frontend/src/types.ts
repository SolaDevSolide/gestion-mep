// Types alignés sur docs/api.md

export type Phase = 'Preparation' | 'Execution' | 'Recette' | 'RetourArriere' | 'Cloturee';
export type ModeSauvegarde = 'AvantMep' | 'PendantMep';
export type TypeEtape = 'Standard' | 'Sauvegarde';
export type DecisionApprobation = 'Approuvee' | 'Refusee';
export type DisponibiliteJourJ = 'Joignable' | 'Absent';
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

/** Expertise d'un compte sur un système du catalogue (« Expert de la zone facturation »). */
export interface Expertise {
  systemeCatalogueId: string;
  detail: string;
}

export interface Utilisateur {
  id: string;
  login: string;
  nomAffiche: string;
  estAdmin: boolean;
  role: string;
  contact: string;
  expertises: Expertise[];
}

export interface ProfilSaisie {
  nomAffiche?: string;
  role: string;
  contact: string;
  expertises: Expertise[];
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
  environnement: string;
  estModele: boolean;
  pilote: string | null;
  nbEtapes: number;
  nbReussies: number;
  nbEchecs: number;
}

export interface Sauvegarde {
  mode: ModeSauvegarde;
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

export type StatutSauvegarde = 'ARenseigner' | 'Requise' | 'NonRequise';

/** Sauvegarde d'un système de la MEP : requise (quoi, quand) ou non requise (justification). */
export interface SauvegardeSysteme {
  statut: StatutSauvegarde;
  moment: ModeSauvegarde;
  contenu: string;
  justification: string;
}
export type SauvegardeSystemeSaisie = SauvegardeSysteme & { version: number };

export interface Systeme {
  id: string;
  nom: string;
  cible: string;
  impact: string;
  catalogueId: string | null;
  sauvegarde: SauvegardeSysteme;
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
  /** Ligne du graphe (0 = en haut), par plan. */
  ligne: number;
  /** Position dans la ligne (0, 1, 2…). */
  ordre: number;
  action: string;
  /** Systèmes de la MEP touchés par l'étape (plusieurs possibles). */
  systemeIds: string[];
  responsableId: string | null;
  criticite: Criticite | null;
  dureeEstimeeMinutes: number | null;
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
  type: TypeEtape;
  pointDeNonRetour: boolean;
  nonRetourAutorise: { par: string | null; le: string } | null;
  disponibilite: Disponibilite;
}

export interface Validation {
  par: string;
  le: string;
}

export interface ResponsableTechnique {
  participantId: string;
  systemeIds: string[];
  perimetre: string;
  disponibilite: DisponibiliteJourJ;
  contactJourJ: string;
}
export type ResponsableTechniqueSaisie = ResponsableTechnique;

export interface Droits {
  estPilote: boolean;
  estApprobateur: boolean;
  estResponsableTechnique: boolean;
  /** Phase de préparation : le plan peut être modifié (par ceux qui en ont le droit). */
  peutModifierPlan: boolean;
  /** Préparation ET droit d'édition (responsable technique, pilote, suppléant, administrateur). */
  peutEditerContenu: boolean;
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
  suppleantIds: string[];
  environnement: string;
  estModele: boolean;
  approbationsMinimum: number;
  approbations: Approbation[];
  goNoGo: CritereCoche[];
  fichesRecette: FicheRecette[];
  bilanActive: boolean;
  bilan: Bilan | null;
  nonRetourFranchi: string | null;
  piecesJointes: PieceJointe[];
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
  /** Avertissements non bloquants de la vérification du plan (responsables techniques). */
  avertissements: string[];
  responsablesTechniques: ResponsableTechnique[];
  droits: Droits;
}

export interface Approbation {
  id: string;
  utilisateurId: string;
  nom: string;
  decision: DecisionApprobation | null;
  commentaire: string;
  le: string | null;
}
export interface CritereCoche { libelle: string; par: string | null; le: string }
/** Étape de test d'une fiche de recette (instructions et résultat attendu en markdown). */
export interface EtapeTest {
  id: string;
  ordre: number;
  instructions: string;
  resultatAttendu: string;
  resultat: ResultatControle | null;
  observation: string;
  par: string | null;
  le: string | null;
}
/** Fiche de recette d'un système de la MEP (systemeId null = « sans système »). */
export interface FicheRecette {
  id: string;
  ordre: number;
  systemeId: string | null;
  titre: string;
  etapes: EtapeTest[];
}
export interface EtapeTestSaisie { id?: string; instructions: string; resultatAttendu: string }
export interface FicheRecetteSaisie { systemeId: string | null; titre: string; etapes: EtapeTestSaisie[] }
export interface Bilan { ceQuiAMarche: string; incidents: string; actions: string; redigePar: string | null; le: string | null }
export interface PieceJointe {
  id: string;
  mepId: string | null;
  etapeId: string | null;
  resolutionTypeId: string | null;
  executionResolutionId: string | null;
  nomFichier: string;
  typeContenu: string;
  taille: number;
  sha256: string;
  deposeParId: string | null;
  deposePar: string | null;
  deposeLe: string;
}
export interface CritereGoNoGo { id: string; libelle: string; ordre: number; actif: boolean }
export interface CritereGoNoGoSaisie { libelle: string; ordre?: number; actif?: boolean }
export interface ParametresGlobaux { approbationsMinimum: number }
export interface BilanSaisie { ceQuiAMarche: string; incidents: string; actions: string }
export interface DuplicationSaisie { environnement?: string | null; commeModele: boolean }

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
  | 'suppleantIds' | 'echeanceRepli'
> & { environnement?: string; bilanActive?: boolean };

export type SauvegardeSaisie = Sauvegarde & { version: number };

export interface ParticipantSaisie { nom: string; role: string; contact: string; utilisateurId: string | null }
export interface SystemeSaisie { nom: string; cible: string; impact: string }
export interface LivrableSaisie { objet: string; version: string; emplacement: string; systemeId: string | null }

export interface EtapeSaisie {
  revision: number;
  action: string;
  systemeIds: string[];
  responsableId: string | null;
  criticite: Criticite | null;
  dureeEstimeeMinutes: number | null;
  instructions: string;
  resultatAttendu: string;
  dependances: string[];
  resolutionIds: string[];
  type: TypeEtape;
  pointDeNonRetour: boolean;
}
