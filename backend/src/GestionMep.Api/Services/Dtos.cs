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

/// <summary>
/// Équipe : <c>MembreIds</c>, comptes membres (un compte peut appartenir à plusieurs équipes) ; <c>ContactSupport</c> et
/// <c>Signature</c> : informations communes aux modèles de texte de l'équipe (signature vide : « L'équipe {nom} »).
/// </summary>
public record EquipeDto(Guid Id, string Nom, string Description, List<Guid> MembreIds, DateTimeOffset CreeeLe, string ContactSupport, string Signature);

/// <summary>
/// <c>DateMep</c> : date du Go (dans le fuseau de la MEP) ou null ; <c>CreeLe</c> : création de la MEP ;
/// <c>RetourArriereActive</c> : false pour une MEP sans plan de retour arrière (son échéance éventuelle ne s'applique pas).
/// <c>ToucheProduction</c> : l'un des systèmes de la MEP touche un environnement de production.
/// <c>EquipeIds</c> : équipes des personnes de la MEP (une MEP appartient à une équipe dès qu'une de ses personnes en fait
/// partie, par l'un de ses comptes reliés). <c>Personnes</c> : noms des personnes de la MEP et, pour celles qui ont un compte,
/// noms et logins de tous leurs comptes reliés (pour la recherche). <c>Type</c> : MEP complète ou simplifiée.
/// </summary>
public record MepResumeDto(Guid Id, string Outil, string VersionCible, string Objet, DateOnly? DateMep, Phase Phase,
    DateTimeOffset? EcheanceRepli, string? Pilote, int NbEtapes, int NbReussies, int NbEchecs, bool ToucheProduction, bool EstModele,
    DateTimeOffset CreeLe, IssueMep? Issue, bool RetourArriereActive, List<Guid> EquipeIds, List<string> Personnes, TypeMep Type);

public record SauvegardeDto(ModeSauvegarde Mode, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string Procedure, string Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string PreuveTestRestauration);

public record ValidationDto(string Par, DateTimeOffset Le);
public record ParticipantDto(Guid Id, string Nom, string Role, string Contact, Guid? UtilisateurId, string? UtilisateurNom);
/// <summary><c>Emplacement</c> et <c>MethodeRecuperation</c> : obligatoires si la sauvegarde est requise (vérification du plan).</summary>
public record SauvegardeSystemeDto(StatutSauvegarde Statut, ModeSauvegarde Moment, string Contenu, string Justification, string Emplacement,
    string MethodeRecuperation);
/// <summary>Environnement d'un système de la MEP (<c>Production</c> : copié de la fiche du catalogue, ou déduit du nom hors catalogue).</summary>
public record EnvironnementSystemeDto(string Nom, bool Production);
/// <summary><c>Environnements</c> : environnements touchés par la MEP sur ce système.</summary>
public record SystemeDto(Guid Id, string Nom, string Cible, string Impact, Guid? CatalogueId, SauvegardeSystemeDto Sauvegarde,
    List<EnvironnementSystemeDto> Environnements);
public record PlanRepliDto(string QuandDeclencher, int? DureeTesteeMinutes, string VersionARestaurer, string DonneesDepuisSauvegarde, string SiRepliEchoue);
/// <summary><c>Par</c> vaut null quand le retour arrière s'est déclenché automatiquement.</summary>
public record RetourArriereDto(DateTimeOffset Le, string? Par, string Motif);
public record ClotureDto(DateTimeOffset Le, string? Par);
/// <summary>
/// Objet impacté : copie de l'objet du catalogue (<c>ObjetCatalogueId</c>) au moment du choix, et version ou correction livrée.
/// <c>Repris</c> : ancien livrable repris par la migration ; <c>ObjetCatalogueId</c> null : livrable d'un système hors catalogue.
/// </summary>
public record ObjetImpacteDto(Guid Id, Guid? SystemeId, Guid? ObjetCatalogueId, string Categorie, string Type, string Nom, string Libelle,
    string Precision, string Version, bool Repris);
