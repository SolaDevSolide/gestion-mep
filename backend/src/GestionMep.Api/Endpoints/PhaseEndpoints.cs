using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Endpoints;

public static class PhaseEndpoints
{
    public static void MapPhase(this RouteGroupBuilder api)
    {
        api.MapPost("/meps/{id:guid}/procedure-relue", async (Guid id, ProcedureRelueSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
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
            return await svc.Enregistrer(mep, acteur, "procedure.relue", detail);
        })));

        api.MapPut("/meps/{id:guid}/plan-repli", async (Guid id, PlanRepliSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerVersion(mep, c.Version);
            if (c.DureeTesteeMinutes is < 0) throw ErreurHttp.Saisie("La durée testée ne peut pas être négative.");
            Regles.ExigerPreparation(mep);
            var p = mep.PlanRepli;
            p.QuandDeclencher = ServiceMep.Texte(c.QuandDeclencher);
            p.DureeTesteeMinutes = c.DureeTesteeMinutes;
            p.VersionARestaurer = ServiceMep.Texte(c.VersionARestaurer);
            p.DonneesDepuisSauvegarde = ServiceMep.Texte(c.DonneesDepuisSauvegarde);
            p.SiRepliEchoue = ServiceMep.Texte(c.SiRepliEchoue);
            return await svc.EnregistrerContenu(mep, acteur, "repli.modifie", "Plan de retour arrière modifié");
        })));

        api.MapPost("/meps/{id:guid}/retour-arriere", async (Guid id, RetourArriereSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            Regles.DeclencherRetourArriere(mep, acteur.Id, ServiceMep.Texte(c.Motif), svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "repli.declenche", ServiceMep.DetailRetourArriere(mep, acteur.Nom));
        })));

        api.MapPost("/meps/{id:guid}/cloturer", async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ClotureSaisie? c,
            ClaimsPrincipal user, ServiceMep svc, RapportsFin rapports, ILogger<RapportsFin> journal) =>
        {
            var dto = await svc.Modifier(id, async mep =>
            {
                var acteur = Acteur.Depuis(user);
                var bilan = c is null ? null : new Bilan { CeQuiAMarche = ServiceMep.Texte(c.CeQuiAMarche), Incidents = ServiceMep.Texte(c.Incidents), Actions = ServiceMep.Texte(c.Actions) };
                var echec = c?.Echec == true;
                Regles.Cloturer(mep, acteur.Id, svc.Maintenant, bilan, echec, c?.EtatService, c?.ReferenceIncident);
                var detail = $"MEP clôturée : {Regles.IssueEnClair(mep)}";
                if (echec)
                    detail += $". État du service : {mep.EtatService}" + (mep.ReferenceIncident.Length > 0 ? $" (incident {mep.ReferenceIncident})" : "");
                return await svc.Enregistrer(mep, acteur, "mep.cloturee", detail);
            });
            // Rapport de fin figé à la clôture, une fois celle-ci enregistrée (une écriture rejouée ne le fige pas deux fois). S'il
            // échoue, la clôture reste acquise : le rapport sera figé à sa première demande.
            try
            {
                await rapports.Figer(id, calculeApres: false);
            }
            catch (Exception e) when (e is DbUpdateException or InvalidOperationException or NpgsqlException)
            {
                journal.LogError(e, "Rapport de fin de la MEP {Mep} non figé à la clôture.", id);
            }
            return Results.Ok(dto);
        });

        api.MapPost("/meps/{id:guid}/no-go", async (Guid id, NoGoSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var motif = ServiceMep.Texte(c.Motif);
            Regles.NoGo(mep, acteur.Id, motif);
            if (c.Reporter != true) return await svc.Enregistrer(mep, acteur, "gonogo.nogo", $"No-go : {motif}");
            // Le no-go peut reporter la MEP dans la foulée, avec le même motif.
            Regles.Reporter(mep, acteur.Id, motif, c.DateVisee, svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, [new("gonogo.nogo", $"No-go : {motif}"), new("mep.reportee", Regles.DetailReport(mep.Report!))]);
        })));

        api.MapPost("/meps/{id:guid}/phase", async (Guid id, PhaseSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            switch (corps.Phase)
            {
                case Phase.Execution when mep.Phase == Phase.Recette:
                    // « Corriger en avant » : retour de la recette à l'exécution, motif obligatoire ; les tests en échec sont à refaire.
                    var motifExecution = ServiceMep.Texte(corps.Motif);
                    var remis = Regles.RevenirEnExecution(mep, acteur.Id, motifExecution, svc.Maintenant);
                    return await svc.Enregistrer(mep, acteur, "phase.execution", $"Retour en exécution pour corriger : {motifExecution}"
                        + (remis.Count > 0 ? $". Tests à refaire : {string.Join(", ", remis)}" : ""));
                case Phase.Execution:
                    // Décision Go : tous les critères go / no-go actifs doivent être cochés.
                    var report = mep.Report;
                    var figeesAvant = mep.ResolutionsFigeesLe;
                    // Séparation des tâches sur les comptes reliés, résolutions liées actives et complètes, figées si besoin.
                    Regles.PasserEnExecution(mep, acteur.Id, corps.CriteresCoches ?? [], await svc.CriteresGoNoGo(), await svc.Contexte(mep), svc.Maintenant);
                    var criteres = mep.GoNoGo.Count == 0 ? "" : " (" + string.Join(", ", mep.GoNoGo.Select(g => g.Libelle)) + ")";
                    List<ServiceMep.Evenement> evenements = [new("gonogo.go", $"Go{criteres}"), new("phase.execution", "Plan passé en exécution")];
                    if (report is not null) evenements.Insert(0, new("mep.reprise", "Report levé par le Go"));
                    evenements.AddRange(ServiceMep.EvenementsFigees(mep, figeesAvant));
                    return await svc.Enregistrer(mep, acteur, evenements);
                case Phase.Recette:
                    Regles.PasserEnRecette(mep, acteur.Id, svc.Maintenant);
                    return await svc.Enregistrer(mep, acteur, "phase.recette", "MEP passée en recette");
                case Phase.Preparation:
                    var motif = ServiceMep.Texte(corps.Motif);
                    Regles.RevenirEnPreparation(mep, acteur.Id, motif);
                    return await svc.Enregistrer(mep, acteur, "phase.preparation", $"Retour en préparation : {motif}");
                default:
                    throw ErreurHttp.Saisie("Phase attendue : Preparation, Execution ou Recette (le retour arrière, la clôture et l'annulation ont leurs propres routes).");
            }
        })));
    }
}
