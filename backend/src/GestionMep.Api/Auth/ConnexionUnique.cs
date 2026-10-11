using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace GestionMep.Api.Auth;

/// <summary>
/// Connexion unique (SSO) facultative, Microsoft Entra ID en tête, par OpenID Connect (flux « code » + PKCE).
/// Les réglages vivent en base (<see cref="ConfigurationSso"/>) : la console d'administration et la commande
/// <c>gestion-mep sso</c> les modifient sans redémarrage. Le fournisseur ne fait que prouver l'identité : la session
/// ouverte ensuite est le même cookie que pour un compte local.
/// </summary>
public static partial class ConnexionUnique
{
    public const string Schema = "sso";
    /// <summary>Adresse de retour à déclarer chez le fournisseur (hors /api : aucune contrainte CSRF sur ce retour).</summary>
    public const string CheminRetour = "/connexion-sso/retour";
    public const string LibelleMicrosoft = "Se connecter avec Microsoft";
    public const string LibelleOidc = "Se connecter avec l'authentification unique";
    public const string HoteMicrosoft = "login.microsoftonline.com";
    /// <summary>Clé de la propriété d'authentification qui porte le compte à lier (liaison depuis le profil).</summary>
    internal const string ProprieteLier = "mep:lier";
    private const string ButSecret = "GestionMep.Sso.Secret";
    private const string ButLiaison = "GestionMep.Sso.Liaison";
    private static readonly string[] AnnuairesInterdits = ["common", "organizations", "consumers"];

    public static IServiceCollection AjouterConnexionUnique(this IServiceCollection services)
    {
        services.AddSingleton<ReglagesSso>();
        // Sans suivre les redirections : le secret n'est envoyé qu'à l'adresse contrôlée du point de jeton.
        services.AddHttpClient(nameof(ConnexionUnique), c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, OptionsSso>();
        services.AddAuthentication().AddOpenIdConnect(Schema, _ => { });
        return services;
    }

    /// <summary>
    /// Avant l'authentification (le gestionnaire OpenID Connect lit ses options dès le début de la requête) : relit les
    /// réglages en base au départ vers le fournisseur et au retour. Le retour n'est traité que si la connexion unique est
    /// active ; sinon (réglage retiré entre-temps), retour à la page de connexion avec un message.
    /// </summary>
    public static IApplicationBuilder UtiliserGardeConnexionUnique(this IApplicationBuilder app) =>
        app.Use(async (ctx, suivant) =>
        {
            var chemin = ctx.Request.Path;
            var retour = chemin.StartsWithSegments(CheminRetour);
            if (retour || chemin.StartsWithSegments("/api/auth/sso/connexion"))
            {
                var reglages = await ctx.RequestServices.GetRequiredService<ReglagesSso>().Recharger();
                if (retour && !reglages.Active)
                {
                    ctx.Response.Redirect("/connexion?erreur=sso-inactif");
                    return;
                }
            }
            await suivant();
        });

    public static string LibelleEffectif(ConfigurationSso c) =>
        c.Libelle.Length > 0 ? c.Libelle : c.Fournisseur == FournisseurSso.Microsoft ? LibelleMicrosoft : LibelleOidc;

    /// <summary>Adresse de l'émetteur OpenID Connect correspondant aux réglages.</summary>
    public static string AutoriteEffective(ConfigurationSso c) =>
        c.Fournisseur == FournisseurSso.Microsoft ? $"https://{HoteMicrosoft}/{c.Annuaire}/v2.0" : c.Autorite.TrimEnd('/');

    /// <summary>Adresse de retour complète : <c>Application:UrlPublique</c> si elle est définie, sinon celle de la requête.</summary>
    public static string? UrlRetour(IConfiguration config, HttpRequest? requete)
    {
        var baseUrl = config["Application:UrlPublique"] is { Length: > 0 } u ? u.TrimEnd('/')
            : requete is null ? null : $"{requete.Scheme}://{requete.Host}{requete.PathBase}";
        return baseUrl is null ? null : baseUrl + CheminRetour;
    }

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex Domaine();

    /// <summary>Anomalies des réglages (liste vide : utilisables). <paramref name="pourActiver"/> exige aussi un secret lisible.</summary>
    public static List<string> Anomalies(ConfigurationSso c, IDataProtectionProvider cles, bool pourActiver)
    {
        var erreurs = new List<string>();
        if (c.Fournisseur == FournisseurSso.Microsoft)
        {
            var a = c.Annuaire;
            if (a.Length == 0) erreurs.Add("L'ID de l'annuaire (locataire) Microsoft est obligatoire.");
            else if (AnnuairesInterdits.Contains(a))
                erreurs.Add($"« {a} » ouvrirait l'application à tous les comptes Microsoft : indiquez l'ID de l'annuaire de l'entreprise.");
            else if (!Guid.TryParse(a, out _) && !Domaine().IsMatch(a))
                erreurs.Add("L'ID de l'annuaire doit être un GUID (ex. 72f988bf-…) ou un domaine (ex. entreprise.onmicrosoft.com).");
        }
        else if (!Uri.TryCreate(c.Autorite, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)))
        {
            erreurs.Add("L'adresse de l'émetteur doit être une adresse https complète (ex. https://idp.entreprise.fr/realms/mep).");
        }
        if (c.ClientId.Length == 0) erreurs.Add("L'ID d'application (client) est obligatoire.");
        if (pourActiver && Secret(c, cles) is null)
            erreurs.Add(c.SecretChiffre.Length == 0
                ? "Le secret client est obligatoire."
                : "Le secret enregistré est illisible (clés de l'application changées, par exemple après une restauration) : saisissez-le de nouveau.");
        return erreurs;
    }

