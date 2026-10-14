using GestionMep.Domain;

namespace GestionMep.Api.Services;

// ---------- Sorties ----------
public record ExpertiseDto(Guid SystemeCatalogueId, string Detail);
/// <summary>
/// <c>ComptePrincipalId</c> : compte principal auquel ce compte est relié (même personne), null sinon ; <c>NomPersonne</c> : nom
/// affiché de la personne (celui du compte principal) ; <c>EquipeIds</c> : équipes dont ce compte est membre.
/// </summary>
public record UtilisateurDto(Guid Id, string Login, string NomAffiche, bool EstAdmin, string Role, string Contact, List<ExpertiseDto> Expertises, bool Actif,
    bool DoitChangerMotDePasse, Guid? ComptePrincipalId, string NomPersonne, List<Guid> EquipeIds);

/// <summary>Équipe : <c>MembreIds</c>, comptes membres (un compte peut appartenir à plusieurs équipes).</summary>
public record EquipeDto(Guid Id, string Nom, string Description, List<Guid> MembreIds, DateTimeOffset CreeeLe);

/// <summary>
/// <c>DateMep</c> : date du Go (dans le fuseau de la MEP) ou null ; <c>CreeLe</c> : création de la MEP ;
/// <c>RetourArriereActive</c> : false pour une MEP sans plan de retour arrière (son échéance éventuelle ne s'applique pas).
/// <c>EquipeIds</c> : équipes des personnes de la MEP (une MEP appartient à une équipe dès qu'une de ses personnes en fait
/// partie, par l'un de ses comptes reliés). <c>Personnes</c> : noms des personnes de la MEP et, pour celles qui ont un compte,
/// noms et logins de tous leurs comptes reliés (pour la recherche).
/// </summary>
public record MepResumeDto(Guid Id, string Outil, string VersionCible, string Objet, DateOnly? DateMep, Phase Phase,
    DateTimeOffset? EcheanceRepli, string? Pilote, int NbEtapes, int NbReussies, int NbEchecs, string Environnement, bool EstModele,
    DateTimeOffset CreeLe, IssueMep? Issue, bool RetourArriereActive, List<Guid> EquipeIds, List<string> Personnes);

public record SauvegardeDto(ModeSauvegarde Mode, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string Procedure, string Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string PreuveTestRestauration);

public record ValidationDto(string Par, DateTimeOffset Le);
public record ParticipantDto(Guid Id, string Nom, string Role, string Contact, Guid? UtilisateurId, string? UtilisateurNom);
/// <summary><c>Emplacement</c> et <c>MethodeRecuperation</c> : obligatoires si la sauvegarde est requise (vérification du plan).</summary>
public record SauvegardeSystemeDto(StatutSauvegarde Statut, ModeSauvegarde Moment, string Contenu, string Justification, string Emplacement,
    string MethodeRecuperation);
public record SystemeDto(Guid Id, string Nom, string Cible, string Impact, Guid? CatalogueId, SauvegardeSystemeDto Sauvegarde);
public record PlanRepliDto(string QuandDeclencher, int? DureeTesteeMinutes, string VersionARestaurer, string DonneesDepuisSauvegarde, string SiRepliEchoue);
/// <summary><c>Par</c> vaut null quand le retour arrière s'est déclenché automatiquement.</summary>
public record RetourArriereDto(DateTimeOffset Le, string? Par, string Motif);
public record ClotureDto(DateTimeOffset Le, string? Par);
public record LivrableDto(Guid Id, string Objet, string Version, string Emplacement, Guid? SystemeId);
public record DisponibiliteDto(StatutDisponibilite Statut, string Libelle);

