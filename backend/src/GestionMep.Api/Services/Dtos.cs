using GestionMep.Domain;

namespace GestionMep.Api.Services;

// ---------- Sorties ----------
public record UtilisateurDto(Guid Id, string Login, string NomAffiche, bool EstAdmin);

public record MepResumeDto(Guid Id, string Outil, string VersionCible, string Objet, DateOnly? DateMep, Phase Phase,
    DateTimeOffset? EcheanceRepli, string? Pilote, int NbEtapes, int NbReussies, int NbEchecs);

public record SauvegardeDto(bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string Procedure, string Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string PreuveTestRestauration);

public record ValidationDto(string Par, DateTimeOffset Le);
public record ParticipantDto(Guid Id, string Nom, string Role, string Contact, Guid? UtilisateurId, string? UtilisateurNom);
public record SystemeDto(Guid Id, string Nom, string Cible, string Impact);
public record LivrableDto(Guid Id, string Objet, string Version, string Emplacement, Guid? SystemeId);
public record DisponibiliteDto(StatutDisponibilite Statut, string Libelle);

public record EtapeDto(Guid Id, string Code, int Ordre, string Action, Guid? SystemeId, Guid? ResponsableId, Criticite? Criticite,
    EtatEtape Etat, string Instructions, string ResultatAttendu, string Preuve, DateTimeOffset? DebutReel, DateTimeOffset? FinReelle,
    int Revision, List<Guid> Dependances, DisponibiliteDto Disponibilite);

public record DroitsDto(bool EstPilote, bool PeutModifierPlan, Guid? ParticipantId);

public record MepDto(Guid Id, int Version, string Outil, string VersionActuelle, string VersionCible, string Objet, string Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, Guid? SuppleantId, Phase Phase, DateTimeOffset? EcheanceRepli, SauvegardeDto Sauvegarde,
    ValidationDto? ProcedureRelue, ValidationDto? DemarrageAutorise,
    List<ParticipantDto> Participants, List<SystemeDto> Systemes, List<LivrableDto> Livrables, List<EtapeDto> Etapes,
    List<string> ErreursPlan, DroitsDto Droits);

public record EvenementDto(long Id, DateTimeOffset Quand, string Auteur, string Type, string Detail);

// ---------- Entrées ----------
public record ConnexionSaisie(string? Login, string? MotDePasse);
public record UtilisateurSaisie(string? Login, string? NomAffiche, string? MotDePasse, bool EstAdmin);
public record MepCreationSaisie(string? Outil, string? Objet);

public record MepIdentiteSaisie(int? Version, string? Outil, string? VersionActuelle, string? VersionCible, string? Objet, string? Impacts,
    DateOnly? DateMep, TimeOnly? HeureDebut, TimeOnly? HeureFin, string? FuseauHoraire, int? IndisponibiliteMinutes,
    Guid? PiloteId, Guid? SuppleantId, DateTimeOffset? EcheanceRepli);

public record SauvegardeSaisie(int? Version, bool Structure, bool Donnees, bool Configuration, Guid? ResponsableId, DateTimeOffset? RealiseeLe,
    string? Procedure, string? Copies, bool RestaurationTestee, DateOnly? DateTestRestauration, string? PreuveTestRestauration);

public record ParticipantSaisie(string? Nom, string? Role, string? Contact, Guid? UtilisateurId);
public record SystemeSaisie(string? Nom, string? Cible, string? Impact);
public record LivrableSaisie(string? Objet, string? Version, string? Emplacement, Guid? SystemeId);

public record EtapeSaisie(int? Revision, string? Action, Guid? SystemeId, Guid? ResponsableId, Criticite? Criticite,
    string? Instructions, string? ResultatAttendu, List<Guid>? Dependances);

public record DeplacerSaisie(string? Sens);
public record RevisionSaisie(int? Revision);
public record TerminerSaisie(int? Revision, EtatEtape? Resultat, string? Preuve, bool ResultatVerifie);
public record ProcedureRelueSaisie(bool Relue);
public record PhaseSaisie(Phase? Phase);
