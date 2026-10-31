using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Catalogue global des systèmes (lecture : tout connecté ; écriture : admin) : fiche de chaque système (environnements,
/// catégories et objets), catégories par défaut, et ajout d'un système du catalogue à une MEP.
/// </summary>
public static class CatalogueEndpoints
{
    public static SystemeCatalogueDto Dto(SystemeCatalogue s) => new(s.Id, s.Nom, s.Description, s.Cible, s.Actif,
        s.Environnements.Select(e => new EnvironnementCatalogueDto(e.Nom, e.Cible, e.Production)).ToList(), [.. s.Categories], s.Objets.Count,
        s.Source is { } src ? new SourceNoosphereDto(src.LiaisonId, src.ApplicationId, src.Url, src.Domaine, src.Equipe, src.Cycle, src.DerniereSynchro, src.Absente) : null);

    /// <summary>Système suivi depuis Noosphere : nom et description y sont gérés (changements par « Ouvrir dans Noosphere »).</summary>
    public const string MessageGereParNoosphere = "Ce système est géré par Noosphere : son nom et sa description se modifient dans Noosphere (« Ouvrir dans Noosphere »).";

    public static ObjetCatalogueDto Dto(ObjetCatalogue o) => new(o.Id, o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision);

    /// <summary>Objets de la fiche, dans l'ordre des catégories puis par nom.</summary>
    public static List<ObjetCatalogueDto> Objets(SystemeCatalogue s) =>
        s.Objets.OrderBy(o => s.Categories.IndexOf(o.Categorie)).ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase).Select(Dto).ToList();

    private static async Task<SystemeCatalogue> Trouver(MepDbContext db, Guid id) =>
        await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");

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
        if (s.Source is not null)
        {
            // Géré par Noosphere : nom et description en lecture seule (les renvoyer inchangés reste accepté).
            if ((c.Nom is not null && ServiceMep.Texte(c.Nom) != s.Nom) || (c.Description is not null && ServiceMep.Texte(c.Description) != s.Description))
                throw ErreurHttp.Saisie(MessageGereParNoosphere);
        }
        else
        {
            s.Nom = await NomUnique(db, c.Nom, id);
            s.Description = ServiceMep.Texte(c.Description);
        }
        s.Cible = ServiceMep.Texte(c.Cible);
        s.Actif = c.Actif ?? s.Actif;
        JournalAdmin.Ecrire(db, acteur, "systeme.modifie",
            $"Système « {ancien} » du catalogue modifié{(ancien != s.Nom ? $", renommé « {s.Nom} »" : "")}{(s.Actif ? "" : " (désactivé)")}");
        await db.SaveChangesAsync();
        return Dto(s);
    }

    /// <summary>Remplace les environnements de la fiche (les MEP gardent ceux déjà choisis).</summary>
    public static async Task<SystemeCatalogueDto> DefinirEnvironnements(MepDbContext db, Acteur? acteur, Guid id, EnvironnementsCatalogueSaisie c)
    {
        var s = await Trouver(db, id);
        try { Regles.DefinirEnvironnementsCatalogue(s, (c.Environnements ?? []).Select(e => (e.Nom, e.Cible, e.Production))); }
        catch (RegleMetierException ex) { throw ErreurHttp.Saisie(ex.Message); }
        s.Version++;
        JournalAdmin.Ecrire(db, acteur, "systeme.environnements",
            $"Environnements de « {s.Nom} » : {(s.Environnements.Count == 0 ? "aucun" : string.Join(", ", s.Environnements.Select(e => e.Nom)))}");
        await db.SaveChangesAsync();
        return Dto(s);
    }

    /// <summary>Remplace les catégories de la fiche : renommage (les objets suivent) ou retrait (avec leurs objets).</summary>
    public static async Task<SystemeCatalogueDto> DefinirCategories(MepDbContext db, Acteur? acteur, Guid id, CategoriesSystemeSaisie c)
    {
        var s = await Trouver(db, id);
        int retires;
        try { retires = Regles.DefinirCategoriesCatalogue(s, (c.Categories ?? []).Select(x => (x.Nom, x.Ancien))); }
        catch (RegleMetierException ex) { throw ErreurHttp.Saisie(ex.Message); }
        s.Version++;
        JournalAdmin.Ecrire(db, acteur, "systeme.categories",
            $"Catégories de « {s.Nom} » : {(s.Categories.Count == 0 ? "aucune" : string.Join(", ", s.Categories))}{(retires > 0 ? $" ({retires} objet(s) retiré(s))" : "")}");
        await db.SaveChangesAsync();
        return Dto(s);
    }

    /// <summary>Ajoute (<paramref name="objetId"/> null) ou modifie un objet de la fiche.</summary>
    public static async Task<ObjetCatalogueDto> EnregistrerObjet(MepDbContext db, Acteur? acteur, Guid id, Guid? objetId, ObjetCatalogueSaisie c)
    {
        var s = await Trouver(db, id);
        if (objetId is { } oid && s.Objets.All(o => o.Id != oid)) throw ErreurHttp.NonTrouve("Objet introuvable dans la fiche du système.");
        ObjetCatalogue objet;
        try { objet = Regles.EnregistrerObjetCatalogue(s, objetId, c.Categorie, c.Type, c.Nom, c.Libelle, c.Precision); }
        catch (RegleMetierException ex) { throw ErreurHttp.Saisie(ex.Message); }
        s.Version++;
        JournalAdmin.Ecrire(db, acteur, objetId is null ? "systeme.objet-ajoute" : "systeme.objet-modifie",
            $"Objet « {objet.Nom} » ({objet.Categorie}) {(objetId is null ? "ajouté à" : "modifié dans")} la fiche « {s.Nom} »");
        await db.SaveChangesAsync();
        return Dto(objet);
    }

    /// <summary>Retire un objet de la fiche (les MEP qui l'ont retenu gardent leur copie).</summary>
    public static async Task SupprimerObjet(MepDbContext db, Acteur? acteur, Guid id, Guid objetId)
    {
        var s = await Trouver(db, id);
        var o = s.Objets.FirstOrDefault(x => x.Id == objetId) ?? throw ErreurHttp.NonTrouve("Objet introuvable dans la fiche du système.");
        s.Objets = s.Objets.Where(x => x.Id != objetId).ToList();
        s.Version++;
        JournalAdmin.Ecrire(db, acteur, "systeme.objet-supprime", $"Objet « {o.Nom} » ({o.Categorie}) retiré de la fiche « {s.Nom} »");
        await db.SaveChangesAsync();
    }

    /// <summary>Catégories d'objets proposées par défaut à chaque système.</summary>
    public static async Task<List<string>> CategoriesDefaut(MepDbContext db) =>
        (await db.Parametres.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ParametresGlobaux.IdUnique))?.CategoriesObjets
        ?? [.. ParametresGlobaux.CategoriesObjetsParDefaut];

    /// <summary>Remplace la liste des catégories par défaut (les systèmes gardent leurs catégories).</summary>
    public static async Task<List<string>> DefinirCategoriesDefaut(MepDbContext db, Acteur? acteur, CategoriesDefautSaisie c)
    {
        List<string> liste;
        try { liste = Regles.NormaliserCategoriesDefaut(c.Categories ?? []); }
        catch (RegleMetierException ex) { throw ErreurHttp.Saisie(ex.Message); }
        var p = await db.Parametres.FirstOrDefaultAsync(x => x.Id == ParametresGlobaux.IdUnique);
        if (p is null) db.Parametres.Add(p = new ParametresGlobaux());
        var ajoutees = liste.Where(x => !p.CategoriesObjets.Contains(x)).ToList();
        var retirees = p.CategoriesObjets.Where(x => !liste.Contains(x)).ToList();
        p.CategoriesObjets = liste;
        var details = new List<string>();
        if (ajoutees.Count > 0) details.Add("ajoutées : " + string.Join(", ", ajoutees));
        if (retirees.Count > 0) details.Add("retirées : " + string.Join(", ", retirees));
        JournalAdmin.Ecrire(db, acteur, "categories.modifiees",
            "Catégories d'objets par défaut modifiées" + (details.Count > 0 ? " (" + string.Join(" ; ", details) + ")" : ""));
        await db.SaveChangesAsync();
        return liste;
    }

    /// <summary>Supprime le système ; s'il est utilisé par une MEP, une résolution type, un modèle de recette ou une étape modèle, le désactive (renvoie false).</summary>
    public static async Task<bool> Supprimer(MepDbContext db, Acteur? acteur, Guid id)
    {
        var s = await db.SystemesCatalogue.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
        if (s.Source is not null)
            throw ErreurHttp.Saisie("Ce système est géré par Noosphere : cessez d'abord de suivre l'application (console › Liaisons), il redeviendra local.");
        // Utilisé par une MEP, une résolution type, un modèle de recette ou une étape modèle : on le désactive au lieu de le supprimer.
        var utilise = await db.Systemes.AnyAsync(x => x.CatalogueId == id) || await db.ResolutionsTypes.AnyAsync(r => r.SystemeCatalogueIds.Contains(id))
            || await db.ModelesRecette.AnyAsync(r => r.SystemeCatalogueId == id) || await db.ModelesEtape.AnyAsync(m => m.SystemeCatalogueIds.Contains(id));
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

        // Recherche avancée (#59) : activité de chaque système (colonne « Dernière MEP ») et historique des MEP d'un système.
        api.MapGet("/catalogue/systemes/activite", async (MepDbContext db) => (await HistoriqueSystemes.Activite(db)).AsEnumerable());

        api.MapGet("/catalogue/systemes/{id:guid}/historique", async (Guid id, MepDbContext db, TimeProvider temps) =>
            await HistoriqueSystemes.Historique(db, id, Fuseaux.Jour(temps.GetUtcNow(), null)));

        api.MapPost("/catalogue/systemes", async (SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            var s = await Creer(db, Acteur.Depuis(user), c);
            return Results.Created($"/api/catalogue/systemes/{s.Id}", s);
        }).ExigerAdmin();

        api.MapPut("/catalogue/systemes/{id:guid}", async (Guid id, SystemeCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            return Results.Ok(await Modifier(db, Acteur.Depuis(user), id, c));
        }).ExigerAdmin();

        api.MapDelete("/catalogue/systemes/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            await Supprimer(db, Acteur.Depuis(user), id);
            return Results.NoContent();
        }).ExigerAdmin();

        // ---------- Fiche d'un système : environnements, catégories, objets ----------
        api.MapGet("/catalogue/systemes/{id:guid}/objets", async (Guid id, MepDbContext db) =>
        {
            var s = await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Système du catalogue introuvable.");
            return Objets(s).AsEnumerable();
        });

        api.MapPut("/catalogue/systemes/{id:guid}/environnements", async (Guid id, EnvironnementsCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            return Results.Ok(await DefinirEnvironnements(db, Acteur.Depuis(user), id, c));
        }).ExigerAdmin();

        api.MapPut("/catalogue/systemes/{id:guid}/categories", async (Guid id, CategoriesSystemeSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            return Results.Ok(await DefinirCategories(db, Acteur.Depuis(user), id, c));
        }).ExigerAdmin();

        api.MapPost("/catalogue/systemes/{id:guid}/objets", async (Guid id, ObjetCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            var o = await EnregistrerObjet(db, Acteur.Depuis(user), id, null, c);
            return Results.Created($"/api/catalogue/systemes/{id}/objets/{o.Id}", o);
        }).ExigerAdmin();

        api.MapPut("/catalogue/systemes/{id:guid}/objets/{oid:guid}", async (Guid id, Guid oid, ObjetCatalogueSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            return Results.Ok(await EnregistrerObjet(db, Acteur.Depuis(user), id, oid, c));
        }).ExigerAdmin();

        api.MapDelete("/catalogue/systemes/{id:guid}/objets/{oid:guid}", async (Guid id, Guid oid, ClaimsPrincipal user, MepDbContext db) =>
        {
            await SupprimerObjet(db, Acteur.Depuis(user), id, oid);
            return Results.NoContent();
        }).ExigerAdmin();

        // ---------- Catégories d'objets par défaut ----------
        api.MapGet("/catalogue/categories", async (MepDbContext db) => (await CategoriesDefaut(db)).AsEnumerable());

        api.MapPut("/catalogue/categories", async (CategoriesDefautSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            return Results.Ok((await DefinirCategoriesDefaut(db, Acteur.Depuis(user), c)).AsEnumerable());
        }).ExigerAdmin();

        api.MapPost("/meps/{id:guid}/systemes/depuis-catalogue", async (Guid id, DepuisCatalogueSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            if (c.CatalogueId is null) throw ErreurHttp.Saisie("Le système du catalogue est obligatoire.");
            var cat = await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CatalogueId)
                ?? throw ErreurHttp.Saisie("Système du catalogue inconnu.");
            if (!cat.Actif) throw new RegleMetierException($"Le système {cat.Nom} est désactivé dans le catalogue.");
            if (mep.Systemes.Any(x => x.CatalogueId == cat.Id)) throw new RegleMetierException($"Le système {cat.Nom} est déjà présent dans cette MEP.");
            // Un seul environnement dans la fiche : il est choisi d'office (modifiable ensuite).
            mep.Systemes.Add(new Systeme
            {
                MepId = mep.Id, Nom = cat.Nom, Cible = cat.Cible, Impact = ServiceMep.Texte(c.Impact), CatalogueId = cat.Id,
                Environnements = cat.Environnements.Count == 1 ? [new EnvironnementSysteme { Nom = cat.Environnements[0].Nom, Production = cat.Environnements[0].Production }] : [],
            });
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "systeme.ajoute", $"Système ajouté depuis le catalogue : {cat.Nom}");
        })));
    }
}
