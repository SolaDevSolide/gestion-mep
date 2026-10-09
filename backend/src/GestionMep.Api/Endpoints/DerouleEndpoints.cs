using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Déroulé d'une MEP : circuit d'approbation et tests de recette (préparation, puis validation en phase de recette).</summary>
public static class DerouleEndpoints
{
    private static TestRecette TrouverTest(Mep mep, Guid tid) =>
        mep.TestsRecette.FirstOrDefault(t => t.Id == tid) ?? throw ErreurHttp.NonTrouve("Test de recette introuvable.");

    private static void Appliquer(Mep mep, TestRecette t, TestRecetteSaisie c)
    {
        if (c.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, c.ResponsableId, "Responsable du test");
        t.Libelle = ServiceMep.Texte(c.Libelle);
        t.ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu);
        t.ResponsableId = c.ResponsableId;
    }

    private static void Renumeroter(Mep mep)
    {
        var i = 1;
        foreach (var t in mep.TestsRecette.OrderBy(t => t.Ordre).ToList()) t.Ordre = i++;
        mep.TestsRecette = [.. mep.TestsRecette.OrderBy(t => t.Ordre)];
    }

    public static void MapDeroule(this RouteGroupBuilder api)
    {
        // ---------- Approbation ----------
        api.MapPut("/meps/{id:guid}/approbateurs", async (Guid id, ApprobateursSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerVersion(mep, c.Version);
            var ids = (c.UtilisateurIds ?? []).Distinct().ToList();
            var comptes = await db.Utilisateurs.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomAffiche);
            if (comptes.Count != ids.Count) throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
            Regles.DefinirApprobateurs(mep, ids.Select(i => (i, comptes[i])).ToList());
            var noms = mep.Approbations.Count == 0 ? "aucun" : string.Join(", ", mep.Approbations.Select(a => a.Nom));
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "approbateurs.modifies", $"Approbateurs : {noms}"));
        });

        api.MapPost("/meps/{id:guid}/approbation", async (Guid id, DecisionSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerVersion(mep, c.Version);
            if (c.Decision is null) throw ErreurHttp.Saisie("La décision (Approuvee ou Refusee) est obligatoire.");
            var a = Regles.Decider(mep, acteur.Id, c.Decision.Value, ServiceMep.Texte(c.Commentaire), svc.Maintenant);
            var suite = a.Commentaire.Length == 0 ? "" : $" : {a.Commentaire}";
            return Results.Ok(a.Decision == DecisionApprobation.Approuvee
                ? await svc.Enregistrer(mep, acteur, "approbation.approuvee", $"MEP approuvée par {acteur.Nom}{suite}")
                : await svc.Enregistrer(mep, acteur, "approbation.refusee", $"MEP refusée par {acteur.Nom}{suite}"));
        });

        // ---------- Tests de recette : préparation ----------
        api.MapPost("/meps/{id:guid}/tests-recette", async (Guid id, TestRecetteSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var t = new TestRecette { Ordre = mep.TestsRecette.Count == 0 ? 1 : mep.TestsRecette.Max(x => x.Ordre) + 1 };
            Appliquer(mep, t, c);
            mep.TestsRecette = [.. mep.TestsRecette, t];
            return Results.Ok(await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "recette.test-ajoute", $"Test de recette ajouté : {t.Ordre}. {t.Libelle}"));
        });

        api.MapPut("/meps/{id:guid}/tests-recette/{tid:guid}", async (Guid id, Guid tid, TestRecetteSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var t = TrouverTest(mep, tid);
            Appliquer(mep, t, c);
            mep.TestsRecette = [.. mep.TestsRecette]; // nouvelle liste : la colonne jsonb est réécrite
            return Results.Ok(await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "recette.test-modifie", $"Test de recette modifié : {t.Ordre}. {t.Libelle}"));
        });

        api.MapDelete("/meps/{id:guid}/tests-recette/{tid:guid}", async (Guid id, Guid tid, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var t = TrouverTest(mep, tid);
            mep.TestsRecette.Remove(t);
            Renumeroter(mep);
            return Results.Ok(await svc.EnregistrerContenu(mep, Acteur.Depuis(user), "recette.test-supprime", $"Test de recette supprimé : {t.Libelle}"));
        });

        api.MapPost("/meps/{id:guid}/tests-recette/{tid:guid}/deplacer", async (Guid id, Guid tid, DeplacerSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            Regles.ExigerPreparation(mep);
            var t = TrouverTest(mep, tid);
            var sens = (c.Sens ?? "").ToLowerInvariant();
            if (sens is not ("haut" or "bas")) throw ErreurHttp.Saisie("Sens attendu : haut ou bas.");
            var ordonnes = mep.TestsRecette.OrderBy(x => x.Ordre).ToList();
            var i = ordonnes.IndexOf(t);
            var j = sens == "haut" ? i - 1 : i + 1;
            if (j < 0 || j >= ordonnes.Count) return Results.Ok(await svc.VersDto(mep, acteur.Id));
            ordonnes.RemoveAt(i);
            ordonnes.Insert(j, t);
            for (var k = 0; k < ordonnes.Count; k++) ordonnes[k].Ordre = k + 1;
            mep.TestsRecette = ordonnes;
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "recette.test-deplace", $"Test de recette déplacé : {t.Libelle}"));
        });

        // ---------- Tests de recette : validation (phase Recette) ----------
        api.MapPost("/meps/{id:guid}/tests-recette/{tid:guid}/resultat", async (Guid id, Guid tid, ControleSaisie c, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var t = TrouverTest(mep, tid);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Conforme ou Echec) est obligatoire.");
            Regles.ValiderTestRecette(mep, t, c.Resultat.Value, ServiceMep.Texte(c.Observation), acteur.Id, svc.Maintenant);
            mep.TestsRecette = [.. mep.TestsRecette];
            var resultat = t.Resultat == ResultatControle.Conforme ? "conforme" : "en échec";
            return Results.Ok(await svc.Enregistrer(mep, acteur, "recette.test", $"Test de recette {t.Ordre} {resultat} : {t.Observation}"));
        });

        api.MapPost("/meps/{id:guid}/tests-recette/{tid:guid}/refaire", async (Guid id, Guid tid, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var t = TrouverTest(mep, tid);
            Regles.RefaireTestRecette(mep, t, acteur.Id);
            mep.TestsRecette = [.. mep.TestsRecette];
            return Results.Ok(await svc.Enregistrer(mep, acteur, "recette.refaire", $"Test de recette {t.Ordre} à refaire"));
        });
    }
}
