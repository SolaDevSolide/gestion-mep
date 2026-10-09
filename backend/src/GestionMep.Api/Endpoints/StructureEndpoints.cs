using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Api.Endpoints;

/// <summary>Participants, systèmes et livrables : modifiables uniquement en préparation.</summary>
public static class StructureEndpoints
{
    private static string Requis(string? valeur, string libelle)
    {
        var t = ServiceMep.Texte(valeur);
        return t.Length == 0 ? throw ErreurHttp.Saisie($"{libelle} est obligatoire.") : t;
    }

    public static void MapStructure(this RouteGroupBuilder api)
    {
        // ---------- Participants ----------
        api.MapPost("/meps/{id:guid}/participants", async (Guid id, ParticipantSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var nom = Requis(c.Nom, "Le nom");
            await svc.ExigerUtilisateurExistant(c.UtilisateurId);
            mep.Participants.Add(new Participant { MepId = mep.Id, Nom = nom, Role = ServiceMep.Texte(c.Role), Contact = ServiceMep.Texte(c.Contact), UtilisateurId = c.UtilisateurId });
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "participant.ajoute", $"Participant ajouté : {nom}"));
        });

        api.MapPut("/meps/{id:guid}/participants/{pid:guid}", async (Guid id, Guid pid, ParticipantSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var p = mep.Participants.FirstOrDefault(x => x.Id == pid) ?? throw ErreurHttp.NonTrouve("Participant introuvable.");
            p.Nom = Requis(c.Nom, "Le nom");
            await svc.ExigerUtilisateurExistant(c.UtilisateurId);
            p.Role = ServiceMep.Texte(c.Role);
            p.Contact = ServiceMep.Texte(c.Contact);
            p.UtilisateurId = c.UtilisateurId;
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "participant.modifie", $"Participant modifié : {p.Nom}"));
        });

        api.MapDelete("/meps/{id:guid}/participants/{pid:guid}", async (Guid id, Guid pid, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var p = mep.Participants.FirstOrDefault(x => x.Id == pid) ?? throw ErreurHttp.NonTrouve("Participant introuvable.");
            if (mep.PiloteId == pid) throw new RegleMetierException($"{p.Nom} est le pilote : désigner un autre pilote d'abord.");
            if (mep.SuppleantId == pid) throw new RegleMetierException($"{p.Nom} est le suppléant : désigner un autre suppléant d'abord.");
            var etape = mep.Etapes.FirstOrDefault(e => e.ResponsableId == pid);
            if (etape is not null) throw new RegleMetierException($"{p.Nom} est responsable de {etape.Code} : changer le responsable d'abord.");
            var executee = mep.Etapes.FirstOrDefault(e => e.ExecutantId == pid);
            if (executee is not null) throw new RegleMetierException($"{p.Nom} a pris la main sur {executee.Code} : il ne peut pas être supprimé.");
            if (mep.Sauvegarde.ResponsableId == pid) throw new RegleMetierException($"{p.Nom} est responsable de la sauvegarde : changer le responsable d'abord.");
            mep.Participants.Remove(p);
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "participant.supprime", $"Participant supprimé : {p.Nom}"));
        });

        // ---------- Systèmes ----------
        api.MapPost("/meps/{id:guid}/systemes", async (Guid id, SystemeSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var nom = Requis(c.Nom, "Le nom");
            mep.Systemes.Add(new Systeme { MepId = mep.Id, Nom = nom, Cible = ServiceMep.Texte(c.Cible), Impact = ServiceMep.Texte(c.Impact) });
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "systeme.ajoute", $"Système ajouté : {nom}"));
        });

        api.MapPut("/meps/{id:guid}/systemes/{sid:guid}", async (Guid id, Guid sid, SystemeSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var s = mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");
            s.Nom = Requis(c.Nom, "Le nom");
            s.Cible = ServiceMep.Texte(c.Cible);
            s.Impact = ServiceMep.Texte(c.Impact);
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "systeme.modifie", $"Système modifié : {s.Nom}"));
        });

        api.MapDelete("/meps/{id:guid}/systemes/{sid:guid}", async (Guid id, Guid sid, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var s = mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");
            var etape = mep.Etapes.FirstOrDefault(e => e.SystemeId == sid);
            if (etape is not null) throw new RegleMetierException($"Le système {s.Nom} est utilisé par {etape.Code} : changer le système de l'étape d'abord.");
            foreach (var l in mep.Livrables.Where(l => l.SystemeId == sid)) l.SystemeId = null;
            mep.Systemes.Remove(s);
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "systeme.supprime", $"Système supprimé : {s.Nom}"));
        });

        // ---------- Livrables ----------
        api.MapPost("/meps/{id:guid}/livrables", async (Guid id, LivrableSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var objet = Requis(c.Objet, "L'objet");
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = objet, Version = ServiceMep.Texte(c.Version), Emplacement = ServiceMep.Texte(c.Emplacement), SystemeId = c.SystemeId });
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "livrable.ajoute", $"Livrable ajouté : {objet}"));
        });

        api.MapPut("/meps/{id:guid}/livrables/{lid:guid}", async (Guid id, Guid lid, LivrableSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var l = mep.Livrables.FirstOrDefault(x => x.Id == lid) ?? throw ErreurHttp.NonTrouve("Livrable introuvable.");
            l.Objet = Requis(c.Objet, "L'objet");
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            l.Version = ServiceMep.Texte(c.Version);
            l.Emplacement = ServiceMep.Texte(c.Emplacement);
            l.SystemeId = c.SystemeId;
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "livrable.modifie", $"Livrable modifié : {l.Objet}"));
        });

        api.MapDelete("/meps/{id:guid}/livrables/{lid:guid}", async (Guid id, Guid lid, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            Regles.ExigerPreparation(mep);
            var l = mep.Livrables.FirstOrDefault(x => x.Id == lid) ?? throw ErreurHttp.NonTrouve("Livrable introuvable.");
            mep.Livrables.Remove(l);
            return Results.Ok(await svc.Enregistrer(mep, Acteur.Depuis(user), "livrable.supprime", $"Livrable supprimé : {l.Objet}"));
        });
    }
}