/// <summary>
/// <c>Ligne</c> : ligne du graphe (0 en haut) ; <c>Ordre</c> : position dans la ligne (0 à gauche). <c>Fin</c> : test unitaire
/// (le résultat attendu est l'instruction de test, <c>ResultatConstate</c> se saisit en terminant l'étape) ou simple vérification.
/// <c>SauvegardeEmplacement</c> et <c>SauvegardeMethodeRecuperation</c> : étape de type Sauvegarde seulement.
/// </summary>
public record EtapeDto(Guid Id, TypePlan Plan, string Code, int Ligne, int Ordre, string Action, List<Guid> SystemeIds, Guid? ResponsableId, Guid? ExecutantId,
    Criticite? Criticite, int? DureeEstimeeMinutes,
    EtatEtape Etat, string Instructions, string ResultatAttendu, string Preuve, string Commentaire, DateTimeOffset? DebutReel, DateTimeOffset? FinReelle,
    int Revision, List<Guid> Dependances, List<Guid> ResolutionIds, DisponibiliteDto Disponibilite,
    TypeEtape Type, bool PointDeNonRetour, ValidationDto? NonRetourAutorise, FinEtape Fin, string ResultatConstate, string SauvegardeEmplacement,
    string SauvegardeMethodeRecuperation);

/// <summary>Test unitaire d'une étape : <c>Fait</c> si l'étape est terminée (réussie ou en échec) ; <c>Executant</c> : dernier exécutant.</summary>
public record TestUnitaireDto(Guid EtapeId, string Code, TypePlan Plan, string Action, EtatEtape Etat, bool Fait, string ResultatAttendu,
    string ResultatConstate, string? Executant, DateTimeOffset? FinReelle);

/// <summary>
/// Suivi des tests unitaires d'une MEP, dans l'ordre du plan : le déploiement, puis le retour arrière s'il a été déclenché.
/// <c>Verifications</c> : nombre d'étapes suivies qui se terminent par une simple vérification (absentes de <c>Tests</c>).
/// </summary>
public record TestsUnitairesDto(int Faits, int NonFaits, int Verifications, List<TestUnitaireDto> Tests);

public record ApprobationDto(Guid Id, Guid UtilisateurId, string Nom, DecisionApprobation? Decision, string Commentaire, DateTimeOffset? Le);
public record CritereValideDto(string Libelle, string? Par, DateTimeOffset Le);
/// <param name="ValideePar">Validation reportée d'un fichier Excel : nom saisi par le métier (« » sinon) ; <c>Par</c> est alors le compte qui a importé.</param>
/// <param name="ValideeLe">Validation reportée d'un fichier Excel : date indiquée dans le fichier.</param>
public record EtapeTestDto(Guid Id, int Ordre, string Instructions, string ResultatAttendu, ResultatControle? Resultat,
    string Observation, string? Par, DateTimeOffset? Le, string ValideePar, DateOnly? ValideeLe);
public record FicheRecetteDto(Guid Id, int Ordre, Guid? SystemeId, string Titre, List<EtapeTestDto> Etapes);
public record BilanDto(string CeQuiAMarche, string Incidents, string Actions, string? RedigePar, DateTimeOffset? Le);
public record PieceJointeDto(Guid Id, Guid? MepId, Guid? EtapeId, Guid? ResolutionTypeId, Guid? ExecutionResolutionId, string NomFichier,
    string TypeContenu, long Taille, string Sha256, Guid? DeposeParId, string? DeposePar, DateTimeOffset DeposeLe);
public record CritereGoNoGoDto(Guid Id, string Libelle, int Ordre, bool Actif);
public record ParametresDto(int ApprobationsMinimum);

public record CorrectionDto(int Ordre, string Instruction, string ResultatAttendu);
public record CorrectionExecuteeDto(int Ordre, string Instruction, string ResultatAttendu, ResultatControle? Resultat, string Observation,
    string? Par, DateTimeOffset? Le);
public record ExecutionResolutionDto(Guid Id, Guid EtapeId, Guid ResolutionTypeId, string Titre, string Symptomes, string Diagnostic,
    List<CorrectionExecuteeDto> Corrections, string VerificationInstruction, string VerificationAttendu,
    ResultatControle? VerificationResultat, string VerificationObservation, StatutResolution Statut,
    string DemarreePar, DateTimeOffset DemarreeLe, DateTimeOffset? TermineeLe);

public record SystemeCatalogueDto(Guid Id, string Nom, string Description, string Cible, bool Actif);
public record ResolutionTypeDto(Guid Id, string Titre, string Symptomes, string Diagnostic, List<Guid> SystemeCatalogueIds,
    List<CorrectionDto> Corrections, string VerificationInstruction, string VerificationAttendu, bool Actif, int Version, DateTimeOffset ModifieeLe,
    Guid? ModifieeParId, string? ModifieeParNom);

