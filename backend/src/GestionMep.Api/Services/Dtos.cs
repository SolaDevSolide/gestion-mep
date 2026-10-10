using GestionMep.Domain;

namespace GestionMep.Api.Services;

// ---------- Sorties ----------
public record ExpertiseDto(Guid SystemeCatalogueId, string Detail);
public record UtilisateurDto(Guid Id, string Login, string NomAffiche, bool EstAdmin, string Role, string Contact, List<ExpertiseDto> Expertises);

public record MepResumeDto(Guid Id, string Outil, string VersionCible, string Objet, DateOnly? DateMep, Phase Phase,
    DateTimeOffset? EcheanceRepli, string? Pilote, int NbEtapes, int NbReussies, int NbEchecs, string Environnement, bool EstModele);

public record SauvegardeDto(ModeSauvegarde Mode, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string Procedure, string Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string PreuveTestRestauration);

public record ValidationDto(string Par, DateTimeOffset Le);
public record ParticipantDto(Guid Id, string Nom, string Role, string Contact, Guid? UtilisateurId, string? UtilisateurNom);
public record SauvegardeSystemeDto(StatutSauvegarde Statut, ModeSauvegarde Moment, string Contenu, string Justification);
public record SystemeDto(Guid Id, string Nom, string Cible, string Impact, Guid? CatalogueId, SauvegardeSystemeDto Sauvegarde);
public record PlanRepliDto(string QuandDeclencher, int? DureeTesteeMinutes, string VersionARestaurer, string DonneesDepuisSauvegarde, string SiRepliEchoue);
/// <summary><c>Par</c> vaut null quand le retour arrière s'est déclenché automatiquement.</summary>
public record RetourArriereDto(DateTimeOffset Le, string? Par, string Motif);
public record ClotureDto(DateTimeOffset Le, string? Par);
public record LivrableDto(Guid Id, string Objet, string Version, string Emplacement, Guid? SystemeId);
public record DisponibiliteDto(StatutDisponibilite Statut, string Libelle);

/// <summary><c>Ligne</c> : ligne du graphe (0 en haut) ; <c>Ordre</c> : position dans la ligne (0 à gauche).</summary>
public record EtapeDto(Guid Id, TypePlan Plan, string Code, int Ligne, int Ordre, string Action, List<Guid> SystemeIds, Guid? ResponsableId, Guid? ExecutantId,
    Criticite? Criticite, int? DureeEstimeeMinutes,
    EtatEtape Etat, string Instructions, string ResultatAttendu, string Preuve, DateTimeOffset? DebutReel, DateTimeOffset? FinReelle,
    int Revision, List<Guid> Dependances, List<Guid> ResolutionIds, DisponibiliteDto Disponibilite,
    TypeEtape Type, bool PointDeNonRetour, ValidationDto? NonRetourAutorise);

public record ApprobationDto(Guid Id, Guid UtilisateurId, string Nom, DecisionApprobation? Decision, string Commentaire, DateTimeOffset? Le);
public record CritereValideDto(string Libelle, string? Par, DateTimeOffset Le);
public record EtapeTestDto(Guid Id, int Ordre, string Instructions, string ResultatAttendu, ResultatControle? Resultat,
    string Observation, string? Par, DateTimeOffset? Le);
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
public record ResolutionTypeDto(Guid Id, string Titre, string Symptomes, string Diagnostic, Guid? SystemeCatalogueId,
    List<CorrectionDto> Corrections, string VerificationInstruction, string VerificationAttendu, bool Actif, int Version, DateTimeOffset ModifieeLe);

public record ResponsableTechniqueDto(Guid ParticipantId, List<Guid> SystemeIds, string Perimetre, DisponibiliteJourJ Disponibilite, string ContactJourJ);

public record DroitsDto(bool EstPilote, bool PeutModifierPlan, Guid? ParticipantId, bool EstApprobateur, bool EstResponsableTechnique, bool PeutEditerContenu);

public record MepDto(Guid Id, int Version, string Outil, string VersionActuelle, string VersionCible, string Objet, string Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid> SuppleantIds, Phase Phase, DateTimeOffset? EcheanceRepli,
    IssueMep? Issue, RetourArriereDto? RetourArriere, ClotureDto? Cloture, PlanRepliDto PlanRepli, SauvegardeDto Sauvegarde,
    ValidationDto? ProcedureRelue, ValidationDto? DemarrageAutorise,
    List<ParticipantDto> Participants, List<SystemeDto> Systemes, List<LivrableDto> Livrables, List<EtapeDto> Etapes,
    List<ExecutionResolutionDto> Resolutions, List<string> ErreursPlan, DroitsDto Droits,
    string Environnement, bool EstModele, int ApprobationsMinimum, List<ApprobationDto> Approbations, List<CritereValideDto> GoNoGo,
    List<FicheRecetteDto> FichesRecette, bool BilanActive, BilanDto? Bilan, string? NonRetourFranchi, List<PieceJointeDto> PiecesJointes,
    List<ResponsableTechniqueDto> ResponsablesTechniques, List<string> Avertissements);

