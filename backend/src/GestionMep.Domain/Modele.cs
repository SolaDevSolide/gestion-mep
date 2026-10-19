namespace GestionMep.Domain;

/// <summary>
/// Phase de la MEP. Stockée en entier : toute nouvelle valeur s'ajoute à la fin. <see cref="Annulee"/> : MEP arrêtée depuis la
/// préparation (motif obligatoire, voir <see cref="Mep.Annulation"/>), terminale et en lecture seule comme <see cref="Cloturee"/>.
/// </summary>
public enum Phase { Preparation, Execution, RetourArriere, Cloturee, Recette, Annulee }

/// <summary>
/// Issue d'une MEP clôturée. Stockée en entier : toute nouvelle valeur s'ajoute à la fin. <see cref="Echec"/> : ni la recette
/// ni le retour arrière n'ont rétabli le service (état laissé en production : <see cref="Mep.EtatService"/>).
/// </summary>
public enum IssueMep { Reussie, Repliee, Echec }

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

/// <summary>
/// Fin d'une étape : test unitaire (le résultat attendu est l'instruction de test, le résultat constaté se saisit en
/// terminant l'étape) ou simple vérification. Stockée en entier : 0, la valeur par défaut, est le test unitaire.
/// </summary>
public enum FinEtape { TestUnitaire, Verification }

public enum DecisionApprobation { Approuvee, Refusee }

/// <summary>
/// Type de MEP. Complète : plan détaillé (versions, suppléants choisis, durée, criticité et résolutions liées par étape).
/// Simplifiée : un pilote dont toutes les autres personnes sont suppléantes, ni versions, ni durée, ni criticité, ni
/// résolutions liées (le jour J, toutes les résolutions des systèmes de l'étape sont proposées). Stocké en entier : 0, la
/// valeur par défaut, est la MEP complète.
/// </summary>
public enum TypeMep { Complete, Simplifiee }

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
    /// <summary>Compte désactivé par un administrateur : connexion refusée et sessions coupées.</summary>
    public bool Actif { get; set; } = true;
    /// <summary>Mot de passe provisoire (création ou réinitialisation par un administrateur) : à changer avant toute autre action.</summary>
    public bool DoitChangerMotDePasse { get; set; }
    /// <summary>Renouvelé à chaque changement de mot de passe ou désactivation : les sessions ouvertes avant sont refusées.</summary>
    public Guid TamponSecurite { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Compte principal de la même personne quand ce compte lui est relié (null : compte principal ou compte seul). Les comptes
    /// reliés restent distincts (connexion, droits, historique) mais s'affichent sous le nom du compte principal.
    /// </summary>
    public Guid? ComptePrincipalId { get; set; }
}

