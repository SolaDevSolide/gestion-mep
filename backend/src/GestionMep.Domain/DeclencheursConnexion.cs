using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GestionMep.Domain;

/// <summary>
/// Déclencheur d'une connexion : un événement du journal de la MEP (<paramref name="Types"/> : types du journal qui le
/// produisent). <paramref name="Frequent"/> : beaucoup d'appels (une MEP de 14 étapes en donne 28 pour « démarrée » et
/// « réussie ») ; proposé quand même, signalé dans l'éditeur.
/// </summary>
public record DeclencheurConnexion(string Cle, string Libelle, string Groupe, bool Frequent, IReadOnlyList<string> Types);

/// <summary>
/// Ce qui déclenche les connexions : les événements du journal de la MEP, regroupés par moment du cycle de vie, plus l'envoi
/// depuis une MEP. D'autres événements s'ajoutent ici sans changer le modèle (les connexions gardent des clés).
/// </summary>
public static class DeclencheursConnexion
{
    public const string GroupePreparation = "Préparation et décision";
    public const string GroupeExecution = "Exécution";
    public const string GroupeFin = "Recette, retour arrière et fin";

    /// <summary>Envoi décidé depuis la MEP (« ⋯ Plus d'actions ») : pas un déclencheur, mais une clé d'événement.</summary>
    public const string EnvoiManuel = "envoi.manuel";
    public const string LibelleEnvoiManuel = "Point d'étape";
    /// <summary>Test de recette non conforme : un « recette.test » dont le résultat est un échec.</summary>
    public const string RecetteNonConforme = "recette.non-conforme";

    public static readonly IReadOnlyList<DeclencheurConnexion> Tous =
    [
        new("gonogo.go", "Go donné", GroupePreparation, false, ["gonogo.go"]),
        new("gonogo.nogo", "No-go", GroupePreparation, false, ["gonogo.nogo"]),
        new("mep.reportee", "MEP reportée", GroupePreparation, false, ["mep.reportee"]),
        new("mep.annulee", "MEP annulée", GroupePreparation, false, ["mep.annulee"]),
        new("etape.demarree", "Étape démarrée", GroupeExecution, true, ["etape.demarree"]),
        new("etape.reussie", "Étape réussie", GroupeExecution, true, ["etape.reussie"]),
        new("etape.echec", "Étape en échec", GroupeExecution, false, ["etape.echec"]),
        new("etape.non-retour-autorise", "Point de non-retour autorisé", GroupeExecution, false, ["etape.non-retour-autorise"]),
        new("echeance.depassee", "Échéance de retour arrière dépassée", GroupeExecution, false, ["echeance.depassee"]),
        new("pilotage", "Pilotage passé ou pilotage de secours", GroupeExecution, false, ["pilotage.passe", "pilotage.secours"]),
        new("phase.recette", "Passage en recette", GroupeFin, false, ["phase.recette"]),
        new(RecetteNonConforme, "Test de recette non conforme", GroupeFin, false, []),
        new("repli.declenche", "Retour arrière déclenché", GroupeFin, false, ["repli.declenche"]),
        new("mep.cloturee", "MEP clôturée", GroupeFin, false, ["mep.cloturee"]),
    ];

    public static DeclencheurConnexion? Trouver(string cle) => Tous.FirstOrDefault(d => d.Cle == cle);

    /// <summary>
    /// Déclencheur d'un événement du journal : <paramref name="declencheur"/> s'il est donné (cas où le type ne suffit pas,
    /// comme un test de recette non conforme), sinon celui dont les types comprennent <paramref name="type"/> ; null si aucun.
    /// </summary>
    public static DeclencheurConnexion? DeEvenement(string type, string? declencheur = null) =>
        declencheur is not null ? Trouver(declencheur) : Tous.FirstOrDefault(d => d.Types.Contains(type));

    /// <summary>Libellé d'une clé d'événement (déclencheur, envoi manuel), la clé elle-même à défaut.</summary>
    public static string Libelle(string cle) => cle == EnvoiManuel ? LibelleEnvoiManuel : Trouver(cle)?.Libelle ?? cle;

    /// <summary>Événement d'étape : la criticité de l'étape filtre, et les variables d'étape sont remplies.</summary>
    public static bool EstEvenementEtape(string cle) => cle.StartsWith("etape.", StringComparison.Ordinal);
}

