using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Résolutions types (Paramètres) et exécution d'une résolution sur une étape en échec.</summary>
public static class ResolutionsEndpoints
{
    private static List<CorrectionDto> Corrections(List<Correction> corrections) =>
        corrections.OrderBy(c => c.Ordre).Select(c => new CorrectionDto(c.Ordre, c.Instruction, c.ResultatAttendu)).ToList();

    private static ResolutionTypeDto Dto(ResolutionType r, IReadOnlyDictionary<Guid, string> noms) => new(r.Id, r.Titre, r.Symptomes, r.Diagnostic,
        [.. r.SystemeCatalogueIds], Corrections(r.Corrections), r.VerificationInstruction, r.VerificationAttendu, r.Actif, r.Version, r.ModifieeLe,
        r.ModifieeParId, Nom(r.ModifieeParId, noms));

    private static VersionResolutionTypeDto Dto(VersionResolutionType v, IReadOnlyDictionary<Guid, string> noms) => new(v.Version, v.Action,
        v.ModifieeLe, v.ModifieeParId, Nom(v.ModifieeParId, noms), v.Titre, v.Symptomes, v.Diagnostic, [.. v.SystemeCatalogueIds],
        Corrections(v.Corrections), v.VerificationInstruction, v.VerificationAttendu, v.Actif);

    private static string? Nom(Guid? id, IReadOnlyDictionary<Guid, string> noms) => id is { } u && noms.TryGetValue(u, out var n) ? n : null;

    private static Task<Dictionary<Guid, string>> Noms(MepDbContext db) => Personnes.Noms(db);

    /// <summary>Date et auteur de la modification, puis archivage de la fiche dans son historique (acteur null : ligne de commande).</summary>
    private static void Tracer(MepDbContext db, ResolutionType r, string action, Acteur? acteur, DateTimeOffset maintenant)
    {
        r.ModifieeLe = maintenant;
        r.ModifieeParId = acteur?.Id;
        db.VersionsResolutionType.Add(VersionResolutionType.De(r, action));
    }

    /// <summary>Applique la saisie à la résolution : 400 si un système est inconnu, 422 (liste des anomalies) si elle est incomplète.</summary>
    public static async Task Appliquer(MepDbContext db, ResolutionType r, ResolutionTypeSaisie c)
    {
        var systemes = (c.SystemeCatalogueIds ?? []).Distinct().ToList();
        if (await db.SystemesCatalogue.CountAsync(s => systemes.Contains(s.Id)) != systemes.Count)
            throw ErreurHttp.Saisie("Système du catalogue inconnu.");
        r.Titre = ServiceMep.Texte(c.Titre);
        r.Symptomes = ServiceMep.Texte(c.Symptomes);
        r.Diagnostic = ServiceMep.Texte(c.Diagnostic);
        r.SystemeCatalogueIds = systemes;
        // Ordre donné par le client (à égalité, ordre de saisie), puis renuméroté 1..n.
        r.Corrections = (c.Corrections ?? []).Select((x, i) => (x, i)).OrderBy(t => t.x.Ordre ?? int.MaxValue).ThenBy(t => t.i)
            .Select((t, i) => new Correction
            {
                Ordre = i + 1, Instruction = ServiceMep.Texte(t.x.Instruction), ResultatAttendu = ServiceMep.Texte(t.x.ResultatAttendu),
            }).ToList();
        r.VerificationInstruction = ServiceMep.Texte(c.VerificationInstruction);
        r.VerificationAttendu = ServiceMep.Texte(c.VerificationAttendu);
        var erreurs = Regles.ErreursResolutionType(r);
        if (erreurs.Count > 0) throw ErreurHttp.Regle(erreurs);
    }

    private static ExecutionResolution TrouverResolution(Mep mep, Guid rid) =>
        mep.Resolutions.FirstOrDefault(r => r.Id == rid) ?? throw ErreurHttp.NonTrouve("Résolution introuvable.");

    // ---------- Opérations partagées avec « gestion-mep resolutions » (acteur null : ligne de commande) ----------

