using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Catalogue global des systèmes (lecture : tout connecté ; écriture : admin) et ajout d'un système du catalogue à une MEP.</summary>
public static class CatalogueEndpoints
{
    private static SystemeCatalogueDto Dto(SystemeCatalogue s) => new(s.Id, s.Nom, s.Description, s.Cible, s.Actif);

    private static void ExigerAdmin(ClaimsPrincipal user)
    {
        if (!Acteur.Depuis(user).EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
    }

    private static async Task<string> NomUnique(MepDbContext db, string? nom, Guid? sauf)
    {
        var n = ServiceMep.Texte(nom);
        if (n.Length == 0) throw ErreurHttp.Saisie("Le nom est obligatoire.");
        var minuscule = n.ToLowerInvariant();
        // Requête traduite en SQL : seul ToLower() devient lower() ; ni ToLowerInvariant() ni string.Equals(…, StringComparison)
        // ne sont traduisibles par EF Core.
#pragma warning disable CA1304, CA1311, CA1862
        if (await db.SystemesCatalogue.AnyAsync(s => s.Nom.ToLower() == minuscule && s.Id != sauf))
#pragma warning restore CA1304, CA1311, CA1862
            throw ErreurHttp.Saisie($"Un système nommé « {n} » existe déjà dans le catalogue.");
        return n;
    }

    // ---------- Opérations partagées avec « gestion-mep systemes » (acteur null : ligne de commande) ----------

    public static async Task<List<SystemeCatalogueDto>> Lister(MepDbContext db) =>
        (await db.SystemesCatalogue.AsNoTracking().OrderBy(s => s.Nom).ToListAsync()).Select(Dto).ToList();

    public static async Task<SystemeCatalogueDto> Creer(MepDbContext db, Acteur? acteur, SystemeCatalogueSaisie c)
    {
        var s = new SystemeCatalogue
        {
            Nom = await NomUnique(db, c.Nom, null), Description = ServiceMep.Texte(c.Description),
            Cible = ServiceMep.Texte(c.Cible), Actif = c.Actif ?? true,
        };
        db.SystemesCatalogue.Add(s);
        JournalAdmin.Ecrire(db, acteur, "systeme.cree", $"Système « {s.Nom} » ajouté au catalogue{(s.Actif ? "" : " (désactivé)")}");
        await db.SaveChangesAsync();
        return Dto(s);
    }

    public static async Task<SystemeCatalogueDto> Modifier(MepDbContext db, Acteur? acteur, Guid id, SystemeCatalogueSaisie c)
    {
        var s = await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
        var ancien = s.Nom;
        s.Nom = await NomUnique(db, c.Nom, id);
        s.Description = ServiceMep.Texte(c.Description);
        s.Cible = ServiceMep.Texte(c.Cible);
        s.Actif = c.Actif ?? s.Actif;
        JournalAdmin.Ecrire(db, acteur, "systeme.modifie",
            $"Système « {ancien} » du catalogue modifié{(ancien != s.Nom ? $", renommé « {s.Nom} »" : "")}{(s.Actif ? "" : " (désactivé)")}");
        await db.SaveChangesAsync();
        return Dto(s);
    }

    /// <summary>Supprime le système ; s'il est utilisé par une MEP, une résolution type ou un modèle de recette, le désactive (renvoie false).</summary>
    public static async Task<bool> Supprimer(MepDbContext db, Acteur? acteur, Guid id)
    {
        var s = await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
        // Utilisé par une MEP, une résolution type ou un modèle de recette : on le désactive au lieu de le supprimer.
        var utilise = await db.Systemes.AnyAsync(x => x.CatalogueId == id) || await db.ResolutionsTypes.AnyAsync(r => r.SystemeCatalogueIds.Contains(id))
            || await db.ModelesRecette.AnyAsync(r => r.SystemeCatalogueId == id);
        if (utilise)
        {
            s.Actif = false;
            JournalAdmin.Ecrire(db, acteur, "systeme.desactive", $"Système « {s.Nom} » du catalogue désactivé (utilisé, donc conservé)");
        }
        else
        {
            db.SystemesCatalogue.Remove(s);
            JournalAdmin.Ecrire(db, acteur, "systeme.supprime", $"Système « {s.Nom} » supprimé du catalogue");
        }
        await db.SaveChangesAsync();
        return !utilise;
    }

    public static void MapCatalogue(this RouteGroupBuilder api)
    {
        api.MapGet("/catalogue/systemes", async (MepDbContext db) => (await Lister(db)).AsEnumerable());

        api.MapPost("/catalogue/systemes", async (SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var s = await Creer(db, Acteur.Depuis(user), c);
            return Results.Created($"/api/catalogue/systemes/{s.Id}", s);
        });

        api.MapPut("/catalogue/systemes/{id:guid}", async (Guid id, SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            return Results.Ok(await Modifier(db, Acteur.Depuis(user), id, c));
        });

        api.MapDelete("/catalogue/systemes/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            await Supprimer(db, Acteur.Depuis(user), id);
            return Results.NoContent();
        });

        api.MapPost("/meps/{id:guid}/systemes/depuis-catalogue", async (Guid id, DepuisCatalogueSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            if (c.CatalogueId is null) throw ErreurHttp.Saisie("Le système du catalogue est obligatoire.");
            var cat = await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CatalogueId)
                ?? throw ErreurHttp.Saisie("Système du catalogue inconnu.");
            if (!cat.Actif) throw new RegleMetierException($"Le système {cat.Nom} est désactivé dans le catalogue.");
            if (mep.Systemes.Any(x => x.CatalogueId == cat.Id)) throw new RegleMetierException($"Le système {cat.Nom} est déjà présent dans cette MEP.");
            mep.Systemes.Add(new Systeme { MepId = mep.Id, Nom = cat.Nom, Cible = cat.Cible, Impact = ServiceMep.Texte(c.Impact), CatalogueId = cat.Id });
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "systeme.ajoute", $"Système ajouté depuis le catalogue : {cat.Nom}");
        })));
    }
}
