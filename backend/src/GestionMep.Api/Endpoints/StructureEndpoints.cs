using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Participants, systèmes et livrables : modifiables uniquement en préparation.
/// Chaque modification remet les approbations à zéro (<see cref="ServiceMep.EnregistrerContenu"/>).
/// </summary>
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
        api.MapPost("/meps/{id:guid}/participants", async (Guid id, ParticipantSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var nom = Requis(c.Nom, "Le nom");
            await svc.ExigerUtilisateurExistant(c.UtilisateurId);
            mep.Participants.Add(new Participant { MepId = mep.Id, Nom = nom, Role = ServiceMep.Texte(c.Role), Contact = ServiceMep.Texte(c.Contact), UtilisateurId = c.UtilisateurId });
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "participant.ajoute", $"Participant ajouté : {nom}");
        })));

        api.MapPut("/meps/{id:guid}/participants/{pid:guid}", async (Guid id, Guid pid, ParticipantSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var p = mep.Participants.FirstOrDefault(x => x.Id == pid) ?? throw ErreurHttp.NonTrouve("Participant introuvable.");
            // Le compte relié au pilote ou à un suppléant porte leurs droits (Go, clôture…) : comme pour désigner le pilote,
            // seul le pilote, un suppléant ou un administrateur peut le changer. Contrôle fait sur l'état avant modification.
            if (p.UtilisateurId != c.UtilisateurId && (mep.PiloteId == pid || mep.SuppleantIds.Contains(pid)))
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, un suppléant ou un administrateur peut changer le compte relié au pilote ou à un suppléant.");
            p.Nom = Requis(c.Nom, "Le nom");
            await svc.ExigerUtilisateurExistant(c.UtilisateurId);
            p.Role = ServiceMep.Texte(c.Role);
            p.Contact = ServiceMep.Texte(c.Contact);
            p.UtilisateurId = c.UtilisateurId;
            return await svc.EnregistrerContenu(mep, acteur, "participant.modifie", $"Participant modifié : {p.Nom}");
        })));

        api.MapDelete("/meps/{id:guid}/participants/{pid:guid}", async (Guid id, Guid pid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var p = mep.Participants.FirstOrDefault(x => x.Id == pid) ?? throw ErreurHttp.NonTrouve("Participant introuvable.");
            if (mep.PiloteId == pid) throw new RegleMetierException($"{p.Nom} est le pilote : désigner un autre pilote d'abord.");
            if (mep.SuppleantIds.Contains(pid)) throw new RegleMetierException($"{p.Nom} est suppléant : le retirer des suppléants d'abord.");
            if (mep.ResponsablesTechniques.Any(r => r.ParticipantId == pid)) throw new RegleMetierException($"{p.Nom} est responsable technique : le retirer des responsables techniques d'abord.");
            var executee = mep.EtapesTriees().FirstOrDefault(e => e.ExecutantId == pid);
            if (executee is not null) throw new RegleMetierException($"{p.Nom} a exécuté {executee.Code} : il ne peut pas être supprimé.");
            if (mep.Sauvegarde.ResponsableId == pid) throw new RegleMetierException($"{p.Nom} est responsable de la sauvegarde : changer le responsable d'abord.");
            mep.Participants.Remove(p);
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "participant.supprime", $"Participant supprimé : {p.Nom}");
        })));

        // ---------- Responsables techniques ----------
        api.MapPut("/meps/{id:guid}/responsables-techniques", async (Guid id, ResponsablesTechniquesSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            ServiceMep.ExigerVersion(mep, c.Version);
            var responsables = (c.Responsables ?? []).Select(r =>
            {
                if (r.ParticipantId is null) throw ErreurHttp.Saisie("Responsable technique : le participant est obligatoire.");
                if (r.Disponibilite is null) throw ErreurHttp.Saisie("Responsable technique : la disponibilité (Joignable ou Absent) est obligatoire.");
                return new ResponsableTechnique
                {
                    ParticipantId = r.ParticipantId.Value, SystemeIds = r.SystemeIds ?? [], Perimetre = r.Perimetre ?? "",
                    Disponibilite = r.Disponibilite.Value, ContactJourJ = r.ContactJourJ ?? "",
                };
            }).ToList();
            Regles.DefinirResponsablesTechniques(mep, responsables);
            var noms = mep.ResponsablesTechniques.Count == 0 ? "aucun"
                : string.Join(", ", mep.ResponsablesTechniques.Select(r => mep.Participants.First(p => p.Id == r.ParticipantId).Nom));
            return await svc.EnregistrerContenu(mep, acteur, "responsables-techniques.modifies", $"Responsables techniques : {noms}");
        })));

        // ---------- Systèmes ----------
        api.MapPost("/meps/{id:guid}/systemes", async (Guid id, SystemeSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var nom = Requis(c.Nom, "Le nom");
            mep.Systemes.Add(new Systeme { MepId = mep.Id, Nom = nom, Cible = ServiceMep.Texte(c.Cible), Impact = ServiceMep.Texte(c.Impact) });
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "systeme.ajoute", $"Système ajouté : {nom}");
        })));

        api.MapPut("/meps/{id:guid}/systemes/{sid:guid}", async (Guid id, Guid sid, SystemeSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var s = mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");
            s.Nom = Requis(c.Nom, "Le nom");
            s.Cible = ServiceMep.Texte(c.Cible);
            s.Impact = ServiceMep.Texte(c.Impact);
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "systeme.modifie", $"Système modifié : {s.Nom}");
        })));

        api.MapPut("/meps/{id:guid}/systemes/{sid:guid}/sauvegarde", async (Guid id, Guid sid, SauvegardeSystemeSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            ServiceMep.ExigerVersion(mep, c.Version);
            var s = mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");
            if (c.Statut is null) throw ErreurHttp.Saisie("Le statut (ARenseigner, Requise ou NonRequise) est obligatoire.");
            Regles.DefinirSauvegardeSysteme(mep, s, c.Statut.Value, c.Moment ?? ModeSauvegarde.AvantMep, c.Contenu ?? "", c.Justification ?? "",
                c.Emplacement, c.MethodeRecuperation);
            return await svc.EnregistrerContenu(mep, acteur, "sauvegarde.systeme", Regles.DetailSauvegarde(s));
        })));

        api.MapDelete("/meps/{id:guid}/systemes/{sid:guid}", async (Guid id, Guid sid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var s = mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");
            var etape = mep.EtapesTriees().FirstOrDefault(e => e.SystemeIds.Contains(sid));
            if (etape is not null) throw new RegleMetierException($"Le système {s.Nom} est utilisé par {etape.Code} : changer le système de l'étape d'abord.");
            foreach (var l in mep.Livrables.Where(l => l.SystemeId == sid)) l.SystemeId = null;
            // Ses fiches de recette passent « Sans système ».
            if (mep.FichesRecette.Any(f => f.SystemeId == sid))
                mep.FichesRecette = mep.FichesRecette.Select(f => { if (f.SystemeId == sid) f.SystemeId = null; return f; }).ToList();
            if (mep.ResponsablesTechniques.Any(r => r.SystemeIds.Contains(sid)))
                mep.ResponsablesTechniques = mep.ResponsablesTechniques.Select(r => { r.SystemeIds = r.SystemeIds.Where(x => x != sid).ToList(); return r; }).ToList();
            mep.Systemes.Remove(s);
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "systeme.supprime", $"Système supprimé : {s.Nom}");
        })));

        // ---------- Livrables ----------
        api.MapPost("/meps/{id:guid}/livrables", async (Guid id, LivrableSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var objet = Requis(c.Objet, "L'objet");
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = objet, Version = ServiceMep.Texte(c.Version), Emplacement = ServiceMep.Texte(c.Emplacement), SystemeId = c.SystemeId });
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "livrable.ajoute", $"Livrable ajouté : {objet}");
        })));

        api.MapPut("/meps/{id:guid}/livrables/{lid:guid}", async (Guid id, Guid lid, LivrableSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var l = mep.Livrables.FirstOrDefault(x => x.Id == lid) ?? throw ErreurHttp.NonTrouve("Livrable introuvable.");
            l.Objet = Requis(c.Objet, "L'objet");
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            l.Version = ServiceMep.Texte(c.Version);
            l.Emplacement = ServiceMep.Texte(c.Emplacement);
            l.SystemeId = c.SystemeId;
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "livrable.modifie", $"Livrable modifié : {l.Objet}");
        })));

        api.MapDelete("/meps/{id:guid}/livrables/{lid:guid}", async (Guid id, Guid lid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var l = mep.Livrables.FirstOrDefault(x => x.Id == lid) ?? throw ErreurHttp.NonTrouve("Livrable introuvable.");
            mep.Livrables.Remove(l);
            return await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "livrable.supprime", $"Livrable supprimé : {l.Objet}");
        })));
    }
}