public record EvenementDto(long Id, DateTimeOffset Quand, string Auteur, string Type, string Detail);

// ---------- Entrées ----------
public record ConnexionSaisie(string? Login, string? MotDePasse);
public record UtilisateurSaisie(string? Login, string? NomAffiche, string? MotDePasse, bool EstAdmin, string? Role = null, string? Contact = null);
public record ExpertiseSaisie(Guid? SystemeCatalogueId, string? Detail);
/// <summary><c>NomAffiche</c> absent ou null : inchangé.</summary>
public record ProfilSaisie(string? NomAffiche, string? Role, string? Contact, List<ExpertiseSaisie>? Expertises);
public record MepCreationSaisie(string? Outil, string? Objet);

public record MepIdentiteSaisie(int? Version, string? Outil, string? VersionActuelle, string? VersionCible, string? Objet, string? Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string? FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, List<Guid>? SuppleantIds, DateTimeOffset? EcheanceRepli, string? Environnement = null, bool? BilanActive = null);

public record SauvegardeSaisie(int? Version, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string? Procedure, string? Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string? PreuveTestRestauration,
    ModeSauvegarde? Mode = null);

public record ParticipantSaisie(string? Nom, string? Role, string? Contact, Guid? UtilisateurId);
public record SystemeSaisie(string? Nom, string? Cible, string? Impact);
public record SauvegardeSystemeSaisie(int? Version, StatutSauvegarde? Statut, ModeSauvegarde? Moment, string? Contenu, string? Justification);
public record LivrableSaisie(string? Objet, string? Version, string? Emplacement, Guid? SystemeId);

/// <summary><c>Ligne</c> et <c>Position</c> ne servent qu'à la création (<c>POST /etapes</c>).</summary>
public record EtapeSaisie(int? Revision, string? Action, List<Guid>? SystemeIds, Guid? ResponsableId, Criticite? Criticite,
    string? Instructions, string? ResultatAttendu, List<Guid>? Dependances, TypePlan? Plan = null, List<Guid>? ResolutionIds = null,
    TypeEtape? Type = null, bool PointDeNonRetour = false, int? DureeEstimeeMinutes = null, int? Ligne = null, int? Position = null);

public record PlacerSaisie(int? Ligne, int? Position, bool LiensParDefaut = true);
public record RevisionSaisie(int? Revision);
public record PriseDeMainSaisie(int? Revision, string? Motif);
public record ResponsableTechniqueSaisie(Guid? ParticipantId, List<Guid>? SystemeIds, string? Perimetre, DisponibiliteJourJ? Disponibilite, string? ContactJourJ);
public record ResponsablesTechniquesSaisie(int? Version, List<ResponsableTechniqueSaisie>? Responsables);
public record TerminerSaisie(int? Revision, EtatEtape? Resultat, string? Preuve, bool ResultatVerifie);
public record ProcedureRelueSaisie(bool Relue);
public record PhaseSaisie(Phase? Phase, List<Guid>? CriteresCoches = null);
public record NoGoSaisie(string? Motif);
public record ApprobateursSaisie(int? Version, List<Guid>? UtilisateurIds);
public record DecisionSaisie(int? Version, DecisionApprobation? Decision, string? Commentaire);
public record DuplicationSaisie(string? Environnement, bool CommeModele);
public record FicheRecetteCreationSaisie(Guid? SystemeId, string? Titre);
public record EtapeTestSaisie(Guid? Id, string? Instructions, string? ResultatAttendu);
public record FicheRecetteSaisie(Guid? SystemeId, string? Titre, List<EtapeTestSaisie>? Etapes);
public record ClotureSaisie(string? CeQuiAMarche, string? Incidents, string? Actions);
public record CritereGoNoGoSaisie(string? Libelle, int? Ordre, bool? Actif);
public record ParametresSaisie(int? ApprobationsMinimum);

public record PlanRepliSaisie(int? Version, string? QuandDeclencher, int? DureeTesteeMinutes, string? VersionARestaurer,
    string? DonneesDepuisSauvegarde, string? SiRepliEchoue);
public record RetourArriereSaisie(string? Motif);
public record SystemeCatalogueSaisie(string? Nom, string? Description, string? Cible, bool? Actif);
public record DepuisCatalogueSaisie(Guid? CatalogueId, string? Impact);
public record CorrectionSaisie(int? Ordre, string? Instruction, string? ResultatAttendu);
public record ResolutionTypeSaisie(int? Version, string? Titre, string? Symptomes, string? Diagnostic, Guid? SystemeCatalogueId,
    List<CorrectionSaisie>? Corrections, string? VerificationInstruction, string? VerificationAttendu);
public record DemarrerResolutionSaisie(Guid? ResolutionTypeId, int? Revision);
public record ControleSaisie(ResultatControle? Resultat, string? Observation);
