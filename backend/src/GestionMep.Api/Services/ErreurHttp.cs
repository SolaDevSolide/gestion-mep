using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>Erreur renvoyée telle quelle au client sous la forme <c>{ "message": "…" }</c>.</summary>
public class ErreurHttp(int statut, string message, IReadOnlyList<string>? erreurs = null) : Exception(message)
{
    public int Statut { get; } = statut;
    /// <summary>Liste détaillée des anomalies (champ facultatif <c>erreurs</c> du corps), en plus du message.</summary>
    public IReadOnlyList<string>? Erreurs { get; } = erreurs;

    /// <summary>422 avec toutes les anomalies d'une saisie (le message les joint).</summary>
    public static ErreurHttp Regle(IReadOnlyList<string> erreurs, string prefixe = "") =>
        new(422, prefixe + string.Join(" ", erreurs), erreurs);

    public static ErreurHttp Saisie(string message) => new(400, message);
    public static ErreurHttp Interdit(string message) => new(403, message);
    public static ErreurHttp NonTrouve(string message) => new(404, message);
    public static ErreurHttp Conflit(string message) => new(409, message);
}

/// <summary>Traduit les exceptions en réponses JSON : règle métier 422, saisie 400, conflit 409…</summary>
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
                    await ctx.Response.WriteAsJsonAsync(new { message = MessageTropVolumineux });
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
                ctx.Response.StatusCode = statut;
                if (ex is ErreurHttp { Erreurs: { } erreurs }) await ctx.Response.WriteAsJsonAsync(new { message, erreurs });
                else await ctx.Response.WriteAsJsonAsync(new { message });
            }
        });

    private static (int, string) Traduire(Exception ex, HttpContext ctx)
    {
        switch (ex)
        {
            case ErreurHttp e: return (e.Statut, e.Message);
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
