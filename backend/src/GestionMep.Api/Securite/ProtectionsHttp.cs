using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using GestionMep.Api.Services;

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
        // réseaux listés dans ProxyInverse:Reseaux (sinon, n'importe qui pourrait choisir son adresse IP). Réglage
        // modifiable sans redémarrage : les options sont reconstruites quand la configuration change.
        services.AddOptions<ForwardedHeadersOptions>().Configure(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var reseau in (config["ProxyInverse:Reseaux"] ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (System.Net.IPNetwork.TryParse(reseau, out var r)) o.KnownIPNetworks.Add(r);
                else if (System.Net.IPAddress.TryParse(reseau, out var ip)) o.KnownProxies.Add(ip);
            }
        });
        services.AddSingleton<IOptionsChangeTokenSource<ForwardedHeadersOptions>>(new ConfigurationChangeTokenSource<ForwardedHeadersOptions>(config));

        services.AddSingleton<LimiteurConnexion>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, annulation) => new ValueTask(ctx.HttpContext.Response.WriteAsJsonAsync(
                new { message = "Trop de tentatives de connexion. Réessayez dans une minute." }, annulation));
            // Limite relue à chaque requête (réglage modifiable) ; elle fait partie de la clé, donc un changement s'applique tout de suite.
            o.AddPolicy(PolitiqueConnexion, ctx =>
            {
                var parMinute = Math.Max(1, config.GetValue("Connexion:TentativesParMinute", 20));
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue"}|{parMinute}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = parMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });
        return services;
    }

    /// <summary>
    /// En-têtes X-Forwarded-For / X-Forwarded-Proto du reverse proxy, avec les réseaux autorisés en cours (le middleware
    /// standard est reconstruit quand le réglage ProxyInverse:Reseaux change).
    /// </summary>
    public static IApplicationBuilder UtiliserEnTetesProxy(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptionsMonitor<ForwardedHeadersOptions>>();
        var journaux = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
        TraitementProxy? actuel = null;
        return app.Use((ctx, suivant) =>
        {
            var o = options.CurrentValue;
            var t = actuel;
            if (t is null || !ReferenceEquals(t.Options, o))
                actuel = t = new TraitementProxy(o, new ForwardedHeadersMiddleware(_ => Task.CompletedTask, journaux, Options.Create(o)));
            t.Middleware.ApplyForwarders(ctx);
            return suivant(ctx);
        });
    }

    private sealed record TraitementProxy(ForwardedHeadersOptions Options, ForwardedHeadersMiddleware Middleware);

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
    /// <summary>Adresse de l'application vue de la machine : celle du fichier d'installation autonome, sinon le port de l'image Docker.</summary>
    private static string AdresseLocale() =>
        (FichierInstallation.AdresseEcoute(FichierInstallation.Lire(FichierInstallation.Chemin)) ?? "http://localhost:8080")
            .Replace("://0.0.0.0:", "://127.0.0.1:").Replace("://[::]:", "://[::1]:");

    public static async Task<int> VerifierSante()
    {
        var url = Environment.GetEnvironmentVariable("SANTE_URL") ?? $"{AdresseLocale()}/api/sante";
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