/// <summary>Équipe gérée par les administrateurs : regroupe des comptes, sert à filtrer les personnes et les MEP.</summary>
public class Equipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Comptes membres ; un compte peut appartenir à plusieurs équipes.</summary>
    public List<Guid> MembreIds { get; set; } = [];
    public DateTimeOffset CreeeLe { get; set; }
    /// <summary>Contact du support pour les utilisateurs (variable <c>contact_support</c> des modèles de texte).</summary>
    public string ContactSupport { get; set; } = "";
    /// <summary>Signature des courriels (variable <c>signature</c> ; vide : « L'équipe {Nom} »).</summary>
    public string Signature { get; set; } = "";
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
    /// <summary>
    /// Participants suppléants du pilote : ils ont les mêmes droits que lui. MEP simplifiée : liste vide, les suppléants sont
    /// implicites (toutes les personnes sauf le pilote, voir <see cref="Regles.Suppleants"/>).
    /// </summary>
    public List<Guid> SuppleantIds { get; set; } = [];
    /// <summary>
    /// Obsolète : ancien environnement de la MEP (texte libre). Les environnements appartiennent désormais aux systèmes
    /// (<see cref="Systeme.Environnements"/>) ; la valeur est gardée en base, plus affichée ni modifiable.
    /// </summary>
    public string Environnement { get; set; } = "";
    /// <summary>Modèle de MEP : sert uniquement à être dupliqué, ne passe jamais en exécution.</summary>
    public bool EstModele { get; set; }
    /// <summary>Complète (par défaut, et pour les MEP existantes) ou simplifiée ; un modèle est toujours complet.</summary>
    public TypeMep Type { get; set; } = TypeMep.Complete;

    // Pilotage
    public Phase Phase { get; set; } = Phase.Preparation;
    /// <summary>
    /// Plan de retour arrière prévu (activé par défaut, et pour les MEP existantes). Désactivé (préproduction réinstallable…) :
    /// ni plan ni échéance réclamés pour le Go, onglet Retour arrière masqué, aucun déclenchement (ni automatique ni manuel).
    /// Les étapes et informations déjà saisies sont gardées et reviennent si on le réactive.
    /// </summary>
    public bool RetourArriereActive { get; set; } = true;
    /// <summary>
    /// Échéance de retour arrière, facultative : sans elle, ni compte à rebours ni déclenchement automatique. Atteinte pendant
    /// l'exécution, elle déclenche le retour arrière ; pendant la recette, elle est seulement signalée (voir
    /// <see cref="EcheanceSignaleeLe"/>) et le pilote décide. Se modifie par <see cref="Regles.ModifierEcheance"/> (motif, journal).
    /// </summary>
    public DateTimeOffset? EcheanceRepli { get; set; }
    /// <summary>Échéance atteinte pendant la recette : signalée (journal, bandeau) à cet instant, sans retour arrière automatique.</summary>
    public DateTimeOffset? EcheanceSignaleeLe { get; set; }
    public IssueMep? Issue { get; set; }
    public DateTimeOffset? RetourArriereDeclencheLe { get; set; }
    /// <summary>Utilisateur ayant déclenché le retour arrière ; null si déclenchement automatique.</summary>
    public Guid? RetourArriereDeclenchePar { get; set; }
    public string RetourArriereMotif { get; set; } = "";
    public DateTimeOffset? ClotureeLe { get; set; }
    public Guid? ClotureeParId { get; set; }
    /// <summary>Clôture en échec : état du service laissé en production (obligatoire), vide pour les autres issues.</summary>
    public string EtatService { get; set; } = "";
    /// <summary>Clôture en échec : référence de l'incident ouvert (facultative).</summary>
    public string ReferenceIncident { get; set; } = "";
    /// <summary>MEP annulée (<see cref="Phase.Annulee"/>) : par qui, quand, pourquoi et état laissé en production.</summary>
    public Annulation? Annulation { get; set; }
    /// <summary>
    /// Report d'une MEP en préparation (motif, date visée facultative) : la phase reste <see cref="Phase.Preparation"/>, le plan
    /// reste modifiable. Levé par « Reprendre la préparation » ou par le Go.
    /// </summary>
    public Report? Report { get; set; }
    /// <summary>
    /// Pilotage de secours par un administrateur après le Go (pilote et suppléants injoignables) : affiché à tous jusqu'à la
    /// clôture. Effacé si le pilotage est ensuite passé normalement.
    /// </summary>
    public PilotageSecours? PilotageSecours { get; set; }

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

    // Plan de retour arrière : déclenchement et données
    public PlanRepli PlanRepli { get; set; } = new();

    // 4. Sauvegarde
    public Sauvegarde Sauvegarde { get; set; } = new();

    // Communication : informations facultatives reprises par les modèles de texte (annonce, rappel, demande de MEP…).
    public Communication Communication { get; set; } = new();

    // 5. Validation avant intervention
    public Guid? ProcedureRelueParId { get; set; }
    public DateTimeOffset? ProcedureRelueLe { get; set; }
    public Guid? DemarrageAutoriseParId { get; set; }
    /// <summary>Dernier Go (un retour en préparation suivi d'un nouveau Go le remplace).</summary>
    public DateTimeOffset? DemarrageAutoriseLe { get; set; }
    /// <summary>Premier Go de la MEP, gardé après un retour en préparation (null : jamais lancée).</summary>
    public DateTimeOffset? PremierGoLe { get; set; }

    /// <summary>
    /// Résolutions types liées aux étapes, copiées (titre, contenu, version) à la première approbation, à défaut au Go : le
    /// déroulé applique ces copies, une modification de la bibliothèque ne change plus la MEP approuvée. Effacées quand les
    /// approbations sont remises à zéro (<see cref="Regles.ReinitialiserApprobations"/>). MEP simplifiée : aucune (les
    /// résolutions des systèmes sont lues dans la bibliothèque le jour J).
    /// </summary>
    public List<ResolutionFigee> ResolutionsFigees { get; set; } = [];
    /// <summary>Moment où les résolutions ont été figées (null : pas encore figées).</summary>
    public DateTimeOffset? ResolutionsFigeesLe { get; set; }

    // Compteurs tenus à jour par les règles quand l'événement survient (jamais recalculés depuis le texte du journal).
    /// <summary>Échecs d'étape survenus (une étape reprise puis réussie garde son échec).</summary>
    public int EchecsEtapes { get; set; }
    /// <summary>Résultats de recette en échec survenus (saisis dans l'application ou importés d'Excel), même refaits ensuite.</summary>
    public int EchecsRecette { get; set; }

    public DateTimeOffset CreeLe { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreeParUtilisateurId { get; set; }

    /// <summary>Compteur de révision pour le verrou optimiste.</summary>
    public int Version { get; set; }

    public List<Participant> Participants { get; set; } = [];
    public List<Systeme> Systemes { get; set; } = [];
    /// <summary>Objets impactés, par système et par catégorie, choisis dans la fiche du système au catalogue.</summary>
    public List<ObjetImpacte> ObjetsImpactes { get; set; } = [];
    public List<Etape> Etapes { get; set; } = [];
    public List<ExecutionResolution> Resolutions { get; set; } = [];

    /// <summary>
    /// Éléments des bibliothèques (étapes modèles, modèles de recette, résolutions types) déjà comptés comme utilisés par
    /// cette MEP : le compteur d'utilisations d'un élément n'augmente qu'une fois par MEP (voir <see cref="Regles.NouveauxElementsBibliotheque"/>).
    /// </summary>
    public List<Guid> ElementsBibliothequeComptes { get; set; } = [];

    /// <summary>
    /// Sections du plan (repères qui regroupent des étapes, avec leurs cercles d'entrée et de sortie) ; elles n'ont aucun effet
    /// sur l'exécution, qui ne lit que <see cref="Etape.Dependances"/>.
    /// </summary>
    public List<Section> Sections { get; set; } = [];

    /// <summary>Sections d'un plan, par ordre de création.</summary>
    public IEnumerable<Section> SectionsDu(TypePlan plan) => Sections.Where(s => s.Plan == plan).OrderBy(s => s.CreeLe).ThenBy(s => s.Titre, StringComparer.Ordinal);

    /// <summary>Étapes d'un plan, de haut en bas puis de gauche à droite (ligne, puis ordre dans la ligne).</summary>
    public IEnumerable<Etape> EtapesDu(TypePlan plan) => Etapes.Where(e => e.Plan == plan).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre);

    /// <summary>Toutes les étapes, triées par plan, ligne et ordre.</summary>
    public IEnumerable<Etape> EtapesTriees() => Etapes.OrderBy(e => e.Plan).ThenBy(e => e.Ligne).ThenBy(e => e.Ordre);
}

