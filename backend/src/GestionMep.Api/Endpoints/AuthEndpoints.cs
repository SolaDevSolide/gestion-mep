using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Securite;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

public static class AuthEndpoints
{
    private static UtilisateurDto Dto(Utilisateur u) => MepMapping.VersDto(u);

    /// <summary>
    /// Applique un profil (nom affiché facultatif, rôle, contact, expertises) : textes rognés, systèmes du catalogue existants
    /// et sans doublon (400 sinon).
    /// </summary>
    private static async Task AppliquerProfil(Utilisateur u, ProfilSaisie c, MepDbContext db)
    {
        if (c.NomAffiche is not null)
        {
            var nom = ServiceMep.Texte(c.NomAffiche);
            if (nom.Length == 0) throw ErreurHttp.Saisie("Le nom affiché est obligatoire.");
            await AuthentificationLocale.ExigerNomDisponible(db, nom, u.Id);
            u.NomAffiche = nom;
        }
        var expertises = (c.Expertises ?? []).Select(x => x.SystemeCatalogueId is { } s
            ? new Expertise { SystemeCatalogueId = s, Detail = ServiceMep.Texte(x.Detail) }
            : throw ErreurHttp.Saisie("Expertise : le système du catalogue est obligatoire.")).ToList();
        if (expertises.GroupBy(x => x.SystemeCatalogueId).Any(g => g.Count() > 1))
            throw ErreurHttp.Saisie("Expertise : un système du catalogue n'apparaît qu'une fois.");
        var ids = expertises.Select(x => x.SystemeCatalogueId).ToList();
        if (await db.SystemesCatalogue.CountAsync(s => ids.Contains(s.Id)) != ids.Count)
            throw ErreurHttp.Saisie("Expertise : système du catalogue inconnu.");
        u.Role = ServiceMep.Texte(c.Role);
        u.Contact = ServiceMep.Texte(c.Contact);
        u.Expertises = expertises;
    }

