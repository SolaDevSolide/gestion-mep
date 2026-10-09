namespace GestionMep.Domain;

public enum Phase { Preparation, Execution, RetourArriere, Cloturee }

/// <summary>Issue d'une MEP clôturée.</summary>
public enum IssueMep { Reussie, Repliee }

/// <summary>Plan auquel appartient une étape : le déploiement, ou la procédure de retour arrière.</summary>
public enum TypePlan { Deploiement, RetourArriere }

public enum ResultatControle { Conforme, Echec }

public enum StatutResolution { EnCours, Reussie, Echouee }

public enum EtatEtape { AFaire, EnCours, Reussie, Echec }

public enum Criticite { Faible, Moderee, Elevee, Bloquante }

public class Utilisateur
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Login { get; set; } = "";
    public string NomAffiche { get; set; } = "";
    public string MotDePasseHash { get; set; } = "";
    public bool EstAdmin { get; set; }
}

public class Mep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // 1. Intervention
    public string Outil { get; set; } = "";
    public string VersionActuelle { get; set; } = "";
    public string VersionCible { get; set; } = "";
    public string Objet { get; set; } = "";
    public string Impacts { get; set; } = "";
    public DateOnly? DateMep { get; set; }
    public TimeOnly? HeureDebut { get; set; }
    public TimeOnly? HeureFin { get; set; }
    public string FuseauHoraire { get; set; } = "Europe/Paris";
    public int? IndisponibiliteMinutes { get; set; }
    public Guid? PiloteId { get; set; }
    public Guid? SuppleantId { get; set; }

    // Pilotage
    public Phase Phase { get; set; } = Phase.Preparation;
    public DateTimeOffset? EcheanceRepli { get; set; }
    public IssueMep? Issue { get; set; }
    public DateTimeOffset? RetourArriereDeclencheLe { get; set; }
    /// <summary>Utilisateur ayant déclenché le retour arrière ; null si déclenchement automatique.</summary>
    public Guid? RetourArriereDeclenchePar { get; set; }
    public string RetourArriereMotif { get; set; } = "";
    public DateTimeOffset? ClotureeLe { get; set; }
    public Guid? ClotureeParId { get; set; }

    // Onglet « Retour arrière » de l'Excel : déclenchement et données
    public PlanRepli PlanRepli { get; set; } = new();

    // 4. Sauvegarde
    public Sauvegarde Sauvegarde { get; set; } = new();

    // 5. Validation avant intervention
    public Guid? ProcedureRelueParId { get; set; }
    public DateTimeOffset? ProcedureRelueLe { get; set; }
    public Guid? DemarrageAutoriseParId { get; set; }
    public DateTimeOffset? DemarrageAutoriseLe { get; set; }

    public DateTimeOffset CreeLe { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreeParUtilisateurId { get; set; }

    /// <summary>Compteur de révision pour le verrou optimiste (équivalent de la colonne « Révision » de l'Excel).</summary>
    public int Version { get; set; }

    public List<Participant> Participants { get; set; } = [];
    public List<Systeme> Systemes { get; set; } = [];
    public List<Livrable> Livrables { get; set; } = [];
    public List<Etape> Etapes { get; set; } = [];
    public List<ExecutionResolution> Resolutions { get; set; } = [];

    public IEnumerable<Etape> EtapesDu(TypePlan plan) => Etapes.Where(e => e.Plan == plan).OrderBy(e => e.Ordre);
}

public class PlanRepli
{
    public string QuandDeclencher { get; set; } = "";
    public int? DureeTesteeMinutes { get; set; }
    public string VersionARestaurer { get; set; } = "";
    public string DonneesDepuisSauvegarde { get; set; } = "";
    public string SiRepliEchoue { get; set; } = "";
}

/// <summary>Personne impliquée dans la MEP (tableau tActeurs). Peut être reliée à un compte de l'application.</summary>
public class Participant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    public string Nom { get; set; } = "";
    public string Role { get; set; } = "";
    public string Contact { get; set; } = "";
    public Guid? UtilisateurId { get; set; }
}

/// <summary>Système impacté (tableau tSystemes).</summary>
public class Systeme
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    public string Nom { get; set; } = "";
    public string Cible { get; set; } = "";
    public string Impact { get; set; } = "";
    /// <summary>Système du catalogue global dont celui-ci est issu, le cas échéant.</summary>
    public Guid? CatalogueId { get; set; }
}

/// <summary>Système du catalogue global, saisi une fois et sélectionné ensuite dans chaque MEP.</summary>
public class SystemeCatalogue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    public string Cible { get; set; } = "";
    public bool Actif { get; set; } = true;
}

