namespace GestionMep.Domain;

/// <summary>Phase de la MEP. Stockée en entier : toute nouvelle valeur s'ajoute à la fin.</summary>
public enum Phase { Preparation, Execution, RetourArriere, Cloturee, Recette }

/// <summary>Issue d'une MEP clôturée.</summary>
public enum IssueMep { Reussie, Repliee }

/// <summary>Plan auquel appartient une étape : le déploiement, ou la procédure de retour arrière.</summary>
public enum TypePlan { Deploiement, RetourArriere }

public enum ResultatControle { Conforme, Echec }

public enum StatutResolution { EnCours, Reussie, Echouee }

public enum EtatEtape { AFaire, EnCours, Reussie, Echec }

public enum Criticite { Faible, Moderee, Elevee, Bloquante }

/// <summary>Sauvegarde réalisée avant la MEP (fiche Sauvegarde) ou pendant, par une étape de déploiement dédiée.</summary>
public enum ModeSauvegarde { AvantMep, PendantMep }

/// <summary>Faut-il sauvegarder un système de la MEP ? À renseigner tant que personne n'a répondu.</summary>
public enum StatutSauvegarde { ARenseigner, Requise, NonRequise }

/// <summary>Nature d'une étape de déploiement : standard, ou étape qui réalise la sauvegarde.</summary>
public enum TypeEtape { Standard, Sauvegarde }

public enum DecisionApprobation { Approuvee, Refusee }

/// <summary>Disponibilité d'un responsable technique le jour de la MEP.</summary>
public enum DisponibiliteJourJ { Joignable, Absent }

public class Utilisateur
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Login { get; set; } = "";
    public string NomAffiche { get; set; } = "";
    public string MotDePasseHash { get; set; } = "";
    public bool EstAdmin { get; set; }
    /// <summary>Profil : rôle habituel et contact, repris quand le compte est ajouté à une MEP.</summary>
    public string Role { get; set; } = "";
    public string Contact { get; set; } = "";
    /// <summary>Systèmes du catalogue que la personne connaît : sert à suggérer les responsables techniques.</summary>
    public List<Expertise> Expertises { get; set; } = [];
}

/// <summary>Expertise d'un compte sur un système du catalogue (ex. « Expert de la zone facturation »).</summary>
public class Expertise
{
    public Guid SystemeCatalogueId { get; set; }
    public string Detail { get; set; } = "";
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
    /// <summary>Obsolètes depuis le jalon 4.2 (gardés pour l'existant, plus modifiables) : début = Go, fin = clôture.</summary>
    public DateOnly? DateMep { get; set; }
    public TimeOnly? HeureDebut { get; set; }
    public TimeOnly? HeureFin { get; set; }
    public string FuseauHoraire { get; set; } = "Europe/Paris";
    public int? IndisponibiliteMinutes { get; set; }
    public Guid? PiloteId { get; set; }
    /// <summary>Participants suppléants du pilote : ils ont les mêmes droits que lui.</summary>
    public List<Guid> SuppleantIds { get; set; } = [];
    /// <summary>Environnement visé (texte libre : Recette, Préproduction, Production…).</summary>
    public string Environnement { get; set; } = "";
    /// <summary>Modèle de MEP : sert uniquement à être dupliqué, ne passe jamais en exécution.</summary>
    public bool EstModele { get; set; }

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

    /// <summary>
    /// Développeurs / sachants de la MEP (le pilote peut en faire partie) : ils ont les droits d'édition du contenu et sont à appeler
    /// en cas de problème. Un participant n'apparaît qu'une fois.
    /// </summary>
    public List<ResponsableTechnique> ResponsablesTechniques { get; set; } = [];

    // Circuit d'approbation et go / no-go
    public List<Approbation> Approbations { get; set; } = [];
    /// <summary>Critères go / no-go cochés lors du passage en exécution (copie figée du catalogue).</summary>
    public List<CritereValide> GoNoGo { get; set; } = [];

    // Recette en production (fiches par système) et bilan de clôture
    public List<FicheRecette> FichesRecette { get; set; } = [];
    public bool BilanActive { get; set; } = true;
    public Bilan? Bilan { get; set; }

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

    /// <summary>Étapes d'un plan, de haut en bas puis de gauche à droite (ligne, puis ordre dans la ligne).</summary>
    public IEnumerable<Etape> EtapesDu(TypePlan plan) => Etapes.Where(e => e.Plan == plan).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre);

    /// <summary>Toutes les étapes, triées par plan, ligne et ordre.</summary>
    public IEnumerable<Etape> EtapesTriees() => Etapes.OrderBy(e => e.Plan).ThenBy(e => e.Ligne).ThenBy(e => e.Ordre);
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