/// <summary>Environnement d'une étape sur l'un de ses systèmes.</summary>
public record EnvironnementEtapeDto(Guid SystemeId, string Nom);
public record DisponibiliteDto(StatutDisponibilite Statut, string Libelle);

/// <summary>
/// <c>Ligne</c> : ligne du graphe (0 en haut) ; <c>Ordre</c> : position dans la ligne (0 à gauche). <c>Fin</c> : test unitaire
/// (le résultat attendu est l'instruction de test, <c>ResultatConstate</c> se saisit en terminant l'étape) ou simple vérification.
/// <c>SauvegardeEmplacement</c> et <c>SauvegardeMethodeRecuperation</c> : étape de type Sauvegarde seulement.
/// <c>ModeleEtapeId</c> : étape modèle dont l'étape est une copie (null : saisie dans la MEP).
/// <c>Environnements</c> : environnements où agit l'étape, système par système.
/// </summary>
public record EtapeDto(Guid Id, TypePlan Plan, string Code, int Ligne, int Ordre, string Action, List<Guid> SystemeIds, Guid? ResponsableId, Guid? ExecutantId,
    Criticite? Criticite, int? DureeEstimeeMinutes,
    EtatEtape Etat, string Instructions, string ResultatAttendu, string Preuve, string Commentaire, DateTimeOffset? DebutReel, DateTimeOffset? FinReelle,
    int Revision, List<Guid> Dependances, List<Guid> ResolutionIds, DisponibiliteDto Disponibilite,
    TypeEtape Type, bool PointDeNonRetour, ValidationDto? NonRetourAutorise, FinEtape Fin, string ResultatConstate, string SauvegardeEmplacement,
    string SauvegardeMethodeRecuperation, Guid? ModeleEtapeId, List<EnvironnementEtapeDto> Environnements);

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

public record EnvironnementCatalogueDto(string Nom, string Cible, bool Production);
/// <summary>
/// Système du catalogue : <c>Environnements</c> et <c>Categories</c> (dans l'ordre de la fiche), <c>NbObjets</c> connus
/// (le détail : <c>GET /catalogue/systemes/{id}/objets</c>).
/// </summary>
public record SystemeCatalogueDto(Guid Id, string Nom, string Description, string Cible, bool Actif, List<EnvironnementCatalogueDto> Environnements,
    List<string> Categories, int NbObjets);
public record ObjetCatalogueDto(Guid Id, string Categorie, string Type, string Nom, string Libelle, string Precision);
/// <summary>
/// <c>Utilisations</c> : nombre de MEP où la résolution a été liée à une étape (une fois par MEP) ; <c>CreeeLe</c> : création.
/// Les listes sont triées par nombre d'utilisations, puis par ordre de création.
/// </summary>
public record ResolutionTypeDto(Guid Id, string Titre, string Symptomes, string Diagnostic, List<Guid> SystemeCatalogueIds,
    List<CorrectionDto> Corrections, string VerificationInstruction, string VerificationAttendu, bool Actif, int Version, DateTimeOffset ModifieeLe,
    Guid? ModifieeParId, string? ModifieeParNom, int Utilisations, DateTimeOffset CreeeLe);

public record VersionResolutionTypeDto(int Version, string Action, DateTimeOffset ModifieeLe, Guid? ModifieeParId, string? ModifieeParNom,
    string Titre, string Symptomes, string Diagnostic, List<Guid> SystemeCatalogueIds, List<CorrectionDto> Corrections,
    string VerificationInstruction, string VerificationAttendu, bool Actif);

public record ResponsableTechniqueDto(Guid ParticipantId, List<Guid> SystemeIds, string Perimetre, DisponibiliteJourJ Disponibilite, string ContactJourJ);

public record DroitsDto(bool EstPilote, bool PeutModifierPlan, Guid? ParticipantId, bool EstApprobateur, bool EstResponsableTechnique, bool PeutEditerContenu);

