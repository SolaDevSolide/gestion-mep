using System.Security.Claims;
using System.Security.Cryptography;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Auth;

/// <summary>
/// Authentification par comptes locaux et cookie. La connexion unique (<see cref="ConnexionUnique"/>) émet le même cookie
/// avec <see cref="Acteur.VersPrincipal"/>. Le reste de l'API ne lit que les claims NameIdentifier / Name / Role.
/// </summary>
public static class AuthentificationLocale
{
    public const string Cookie = "gestion-mep.auth";
    public const string PolitiqueAdmin = "admin";
    public const int LongueurMinimaleMotDePasse = 10;

    /// <param name="env">Hors Development, le cookie est toujours marqué Secure : derrière le reverse proxy HTTPS,
    /// Kestrel ne voit que du HTTP et <c>SameAsRequest</c> l'enverrait en clair.</param>
    public static IServiceCollection AjouterAuthentification(this IServiceCollection services, IHostEnvironment env)
    {
        services.AddSingleton<IPasswordHasher<Utilisateur>, PasswordHasher<Utilisateur>>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie = new CookieSession();
                o.Cookie.Name = Cookie;
                o.Cookie.IsEssential = true;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.SlidingExpiration = true;
                // API : jamais de redirection vers une page de connexion.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ctx.Response.WriteAsJsonAsync(new { message = "Réservé aux administrateurs." });
                };
                o.Events.OnValidatePrincipal = ValiderSession;
            });
        services.AddAuthorizationBuilder().AddPolicy(PolitiqueAdmin, p => p.RequireRole(Acteur.RoleAdmin));
        return services;
    }

    /// <summary>
    /// Cookie de session marqué Secure, sauf si le réglage <see cref="Reglages.HttpsObligatoire"/> est désactivé (mode
    /// d'essai sans reverse proxy) : il suit alors le schéma de la requête, relu à chaque fois.
    /// </summary>
    private sealed class CookieSession : RequestPathBaseCookieBuilder
    {
        public override CookieOptions Build(HttpContext context, DateTimeOffset expiresFrom)
        {
            var options = base.Build(context, expiresFrom);
            if (!context.RequestServices.GetRequiredService<IConfiguration>().GetValue(Reglages.HttpsObligatoire, true))
                options.Secure = context.Request.IsHttps;
            return options;
        }
    }

    /// <summary>
    /// À chaque requête : le compte doit exister, être actif et porter le même tampon de sécurité que le cookie
    /// (sinon la session est fermée). Nom, rôle admin et mot de passe provisoire sont relus en base : un retrait de droit
    /// s'applique tout de suite, sans attendre l'expiration du cookie. Le nom est celui de la personne : un compte relié à un
    /// compte principal signe les journaux du nom du compte principal (le login du compte reste dans le journal).
    /// </summary>
    private static async Task ValiderSession(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        var tampon = principal?.FindFirstValue(Acteur.ClaimTampon);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<MepDbContext>();
        var compte = Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Utilisateurs.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new Utilisateur
                {
                    Id = u.Id,
                    NomAffiche = u.ComptePrincipalId == null ? u.NomAffiche
                        : db.Utilisateurs.Where(p => p.Id == u.ComptePrincipalId).Select(p => p.NomAffiche).FirstOrDefault() ?? u.NomAffiche,
                    EstAdmin = u.EstAdmin, Actif = u.Actif,
                    DoitChangerMotDePasse = u.DoitChangerMotDePasse, TamponSecurite = u.TamponSecurite,
                })
                .FirstOrDefaultAsync()
            : null;
        if (compte is null || !compte.Actif || tampon != compte.TamponSecurite.ToString())
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        ctx.ReplacePrincipal(Acteur.VersPrincipal(compte, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// Tant que le mot de passe est provisoire, seules les routes /api/auth (dont le changement de mot de passe),
    /// /api/sante, /api/version, la lecture de /api/demo (bandeau du mode démonstration) et celle de /api/charte (couleurs
    /// de l'interface) répondent ; les autres renvoient 403.
    /// </summary>
    public static IApplicationBuilder UtiliserMotDePasseProvisoire(this IApplicationBuilder app) =>
        app.Use(async (ctx, suivant) =>
        {
            var chemin = ctx.Request.Path;
            if (ctx.User.HasClaim(c => c.Type == Acteur.ClaimMotDePasseProvisoire)
                && chemin.StartsWithSegments("/api")
                && !chemin.StartsWithSegments("/api/auth") && !chemin.StartsWithSegments("/api/sante") && !chemin.StartsWithSegments("/api/version")
                && !(HttpMethods.IsGet(ctx.Request.Method) && chemin.Equals("/api/demo", StringComparison.OrdinalIgnoreCase))
                && !(HttpMethods.IsGet(ctx.Request.Method) && chemin.StartsWithSegments("/api/charte")))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = "Changez votre mot de passe provisoire avant de continuer." });
                return;
            }
            await suivant();
        });

    /// <summary>Mot de passe provisoire aléatoire, lisible (sans 0/O, 1/l/I) : quatre groupes de quatre caractères.</summary>
    public static string MotDePasseProvisoire()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var brut = RandomNumberGenerator.GetString(alphabet, 16);
        return string.Join('-', Enumerable.Range(0, 4).Select(i => brut.Substring(i * 4, 4)));
    }

    /// <summary>Valeurs d'exemple ou évidentes, refusées comme mot de passe du compte administrateur initial.</summary>
    private static readonly string[] MotsDePasseExemple = ["changer-moi", "changeme", "admin", "administrateur", "password", "motdepasse", "demo", "mep"];

    /// <summary>Raison du refus d'un mot de passe administrateur initial (valeur d'exemple ou trop courte), ou null s'il convient.</summary>
    public static string? MotDePasseInitialRefuse(string motDePasse)
    {
        var m = motDePasse.Trim().ToLowerInvariant();
        if (MotsDePasseExemple.Any(x => m == x || m.StartsWith(x, StringComparison.Ordinal) && m.Length < x.Length + 4))
            return "valeur d'exemple";
        return motDePasse.Length < LongueurMinimaleMotDePasse ? $"moins de {LongueurMinimaleMotDePasse} caractères" : null;
    }

    /// <summary>Contrôle d'un nouveau mot de passe choisi par l'utilisateur (400 sinon).</summary>
    public static void ValiderNouveauMotDePasse(string? nouveau, string? ancien)
    {
        if ((nouveau ?? "").Length < LongueurMinimaleMotDePasse)
            throw Services.ErreurHttp.Saisie($"Le mot de passe doit contenir au moins {LongueurMinimaleMotDePasse} caractères.");
        if (nouveau == ancien) throw Services.ErreurHttp.Saisie("Le nouveau mot de passe doit être différent de l'actuel.");
    }

    /// <summary>
    /// Pose un mot de passe provisoire (création ou réinitialisation) : le compte devra le changer et ses sessions sont fermées.
    /// Renvoie le mot de passe en clair, à afficher une seule fois.
    /// </summary>
    public static string PoserMotDePasseProvisoire(Utilisateur u, IPasswordHasher<Utilisateur> hasher)
    {
        var mdp = MotDePasseProvisoire();
        u.MotDePasseHash = hasher.HashPassword(u, mdp);
        u.DoitChangerMotDePasse = true;
        u.TamponSecurite = Guid.NewGuid();
        return mdp;
    }

    public static string NormaliserLogin(string? login) => (login ?? "").Trim().ToLowerInvariant();

    /// <summary>Login obligatoire et de longueur raisonnable (400 sinon) ; au-delà, la page de connexion le refuserait.</summary>
    public static void ExigerLoginValide(string login)
    {
        if (login.Length == 0) throw Services.ErreurHttp.Saisie("Le login est obligatoire.");
        if (login.Length > Securite.LimiteurConnexion.LongueurMaxLogin)
            throw Services.ErreurHttp.Saisie($"Le login ne doit pas dépasser {Securite.LimiteurConnexion.LongueurMaxLogin} caractères.");
    }

    /// <summary>
    /// Le nom affiché signe les journaux : il ne peut pas reprendre le nom affiché ou le login d'un autre compte (400 sinon),
    /// pour que personne ne se fasse passer pour un autre.
    /// </summary>
    public static async Task ExigerNomDisponible(MepDbContext db, string nom, Guid? compteId)
    {
        var autres = await db.Utilisateurs.AsNoTracking().Where(u => u.Id != compteId).Select(u => new { u.NomAffiche, u.Login }).ToListAsync();
        if (autres.Any(u => string.Equals(u.NomAffiche, nom, StringComparison.OrdinalIgnoreCase) || string.Equals(u.Login, nom, StringComparison.OrdinalIgnoreCase)))
            throw Services.ErreurHttp.Saisie($"« {nom} » est déjà le nom ou le login d'un autre compte : choisissez un nom qui vous distingue.");
    }

    /// <summary>Vérifie le couple login / mot de passe ; renvoie l'utilisateur ou null.</summary>
    public static async Task<Utilisateur?> Verifier(MepDbContext db, IPasswordHasher<Utilisateur> hasher, string? login, string? motDePasse)
    {
        var l = NormaliserLogin(login);
        var u = l.Length == 0 ? null : await db.Utilisateurs.FirstOrDefaultAsync(x => x.Login == l);
        if (u is null)
        {
            // Même coût qu'un vrai contrôle, pour ne pas révéler l'existence du login.
            hasher.HashPassword(new Utilisateur(), motDePasse ?? "");
            return null;
        }
        var res = hasher.VerifyHashedPassword(u, u.MotDePasseHash, motDePasse ?? "");
        if (res == PasswordVerificationResult.Failed) return null;
        if (res == PasswordVerificationResult.SuccessRehashNeeded)
        {
            u.MotDePasseHash = hasher.HashPassword(u, motDePasse!);
            await db.SaveChangesAsync();
        }
        return u;
    }

    public static Task Ouvrir(HttpContext ctx, Utilisateur u) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Acteur.VersPrincipal(u, CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task Fermer(HttpContext ctx) => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