/// <summary>
/// Contexte de l'événement qui remplit le groupe « Événement » : clé et libellé, ligne du journal, auteur, heure, étape,
/// motif, commentaire (envoi depuis une MEP), essai et identifiant de l'appel.
/// </summary>
public record ContexteEvenement(string Cle, string Detail, string Auteur, DateTimeOffset Quand, Guid? EtapeId = null, string Motif = "",
    string Commentaire = "", bool Essai = false, Guid? AppelId = null)
{
    public string Libelle => DeclencheursConnexion.Libelle(Cle);
}

/// <summary>Variables propres aux connexions (groupe « Événement ») et valeurs complètes d'un message.</summary>
public static class VariablesEvenement
{
    public const string Groupe = "Événement";
    public const string DonneesJson = "donnees_json";

    /// <summary>Variables du groupe « Événement », proposées seulement dans l'éditeur des connexions.</summary>
    public static readonly IReadOnlyList<VariableTexte> Toutes =
    [
        new("evenement", "Événement", Groupe, "Libellé de l'événement. Ex. « Étape en échec », « Point d'étape » pour un envoi depuis la MEP."),
        new("evenement_detail", "Détail", Groupe, "La ligne du journal de la MEP. Ex. « E0005 en échec : erreur 403 au moment de la purge du cache »."),
        new("auteur", "Auteur", Groupe, "Personne qui a fait l'action (« Système » pour un événement automatique, comme l'échéance)."),
        new("quand", "Date et heure", Groupe, "Date et heure de l'événement, dans le fuseau de la MEP. Ex. « jeudi 29 octobre 2026 à 9h16 »."),
        new("quand_iso", "Date ISO 8601", Groupe, "Date et heure de l'événement pour les outils, en UTC. Ex. « 2026-10-29T08:16:24Z »."),
        new("etape", "Étape", Groupe, "Code et action de l'étape. Ex. « E0005 Publication de l'écran de paiement ». Vide hors événements d'étape."),
        new("etape_systemes", "Systèmes de l'étape", Groupe, "Systèmes de l'étape. Vide hors événements d'étape.", Liste: true),
        new("etape_criticite", "Criticité de l'étape", Groupe, "« faible », « modérée », « élevée » ou « bloquante ». Vide hors événements d'étape."),
        new("motif", "Motif", Groupe, "Motif du no-go, du report, de l'annulation, du retour arrière ou du passage de pilotage."),
        new("phase", "Phase de la MEP", Groupe, "« préparation », « exécution », « recette », « retour arrière », « clôturée » ou « annulée »."),
        new("commentaire", "Commentaire", Groupe, "Saisi lors d'un envoi depuis la MEP ; vide pour un envoi automatique."),
        new("essai", "Essai", Groupe, "« [Essai] » (suivi d'une espace) lors d'un essai depuis l'éditeur, vide sinon : à mettre en tête d'un titre."),
        new("evenement_cle", "Clé de l'événement", Groupe, "Pour les outils. Ex. « etape.echec », « gonogo.go », « envoi.manuel »."),
        new("mep_id", "Identifiant de la MEP", Groupe, "Identifiant unique de la MEP, pour les outils."),
        new("appel_id", "Identifiant de l'appel", Groupe, "Identifiant unique de l'appel (aussi dans l'en-tête X-Gestion-Mep-Appel), pour écarter les doublons."),
        new(DonneesJson, "Toutes les variables (JSON)", Groupe,
            "Objet JSON de toutes les variables, clé par clé (null quand l'information manque). Format JSON, hors guillemets.", Liste: false),
    ];

    /// <summary>Catalogue complet des connexions : le groupe « Événement » d'abord, puis celui des modèles de texte.</summary>
    public static IReadOnlyList<VariableTexte> Catalogue { get; } = [.. Toutes, .. VariablesTexte.Toutes];

    private static readonly Dictionary<string, VariableTexte> ParCle = Catalogue.ToDictionary(v => v.Cle);

    public static VariableTexte? Trouver(string cle) => ParCle.GetValueOrDefault(cle);

    public static string LibellePhase(Phase p) => p switch
    {
        Phase.Preparation => "préparation",
        Phase.Execution => "exécution",
        Phase.Recette => "recette",
        Phase.RetourArriere => "retour arrière",
        Phase.Cloturee => "clôturée",
        Phase.Annulee => "annulée",
        _ => p.ToString(),
    };