/// <summary>
/// <c>RetourArriereActive</c> : plan de retour arrière prévu (false : ni plan ni échéance réclamés, aucun déclenchement).
/// <c>EcheanceRepli</c> est facultative et ne s'applique que si le retour arrière est activé.
/// <c>Type</c> : MEP complète ou simplifiée. MEP simplifiée : <c>SuppleantIds</c> liste les suppléants implicites (toutes les
/// personnes sauf le pilote, aucune sans pilote), les versions sont vides, les étapes n'ont ni durée, ni criticité, ni
/// résolutions liées.
/// </summary>
public record MepDto(Guid Id, int Version, string Outil, string VersionActuelle, string VersionCible, string Objet, string Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid> SuppleantIds, Phase Phase, DateTimeOffset? EcheanceRepli,
    IssueMep? Issue, RetourArriereDto? RetourArriere, ClotureDto? Cloture, PlanRepliDto PlanRepli, SauvegardeDto Sauvegarde,
    ValidationDto? ProcedureRelue, ValidationDto? DemarrageAutorise,
    List<ParticipantDto> Participants, List<SystemeDto> Systemes, List<ObjetImpacteDto> ObjetsImpactes, List<EtapeDto> Etapes,
    List<ExecutionResolutionDto> Resolutions, List<string> ErreursPlan, DroitsDto Droits,
    bool EstModele, int ApprobationsMinimum, List<ApprobationDto> Approbations, List<CritereValideDto> GoNoGo,
    List<FicheRecetteDto> FichesRecette, bool BilanActive, BilanDto? Bilan, string? NonRetourFranchi, List<PieceJointeDto> PiecesJointes,
    List<ResponsableTechniqueDto> ResponsablesTechniques, List<string> Avertissements, bool RetourArriereActive, CommunicationDto Communication, TypeMep Type);

/// <summary>
/// Section Communication de la MEP (tout est facultatif). <c>PreuveValidationId</c> : pièce jointe de la MEP qui prouve la
/// validation par les utilisateurs (null si aucune, ou si la pièce a été supprimée).
/// </summary>
public record CommunicationDto(string ReferenceDemande, NiveauRisque NiveauRisque, string Benefices, string UtilisateursImpactes,
    bool ArretApplicatif, string ActionUtilisateurs, TimeOnly? HeureLimite, DateOnly? DateAnnoncee, TimeOnly? HeureDebutAnnoncee,
    TimeOnly? HeureFinAnnoncee, bool ValideeParUtilisateurs, string ValideePar, DateOnly? ValideeLe, Guid? PreuveValidationId);

/// <summary>
/// Modèle de texte d'une équipe. <c>Fourni</c> : copie d'un modèle fourni (ne se supprime pas, se rétablit) ; <c>Modifie</c> :
/// son texte diffère du texte fourni. <c>PeutModifier</c> : le compte courant est membre de l'équipe ou administrateur.
/// </summary>
public record ModeleTexteDto(Guid Id, Guid EquipeId, string Nom, UsageModeleTexte Usage, string Objet, string Corps, bool Fourni, bool Modifie,
    int Version, DateTimeOffset ModifieLe, Guid? ModifieParId, string? ModifiePar, bool PeutModifier);

/// <summary>Variable des modèles de texte (<c>{{cle}}</c>) ; <c>Liste</c> : rendue en puces quand elle est seule sur sa ligne.</summary>
public record VariableTexteDto(string Cle, string Libelle, string Groupe, string Description, bool Liste);

/// <summary>Valeur d'une variable pour une MEP : <c>Texte</c> ou <c>Liste</c> ; les deux null : information manquante.</summary>
public record ValeurVariableDto(string Cle, string? Texte, List<string>? Liste);

