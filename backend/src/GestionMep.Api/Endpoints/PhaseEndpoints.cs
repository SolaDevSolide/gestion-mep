using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GestionMep.Api.Endpoints;

public static class PhaseEndpoints
{
    public static void MapPhase(this RouteGroupBuilder api)
    {
        api.MapPost("/meps/{id:guid}/procedure-relue", async (Guid id, ProcedureRelueSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            if (!mep.Participants.Any(p => p.UtilisateurId == acteur.Id))
                throw ErreurHttp.Interdit("Seul un participant relié à votre compte peut valider la relecture de la procédure.");
            Regles.ExigerPreparation(mep);
            if (corps.Relue)
            {
                mep.ProcedureRelueParId = acteur.Id;
                mep.ProcedureRelueLe = svc.Maintenant;
            }
            else
            {
                mep.ProcedureRelueParId = null;
                mep.ProcedureRelueLe = null;
            }
            var detail = corps.Relue ? "Procédure relue" : "Relecture de la procédure retirée";
            return Results.Ok(await svc.Enregistrer(mep, acteur, "procedure.relue", detail));
        });

        api.MapPut("/meps/{id:guid}/plan-repli", async (Guid id, PlanRepliSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerVersion(mep, c.Version);
            if (c.DureeTesteeMinutes is < 0) throw ErreurHttp.Saisie("La durée testée ne peut pas être négative.");
            Regles.ExigerPreparation(mep);
            var p = mep.PlanRepli;
            p.QuandDeclencher = ServiceMep.Texte(c.QuandDeclencher);
            p.DureeTesteeMinutes = c.DureeTesteeMinutes;
            p.VersionARestaurer = ServiceMep.Texte(c.VersionARestaurer);
            p.DonneesDepuisSauvegarde = ServiceMep.Texte(c.DonneesDepuisSauvegarde);
            p.SiRepliEchoue = ServiceMep.Texte(c.SiRepliEchoue);
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "repli.modifie", "Plan de retour arrière modifié"));
        });

        api.MapPost("/meps/{id:guid}/retour-arriere", async (Guid id, RetourArriereSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            Regles.DeclencherRetourArriere(mep, acteur.Id, ServiceMep.Texte(c.Motif), svc.Maintenant);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "repli.declenche", ServiceMep.DetailRetourArriere(mep, acteur.Nom)));
        });

        api.MapPost("/meps/{id:guid}/cloturer", async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ClotureSaisie? c,
            ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var bilan = c is null ? null : new Bilan { CeQuiAMarche = ServiceMep.Texte(c.CeQuiAMarche), Incidents = ServiceMep.Texte(c.Incidents), Actions = ServiceMep.Texte(c.Actions) };
            Regles.Cloturer(mep, acteur.Id, svc.Maintenant, bilan);
            var issue = mep.Issue == IssueMep.Reussie ? "réussie" : "repliée";
            return Results.Ok(await svc.Enregistrer(mep, acteur, "mep.cloturee", $"MEP clôturée : {issue}"));
        });

        api.MapPost("/meps/{id:guid}/no-go", async (Guid id, NoGoSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var motif = ServiceMep.Texte(c.Motif);
            Regles.NoGo(mep, acteur.Id, motif);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "gonogo.nogo", $"No-go : {motif}"));
        });

        api.MapPost("/meps/{id:guid}/phase", async (Guid id, PhaseSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            switch (corps.Phase)
            {
                case Phase.Execution:
                    // Décision Go : tous les critères go / no-go actifs doivent être cochés.
                    Regles.PasserEnExecution(mep, acteur.Id, corps.CriteresCoches ?? [], await svc.CriteresGoNoGo(), await svc.ApprobationsMinimum(), svc.Maintenant);
                    var criteres = mep.GoNoGo.Count == 0 ? "" : " (" + string.Join(", ", mep.GoNoGo.Select(g => g.Libelle)) + ")";
                    return Results.Ok(await svc.Enregistrer(mep, acteur, [
                        new("gonogo.go", $"Go{criteres}"),
                        new("phase.execution", "Plan passé en exécution"),
                    ]));
                case Phase.Recette:
                    Regles.PasserEnRecette(mep, acteur.Id, svc.Maintenant);
                    return Results.Ok(await svc.Enregistrer(mep, acteur, "phase.recette", "MEP passée en recette"));
                case Phase.Preparation:
                    Regles.RevenirEnPreparation(mep, acteur.Id);
                    return Results.Ok(await svc.Enregistrer(mep, acteur, "phase.preparation", "Retour en préparation"));
                default:
                    throw ErreurHttp.Saisie("Phase attendue : Preparation, Execution ou Recette (le retour arrière et la clôture ont leurs propres routes).");
            }
        });
    }
}
