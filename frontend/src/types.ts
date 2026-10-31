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
/** Fin d'une étape : test unitaire (résultat constaté saisi en la terminant) ou simple vérification. */
export type FinEtape = NonNullable<S['FinEtape']>;
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
export type NiveauRisque = NonNullable<S['NiveauRisque']>;
export type UsageModeleTexte = NonNullable<S['UsageModeleTexte']>;
/** MEP complète (plan détaillé) ou simplifiée (suppléants implicites, étapes sans durée, criticité ni résolutions liées). */
export type TypeMep = NonNullable<S['TypeMep']>;

// ---------- Sorties (DTO) ----------

/** Expertise d'un compte sur un système du catalogue (« Expert de la zone facturation »). */
export type Expertise = S['ExpertiseDto'];
export type Utilisateur = S['UtilisateurDto'];
/** Compte tel que l'annuaire le donne à tous (choix d'une personne) : sans les droits ni l'état du mot de passe. */
export type CompteAnnuaire = S['CompteAnnuaireDto'];
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
  | 'critere.supprime'
  | 'compte.renomme'
  | 'connexion.echecs'
  | 'sso.regle'
  | 'sso.active'
  | 'sso.desactive'
  | 'sso.delie'
  | 'sso.refuse'
  | 'sso.lie'
  | 'donnees.controle-reussi'
  | 'donnees.controle-echoue'
  | 'demo.reinitialisee'
  | 'equipe.creee'
  | 'equipe.modifiee'
  | 'equipe.supprimee'
  | 'compte.relie'
  | 'compte.delie'
  | 'mep.supprimee'
  | 'mep.pilotage-secours'
  | 'modele-texte.cree'
  | 'modele-texte.modifie'
  | 'modele-texte.retabli'
  | 'modele-texte.supprime';

/** `dateMep` : date du Go si elle existe ; `creeLe` : date de création (affichée tant que la MEP n'a pas démarré). */
export type MepResume = S['MepResumeDto'];
/**
 * Page de la liste (#96) : `total`, MEP qui correspondent aux filtres ; `ensemble`, toutes (ou tous les modèles) ;
 * `suivant`, à passer dans `apres` pour la page suivante (null : c'était la dernière).
 */
export type PageMeps = S['PageMepsDto'];
/** Comptes de la barre de filtres de la liste, calculés par le serveur avec les filtres en cours (#96). */
export type FacettesMeps = S['FacettesMepsDto'];
export type EtatMep = S['EtatMep'];
export type RoleMep = S['RoleMep'];
export type SystemeResume = S['SystemeResumeDto'];
export type Intervenant = S['IntervenantDto'];
export type EchecSysteme = S['EchecSystemeDto'];
export type MepHistorique = S['MepHistoriqueDto'];
export type HistoriqueSysteme = S['HistoriqueSystemeDto'];
export type ActiviteSysteme = S['ActiviteSystemeDto'];

export type Sauvegarde = S['SauvegardeDto'];
export type Participant = S['ParticipantDto'];
/** Sauvegarde d'un système de la MEP : requise (quoi, quand) ou non requise (justification). */
export type SauvegardeSysteme = S['SauvegardeSystemeDto'];
export type Systeme = S['SystemeDto'];
/** Environnement touché par la MEP sur un système (nom et marque « production » repris de la fiche du catalogue). */
export type EnvironnementSysteme = S['EnvironnementSystemeDto'];
/** Environnement sur lequel agit une étape, pour l'un de ses systèmes. */
export type EnvironnementEtape = S['EnvironnementEtapeDto'];
/**
 * Objet impacté d'un système de la MEP : copie d'un objet de la fiche du catalogue (`objetCatalogueId`) et sa version ou
 * correction. `repris` : ancien livrable repris tel quel (hors catalogue s'il n'a pas d'objet du catalogue).
 */
export type ObjetImpacte = S['ObjetImpacteDto'];
/** Résultat d'un import Excel des objets impactés : MEP à jour, bilan et lignes rejetées (inconnues du catalogue). */
export type ImportObjets = S['ImportObjetsDto'];
export type LigneRejetee = S['LigneRejeteeDto'];
export type Disponibilite = S['DisponibiliteDto'];
export type Validation = S['ValidationDto'];

