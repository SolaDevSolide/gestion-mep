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

    private static Task<Dictionary<Guid, string>> Noms(MepDbContext db) => db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NomAffiche);

    /// <summary>Date et auteur de la modification, puis archivage de la fiche dans son historique.</summary>
    private static void Tracer(MepDbContext db, ResolutionType r, string action, Acteur acteur, TimeProvider temps)
    {
        r.ModifieeLe = temps.GetUtcNow();
        r.ModifieeParId = acteur.Id;
        db.VersionsResolutionType.Add(VersionResolutionType.De(r, action));
    }

    /// <summary>Applique la saisie à la résolution : 400 si un système est inconnu, 422 (liste des anomalies) si elle est incomplète.</summary>
    private static async Task Appliquer(MepDbContext db, ResolutionType r, ResolutionTypeSaisie c)
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

    public static void MapResolutions(this RouteGroupBuilder api)
    {
        // ---------- Résolutions types ----------
        api.MapGet("/resolutions", async (Guid? systemeCatalogueId, MepDbContext db) =>
        {
            var q = db.ResolutionsTypes.AsNoTracking().Where(r => r.Actif);
            // Filtre « contient » : les résolutions dont l'un des systèmes est celui demandé.
            if (systemeCatalogueId is { } s) q = q.Where(r => r.SystemeCatalogueIds.Contains(s));
            var noms = await Noms(db);
            return (await q.OrderBy(r => r.Titre).ToListAsync()).Select(r => Dto(r, noms));
        });

        api.MapGet("/resolutions/{id:guid}", async (Guid id, MepDbContext db) =>
            Dto(await db.ResolutionsTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable."),
                await Noms(db)));

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
            var acteur = Acteur.Depuis(user);
            var r = new ResolutionType { Version = 1 };
            await Appliquer(db, r, c);
            Tracer(db, r, "creee", acteur, temps);
            db.ResolutionsTypes.Add(r);
            await db.SaveChangesAsync();
            return Results.Created($"/api/resolutions/{r.Id}", Dto(r, new Dictionary<Guid, string> { [acteur.Id] = acteur.Nom }));
        });

        api.MapPut("/resolutions/{id:guid}", async (Guid id, ResolutionTypeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            if (c.Version is null) throw ErreurHttp.Saisie("La version de la résolution est obligatoire.");
            if (c.Version != r.Version)
                throw ErreurHttp.Conflit("Cette résolution a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
            await Appliquer(db, r, c);
            r.Version++;
            Tracer(db, r, "modifiee", acteur, temps);
            await db.SaveChangesAsync();
            return Results.Ok(Dto(r, await Noms(db)));
        });

        api.MapDelete("/resolutions/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider temps, StockagePiecesJointes stockage) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            // Liée à une étape (ou déjà exécutée) : désactivation, pour garder la traçabilité.
            if (await db.Etapes.AnyAsync(e => e.ResolutionIds.Contains(id)) || await db.ExecutionsResolution.AnyAsync(x => x.ResolutionTypeId == id))
            {
                r.Actif = false;
                r.Version++;
                Tracer(db, r, "desactivee", acteur, temps);
            }
            else
            {
                // Supprimée : ses pièces jointes partent en cascade, leurs fichiers sont effacés ensuite.
                var pieces = await db.PiecesJointes.Where(p => p.ResolutionTypeId == id).Select(p => p.Id).ToListAsync();
                db.ResolutionsTypes.Remove(r);
                await db.SaveChangesAsync();
                foreach (var p in pieces) stockage.Supprimer(p);
                return Results.NoContent();
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---------- Exécution ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/resolutions", async (Guid id, Guid eid, DemarrerResolutionSaisie c, ClaimsPrincipal user,
            ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var etape = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(etape, c.Revision);
            if (c.ResolutionTypeId is null) throw ErreurHttp.Saisie("La résolution type est obligatoire.");
            var type = await db.ResolutionsTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == c.ResolutionTypeId)
                ?? throw ErreurHttp.Saisie("Résolution type inconnue.");
            var execution = Regles.DemarrerResolution(mep, etape, type, acteur.Id, svc.Maintenant);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "resolution.demarree", $"Résolution « {execution.Titre} » démarrée sur {etape.Code}"));
        });

        api.MapPost("/meps/{id:guid}/resolutions/{rid:guid}/corrections/{ordre:int}", async (Guid id, Guid rid, int ordre, ControleSaisie c,
            ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
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
            return Results.Ok(await svc.Enregistrer(mep, acteur, evenements));
        });

        api.MapPost("/meps/{id:guid}/resolutions/{rid:guid}/verification", async (Guid id, Guid rid, ControleSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var execution = TrouverResolution(mep, rid);
            var etape = mep.Etapes.Single(e => e.Id == execution.EtapeId);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Conforme ou Echec) est obligatoire.");
            Regles.ValiderVerification(mep, execution, c.Resultat.Value, ServiceMep.Texte(c.Observation), acteur.Id, svc.Maintenant);

            var evenements = new List<ServiceMep.Evenement>();
            if (execution.Statut == StatutResolution.Reussie)
                evenements.Add(new("resolution.reussie", $"Résolution « {execution.Titre} » réussie sur {etape.Code} : {etape.Code} peut être redémarrée"));
            else evenements.AddRange(EvenementsEchec(mep, execution, etape));
            return Results.Ok(await svc.Enregistrer(mep, acteur, evenements));
        });
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