    /// <summary>
    /// Normalise une saisie (console ou ligne de commande) avant contrôle et enregistrement. Le secret appartient à une
    /// application d'un émetteur : si l'émetteur ou l'ID d'application change, l'ancien secret est effacé (renvoie vrai), pour
    /// qu'il ne soit jamais envoyé à un autre fournisseur ; il faut alors saisir celui de la nouvelle application.
    /// </summary>
    public static bool Appliquer(ConfigurationSso c, FournisseurSso fournisseur, string? annuaire, string? autorite, string? clientId,
        DateOnly? secretExpireLe, bool creationAutomatique, string? roleAdmin, string? libelle)
    {
        var avant = (AutoriteEffective(c), c.ClientId);
        c.Fournisseur = fournisseur;
        c.Annuaire = (annuaire ?? "").Trim().ToLowerInvariant();
        c.Autorite = (autorite ?? "").Trim();
        c.ClientId = (clientId ?? "").Trim();
        c.SecretExpireLe = secretExpireLe;
        c.CreationAutomatique = creationAutomatique;
        c.RoleAdmin = Borner((roleAdmin ?? "").Trim(), 100, "Le rôle d'application");
        c.Libelle = Borner((libelle ?? "").Trim(), 60, "Le texte du bouton");
        if (c.SecretChiffre.Length == 0 || avant == (AutoriteEffective(c), c.ClientId)) return false;
        c.SecretChiffre = "";
        return true;
    }

    private static string Borner(string valeur, int max, string libelle) =>
        valeur.Length <= max ? valeur : throw ErreurHttp.Saisie($"{libelle} ne doit pas dépasser {max} caractères.");

    public static void PoserSecret(ConfigurationSso c, IDataProtectionProvider cles, string secret) =>
        c.SecretChiffre = cles.CreateProtector(ButSecret).Protect(secret.Trim());

