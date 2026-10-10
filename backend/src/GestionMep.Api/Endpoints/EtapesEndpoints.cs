using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

public static class EtapesEndpoints
{
    /// <summary>
    /// Applique les champs de la fiche (sans la révision ni la place dans le graphe) ; les dépendances passent par
    /// <see cref="Regles.DefinirDependances"/>, sauf si <paramref name="dependances"/> vaut null (inchangées).
    /// </summary>
    private static void Appliquer(Mep mep, Etape e, EtapeSaisie c, List<Guid> resolutionIds, List<Guid>? dependances)
    {
        var systemes = ServiceMep.ValiderSystemeIds(mep, c.SystemeIds);
        if (c.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, c.ResponsableId, "Responsable");
        if (c.DureeEstimeeMinutes is < 0) throw ErreurHttp.Saisie("La durée estimée ne peut pas être négative.");
        e.Action = ServiceMep.Texte(c.Action);
        e.SystemeIds = systemes;
        e.ResponsableId = c.ResponsableId;
        e.Criticite = c.Criticite;
        e.DureeEstimeeMinutes = c.DureeEstimeeMinutes;
        e.Instructions = ServiceMep.Texte(c.Instructions);
        e.ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu);
        e.ResolutionIds = resolutionIds;
        Regles.DefinirType(e, c.Type ?? TypeEtape.Standard, c.PointDeNonRetour);
        if (dependances is not null) Regles.DefinirDependances(mep, e, dependances);
    }

    public static void MapEtapes(this RouteGroupBuilder api)
    {
        api.MapPost("/meps/{id:guid}/etapes", async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EtapeSaisie? c,
            ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var plan = c?.Plan ?? TypePlan.Deploiement;
            var nbLignes = Regles.Lignes(mep, plan).Count;
            if (c?.Ligne is { } l && (l < 0 || l > nbLignes)) throw ErreurHttp.Saisie($"Ligne invalide : entre 0 et {nbLignes} (nouvelle ligne en bas).");
            if (c?.Position is < 0) throw ErreurHttp.Saisie("La position ne peut pas être négative.");
            var etape = new Etape
            {
                MepId = mep.Id,
                Plan = plan,
                Code = Regles.ProchainCode(mep.Etapes, plan),
                Criticite = Criticite.Moderee,
            };
            // Ligne absente : nouvelle ligne en bas ; position absente : en fin de ligne.
            Regles.PlacerNouvelle(mep, etape, c?.Ligne, c?.Position);
            mep.Etapes.Add(etape);
            if (c is not null)
                Appliquer(mep, etape, c with { Criticite = c.Criticite ?? Criticite.Moderee }, await svc.ValiderResolutionIds(c.ResolutionIds, []), c.Dependances);
            // Sans dépendances explicites : liens par défaut avec la ligne au-dessus et la ligne en dessous.
            if (c?.Dependances is null) Regles.RelierParDefaut(mep, etape);
            var dto = await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.creee", $"{etape.Code} créée");
            return Results.Ok(dto);
        });

        api.MapPut("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, EtapeSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            // Ni la ligne ni la position ne changent ici (voir « placer ») ; dependances absentes = aucun préalable.
            Appliquer(mep, e, c, await svc.ValiderResolutionIds(c.ResolutionIds, e.ResolutionIds), c.Dependances ?? []);
            e.Revision++;
            return Results.Ok(await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.modifiee", $"{e.Code} modifiée"));
        });

        api.MapDelete("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            StockagePiecesJointes stockage) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            var e = ServiceMep.TrouverEtape(mep, eid);
            // Ses suivants restés sans préalable attendent la ligne au-dessus d'eux ; une ligne vide disparaît.
            Regles.RetirerEtape(mep, e);
            var executions = mep.Resolutions.Where(r => r.EtapeId == e.Id).Select(r => (Guid?)r.Id).ToList();
            var pieces = await db.PiecesJointes.Where(p => p.EtapeId == e.Id || executions.Contains(p.ExecutionResolutionId)).Select(p => p.Id).ToListAsync();
            db.Etapes.Remove(e);
            // Les exécutions de résolution de cette étape (restes d'une exécution annulée) partent avec elle.
            foreach (var r in mep.Resolutions.Where(r => r.EtapeId == e.Id).ToList())
            {
                mep.Resolutions.Remove(r);
                db.ExecutionsResolution.Remove(r);
            }
            var dto = await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.supprimee", $"{e.Code} supprimée");
            foreach (var p in pieces) stockage.Supprimer(p); // lignes supprimées en cascade avec l'étape
            return Results.Ok(dto);
        });

        // Déplacement dans le graphe (glisser-déposer) : pas de révision exigée, la place n'est pas dans la fiche.
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/placer", async (Guid id, Guid eid, PlacerSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            var nbLignes = Regles.Lignes(mep, e.Plan).Count;
            if (c.Ligne is not { } ligne || ligne < 0 || ligne > nbLignes)
                throw ErreurHttp.Saisie($"Ligne obligatoire : entre 0 et {nbLignes} (nouvelle ligne en bas).");
            if (c.Position is not { } position || position < 0) throw ErreurHttp.Saisie("Position obligatoire, positive ou nulle.");
            Regles.Placer(mep, e, ligne, position, c.LiensParDefaut);
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "etape.deplacee", $"{e.Code} placée ligne {e.Ligne + 1}"));
        });

        // ---------- Prise de main ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/prendre-la-main", async (Guid id, Guid eid, PriseDeMainSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            var motif = ServiceMep.Texte(c.Motif);

            // Un utilisateur qui n'est pas encore participant le devient (créé seulement si la prise de main aboutit).
            var existant = mep.Participants.FirstOrDefault(p => p.UtilisateurId == acteur.Id);
            var preneur = existant ?? new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Prise en main", UtilisateurId = acteur.Id };
            var prevu = mep.Participants.FirstOrDefault(p => p.Id == e.ResponsableId)?.Nom;
            Regles.PrendreLaMain(mep, e, preneur, motif);
            if (existant is null) mep.Participants.Add(preneur);

            var detail = preneur.Id == e.ResponsableId
                ? $"{acteur.Nom} a repris la main sur {e.Code} : {motif}"
                : $"{acteur.Nom} a pris la main sur {e.Code}" + (prevu is null ? "" : $" (prévu : {prevu})") + $" : {motif}";
            return Results.Ok(await svc.Enregistrer(mep, acteur, "etape.prise-en-main", detail));
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

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/autoriser-non-retour", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.AutoriserNonRetour(mep, e, acteur.Id, svc.Maintenant);
            return Results.Ok(await svc.Enregistrer(mep, acteur, "etape.non-retour-autorise", $"Passage du point de non-retour {e.Code} autorisé"));
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