/// <summary>Annulation d'une MEP (depuis la préparation).</summary>
public class Annulation
{
    public string Motif { get; set; } = "";
    /// <summary>État laissé en production : obligatoire si la MEP a déjà eu un Go (vide sinon).</summary>
    public string EtatProduction { get; set; } = "";
    public Guid? ParId { get; set; }
    public DateTimeOffset Le { get; set; }
}

/// <summary>Report d'une MEP en préparation : motif et date visée facultative (indicative, le Go reste la décision du pilote).</summary>
public class Report
{
    public string Motif { get; set; } = "";
    public DateOnly? DateVisee { get; set; }
    public Guid? ParId { get; set; }
    public DateTimeOffset Le { get; set; }
}

/// <summary>Pilotage repris par un administrateur après le Go : qui, quand, pourquoi, et les participants pilotes avant et après.</summary>
public class PilotageSecours
{
    public Guid? ParId { get; set; }
    public DateTimeOffset Le { get; set; }
    public string Motif { get; set; } = "";
    public Guid? AncienPiloteId { get; set; }
    public Guid? NouveauPiloteId { get; set; }
}

public class PlanRepli
{
    public string QuandDeclencher { get; set; } = "";
    public int? DureeTesteeMinutes { get; set; }
    public string VersionARestaurer { get; set; } = "";
    public string DonneesDepuisSauvegarde { get; set; } = "";
    public string SiRepliEchoue { get; set; } = "";
}

/// <summary>Niveau de risque retenu par l'analyse d'impact (section Communication). Stocké en entier : toute nouvelle valeur s'ajoute à la fin.</summary>
public enum NiveauRisque { NonEvalue, Faible, Modere, Eleve }

/// <summary>
/// Informations de communication d'une MEP, toutes facultatives : elles remplissent les textes à copier (annonce aux
/// utilisateurs, rappel, demande de MEP, compte rendu). Modifiables par les éditeurs en préparation, puis par le pilote,
/// ses suppléants ou un administrateur jusqu'à la clôture.
/// </summary>
public class Communication
{
    /// <summary>Numéro du ticket ou de la demande de changement.</summary>
    public string ReferenceDemande { get; set; } = "";
    public NiveauRisque NiveauRisque { get; set; } = NiveauRisque.NonEvalue;
    /// <summary>Bénéfices et nouveautés, une par ligne (liste à puces dans les textes).</summary>
    public string Benefices { get; set; } = "";
    public string UtilisateursImpactes { get; set; } = "";
    /// <summary>Les utilisateurs ne peuvent pas travailler pendant l'intervention ; la durée est l'indisponibilité prévue.</summary>
    public bool ArretApplicatif { get; set; }
    /// <summary>Action demandée aux utilisateurs (« quitter l'application ») et heure limite, le jour de la MEP.</summary>
    public string ActionUtilisateurs { get; set; } = "";
    public TimeOnly? HeureLimite { get; set; }
    /// <summary>Créneau annoncé aux utilisateurs (les heures réelles restent celles du Go et de la clôture).</summary>
    public DateOnly? DateAnnoncee { get; set; }
    public TimeOnly? HeureDebutAnnoncee { get; set; }
    public TimeOnly? HeureFinAnnoncee { get; set; }
    /// <summary>Validation de la nouvelle version par les utilisateurs : par qui, quand, et la pièce jointe de la MEP qui la prouve.</summary>
    public bool ValideeParUtilisateurs { get; set; }
    public string ValideePar { get; set; } = "";
    public DateOnly? ValideeLe { get; set; }
    public Guid? PreuveValidationId { get; set; }
}

