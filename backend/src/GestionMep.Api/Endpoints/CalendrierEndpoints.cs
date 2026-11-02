using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Calendrier des MEP, export et abonnement iCalendar, périodes de gel et dérogations (#129). Lecture : toute personne
/// connectée ; périodes de gel : administrateurs ; abonnement : adresse secrète propre à chaque compte, sans session.
/// </summary>
public static class CalendrierEndpoints
{
    private static bool AvecGels(string? valeur) => valeur?.Trim().ToLowerInvariant() switch
    {
        null or "" or "1" or "oui" or "true" => true,
        "0" or "non" or "false" => false,
        _ => throw ErreurHttp.Saisie("« gels » attend 1 ou 0."),
    };

    /// <summary>Nom du fichier exporté : « calendrier-mep-2026-11-01-2026-11-30.ics ».</summary>
    private static string NomFichier(ServiceCalendrier.Plage p) => $"calendrier-mep-{p.Du:yyyy-MM-dd}-{p.Au:yyyy-MM-dd}.ics";

    public static void MapCalendrier(this RouteGroupBuilder api)
    {
        // ----- Calendrier -----

        // MEP dont le créneau touche la plage (du, au, fuseau), filtres de la liste des MEP, page (#96 : limite, apres).
        api.MapGet("/calendrier", async ([AsParameters] ParametresCalendrier p, int? limite, string? apres, ServiceCalendrier cal) =>
        {
            var plage = cal.LirePlage(p.Du, p.Au, p.Fuseau);
            return await cal.Page(plage, ServiceCalendrier.Filtre(p), limite, apres);
        });

        // Export iCalendar de la plage filtrée (fichier à importer dans un agenda).
        api.MapGet("/calendrier/ics", async ([AsParameters] ParametresCalendrier p, ServiceCalendrier cal, IConfiguration config, HttpRequest requete) =>
        {
            var plage = cal.LirePlage(p.Du, p.Au, p.Fuseau);
            var ics = await cal.Ics(plage, ServiceCalendrier.Filtre(p), AvecGels(p.Gels), ServiceCalendrier.AdresseBase(config, requete));
            return Results.File(System.Text.Encoding.UTF8.GetBytes(ics), ICalendar.TypeContenu, NomFichier(plage));
        }).Produces(StatusCodes.Status200OK, contentType: ICalendar.TypeContenu);

        // ----- Abonnement (adresse secrète par compte) -----

        api.MapGet("/calendrier/abonnement", async (ClaimsPrincipal user, ServiceCalendrier cal) => await cal.Abonnement(Acteur.Depuis(user).Id));

        // Crée ou régénère l'adresse : elle n'est donnée qu'ici, l'ancienne cesse de répondre.
        api.MapPost("/calendrier/abonnement", async (ClaimsPrincipal user, ServiceCalendrier cal, IConfiguration config, HttpRequest requete) =>
            await cal.Regenerer(Acteur.Depuis(user), ServiceCalendrier.AdresseBase(config, requete)));

        api.MapDelete("/calendrier/abonnement", async (ClaimsPrincipal user, ServiceCalendrier cal) =>
        {
            await cal.Revoquer(Acteur.Depuis(user));
            return Results.NoContent();
        });

        // Flux de l'abonnement, sans session : le secret de l'adresse désigne le compte (404 s'il est inconnu, révoqué, ou si
        // le compte est désactivé). Mêmes filtres que le calendrier dans l'adresse ; plage : de 90 jours avant à un an après.
        api.MapGet("/calendrier/abonnement/{secret}.ics", async (string secret, [AsParameters] ParametresCalendrier p, ServiceCalendrier cal,
            IConfiguration config, HttpRequest requete, HttpResponse reponse) =>
        {
            if (await cal.CompteAbonne(secret) is null) return ReponsesErreur.Resultat(404, "Abonnement introuvable ou révoqué.");
            var ics = await cal.Ics(cal.PlageAbonnement(), ServiceCalendrier.Filtre(p), AvecGels(p.Gels), ServiceCalendrier.AdresseBase(config, requete));
            reponse.Headers.CacheControl = "private, max-age=300";
            return Results.Text(ics, ICalendar.TypeContenu);
        }).AllowAnonymous().Produces(StatusCodes.Status200OK, contentType: ICalendar.TypeContenu);

        // ----- Périodes de gel -----

        api.MapGet("/periodes-gel", async (ServiceCalendrier cal) => await cal.ListerPeriodes());

        var admin = api.MapGroup("/admin/periodes-gel").ExigerAdmin();

        admin.MapPost("", async (PeriodeGelSaisie s, ClaimsPrincipal user, ServiceCalendrier cal) =>
        {
            var p = await cal.Creer(Acteur.Depuis(user), s);
            return Results.Created($"/api/periodes-gel/{p.Id}", p);
        });

        admin.MapPut("/{id:guid}", async (Guid id, PeriodeGelSaisie s, ClaimsPrincipal user, ServiceCalendrier cal) =>
            Results.Ok(await cal.Modifier(Acteur.Depuis(user), id, s)));

        admin.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceCalendrier cal) =>
        {
            await cal.Supprimer(Acteur.Depuis(user), id);
            return Results.NoContent();
        });

        // ----- Dérogations au gel (journal de la MEP) -----

        // Demande : pilote ou suppléant, en préparation, motif obligatoire ; la période doit toucher le créneau ou un Go maintenant.
        api.MapPost("/meps/{id:guid}/derogations-gel", async (Guid id, DerogationGelSaisie s, ClaimsPrincipal user, ServiceMep svc) =>
            Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var gels = await svc.PeriodesGel();
            var periode = gels.FirstOrDefault(p => p.Id == s.PeriodeGelId) ?? throw ErreurHttp.NonTrouve("Période de gel introuvable.");
            var candidates = Regles.GelsConcernes(mep, [periode], svc.Maintenant);
            var o = (s.Occurrence is { } debut ? candidates.FirstOrDefault(c => c.Debut == debut) : candidates.FirstOrDefault());
            if (o is null && !Regles.EstPilote(mep, acteur.Id))
                throw new DroitInsuffisantException("Seul le pilote ou un suppléant peut demander une dérogation au gel.");
            if (o is null) throw new RegleMetierException($"La MEP n'est pas concernée par la période de gel « {periode.Nom} » : aucune dérogation à demander.");
            var d = Regles.DemanderDerogation(mep, acteur.Id, o, ServiceMep.Texte(s.Motif), svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "gel.derogation.demandee", $"Dérogation demandée pour la période de gel {Regles.DecrireOccurrence(o)} : {d.Motif}");
        })));

        // Décision : approbateur désigné, ni pilote, ni suppléant, ni responsable technique, ni demandeur ; refus commenté.
        api.MapPost("/meps/{id:guid}/derogations-gel/{derogationId:guid}/decision", async (Guid id, Guid derogationId, DecisionDerogationSaisie s,
            ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var d = mep.DerogationsGel.FirstOrDefault(x => x.Id == derogationId) ?? throw ErreurHttp.NonTrouve("Dérogation introuvable.");
            var commentaire = ServiceMep.Texte(s.Commentaire);
            Regles.DeciderDerogation(mep, acteur.Id, d, s.Accorder, commentaire, svc.Maintenant, await Personnes.Charger(db, avecEquipes: false));
            var detail = $"Dérogation {(s.Accorder ? "accordée" : "refusée")} pour la période de gel « {d.NomPeriode} »"
                + (commentaire.Length > 0 ? $" : {commentaire}" : "");
            return await svc.Enregistrer(mep, acteur, s.Accorder ? "gel.derogation.accordee" : "gel.derogation.refusee", detail);
        })));

        api.MapPost("/meps/{id:guid}/derogations-gel/{derogationId:guid}/retirer", async (Guid id, Guid derogationId, ClaimsPrincipal user, ServiceMep svc) =>
            Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var d = mep.DerogationsGel.FirstOrDefault(x => x.Id == derogationId) ?? throw ErreurHttp.NonTrouve("Dérogation introuvable.");
            Regles.RetirerDerogation(mep, acteur.Id, d);
            return await svc.Enregistrer(mep, acteur, "gel.derogation.retiree", $"Dérogation retirée pour la période de gel « {d.NomPeriode} »");
        })));
    }
}
