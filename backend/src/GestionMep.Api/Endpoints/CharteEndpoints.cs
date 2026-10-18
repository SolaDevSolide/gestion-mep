using System.Reflection;
using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using Microsoft.Net.Http.Headers;

namespace GestionMep.Api.Endpoints;

/// <summary>Version de l'interface servie et empreinte de la charte visuelle en vigueur (/api/version).</summary>
public record VersionDto(string Version, string Charte);

/// <summary>
/// Charte visuelle : lecture publique (la page de connexion est déjà aux bonnes couleurs), schéma du fichier, et réglage
/// réservé aux administrateurs (mêmes règles que <c>gestion-mep charte</c>). La feuille <c>/charte.css</c> est servie hors
/// de /api, chargée par index.html avant l'application.
/// </summary>
public static class CharteEndpoints
{
    /// <summary>Schéma JSON du fichier charte.json (ressource incorporée).</summary>
    public static readonly string Schema = LireSchema();

    private static string LireSchema()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("charte.schema.json")
            ?? throw new InvalidOperationException("Ressource charte.schema.json absente.");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    public static void MapCharte(this RouteGroupBuilder api)
    {
        api.MapGet("/charte", (ServiceCharte charte) => ServiceCharte.Public(charte.Courant)).AllowAnonymous();
        api.MapGet("/charte/schema.json", () => Results.Text(Schema, "application/schema+json; charset=utf-8")).AllowAnonymous();

        var admin = api.MapGroup("/admin/charte").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapGet("", async (ServiceCharte charte) =>
        {
            await charte.RechargerAsync();
            return ServiceCharte.Dto(charte.Courant);
        });

        // Après l'enregistrement, la charte est relue tout de suite : /charte.css et /api/version changent dès la requête suivante.
        admin.MapPut("", async (CharteFichier saisie, ClaimsPrincipal user, MepDbContext db, IConfiguration config, ServiceCharte charte,
            TimeProvider temps) =>
        {
            var nouvelle = ServiceCharte.Valider(CharteVisuelle.Lire(saisie));
            await ServiceCharte.Enregistrer(db, config, nouvelle, Acteur.Depuis(user), temps.GetUtcNow());
            await charte.RechargerAsync();
            return ServiceCharte.Dto(charte.Courant);
        });

        admin.MapDelete("", async (ClaimsPrincipal user, MepDbContext db, IConfiguration config, ServiceCharte charte) =>
        {
            await ServiceCharte.Retablir(db, config, Acteur.Depuis(user));
            await charte.RechargerAsync();
            return ServiceCharte.Dto(charte.Courant);
        });

        admin.MapPost("/verifier", (VerificationCharteSaisie saisie) => ServiceCharte.Verifier(saisie.Contenu));
    }

    /// <summary>
    /// <c>/charte.css</c> : variables CSS de la charte en vigueur, publique. Revalidée à chaque chargement (ETag = empreinte),
    /// donc jamais périmée et presque gratuite quand elle n'a pas changé.
    /// </summary>
    public static void MapFeuilleCharte(this IEndpointRouteBuilder app) =>
        app.MapGet("/charte.css", (HttpContext ctx, ServiceCharte charte) =>
        {
            var etat = charte.Courant;
            var etag = new EntityTagHeaderValue($"\"{etat.Empreinte}\"");
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.ETag = etag.ToString();
            if (ctx.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } demandes && demandes.Any(d => d.Compare(etag, useStrongComparison: false)))
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Text(etat.Css, "text/css; charset=utf-8");
        }).AllowAnonymous().ExcludeFromDescription();
}