/// <summary>Usage d'un modèle de texte. Stocké en entier : toute nouvelle valeur s'ajoute à la fin.</summary>
public enum UsageModeleTexte { Annonce, Rappel, DemandeMep, CompteRendu, Autre }

/// <summary>
/// Modèle de texte d'une équipe (courriel type) : objet et texte avec variables <c>{{variable}}</c>, <c>**gras**</c> et puces
/// « - ». Les modèles fournis (<see cref="CleFournie"/> renseignée) sont copiés dans chaque équipe : modifiables et
/// rétablissables, jamais supprimés.
/// </summary>
public class ModeleTexte
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipeId { get; set; }
    public string Nom { get; set; } = "";
    public UsageModeleTexte Usage { get; set; } = UsageModeleTexte.Autre;
    public string Objet { get; set; } = "";
    public string Corps { get; set; } = "";
    /// <summary>Modèle fourni dont celui-ci est la copie (<see cref="ModelesTexteFournis"/>) ; null : modèle créé par l'équipe.</summary>
    public string? CleFournie { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset ModifieLe { get; set; }
    /// <summary>Compte auteur de la dernière modification (null : copie automatique d'un modèle fourni, ou ligne de commande).</summary>
    public Guid? ModifieParId { get; set; }
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
    /// <summary>
    /// Ajouté d'office : administrateur devenu personne de la MEP en démarrant une étape ou en prenant la main. Il n'est
    /// qu'exécutant : jamais suppléant implicite d'une MEP simplifiée (voir <see cref="Regles.Suppleants"/>), sauf s'il est
    /// ensuite désigné pilote.
    /// </summary>
    public bool AjouteDOffice { get; set; }
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
    /// <summary>
    /// Environnements touchés par la MEP sur ce système (un ou plusieurs), copiés de la fiche du catalogue au moment du choix.
    /// </summary>
    public List<EnvironnementSysteme> Environnements { get; set; } = [];
}

/// <summary>Environnement d'un système de la MEP : nom et marque « production » (copiés du catalogue).</summary>
public class EnvironnementSysteme
{
    public string Nom { get; set; } = "";
    public bool Production { get; set; }
}

/// <summary>Environnement d'une étape sur l'un de ses systèmes (« BDD : Préproduction »).</summary>
public class EnvironnementEtape
{
    public Guid SystemeId { get; set; }
    public string Nom { get; set; } = "";
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
    /// <summary>Où se trouve la sauvegarde (chemin, serveur, coffre…) : obligatoire si requise.</summary>
    public string Emplacement { get; set; } = "";
    /// <summary>Comment récupérer (restaurer) la sauvegarde : obligatoire si requise.</summary>
    public string MethodeRecuperation { get; set; } = "";
}

/// <summary>Système du catalogue global, saisi une fois et sélectionné ensuite dans chaque MEP.</summary>
public class SystemeCatalogue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    public string Cible { get; set; } = "";
    public bool Actif { get; set; } = true;
    /// <summary>Environnements du système (développement, recette, production…), dans l'ordre d'affichage.</summary>
    public List<EnvironnementCatalogue> Environnements { get; set; } = [];
    /// <summary>Catégories d'objets qui s'appliquent au système, dans l'ordre d'affichage (une catégorie peut être vide).</summary>
    public List<string> Categories { get; set; } = [];
    /// <summary>Objets connus du système, chacun dans l'une de ses <see cref="Categories"/> : seuls choix possibles dans une MEP.</summary>
    public List<ObjetCatalogue> Objets { get; set; } = [];
    /// <summary>Compteur de révision : protège la fiche contre deux écritures simultanées.</summary>
    public int Version { get; set; }
}

/// <summary>Environnement d'un système du catalogue : nom, cible exacte et marque « production ».</summary>
public class EnvironnementCatalogue
{
    public string Nom { get; set; } = "";
    public string Cible { get; set; } = "";
    /// <summary>Environnement de production : une MEP qui le touche est signalée dans la liste des MEP.</summary>
    public bool Production { get; set; }
}

