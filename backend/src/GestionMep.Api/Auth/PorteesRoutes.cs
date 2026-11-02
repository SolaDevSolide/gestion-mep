using GestionMep.Domain;
using Microsoft.AspNetCore.Authorization;

namespace GestionMep.Api.Auth;

/// <summary>
/// Portée exigée de chaque route appelée avec un jeton d'accès, d'après son modèle (« /api/meps/{id:guid}/etapes/… ») : une
/// seule table, vérifiée par un test sur toutes les routes déclarées. Les droits de la personne (pilote, administrateur…)
/// s'appliquent ensuite comme pour une session : une portée n'ajoute jamais de droit.
/// </summary>
public static class PorteesRoutes
{
    public const string MessageFermee = "Cette route n'est pas accessible avec un jeton d'accès.";

    /// <summary>
    /// <paramref name="Ouverte"/> : la route accepte un jeton ; <paramref name="UneParmi"/> : portées dont l'une suffit
    /// (vide : aucune portée exigée, par exemple <c>GET /api/auth/moi</c>).
    /// </summary>
    public sealed record Decision(bool Ouverte, IReadOnlyList<string> UneParmi);

    private static readonly Decision Fermee = new(false, []);
    private static readonly Decision SansPortee = new(true, []);
    private static Decision Exige(params string[] portees) => new(true, portees);

    /// <summary>
    /// Fermées aux jetons : connexion et session (aucun jeton pour ouvrir l'interface), profil (il renouvelle la session),
    /// gestion des jetons (un jeton ne crée pas d'autre jeton) et mode démonstration.
    /// </summary>
    private static readonly string[] Fermees = ["/auth", "/utilisateurs/moi", "/jetons", "/admin/jetons", "/demo"];

    /// <summary>Exécution d'une MEP : démarrer, terminer, commenter une étape, saisir la recette, dérouler les résolutions.</summary>
    private static readonly HashSet<string> Execution =
    [
        "/meps/*/etapes/*/demarrer", "/meps/*/etapes/*/terminer", "/meps/*/etapes/*/prendre-la-main", "/meps/*/etapes/*/commentaire",
        "/meps/*/etapes/*/autoriser-non-retour", "/meps/*/etapes/*/autoriser-reprise",
        "/meps/*/fiches-recette/*/etapes/*/resultat", "/meps/*/fiches-recette/*/etapes/*/refaire", "/meps/*/fiches-recette/*/refaire",
        "/meps/*/resolutions/*/corrections/*", "/meps/*/resolutions/*/verification",
    ];

    /// <summary>Preuves déposées sur une étape ou une résolution : pendant l'exécution comme en préparation.</summary>
    private static readonly HashSet<string> ExecutionOuEcriture = ["/meps/*/etapes/*/pieces-jointes", "/meps/*/resolutions/*/pieces-jointes"];

    /// <summary>Décision pour la route de la requête (null : aucune route, la réponse sera 404).</summary>
    public static Decision Requises(string methode, Endpoint? route)
    {
        if (route is not RouteEndpoint r) return SansPortee;
        var donnees = route.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var reserveAdmin = donnees.Any(d => d.Policy == AuthentificationLocale.PolitiqueAdmin);
        var anonyme = route.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        return Requises(methode, r.RoutePattern.RawText ?? "", reserveAdmin, anonyme);
    }

    /// <summary>Modèle de route ramené à sa forme sans /api, paramètres remplacés par « * » : « /meps/*/etapes/*/demarrer ».</summary>
    public static string Forme(string modele)
    {
        var segments = modele.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.StartsWith('{') ? "*" : s).ToList();
        if (segments.Count > 0 && segments[0] == "api") segments.RemoveAt(0);
        return "/" + string.Join('/', segments);
    }

    public static Decision Requises(string methode, string modele, bool reserveAdmin, bool anonyme)
    {
        var f = Forme(modele);
        var lecture = HttpMethods.IsGet(methode) || HttpMethods.IsHead(methode);
        if (f == "/auth/moi") return lecture ? SansPortee : Fermee;
        if (Fermees.Any(p => f == p || f.StartsWith(p + "/", StringComparison.Ordinal))) return Fermee;
        // Route inconnue (404), contrat OpenAPI, version de l'application, routes publiques (sondes, charte).
        if (f is "/*" or "/openapi.json" or "/application" || anonyme) return SansPortee;
        // Catalogue : sa propre portée, même pour les écritures réservées aux administrateurs (le rôle reste exigé ensuite :
        // seul un jeton de service avec admin et catalogue:ecriture modifie le catalogue).
        if (f.StartsWith("/catalogue/", StringComparison.Ordinal))
            return Exige(lecture ? PorteesJeton.CatalogueLecture : PorteesJeton.CatalogueEcriture);
        if (reserveAdmin || f == "/admin" || f.StartsWith("/admin/", StringComparison.Ordinal)) return Exige(PorteesJeton.Admin);
        if (!lecture && Execution.Contains(f)) return Exige(PorteesJeton.Execution);
        if (!lecture && ExecutionOuEcriture.Contains(f)) return Exige(PorteesJeton.Execution, PorteesJeton.MepsEcriture);
        // Présence sur une MEP ouverte : simple information pour les autres, sans écriture.
        if (f == "/meps/*/presence") return Exige(PorteesJeton.MepsLecture);
        return Exige(lecture ? PorteesJeton.MepsLecture : PorteesJeton.MepsEcriture);
    }
}
