using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace GestionMep.Api.Securite;

/// <summary>
/// Protections HTTP de déploiement : en-têtes du reverse proxy, limite des tentatives de connexion par adresse IP,
/// en-tête anti-CSRF exigé sur les requêtes qui modifient.
/// </summary>
public static class ProtectionsHttp
{
    public const string PolitiqueConnexion = "connexion";
    public const string EnTeteCsrf = "X-Requested-With";

    public static IServiceCollection AjouterProtectionsHttp(this IServiceCollection services, IConfiguration config)
    {
        // Reverse proxy HTTPS (IIS, nginx, Traefik…) : adresse du client et schéma d'origine, acceptés seulement des
        // réseaux listés dans ProxyInverse:Reseaux (sinon, n'importe qui pourrait choisir son adresse IP).
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var reseau in (config["ProxyInverse:Reseaux"] ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(reseau));
        });

        services.AddSingleton<LimiteurConnexion>();
        var parMinute = config.GetValue("Connexion:TentativesParMinute", 20);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, annulation) => new ValueTask(ctx.HttpContext.Response.WriteAsJsonAsync(
                new { message = "Trop de tentatives de connexion. Réessayez dans une minute." }, annulation));
            o.AddPolicy(PolitiqueConnexion, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = parMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    /// <summary>
    /// Refuse (403) toute requête /api qui modifie sans l'en-tête <see cref="EnTeteCsrf"/>. Un formulaire ou une image
    /// d'un autre site ne peut pas l'ajouter, et un script d'un autre site ne le peut qu'après un pré-vol CORS, refusé.
    /// </summary>
    public static IApplicationBuilder UtiliserProtectionCsrf(this IApplicationBuilder app) =>
        app.Use(async (ctx, suivant) =>
        {
            var m = ctx.Request.Method;
            if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m) && !HttpMethods.IsOptions(m)
                && !ctx.Request.Headers.ContainsKey(EnTeteCsrf))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = $"En-tête {EnTeteCsrf} manquant." });
                return;
            }
            await suivant();
        });

    /// <summary>
    /// Mode « --sante » de l'image Docker : interroge /api/sante et renvoie 0 si l'application répond
    /// (l'image aspnet n'a ni curl ni wget pour le HEALTHCHECK).
    /// </summary>
    public static async Task<int> VerifierSante()
    {
        var url = Environment.GetEnvironmentVariable("SANTE_URL") ?? "http://localhost:8080/api/sante";
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
            return (await client.GetAsync(url)).StatusCode == HttpStatusCode.OK ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