/**
 * Étape du plan : `ligne` (0 = en haut) et `ordre` (position dans la ligne) placent l'étape dans le graphe ;
 * `commentaire` : commentaire facultatif de l'exécutant (modifiable pendant l'exécution) ; `fin` : test unitaire
 * (`resultatConstate` saisi en terminant l'étape) ou simple vérification.
 */
export type Etape = S['EtapeDto'];
/** Test unitaire d'une étape (`fait` : étape terminée, réussie ou en échec). */
export type TestUnitaire = S['TestUnitaireDto'];
/** Suivi des tests unitaires d'une MEP (`GET /meps/{id}/tests-unitaires`). */
export type TestsUnitaires = S['TestsUnitairesDto'];

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
/** Version en cours, lien de signalement et, pour un administrateur, l'état des mises à jour. */
export type InfosApplication = S['ApplicationDto'];
export type MiseAJour = NonNullable<S['MiseAJourDto']>;
export type ParametresGlobaux = SaisieVerifiee<S['ParametresSaisie'], S['ParametresDto']>;
/** Réglage d'exploitation (console ou « gestion-mep reglages ») : valeur en cours et son origine. */
export type Reglage = S['ReglageDto'];
/** Place occupée par les pièces jointes : fichiers déposés et stockage sur le serveur, après compression. */
export type EtatStockage = S['EtatStockageDto'];
/** Charte visuelle en vigueur vue de la console : charte (format du fichier charte.json), origine, fichier, lisibilité. */
export type EtatCharteServeur = S['EtatCharteDto'];
/** Vérification d'un fichier charte.json par le serveur (import à blanc). */
export type VerificationCharte = S['VerificationCharteDto'];
export type RetourArriereInfo = S['RetourArriereDto'];
export type ClotureInfo = S['ClotureDto'];
/** Sorties du cycle de vie (#93) : annulation, report d'une MEP en préparation, pilotage de secours après le Go. */
export type AnnulationInfo = S['AnnulationDto'];
export type ReportInfo = S['ReportDto'];
export type PilotageSecoursInfo = S['PilotageSecoursDto'];
export type PlanRepli = S['PlanRepliDto'];
export type CorrectionExecutee = S['CorrectionExecuteeDto'];
export type ExecutionResolution = S['ExecutionResolutionDto'];
export type SystemeCatalogue = S['SystemeCatalogueDto'];
/** Environnement de la fiche d'un système du catalogue (`cible` : serveur, URL…). */
export type EnvironnementCatalogue = S['EnvironnementCatalogueDto'];
/** Objet connu de la fiche d'un système du catalogue, rangé dans une catégorie. */
export type ObjetCatalogue = S['ObjetCatalogueDto'];
export type CorrectionType = S['CorrectionDto'];
/** `systemeCatalogueIds` vide = tous les systèmes ; `modifieeParId` null : démo ou fiche antérieure au suivi. */
export type ResolutionType = S['ResolutionTypeDto'];
/** `utilisations` : nombre de MEP où le modèle a été inséré ; `creeLe` : création (tri des bibliothèques). */
export type ModeleRecette = S['ModeleRecetteDto'];
/** Étape modèle de la bibliothèque d'étapes (`utilisations` : nombre de MEP où elle a été insérée). */
export type ModeleEtape = Omit<S['ModeleEtapeDto'], 'type' | 'fin'> & { type: TypeEtape; fin: FinEtape };
export type EtapeModeleRecette = S['EtapeModeleRecetteDto'];
export type ApercuValidation = S['ApercuValidationDto'];
export type ChangementValidation = S['ChangementValidationDto'];
/** Version archivée d'une résolution type (historique, la plus récente d'abord). `action` : creee, modifiee, desactivee ou existante. */
export type VersionResolutionType = S['VersionResolutionTypeDto'];
export type Evenement = S['EvenementDto'];
/** Personne qui a une MEP ouverte, et éléments sur lesquels elle travaille (`etape:{id}`, `preparation:{section}`…). */
export type Presence = S['PresenceDto'];

/** `avertissements` : avertissements non bloquants de la vérification du plan (responsables techniques). */
export type Mep = S['MepDto'];

/**
 * Section du plan (#60) : groupe d'étapes sous un titre, avec ses cercles d'entrée et de sortie (`ports`, entrées puis
 * sorties par ordre). `placee` : false tant qu'elle attend dans le bac.
 */