/// <summary>
/// Valeurs des variables pour une MEP et une équipe. <c>EquipeId</c> : équipe utilisée (celle demandée, sinon celle proposée) ;
/// <c>EquipeProposeeId</c> : équipe du pilote (à défaut, une équipe du compte courant, puis la première équipe) ; null s'il n'y a aucune équipe.
/// </summary>
public record VariablesMepDto(Guid? EquipeId, Guid? EquipeProposeeId, List<ValeurVariableDto> Valeurs);

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
/// <summary>Informations communes aux modèles de texte d'une équipe (membres de l'équipe ou administrateurs).</summary>
public record EquipeCommunicationSaisie(string? ContactSupport, string? Signature);
/// <summary>Relie le compte à <c>ComptePrincipalId</c> (même personne) ; <c>Desactiver</c> : désactive aussi le compte relié.</summary>
public record LiaisonCompteSaisie(Guid? ComptePrincipalId, bool Desactiver = false);
public record CompteEtMotDePasseDto(UtilisateurDto Utilisateur, string MotDePasseProvisoire);
public record EvenementAdminDto(long Id, DateTimeOffset Quand, string Auteur, string Type, string Detail, string? AuteurLogin);
public record UtilisateurSaisie(string? Login, string? NomAffiche, string? MotDePasse, bool EstAdmin, string? Role = null, string? Contact = null);
public record ExpertiseSaisie(Guid? SystemeCatalogueId, string? Detail);
/// <summary><c>NomAffiche</c> absent ou null : inchangé.</summary>
public record ProfilSaisie(string? NomAffiche, string? Role, string? Contact, List<ExpertiseSaisie>? Expertises);
/// <summary>
/// Création d'une MEP, ou d'un modèle si <c>Modele</c> vaut true. <c>Type</c> absent : MEP complète ; un modèle est
/// toujours complet.
/// </summary>
public record MepCreationSaisie(string? Outil, string? Objet, bool? Modele = null, TypeMep? Type = null);

/// <summary>Changement de type d'une MEP en préparation (passer en simplifiée efface durées, criticités, résolutions liées et versions).</summary>
public record TypeMepSaisie(int? Version, TypeMep? Type);

/// <summary>
/// <c>BilanActive</c> et <c>RetourArriereActive</c> absents : inchangés. <c>EcheanceRepli</c> null : sans échéance. La MEP n'a plus
/// d'environnement propre : ils se choisissent par système (<c>PUT /meps/{id}/systemes/{sid}/environnements</c>).
/// MEP simplifiée : <c>VersionActuelle</c>, <c>VersionCible</c> et <c>SuppleantIds</c> sont ignorés (pas de versions, suppléants implicites).
/// </summary>
public record MepIdentiteSaisie(int? Version, string? Outil, string? VersionActuelle, string? VersionCible, string? Objet, string? Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string? FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid>? SuppleantIds, DateTimeOffset? EcheanceRepli, bool? BilanActive = null, bool? RetourArriereActive = null);

/// <summary>Section Communication, remplacée en entier (champs absents : vides).</summary>
public record CommunicationSaisie(int? Version, string? ReferenceDemande, NiveauRisque? NiveauRisque, string? Benefices, string? UtilisateursImpactes,
    bool ArretApplicatif, string? ActionUtilisateurs, TimeOnly? HeureLimite, DateOnly? DateAnnoncee, TimeOnly? HeureDebutAnnoncee,
    TimeOnly? HeureFinAnnoncee, bool ValideeParUtilisateurs, string? ValideePar, DateOnly? ValideeLe, Guid? PreuveValidationId);

/// <summary>Modèle de texte. <c>Version</c> : obligatoire pour une modification ; <c>EquipeId</c> : à la création seulement (l'équipe ne change pas ensuite).</summary>
public record ModeleTexteSaisie(int? Version, Guid? EquipeId, string? Nom, UsageModeleTexte? Usage, string? Objet, string? Corps);

public record SauvegardeSaisie(int? Version, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string? Procedure, string? Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string? PreuveTestRestauration,
    ModeSauvegarde? Mode = null);

