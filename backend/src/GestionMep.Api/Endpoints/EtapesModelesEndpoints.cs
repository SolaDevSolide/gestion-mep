using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Bibliothèque d'étapes modèles : étapes types rattachées à un ou plusieurs systèmes du catalogue, insérées par copie dans
/// le plan des MEP. Tout le monde crée et modifie une étape modèle, seul un administrateur la supprime (comme les recettes).
/// </summary>
public static class EtapesModelesEndpoints
{
    private static ModeleEtapeDto Dto(ModeleEtape m, Dictionary<Guid, string> noms) => new(m.Id, m.Action, [.. m.SystemeCatalogueIds],
        m.Type, m.Criticite, m.DureeEstimeeMinutes, m.Instructions, m.ResultatAttendu, m.Fin, m.SauvegardeEmplacement,
        m.SauvegardeMethodeRecuperation, [.. m.ResolutionIds], m.Utilisations, m.CreeLe, m.Version, m.ModifieLe, m.ModifieParId,
        m.ModifieParId is { } u && noms.TryGetValue(u, out var n) ? n : null);

    private static Task<Dictionary<Guid, string>> Noms(MepDbContext db) => Personnes.Noms(db);

    private static async Task<ModeleEtape> Trouver(MepDbContext db, Guid id) =>
        await db.ModelesEtape.FirstOrDefaultAsync(m => m.Id == id) ?? throw ErreurHttp.NonTrouve("Étape modèle introuvable.");

    /// <summary>400 si un système du catalogue ou une résolution type est inconnu (une résolution désactivée reste admise si déjà liée).</summary>
    private static async Task ControlerReferences(MepDbContext db, ModeleEtape m, IEnumerable<Guid> resolutionsDejaLiees)
    {
        if (await db.SystemesCatalogue.CountAsync(s => m.SystemeCatalogueIds.Contains(s.Id)) != m.SystemeCatalogueIds.Count)
            throw ErreurHttp.Saisie("Système du catalogue inconnu.");
        var deja = resolutionsDejaLiees.ToHashSet();
        var connues = await db.ResolutionsTypes.AsNoTracking().Where(r => m.ResolutionIds.Contains(r.Id)).Select(r => new { r.Id, r.Actif }).ToListAsync();
        foreach (var id in m.ResolutionIds)
            if (!connues.Any(r => r.Id == id && (r.Actif || deja.Contains(id))))
                throw ErreurHttp.Saisie("Résolution type inconnue ou désactivée.");
    }

    /// <summary>Contrôle l'étape modèle (422 avec la liste des anomalies), puis note la date et l'auteur.</summary>
    private static void Valider(ModeleEtape m, Acteur acteur, DateTimeOffset maintenant)
    {
        Regles.NormaliserModeleEtape(m);
        var erreurs = Regles.ErreursModeleEtape(m);
        if (erreurs.Count > 0) throw ErreurHttp.Regle(erreurs);
        m.ModifieLe = maintenant;
        m.ModifieParId = acteur.Id;
    }