    public static void MapAuth(this RouteGroupBuilder api)
    {
        // Limites : saisie de taille raisonnable, tentatives par adresse IP (politique « connexion »), blocage progressif
        // par login et adresse, nombre de contrôles simultanés (voir LimiteurConnexion).
        api.MapPost("/auth/connexion", async (ConnexionSaisie corps, HttpContext ctx, MepDbContext db, IPasswordHasher<Utilisateur> hasher, LimiteurConnexion limiteur) =>
        {
            var refus = Results.Json(new { message = "Identifiant ou mot de passe incorrect." }, statusCode: 401);
            var login = AuthentificationLocale.NormaliserLogin(corps.Login);
            if (!LimiteurConnexion.SaisieAdmissible(login, corps.MotDePasse)) return refus;
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue";
            if (limiteur.BloqueJusqua(login, ip) is not null)
                return Results.Json(new { message = "Trop d'échecs de connexion pour ce compte. Réessayez dans quelques minutes." }, statusCode: 429);
            var (execute, u) = await limiteur.Calculer(() => AuthentificationLocale.Verifier(db, hasher, login, corps.MotDePasse), ctx.RequestAborted);
            if (!execute)
                return Results.Json(new { message = "Serveur très sollicité. Réessayez dans un instant." }, statusCode: 503);
            if (u is null)
            {
                if (limiteur.Echec(login, ip) && await db.Utilisateurs.FirstOrDefaultAsync(x => x.Login == login) is { } cible)
                {
                    JournalAdmin.Ecrire(db, null, "connexion.echecs",
                        $"{LimiteurConnexion.SeuilAlerte} échecs de connexion en moins d'une heure sur le compte {JournalAdmin.Compte(cible)} (dernier depuis {ip})", JournalAdmin.AuteurApplication);
                    await db.SaveChangesAsync();
                }
                return refus;
            }
            limiteur.Reussite(login, ip);
            if (!u.Actif) return Results.Json(new { message = "Ce compte est désactivé. Contactez un administrateur." }, statusCode: 403);
            await AuthentificationLocale.Ouvrir(ctx, u);
            return Results.Ok(Dto(u));
        }).AllowAnonymous().RequireRateLimiting(ProtectionsHttp.PolitiqueConnexion);

        // Changement de son propre mot de passe (y compris le provisoire) ; les autres sessions du compte sont fermées.
        api.MapPost("/auth/mot-de-passe", async (ChangementMotDePasseSaisie corps, ClaimsPrincipal user, HttpContext ctx, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == acteur.Id) ?? throw new ErreurHttp(401, "Session expirée.");
            if (hasher.VerifyHashedPassword(u, u.MotDePasseHash, corps.Ancien ?? "") == PasswordVerificationResult.Failed)
                throw ErreurHttp.Regle(["Le mot de passe actuel est incorrect."]);
            AuthentificationLocale.ValiderNouveauMotDePasse(corps.Nouveau, corps.Ancien);
            u.MotDePasseHash = hasher.HashPassword(u, corps.Nouveau!);
            u.DoitChangerMotDePasse = false;
            u.TamponSecurite = Guid.NewGuid();
            JournalAdmin.Ecrire(db, acteur, "compte.mdp-change", $"{JournalAdmin.Compte(u)} a changé son mot de passe");
            await db.SaveChangesAsync();
            await AuthentificationLocale.Ouvrir(ctx, u);
            return Results.Ok(Dto(u));
        }).RequireRateLimiting(ProtectionsHttp.PolitiqueConnexion);

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
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var login = AuthentificationLocale.NormaliserLogin(corps.Login);
            var nom = ServiceMep.Texte(corps.NomAffiche);
            AuthentificationLocale.ExigerLoginValide(login);
            if (nom.Length == 0) throw ErreurHttp.Saisie("Le nom affiché est obligatoire.");
            await AuthentificationLocale.ExigerNomDisponible(db, nom, null);
            if ((corps.MotDePasse ?? "").Length < 8) throw ErreurHttp.Saisie("Le mot de passe doit contenir au moins 8 caractères.");
            if (await db.Utilisateurs.AnyAsync(u => u.Login == login)) throw ErreurHttp.Saisie("Ce login existe déjà.");
            var nouveau = new Utilisateur
            {
                Login = login, NomAffiche = nom, EstAdmin = corps.EstAdmin, Role = ServiceMep.Texte(corps.Role), Contact = ServiceMep.Texte(corps.Contact),
            };
            nouveau.MotDePasseHash = hasher.HashPassword(nouveau, corps.MotDePasse!);
            db.Utilisateurs.Add(nouveau);
            JournalAdmin.Ecrire(db, acteur, "compte.cree", $"Compte {JournalAdmin.Compte(nouveau)} créé{(nouveau.EstAdmin ? ", administrateur" : "")}");
            await db.SaveChangesAsync();
            return Results.Created($"/api/utilisateurs/{nouveau.Id}", Dto(nouveau));
        });

        // Chacun gère son propre profil ; le nom affiché du cookie est rafraîchi.
        api.MapPut("/utilisateurs/moi/profil", async (ProfilSaisie corps, ClaimsPrincipal user, HttpContext ctx, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == acteur.Id)
                ?? throw new ErreurHttp(401, "Session expirée.");
            var ancien = u.NomAffiche;
            await AppliquerProfil(u, corps, db);
            // Le nom affiché signe les journaux : son changement est tracé avec le login, qui ne change pas.
            if (u.NomAffiche != ancien)
                JournalAdmin.Ecrire(db, acteur, "compte.renomme", $"{ancien} ({u.Login}) s'affiche désormais « {u.NomAffiche} »");
            await db.SaveChangesAsync();
            await AuthentificationLocale.Ouvrir(ctx, u);
            return Results.Ok(Dto(u));
        });

        api.MapPut("/utilisateurs/{uid:guid}/profil", async (Guid uid, ProfilSaisie corps, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == uid) ?? throw ErreurHttp.NonTrouve("Utilisateur introuvable.");
            await AppliquerProfil(u, corps, db);
            JournalAdmin.Ecrire(db, acteur, "compte.modifie", $"Profil de {JournalAdmin.Compte(u)} modifié");
            await db.SaveChangesAsync();
            return Results.Ok(Dto(u));
        });
    }
}