    /// <summary>Secret en clair, ou null s'il est absent ou illisible avec les clés actuelles.</summary>
    public static string? Secret(ConfigurationSso c, IDataProtectionProvider cles)
    {
        if (c.SecretChiffre.Length == 0) return null;
        try { return cles.CreateProtector(ButSecret).Unprotect(c.SecretChiffre); }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Jeton de liaison valable 5 minutes, propre au compte connecté (voir <c>POST /api/auth/sso/lier</c>).</summary>
    public static string JetonLiaison(IDataProtectionProvider cles, Guid utilisateurId) =>
        cles.CreateProtector(ButLiaison).ToTimeLimitedDataProtector().Protect(utilisateurId.ToString(), TimeSpan.FromMinutes(5));

    public static Guid? LireJetonLiaison(IDataProtectionProvider cles, string jeton)
    {
        try { return Guid.TryParse(cles.CreateProtector(ButLiaison).ToTimeLimitedDataProtector().Unprotect(jeton), out var id) ? id : null; }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Résultat d'une connexion par le fournisseur : le compte à ouvrir, ou un code d'erreur affiché par la page de connexion.</summary>
    public record Resultat(Utilisateur? Compte, string? Erreur);

    /// <summary>Ce que le fournisseur affirme de la personne (jeton d'identité validé).</summary>
    public record Identite(string Emetteur, string Sujet, string Identifiant, string Nom, IReadOnlyCollection<string> Roles)
    {
        /// <summary>Microsoft : « oid » (identique pour toutes les applications de l'annuaire), sinon « sub ».</summary>
        public static Identite? Depuis(ClaimsPrincipal p)
        {
            var emetteur = p.FindFirstValue("iss");
            var sujet = p.FindFirstValue("oid") ?? p.FindFirstValue("sub");
            if (string.IsNullOrEmpty(emetteur) || string.IsNullOrEmpty(sujet)) return null;
            var identifiant = p.FindFirstValue("preferred_username") ?? p.FindFirstValue("email") ?? p.FindFirstValue("upn") ?? sujet;
            return new Identite(emetteur, sujet, identifiant, p.FindFirstValue("name") ?? identifiant, p.FindAll("roles").Select(r => r.Value).ToHashSet());
        }
    }

    /// <summary>
    /// Retrouve le compte relié à l'identité (émetteur + sujet), le relie à <paramref name="lier"/> (liaison depuis le profil)
    /// ou le crée si les réglages le permettent. Jamais de rapprochement par mail ou login : un compte local existant se relie
    /// seulement depuis son profil, connecté avec son mot de passe.
    /// </summary>
    public static async Task<Resultat> Resoudre(MepDbContext db, ConfigurationSso c, Identite id, Guid? lier, DateTimeOffset maintenant)
    {
        var lien = await db.IdentitesExternes.Include(x => x.Utilisateur)
            .FirstOrDefaultAsync(x => x.Emetteur == id.Emetteur && x.Sujet == id.Sujet);
        Utilisateur u;
        if (lier is { } cible)
        {
            if (lien is not null && lien.UtilisateurId != cible) return new(null, "deja-lie");
            u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == cible) ?? throw new ErreurHttp(401, "Session expirée.");
            if (lien is null)
            {
                // Un seul compte par fournisseur et par personne : une ancienne liaison au même émetteur est remplacée.
                db.IdentitesExternes.RemoveRange(db.IdentitesExternes.Where(x => x.UtilisateurId == u.Id && x.Emetteur == id.Emetteur));
                lien = new IdentiteExterne { UtilisateurId = u.Id, Emetteur = id.Emetteur, Sujet = id.Sujet, CreeeLe = maintenant };
                db.IdentitesExternes.Add(lien);
                JournalAdmin.Ecrire(db, new Acteur(u.Id, u.NomAffiche, u.EstAdmin), "sso.lie", $"{JournalAdmin.Compte(u)} a lié son compte {id.Identifiant}");
            }
        }
        else if (lien?.Utilisateur is { } existant)
        {
            u = existant;
        }
        else
        {
            if (!c.CreationAutomatique) return new(null, "compte-inconnu");
            var login = AuthentificationLocale.NormaliserLogin(id.Identifiant);
            if (await db.Utilisateurs.AnyAsync(x => x.Login == login)) return new(null, "login-existant");
            u = new Utilisateur { Login = login, NomAffiche = id.Nom.Trim().Length > 0 ? id.Nom.Trim() : login, MotDePasseHash = "" };
            db.Utilisateurs.Add(u);
            lien = new IdentiteExterne { UtilisateurId = u.Id, Emetteur = id.Emetteur, Sujet = id.Sujet, CreeeLe = maintenant };
            db.IdentitesExternes.Add(lien);
            JournalAdmin.Ecrire(db, new Acteur(u.Id, u.NomAffiche, false), "compte.cree", $"Compte {JournalAdmin.Compte(u)} créé à la première connexion par {id.Identifiant}");
        }
        if (!u.Actif) return new(null, "compte-desactive");

        if (c.RoleAdmin.Length > 0)
        {
            var admin = id.Roles.Contains(c.RoleAdmin);
            // Le dernier administrateur actif garde son rôle : sinon plus personne ne pourrait corriger les réglages.
            var dernier = u.EstAdmin && !await db.Utilisateurs.AnyAsync(x => x.EstAdmin && x.Actif && x.Id != u.Id);
            if (admin != u.EstAdmin && (admin || !dernier))
            {
                u.EstAdmin = admin;
                JournalAdmin.Ecrire(db, new Acteur(u.Id, u.NomAffiche, admin), "compte.modifie",
                    $"{JournalAdmin.Compte(u)} {(admin ? "devient" : "n'est plus")} administrateur (rôle « {c.RoleAdmin} » du fournisseur)");
            }
        }
        lien.Identifiant = id.Identifiant.Length <= 255 ? id.Identifiant : id.Identifiant[..255];
        lien.DerniereConnexion = maintenant;
        await db.SaveChangesAsync();
        return new(u, null);
    }

    /// <summary>Une vérification du test de configuration (console et <c>gestion-mep sso tester</c>).</summary>
    public record Verification(string Libelle, bool Ok, string Detail);

    /// <summary>
    /// Vérifie que le fournisseur répond et reconnaît l'application et son secret, sans connecter personne : lecture du
    /// document de découverte, puis demande d'un jeton « client_credentials » (aucune donnée lue).
    /// </summary>
    public static async Task<List<Verification>> Tester(ConfigurationSso c, IDataProtectionProvider cles, HttpClient http, CancellationToken ct = default)
    {
        var resultats = new List<Verification>();
        var anomalies = Anomalies(c, cles, pourActiver: true);
        if (anomalies.Count > 0)
        {
            resultats.Add(new("Réglages complets", false, string.Join(" ", anomalies)));
            return resultats;
        }
        resultats.Add(new("Réglages complets", true, $"Émetteur {AutoriteEffective(c)}"));

        string? pointJeton;
        try
        {
            using var reponse = await http.GetAsync(AutoriteEffective(c) + "/.well-known/openid-configuration", ct);
            if (!reponse.IsSuccessStatusCode)
            {
                resultats.Add(new("Fournisseur joignable", false, c.Fournisseur == FournisseurSso.Microsoft && reponse.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "Microsoft ne connaît pas cet annuaire : vérifiez l'ID de l'annuaire."
                    : $"Le document de découverte a répondu {(int)reponse.StatusCode}."));
                return resultats;
            }
            using var json = await JsonDocument.ParseAsync(await reponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            pointJeton = json.RootElement.TryGetProperty("token_endpoint", out var t) ? t.GetString() : null;
            // Le secret ne part que vers un point de jeton sûr : https (http seulement en local), et chez Microsoft pour Entra ID.
            var refus = pointJeton is null ? "Document de découverte incomplet (token_endpoint absent)."
                : !Uri.TryCreate(pointJeton, UriKind.Absolute, out var uriJeton) || !(uriJeton.Scheme == "https" || (uriJeton.Scheme == "http" && uriJeton.IsLoopback))
                    ? $"Point de jeton refusé ({pointJeton}) : il doit être en https."
                : c.Fournisseur == FournisseurSso.Microsoft && !string.Equals(uriJeton.Host, HoteMicrosoft, StringComparison.OrdinalIgnoreCase)
                    ? $"Point de jeton refusé ({pointJeton}) : il doit être sur {HoteMicrosoft}."
                : null;
            resultats.Add(new("Fournisseur joignable", refus is null, refus ?? "Document de découverte lu."));
            if (refus is not null) return resultats;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            resultats.Add(new("Fournisseur joignable", false, $"Injoignable depuis le serveur ({e.Message}). Vérifiez l'accès à Internet ou le proxy sortant."));
            return resultats;
        }

        var champs = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = c.ClientId,
            ["client_secret"] = Secret(c, cles)!,
            ["scope"] = c.Fournisseur == FournisseurSso.Microsoft ? "https://graph.microsoft.com/.default" : "openid",
        };
        try
        {
            using var requete = new HttpRequestMessage(HttpMethod.Post, pointJeton) { Content = new FormUrlEncodedContent(champs) };
            requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var reponse = await http.SendAsync(requete, ct);
            if (reponse.IsSuccessStatusCode)
            {
                resultats.Add(new("Application et secret reconnus", true, "Le fournisseur a accepté l'ID d'application et le secret."));
                return resultats;
            }
            string? code = null, description = null;
            try
            {
                using var json = await JsonDocument.ParseAsync(await reponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                code = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                description = json.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            }
            catch (JsonException) { }
            resultats.Add(InterpreterRefus(code, description));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            resultats.Add(new("Application et secret reconnus", false, $"Le point de jeton est injoignable ({e.Message})."));
        }
        return resultats;
    }

    private static Verification InterpreterRefus(string? code, string? description)
    {
        const string libelle = "Application et secret reconnus";
        var aad = description?.Split(':', 2)[0].Trim() ?? "";
        // Première phrase du message du fournisseur, sans les identifiants de trace.
        var texte = description?.Split(". ", 2)[0].Trim() ?? code ?? "réponse vide";
        return aad switch
        {
            "AADSTS700016" => new(libelle, false, "Application introuvable dans cet annuaire : vérifiez l'ID d'application (client)."),
            "AADSTS7000215" => new(libelle, false, "Secret refusé : copiez la « Valeur » du secret, pas son « ID »."),
            "AADSTS7000222" => new(libelle, false, "Secret expiré : créez-en un nouveau dans Entra (Certificats et secrets) puis saisissez-le ici."),
            _ when code == "invalid_client" => new(libelle, false, $"Le fournisseur refuse l'ID d'application ou le secret ({texte})."),
            // Le client s'est authentifié, seule la demande de jeton de test (sans utilisateur) n'est pas permise : attendu.
            _ when code is "unauthorized_client" or "unsupported_grant_type" or "invalid_scope" =>
                new(libelle, true, $"Application authentifiée (le fournisseur refuse seulement le jeton de test : « {code} », ce qui est attendu)."),
            _ => new(libelle, false, $"Vérification impossible, le fournisseur a répondu : {texte}."),
        };
    }
}

/// <summary>
/// Réglages SSO en mémoire, relus en base à chaque début de connexion : un changement fait par la ligne de commande
/// (autre processus) ou par une autre instance est pris en compte sans redémarrage.
/// </summary>
public class ReglagesSso(IServiceScopeFactory scopes, IOptionsMonitorCache<OpenIdConnectOptions> cache)
{
    private volatile ConfigurationSso _actuels = new();

    public ConfigurationSso Actuels => _actuels;

    public async Task<ConfigurationSso> Recharger()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var lus = await db.ConfigurationsSso.AsNoTracking().FirstOrDefaultAsync() ?? new ConfigurationSso();
        if (lus.ModifieeLe != _actuels.ModifieeLe || lus.Active != _actuels.Active)
        {
            _actuels = lus;
            // Les options OpenID Connect sont reconstruites à la prochaine utilisation.
            cache.TryRemove(ConnexionUnique.Schema);
        }
        return _actuels;
    }
}

/// <summary>Options du gestionnaire OpenID Connect, construites à partir des réglages en base.</summary>
public class OptionsSso(ReglagesSso reglages, IDataProtectionProvider cles, IConfiguration config) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions o) => Configure(Options.DefaultName, o);

