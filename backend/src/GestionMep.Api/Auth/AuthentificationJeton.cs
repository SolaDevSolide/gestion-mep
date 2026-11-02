using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionMep.Api.Auth;

/// <summary>
/// Authentification par jeton d'accès (<c>Authorization: Bearer gmep_…</c>), à côté du cookie de session. Une requête /api qui
/// porte un jeton n'est authentifiée que par lui (le cookie est ignoré) ; elle n'ouvre jamais de session et n'est pas soumise
/// à l'en-tête anti-CSRF (un autre site ne peut pas ajouter cet en-tête sans pré-vol CORS, refusé). Jeton inconnu, expiré,
/// révoqué ou compte désactivé : 401 sur toute route /api, même publique.
/// </summary>
public static class AuthentificationJeton
{
    public const string Schema = "Jeton";
    /// <summary>Schéma par défaut : jeton si la requête /api en porte un, cookie de session sinon.</summary>
    public const string SchemaAuto = "SessionOuJeton";
    public const string Prefixe = "gmep_";
    /// <summary>Caractères aléatoires après <see cref="Prefixe"/> : 43 en base 62, soit plus de 256 bits.</summary>
    public const int LongueurSecret = 43;
    /// <summary>Caractères aléatoires repris dans le préfixe visible.</summary>
    public const int LongueurPrefixeVisible = 8;
    public const string ClaimJeton = "mep:jeton";
    public const string ClaimPortee = "mep:portee";
    public const string MessageInvalide = "Jeton d'accès invalide.";
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Requête /api avec un en-tête <c>Authorization: Bearer …</c>.</summary>
    public static bool PorteJeton(HttpRequest r) =>
        r.Path.StartsWithSegments("/api") && r.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nouveau jeton : texte complet (à montrer une seule fois), préfixe visible et empreinte à enregistrer.</summary>
    public static (string Jeton, string PrefixeVisible, string Empreinte) Generer()
    {
        var jeton = Prefixe + RandomNumberGenerator.GetString(Base62, LongueurSecret);
        return (jeton, jeton[..(Prefixe.Length + LongueurPrefixeVisible)], Empreinte(jeton));
    }

