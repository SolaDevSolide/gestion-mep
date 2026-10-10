using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Console d'administration : comptes et journal d'administration (groupe /api/admin, administrateurs seulement).</summary>
public static class AdminEndpoints
{
    public const string MessageDernierAdmin = "Impossible de désactiver le dernier administrateur actif.";

    private static UtilisateurDto Dto(Utilisateur u) => MepMapping.VersDto(u);

    private static async Task<Utilisateur> Charger(MepDbContext db, Guid id) =>
        await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Compte introuvable.");

    /// <summary>422 si le compte est le dernier administrateur actif (on ne peut plus administrer l'application sans lui).</summary>
    public static async Task ExigerAutreAdmin(MepDbContext db, Utilisateur u, string message)
    {
        if (u.EstAdmin && u.Actif && !await db.Utilisateurs.AnyAsync(x => x.EstAdmin && x.Actif && x.Id != u.Id))
            throw ErreurHttp.Regle([message]);
    }

    private static string Nom(string? nomAffiche)
    {
        var nom = ServiceMep.Texte(nomAffiche);
        return nom.Length == 0 ? throw ErreurHttp.Saisie("Le nom affiché est obligatoire.") : nom;
    }

    public static void MapAdmin(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapGet("/utilisateurs", async (MepDbContext db) =>
            (await db.Utilisateurs.AsNoTracking().OrderBy(u => u.NomAffiche).ToListAsync()).Select(Dto));

        // Le mot de passe provisoire est renvoyé une seule fois : l'administrateur le transmet, la personne le change à la connexion.
        admin.MapPost("/utilisateurs", async (CompteSaisie c, ClaimsPrincipal user, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var login = AuthentificationLocale.NormaliserLogin(c.Login);
            if (login.Length == 0) throw ErreurHttp.Saisie("Le login est obligatoire.");
            var u = new Utilisateur { Login = login, NomAffiche = Nom(c.NomAffiche), EstAdmin = c.EstAdmin, Role = ServiceMep.Texte(c.Role), Contact = ServiceMep.Texte(c.Contact) };
            if (await db.Utilisateurs.AnyAsync(x => x.Login == login)) throw ErreurHttp.Saisie("Ce login existe déjà.");
            var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, hasher);
            db.Utilisateurs.Add(u);
            JournalAdmin.Ecrire(db, Acteur.Depuis(user), "compte.cree", $"Compte {JournalAdmin.Compte(u)} créé{(u.EstAdmin ? ", administrateur" : "")}");
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/utilisateurs/{u.Id}", new CompteEtMotDePasseDto(Dto(u), mdp));
        });

        admin.MapPut("/utilisateurs/{id:guid}", async (Guid id, CompteModification c, ClaimsPrincipal user, MepDbContext db) =>
        {
            var u = await Charger(db, id);
            var nom = Nom(c.NomAffiche);
            if (u.EstAdmin && !c.EstAdmin) await ExigerAutreAdmin(db, u, "Impossible de retirer le rôle administrateur au dernier administrateur actif.");
            var changements = new List<string>();
            if (nom != u.NomAffiche) changements.Add($"nom « {nom} »");
            if (c.EstAdmin != u.EstAdmin) changements.Add(c.EstAdmin ? "devient administrateur" : "n'est plus administrateur");
            var role = ServiceMep.Texte(c.Role);
            var contact = ServiceMep.Texte(c.Contact);
            if (role != u.Role) changements.Add($"rôle « {role} »");
            if (contact != u.Contact) changements.Add("contact modifié");
            u.NomAffiche = nom;
            u.EstAdmin = c.EstAdmin;
            u.Role = role;
            u.Contact = contact;
            if (changements.Count > 0)
                JournalAdmin.Ecrire(db, Acteur.Depuis(user), "compte.modifie", $"Compte {JournalAdmin.Compte(u)} : {string.Join(", ", changements)}");
            await db.SaveChangesAsync();
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/desactiver", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await Charger(db, id);
            if (u.Id == acteur.Id) throw ErreurHttp.Regle(["Vous ne pouvez pas désactiver votre propre compte."]);
            await ExigerAutreAdmin(db, u, MessageDernierAdmin);
            if (u.Actif)
            {
                u.Actif = false;
                u.TamponSecurite = Guid.NewGuid();
                JournalAdmin.Ecrire(db, acteur, "compte.desactive", $"Compte {JournalAdmin.Compte(u)} désactivé");
                await db.SaveChangesAsync();
            }
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/reactiver", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var u = await Charger(db, id);
            if (!u.Actif)
            {
                u.Actif = true;
                JournalAdmin.Ecrire(db, Acteur.Depuis(user), "compte.reactive", $"Compte {JournalAdmin.Compte(u)} réactivé");
                await db.SaveChangesAsync();
            }
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/reinitialiser-mot-de-passe", async (Guid id, ClaimsPrincipal user, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var u = await Charger(db, id);
            var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, hasher);
            JournalAdmin.Ecrire(db, Acteur.Depuis(user), "compte.mdp-reinitialise", $"Mot de passe de {JournalAdmin.Compte(u)} réinitialisé");
            await db.SaveChangesAsync();
            return Results.Ok(new CompteEtMotDePasseDto(Dto(u), mdp));
        });

        admin.MapGet("/journal", async (int? limite, MepDbContext db) =>
        {
            var n = Math.Clamp(limite ?? 200, 1, 1000);
            return (await db.JournalAdmin.AsNoTracking().OrderByDescending(e => e.Id).Take(n).ToListAsync())
                .Select(e => new EvenementAdminDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail));
        });
    }
}