/// <summary>Objet connu d'un système du catalogue (table, programme, flux…), rangé dans une catégorie.</summary>
public class ObjetCatalogue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Categorie { get; set; } = "";
    public string Type { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Libelle { get; set; } = "";
    /// <summary>Précision libre (schéma, planification, langage…).</summary>
    public string Precision { get; set; } = "";
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
    /// <summary>Création de la fiche : départage le tri des bibliothèques à nombre d'utilisations égal.</summary>
    public DateTimeOffset CreeeLe { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Nombre de MEP où la résolution a été liée à une étape (une fois par MEP, jamais décompté).</summary>
    public int Utilisations { get; set; }
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

/// <summary>Copie d'une résolution type figée dans une MEP (voir <see cref="Mep.ResolutionsFigees"/>).</summary>
public class ResolutionFigee
{
    public Guid ResolutionTypeId { get; set; }
    public int Version { get; set; }
    public string Titre { get; set; } = "";
    public string Symptomes { get; set; } = "";
    public string Diagnostic { get; set; } = "";
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    public List<Correction> Corrections { get; set; } = [];
    public string VerificationInstruction { get; set; } = "";
    public string VerificationAttendu { get; set; } = "";

    public static ResolutionFigee De(ResolutionType r) => new()
    {
        ResolutionTypeId = r.Id, Version = r.Version, Titre = r.Titre, Symptomes = r.Symptomes, Diagnostic = r.Diagnostic,
        SystemeCatalogueIds = [.. r.SystemeCatalogueIds],
        Corrections = r.Corrections.Select(c => new Correction { Ordre = c.Ordre, Instruction = c.Instruction, ResultatAttendu = c.ResultatAttendu }).ToList(),
        VerificationInstruction = r.VerificationInstruction, VerificationAttendu = r.VerificationAttendu,
    };

    /// <summary>La copie vue comme une résolution type (même identifiant et version), pour la contrôler ou l'appliquer.</summary>
    public ResolutionType VersResolutionType(bool actif = true) => new()
    {
        Id = ResolutionTypeId, Version = Version, Titre = Titre, Symptomes = Symptomes, Diagnostic = Diagnostic,
        SystemeCatalogueIds = [.. SystemeCatalogueIds], Corrections = [.. Corrections], VerificationInstruction = VerificationInstruction,
        VerificationAttendu = VerificationAttendu, Actif = actif,
    };
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

/// <summary>
/// Objet impacté par la MEP sur l'un de ses systèmes : copie de l'objet du catalogue au moment du choix (catégorie, type,
/// nom, libellé, précision), plus la version ou la correction livrée.
/// </summary>
public class ObjetImpacte
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MepId { get; set; }
    /// <summary>Système de la MEP ; null seulement pour un ancien livrable repris sans système.</summary>
    public Guid? SystemeId { get; set; }
    /// <summary>Objet de la fiche du système au catalogue ; null pour un ancien livrable d'un système hors catalogue.</summary>
    public Guid? ObjetCatalogueId { get; set; }
    public string Categorie { get; set; } = "";
    public string Type { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Precision { get; set; } = "";
    /// <summary>Version ou correction livrée.</summary>
    public string Version { get; set; } = "";
    /// <summary>Ancien livrable repris par la migration (catégorie « Packages »).</summary>
    public bool Repris { get; set; }
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
    /// <summary>
    /// Profondeur dans le graphe (0 en haut), propre à chaque plan, sans trou ; recalculée après chaque opération sur le graphe
    /// (<see cref="Regles.CalculerLignes"/>), jamais saisie. Sert aux exports, à la ligne de commande et d'indice d'ordre pour le
    /// rangement. -1 : à calculer (données d'avant le jalon 4).
    /// </summary>
    public int Ligne { get; set; }
    /// <summary>Position dans la ligne (0, 1, 2… de gauche à droite), recalculée avec <see cref="Ligne"/>.</summary>
    public int Ordre { get; set; }
    /// <summary>
    /// Ordre gauche-droite choisi dans le graphe, commun aux étapes et aux sections d'un plan (<see cref="Regles.Ordonner"/>) :
    /// le rangement l'impose aux éléments d'un même niveau. 0 : jamais réordonné (l'ordre suit alors Ligne, Ordre, Code).
    /// </summary>
    public int Rang { get; set; }
    /// <summary>
    /// Étape retirée du graphe, en attente dans le bac « Étapes à placer » (<see cref="Regles.RetirerDuPlan"/>) : elle n'a aucun
    /// lien tant qu'elle y reste et bloque le Go (« Vérifier le plan »). Explicite plutôt que déduit de l'absence de liens : une
    /// étape seule dans son plan, ou posée à part, reste dans le graphe.
    /// </summary>
    public bool APlacer { get; set; }
    /// <summary>Étape corrective ajoutée pendant l'exécution, après le Go (signalée dans le plan et dans le rapport de fin).</summary>
    public bool AjouteeApresGo { get; set; }
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
    /// <summary>Test unitaire ou simple vérification : décide si un résultat constaté est exigé en terminant l'étape.</summary>
    public FinEtape Fin { get; set; } = FinEtape.TestUnitaire;
    /// <summary>Résultat constaté du test unitaire, saisi en terminant l'étape (vide pour une simple vérification).</summary>
    public string ResultatConstate { get; set; } = "";
    /// <summary>Étape de type Sauvegarde : où se trouve la sauvegarde réalisée (obligatoire).</summary>
    public string SauvegardeEmplacement { get; set; } = "";
    /// <summary>Étape de type Sauvegarde : comment récupérer la sauvegarde (obligatoire).</summary>
    public string SauvegardeMethodeRecuperation { get; set; } = "";
    public string Preuve { get; set; } = "";
    /// <summary>Commentaire facultatif de l'exécution (en plus de la preuve) ; conservé après une reprise, non copié à la duplication.</summary>
    public string Commentaire { get; set; } = "";
    public DateTimeOffset? DebutReel { get; set; }
    public DateTimeOffset? FinReelle { get; set; }
    public int Revision { get; set; }
    /// <summary>
    /// Identifiants (Id) des étapes préalables, qui doivent toutes être Réussies : ensemble EFFECTIF, seul utilisé par
    /// l'exécution. Il réunit les préalables directs et ceux qui viennent des cercles de section (<see cref="DependancesDerivees"/>).
    /// </summary>
    public List<Guid> Dependances { get; set; } = [];
    /// <summary>
    /// Part de <see cref="Dependances"/> qui vient des cercles de section (jamais un préalable direct), recalculée par
    /// <see cref="Regles.CompilerSections"/> après chaque changement.
    /// </summary>
    public List<Guid> DependancesDerivees { get; set; } = [];
    /// <summary>Section de l'étape (une au plus, dans le même plan) ; null : étape hors section.</summary>
    public Guid? SectionId { get; set; }
    /// <summary>Cercles de sortie d'autres sections que l'étape attend (elle est hors de leur section).</summary>
    public List<Guid> PortsAttendus { get; set; } = [];
    /// <summary>Résolutions types applicables si l'étape échoue.</summary>
    public List<Guid> ResolutionIds { get; set; } = [];
    /// <summary>Étape modèle dont l'étape est une copie (null : étape saisie dans la MEP). La copie ne suit pas le modèle.</summary>
    public Guid? ModeleEtapeId { get; set; }
    /// <summary>
    /// Environnements où agit l'étape, système par système (plusieurs possibles), parmi ceux choisis pour la MEP sur chaque
    /// système. Aucun contrôle de cohérence entre systèmes : c'est à la personne qui relit de repérer une incohérence.
    /// </summary>
    public List<EnvironnementEtape> Environnements { get; set; } = [];
}

/// <summary>Sens d'un cercle de section : entrée (bord haut, ce que la section attend) ou sortie (bord bas, ce qu'elle donne).</summary>
public enum SensPort { Entree, Sortie }

/// <summary>
/// Section d'un plan (#60) : un repère qui regroupe des étapes, avec ses systèmes et ses cercles nommés. Ce n'est pas une
/// étape : elle ne s'exécute pas, et ses cercles ne servent qu'à calculer les préalables effectifs des étapes
/// (<see cref="Regles.CompilerSections"/>). Une section non placée (<see cref="Placee"/> faux) attend dans le bac.
/// </summary>
public class Section
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TypePlan Plan { get; set; } = TypePlan.Deploiement;
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Systèmes de la MEP touchés par la section (indicatif, modifiables à tout moment).</summary>
    public List<Guid> SystemeIds { get; set; } = [];
    /// <summary>Compte qui a créé la section.</summary>
    public Guid? AuteurId { get; set; }
    public DateTimeOffset CreeLe { get; set; }
    /// <summary>Faux : la section attend dans le bac (« Vérifier le plan » bloque le Go tant qu'elle y reste).</summary>
    public bool Placee { get; set; } = true;
    /// <summary>Augmente à chaque modification de la section ou de ses cercles (contrôle de concurrence de la fiche).</summary>
    public int Revision { get; set; }
    /// <summary>Ordre gauche-droite dans le graphe, partagé avec les étapes du plan (<see cref="Etape.Rang"/>).</summary>
    public int Rang { get; set; }
    /// <summary>Cercles d'entrée et de sortie.</summary>
    public List<PortSection> Ports { get; set; } = [];
}

/// <summary>
/// Cercle d'une section. Entrée : <see cref="Etapes"/> sont les étapes de la section qui l'attendent, <see cref="Sources"/> ce qui
/// y arrive de l'extérieur (étapes hors de la section ou cercles de sortie d'autres sections). Sortie : <see cref="Etapes"/> sont
/// les étapes de la section qu'il attend ; les étapes qui l'attendent le portent dans <see cref="Etape.PortsAttendus"/>, les
/// cercles d'entrée d'autres sections dans leurs <see cref="Sources"/>.
/// </summary>
public class PortSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SensPort Sens { get; set; }
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Position sur son bord (0, 1, 2… de gauche à droite), parmi les cercles du même sens.</summary>
    public int Ordre { get; set; }
    public List<Guid> Etapes { get; set; } = [];
    /// <summary>Entrée seulement : Id d'étapes hors de la section ou de cercles de sortie d'autres sections.</summary>
    public List<Guid> Sources { get; set; } = [];
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
/// Fiche de recette en production, préparée avec la MEP et déroulée après le déploiement. Elle couvre une partie d'un
/// seul système de la MEP. Pas de responsable : en recette, on contacte les responsables techniques du système de la fiche.
/// </summary>
public class FicheRecette
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Ordre { get; set; }
    /// <summary>
    /// Système de la MEP testé. null seulement pour les fiches d'avant la bibliothèque de recettes, ou dont le système a
    /// été retiré de la MEP : elles restent modifiables et peuvent être rattachées à un système.
    /// </summary>
    public Guid? SystemeId { get; set; }
    public string Titre { get; set; } = "";
    public List<EtapeTest> Etapes { get; set; } = [];
    /// <summary>Modèle de la bibliothèque de recettes dont la fiche est une copie (null : fiche saisie dans la MEP).</summary>
    public Guid? ModeleRecetteId { get; set; }
}

/// <summary>
/// Étape d'une fiche de recette : déroulé du test en blocs Texte / Code (markdown) et résultat attendu (texte simple ;
/// les recettes plus anciennes peuvent y contenir du markdown, toujours affiché), puis résultat constaté.
/// </summary>
public class EtapeTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Ordre { get; set; }
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public ResultatControle? Resultat { get; set; }
    public string Observation { get; set; } = "";
    /// <summary>Compte qui a saisi le résultat (ou importé le fichier Excel qui le contenait).</summary>
    public Guid? ParId { get; set; }
    public DateTimeOffset? Le { get; set; }
    /// <summary>Validation reportée d'un fichier Excel : nom saisi par la personne du métier qui a validé (« » sinon).</summary>
    public string ValideePar { get; set; } = "";
    /// <summary>Validation reportée d'un fichier Excel : date indiquée dans le fichier, si elle l'est.</summary>
    public DateOnly? ValideeLe { get; set; }
}