    public static string LibelleCriticite(Criticite c) => c switch
    {
        Criticite.Faible => "faible",
        Criticite.Moderee => "modérée",
        Criticite.Elevee => "élevée",
        Criticite.Bloquante => "bloquante",
        _ => c.ToString(),
    };

    private static ValeurVariable V(string? texte) => string.IsNullOrWhiteSpace(texte) ? ValeurVariable.Manquante : new(texte.Trim(), null);

    /// <summary>
    /// Valeurs de toutes les variables d'un message : celles des modèles de texte (<paramref name="texte"/>, calculées par
    /// <see cref="VariablesTexte.Valeurs"/>) et celles de l'événement, puis <c>donnees_json</c> qui les reprend toutes.
    /// </summary>
    public static Dictionary<string, ValeurVariable> Valeurs(Mep mep, ContexteEvenement ev, IReadOnlyDictionary<string, ValeurVariable> texte)
    {
        var etape = ev.EtapeId is { } id ? mep.Etapes.FirstOrDefault(e => e.Id == id) : null;
        var local = Fuseaux.Local(ev.Quand, mep.FuseauHoraire);
        var noms = mep.Systemes.ToDictionary(s => s.Id, s => s.Nom);
        var valeurs = new Dictionary<string, ValeurVariable>(texte)
        {
            ["evenement"] = V(ev.Libelle),
            ["evenement_detail"] = V(ev.Detail),
            ["auteur"] = V(ev.Auteur),
            ["quand"] = V($"{VariablesTexte.Jour(DateOnly.FromDateTime(local))} à {VariablesTexte.Heure(TimeOnly.FromDateTime(local))}"),
            ["quand_iso"] = V(ev.Quand.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            ["etape"] = V(etape is null ? null : $"{etape.Code} {etape.Action}".Trim()),
            ["etape_systemes"] = etape is null ? ValeurVariable.Manquante
                : etape.SystemeIds.Where(noms.ContainsKey).Select(s => noms[s]).ToList() is { Count: > 0 } liste ? new(null, liste) : ValeurVariable.Manquante,
            ["etape_criticite"] = V(etape?.Criticite is { } c ? LibelleCriticite(c) : null),
            ["motif"] = V(ev.Motif),
            ["phase"] = V(LibellePhase(mep.Phase)),
            ["commentaire"] = V(ev.Commentaire),
            ["essai"] = ev.Essai ? new("[Essai] ", null) : ValeurVariable.Manquante,
            ["evenement_cle"] = V(ev.Cle),
            ["mep_id"] = V(mep.Id.ToString()),
            ["appel_id"] = V(ev.AppelId?.ToString()),
        };
        valeurs[DonneesJson] = new(DonneesJsonDe(valeurs), null);
        return valeurs;
    }

    /// <summary>Objet JSON de toutes les variables (sauf lui-même), dans l'ordre du catalogue ; null quand l'information manque.</summary>
    public static string DonneesJsonDe(IReadOnlyDictionary<string, ValeurVariable> valeurs)
    {
        var objet = new JsonObject();
        foreach (var v in Catalogue.Where(v => v.Cle != DonneesJson))
        {
            var valeur = valeurs.GetValueOrDefault(v.Cle) ?? ValeurVariable.Manquante;
            objet[v.Cle] = valeur.EstManquante ? null
                : v.Liste ? new JsonArray([.. (valeur.Liste ?? (valeur.Texte is { } t ? [t] : [])).Select(x => (JsonNode?)JsonValue.Create(x))])
                : JsonValue.Create(valeur.Texte ?? string.Join(", ", valeur.Liste ?? []));
        }
        return objet.ToJsonString(OptionsJson);
    }

    private static readonly JsonSerializerOptions OptionsJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Valeurs d'exemple (une par variable du catalogue) : le message rendu avec elles sert à vérifier le JSON avant
    /// l'enregistrement, sans dépendre d'une MEP.
    /// </summary>
    public static Dictionary<string, ValeurVariable> ValeursExemple()
    {
        var valeurs = Catalogue.ToDictionary(v => v.Cle, v => v.Liste ? new ValeurVariable(null, ["exemple"]) : new ValeurVariable("exemple", null));
        valeurs[DonneesJson] = new(DonneesJsonDe(valeurs), null);
        return valeurs;
    }
}
