using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

public static class AuthEndpoints
{
    private static UtilisateurDto Dto(Utilisateur u) => new(u.Id, u.Login, u.NomAffiche, u.EstAdmin);

    public static void MapAuth(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/connexion", async (ConnexionSaisie corps, HttpContext ctx, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var u = await AuthentificationLocale.Verifier(db, hasher, corps.Login, corps.MotDePasse);
            if (u is null) return Results.Json(new { message = "Identifiant ou mot de passe incorrect." }, statusCode: 401);
            await AuthentificationLocale.Ouvrir(ctx, u);
            return Results.Ok(Dto(u));
        }).AllowAnonymous();

        api.MapPost("/auth/deconnexion", async (HttpContext ctx) =>
        {
            await AuthentificationLocale.Fermer(ctx);
            return Results.NoContent();
        }).AllowAnonymous();

        api.MapGet("/auth/moi", async (ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await db.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == acteur.Id);
            // Compte supprimé depuis l'émission du cookie : la session n'est plus valable.
            return u is null ? Results.Json(new { message = "Session expirée." }, statusCode: 401) : Results.Ok(Dto(u));
        });

        api.MapGet("/utilisateurs", async (MepDbContext db) =>
            (await db.Utilisateurs.AsNoTracking().OrderBy(u => u.NomAffiche).ToListAsync()).Select(Dto));

        api.MapPost("/utilisateurs", async (UtilisateurSaisie corps, ClaimsPrincipal user, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            if (!Acteur.Depuis(user).EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var login = AuthentificationLocale.NormaliserLogin(corps.Login);
            var nom = ServiceMep.Texte(corps.NomAffiche);
            if (login.Length == 0) throw ErreurHttp.Saisie("Le login est obligatoire.");
            if (nom.Length == 0) throw ErreurHttp.Saisie("Le nom affiché est obligatoire.");
            if ((corps.MotDePasse ?? "").Length < 8) throw ErreurHttp.Saisie("Le mot de passe doit contenir au moins 8 caractères.");
            if (await db.Utilisateurs.AnyAsync(u => u.Login == login)) throw ErreurHttp.Saisie("Ce login existe déjà.");
            var nouveau = new Utilisateur { Login = login, NomAffiche = nom, EstAdmin = corps.EstAdmin };
            nouveau.MotDePasseHash = hasher.HashPassword(nouveau, corps.MotDePasse!);
            db.Utilisateurs.Add(nouveau);
            await db.SaveChangesAsync();
            return Results.Created($"/api/utilisateurs/{nouveau.Id}", Dto(nouveau));
        });
    }
}
