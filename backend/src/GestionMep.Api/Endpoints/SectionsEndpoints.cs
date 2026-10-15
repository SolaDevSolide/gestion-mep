using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Graphe libre et sections du plan (#60, #76), en préparation : liens entre étapes et cercles, sections et leurs cercles, et
/// verrous d'édition des étapes (panneau d'édition ouvert). Les sections sont des repères : l'exécution ne lit que les
/// préalables effectifs des étapes, recalculés après chaque opération.
/// </summary>
public static class SectionsEndpoints
{
    private static Section TrouverSection(Mep mep, Guid sid) =>
        mep.Sections.FirstOrDefault(s => s.Id == sid) ?? throw ErreurHttp.NonTrouve("Section introuvable.");

    private static PortSection TrouverPort(Section s, Guid pid) =>
        s.Ports.FirstOrDefault(p => p.Id == pid) ?? throw ErreurHttp.NonTrouve("Cercle introuvable.");

    private static string Requis(string? valeur, string libelle)
    {
        var t = ServiceMep.Texte(valeur);
        return t.Length == 0 ? throw ErreurHttp.Saisie($"{libelle} est obligatoire.") : t;
    }

    private static string NomPort(PortSection p) => $"Cercle {(p.Sens == SensPort.Entree ? "d'entrée" : "de sortie")} « {p.Titre} »";

    /// <summary>Plan des deux extrémités d'un lien (404 si l'une est introuvable, 422 si elles sont dans deux plans).</summary>
    private static (TypePlan Plan, Guid De, Guid Vers) Lien(Mep mep, LienSaisie c)
    {
        if (c.De is not { } de || c.Vers is not { } vers) throw ErreurHttp.Saisie("Un lien se désigne par « de » et « vers ».");
        TypePlan? PlanDe(Guid id) =>
            Regles.TrouverNoeud(mep, TypePlan.Deploiement, id) is not null ? TypePlan.Deploiement
            : Regles.TrouverNoeud(mep, TypePlan.RetourArriere, id) is not null ? TypePlan.RetourArriere : null;
        var planDe = PlanDe(de) ?? throw ErreurHttp.NonTrouve("Étape ou cercle introuvable.");
        var planVers = PlanDe(vers) ?? throw ErreurHttp.NonTrouve("Étape ou cercle introuvable.");
        if (planDe != planVers) throw new RegleMetierException("Un lien relie deux éléments du même plan.");
        return (planDe, de, vers);
    }

    public static void MapSections(this RouteGroupBuilder api)
    {
        // ---------- Liens du graphe ----------

        // « vers » attend « de » : étapes ou cercles (voir la documentation de l'API pour les liens possibles).
        api.MapPost("/meps/{id:guid}/liens", async (Guid id, LienSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var (plan, deId, versId) = Lien(mep, c);
            var (de, vers) = Regles.Relier(mep, plan, deId, versId);
            return await svc.EnregistrerContenu(mep, acteur, "plan.lien-ajoute", $"Lien ajouté : {vers} attend {de}");
        })));

        api.MapPost("/meps/{id:guid}/liens/retirer", async (Guid id, LienSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var (plan, deId, versId) = Lien(mep, c);
            var (de, vers) = Regles.Delier(mep, plan, deId, versId);
            return await svc.EnregistrerContenu(mep, acteur, "plan.lien-retire", $"Lien retiré : {vers} n'attend plus {de}");
        })));

        // ---------- Sections ----------

        api.MapPost("/meps/{id:guid}/sections", async (Guid id, SectionSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            Guid sectionId = default;
            var dto = await svc.Modifier(id, async mep =>
            {
                var acteur = Acteur.Depuis(user);
                ServiceMep.ExigerEditeur(mep, acteur);
                Regles.ExigerPreparation(mep);
                var titre = Requis(c.Titre, "Le titre de la section");
                var s = Regles.CreerSection(mep, c.Plan ?? TypePlan.Deploiement, titre, ServiceMep.Texte(c.Description),
                    ServiceMep.ValiderSystemeIds(mep, c.SystemeIds), acteur.Id, svc.Maintenant, c.Placee ?? false);
                sectionId = s.Id;
                return await svc.EnregistrerContenu(mep, acteur, "section.creee",
                    $"Section « {s.Titre} » créée{(s.Plan == TypePlan.RetourArriere ? " (retour arrière)" : "")}{(s.Placee ? "" : ", dans le bac")}");
            });
            return Results.Created($"/api/meps/{id}/sections/{sectionId}", new SectionCreeeDto(sectionId, dto));
        });

        api.MapPut("/meps/{id:guid}/sections/{sid:guid}", async (Guid id, Guid sid, SectionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var s = TrouverSection(mep, sid);
            if (c.Revision is null) throw ErreurHttp.Saisie("La révision de la section est obligatoire.");
            if (c.Revision != s.Revision)
                throw ErreurHttp.Conflit($"La section « {s.Titre} » a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
            var ancien = s.Titre;
            var description = s.Description;
            var systemes = s.SystemeIds.ToList();
            Regles.ModifierSection(mep, s, Requis(c.Titre, "Le titre de la section"), ServiceMep.Texte(c.Description),
                ServiceMep.ValiderSystemeIds(mep, c.SystemeIds));
            var evenements = new List<ServiceMep.Evenement>();
            if (s.Titre != ancien) evenements.Add(new("section.renommee", $"Section « {ancien} » renommée « {s.Titre} »"));
            var autres = new List<string>();
            if (s.Description != description) autres.Add("description");
            if (!s.SystemeIds.SequenceEqual(systemes)) autres.Add("systèmes");
            if (autres.Count > 0) evenements.Add(new("section.modifiee", $"Section « {s.Titre} » modifiée : {string.Join(", ", autres)}"));
            if (evenements.Count == 0) return await svc.VersDto(mep, acteur.Id);
            return await svc.EnregistrerContenu(mep, acteur, evenements);
        })));

        // Placer dans le graphe, ou remettre dans le bac.
        api.MapPut("/meps/{id:guid}/sections/{sid:guid}/placement", async (Guid id, Guid sid, PlacementSectionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            if (c.Placee is not { } placee) throw ErreurHttp.Saisie("Préciser « placee » (true : dans le plan ; false : dans le bac).");
            if (!Regles.PlacerSection(mep, s, placee)) return await svc.VersDto(mep, acteur.Id);
            return placee
                ? await svc.EnregistrerContenu(mep, acteur, "section.placee", $"Section « {s.Titre} » placée dans le plan depuis le bac")
                : await svc.EnregistrerContenu(mep, acteur, "section.bac", $"Section « {s.Titre} » remise dans le bac");
        })));

        // Ordre gauche-droite du graphe (disposition seulement : aucun lien ne change, les approbations restent).
        api.MapPut("/meps/{id:guid}/ordre", async (Guid id, OrdreGrapheSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            if (c.Plan is not { } plan) throw ErreurHttp.Saisie("Préciser le plan.");
            if (c.Ids is not { Count: > 0 } ids) throw ErreurHttp.Saisie("Indiquer l'ordre des éléments.");
            Regles.Ordonner(mep, plan, ids);
            return await svc.Enregistrer(mep, acteur, "graphe.ordre",
                plan == TypePlan.Deploiement ? "Ordre du graphe de déploiement modifié" : "Ordre du graphe de retour arrière modifié");
        })));

        // Ses étapes restent dans le plan, avec leurs préalables effectifs.
        api.MapDelete("/meps/{id:guid}/sections/{sid:guid}", async (Guid id, Guid sid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            var etapes = mep.Etapes.Count(e => e.SectionId == s.Id);
            Regles.SupprimerSection(mep, s);
            return await svc.EnregistrerContenu(mep, acteur, "section.supprimee",
                $"Section « {s.Titre} » supprimée" + (etapes == 0 ? "" : $" : ses {etapes} étape(s) restent dans le plan, avec leurs liens"));
        })));

        // ---------- Cercles ----------

        api.MapPost("/meps/{id:guid}/sections/{sid:guid}/ports", async (Guid id, Guid sid, PortSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            if (c.Sens is not { } sens) throw ErreurHttp.Saisie("Le sens du cercle (Entree ou Sortie) est obligatoire.");
            var p = Regles.AjouterPort(mep, s, sens, ServiceMep.Texte(c.Titre), ServiceMep.Texte(c.Description));
            return await svc.EnregistrerContenu(mep, acteur, "section.cercle-ajoute", $"{NomPort(p)} ajouté à la section « {s.Titre} »");
        })));

        api.MapPut("/meps/{id:guid}/sections/{sid:guid}/ports/ordre", async (Guid id, Guid sid, OrdrePortsSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            if (c.Sens is not { } sens) throw ErreurHttp.Saisie("Le sens des cercles (Entree ou Sortie) est obligatoire.");
            Regles.OrdonnerPorts(mep, s, sens, c.Ports ?? []);
            return await svc.EnregistrerContenu(mep, acteur, "section.modifiee",
                $"Section « {s.Titre} » : cercles {(sens == SensPort.Entree ? "d'entrée" : "de sortie")} réordonnés");
        })));

        api.MapPut("/meps/{id:guid}/sections/{sid:guid}/ports/{pid:guid}", async (Guid id, Guid sid, Guid pid, PortSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            var p = TrouverPort(s, pid);
            var ancien = p.Titre;
            var description = p.Description;
            Regles.ModifierPort(mep, s, p, Requis(c.Titre, "Le titre du cercle"), ServiceMep.Texte(c.Description));
            if (p.Titre == ancien && p.Description == description) return await svc.VersDto(mep, acteur.Id);
            return await svc.EnregistrerContenu(mep, acteur, "section.cercle-modifie", p.Titre == ancien
                ? $"{NomPort(p)} de la section « {s.Titre} » : description modifiée"
                : $"Cercle « {ancien} » de la section « {s.Titre} » renommé « {p.Titre} »");
        })));

        // Les liens qui passaient par le cercle disparaissent avec lui.
        api.MapDelete("/meps/{id:guid}/sections/{sid:guid}/ports/{pid:guid}", async (Guid id, Guid sid, Guid pid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            var s = TrouverSection(mep, sid);
            var p = TrouverPort(s, pid);
            Regles.SupprimerPort(mep, s, p);
            return await svc.EnregistrerContenu(mep, acteur, "section.cercle-supprime", $"{NomPort(p)} supprimé de la section « {s.Titre} »");
        })));

        // ---------- Verrous d'édition des étapes ----------

        // Prise ou renouvellement (toutes les 20 s environ tant que le panneau est ouvert) ; expire après une minute sans signe de vie.
        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/verrou", async (Guid id, Guid eid, VerrouSaisie c, ClaimsPrincipal user, ServiceMep svc, DiffuseurMep diffuseur) =>
        {
            var session = ServiceMep.Texte(c.Session);
            if (session.Length is 0 or > 64) throw ErreurHttp.Saisie("La session (identifiant de l'onglet, 64 caractères au plus) est obligatoire.");
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var e = ServiceMep.TrouverEtape(mep, eid);
            var (pris, verrou) = diffuseur.PrendreVerrou(mep.Id, e.Id, acteur.Id, acteur.Nom, session, c.PrendreLaMain);
            return Results.Ok(new PriseVerrouDto(pris, verrou));
        });

        // Fermeture du panneau : relâché s'il est tenu par cet onglet (sinon rien ne change).
        api.MapDelete("/meps/{id:guid}/etapes/{eid:guid}/verrou", (Guid id, Guid eid, string? session, ClaimsPrincipal user, DiffuseurMep diffuseur) =>
        {
            diffuseur.RelacherVerrou(id, eid, Acteur.Depuis(user).Id, ServiceMep.Texte(session));
            return Results.NoContent();
        });

        api.MapGet("/meps/{id:guid}/verrous", async (Guid id, MepDbContext db, DiffuseurMep diffuseur) =>
        {
            if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
            return diffuseur.Verrous(id);
        });
    }
}