public record VersionResolutionTypeDto(int Version, string Action, DateTimeOffset ModifieeLe, Guid? ModifieeParId, string? ModifieeParNom,
    string Titre, string Symptomes, string Diagnostic, List<Guid> SystemeCatalogueIds, List<CorrectionDto> Corrections,
    string VerificationInstruction, string VerificationAttendu, bool Actif);

public record ResponsableTechniqueDto(Guid ParticipantId, List<Guid> SystemeIds, string Perimetre, DisponibiliteJourJ Disponibilite, string ContactJourJ);

public record DroitsDto(bool EstPilote, bool PeutModifierPlan, Guid? ParticipantId, bool EstApprobateur, bool EstResponsableTechnique, bool PeutEditerContenu);

/// <summary>
/// <c>RetourArriereActive</c> : plan de retour arrière prévu (false : ni plan ni échéance réclamés, aucun déclenchement).
/// <c>EcheanceRepli</c> est facultative et ne s'applique que si le retour arrière est activé.
/// </summary>
public record MepDto(Guid Id, int Version, string Outil, string VersionActuelle, string VersionCible, string Objet, string Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid> SuppleantIds, Phase Phase, DateTimeOffset? EcheanceRepli,
    IssueMep? Issue, RetourArriereDto? RetourArriere, ClotureDto? Cloture, PlanRepliDto PlanRepli, SauvegardeDto Sauvegarde,
    ValidationDto? ProcedureRelue, ValidationDto? DemarrageAutorise,
    List<ParticipantDto> Participants, List<SystemeDto> Systemes, List<LivrableDto> Livrables, List<EtapeDto> Etapes,
    List<ExecutionResolutionDto> Resolutions, List<string> ErreursPlan, DroitsDto Droits,
    string Environnement, bool EstModele, int ApprobationsMinimum, List<ApprobationDto> Approbations, List<CritereValideDto> GoNoGo,
    List<FicheRecetteDto> FichesRecette, bool BilanActive, BilanDto? Bilan, string? NonRetourFranchi, List<PieceJointeDto> PiecesJointes,
    List<ResponsableTechniqueDto> ResponsablesTechniques, List<string> Avertissements, bool RetourArriereActive);

/// <summary><c>AuteurLogin</c> : login actuel du compte auteur (stable, contrairement au nom affiché) ; null pour le système ou un compte supprimé.</summary>
public record EvenementDto(long Id, DateTimeOffset Quand, string Auteur, string Type, string Detail, string? AuteurLogin);

/// <summary>
/// Personne qui a la MEP ouverte (flux temps réel connecté) et éléments sur lesquels elle travaille, tous onglets confondus :
/// <c>etape:{id}</c>, <c>preparation:{section}</c>, <c>recette:{idFiche}</c>, <c>retour:fiche</c>…
/// </summary>
public record PresenceDto(Guid UtilisateurId, string Nom, List<string> Elements);

// ---------- Entrées ----------
public record ConnexionSaisie(string? Login, string? MotDePasse);
public record ChangementMotDePasseSaisie(string? Ancien, string? Nouveau);
public record CompteSaisie(string? Login, string? NomAffiche, bool EstAdmin, string? Role, string? Contact);
public record CompteModification(string? NomAffiche, bool EstAdmin, string? Role, string? Contact);
/// <summary>Équipe créée ou modifiée ; <c>MembreIds</c> absent (null) : membres inchangés.</summary>
public record EquipeSaisie(string? Nom, string? Description, List<Guid>? MembreIds);
/// <summary>Relie le compte à <c>ComptePrincipalId</c> (même personne) ; <c>Desactiver</c> : désactive aussi le compte relié.</summary>
public record LiaisonCompteSaisie(Guid? ComptePrincipalId, bool Desactiver = false);
public record CompteEtMotDePasseDto(UtilisateurDto Utilisateur, string MotDePasseProvisoire);
public record EvenementAdminDto(long Id, DateTimeOffset Quand, string Auteur, string Type, string Detail, string? AuteurLogin);
public record UtilisateurSaisie(string? Login, string? NomAffiche, string? MotDePasse, bool EstAdmin, string? Role = null, string? Contact = null);
public record ExpertiseSaisie(Guid? SystemeCatalogueId, string? Detail);
/// <summary><c>NomAffiche</c> absent ou null : inchangé.</summary>
public record ProfilSaisie(string? NomAffiche, string? Role, string? Contact, List<ExpertiseSaisie>? Expertises);
/// <summary>Création d'une MEP, ou d'un modèle si <c>Modele</c> vaut true.</summary>
public record MepCreationSaisie(string? Outil, string? Objet, bool? Modele = null);