/// <summary>
/// Résolution type d'un problème connu, réutilisable dans toutes les MEP
/// (ex. « l'utilisateur applicatif a été retiré des droits de la base »).
/// </summary>
public class ResolutionType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titre { get; set; } = "";
    /// <summary>Symptômes observés : comment reconnaître le problème.</summary>
    public string Symptomes { get; set; } = "";
    /// <summary>Comment confirmer le diagnostic avant de corriger.</summary>
    public string Diagnostic { get; set; } = "";
    public Guid? SystemeCatalogueId { get; set; }
    public List<Correction> Corrections { get; set; } = [];
    public string VerificationInstruction { get; set; } = "";
    public string VerificationAttendu { get; set; } = "";
    public bool Actif { get; set; } = true;
    public int Version { get; set; }
    public DateTimeOffset ModifieeLe { get; set; } = DateTimeOffset.UtcNow;
}

public class Correction
{
    public int Ordre { get; set; }
    public string Instruction { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
}

/// <summary>Application d'une résolution type à une étape en échec, avec une copie figée de la résolution.</summary>
public class ExecutionResolution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    public Guid EtapeId { get; set; }
    public Guid ResolutionTypeId { get; set; }
    public string Titre { get; set; } = "";
    public string Symptomes { get; set; } = "";
    public string Diagnostic { get; set; } = "";
    public List<CorrectionExecutee> Corrections { get; set; } = [];
    public string VerificationInstruction { get; set; } = "";
    public string VerificationAttendu { get; set; } = "";
    public ResultatControle? VerificationResultat { get; set; }
    public string VerificationObservation { get; set; } = "";
    public StatutResolution Statut { get; set; } = StatutResolution.EnCours;
    public Guid? DemarreeParId { get; set; }
    public DateTimeOffset DemarreeLe { get; set; }
    public DateTimeOffset? TermineeLe { get; set; }
}

public class CorrectionExecutee
{
    public int Ordre { get; set; }
    public string Instruction { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public ResultatControle? Resultat { get; set; }
    public string Observation { get; set; } = "";
    public Guid? ParId { get; set; }
    public DateTimeOffset? Le { get; set; }
}

/// <summary>Objet livré (tableau tLivrables).</summary>
public class Livrable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    public string Objet { get; set; } = "";
    public string Version { get; set; } = "";
    public string Emplacement { get; set; } = "";
    public Guid? SystemeId { get; set; }
}

public class Sauvegarde
{
    public bool Structure { get; set; }
    public bool Donnees { get; set; }
    public bool Configuration { get; set; }
    public Guid? ResponsableId { get; set; }
    public DateTimeOffset? RealiseeLe { get; set; }
    public string Procedure { get; set; } = "";
    public string Copies { get; set; } = "";
    public bool RestaurationTestee { get; set; }
    public DateOnly? DateTestRestauration { get; set; }
    public string PreuveTestRestauration { get; set; } = "";
}

/// <summary>Action du plan d'exécution (tableau tEtapes).</summary>
public class Etape
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    public TypePlan Plan { get; set; } = TypePlan.Deploiement;
    /// <summary>Identifiant lisible et stable (E0001… pour le déploiement, R0001… pour le retour arrière), jamais renuméroté.</summary>
    public string Code { get; set; } = "";
    public int Ordre { get; set; }
    public string Action { get; set; } = "";
    public Guid? SystemeId { get; set; }
    public Guid? ResponsableId { get; set; }
    /// <summary>Participant ayant pris la main sur l'étape à la place du responsable ; null sinon.</summary>
    public Guid? ExecutantId { get; set; }
    public Criticite? Criticite { get; set; } = Domain.Criticite.Moderee;
    public EtatEtape Etat { get; set; } = EtatEtape.AFaire;
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public string Preuve { get; set; } = "";
    public DateTimeOffset? DebutReel { get; set; }
    public DateTimeOffset? FinReelle { get; set; }
    public int Revision { get; set; }
    /// <summary>Identifiants (Id) des étapes préalables, qui doivent toutes être Réussies.</summary>
    public List<Guid> Dependances { get; set; } = [];
    /// <summary>Résolutions types applicables si l'étape échoue.</summary>
    public List<Guid> ResolutionIds { get; set; } = [];
}

public class EvenementJournal
{
    public long Id { get; set; }
    public Guid MepId { get; set; }
    public DateTimeOffset Quand { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UtilisateurId { get; set; }
    public string Auteur { get; set; } = "";
    public string Type { get; set; } = "";
    public string Detail { get; set; } = "";
}
