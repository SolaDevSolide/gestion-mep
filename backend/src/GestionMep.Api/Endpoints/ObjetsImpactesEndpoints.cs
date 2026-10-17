using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using GestionMep.Tableur;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Environnements et objets impactés des systèmes d'une MEP (préparation uniquement) : choix dans la fiche du système au
/// catalogue, et objets impactés en Excel (modèle, export, import).
/// </summary>
public static class ObjetsImpactesEndpoints
{
    private const string TypeExcel = ExcelCommun.TypeContenu;

    /// <summary>Fiches du catalogue des systèmes de la MEP, par identifiant.</summary>
    private static async Task<Dictionary<Guid, SystemeCatalogue>> Fiches(MepDbContext db, Mep mep)
    {
        var ids = mep.Systemes.Select(s => s.CatalogueId).OfType<Guid>().Distinct().ToList();
        return await db.SystemesCatalogue.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
    }

    private static Systeme TrouverSysteme(Mep mep, Guid sid) =>
        mep.Systemes.FirstOrDefault(x => x.Id == sid) ?? throw ErreurHttp.NonTrouve("Système introuvable.");

    public static void MapObjetsImpactes(this RouteGroupBuilder api)
    {
        // ---------- Environnements d'un système ----------
        api.MapPut("/meps/{id:guid}/systemes/{sid:guid}/environnements", async (Guid id, Guid sid, EnvironnementsSystemeSaisie c, ClaimsPrincipal user,
            ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var s = TrouverSysteme(mep, sid);
            var fiche = s.CatalogueId is { } cid ? await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid) : null;
            Regles.DefinirEnvironnementsSysteme(mep, s, c.Noms ?? [], fiche?.Environnements);
            var noms = s.Environnements.Count == 0 ? "aucun" : string.Join(", ", s.Environnements.Select(e => e.Nom));
            return await svc.EnregistrerContenu(mep, acteur, "systeme.environnements", $"Environnements de {s.Nom} : {noms}");
        })));

        // ---------- Objets impactés ----------
        api.MapPut("/meps/{id:guid}/systemes/{sid:guid}/objets", async (Guid id, Guid sid, ObjetsSystemeSaisie c, ClaimsPrincipal user,
            ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var s = TrouverSysteme(mep, sid);
            if (s.CatalogueId is not { } cid)
                throw new RegleMetierException($"{s.Nom} n'est pas issu du catalogue : ses objets impactés se choisissent dans la fiche d'un système du catalogue.");
            var fiche = await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid)
                ?? throw new RegleMetierException($"La fiche {s.Nom} n'existe plus dans le catalogue.");
            var choix = (c.Objets ?? []).Select(o => (o.ObjetCatalogueId ?? throw ErreurHttp.Saisie("Objet du catalogue obligatoire."), o.Version ?? "")).ToList();
            var bilan = Regles.DefinirObjetsSysteme(mep, s, fiche, choix);
            if (bilan.Ajoutes + bilan.Modifies + bilan.Retires == 0) return await svc.VersDto(mep, acteur.Id);
            var total = mep.ObjetsImpactes.Count(o => o.SystemeId == s.Id);
            var details = new List<string>();
            if (bilan.Ajoutes > 0) details.Add($"{bilan.Ajoutes} ajouté(s)");
            if (bilan.Modifies > 0) details.Add($"{bilan.Modifies} modifié(s)");
            if (bilan.Retires > 0) details.Add($"{bilan.Retires} retiré(s)");
            return await svc.EnregistrerContenu(mep, acteur, "objets.modifies", $"Objets impactés de {s.Nom} : {total} ({string.Join(", ", details)})");
        })));

        api.MapDelete("/meps/{id:guid}/objets/{oid:guid}", async (Guid id, Guid oid, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            var o = mep.ObjetsImpactes.FirstOrDefault(x => x.Id == oid) ?? throw ErreurHttp.NonTrouve("Objet impacté introuvable.");
            mep.ObjetsImpactes.Remove(o);
            var systeme = mep.Systemes.FirstOrDefault(s => s.Id == o.SystemeId)?.Nom;
            return await svc.EnregistrerContenu(mep, acteur, "objets.modifies",
                $"Objet impacté retiré : {o.Nom} ({(systeme is null ? "" : systeme + " › ")}{o.Categorie})");
        })));

        // ---------- Excel : modèle, export, import ----------
        // ?modele=true : classeur vide à remplir ; sinon les objets de la MEP. Les deux se réimportent.
        api.MapGet("/meps/{id:guid}/objets/excel", async (Guid id, bool? modele, ServiceMep svc, MepDbContext db, TimeProvider temps) =>
        {
            var mep = await svc.Charger(id);
            var vide = modele == true;
            return Results.File(ObjetsExcel.Exporter(mep, await Fiches(db, mep), vide, temps.GetUtcNow()), TypeExcel, ObjetsExcel.NomFichier(mep, vide));
        }).Produces(StatusCodes.Status200OK, contentType: TypeExcel);

        // Import : les lignes rattachées au catalogue sont ajoutées (ou leur version mise à jour) ; les autres sont rejetées et
        // listées, sans rien créer au catalogue. Rien n'est retiré de la MEP.
        api.MapPost("/meps/{id:guid}/objets/excel", async (Guid id, HttpRequest req, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var contenu = await RecetteEndpoints.LireFichier(req);
            var acteur = Acteur.Depuis(user);
            ImportObjetsDto? resultat = null;
            await svc.Modifier(id, async mep =>
            {
                ServiceMep.ExigerEditeur(mep, acteur);
                Regles.ExigerPreparation(mep);
                ObjetsExcel.Lecture lecture;
                try
                {
                    lecture = ObjetsExcel.Lire(contenu, mep, await Fiches(db, mep));
                }
                catch (ClasseurIllisibleException ex)
                {
                    throw ErreurHttp.Regle([ex.Message], "Fichier refusé : ");
                }
                if (lecture.Erreur is { } erreur) throw ErreurHttp.Regle([erreur], "Fichier refusé : ");
                if (lecture.Retenues.Count == 0 && lecture.Rejetees.Count == 0)
                    throw ErreurHttp.Regle(["Aucun objet dans ce fichier."], "Fichier refusé : ");
                var bilan = Regles.ImporterObjets(mep, lecture.Retenues);
                var dto = bilan.Ajoutes + bilan.Modifies == 0
                    ? await svc.VersDto(mep, acteur.Id)
                    : await svc.EnregistrerContenu(mep, acteur, "objets.importes",
                        $"Objets impactés importés d'Excel : {bilan.Ajoutes} ajouté(s), {bilan.Modifies} version(s) mise(s) à jour"
                        + (lecture.Rejetees.Count > 0 ? $", {lecture.Rejetees.Count} ligne(s) rejetée(s)" : ""));
                resultat = new ImportObjetsDto(dto, bilan.Ajoutes, bilan.Modifies, bilan.Inchanges, lecture.Rejetees);
                return dto;
            });
            return Results.Ok(resultat);
        }).WithMetadata(new RequestSizeLimitAttribute(RecetteEndpoints.TailleFichierMax + 64 * 1024));
    }
}