/// <summary>
/// <c>Environnement</c>, <c>BilanActive</c> et <c>RetourArriereActive</c> absents : inchangés. <c>EcheanceRepli</c> null : sans échéance.
/// </summary>
public record MepIdentiteSaisie(int? Version, string? Outil, string? VersionActuelle, string? VersionCible, string? Objet, string? Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string? FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid>? SuppleantIds, DateTimeOffset? EcheanceRepli, string? Environnement = null, bool? BilanActive = null,
    bool? RetourArriereActive = null);

public record SauvegardeSaisie(int? Version, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string? Procedure, string? Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string? PreuveTestRestauration,
    ModeSauvegarde? Mode = null);

public record ParticipantSaisie(string? Nom, string? Role, string? Contact, Guid? UtilisateurId);
public record SystemeSaisie(string? Nom, string? Cible, string? Impact);
/// <summary><c>Emplacement</c> et <c>MethodeRecuperation</c> absents : inchangés.</summary>
public record SauvegardeSystemeSaisie(int? Version, StatutSauvegarde? Statut, ModeSauvegarde? Moment, string? Contenu, string? Justification,
    string? Emplacement = null, string? MethodeRecuperation = null);
public record LivrableSaisie(string? Objet, string? Version, string? Emplacement, Guid? SystemeId);

/// <summary>
/// <c>Ligne</c> et <c>Position</c> ne servent qu'à la création (<c>POST /etapes</c>). <c>Fin</c>, <c>SauvegardeEmplacement</c> et
/// <c>SauvegardeMethodeRecuperation</c> absents : inchangés (test unitaire pour une nouvelle étape).
/// </summary>
public record EtapeSaisie(int? Revision, string? Action, List<Guid>? SystemeIds, Guid? ResponsableId, Criticite? Criticite,
    string? Instructions, string? ResultatAttendu, List<Guid>? Dependances, TypePlan? Plan = null, List<Guid>? ResolutionIds = null,
    TypeEtape? Type = null, bool PointDeNonRetour = false, int? DureeEstimeeMinutes = null, int? Ligne = null, int? Position = null,
    FinEtape? Fin = null, string? SauvegardeEmplacement = null, string? SauvegardeMethodeRecuperation = null);

public record PlacerSaisie(int? Ligne, int? Position, bool LiensParDefaut = true);
public record RevisionSaisie(int? Revision);
public record PriseDeMainSaisie(int? Revision, string? Motif);
public record ResponsableTechniqueSaisie(Guid? ParticipantId, List<Guid>? SystemeIds, string? Perimetre, DisponibiliteJourJ? Disponibilite, string? ContactJourJ);
public record ResponsablesTechniquesSaisie(int? Version, List<ResponsableTechniqueSaisie>? Responsables);
/// <summary><c>ResultatConstate</c> : résultat du test unitaire, exigé pour le réussir (à défaut, la preuve en tient lieu).</summary>
public record TerminerSaisie(int? Revision, EtatEtape? Resultat, string? Preuve, bool ResultatVerifie, string? Commentaire = null,
    string? ResultatConstate = null);
