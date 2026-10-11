using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Connexion unique : routes publiques (bouton de la page de connexion, liaison depuis le profil) et réglages de la console
/// (/api/admin/sso). Les opérations de réglage sont partagées avec la commande <c>gestion-mep sso</c>.
/// </summary>
public static class SsoEndpoints
{
    public static async Task<ConfigurationSso> Charger(MepDbContext db) =>
        await db.ConfigurationsSso.FirstOrDefaultAsync() ?? throw new InvalidOperationException("Ligne ConfigurationSso absente : migrations non appliquées.");

    public static SsoReglagesDto Dto(ConfigurationSso c, IDataProtectionProvider cles, string? urlRetour) =>
        new(c.Active, c.Fournisseur, c.Annuaire, c.Autorite, c.ClientId, c.SecretChiffre.Length > 0, ConnexionUnique.Secret(c, cles) is not null,
            c.SecretExpireLe, c.CreationAutomatique, c.RoleAdmin, c.Libelle, ConnexionUnique.LibelleEffectif(c), urlRetour, c.ModifieeLe,
            ConnexionUnique.Anomalies(c, cles, pourActiver: true));

    /// <summary>
    /// Enregistre les réglages (secret inchangé si absent, sauf changement d'émetteur ou d'application : il est alors effacé).
    /// Une connexion active doit rester utilisable (422 sinon).
    /// </summary>
    public static async Task Enregistrer(MepDbContext db, IDataProtectionProvider cles, Acteur? acteur, SsoSaisie s, DateTimeOffset maintenant)
    {
        var c = await Charger(db);
        var secretEfface = ConnexionUnique.Appliquer(c, s.Fournisseur, s.Annuaire, s.Autorite, s.ClientId, s.SecretExpireLe, s.CreationAutomatique, s.RoleAdmin, s.Libelle);
        var nouveauSecret = !string.IsNullOrWhiteSpace(s.Secret);
        if (nouveauSecret) ConnexionUnique.PoserSecret(c, cles, s.Secret!);
        else if (secretEfface) c.SecretExpireLe = null;
        var anomalies = ConnexionUnique.Anomalies(c, cles, pourActiver: c.Active);
        if (anomalies.Count > 0) throw ErreurHttp.Regle(anomalies);
        c.ModifieeLe = maintenant;
        var detail = $"Connexion unique réglée : {(c.Fournisseur == FournisseurSso.Microsoft ? $"Microsoft, annuaire {c.Annuaire}" : $"émetteur {c.Autorite}")}, application {c.ClientId}"
            + (nouveauSecret ? ", nouveau secret" : secretEfface ? ", ancien secret effacé (émetteur ou application changés)" : "")
            + (c.CreationAutomatique ? ", création automatique des comptes" : ", comptes déjà liés seulement")
            + (c.RoleAdmin.Length > 0 ? $", rôle administrateur « {c.RoleAdmin} »" : "");
        JournalAdmin.Ecrire(db, acteur, "sso.regle", detail);
        await db.SaveChangesAsync();
    }

    public static async Task ChangerEtat(MepDbContext db, IDataProtectionProvider cles, Acteur? acteur, bool active, DateTimeOffset maintenant)
    {
        var c = await Charger(db);
        if (c.Active == active) return;
        if (active && ConnexionUnique.Anomalies(c, cles, pourActiver: true) is { Count: > 0 } anomalies)
            throw ErreurHttp.Regle(anomalies, "Impossible d'activer la connexion unique : ");
        c.Active = active;
        c.ModifieeLe = maintenant;
        JournalAdmin.Ecrire(db, acteur, active ? "sso.active" : "sso.desactive", active ? "Connexion unique activée" : "Connexion unique désactivée");
        await db.SaveChangesAsync();
    }

    /// <summary>Supprime une liaison : la personne ne se connecte plus par le fournisseur avec ce compte (elle peut le relier de nouveau).</summary>
    public static async Task Delier(MepDbContext db, Acteur? acteur, IdentiteExterne lien)
    {
        db.IdentitesExternes.Remove(lien);
        JournalAdmin.Ecrire(db, acteur, "sso.delie", $"Liaison de {JournalAdmin.Compte(lien.Utilisateur!)} avec {lien.Identifiant} supprimée");
        await db.SaveChangesAsync();
    }

    public static IQueryable<SsoIdentiteDto> Identites(MepDbContext db) =>
        db.IdentitesExternes.AsNoTracking().OrderBy(x => x.Utilisateur!.NomAffiche)
            .Select(x => new SsoIdentiteDto(x.Id, x.UtilisateurId, x.Utilisateur!.Login, x.Utilisateur.NomAffiche, x.Identifiant, x.CreeeLe, x.DerniereConnexion));