/// <summary>Responsable technique d'une MEP (participant) : ce qu'il a développé et comment le joindre le jour J.</summary>
public class ResponsableTechnique
{
    public Guid ParticipantId { get; set; }
    /// <summary>Systèmes de la MEP qu'il a développés (facultatif).</summary>
    public List<Guid> SystemeIds { get; set; } = [];
    public string Perimetre { get; set; } = "";
    public DisponibiliteJourJ Disponibilite { get; set; } = DisponibiliteJourJ.Joignable;
    public string ContactJourJ { get; set; } = "";
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
    /// <summary>Faut-il sauvegarder ce système, quand et comment.</summary>
    public SauvegardeSysteme Sauvegarde { get; set; } = new();
}

/// <summary>Sauvegarde d'un système de la MEP.</summary>
public class SauvegardeSysteme
{
    public StatutSauvegarde Statut { get; set; } = StatutSauvegarde.ARenseigner;
    /// <summary>Avant la MEP, ou pendant par une étape de déploiement de type Sauvegarde sur ce système.</summary>
    public ModeSauvegarde Moment { get; set; } = ModeSauvegarde.AvantMep;
    /// <summary>Quoi et comment (markdown), si la sauvegarde est requise.</summary>
    public string Contenu { get; set; } = "";
    /// <summary>Pourquoi aucune sauvegarde n'est nécessaire : obligatoire si non requise.</summary>
    public string Justification { get; set; } = "";
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
    /// <summary>Systèmes du catalogue concernés (un, plusieurs ou aucun).</summary>
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    public List<Correction> Corrections { get; set; } = [];
    public string VerificationInstruction { get; set; } = "";
    public string VerificationAttendu { get; set; } = "";
    public bool Actif { get; set; } = true;
    public int Version { get; set; }
    public DateTimeOffset ModifieeLe { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Compte auteur de la dernière modification (null : données de démo ou fiche antérieure au suivi).</summary>
    public Guid? ModifieeParId { get; set; }
}

/// <summary>
/// Version archivée d'une résolution type : copie de la fiche telle qu'enregistrée à chaque création,
/// modification ou désactivation, avec son auteur. Permet de savoir qui a changé quoi avant un incident.
/// </summary>
public class VersionResolutionType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResolutionTypeId { get; set; }
    public int Version { get; set; }
    /// <summary>« creee », « modifiee » ou « desactivee » (« existante » : fiche reprise lors de la mise en place du suivi).</summary>
    public string Action { get; set; } = "";
    public DateTimeOffset ModifieeLe { get; set; }
    public Guid? ModifieeParId { get; set; }
    public string Titre { get; set; } = "";
    public string Symptomes { get; set; } = "";
    public string Diagnostic { get; set; } = "";
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    public List<Correction> Corrections { get; set; } = [];
    public string VerificationInstruction { get; set; } = "";
    public string VerificationAttendu { get; set; } = "";
    public bool Actif { get; set; }

    /// <summary>Copie de l'état actuel de la fiche.</summary>
    public static VersionResolutionType De(ResolutionType r, string action) => new()
    {
        ResolutionTypeId = r.Id, Version = r.Version, Action = action, ModifieeLe = r.ModifieeLe, ModifieeParId = r.ModifieeParId,
        Titre = r.Titre, Symptomes = r.Symptomes, Diagnostic = r.Diagnostic, SystemeCatalogueIds = [.. r.SystemeCatalogueIds],
        Corrections = r.Corrections.Select(c => new Correction { Ordre = c.Ordre, Instruction = c.Instruction, ResultatAttendu = c.ResultatAttendu }).ToList(),
        VerificationInstruction = r.VerificationInstruction, VerificationAttendu = r.VerificationAttendu, Actif = r.Actif,
    };
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

/// <summary>
/// Fiche Sauvegarde globale de la MEP. Depuis le jalon 4, ce qu'il faut sauvegarder se décide par système
/// (<see cref="Systeme.Sauvegarde"/>) : <see cref="Mode"/> et les cases restent en base pour compatibilité mais ne sont plus vérifiés.
/// </summary>
public class Sauvegarde
{
    public ModeSauvegarde Mode { get; set; } = ModeSauvegarde.AvantMep;
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
    /// <summary>Type de l'étape (déploiement uniquement : le retour arrière n'a que des étapes standard).</summary>
    public TypeEtape Type { get; set; } = TypeEtape.Standard;
    /// <summary>Point de non-retour : une fois démarrée, le retour arrière n'est plus possible (déploiement uniquement).</summary>
    public bool PointDeNonRetour { get; set; }
    /// <summary>Accord du pilote pour démarrer l'étape point de non-retour.</summary>
    public Guid? NonRetourAutoriseParId { get; set; }
    public DateTimeOffset? NonRetourAutoriseLe { get; set; }
    /// <summary>Identifiant lisible et stable (E0001… pour le déploiement, R0001… pour le retour arrière), jamais renuméroté.</summary>
    public string Code { get; set; } = "";
    /// <summary>Ligne du graphe (0 en haut), propre à chaque plan, sans trou. -1 : à calculer (données d'avant le jalon 4).</summary>
    public int Ligne { get; set; }
    /// <summary>Position dans la ligne (0, 1, 2… de gauche à droite).</summary>
    public int Ordre { get; set; }
    public string Action { get; set; } = "";
    /// <summary>Systèmes de la MEP concernés par l'étape (un ou plusieurs).</summary>
    public List<Guid> SystemeIds { get; set; } = [];
    public int? DureeEstimeeMinutes { get; set; }
    /// <summary>Obsolète depuis le jalon 4.2 : plus de responsable prévu, conservé en base sans effet.</summary>
    public Guid? ResponsableId { get; set; }
    /// <summary>Participant qui a démarré l'étape ou qui a pris la main ; null tant qu'elle n'est pas démarrée.</summary>
    public Guid? ExecutantId { get; set; }
    public Criticite? Criticite { get; set; } = Domain.Criticite.Moderee;
    public EtatEtape Etat { get; set; } = EtatEtape.AFaire;
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public string Preuve { get; set; } = "";
    /// <summary>Commentaire facultatif de l'exécution (en plus de la preuve) ; conservé après une reprise, non copié à la duplication.</summary>
    public string Commentaire { get; set; } = "";
    public DateTimeOffset? DebutReel { get; set; }
    public DateTimeOffset? FinReelle { get; set; }
    public int Revision { get; set; }
    /// <summary>Identifiants (Id) des étapes préalables, qui doivent toutes être Réussies.</summary>
    public List<Guid> Dependances { get; set; } = [];
    /// <summary>Résolutions types applicables si l'étape échoue.</summary>
    public List<Guid> ResolutionIds { get; set; } = [];
}

/// <summary>Décision attendue d'un approbateur (compte de l'application) sur la MEP en préparation.</summary>
public class Approbation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UtilisateurId { get; set; }
    /// <summary>Nom affiché de l'approbateur au moment de sa désignation (pour les messages).</summary>
    public string Nom { get; set; } = "";
    /// <summary>null : décision en attente.</summary>
    public DecisionApprobation? Decision { get; set; }
    public string Commentaire { get; set; } = "";
    public DateTimeOffset? Le { get; set; }
}

