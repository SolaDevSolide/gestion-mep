using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Auth;

/// <summary>
/// Authentification par comptes locaux et cookie. Tout ce qui concerne l'identité est isolé dans ce dossier :
/// pour ajouter un SSO OpenID Connect (Entra ID), enregistrer <c>AddOpenIdConnect</c> ici, créer le compte
/// local au premier passage (<see cref="TrouverOuCreer"/>) puis émettre le même cookie avec <see cref="Acteur.VersPrincipal"/>.
/// Le reste de l'API ne lit que les claims NameIdentifier / Name / Role.
/// </summary>
public static class AuthentificationLocale
{
    public const string Cookie = "gestion-mep.auth";

    public static IServiceCollection AjouterAuthentification(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<Utilisateur>, PasswordHasher<Utilisateur>>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = Cookie;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.SlidingExpiration = true;
                // API : jamais de redirection vers une page de connexion.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        services.AddAuthorization();
        return services;
    }

    public static string NormaliserLogin(string? login) => (login ?? "").Trim().ToLowerInvariant();

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

    /// <summary>Point d'entrée prévu pour un futur SSO : retrouve le compte par login, ou le crée.</summary>
    public static async Task<Utilisateur> TrouverOuCreer(MepDbContext db, string login, string nomAffiche)
    {
        var l = NormaliserLogin(login);
        var u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Login == l);
        if (u is not null) return u;
        u = new Utilisateur { Login = l, NomAffiche = nomAffiche, MotDePasseHash = "" };
        db.Utilisateurs.Add(u);
        await db.SaveChangesAsync();
        return u;
    }
}