    public static void MapSso(this RouteGroupBuilder api)
    {
        api.MapGet("/auth/options", async (ReglagesSso reglages) =>
        {
            var c = await reglages.Recharger();
            return new OptionsConnexionDto(c.Active, c.Active ? ConnexionUnique.LibelleEffectif(c) : null);
        }).AllowAnonymous();

        // Navigation du navigateur (pas un appel fetch) : renvoie vers le fournisseur. « lier » : jeton obtenu par POST /auth/sso/lier.
        api.MapGet("/auth/sso/connexion", async (string? lier, HttpContext ctx, ClaimsPrincipal user, ReglagesSso reglages, IDataProtectionProvider cles, ILogger<ReglagesSso> journal) =>
        {
            if (!(await reglages.Recharger()).Active) return Results.Redirect("/connexion?erreur=sso-inactif");
            var proprietes = new AuthenticationProperties { RedirectUri = "/" };
            if (lier is not null)
            {
                var id = ConnexionUnique.LireJetonLiaison(cles, lier);
                if (id is null || user.FindFirstValue(ClaimTypes.NameIdentifier) != id.ToString()) return Results.Redirect("/profil?sso=sso-session");
                proprietes.Items[ConnexionUnique.ProprieteLier] = id.ToString();
                proprietes.RedirectUri = "/profil?sso=lie";
            }
            try
            {
                await ctx.ChallengeAsync(ConnexionUnique.Schema, proprietes);
                return Results.Empty;
            }
            catch (InvalidOperationException e) when (!ctx.Response.HasStarted)
            {
                // Fournisseur injoignable (document de découverte illisible) : message sur la page de connexion plutôt qu'une erreur brute.
                journal.LogWarning("Connexion unique : départ vers le fournisseur impossible : {Message}", e.Message);
                return Results.Redirect(lier is null ? "/connexion?erreur=sso-injoignable" : "/profil?sso=sso-injoignable");
            }
        }).AllowAnonymous();

        api.MapGet("/auth/sso/etat", async (ClaimsPrincipal user, MepDbContext db, ReglagesSso reglages) =>
        {
            var c = await reglages.Recharger();
            var id = Acteur.Depuis(user).Id;
            var lien = await db.IdentitesExternes.AsNoTracking().Where(x => x.UtilisateurId == id).OrderByDescending(x => x.DerniereConnexion).FirstOrDefaultAsync();
            var aMotDePasse = await db.Utilisateurs.AnyAsync(u => u.Id == id && u.MotDePasseHash != "");
            return new SsoProfilDto(c.Active, c.Active ? ConnexionUnique.LibelleEffectif(c) : null, lien?.Identifiant, lien is not null && aMotDePasse);
        });

        api.MapPost("/auth/sso/lier", async (ClaimsPrincipal user, ReglagesSso reglages, IDataProtectionProvider cles) =>
        {
            if (!(await reglages.Recharger()).Active) throw ErreurHttp.Regle(["La connexion unique n'est pas activée."]);
            var jeton = ConnexionUnique.JetonLiaison(cles, Acteur.Depuis(user).Id);
            return new SsoLiaisonDto($"/api/auth/sso/connexion?lier={Uri.EscapeDataString(jeton)}");
        });

        // Un compte créé par le fournisseur n'a pas de mot de passe : le délier le rendrait inaccessible.
        api.MapDelete("/auth/sso/lien", async (ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var u = await db.Utilisateurs.FirstOrDefaultAsync(x => x.Id == acteur.Id) ?? throw new ErreurHttp(401, "Session expirée.");
            if (u.MotDePasseHash.Length == 0)
                throw ErreurHttp.Regle(["Ce compte n'a pas de mot de passe : le délier vous empêcherait de vous connecter. Demandez à un administrateur."]);
            foreach (var lien in await db.IdentitesExternes.Include(x => x.Utilisateur).Where(x => x.UtilisateurId == u.Id).ToListAsync())
                await Delier(db, acteur, lien);
            return Results.NoContent();
        });

        var admin = api.MapGroup("/admin/sso").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapGet("", async (HttpRequest requete, MepDbContext db, IDataProtectionProvider cles, IConfiguration config) =>
            Dto(await db.ConfigurationsSso.AsNoTracking().FirstAsync(), cles, ConnexionUnique.UrlRetour(config, requete)));

        admin.MapPut("", async (SsoSaisie s, HttpRequest requete, ClaimsPrincipal user, MepDbContext db, IDataProtectionProvider cles, IConfiguration config, TimeProvider temps, ReglagesSso reglages) =>
        {
            await Enregistrer(db, cles, Acteur.Depuis(user), s, temps.GetUtcNow());
            await reglages.Recharger();
            return Dto(await Charger(db), cles, ConnexionUnique.UrlRetour(config, requete));
        });

        admin.MapPost("/activer", async (HttpRequest requete, ClaimsPrincipal user, MepDbContext db, IDataProtectionProvider cles, IConfiguration config, TimeProvider temps, ReglagesSso reglages) =>
        {
            await ChangerEtat(db, cles, Acteur.Depuis(user), true, temps.GetUtcNow());
            await reglages.Recharger();
            return Dto(await Charger(db), cles, ConnexionUnique.UrlRetour(config, requete));
        });

        admin.MapPost("/desactiver", async (HttpRequest requete, ClaimsPrincipal user, MepDbContext db, IDataProtectionProvider cles, IConfiguration config, TimeProvider temps, ReglagesSso reglages) =>
        {
            await ChangerEtat(db, cles, Acteur.Depuis(user), false, temps.GetUtcNow());
            await reglages.Recharger();
            return Dto(await Charger(db), cles, ConnexionUnique.UrlRetour(config, requete));
        });

        admin.MapPost("/tester", async (MepDbContext db, IDataProtectionProvider cles, IHttpClientFactory http, CancellationToken ct) =>
        {
            using var client = http.CreateClient(nameof(ConnexionUnique));
            var verifications = await ConnexionUnique.Tester(await Charger(db), cles, client, ct);
            return new SsoTestDto(verifications.All(v => v.Ok), verifications.Select(v => new SsoVerificationDto(v.Libelle, v.Ok, v.Detail)).ToList());
        });

        admin.MapGet("/identites", async (MepDbContext db) => await Identites(db).ToListAsync());

        admin.MapDelete("/identites/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var lien = await db.IdentitesExternes.Include(x => x.Utilisateur).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw ErreurHttp.NonTrouve("Liaison introuvable.");
            await Delier(db, Acteur.Depuis(user), lien);
            return Results.NoContent();
        });
    }
}