/// <summary>Critère du catalogue global à cocher avant la décision Go (Paramètres).</summary>
public class CritereGoNoGo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Libelle { get; set; } = "";
    public int Ordre { get; set; }
    public bool Actif { get; set; } = true;
}

/// <summary>Critère coché lors de la décision Go (copie figée du libellé).</summary>
public class CritereValide
{
    public string Libelle { get; set; } = "";
    public Guid ParId { get; set; }
    public DateTimeOffset Le { get; set; }
}

/// <summary>
/// Fiche de recette en production, préparée avec la MEP et déroulée après le déploiement. Pas de responsable :
/// en recette, on contacte les responsables techniques du système de la fiche.
/// </summary>
public class FicheRecette
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Ordre { get; set; }
    /// <summary>Système de la MEP testé ; null : fiche « Sans système ».</summary>
    public Guid? SystemeId { get; set; }
    public string Titre { get; set; } = "";
    public List<EtapeTest> Etapes { get; set; } = [];
}

/// <summary>Étape d'une fiche de recette : instructions et résultat attendu (markdown), puis résultat constaté.</summary>
public class EtapeTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Ordre { get; set; }
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public ResultatControle? Resultat { get; set; }
    public string Observation { get; set; } = "";
    public Guid? ParId { get; set; }
    public DateTimeOffset? Le { get; set; }
}

/// <summary>Bilan rédigé à la clôture de la MEP.</summary>
public class Bilan
{
    public string CeQuiAMarche { get; set; } = "";
    public string Incidents { get; set; } = "";
    public string Actions { get; set; } = "";
    public Guid? RedigeParId { get; set; }
    public DateTimeOffset? Le { get; set; }
}

/// <summary>Paramètres globaux de l'application (une seule ligne, Id = 1).</summary>
public class ParametresGlobaux
{
    public const int IdUnique = 1;
    public int Id { get; set; } = IdUnique;
    /// <summary>Nombre d'approbations exigées avant le Go (0 : pas d'approbation).</summary>
    public int ApprobationsMinimum { get; set; } = 1;
}

/// <summary>
/// Fichier joint à une MEP, une étape, une résolution type ou une exécution de résolution.
/// Le contenu est stocké sur disque sous l'Id ; <see cref="NomFichier"/> n'est jamais utilisé comme chemin.
/// </summary>
public class PieceJointe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MepId { get; set; }
    public Guid? EtapeId { get; set; }
    public Guid? ResolutionTypeId { get; set; }
    public Guid? ExecutionResolutionId { get; set; }
    public string NomFichier { get; set; } = "";
    public string TypeContenu { get; set; } = "application/octet-stream";
    public long Taille { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid? DeposeParId { get; set; }
    public DateTimeOffset DeposeLe { get; set; }
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
