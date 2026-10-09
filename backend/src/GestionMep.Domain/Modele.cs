namespace GestionMep.Domain;

public enum Phase { Preparation, Execution }

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
    /// <summary>Identifiant lisible et stable (E0001…), jamais renuméroté.</summary>
    public string Code { get; set; } = "";
    public int Ordre { get; set; }
    public string Action { get; set; } = "";
    public Guid? SystemeId { get; set; }
    public Guid? ResponsableId { get; set; }
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