    /// <summary>Résolutions types par titre ; actives seulement sauf <paramref name="inactives"/>, filtrées sur un système du catalogue.</summary>
    public static async Task<List<ResolutionTypeDto>> Lister(MepDbContext db, Guid? systemeCatalogueId, bool inactives = false)
    {
        var q = db.ResolutionsTypes.AsNoTracking().Where(r => inactives || r.Actif);
        // Filtre « contient » : les résolutions dont l'un des systèmes est celui demandé.
        if (systemeCatalogueId is { } s) q = q.Where(r => r.SystemeCatalogueIds.Contains(s));
        var noms = await Noms(db);
        return (await q.OrderBy(r => r.Titre).ToListAsync()).Select(r => Dto(r, noms)).ToList();
    }

    public static async Task<ResolutionTypeDto> Lire(MepDbContext db, Guid id) =>
        Dto(await db.ResolutionsTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable."),
            await Noms(db));

    /// <summary>Crée une résolution type (version 1) après les mêmes contrôles que la saisie dans l'application.</summary>
    public static async Task<ResolutionTypeDto> Creer(MepDbContext db, Acteur? acteur, ResolutionTypeSaisie c, DateTimeOffset maintenant)
    {
        var r = new ResolutionType { Version = 1 };
        await Appliquer(db, r, c);
        Tracer(db, r, "creee", acteur, maintenant);
        db.ResolutionsTypes.Add(r);
        await db.SaveChangesAsync();
        return Dto(r, await Noms(db));
    }

    /// <summary>Remplace le contenu d'une résolution type (nouvelle version archivée), sans contrôle de version concurrente.</summary>
    public static async Task<ResolutionTypeDto> Remplacer(MepDbContext db, Acteur? acteur, ResolutionType r, ResolutionTypeSaisie c, DateTimeOffset maintenant)
    {
        await Appliquer(db, r, c);
        r.Version++;
        Tracer(db, r, "modifiee", acteur, maintenant);
        await db.SaveChangesAsync();
        return Dto(r, await Noms(db));
    }

    /// <summary>
    /// Supprime une résolution type et ses pièces jointes ; liée à une étape ou déjà exécutée, elle est seulement désactivée
    /// pour garder la traçabilité (renvoie false). Inscrit dans le journal d'administration.
    /// </summary>
    public static async Task<bool> Supprimer(MepDbContext db, Acteur? acteur, Guid id, DateTimeOffset maintenant, StockagePiecesJointes stockage)
    {
        var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
        if (await db.Etapes.AnyAsync(e => e.ResolutionIds.Contains(id)) || await db.ExecutionsResolution.AnyAsync(x => x.ResolutionTypeId == id))
        {
            r.Actif = false;
            r.Version++;
            Tracer(db, r, "desactivee", acteur, maintenant);
            JournalAdmin.Ecrire(db, acteur, "resolution.desactivee", $"Résolution type « {r.Titre} » désactivée (utilisée, donc conservée)");
            await db.SaveChangesAsync();
            return false;
        }
        // Supprimée : ses pièces jointes partent en cascade, leurs fichiers sont effacés ensuite.
        var pieces = await db.PiecesJointes.Where(p => p.ResolutionTypeId == id).Select(p => p.Id).ToListAsync();
        db.ResolutionsTypes.Remove(r);
        JournalAdmin.Ecrire(db, acteur, "resolution.supprimee", $"Résolution type « {r.Titre} » supprimée");
        await db.SaveChangesAsync();
        foreach (var p in pieces) stockage.Supprimer(p);
        return true;
    }