export type Section = S['SectionDto'];
/** Cercle d'une section : `etapes`, ses étapes reliées ; `sources` (entrée seulement), ce qui y arrive du dehors. */
export type CercleSection = S['PortSectionDto'];
export type SensCercle = S['PortSectionDto']['sens'];
/** Création d'une section : son identifiant et la MEP à jour. */
export type SectionCreee = S['SectionCreeeDto'];
/** Verrou d'édition d'une étape : tenu par l'onglet `session` jusqu'à `expireLe` sans renouvellement. */
export type Verrou = S['VerrouEtapeDto'];
/** `pris` : pris ou renouvelé par cet onglet ; sinon `verrou` dit qui est dessus. */
export type PriseVerrou = S['PriseVerrouDto'];

/** Section Communication de la MEP (tout est facultatif) ; heures « HH:MM:SS », dates « AAAA-MM-JJ ». */
export type Communication = S['CommunicationDto'];
/** Modèle de texte d'une équipe ; `fourni` : copie d'un modèle fourni (non supprimable, rétablissable). */
export type ModeleTexte = S['ModeleTexteDto'];
/** Variable des modèles de texte (`{{cle}}`), avec son groupe dans l'éditeur. */
export type VariableTexte = S['VariableTexteDto'];
/** Valeur d'une variable pour une MEP : `texte` ou `liste` ; les deux nuls : information manquante. */
export type ValeurVariable = S['ValeurVariableDto'];
/** Valeurs des variables d'une MEP pour une équipe (`equipeProposeeId` : équipe du pilote). */
export type VariablesMep = S['VariablesMepDto'];

/** Tableau de bord « Suivi en direct » (#61) : MEP lancées, prévues aujourd'hui et demain, clôturées depuis 24 heures. */
export type Suivi = S['SuiviDto'];
export type MepPrevue = S['MepPrevueDto'];
export type MepCloturee = S['MepClotureeDto'];
/** Rapport de fin (#61), figé à la clôture ; `calculeApres` : MEP clôturée avant les rapports, figé à sa première demande. */
export type RapportFin = S['RapportFinDto'];
export type SyntheseRapport = S['SyntheseRapportDto'];
export type PointBloquant = S['PointBloquantDto'];

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

/**
 * Saisies des blocs de formulaire d'une MEP (#96) : `revision`, celle du bloc lue dans `mep.revisions` (ou
 * `systeme.revisionSauvegarde`) ; une écriture ailleurs dans la MEP ne la périme pas.
 */
export type MepIdentiteSaisie = SaisieVerifiee<
  S['MepIdentiteSaisie'],
  { revision: number } & Pick<
    Mep,
    | 'outil'
    | 'versionActuelle'
    | 'versionCible'
    | 'objet'
    | 'impacts'
    | 'fuseauHoraire'
    | 'indisponibiliteMinutes'
    | 'piloteId'
    | 'suppleantIds'
  > & { bilanActive?: boolean; retourArriereActive?: boolean }
>;

export type CommunicationSaisie = SaisieVerifiee<S['CommunicationSaisie'], Communication & { revision: number }>;
/** `version` : obligatoire pour une modification ; `equipeId` : à la création seulement. */
export type ModeleTexteSaisie = SaisieVerifiee<
  S['ModeleTexteSaisie'],
  { version?: number; equipeId?: string; nom: string; usage: UsageModeleTexte; objet: string; corps: string }
>;
export type EquipeCommunicationSaisie = SaisieVerifiee<
  S['EquipeCommunicationSaisie'],
  { contactSupport: string; signature: string }
>;
export type TypeMepSaisie = SaisieVerifiee<S['TypeMepSaisie'], { version: number; type: TypeMep }>;

export type SauvegardeSaisie = SaisieVerifiee<S['SauvegardeSaisie'], Sauvegarde & { revision: number }>;
export type SauvegardeSystemeSaisie = SaisieVerifiee<
  S['SauvegardeSystemeSaisie'],
  SauvegardeSysteme & { revision: number }
>;
/** Échéance de retour arrière (#94) : `echeanceRepli` null la retire ; motif obligatoire sauf première saisie en préparation. */
export type EcheanceSaisie = SaisieVerifiee<
  S['EcheanceSaisie'],
  { revision: number; echeanceRepli: string | null; motif: string }
>;
export type ResolutionFigee = S['ResolutionFigeeDto'];
export type PlanRepliSaisie = SaisieVerifiee<S['PlanRepliSaisie'], PlanRepli & { revision: number }>;
export type ResponsableTechniqueSaisie = SaisieVerifiee<S['ResponsableTechniqueSaisie'], ResponsableTechnique>;

