// Types de l'API : façade sur les types générés depuis le contrat OpenAPI (docs/openapi.json → src/api/schema.d.ts,
// `npm run api:types`). Les noms exportés restent ceux du front ; un changement de DTO côté serveur se voit à la compilation.
import type { components } from './api/schema';

type S = components['schemas'];

/**
 * Forme qu'accepte le serveur pour une saisie : le désérialiseur .NET met à null un champ absent, donc un champ
 * nullable du contrat (déclaré obligatoire par le générateur) peut aussi être omis.
 */
type Souple<T> = T extends (infer U)[]
  ? Souple<U>[]
  : T extends object
    ? { [K in keyof T as null extends T[K] ? K : never]?: Souple<T[K]> } & {
        [K in keyof T as null extends T[K] ? never : K]: Souple<T[K]>;
      }
    : T;

/**
 * Saisie écrite côté front (plus stricte que le contrat : champs non nuls, champs facultatifs omis),
 * vérifiée à la compilation contre le schéma de l'API : chaque champ doit y exister avec un type compatible.
 */
type SaisieVerifiee<Schema, F extends Souple<Schema> & Record<Exclude<keyof F, keyof Schema>, never>> = F;

// ---------- Énumérations ----------

export type Phase = S['Phase'];
export type ModeSauvegarde = NonNullable<S['ModeSauvegarde']>;
export type TypeEtape = NonNullable<S['TypeEtape']>;
export type DecisionApprobation = NonNullable<S['DecisionApprobation']>;
export type DisponibiliteJourJ = NonNullable<S['DisponibiliteJourJ']>;
export type IssueMep = NonNullable<S['MepDto']['issue']>;
export type TypePlan = NonNullable<S['TypePlan']>;
export type ResultatControle = NonNullable<S['ResultatControle']>;
export type StatutResolution = S['ExecutionResolutionDto']['statut'];
export type EtatEtape = NonNullable<S['EtatEtape']>;
export type Criticite = NonNullable<S['Criticite']>;
export type StatutDisponibilite = S['DisponibiliteDto']['statut'];
export type StatutSauvegarde = NonNullable<S['StatutSauvegarde']>;

// ---------- Sorties (DTO) ----------

/** Expertise d'un compte sur un système du catalogue (« Expert de la zone facturation »). */
export type Expertise = S['ExpertiseDto'];
export type Utilisateur = S['UtilisateurDto'];
export type EvenementAdmin = S['EvenementAdminDto'];
/** Réponse à la création ou à la réinitialisation : le mot de passe provisoire n'est donné qu'une fois. */
export type CompteAvecMotDePasse = S['CompteEtMotDePasseDto'];
export type TypeEvenementAdmin =
  | 'compte.cree'
  | 'compte.modifie'
  | 'compte.desactive'
  | 'compte.reactive'
  | 'compte.mdp-reinitialise'
  | 'compte.mdp-change'
  | 'parametres.modifies'
  | 'critere.cree'
  | 'critere.modifie'
  | 'critere.supprime';

/** `dateMep` : date du Go si elle existe ; `creeLe` : date de création (affichée tant que la MEP n'a pas démarré). */
export type MepResume = S['MepResumeDto'];

export type Sauvegarde = S['SauvegardeDto'];
export type Participant = S['ParticipantDto'];
/** Sauvegarde d'un système de la MEP : requise (quoi, quand) ou non requise (justification). */
export type SauvegardeSysteme = S['SauvegardeSystemeDto'];
export type Systeme = S['SystemeDto'];
export type Livrable = S['LivrableDto'];
export type Disponibilite = S['DisponibiliteDto'];
export type Validation = S['ValidationDto'];

/**
 * Étape du plan : `ligne` (0 = en haut) et `ordre` (position dans la ligne) placent l'étape dans le graphe ;
 * `commentaire` : commentaire facultatif de l'exécutant (modifiable pendant l'exécution).
 */
export type Etape = S['EtapeDto'];

export type ResponsableTechnique = S['ResponsableTechniqueDto'];

/**
 * `peutModifierPlan` : phase de préparation, le plan peut être modifié (par ceux qui en ont le droit) ;
 * `peutEditerContenu` : préparation ET droit d'édition (responsable technique, pilote, suppléant, administrateur).
 */
export type Droits = S['DroitsDto'];

export type Approbation = S['ApprobationDto'];
export type CritereCoche = S['CritereValideDto'];
/** Étape de test d'une fiche de recette (instructions et résultat attendu en markdown). */
export type EtapeTest = S['EtapeTestDto'];
/** Fiche de recette d'un système de la MEP (systemeId null = « sans système »). */
export type FicheRecette = S['FicheRecetteDto'];
export type Bilan = S['BilanDto'];
export type PieceJointe = S['PieceJointeDto'];
export type CritereGoNoGo = S['CritereGoNoGoDto'];
export type ParametresGlobaux = SaisieVerifiee<S['ParametresSaisie'], S['ParametresDto']>;
export type RetourArriereInfo = S['RetourArriereDto'];
export type ClotureInfo = S['ClotureDto'];
export type PlanRepli = S['PlanRepliDto'];
export type CorrectionExecutee = S['CorrectionExecuteeDto'];
export type ExecutionResolution = S['ExecutionResolutionDto'];
export type SystemeCatalogue = S['SystemeCatalogueDto'];
export type CorrectionType = S['CorrectionDto'];
/** `systemeCatalogueIds` vide = tous les systèmes ; `modifieeParId` null : démo ou fiche antérieure au suivi. */
export type ResolutionType = S['ResolutionTypeDto'];
/** Version archivée d'une résolution type (historique, la plus récente d'abord). `action` : creee, modifiee, desactivee ou existante. */
export type VersionResolutionType = S['VersionResolutionTypeDto'];
export type Evenement = S['EvenementDto'];

