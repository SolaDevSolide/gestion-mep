using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Console d'administration : comptes et journal d'administration (groupe /api/admin, administrateurs seulement).
/// Les opérations sont partagées avec la commande <c>gestion-mep comptes</c> (acteur null : « Ligne de commande »).
/// </summary>
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

    /// <summary>
    /// Crée un compte avec un mot de passe provisoire (renvoyé une seule fois) : login valide et libre, nom affiché
    /// qui n'imite aucun autre compte.
    /// </summary>
    public static async Task<(Utilisateur Compte, string MotDePasse)> Creer(MepDbContext db, Acteur? acteur, CompteSaisie c, IPasswordHasher<Utilisateur> hasher)
    {
        var login = AuthentificationLocale.NormaliserLogin(c.Login);
        AuthentificationLocale.ExigerLoginValide(login);
        await AuthentificationLocale.ExigerNomDisponible(db, Nom(c.NomAffiche), null);
        var u = new Utilisateur { Login = login, NomAffiche = Nom(c.NomAffiche), EstAdmin = c.EstAdmin, Role = ServiceMep.Texte(c.Role), Contact = ServiceMep.Texte(c.Contact) };
        if (await db.Utilisateurs.AnyAsync(x => x.Login == login)) throw ErreurHttp.Saisie("Ce login existe déjà.");
        var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, hasher);
        db.Utilisateurs.Add(u);
        JournalAdmin.Ecrire(db, acteur, "compte.cree", $"Compte {JournalAdmin.Compte(u)} créé{(u.EstAdmin ? ", administrateur" : "")}");
        await db.SaveChangesAsync();
        return (u, mdp);
    }

    /// <summary>Nom affiché, rôle administrateur, rôle et contact ; renvoie la liste des changements (vide : rien n'a changé).</summary>
    public static async Task<List<string>> Modifier(MepDbContext db, Acteur? acteur, Utilisateur u, CompteModification c)
    {
        var nom = Nom(c.NomAffiche);
        if (u.EstAdmin && !c.EstAdmin) await ExigerAutreAdmin(db, u, "Impossible de retirer le rôle administrateur au dernier administrateur actif.");
        var changements = new List<string>();
        if (nom != u.NomAffiche)
        {
            await AuthentificationLocale.ExigerNomDisponible(db, nom, u.Id);
            changements.Add($"nom « {nom} »");
        }
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
            JournalAdmin.Ecrire(db, acteur, "compte.modifie", $"Compte {JournalAdmin.Compte(u)} : {string.Join(", ", changements)}");
        await db.SaveChangesAsync();
        return changements;
    }

    /// <summary>Désactive le compte et ferme ses sessions (sauf le dernier administrateur actif) ; renvoie false s'il l'était déjà.</summary>
    public static async Task<bool> Desactiver(MepDbContext db, Acteur? acteur, Utilisateur u)
    {
        await ExigerAutreAdmin(db, u, MessageDernierAdmin);
        if (!u.Actif) return false;
        u.Actif = false;
        u.TamponSecurite = Guid.NewGuid();
        JournalAdmin.Ecrire(db, acteur, "compte.desactive", $"Compte {JournalAdmin.Compte(u)} désactivé");
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Réactive le compte ; renvoie false s'il était déjà actif.</summary>
    public static async Task<bool> Reactiver(MepDbContext db, Acteur? acteur, Utilisateur u)
    {
        if (u.Actif) return false;
        u.Actif = true;
        JournalAdmin.Ecrire(db, acteur, "compte.reactive", $"Compte {JournalAdmin.Compte(u)} réactivé");
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Nouveau mot de passe provisoire (sessions fermées), renvoyé une seule fois.</summary>
    public static async Task<string> ReinitialiserMotDePasse(MepDbContext db, Acteur? acteur, Utilisateur u, IPasswordHasher<Utilisateur> hasher)
    {
        var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, hasher);
        JournalAdmin.Ecrire(db, acteur, "compte.mdp-reinitialise", $"Mot de passe de {JournalAdmin.Compte(u)} réinitialisé");
        await db.SaveChangesAsync();
        return mdp;
    }

    /// <summary>Événements du journal d'administration, du plus récent au plus ancien, depuis une date facultative.</summary>
    public static Task<List<EvenementAdminDto>> Journal(MepDbContext db, int limite, DateTimeOffset? depuis = null) =>
        db.JournalAdmin.AsNoTracking().Where(e => depuis == null || e.Quand >= depuis).OrderByDescending(e => e.Id).Take(limite)
            .Select(e => new EvenementAdminDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail,
                db.Utilisateurs.Where(u => u.Id == e.UtilisateurId).Select(u => u.Login).FirstOrDefault()))
            .ToListAsync();

    public static void MapAdmin(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapGet("/utilisateurs", async (MepDbContext db) =>
            (await db.Utilisateurs.AsNoTracking().OrderBy(u => u.NomAffiche).ToListAsync()).Select(Dto));

        // Le mot de passe provisoire est renvoyé une seule fois : l'administrateur le transmet, la personne le change à la connexion.
        admin.MapPost("/utilisateurs", async (CompteSaisie c, ClaimsPrincipal user, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var (u, mdp) = await Creer(db, Acteur.Depuis(user), c, hasher);
            return Results.Created($"/api/admin/utilisateurs/{u.Id}", new CompteEtMotDePasseDto(Dto(u), mdp));
        });

        admin.MapPut("/utilisateurs/{id:guid}", async (Guid id, CompteModification c, ClaimsPrincipal user, MepDbContext db) =>
        {
            var u = await Charger(db, id);
            await Modifier(db, Acteur.Depuis(user), u, c);
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/desactiver", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await Charger(db, id);
            if (u.Id == acteur.Id) throw ErreurHttp.Regle(["Vous ne pouvez pas désactiver votre propre compte."]);
            await Desactiver(db, acteur, u);
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/reactiver", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var u = await Charger(db, id);
            await Reactiver(db, Acteur.Depuis(user), u);
            return Results.Ok(Dto(u));
        });

        admin.MapPost("/utilisateurs/{id:guid}/reinitialiser-mot-de-passe", async (Guid id, ClaimsPrincipal user, MepDbContext db, IPasswordHasher<Utilisateur> hasher) =>
        {
            var u = await Charger(db, id);
            var mdp = await ReinitialiserMotDePasse(db, Acteur.Depuis(user), u, hasher);
            return Results.Ok(new CompteEtMotDePasseDto(Dto(u), mdp));
        });

        admin.MapGet("/journal", async (int? limite, MepDbContext db) => await Journal(db, Math.Clamp(limite ?? 200, 1, 1000)));
    }
}