public record ParticipantSaisie(string? Nom, string? Role, string? Contact, Guid? UtilisateurId);
public record SystemeSaisie(string? Nom, string? Cible, string? Impact);
/// <summary><c>Emplacement</c> et <c>MethodeRecuperation</c> absents : inchangés.</summary>
public record SauvegardeSystemeSaisie(int? Version, StatutSauvegarde? Statut, ModeSauvegarde? Moment, string? Contenu, string? Justification,
    string? Emplacement = null, string? MethodeRecuperation = null);
/// <summary>Environnements touchés par la MEP sur un système, dans l'ordre voulu (ceux de sa fiche au catalogue, ou libres hors catalogue).</summary>
public record EnvironnementsSystemeSaisie(List<string>? Noms);
/// <summary>Objet du catalogue retenu pour un système de la MEP, avec sa version ou correction.</summary>
public record ObjetChoisiSaisie(Guid? ObjetCatalogueId, string? Version);
/// <summary>Objets impactés d'un système : la liste remplace les objets du catalogue déjà retenus pour ce système.</summary>
public record ObjetsSystemeSaisie(List<ObjetChoisiSaisie>? Objets);
/// <summary>Ligne du fichier Excel rejetée à l'import (objet absent de la fiche du système, système inconnu…).</summary>
public record LigneRejeteeDto(int Ligne, string Systeme, string Categorie, string Nom, string Raison, Guid? SystemeCatalogueId);
/// <summary>Résultat d'un import Excel d'objets impactés : MEP à jour, compteurs et lignes rejetées (rien n'est créé au catalogue).</summary>
public record ImportObjetsDto(MepDto Mep, int Ajoutes, int Modifies, int Inchanges, List<LigneRejeteeDto> Rejetees);
public record EnvironnementEtapeSaisie(Guid? SystemeId, string? Nom);

/// <summary>
/// <c>Ligne</c> et <c>Position</c> ne servent qu'à la création (<c>POST /etapes</c>). <c>Fin</c>, <c>SauvegardeEmplacement</c>,
/// <c>SauvegardeMethodeRecuperation</c> et <c>Environnements</c> absents : inchangés (test unitaire pour une nouvelle étape) ;
/// les environnements d'un système retiré de l'étape partent avec lui.
/// </summary>
public record EtapeSaisie(int? Revision, string? Action, List<Guid>? SystemeIds, Guid? ResponsableId, Criticite? Criticite,
    string? Instructions, string? ResultatAttendu, List<Guid>? Dependances, TypePlan? Plan = null, List<Guid>? ResolutionIds = null,
    TypeEtape? Type = null, bool PointDeNonRetour = false, int? DureeEstimeeMinutes = null, int? Ligne = null, int? Position = null,
    FinEtape? Fin = null, string? SauvegardeEmplacement = null, string? SauvegardeMethodeRecuperation = null,
    List<EnvironnementEtapeSaisie>? Environnements = null);

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
/// <summary>Duplication : la copie reprend les systèmes avec leurs environnements, modifiables ensuite.</summary>
public record DuplicationSaisie(bool CommeModele);
public record FicheRecetteCreationSaisie(Guid? SystemeId, string? Titre);
public record EtapeTestSaisie(Guid? Id, string? Instructions, string? ResultatAttendu);
/// <param name="SystemeId">Facultatif : la fiche garde son système ; sert seulement à rattacher une ancienne fiche sans système.</param>
public record FicheRecetteSaisie(Guid? SystemeId, string? Titre, List<EtapeTestSaisie>? Etapes);
/// <summary>Insertion d'un modèle de la bibliothèque (copie) dans un système de la MEP.</summary>
public record InsertionModeleRecetteSaisie(Guid? ModeleId, Guid? SystemeId);