export type ParticipantSaisie = SaisieVerifiee<
  S['ParticipantSaisie'],
  { nom: string; role: string; contact: string; utilisateurId: string | null }
>;
export type SystemeSaisie = SaisieVerifiee<S['SystemeSaisie'], { nom: string; cible: string; impact: string }>;
/** Objets impactés d'un système : la liste remplace les objets du catalogue déjà retenus. */
export type ObjetsSystemeSaisie = SaisieVerifiee<
  S['ObjetsSystemeSaisie'],
  { objets: { objetCatalogueId: string; version: string }[] }
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
    /** Préalables directs ; absents : inchangés (la fiche ne les envoie pas : les liens se changent sur le plan). */
    dependances?: string[];
    resolutionIds: string[];
    type: TypeEtape;
    pointDeNonRetour: boolean;
    fin: FinEtape;
    /** Étape de type Sauvegarde seulement (effacés par le serveur pour une étape standard). */
    sauvegardeEmplacement: string;
    sauvegardeMethodeRecuperation: string;
    /** Environnements de l'étape, système par système ; absent : inchangés (sauf systèmes retirés de l'étape). */
    environnements?: EnvironnementEtape[];
  }
>;

/** `id` absent : nouvelle étape de test. */
export type EtapeTestSaisie = SaisieVerifiee<
  S['EtapeTestSaisie'],
  { id?: string; instructions: string; resultatAttendu: string }
>;
/** `systemeId` : seulement pour rattacher une ancienne fiche sans système (une fiche garde son système). */
export type FicheRecetteSaisie = SaisieVerifiee<
  S['FicheRecetteSaisie'],
  { systemeId?: string; titre: string; etapes: EtapeTestSaisie[] }
>;
export type ModeleRecetteSaisie = SaisieVerifiee<
  S['ModeleRecetteSaisie'],
  {
    version: number;
    titre: string;
    systemeCatalogueId: string;
    etapes: { instructions: string; resultatAttendu: string }[];
  }
>;
export type ModeleEtapeSaisie = SaisieVerifiee<
  S['ModeleEtapeSaisie'],
  {
    version: number;
    action: string;
    systemeCatalogueIds: string[];
    type: TypeEtape;
    criticite: Criticite | null;
    dureeEstimeeMinutes: number | null;
    instructions: string;
    resultatAttendu: string;
    fin: FinEtape;
    sauvegardeEmplacement: string;
    sauvegardeMethodeRecuperation: string;
    resolutionIds: string[];
  }
>;
/** Bilan saisi à la clôture (corps de `POST /meps/{id}/cloturer`). */
export type BilanSaisie = SaisieVerifiee<
  S['ClotureSaisie'],
  { ceQuiAMarche: string; incidents: string; actions: string }
>;
/** Clôture en échec (#93) : état du service laissé en production obligatoire, bilan obligatoire, incident facultatif. */
export type ClotureEchecSaisie = SaisieVerifiee<
  S['ClotureSaisie'],
  {
    ceQuiAMarche: string;
    incidents: string;
    actions: string;
    echec: true;
    etatService: string;
    referenceIncident: string;
  }
>;
export type NoGoSaisie = SaisieVerifiee<
  S['NoGoSaisie'],
  { motif: string; reporter?: boolean; dateVisee?: string | null }
>;
export type AnnulationSaisie = SaisieVerifiee<S['AnnulationSaisie'], { motif: string; etatProduction?: string }>;
export type ReportSaisie = SaisieVerifiee<S['ReportSaisie'], { motif: string; dateVisee?: string | null }>;
/** Nouveau pilote : une personne de la MEP (`participantId`) ou un compte (`utilisateurId`), jamais les deux. */
export type PilotageSaisie = SaisieVerifiee<
  S['PilotageSaisie'],
  {
    participantId?: string;
    utilisateurId?: string;
    motif: string;
    ancienPiloteSuppleant: boolean;
    suppleantsGardes?: string[];
  }
>;
export type SuppressionMepSaisie = SaisieVerifiee<S['SuppressionMepSaisie'], { motif: string; nom: string }>;
export type EtapeCorrectiveSaisie = SaisieVerifiee<
  S['EtapeCorrectiveSaisie'],
  {
    action: string;
    systemeIds: string[];
    instructions: string;
    resultatAttendu: string;
    dureeEstimeeMinutes: number | null;
    criticite: Criticite;
  }