    public void Configure(string? name, OpenIdConnectOptions o)
    {
        if (name != ConnexionUnique.Schema) return;
        var c = reglages.Actuels;
        var utilisable = c.Active && ConnexionUnique.Anomalies(c, cles, pourActiver: true).Count == 0;
        // Désactivé : valeurs neutres qui passent la validation ; le retour est de toute façon refusé par la garde.
        o.Authority = utilisable ? ConnexionUnique.AutoriteEffective(c) : "https://login.microsoftonline.com/organizations/v2.0";
        o.ClientId = utilisable ? c.ClientId : "non-configure";
        o.ClientSecret = utilisable ? ConnexionUnique.Secret(c, cles) : null;
        o.RequireHttpsMetadata = o.Authority.StartsWith("https://", StringComparison.Ordinal);
        o.CallbackPath = ConnexionUnique.CheminRetour;
        o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.ResponseType = OpenIdConnectResponseType.Code;
        // Retour par GET : les cookies de corrélation peuvent rester SameSite=Lax (pas de POST inter-sites).
        o.ResponseMode = OpenIdConnectResponseMode.Query;
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.UsePkce = true;
        o.SaveTokens = false;
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.Scope.Clear();
        foreach (var s in new[] { "openid", "profile", "email" }) o.Scope.Add(s);
        o.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                if (ConnexionUnique.UrlRetour(config, null) is { } fixe) ctx.ProtocolMessage.RedirectUri = fixe;
                return Task.CompletedTask;
            },
            OnTokenValidated = ApresValidation,
            OnRemoteFailure = ctx =>
            {
                var annule = ctx.Failure?.Message.Contains("access_denied", StringComparison.Ordinal) == true;
                ctx.Response.Redirect($"/connexion?erreur={(annule ? "sso-annule" : "sso-echec")}");
                ctx.HandleResponse();
                ctx.HttpContext.RequestServices.GetRequiredService<ILogger<OptionsSso>>()
                    .LogWarning("Connexion unique refusée : {Message}", ctx.Failure?.Message);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Jeton d'identité validé (émetteur, audience, signature, nonce) : on ouvre la session du compte correspondant.</summary>
    private async Task ApresValidation(TokenValidatedContext ctx)
    {
        var http = ctx.HttpContext;
        var id = ConnexionUnique.Identite.Depuis(ctx.Principal!);
        Guid? lier = null;
        if (ctx.Properties?.Items.TryGetValue(ConnexionUnique.ProprieteLier, out var l) == true && Guid.TryParse(l, out var g))
        {
            // La liaison n'est faite que pour la session qui l'a demandée.
            var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (session.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) != g.ToString()) { Refuser("sso-session"); return; }
            lier = g;
        }
        if (id is null) { Refuser("sso-echec"); return; }
        var db = http.RequestServices.GetRequiredService<MepDbContext>();
        var temps = http.RequestServices.GetRequiredService<TimeProvider>();
        var resultat = await ConnexionUnique.Resoudre(db, reglages.Actuels, id, lier, temps.GetUtcNow());
        if (resultat.Compte is not { } u) { Refuser(resultat.Erreur!); return; }
        ctx.Principal = Acteur.VersPrincipal(u, CookieAuthenticationDefaults.AuthenticationScheme);

        void Refuser(string code)
        {
            ctx.Response.Redirect(lier is null ? $"/connexion?erreur={code}" : $"/profil?sso={code}");
            ctx.HandleResponse();
        }
    }
}