/** `avertissements` : avertissements non bloquants de la vérification du plan (responsables techniques). */
export type Mep = S['MepDto'];

// ---------- Entrées (saisies) ----------

/** `nomAffiche` absent : inchangé. */
export type ProfilSaisie = SaisieVerifiee<
  S['ProfilSaisie'],
  {
    nomAffiche?: string;
    role: string;
    contact: string;
    expertises: Expertise[];
  }
>;

export type MepIdentiteSaisie = SaisieVerifiee<
  S['MepIdentiteSaisie'],
  Pick<
    Mep,
    | 'version'
    | 'outil'
    | 'versionActuelle'
    | 'versionCible'
    | 'objet'
    | 'impacts'
    | 'fuseauHoraire'
    | 'indisponibiliteMinutes'
    | 'piloteId'
    | 'suppleantIds'
    | 'echeanceRepli'
  > & { environnement?: string; bilanActive?: boolean }
>;

export type SauvegardeSaisie = SaisieVerifiee<S['SauvegardeSaisie'], Sauvegarde & { version: number }>;
export type SauvegardeSystemeSaisie = SaisieVerifiee<
  S['SauvegardeSystemeSaisie'],
  SauvegardeSysteme & { version: number }
>;
export type PlanRepliSaisie = SaisieVerifiee<S['PlanRepliSaisie'], PlanRepli & { version: number }>;
export type ResponsableTechniqueSaisie = SaisieVerifiee<S['ResponsableTechniqueSaisie'], ResponsableTechnique>;

export type ParticipantSaisie = SaisieVerifiee<
  S['ParticipantSaisie'],
  { nom: string; role: string; contact: string; utilisateurId: string | null }
>;
export type SystemeSaisie = SaisieVerifiee<S['SystemeSaisie'], { nom: string; cible: string; impact: string }>;
export type LivrableSaisie = SaisieVerifiee<
  S['LivrableSaisie'],
  { objet: string; version: string; emplacement: string; systemeId: string | null }
>;

export type EtapeSaisie = SaisieVerifiee<
  S['EtapeSaisie'],
  {
    revision: number;
    action: string;
    systemeIds: string[];
    /** Ignoré par le serveur depuis le jalon 4.2 (l'exécutant est celui qui démarre). */
    responsableId?: string | null;
    criticite: Criticite | null;
    dureeEstimeeMinutes: number | null;
    instructions: string;
    resultatAttendu: string;
    dependances: string[];
    resolutionIds: string[];
    type: TypeEtape;
    pointDeNonRetour: boolean;
  }
>;

/** `id` absent : nouvelle étape de test. */
export type EtapeTestSaisie = SaisieVerifiee<
  S['EtapeTestSaisie'],
  { id?: string; instructions: string; resultatAttendu: string }
>;
export type FicheRecetteSaisie = SaisieVerifiee<
  S['FicheRecetteSaisie'],
  { systemeId: string | null; titre: string; etapes: EtapeTestSaisie[] }
>;
/** Bilan saisi à la clôture (corps de `POST /meps/{id}/cloturer`). */
export type BilanSaisie = SaisieVerifiee<
  S['ClotureSaisie'],
  { ceQuiAMarche: string; incidents: string; actions: string }
>;
export type DuplicationSaisie = SaisieVerifiee<
  S['DuplicationSaisie'],
  { environnement?: string | null; commeModele: boolean }
>;

export type CritereGoNoGoSaisie = SaisieVerifiee<
  S['CritereGoNoGoSaisie'],
  { libelle: string; ordre?: number; actif?: boolean }
>;
export type SystemeCatalogueSaisie = SaisieVerifiee<S['SystemeCatalogueSaisie'], Omit<SystemeCatalogue, 'id'>>;
export type ResolutionTypeSaisie = SaisieVerifiee<
  S['ResolutionTypeSaisie'],
  Omit<ResolutionType, 'id' | 'actif' | 'modifieeLe' | 'modifieeParId' | 'modifieeParNom'>
>;

// ---------- Administration ----------

export type ChangementMotDePasseSaisie = SaisieVerifiee<
  S['ChangementMotDePasseSaisie'],
  { ancien: string; nouveau: string }
>;
export type CompteSaisie = SaisieVerifiee<
  S['CompteModification'],
  { nomAffiche: string; estAdmin: boolean; role: string; contact: string }
>;
export type NouveauCompteSaisie = SaisieVerifiee<S['CompteSaisie'], CompteSaisie & { login: string }>;