>;
export type DuplicationSaisie = SaisieVerifiee<S['DuplicationSaisie'], { commeModele: boolean }>;

export type CritereGoNoGoSaisie = SaisieVerifiee<
  S['CritereGoNoGoSaisie'],
  { libelle: string; ordre?: number; actif?: boolean }
>;
export type SystemeCatalogueSaisie = SaisieVerifiee<
  S['SystemeCatalogueSaisie'],
  Pick<SystemeCatalogue, 'nom' | 'description' | 'cible' | 'actif'>
>;
/** Catégorie de la fiche : `ancien` renomme une catégorie existante (ses objets la suivent). */
export type CategorieSaisie = SaisieVerifiee<S['CategorieSaisie'], { nom: string; ancien?: string }>;
export type ObjetCatalogueSaisie = SaisieVerifiee<
  S['ObjetCatalogueSaisie'],
  { categorie: string; type: string; nom: string; libelle: string; precision: string }
>;
export type ResolutionTypeSaisie = SaisieVerifiee<
  S['ResolutionTypeSaisie'],
  Omit<ResolutionType, 'id' | 'actif' | 'modifieeLe' | 'modifieeParId' | 'modifieeParNom' | 'utilisations' | 'creeeLe'>
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
/** Équipe gérée par les administrateurs ; un compte peut appartenir à plusieurs équipes. */
export type Equipe = S['EquipeDto'];
export type EquipeSaisie = SaisieVerifiee<
  S['EquipeSaisie'],
  { nom: string; description: string; membreIds?: string[] }
>;
/** Relie un compte à un compte principal de la même personne ; `desactiver` désactive aussi le compte relié. */
export type LiaisonCompteSaisie = SaisieVerifiee<
  S['LiaisonCompteSaisie'],
  { comptePrincipalId: string; desactiver: boolean }
>;

// ---------- Connexion unique (SSO) ----------

export type FournisseurSso = S['FournisseurSso'];
export type OptionsConnexion = S['OptionsConnexionDto'];
/** Mode démonstration (route /api/demo, absente hors démonstration) : comptes de la connexion rapide. */
export type EtatDemo = S['DemoDto'];
export type CompteDemo = S['CompteDemoDto'];
export type ReinitialisationDemo = S['ReinitialisationDemoDto'];
export type EtatSimulation = S['EtatSimulationDto'];
export type LienRedondant = S['LienRedondantDto'];
export type SsoProfil = S['SsoProfilDto'];
export type SsoLiaison = S['SsoLiaisonDto'];
export type SsoReglages = S['SsoReglagesDto'];
export type SsoTest = S['SsoTestDto'];
export type SsoVerification = S['SsoVerificationDto'];
export type SsoIdentite = S['SsoIdentiteDto'];
/** Réglages envoyés par la console : `secret` vide ou omis = secret inchangé. */
export type SsoSaisie = SaisieVerifiee<
  S['SsoSaisie'],
  {
    fournisseur: FournisseurSso;
    annuaire: string;
    autorite: string;
    clientId: string;
    secret?: string;
    secretExpireLe?: string;
    creationAutomatique: boolean;
    roleAdmin: string;
    libelle: string;
  }
>;

// ---------- Connexions aux API externes (#111) ----------

