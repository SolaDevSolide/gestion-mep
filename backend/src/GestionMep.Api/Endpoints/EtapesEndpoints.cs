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
        if (c.DureeEstimeeMinutes is < 0) throw ErreurHttp.Saisie("La durée estimée ne peut pas être négative.");
        e.Action = ServiceMep.Texte(c.Action);
        e.SystemeIds = systemes;
        // Environnements absents : inchangés, sauf ceux des systèmes que l'étape ne touche plus.
        if (c.Environnements is { } envs)
            Regles.DefinirEnvironnementsEtape(mep, e, envs.Select(x => (x.SystemeId ?? throw ErreurHttp.Saisie("Environnement de l'étape : le système est obligatoire."),
                x.Nom ?? "")));
        else Regles.NettoyerEnvironnementsEtape(e);
        // responsableId est ignoré depuis le jalon 4.2 : l'exécutant est celui qui démarre l'étape.
        e.Criticite = c.Criticite;
        e.DureeEstimeeMinutes = c.DureeEstimeeMinutes;
        e.Instructions = ServiceMep.Texte(c.Instructions);
        e.ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu);
        e.ResolutionIds = resolutionIds;
        if (c.Fin is { } fin) Regles.DefinirFin(e, fin);
        if (c.SauvegardeEmplacement is not null) e.SauvegardeEmplacement = ServiceMep.Texte(c.SauvegardeEmplacement);
        if (c.SauvegardeMethodeRecuperation is not null) e.SauvegardeMethodeRecuperation = ServiceMep.Texte(c.SauvegardeMethodeRecuperation);
        // Après les champs de sauvegarde : une étape standard les efface.
        Regles.DefinirType(e, c.Type ?? TypeEtape.Standard, c.PointDeNonRetour);
        if (dependances is not null) Regles.DefinirDependances(mep, e, dependances);
        // MEP simplifiée : ni durée, ni criticité, ni résolutions liées.
        Regles.AppliquerTypeAEtape(mep, e);
    }

    public static void MapEtapes(this RouteGroupBuilder api)
    {
        api.MapPost("/meps/{id:guid}/etapes", async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EtapeSaisie? c,
            ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
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
            Regles.AppliquerTypeAEtape(mep, etape);
            // Sans dépendances explicites : liens par défaut avec la ligne au-dessus et la ligne en dessous.
            if (c?.Dependances is null) Regles.RelierParDefaut(mep, etape);
            var dto = await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.creee", $"{etape.Code} créée");
            return dto;
        })));

        api.MapPut("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, EtapeSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            // Ni la ligne ni la position ne changent ici (voir « placer ») ; dependances absentes = aucun préalable.
            Appliquer(mep, e, c, await svc.ValiderResolutionIds(c.ResolutionIds, e.ResolutionIds), c.Dependances ?? []);
            e.Revision++;
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.modifiee", $"{e.Code} modifiée");
        })));

        api.MapDelete("/meps/{id:guid}/etapes/{eid:guid}", async (Guid id, Guid eid, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            StockagePiecesJointes stockage) =>
        {
            // Fichiers effacés une seule fois, après l'enregistrement réussi : l'action peut être rejouée (ServiceMep.Modifier).
            List<Guid> pieces = [];
            var dto = await svc.Modifier(id, async mep =>
            {
                ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
                var e = ServiceMep.TrouverEtape(mep, eid);
                // Ses suivants restés sans préalable attendent la ligne au-dessus d'eux ; une ligne vide disparaît.
                Regles.RetirerEtape(mep, e);
                var executions = mep.Resolutions.Where(r => r.EtapeId == e.Id).Select(r => (Guid?)r.Id).ToList();
                pieces = await db.PiecesJointes.Where(p => p.EtapeId == e.Id || executions.Contains(p.ExecutionResolutionId)).Select(p => p.Id).ToListAsync();
                db.Etapes.Remove(e);
                // Les exécutions de résolution de cette étape (restes d'une exécution annulée) partent avec elle.
                foreach (var r in mep.Resolutions.Where(r => r.EtapeId == e.Id).ToList())
                {
                    mep.Resolutions.Remove(r);
                    db.ExecutionsResolution.Remove(r);
                }
                return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "etape.supprimee", $"{e.Code} supprimée");
            });
            foreach (var p in pieces) stockage.Supprimer(p); // lignes supprimées en cascade avec l'étape
            return Results.Ok(dto);
        });

        // Déplacement dans le graphe (glisser-déposer) : pas de révision exigée, la place n'est pas dans la fiche.
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/placer", async (Guid id, Guid eid, PlacerSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            var nbLignes = Regles.Lignes(mep, e.Plan).Count;
            if (c.Ligne is not { } ligne || ligne < 0 || ligne > nbLignes)
                throw ErreurHttp.Saisie($"Ligne obligatoire : entre 0 et {nbLignes} (nouvelle ligne en bas).");
            if (c.Position is not { } position || position < 0) throw ErreurHttp.Saisie("Position obligatoire, positive ou nulle.");
            Regles.Placer(mep, e, ligne, position, c.LiensParDefaut, c.NouvelleLigne);
            return await svc.EnregistrerContenu(mep, acteur, "etape.deplacee", $"{e.Code} placée ligne {e.Ligne + 1}");
        })));

        // ---------- Prise de main ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/prendre-la-main", async (Guid id, Guid eid, PriseDeMainSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            // Réservée aux participants reliés à un compte (dont le pilote et les suppléants) et aux administrateurs.
            var existant = mep.Participants.FirstOrDefault(p => p.UtilisateurId == acteur.Id);
            if (existant is null && !acteur.EstAdmin)
                throw ErreurHttp.Interdit("Seuls les participants de la MEP, le pilote, ses suppléants ou un administrateur peuvent prendre la main.");
            ServiceMep.ExigerRevision(e, c.Revision);
            var motif = ServiceMep.Texte(c.Motif);

            // Un administrateur qui n'est pas encore participant le devient (créé seulement si la prise de main aboutit).
            var preneur = existant ?? new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Prise en main", UtilisateurId = acteur.Id };
            var precedent = Regles.PrendreLaMain(mep, e, preneur, motif);
            if (existant is null) mep.Participants.Add(preneur);

            var avant = mep.Participants.FirstOrDefault(p => p.Id == precedent)?.Nom;
            var detail = $"{acteur.Nom} a pris la main sur {e.Code}" + (avant is null ? "" : $" (à la place de {avant})") + $" : {motif}";
            return await svc.Enregistrer(mep, acteur, "etape.prise-en-main", detail);
        })));

        // ---------- Exécution ----------
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/demarrer", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            // Un administrateur non participant le devient (créé seulement si le démarrage aboutit).
            var existant = mep.Participants.FirstOrDefault(p => p.UtilisateurId == acteur.Id);
            var executant = existant ?? (acteur.EstAdmin
                ? new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Exécutant", UtilisateurId = acteur.Id }
                : null);
            Regles.Demarrer(mep, e, executant, svc.Maintenant);
            if (existant is null && executant is not null) mep.Participants.Add(executant);
            return await svc.Enregistrer(mep, acteur, "etape.demarree", $"{e.Code} démarrée par {executant!.Nom}");
        })));

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/terminer", async (Guid id, Guid eid, TerminerSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Reussie ou Echec) est obligatoire.");
            var preuve = ServiceMep.Texte(c.Preuve);
            Regles.Terminer(mep, e, c.Resultat.Value, preuve, c.ResultatVerifie, acteur.Id, svc.Maintenant, c.Commentaire, ServiceMep.Texte(c.ResultatConstate));
            // Le résultat constaté d'un test unitaire est tracé au journal, s'il diffère de la preuve.
            var constate = e.Fin == FinEtape.TestUnitaire && e.ResultatConstate != preuve ? $"(test unitaire, constaté : {e.ResultatConstate})" : "";
            var detail = string.Join(" ", new[] { preuve, constate }.Where(t => t.Length > 0));
            return c.Resultat == EtatEtape.Reussie
                ? await svc.Enregistrer(mep, acteur, "etape.reussie", $"{e.Code} réussie : {detail}")
                : await svc.Enregistrer(mep, acteur, "etape.echec", $"{e.Code} en échec : {detail}");
        })));

        // Commentaire facultatif sur l'exécution (exécution, recette ou retour arrière).
        api.MapPut("/meps/{id:guid}/etapes/{eid:guid}/commentaire", async (Guid id, Guid eid, CommentaireSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.Commenter(mep, e, ServiceMep.Texte(c.Commentaire), acteur.Id, acteur.EstAdmin);
            var texte = e.Commentaire.Length > 200 ? e.Commentaire[..200] + "…" : e.Commentaire;
            return await svc.Enregistrer(mep, acteur, "etape.commentaire",
                texte.Length == 0 ? $"Commentaire sur {e.Code} effacé" : $"Commentaire sur {e.Code} : {texte}");
        })));

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/autoriser-non-retour", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.AutoriserNonRetour(mep, e, acteur.Id, svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "etape.non-retour-autorise", $"Passage du point de non-retour {e.Code} autorisé");
        })));

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/autoriser-reprise", async (Guid id, Guid eid, RevisionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var e = ServiceMep.TrouverEtape(mep, eid);
            ServiceMep.ExigerRevision(e, c.Revision);
            Regles.AutoriserReprise(mep, e, acteur.Id, svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "etape.reprise", $"Reprise de {e.Code} autorisée");
        })));
    }
}
