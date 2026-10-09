using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;

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

        api.MapPost("/meps/{id:guid}/phase", async (Guid id, PhaseSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            switch (corps.Phase)
            {
                case Phase.Execution:
                    Regles.PasserEnExecution(mep, acteur.Id, svc.Maintenant);
                    return Results.Ok(await svc.Enregistrer(mep, acteur, "phase.execution", "Plan passé en exécution"));
                case Phase.Preparation:
                    Regles.RevenirEnPreparation(mep, acteur.Id);
                    return Results.Ok(await svc.Enregistrer(mep, acteur, "phase.preparation", "Retour en préparation"));
                default:
                    throw ErreurHttp.Saisie("Phase attendue : Preparation ou Execution.");
            }
        });
    }
}