public record EtapeModeleRecetteDto(int Ordre, string Instructions, string ResultatAttendu);
/// <summary><c>Utilisations</c> : nombre de MEP où le modèle a été inséré (une fois par MEP) ; <c>CreeLe</c> : création.</summary>
public record ModeleRecetteDto(Guid Id, string Titre, Guid SystemeCatalogueId, List<EtapeModeleRecetteDto> Etapes, int Version,
    DateTimeOffset ModifieLe, Guid? ModifieParId, string? ModifiePar, int Utilisations, DateTimeOffset CreeLe);
public record EtapeModeleRecetteSaisie(string? Instructions, string? ResultatAttendu);
/// <param name="Version">Obligatoire pour une modification (verrou optimiste).</param>
public record ModeleRecetteSaisie(int? Version, string? Titre, Guid? SystemeCatalogueId, List<EtapeModeleRecetteSaisie>? Etapes);
/// <summary>« Enregistrer comme modèle » : copie d'une fiche de recette d'une MEP dans la bibliothèque.</summary>
public record ModeleDepuisFicheSaisie(Guid? MepId, Guid? FicheId, string? Titre, Guid? SystemeCatalogueId);

/// <summary>
/// Étape modèle de la bibliothèque d'étapes. <c>Utilisations</c> : nombre de MEP où elle a été insérée (une fois par MEP) ;
/// <c>CreeLe</c> : création (les listes sont triées par nombre d'utilisations, puis par ordre de création).
/// </summary>
public record ModeleEtapeDto(Guid Id, string Action, List<Guid> SystemeCatalogueIds, TypeEtape Type, Criticite? Criticite, int? DureeEstimeeMinutes,
    string Instructions, string ResultatAttendu, FinEtape Fin, string SauvegardeEmplacement, string SauvegardeMethodeRecuperation,
    List<Guid> ResolutionIds, int Utilisations, DateTimeOffset CreeLe, int Version, DateTimeOffset ModifieLe, Guid? ModifieParId, string? ModifiePar);
/// <param name="Version">Obligatoire pour une modification (verrou optimiste).</param>
/// <param name="Type">Absent : étape normale (Standard). Les champs de sauvegarde ne sont gardés que pour le type Sauvegarde.</param>
public record ModeleEtapeSaisie(int? Version, string? Action, List<Guid>? SystemeCatalogueIds, TypeEtape? Type, Criticite? Criticite,
    int? DureeEstimeeMinutes, string? Instructions, string? ResultatAttendu, FinEtape? Fin, string? SauvegardeEmplacement,
    string? SauvegardeMethodeRecuperation, List<Guid>? ResolutionIds);
/// <summary>« Transformer en étape modèle » : copie d'une étape d'une MEP dans la bibliothèque, sous le nom donné (l'action à défaut).</summary>
public record ModeleEtapeDepuisEtapeSaisie(Guid? MepId, Guid? EtapeId, string? Action);
/// <summary>
/// Insertion d'une copie d'étape modèle dans un plan de la MEP : <c>Ligne</c> absente = nouvelle ligne en bas, <c>Position</c>
/// absente = en fin de ligne ; <c>Plan</c> absent = déploiement.
/// </summary>
public record InsertionEtapeModeleSaisie(Guid? ModeleId, TypePlan? Plan = null, int? Ligne = null, int? Position = null);

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
/// <summary>Production absente : déduite du nom (« Production », « prod »…).</summary>
public record EnvironnementCatalogueSaisie(string? Nom, string? Cible, bool? Production);
public record EnvironnementsCatalogueSaisie(List<EnvironnementCatalogueSaisie>? Environnements);
/// <summary><c>Ancien</c> : nom actuel d'une catégorie renommée (ses objets la suivent).</summary>
public record CategorieSaisie(string? Nom, string? Ancien = null);
/// <summary>Catégories de la fiche, dans l'ordre ; une catégorie absente est retirée avec ses objets.</summary>
public record CategoriesSystemeSaisie(List<CategorieSaisie>? Categories);
public record ObjetCatalogueSaisie(string? Categorie, string? Type, string? Nom, string? Libelle, string? Precision);
public record CategoriesDefautSaisie(List<string>? Categories);
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
