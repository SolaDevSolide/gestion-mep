using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>
/// Erreur renvoyée telle quelle au client, au format de <see cref="ErreurDto"/>. Codes (voir docs/API.md) : 400 saisie
/// invalide, 403 droits insuffisants, 404 introuvable, 409 conflit, 422 règle métier.
/// </summary>
public class ErreurHttp(int statut, string message, IReadOnlyList<string>? erreurs = null) : Exception(message)
{
    public int Statut { get; } = statut;
    /// <summary>Liste détaillée des anomalies (champ facultatif <c>erreurs</c> du corps), en plus du message.</summary>
    public IReadOnlyList<string>? Erreurs { get; } = erreurs;

    /// <summary>422 avec toutes les anomalies que la règle métier relève (le message les joint).</summary>
    public static ErreurHttp Regle(IReadOnlyList<string> erreurs, string prefixe = "") =>
        new(422, prefixe + string.Join(" ", erreurs), erreurs);

    public static ErreurHttp Saisie(string message) => new(400, message);
    /// <summary>400 avec toutes les anomalies d'un formulaire (le message les joint).</summary>
    public static ErreurHttp Saisie(IReadOnlyList<string> erreurs) => new(400, string.Join(" ", erreurs), erreurs);
    public static ErreurHttp Interdit(string message) => new(403, message);
    public static ErreurHttp NonTrouve(string message) => new(404, message);
    public static ErreurHttp Conflit(string message) => new(409, message);
}

/// <summary>
/// Corps de toute réponse d'erreur de l'API, au format « problem details » (RFC 9457, type <c>application/problem+json</c>) :
/// <c>status</c> reprend le code HTTP, <c>title</c> sa nature (« Droits insuffisants »…), <c>detail</c> et <c>message</c> le
/// message à afficher, en français ; <c>erreurs</c>, absent le plus souvent, liste chaque anomalie quand il y en a plusieurs.
/// </summary>
public record ErreurDto(string Type, string Title, int Status, string Detail, string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Erreurs = null);

/// <summary>Écriture des réponses d'erreur, partagée par le traducteur d'exceptions et les contrôles placés avant les routes.</summary>
public static class ReponsesErreur
{
    public const string TypeContenu = "application/problem+json";

    public static string Titre(int statut) => statut switch
    {
        400 => "Saisie invalide",
        401 => "Authentification requise",
        403 => "Droits insuffisants",
        404 => "Introuvable",
        409 => "Conflit",
        413 => "Requête trop volumineuse",
        422 => "Règle métier",
        429 => "Trop de requêtes",
        503 => "Service indisponible",
        507 => "Espace disque insuffisant",
        >= 500 => "Erreur interne",
        _ => "Erreur",
    };

    public static ErreurDto Corps(int statut, string message, IReadOnlyList<string>? erreurs = null) =>
        new("about:blank", Titre(statut), statut, message, message, erreurs);

    /// <summary>Résultat d'un endpoint (connexion refusée, route inconnue…).</summary>
    public static IResult Resultat(int statut, string message) =>
        Results.Json(Corps(statut, message), contentType: TypeContenu, statusCode: statut);

    /// <summary>Écrit la réponse d'un intergiciel (contrôle avant la route).</summary>
    public static Task Ecrire(HttpContext ctx, int statut, string message, IReadOnlyList<string>? erreurs = null, CancellationToken annulation = default)
    {
        ctx.Response.StatusCode = statut;
        return ctx.Response.WriteAsJsonAsync(Corps(statut, message, erreurs), (System.Text.Json.JsonSerializerOptions?)null, TypeContenu, annulation);
    }
}

/// <summary>
/// Traduit les exceptions en réponses <see cref="ErreurDto"/> : droits insuffisants 403, saisie invalide 400, règle métier
/// 422, conflit 409…
/// </summary>
public static class MiddlewareErreurs
{
    private const string MessageTropVolumineux = "Requête trop volumineuse.";

    public static void UtiliserGestionErreurs(this WebApplication app) =>
        app.Use(async (ctx, suivant) =>
        {
            try
            {
                await suivant();
                // Corps trop gros détecté par Kestrel pendant la lecture du JSON : le statut 413 est posé sans message.
                if (ctx.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !ctx.Response.HasStarted)
                    await ReponsesErreur.Ecrire(ctx, StatusCodes.Status413PayloadTooLarge, MessageTropVolumineux);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // Client déconnecté (flux SSE notamment) : rien à répondre.
            }
            catch (Exception ex)
            {
                var (statut, message) = Traduire(ex, ctx);
                if (ctx.Response.HasStarted) return;
                ctx.Response.Clear();
                await ReponsesErreur.Ecrire(ctx, statut, message, (ex as ErreurHttp)?.Erreurs);
            }
        });

    private static (int, string) Traduire(Exception ex, HttpContext ctx)
    {
        switch (ex)
        {
            case ErreurHttp e: return (e.Statut, e.Message);
            case GestionMep.Domain.DroitInsuffisantException e: return (403, e.Message);
            case GestionMep.Domain.SaisieInvalideException e: return (400, e.Message);
            case GestionMep.Domain.RegleMetierException e: return (422, e.Message);
            case BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }:
                return (413, MessageTropVolumineux);
            case BadHttpRequestException:
                return (400, "Requête invalide : corps JSON absent, mal formé ou contenant une valeur inconnue.");
            case DbUpdateConcurrencyException:
                return (409, "Ces données ont été modifiées par quelqu'un d'autre. Rechargez la page puis recommencez.");
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }:
                return (409, "Conflit avec une modification simultanée (donnée déjà existante). Rechargez la page puis recommencez.");
            default:
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Erreurs")
                    .LogError(ex, "Erreur non gérée sur {Methode} {Chemin}", ctx.Request.Method, ctx.Request.Path);
                return (500, "Erreur interne du serveur.");
        }
    }
}