public record CommentaireSaisie(int? Revision, string? Commentaire);
public record ProcedureRelueSaisie(bool Relue);
public record PhaseSaisie(Phase? Phase, List<Guid>? CriteresCoches = null);
public record NoGoSaisie(string? Motif);
public record ApprobateursSaisie(int? Version, List<Guid>? UtilisateurIds);
public record DecisionSaisie(int? Version, DecisionApprobation? Decision, string? Commentaire);
public record DuplicationSaisie(string? Environnement, bool CommeModele);
public record FicheRecetteCreationSaisie(Guid? SystemeId, string? Titre);
public record EtapeTestSaisie(Guid? Id, string? Instructions, string? ResultatAttendu);
/// <param name="SystemeId">Facultatif : la fiche garde son système ; sert seulement à rattacher une ancienne fiche sans système.</param>
public record FicheRecetteSaisie(Guid? SystemeId, string? Titre, List<EtapeTestSaisie>? Etapes);
/// <summary>Insertion d'un modèle de la bibliothèque (copie) dans un système de la MEP.</summary>
public record InsertionModeleRecetteSaisie(Guid? ModeleId, Guid? SystemeId);

public record EtapeModeleRecetteDto(int Ordre, string Instructions, string ResultatAttendu);
public record ModeleRecetteDto(Guid Id, string Titre, Guid SystemeCatalogueId, List<EtapeModeleRecetteDto> Etapes, int Version,
    DateTimeOffset ModifieLe, Guid? ModifieParId, string? ModifiePar);
public record EtapeModeleRecetteSaisie(string? Instructions, string? ResultatAttendu);
/// <param name="Version">Obligatoire pour une modification (verrou optimiste).</param>
public record ModeleRecetteSaisie(int? Version, string? Titre, Guid? SystemeCatalogueId, List<EtapeModeleRecetteSaisie>? Etapes);
/// <summary>« Enregistrer comme modèle » : copie d'une fiche de recette d'une MEP dans la bibliothèque.</summary>
public record ModeleDepuisFicheSaisie(Guid? MepId, Guid? FicheId, string? Titre, Guid? SystemeCatalogueId);

/// <summary>Étape de test que le fichier Excel importé va renseigner.</summary>
public record ChangementValidationDto(Guid FicheId, Guid EtapeId, string Systeme, string Fiche, int Etape, ResultatControle Resultat,
    string Observation, string ValidePar, DateOnly? Date);
/// <summary>Aperçu d'un import de validation : étapes à renseigner, étapes déjà à jour, lignes sans validation (ignorées).</summary>
public record ApercuValidationDto(List<ChangementValidationDto> Changements, int Inchangees, int SansValidation);
public record ClotureSaisie(string? CeQuiAMarche, string? Incidents, string? Actions);
public record CritereGoNoGoSaisie(string? Libelle, int? Ordre, bool? Actif);
public record ParametresSaisie(int? ApprobationsMinimum);

public record PlanRepliSaisie(int? Version, string? QuandDeclencher, int? DureeTesteeMinutes, string? VersionARestaurer,
    string? DonneesDepuisSauvegarde, string? SiRepliEchoue);
public record RetourArriereSaisie(string? Motif);
public record SystemeCatalogueSaisie(string? Nom, string? Description, string? Cible, bool? Actif);
public record DepuisCatalogueSaisie(Guid? CatalogueId, string? Impact);
public record CorrectionSaisie(int? Ordre, string? Instruction, string? ResultatAttendu);
public record ResolutionTypeSaisie(int? Version, string? Titre, string? Symptomes, string? Diagnostic, List<Guid>? SystemeCatalogueIds,
    List<CorrectionSaisie>? Corrections, string? VerificationInstruction, string? VerificationAttendu);
public record DemarrerResolutionSaisie(Guid? ResolutionTypeId, int? Revision);
public record ControleSaisie(ResultatControle? Resultat, string? Observation);

/// <summary>
/// Version en cours, lien « Signaler un problème ou proposer une idée » (null si désactivé par <c>Signalement:Lien</c>)
/// et, pour un administrateur seulement, l'état des mises à jour. <c>Installation</c> : « docker », « linux » ou « windows »
/// (installation sans Docker), pour que la console donne les commandes adaptées.
/// </summary>
public record ApplicationDto(string Version, string? LienSignalement, MiseAJourDto? MiseAJour, string Installation);

/// <summary>
/// Dernière version publiée sur GitHub. <c>Disponible</c> : elle est plus récente que la version en cours (toujours faux
/// pour une version de développement). <c>Erreur</c> : dernière vérification impossible (serveur sans accès à Internet…).
/// </summary>
public record MiseAJourDto(
    bool Active,
    string Depot,
    string VersionCourante,
    string? DerniereVersion,
    DateTimeOffset? PublieeLe,
    string? NotesUrl,
    DateTimeOffset? VerifieeLe,
    string? Erreur,
    bool Disponible,
    InstallateurDto Installateur);

