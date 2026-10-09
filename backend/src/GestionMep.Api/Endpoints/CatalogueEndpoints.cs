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
        var minuscule = n.ToLower();
        if (await db.SystemesCatalogue.AnyAsync(s => s.Nom.ToLower() == minuscule && s.Id != sauf))
            throw ErreurHttp.Saisie($"Un système nommé « {n} » existe déjà dans le catalogue.");
        return n;
    }

    public static void MapCatalogue(this RouteGroupBuilder api)
    {
        api.MapGet("/catalogue/systemes", async (MepDbContext db) =>
            (await db.SystemesCatalogue.AsNoTracking().OrderBy(s => s.Nom).ToListAsync()).Select(Dto));

        api.MapPost("/catalogue/systemes", async (SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var s = new SystemeCatalogue
            {
                Nom = await NomUnique(db, c.Nom, null), Description = ServiceMep.Texte(c.Description),
                Cible = ServiceMep.Texte(c.Cible), Actif = c.Actif ?? true,
            };
            db.SystemesCatalogue.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/catalogue/systemes/{s.Id}", Dto(s));
        });

        api.MapPut("/catalogue/systemes/{id:guid}", async (Guid id, SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var s = await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
            s.Nom = await NomUnique(db, c.Nom, id);
            s.Description = ServiceMep.Texte(c.Description);
            s.Cible = ServiceMep.Texte(c.Cible);
            s.Actif = c.Actif ?? s.Actif;
            await db.SaveChangesAsync();
            return Results.Ok(Dto(s));
        });

        api.MapDelete("/catalogue/systemes/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var s = await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
            // Utilisé par une MEP ou par une résolution type : on le désactive au lieu de le supprimer.
            var utilise = await db.Systemes.AnyAsync(x => x.CatalogueId == id) || await db.ResolutionsTypes.AnyAsync(r => r.SystemeCatalogueId == id);
            if (utilise) s.Actif = false;
            else db.SystemesCatalogue.Remove(s);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/meps/{id:guid}/systemes/depuis-catalogue", async (Guid id, DepuisCatalogueSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            if (c.CatalogueId is null) throw ErreurHttp.Saisie("Le système du catalogue est obligatoire.");
            var cat = await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CatalogueId)
                ?? throw ErreurHttp.Saisie("Système du catalogue inconnu.");
            if (!cat.Actif) throw new RegleMetierException($"Le système {cat.Nom} est désactivé dans le catalogue.");
            if (mep.Systemes.Any(x => x.CatalogueId == cat.Id)) throw new RegleMetierException($"Le système {cat.Nom} est déjà présent dans cette MEP.");
            mep.Systemes.Add(new Systeme { MepId = mep.Id, Nom = cat.Nom, Cible = cat.Cible, Impact = ServiceMep.Texte(c.Impact), CatalogueId = cat.Id });
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "systeme.ajoute", $"Système ajouté depuis le catalogue : {cat.Nom}"));
        });
    }
}