/// <summary>
/// Modèle de la bibliothèque de recettes : une fiche de recette type, rattachée à un système du catalogue, que l'on
/// insère (par copie) dans les MEP.
/// </summary>
public class ModeleRecette
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titre { get; set; } = "";
    /// <summary>Système du catalogue testé par la fiche.</summary>
    public Guid SystemeCatalogueId { get; set; }
    public List<EtapeModeleRecette> Etapes { get; set; } = [];
    public int Version { get; set; }
    /// <summary>Création du modèle : départage le tri de la bibliothèque à nombre d'utilisations égal.</summary>
    public DateTimeOffset CreeLe { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Nombre de MEP où le modèle a été inséré (une fois par MEP, jamais décompté).</summary>
    public int Utilisations { get; set; }
    public DateTimeOffset ModifieLe { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Compte auteur de la dernière modification (null : données de démo ou ligne de commande).</summary>
    public Guid? ModifieParId { get; set; }
}

/// <summary>
/// Étape modèle de la bibliothèque d'étapes : la fiche d'une étape type (action, systèmes du catalogue, type, instructions,
/// fin, champs de sauvegarde, résolutions), insérée par copie dans le plan d'une MEP. Jamais de point de non-retour, de liens,
/// d'exécutant ni de pièces jointes : ils sont propres à une MEP.
/// </summary>
public class ModeleEtape
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Nom de l'étape modèle : l'action de l'étape insérée.</summary>
    public string Action { get; set; } = "";
    /// <summary>Systèmes du catalogue touchés par l'étape (un ou plusieurs) : elle figure dans la bibliothèque de chacun.</summary>
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    public TypeEtape Type { get; set; } = TypeEtape.Standard;
    public Criticite? Criticite { get; set; } = Domain.Criticite.Moderee;
    public int? DureeEstimeeMinutes { get; set; }
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
    public FinEtape Fin { get; set; } = FinEtape.TestUnitaire;
    /// <summary>Type Sauvegarde : où se trouve la sauvegarde, et comment la récupérer (repris dans la copie).</summary>
    public string SauvegardeEmplacement { get; set; } = "";
    public string SauvegardeMethodeRecuperation { get; set; } = "";
    /// <summary>Résolutions types applicables si l'étape échoue.</summary>
    public List<Guid> ResolutionIds { get; set; } = [];
    /// <summary>Nombre de MEP où l'étape modèle a été insérée (une fois par MEP, jamais décompté).</summary>
    public int Utilisations { get; set; }
    public DateTimeOffset CreeLe { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public DateTimeOffset ModifieLe { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Compte auteur de la dernière modification (null : données de démo ou ligne de commande).</summary>
    public Guid? ModifieParId { get; set; }
}

/// <summary>Étape d'un modèle de recette : déroulé en blocs Texte / Code (markdown) et résultat attendu.</summary>
public class EtapeModeleRecette
{
    public int Ordre { get; set; }
    public string Instructions { get; set; } = "";
    public string ResultatAttendu { get; set; } = "";
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
    /// <summary>Catégories d'objets proposées par défaut à chaque système du catalogue (gérées par les administrateurs).</summary>
    public List<string> CategoriesObjets { get; set; } = [.. CategoriesObjetsParDefaut];

    /// <summary>Liste initiale des catégories d'objets par défaut.</summary>
    public static readonly IReadOnlyList<string> CategoriesObjetsParDefaut =
    [
        "Code source", "Packages", "Flux", "Services", "Base de données", "Tables", "Colonnes", "Vues", "Procédures stockées",
        "Fonctions", "Triggers", "Index", "Référentiels", "Interfaces", "Configuration", "Infrastructure", "Sécurité", "API",
        "Fichiers d'échange", "Ordonnancement", "Monitoring", "Sauvegarde / rollback", "Documentation technique et fonctionnelle",
        "Tests", "Dépendances", "Certificats", "Réseau", "Exploitation", "Traçabilité",
    ];
}

/// <summary>
/// Réglage d'exploitation modifié depuis la console ou la ligne de commande (une ligne par réglage modifié). Sa valeur
/// remplace celle du fichier .env ; supprimer la ligne revient à la valeur du fichier.
/// </summary>
public class Reglage
{
    /// <summary>Clé de configuration, par exemple <c>Application:UrlPublique</c>.</summary>
    public string Cle { get; set; } = "";
    public string Valeur { get; set; } = "";
    public DateTimeOffset ModifieLe { get; set; }
    public string ModifiePar { get; set; } = "";
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

/// <summary>Journal d'administration : comptes, paramètres globaux, critères go / no-go (hors MEP).</summary>
public class EvenementAdmin
{
    public long Id { get; set; }
    public DateTimeOffset Quand { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Auteur connecté ; null pour une commande d'exploitation (ligne de commande).</summary>
    public Guid? UtilisateurId { get; set; }
    public string Auteur { get; set; } = "";
    public string Type { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class EvenementJournal
{
    public long Id { get; set; }
    public Guid MepId { get; set; }
    /// <summary>Étape concernée par un événement d'exécution (démarrage, réussite, échec…), null sinon : rien ne se relit dans <see cref="Detail"/>.</summary>
    public Guid? EtapeId { get; set; }
    public DateTimeOffset Quand { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UtilisateurId { get; set; }
    public string Auteur { get; set; } = "";
    public string Type { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>
/// Rapport de fin d'une MEP (#61) : instantané figé à la clôture (la MEP telle qu'elle s'affichait, son journal complet et la
/// synthèse calculée), stocké en JSON et relu tel quel. Une MEP clôturée avant l'existence des rapports obtient le sien à la
/// première demande, figé à son tour. Supprimé avec la MEP.
/// </summary>
public class RapportFin
{
    public Guid MepId { get; set; }
    public DateTimeOffset FigeLe { get; set; }
    /// <summary>Calculé à la première demande (MEP clôturée avant les rapports de fin) plutôt qu'au moment de la clôture.</summary>
    public bool CalculeApres { get; set; }
    /// <summary>Version du format de <see cref="Contenu"/>, pour relire les anciens rapports si ce format change.</summary>
    public int Format { get; set; }
    /// <summary>Contenu figé (JSON produit et relu par l'API).</summary>
    public string Contenu { get; set; } = "";
}

/// <summary>Fournisseur de la connexion unique : Microsoft Entra ID (préréglage) ou tout autre fournisseur OpenID Connect.</summary>
public enum FournisseurSso { Microsoft, OpenIdConnect }

/// <summary>
/// Connexion unique (SSO) facultative, ligne unique réglée depuis la console d'administration ou la ligne de commande.
/// Désactivée, seuls les comptes locaux se connectent ; activée, le mot de passe local reste possible.
/// </summary>
public class ConfigurationSso
{
    public const int IdUnique = 1;
    public int Id { get; set; } = IdUnique;
    public bool Active { get; set; }
    public FournisseurSso Fournisseur { get; set; } = FournisseurSso.Microsoft;
    /// <summary>Microsoft : identifiant de l'annuaire (locataire), GUID ou domaine. Jamais « common » : seul l'annuaire de l'entreprise est accepté.</summary>
    public string Annuaire { get; set; } = "";
    /// <summary>Autre fournisseur : adresse de l'émetteur OpenID Connect (https).</summary>
    public string Autorite { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Secret client chiffré avec les clés de l'application (dossier Cles:Dossier) ; jamais renvoyé par l'API.</summary>
    public string SecretChiffre { get; set; } = "";
    /// <summary>Date d'expiration du secret indiquée par l'informatique : la console prévient 30 jours avant.</summary>
    public DateOnly? SecretExpireLe { get; set; }
    /// <summary>Crée le compte à la première connexion ; sinon seuls les comptes déjà liés entrent.</summary>
    public bool CreationAutomatique { get; set; } = true;
    /// <summary>Rôle d'application (claim « roles ») qui donne le rôle administrateur ; vide : rôle géré dans l'application.</summary>
    public string RoleAdmin { get; set; } = "";
    /// <summary>Texte du bouton de la page de connexion (vide : texte par défaut).</summary>
    public string Libelle { get; set; } = "";
    public DateTimeOffset? ModifieeLe { get; set; }
}

/// <summary>
/// Compte SSO relié à un compte de l'application, par l'identifiant immuable du fournisseur (émetteur + oid/sub),
/// jamais par l'adresse mail.
/// </summary>
public class IdentiteExterne
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UtilisateurId { get; set; }
    public Utilisateur? Utilisateur { get; set; }
    public string Emetteur { get; set; } = "";
    public string Sujet { get; set; } = "";
    /// <summary>Identifiant lisible chez le fournisseur (UPN, mail), pour l'affichage seulement.</summary>
    public string Identifiant { get; set; } = "";
    public DateTimeOffset CreeeLe { get; set; }
    public DateTimeOffset? DerniereConnexion { get; set; }
}