/// <summary>
/// Service « mise-a-jour » (facultatif) : <c>Present</c> s'il tourne ; <c>Statut</c> pret, en-cours, terminee, echec ou
/// indisponible ; <c>Message</c> et <c>Version</c> de sa dernière action.
/// </summary>
public record InstallateurDto(bool Present, string? Statut, string? Version, string? Message, DateTimeOffset? Le);

/// <summary>Demande d'installation d'une version publiée (bouton « Mettre à jour » de la console).</summary>
public record InstallationSaisie(string? Version);

/// <summary>Options de la page de connexion (sans authentification) : bouton de connexion unique s'il est actif.</summary>
public record OptionsConnexionDto(bool SsoActif, string? SsoLibelle);

/// <summary>Connexion unique vue du profil : <c>Disponible</c> si elle est active, <c>Identifiant</c> du compte lié (null sinon).</summary>
public record SsoProfilDto(bool Disponible, string? Libelle, string? Identifiant, bool PeutDelier);

/// <summary>Adresse à ouvrir pour lier son compte (jeton valable 5 minutes).</summary>
public record SsoLiaisonDto(string Url);

/// <summary>
/// Réglages de la connexion unique pour la console. Le secret n'est jamais renvoyé : <c>SecretEnregistre</c> dit s'il existe,
/// <c>SecretLisible</c> s'il se déchiffre avec les clés actuelles. <c>UrlRetour</c> : adresse à déclarer chez le fournisseur.
/// </summary>
public record SsoReglagesDto(
    bool Active,
    FournisseurSso Fournisseur,
    string Annuaire,
    string Autorite,
    string ClientId,
    bool SecretEnregistre,
    bool SecretLisible,
    DateOnly? SecretExpireLe,
    bool CreationAutomatique,
    string RoleAdmin,
    string Libelle,
    string LibelleEffectif,
    string? UrlRetour,
    DateTimeOffset? ModifieeLe,
    List<string> Anomalies);

/// <summary>Saisie des réglages ; <c>Secret</c> null ou vide : secret inchangé.</summary>
public record SsoSaisie(
    FournisseurSso Fournisseur,
    string? Annuaire,
    string? Autorite,
    string? ClientId,
    string? Secret,
    DateOnly? SecretExpireLe,
    bool CreationAutomatique,
    string? RoleAdmin,
    string? Libelle);

public record SsoVerificationDto(string Libelle, bool Ok, string Detail);
public record SsoTestDto(bool Ok, List<SsoVerificationDto> Verifications);

/// <summary>Compte de l'application relié à un compte du fournisseur.</summary>
public record SsoIdentiteDto(Guid Id, Guid UtilisateurId, string Login, string NomAffiche, string Identifiant, DateTimeOffset CreeeLe, DateTimeOffset? DerniereConnexion);

/// <summary>
/// Mode démonstration (route <c>/api/demo</c>, sans authentification, déclarée seulement si la démonstration est active) :
/// comptes proposés par la connexion rapide.
/// </summary>
public record DemoDto(bool Actif, List<CompteDemoDto> Comptes);

/// <summary>Compte de démonstration : <c>Description</c> donne son rôle dans les MEP de démonstration.</summary>
public record CompteDemoDto(string Login, string NomAffiche, string Role, bool EstAdmin, string Description);

/// <summary>Connexion rapide à un compte de démonstration (mode démonstration uniquement).</summary>
public record ConnexionDemoSaisie(string? Login);

/// <summary>Résultat de la réinitialisation de la démonstration : MEP recréées et fichiers de pièces jointes supprimés.</summary>
public record ReinitialisationDemoDto(int Meps, int FichiersSupprimes);

/// <summary>
/// Éléments sur lesquels travaille un onglet (<c>Session</c> : identifiant passé au flux par <c>?session=</c>). Liste vide : plus aucun.
/// </summary>
public record PresenceSaisie(string? Session, List<string>? Elements);
