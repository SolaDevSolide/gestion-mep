using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Déroulé d'une MEP : circuit d'approbation et fiches de recette (préparation, puis déroulé en phase de recette).</summary>
public static class DerouleEndpoints
{
    private static FicheRecette TrouverFiche(Mep mep, Guid fid) =>
        mep.FichesRecette.FirstOrDefault(f => f.Id == fid) ?? throw ErreurHttp.NonTrouve("Fiche de recette introuvable.");

    private static EtapeTest TrouverEtapeTest(FicheRecette f, Guid tid) =>
        f.Etapes.FirstOrDefault(t => t.Id == tid) ?? throw ErreurHttp.NonTrouve("Étape de test introuvable.");

    public static void MapDeroule(this RouteGroupBuilder api)
    {
        // ---------- Approbation ----------
        api.MapPut("/meps/{id:guid}/approbateurs", async (Guid id, ApprobateursSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerVersion(mep, c.Version);
            var ids = (c.UtilisateurIds ?? []).Distinct().ToList();
            var comptes = await db.Utilisateurs.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomAffiche);
            if (comptes.Count != ids.Count) throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
            Regles.DefinirApprobateurs(mep, ids.Select(i => (i, comptes[i])).ToList());
            var noms = mep.Approbations.Count == 0 ? "aucun" : string.Join(", ", mep.Approbations.Select(a => a.Nom));
            return await svc.EnregistrerContenu(mep, acteur, "approbateurs.modifies", $"Approbateurs : {noms}");
        })));

        api.MapPost("/meps/{id:guid}/approbation", async (Guid id, DecisionSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerVersion(mep, c.Version);
            if (c.Decision is null) throw ErreurHttp.Saisie("La décision (Approuvee ou Refusee) est obligatoire.");
            var a = Regles.Decider(mep, acteur.Id, c.Decision.Value, ServiceMep.Texte(c.Commentaire), svc.Maintenant);
            var suite = a.Commentaire.Length == 0 ? "" : $" : {a.Commentaire}";
            return a.Decision == DecisionApprobation.Approuvee
                ? await svc.Enregistrer(mep, acteur, "approbation.approuvee", $"MEP approuvée par {acteur.Nom}{suite}")
                : await svc.Enregistrer(mep, acteur, "approbation.refusee", $"MEP refusée par {acteur.Nom}{suite}");
        })));

        // ---------- Fiches de recette : préparation ----------
        api.MapPost("/meps/{id:guid}/fiches-recette", async (Guid id, FicheRecetteCreationSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            var f = Regles.AjouterFicheRecette(mep, c.SystemeId, ServiceMep.Texte(c.Titre));
            return await svc.EnregistrerContenu(mep, acteur, "recette.fiche-ajoutee", $"{Regles.NomRecette(f)} ajoutée");
        })));

        api.MapPut("/meps/{id:guid}/fiches-recette/{fid:guid}", async (Guid id, Guid fid, FicheRecetteSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var f = TrouverFiche(mep, fid);
            ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
            Regles.ModifierFicheRecette(mep, f, c.SystemeId, ServiceMep.Texte(c.Titre),
                (c.Etapes ?? []).Select(t => (t.Id, ServiceMep.Texte(t.Instructions), ServiceMep.Texte(t.ResultatAttendu))));
            return await svc.EnregistrerContenu(mep, acteur, "recette.fiche-modifiee", $"{Regles.NomRecette(f)} modifiée");
        })));

        api.MapDelete("/meps/{id:guid}/fiches-recette/{fid:guid}", async (Guid id, Guid fid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var f = TrouverFiche(mep, fid);
            var nom = Regles.NomRecette(f);
            Regles.SupprimerFicheRecette(mep, f);
            return await svc.EnregistrerContenu(mep, acteur, "recette.fiche-supprimee", $"{nom} supprimée");
        })));

        // ---------- Fiches de recette : déroulé (phase Recette) ----------
        api.MapPost("/meps/{id:guid}/fiches-recette/{fid:guid}/etapes/{tid:guid}/resultat", async (Guid id, Guid fid, Guid tid, ControleSaisie c,
            ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var f = TrouverFiche(mep, fid);
            var t = TrouverEtapeTest(f, tid);
            if (c.Resultat is null) throw ErreurHttp.Saisie("Le résultat (Conforme ou Echec) est obligatoire.");
            Regles.ValiderEtapeTest(mep, f, t, c.Resultat.Value, ServiceMep.Texte(c.Observation), acteur.Id, acteur.EstAdmin, svc.Maintenant);
            var resultat = t.Resultat == ResultatControle.Conforme ? "conforme" : "en échec";
            return await svc.Enregistrer(mep, acteur, "recette.test", $"{Regles.NomRecette(f)} étape {t.Ordre} {resultat} : {t.Observation}");
        })));

        api.MapPost("/meps/{id:guid}/fiches-recette/{fid:guid}/refaire", async (Guid id, Guid fid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var f = TrouverFiche(mep, fid);
            Regles.RefaireRecette(mep, f, null, acteur.Id);
            return await svc.Enregistrer(mep, acteur, "recette.refaire", $"{Regles.NomRecette(f)} à refaire");
        })));

        api.MapPost("/meps/{id:guid}/fiches-recette/{fid:guid}/etapes/{tid:guid}/refaire", async (Guid id, Guid fid, Guid tid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            var f = TrouverFiche(mep, fid);
            var t = TrouverEtapeTest(f, tid);
            Regles.RefaireRecette(mep, f, t, acteur.Id);
            return await svc.Enregistrer(mep, acteur, "recette.refaire", $"{Regles.NomRecette(f)} étape {t.Ordre} à refaire");
        })));
    }
}