export type ModeleConnexion = NonNullable<S['ModeleConnexion']>;
export type MethodeHttp = NonNullable<S['MethodeHttp']>;
export type FormatCorps = NonNullable<S['FormatCorps']>;
export type AuthentificationApi = NonNullable<S['AuthentificationApi']>;
export type EtatConnexion = NonNullable<S['EtatConnexion']>;
export type EtatAppel = NonNullable<S['EtatAppel']>;
export type OrigineAppel = NonNullable<S['OrigineAppel']>;
/** État d'un secret : jamais sa valeur, seulement s'il est enregistré, lisible, et quand et par qui il a été remplacé. */
export type SecretEtat = S['SecretEtatDto'];
export type ConnexionApiResume = S['ConnexionApiResumeDto'];
/** Connexion complète pour l'éditeur (sans aucun secret). */
export type ConnexionApi = S['ConnexionApiDto'];
export type EnteteConnexion = S['EnteteConnexionDto'];
/** Champ d'un message au format formulaire (nom et gabarit de la valeur). */
export interface ChampConnexion {
  nom: string;
  valeur: string;
}
/** Filtres d'une connexion : vides = toutes les MEP. */
export interface FiltresConnexion {
  equipeIds: string[];
  systemeCatalogueIds: string[];
  environnements: string[];
  niveauxRisque: NiveauRisque[];
  criticitesEtape: Criticite[];
  typesMep: TypeMep[];
}
export type CatalogueConnexions = S['CatalogueConnexionsDto'];
export type DeclencheurConnexion = S['DeclencheurDto'];
export type ModeleConnexionPropose = S['ModeleConnexionDto'];
export type EvenementPropose = S['EvenementProposeDto'];
export type ApercuConnexion = S['ApercuConnexionDto'];
export type ErreurChamp = S['ErreurChamp'];
export type EssaiConnexion = S['EssaiConnexionDto'];
export type EstimationConnexion = S['EstimationConnexionDto'];
export type AppelCree = S['AppelCreeDto'];
export type AppelResume = S['AppelResumeDto'];
export type Appel = S['AppelDto'];
export type TentativeAppel = S['TentativeDto'];
export type PageAppels = S['PageAppelsDto'];
export type EnteteAppel = S['EnteteAppelDto'];
/** Ce qu'une MEP montre des connexions : envois manuels proposés, échecs définitifs à montrer, droit d'envoyer. */
export type ConnexionsMep = S['ConnexionsMepDto'];
export type ConnexionManuelle = S['ConnexionManuelleDto'];
export type EchecEnvoi = S['EchecEnvoiDto'];
export type MessageRecu = S['MessageRecuDto'];
/** En-tête saisi : `valeurSecrete` vide ou omise = valeur enregistrée conservée. */
export type EnteteConnexionSaisie = SaisieVerifiee<
  S['EnteteConnexionSaisie'],
  { nom: string; valeur: string; secret: boolean; valeurSecrete?: string }
>;
/** Saisie d'une connexion : un secret vide ou omis est conservé (sauf si le domaine change). */
export type ConnexionApiSaisie = SaisieVerifiee<
  S['ConnexionApiSaisie'],
  {
    version?: number;
    nom: string;
    description: string;
    modele: ModeleConnexion;
    methode: MethodeHttp;
    url: string;
    urlSecrete: boolean;
    adresseSecrete?: string;
    authentification: AuthentificationApi;
    utilisateur: string;
    nomEntete: string;
    tokenUrl: string;
    clientId: string;
    scope: string;
    secret?: string;
    entetes: EnteteConnexionSaisie[];
    signature: boolean;
    secretSignature?: string;
    format: FormatCorps;
    corps: string;
    champs: ChampConnexion[];
    delaiSecondes: number;
    tentatives: number;
    declencheurs: string[];
    filtres: FiltresConnexion;
    envoiManuel: boolean;
    libelleEnvoiManuel: string;
    active?: boolean;
  }
>;
export type ApercuConnexionSaisie = SaisieVerifiee<
  S['ApercuConnexionSaisie'],
  {
    brouillon: ConnexionApiSaisie;
    connexionId?: string;
    mepId?: string;
    evenement?: string;
    evenementId?: number;
    commentaire?: string;
  }
>;

// ---------- Liaison avec Noosphere (#123) ----------

export type EtatLiaison = NonNullable<S['EtatLiaison']>;
/** Liaison avec une instance de Noosphere (empreinte, confirmation des deux côtés, interrupteurs). */
export type LiaisonNoosphere = S['LiaisonDto'];
export type AppairageLiaisonSaisie = S['AppairageLiaisonSaisie'];
export type OptionsLiaisonSaisie = S['OptionsLiaisonSaisie'];
export type SuiviLiaisonSaisie = S['SuiviLiaisonSaisie'];
export type SuiviApplicationSaisie = S['SuiviApplicationSaisie'];
/** Dernier catalogue de Noosphere lu : domaines et applications, suivies ou non, avec les systèmes proposés. */
export type CatalogueLiaison = S['CatalogueLiaisonDto'];
export type DomaineLiaison = S['DomaineLiaisonDto'];
export type ApplicationLiaison = S['ApplicationLiaisonDto'];
export type SuggestionSysteme = S['SuggestionSystemeDto'];
export type EchangeLiaison = S['EchangeLiaisonDto'];
export type SynchroLiaison = S['SynchroLiaisonDto'];
/** Système du catalogue suivi depuis Noosphere (« Géré par Noosphere »). */
export type SourceNoosphere = S['SourceNoosphereDto'];
