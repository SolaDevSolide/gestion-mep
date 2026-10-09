using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>Erreur renvoyée telle quelle au client sous la forme <c>{ "message": "…" }</c>.</summary>
public class ErreurHttp(int statut, string message) : Exception(message)
{
    public int Statut { get; } = statut;

    public static ErreurHttp Saisie(string message) => new(400, message);
    public static ErreurHttp Interdit(string message) => new(403, message);
    public static ErreurHttp NonTrouve(string message) => new(404, message);
    public static ErreurHttp Conflit(string message) => new(409, message);
}

/// <summary>Traduit les exceptions en réponses JSON : règle métier 422, saisie 400, conflit 409…</summary>
public static class MiddlewareErreurs
{
    public static void UtiliserGestionErreurs(this WebApplication app) =>
        app.Use(async (ctx, suivant) =>
        {
            try
            {
                await suivant();
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
                await ctx.Response.WriteAsJsonAsync(new { message });
            }
        });

    private static (int, string) Traduire(Exception ex, HttpContext ctx)
    {
        switch (ex)
        {
            case ErreurHttp e: return (e.Statut, e.Message);
            case GestionMep.Domain.RegleMetierException e: return (422, e.Message);
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
