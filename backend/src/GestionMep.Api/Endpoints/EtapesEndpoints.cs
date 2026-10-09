using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GestionMep.Api.Endpoints;

public static class EtapesEndpoints
{
    /// <summary>Applique les champs de la fiche (sans la révision) ; les dépendances passent par <see cref="Regles.DefinirDependances"/>.</summary>
    private static void Appliquer(Mep mep, Etape e, EtapeSaisie c)
    {
        ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
        if (c.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, c.ResponsableId, "Responsable");
        e.Action = ServiceMep.Texte(c.Action);
        e.SystemeId = c.SystemeId;
        e.ResponsableId = c.ResponsableId;
        e.Criticite = c.Criticite;
        e.Instructions = ServiceMep.Texte(c.Instructions);
        e.ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu);
        Regles.DefinirDependances(mep, e, c.Dependances ?? []);
    }

    public static void MapEtapes(this RouteGroupBuilder api)
    {
        api.MapPost("/meps/{id:guid}/etapes", async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EtapeSaisie? c,
            ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var etape = new Etape
            {
                MepId = mep.Id,
                Code = Regles.ProchainCode(mep.Etapes),
                Ordre = mep.Etapes.Count == 0 ? 1 : mep.Etapes.Max(e => e.Ordre) + 1,
                Criticite = Criticite.Moderee,
            };
            mep.Etapes.Add(etape);
            if (c is not null) Appliquer(mep, etape, c with { Criticite = c.Criticite ?? Criticite.Moderee });
            var dto = await svc.Enregistrer(mep, Acteur.Depuis(user), "etape.creee", $"{etape.Code} créée");
            return Results.Ok(dto);
        });

        api.MapPut("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, EtapeSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Appliquer(mep, e, c);
            e.Revision++;
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "etape.modifiee", $"{e.Code} modifiée"));
        });

        api.MapDelete("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            Regles.VerifierSuppression(mep, e);
            mep.Etapes.Remove(e);
            db.Etapes.Remove(e);
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "etape.supprimee", $"{e.Code} supprimée"));
        });

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/deplacer", async (Guid id, Guid eid, DeplacerSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            var sens = (c.Sens ?? "").ToLowerInvariant();
            if (sens is not ("haut" or "bas")) throw ErreurHttp.Saisie("Sens attendu : haut ou bas.");

            var ordonnees = mep.Etapes.OrderBy(x => x.Ordre).ToList();
            var i = ordonnees.IndexOf(e);
            var j = sens == "haut" ? i - 1 : i + 1;
            if (j < 0 || j >= ordonnees.Count) return Results.Ok(await svc.VersDto(mep, acteur.Id)); // déjà en bout de liste
            ordonnees.RemoveAt(i);
            ordonnees.Insert(j, e);
            for (var k = 0; k < ordonnees.Count; k++) ordonnees[k].Ordre = k + 1;
            return Results.Ok(await svc.Enregistrer(mep, acteur, "etape.deplacee", $"{e.Code} déplacée vers le {(sens == "haut" ? "haut" : "bas")}"));
        });

        // ---------- Exécution ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/demarrer", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.Demarrer(mep, e, acteur.Id, svc.Maintenant);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "etape.demarree", $"{e.Code} démarrée"));
        });

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/terminer", async (Guid id, Guid eid, TerminerSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Reussie ou Echec) est obligatoire.");
            var preuve = ServiceMep.Texte(c.Preuve);
            Regles.Terminer(mep, e, c.Resultat.Value, preuve, c.ResultatVerifie, acteur.Id, svc.Maintenant);
            return Results.Ok(c.Resultat == EtatEtape.Reussie
                ? await svc.Enregistrer(mep, acteur, "etape.reussie", $"{e.Code} réussie : {preuve}")
                : await svc.Enregistrer(mep, acteur, "etape.echec", $"{e.Code} en échec : {preuve}"));
        });

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/autoriser-reprise", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.AutoriserReprise(mep, e, acteur.Id, svc.Maintenant);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "etape.reprise", $"Reprise de {e.Code} autorisée"));
        });
    }
}
