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
    private static ResolutionTypeDto Dto(ResolutionType r) => new(r.Id, r.Titre, r.Symptomes, r.Diagnostic, r.SystemeCatalogueId,
        r.Corrections.OrderBy(c => c.Ordre).Select(c => new CorrectionDto(c.Ordre, c.Instruction, c.ResultatAttendu)).ToList(),
        r.VerificationInstruction, r.VerificationAttendu, r.Actif, r.Version, r.ModifieeLe);

    /// <summary>Applique la saisie à la résolution : 400 si le système est inconnu, 422 (liste des anomalies) si elle est incomplète.</summary>
    private static async Task Appliquer(MepDbContext db, ResolutionType r, ResolutionTypeSaisie c)
    {
        if (c.SystemeCatalogueId is not null && !await db.SystemesCatalogue.AnyAsync(s => s.Id == c.SystemeCatalogueId))
            throw ErreurHttp.Saisie("Système du catalogue inconnu.");
        r.Titre = ServiceMep.Texte(c.Titre);
        r.Symptomes = ServiceMep.Texte(c.Symptomes);
        r.Diagnostic = ServiceMep.Texte(c.Diagnostic);
        r.SystemeCatalogueId = c.SystemeCatalogueId;
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
            if (systemeCatalogueId is not null) q = q.Where(r => r.SystemeCatalogueId == systemeCatalogueId);
            return (await q.OrderBy(r => r.Titre).ToListAsync()).Select(Dto);
        });

        api.MapGet("/resolutions/{id:guid}", async (Guid id, MepDbContext db) =>
            Dto(await db.ResolutionsTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.")));

        api.MapPost("/resolutions", async (ResolutionTypeSaisie c, MepDbContext db, TimeProvider temps) =>
        {
            var r = new ResolutionType { ModifieeLe = temps.GetUtcNow(), Version = 1 };
            await Appliquer(db, r, c);
            db.ResolutionsTypes.Add(r);
            await db.SaveChangesAsync();
            return Results.Created($"/api/resolutions/{r.Id}", Dto(r));
        });

        api.MapPut("/resolutions/{id:guid}", async (Guid id, ResolutionTypeSaisie c, MepDbContext db, TimeProvider temps) =>
        {
            var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            if (c.Version is null) throw ErreurHttp.Saisie("La version de la résolution est obligatoire.");
            if (c.Version != r.Version)
                throw ErreurHttp.Conflit("Cette résolution a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
            await Appliquer(db, r, c);
            r.Version++;
            r.ModifieeLe = temps.GetUtcNow();
            await db.SaveChangesAsync();
            return Results.Ok(Dto(r));
        });

        api.MapDelete("/resolutions/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            if (!Acteur.Depuis(user).EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var r = await db.ResolutionsTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            // Liée à une étape (ou déjà exécutée) : désactivation, pour garder la traçabilité.
            if (await db.Etapes.AnyAsync(e => e.ResolutionIds.Contains(id)) || await db.ExecutionsResolution.AnyAsync(x => x.ResolutionTypeId == id))
            {
                r.Actif = false;
                r.Version++;
                r.ModifieeLe = temps.GetUtcNow();
            }
            else db.ResolutionsTypes.Remove(r);
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

    /// <summary>Journal d'une résolution en échec : la résolution échoue, puis le retour arrière se déclenche automatiquement.</summary>
    private static IEnumerable<ServiceMep.Evenement> EvenementsEchec(Mep mep, ExecutionResolution execution, Etape etape)
    {
        yield return new("resolution.echouee", $"Résolution « {execution.Titre} » en échec sur {etape.Code}");
        yield return new("repli.declenche", ServiceMep.DetailRetourArriere(mep, null), Systeme: true);
    }
}