    /// <summary>SHA-256 du jeton complet, en hexadécimal minuscule (secret aléatoire de 256 bits : pas besoin d'un hachage lent).</summary>
    public static string Empreinte(string jeton) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jeton)));

    public static AuthenticationBuilder AjouterJetons(this AuthenticationBuilder auth) => auth
        .AddPolicyScheme(SchemaAuto, "Session ou jeton", o => o.ForwardDefaultSelector = ctx =>
            PorteJeton(ctx.Request) ? Schema : CookieAuthenticationDefaults.AuthenticationScheme)
        .AddScheme<AuthenticationSchemeOptions, GestionnaireJeton>(Schema, _ => { });

    /// <summary>
    /// Après l'authentification : une requête qui porte un jeton refusé reçoit 401 (sans retomber sur une route publique),
    /// puis la route doit être ouverte aux jetons et couverte par ses portées (403 sinon, voir <see cref="PorteesRoutes"/>).
    /// </summary>
    public static IApplicationBuilder UtiliserJetons(this IApplicationBuilder app) =>
        app.Use(async (ctx, suivant) =>
        {
            if (!PorteJeton(ctx.Request))
            {
                await suivant();
                return;
            }
            var resultat = await ctx.AuthenticateAsync(Schema);
            if (!resultat.Succeeded)
            {
                ctx.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                await ReponsesErreur.Ecrire(ctx, StatusCodes.Status401Unauthorized, resultat.Failure?.Message ?? MessageInvalide);
                return;
            }
            var portees = resultat.Principal.FindAll(ClaimPortee).Select(c => c.Value).ToList();
            var decision = PorteesRoutes.Requises(ctx.Request.Method, ctx.GetEndpoint());
            if (!decision.Ouverte)
            {
                await ReponsesErreur.Ecrire(ctx, StatusCodes.Status403Forbidden, PorteesRoutes.MessageFermee);
                return;
            }
            if (decision.UneParmi.Count > 0 && !decision.UneParmi.Any(p => PorteesJeton.Couvre(portees, p)))
            {
                ctx.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{string.Join(' ', decision.UneParmi)}\"";
                await ReponsesErreur.Ecrire(ctx, StatusCodes.Status403Forbidden,
                    $"Ce jeton n'a pas la portée nécessaire : {string.Join(" ou ", decision.UneParmi.Select(p => $"« {p} »"))}.");
                return;
            }
            await suivant();
        });

    /// <summary>Message d'un jeton expiré, avec la date (UTC) pour aider à comprendre un script qui s'arrête.</summary>
    public static string MessageExpire(DateTimeOffset expireLe) =>
        $"Ce jeton d'accès a expiré le {expireLe.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (UTC) : en créer un nouveau.";

    /// <summary>
    /// Vérifie le jeton présenté et construit l'utilisateur de la requête : la personne (jeton personnel, sans le rôle
    /// administrateur : la portée admin est réservée aux jetons de service) ou le compte technique (jeton de service,
    /// administrateur s'il a la portée admin). Le nom est celui de la personne (compte principal), comme pour la session.
    /// </summary>
    private sealed class GestionnaireJeton(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory journaux, UrlEncoder encodeur)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, journaux, encodeur)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var entete = Request.Headers.Authorization.ToString();
            if (!entete.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
            var valeur = entete["Bearer ".Length..].Trim();
            if (!valeur.StartsWith(Prefixe, StringComparison.Ordinal) || valeur.Length > 200) return AuthenticateResult.Fail(MessageInvalide);
            var empreinte = Empreinte(valeur);
            var db = Context.RequestServices.GetRequiredService<MepDbContext>();
            var trouve = await db.JetonsApi.AsNoTracking().Where(j => j.Empreinte == empreinte)
                .Select(j => new
                {
                    Jeton = j,
                    Compte = db.Utilisateurs.Where(u => u.Id == j.UtilisateurId).Select(u => new
                    {
                        u.Actif, u.DoitChangerMotDePasse,
                        Nom = u.ComptePrincipalId == null ? u.NomAffiche
                            : db.Utilisateurs.Where(p => p.Id == u.ComptePrincipalId).Select(p => p.NomAffiche).FirstOrDefault() ?? u.NomAffiche,
                    }).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(Context.RequestAborted);
            if (trouve is null) return AuthenticateResult.Fail(MessageInvalide);
            var jeton = trouve.Jeton;
            var maintenant = Context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            switch (jeton.Etat(maintenant))
            {
                case EtatJeton.Revoque: return AuthenticateResult.Fail("Ce jeton d'accès a été révoqué.");
                case EtatJeton.Expire: return AuthenticateResult.Fail(MessageExpire(jeton.ExpireLe));
            }
            if (trouve.Compte is not { Actif: true } compte) return AuthenticateResult.Fail("Le compte de ce jeton d'accès est désactivé.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, jeton.UtilisateurId.ToString()),
                new(ClaimTypes.Name, compte.Nom),
                new(ClaimJeton, jeton.Id.ToString()),
            };
            claims.AddRange(jeton.Portees.Select(p => new Claim(ClaimPortee, p)));
            if (jeton.Type == TypeJeton.Service && jeton.Portees.Contains(PorteesJeton.Admin)) claims.Add(new Claim(ClaimTypes.Role, Acteur.RoleAdmin));
            if (compte.DoitChangerMotDePasse) claims.Add(new Claim(Acteur.ClaimMotDePasseProvisoire, "1"));
            await EnregistrerUtilisation(db, jeton, maintenant);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Schema, ClaimTypes.Name, ClaimTypes.Role));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Schema));
        }

        /// <summary>Date et adresse de la dernière utilisation, au plus une écriture par intervalle (condition dans la requête).</summary>
        private async Task EnregistrerUtilisation(MepDbContext db, JetonApi jeton, DateTimeOffset maintenant)
        {
            if (!Regles.UtilisationAEnregistrer(jeton.DerniereUtilisationLe, maintenant)) return;
            var limite = maintenant - Regles.IntervalleUtilisationJeton;
            var ip = Context.Connection.RemoteIpAddress?.ToString();
            await db.JetonsApi.Where(j => j.Id == jeton.Id && (j.DerniereUtilisationLe == null || j.DerniereUtilisationLe <= limite))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.DerniereUtilisationLe, maintenant).SetProperty(j => j.DerniereAdresseIp, ip), Context.RequestAborted);
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var resultat = await HandleAuthenticateOnceSafeAsync();
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            await ReponsesErreur.Ecrire(Context, StatusCodes.Status401Unauthorized, resultat.Failure?.Message ?? "Authentification requise.");
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            ReponsesErreur.Ecrire(Context, StatusCodes.Status403Forbidden, AuthentificationLocale.MessageReserveAdmin);
    }
}