    public static void MapResolutions(this RouteGroupBuilder api)
    {
        // ---------- Résolutions types ----------
        api.MapGet("/resolutions", async (Guid? systemeCatalogueId, MepDbContext db) => (await Lister(db, systemeCatalogueId)).AsEnumerable());

        api.MapGet("/resolutions/{id:guid}", (Guid id, MepDbContext db) => Lire(db, id));

        // Historique : chaque version enregistrée, la plus récente d'abord.
        api.MapGet("/resolutions/{id:guid}/versions", async (Guid id, MepDbContext db) =>
        {
            if (!await db.ResolutionsTypes.AnyAsync(r => r.Id == id)) throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            var noms = await Noms(db);
            return (await db.VersionsResolutionType.AsNoTracking().Where(v => v.ResolutionTypeId == id)
                .OrderByDescending(v => v.Version).ThenByDescending(v => v.ModifieeLe).ToListAsync()).Select(v => Dto(v, noms));
        });

        api.MapPost("/resolutions", async (ResolutionTypeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var r = await Creer(db, Acteur.Depuis(user), c, temps.GetUtcNow());
            return Results.Created($"/api/resolutions/{r.Id}", r);
        });

        api.MapPut("/resolutions/{id:guid}", async (Guid id, ResolutionTypeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            if (c.Version is null) throw ErreurHttp.Saisie("La version de la résolution est obligatoire.");
            if (c.Version != r.Version)
                throw ErreurHttp.Conflit("Cette résolution a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
            return Results.Ok(await Remplacer(db, Acteur.Depuis(user), r, c, temps.GetUtcNow()));
        });

        api.MapDelete("/resolutions/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider temps, StockagePiecesJointes stockage) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            await Supprimer(db, acteur, id, temps.GetUtcNow(), stockage);
            return Results.NoContent();
        });

        // ---------- Exécution ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/resolutions", async (Guid id, Guid eid, DemarrerResolutionSaisie c, ClaimsPrincipal user,
            ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var etape = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(etape, c.Revision);
            if (c.ResolutionTypeId is null) throw ErreurHttp.Saisie("La résolution type est obligatoire.");
            var type = await db.ResolutionsTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == c.ResolutionTypeId)
                ?? throw ErreurHttp.Saisie("Résolution type inconnue.");
            var execution = Regles.DemarrerResolution(mep, etape, type, acteur.Id, svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "resolution.demarree", $"Résolution « {execution.Titre} » démarrée sur {etape.Code}");
        })));

        api.MapPost("/meps/{id:guid}/resolutions/{rid:guid}/corrections/{ordre:int}", async (Guid id, Guid rid, int ordre, ControleSaisie c,
            ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var execution = TrouverResolution(mep, rid);
            var etape = mep.Etapes.Single(e => e.Id == execution.EtapeId);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Conforme ou Echec) est obligatoire.");
            var observation = ServiceMep.Texte(c.Observation);
            Regles.ValiderCorrection(mep, execution, ordre, c.Resultat.Value, observation, acteur.Id, svc.Maintenant);

            var suite = string.IsNullOrEmpty(observation) ? "" : $" : {observation}";
            var evenements = new List<ServiceMep.Evenement>
            {
                new("resolution.correction", c.Resultat == ResultatControle.Conforme ? $"Correction {ordre} conforme{suite}" : $"Correction {ordre} en échec{suite}"),
            };
            if (execution.Statut == StatutResolution.Echouee) evenements.AddRange(EvenementsEchec(mep, execution, etape));
            return await svc.Enregistrer(mep, acteur, evenements);
        })));

        api.MapPost("/meps/{id:guid}/resolutions/{rid:guid}/verification", async (Guid id, Guid rid, ControleSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var execution = TrouverResolution(mep, rid);
            var etape = mep.Etapes.Single(e => e.Id == execution.EtapeId);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Conforme ou Echec) est obligatoire.");
            Regles.ValiderVerification(mep, execution, c.Resultat.Value, ServiceMep.Texte(c.Observation), acteur.Id, svc.Maintenant);

            var evenements = new List<ServiceMep.Evenement>();
            if (execution.Statut == StatutResolution.Reussie)
                evenements.Add(new("resolution.reussie", $"Résolution « {execution.Titre} » réussie sur {etape.Code} : {etape.Code} peut être redémarrée"));
            else evenements.AddRange(EvenementsEchec(mep, execution, etape));
            return await svc.Enregistrer(mep, acteur, evenements);
        })));
    }

    /// <summary>
    /// Journal d'une résolution en échec : la résolution échoue, puis le retour arrière se déclenche automatiquement,
    /// sauf point de non-retour franchi (l'étape reste en échec).
    /// </summary>
    private static IEnumerable<ServiceMep.Evenement> EvenementsEchec(Mep mep, ExecutionResolution execution, Etape etape)
    {
        yield return new("resolution.echouee", $"Résolution « {execution.Titre} » en échec sur {etape.Code}");
        if (mep.Phase == Phase.RetourArriere)
            yield return new("repli.declenche", ServiceMep.DetailRetourArriere(mep, null), Systeme: true);
    }
}
