using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Exportations Excel en lecture des vues (#58) : liste des MEP (filtres de la page d'accueil), préparation, étapes,
/// retour arrière et journal d'une MEP. La recette et les objets impactés ont leurs propres routes (export et import).
/// Partagé avec <c>gestion-mep meps lister --excel</c> et <c>gestion-mep meps excel</c>.
/// </summary>
public static class ExportsExcelEndpoints
{
    public record Fichier(byte[] Contenu, string Nom);

    /// <summary>Phase écrite comme dans l'API (« Execution ») ou la ligne de commande (« retour-arriere »).</summary>
    public static Phase? LirePhase(string? texte) =>
        string.IsNullOrWhiteSpace(texte) || texte == "toutes" ? null
            : Enum.TryParse<Phase>(texte.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out var p) && Enum.IsDefined(p) ? p
            : throw ErreurHttp.Saisie("Phase inconnue : preparation, execution, recette, retour-arriere ou cloturee.");

    public static TypeMep? LireType(string? texte) => texte?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "complete" => TypeMep.Complete,
        "simplifiee" => TypeMep.Simplifiee,
        _ => throw ErreurHttp.Saisie("Type de MEP inconnu : complete ou simplifiee."),
    };

    /// <summary>Liste des MEP (ou des modèles) filtrée comme la page d'accueil, en classeur Excel.</summary>
    public static async Task<Fichier> Liste(MepDbContext db, bool modeles, Guid? equipe, Phase? phase, TypeMep? type, string? recherche,
        DateTimeOffset maintenant)
    {
        var equipes = await db.Equipes.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nom);
        if (equipe is { } e && !equipes.ContainsKey(e)) throw ErreurHttp.NonTrouve("Équipe introuvable.");
        var meps = VuesExcel.FiltrerMeps(await MepsEndpoints.Lister(db, modeles, equipe), modeles ? null : phase, type, recherche);
        var filtres = new List<string>();
        if (!string.IsNullOrWhiteSpace(recherche)) filtres.Add($"recherche « {recherche.Trim()} »");
        if (equipe is { } eq) filtres.Add($"équipe {equipes[eq]}");
        if (!modeles && phase is { } p) filtres.Add($"phase {VuesExcel.Libelle(p)}");
        if (type is { } t) filtres.Add(t == TypeMep.Simplifiee ? "MEP simplifiées" : "MEP complètes");
        return new(VuesExcel.ListeMeps(meps, modeles, filtres, equipes, maintenant), VuesExcel.NomFichierListe(modeles, maintenant));
    }

    /// <summary>
    /// Vue d'une MEP en classeur Excel. Journal : <paramref name="famille"/> (préfixe du type d'événement, « etape »…) et
    /// <paramref name="recherche"/> reprennent les filtres de l'onglet.
    /// </summary>
    public static async Task<Fichier> Vue(MepDbContext db, ServiceMep svc, Guid id, VuesExcel.Vue vue, string? famille = null, string? recherche = null)
    {
        var entite = await svc.Charger(id);
        var mep = await svc.VersDto(entite, null);
        var maintenant = svc.Maintenant;
        byte[] contenu;
        switch (vue)
        {
            case VuesExcel.Vue.Preparation:
                contenu = VuesExcel.Preparation(mep, maintenant);
                break;
            case VuesExcel.Vue.Etapes:
            case VuesExcel.Vue.RetourArriere:
                var ids = mep.Etapes.SelectMany(e => e.ResolutionIds).Distinct().ToList();
                var titres = await db.ResolutionsTypes.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Titre);
                contenu = VuesExcel.Plan(mep, vue == VuesExcel.Vue.Etapes ? TypePlan.Deploiement : TypePlan.RetourArriere, titres, maintenant);
                break;
            default:
                var journal = VuesExcel.FiltrerJournal(await MepsEndpoints.Journal(db, id), famille, recherche);
                var filtres = new List<string>();
                if (!string.IsNullOrWhiteSpace(recherche)) filtres.Add($"recherche « {recherche.Trim()} »");
                if (!string.IsNullOrWhiteSpace(famille) && famille != "tous")
                    filtres.Add($"type {(VuesExcel.FamillesEvenement.TryGetValue(famille, out var l) ? l : famille)}");
                contenu = VuesExcel.Journal(mep, journal, filtres, MepMapping.TestsUnitaires(entite), maintenant);
                break;
        }
        return new(contenu, VuesExcel.NomFichier(mep, vue));
    }

    public static void MapExportsExcel(this RouteGroupBuilder api)
    {
        // ?modele=true : modèles ; ?equipe, ?phase, ?type (complete|simplifiee), ?recherche : filtres de la page d'accueil.
        api.MapGet("/meps/excel", async (bool? modele, Guid? equipe, string? phase, string? type, string? recherche, MepDbContext db,
            TimeProvider temps) =>
        {
            var f = await Liste(db, modele == true, equipe, LirePhase(phase), LireType(type), recherche, temps.GetUtcNow());
            return Results.File(f.Contenu, VuesExcel.TypeContenu, f.Nom);
        }).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/preparation/excel", async (Guid id, MepDbContext db, ServiceMep svc) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Preparation))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/etapes/excel", async (Guid id, MepDbContext db, ServiceMep svc) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Etapes))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/retour-arriere/excel", async (Guid id, MepDbContext db, ServiceMep svc) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.RetourArriere))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        // ?type : famille d'événement (etape, recette…) ; ?recherche : texte cherché dans le détail, l'auteur et son compte.
        api.MapGet("/meps/{id:guid}/journal/excel", async (Guid id, string? type, string? recherche, MepDbContext db, ServiceMep svc) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Journal, type, recherche))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);
    }

    private static IResult Telecharger(Fichier f) => Results.File(f.Contenu, VuesExcel.TypeContenu, f.Nom);
}