    private static void Appliquer(ModeleEtape m, ModeleEtapeSaisie c)
    {
        m.Action = ServiceMep.Texte(c.Action);
        m.SystemeCatalogueIds = (c.SystemeCatalogueIds ?? []).Distinct().ToList();
        m.Type = c.Type ?? TypeEtape.Standard;
        m.Criticite = c.Criticite;
        m.DureeEstimeeMinutes = c.DureeEstimeeMinutes;
        m.Instructions = ServiceMep.Texte(c.Instructions);
        m.ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu);
        m.Fin = c.Fin ?? FinEtape.TestUnitaire;
        m.SauvegardeEmplacement = ServiceMep.Texte(c.SauvegardeEmplacement);
        m.SauvegardeMethodeRecuperation = ServiceMep.Texte(c.SauvegardeMethodeRecuperation);
        m.ResolutionIds = (c.ResolutionIds ?? []).Distinct().ToList();
    }

    public static void MapEtapesModeles(this RouteGroupBuilder api)
    {
        // Les plus utilisées d'abord, puis par ordre de création. Filtre « contient » sur un système du catalogue.
        api.MapGet("/etapes-modeles", async (Guid? systemeCatalogueId, MepDbContext db) =>
        {
            var q = db.ModelesEtape.AsNoTracking();
            if (systemeCatalogueId is { } s) q = q.Where(m => m.SystemeCatalogueIds.Contains(s));
            var noms = await Noms(db);
            return (await q.OrderByDescending(m => m.Utilisations).ThenBy(m => m.CreeLe).ThenBy(m => m.Action).ToListAsync()).Select(m => Dto(m, noms));
        });

        api.MapGet("/etapes-modeles/{id:guid}", async (Guid id, MepDbContext db) => Dto(await Trouver(db, id), await Noms(db)));

        api.MapPost("/etapes-modeles", async (ModeleEtapeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            var maintenant = temps.GetUtcNow();
            var m = new ModeleEtape { Version = 1, CreeLe = maintenant };
            Appliquer(m, c);
            await ControlerReferences(db, m, []);
            Valider(m, acteur, maintenant);
            db.ModelesEtape.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/etapes-modeles/{m.Id}", Dto(m, await Noms(db)));
        });

        // « Transformer en étape modèle » depuis une étape d'une MEP : copie sans point de non-retour, liens, exécutant ni résultats.
        api.MapPost("/etapes-modeles/depuis-etape", async (ModeleEtapeDepuisEtapeSaisie c, ClaimsPrincipal user, MepDbContext db, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            if (c.MepId is null || c.EtapeId is null) throw ErreurHttp.Saisie("La MEP et l'étape sont obligatoires.");
            var mep = await svc.Charger(c.MepId.Value);
            var etape = ServiceMep.TrouverEtape(mep, c.EtapeId.Value);
            var (m, _) = Regles.ModeleDepuisEtape(mep, etape, ServiceMep.Texte(c.Action));
            m.Version = 1;
            m.CreeLe = svc.Maintenant;
            // Une résolution désactivée depuis reste liée à l'étape, mais ne passe pas dans le modèle.
            var actives = await db.ResolutionsTypes.AsNoTracking().Where(r => r.Actif && m.ResolutionIds.Contains(r.Id)).Select(r => r.Id).ToListAsync();
            m.ResolutionIds = m.ResolutionIds.Where(actives.Contains).ToList();
            if (m.SystemeCatalogueIds.Count == 0)
                throw ErreurHttp.Regle(["L'étape ne touche aucun système du catalogue : ajouter à la MEP un système du catalogue et le choisir pour l'étape."]);
            Valider(m, acteur, svc.Maintenant);
            db.ModelesEtape.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/etapes-modeles/{m.Id}", Dto(m, await Noms(db)));
        });

        api.MapPut("/etapes-modeles/{id:guid}", async (Guid id, ModeleEtapeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            var m = await Trouver(db, id);
            if (c.Version is null) throw ErreurHttp.Saisie("La version de l'étape modèle est obligatoire.");
            if (c.Version != m.Version)
                throw ErreurHttp.Conflit("Cette étape modèle a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
            var dejaLiees = m.ResolutionIds.ToList();
            Appliquer(m, c);
            await ControlerReferences(db, m, dejaLiees);
            Valider(m, acteur, temps.GetUtcNow());
            m.Version++;
            await db.SaveChangesAsync();
            return Results.Ok(Dto(m, await Noms(db)));
        });

        // Les étapes déjà insérées dans des MEP sont des copies : elles ne changent pas.
        api.MapDelete("/etapes-modeles/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var m = await Trouver(db, id);
            db.ModelesEtape.Remove(m);
            JournalAdmin.Ecrire(db, acteur, "etape-modele.supprimee", $"Étape modèle « {m.Action} » supprimée de la bibliothèque");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Insertion d'une copie dans le plan : même droits et même phase que l'ajout d'une étape.
        api.MapPost("/meps/{id:guid}/etapes/depuis-modele", async (Guid id, InsertionEtapeModeleSaisie c, ClaimsPrincipal user, ServiceMep svc,
            MepDbContext db) =>
        {
            if (c.ModeleId is null) throw ErreurHttp.Saisie("Choisir une étape modèle de la bibliothèque.");
            var modele = await db.ModelesEtape.AsNoTracking().FirstOrDefaultAsync(m => m.Id == c.ModeleId)
                ?? throw ErreurHttp.NonTrouve("Étape modèle introuvable.");
            var actives = await db.ResolutionsTypes.AsNoTracking().Where(r => r.Actif && modele.ResolutionIds.Contains(r.Id)).Select(r => r.Id).ToListAsync();
            return Results.Ok(await svc.Modifier(id, async mep =>
            {
                var acteur = Acteur.Depuis(user);
                ServiceMep.ExigerEditeur(mep, acteur);
                Regles.ExigerPreparation(mep);
                var plan = c.Plan ?? TypePlan.Deploiement;
                var place = EtapesEndpoints.Place(c.Apres, c.Entre, c.ParalleleA, "entre");
                var (etape, _) = Regles.InsererEtapeModele(mep, modele, plan, place, c.SectionId, actives);
                return await svc.EnregistrerContenu(mep, acteur, "etape.creee",
                    $"{etape.Code} créée depuis l'étape modèle « {modele.Action} »{EtapesEndpoints.DansSection(mep, etape)}");
            }));
        });
    }
}
