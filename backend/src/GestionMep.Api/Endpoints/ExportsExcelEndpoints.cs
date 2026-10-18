using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using GestionMep.Tableur;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Exportations Excel en lecture des vues (#58) : liste des MEP (filtres de la page d'accueil), préparation, étapes,
/// retour arrière et journal d'une MEP, historique d'un système du catalogue (#59). La recette et les objets impactés ont leurs propres routes (export et import).
/// Partagé avec <c>gestion-mep meps lister --excel</c> et <c>gestion-mep meps excel</c>. Les classeurs prennent les couleurs
/// de la charte visuelle (palette donnée par <see cref="ServiceCharte"/>).
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

    /// <summary>
    /// Liste des MEP (ou des modèles) filtrée comme la page d'accueil (<see cref="FiltreMeps"/>), en classeur Excel, avec
    /// l'onglet « Échecs ». Pour les modèles, seuls la recherche, l'équipe et le type s'appliquent.
    /// </summary>
    public static async Task<Fichier> Liste(MepDbContext db, bool modeles, FiltreMeps filtre, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        if (modeles) filtre = new FiltreMeps { Recherche = filtre.Recherche, Equipes = filtre.Equipes, Type = filtre.Type };
        var equipes = await db.Equipes.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nom);
        foreach (var e in filtre.Equipes)
            if (!equipes.ContainsKey(e)) throw ErreurHttp.NonTrouve("Équipe introuvable.");
        var catalogue = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
        var annuaire = await Personnes.Charger(db);
        var meps = filtre.Appliquer(await MepsEndpoints.Lister(db, modeles));
        var description = filtre.Decrire(catalogue.GetValueOrDefault, equipes.GetValueOrDefault, annuaire.NomPersonne);
        var echecs = modeles ? null : await HistoriqueSystemes.Echecs(db, meps);
        var nomPersonne = filtre.Personne is { } p ? annuaire.NomPersonne(p) : null;
        // Un seul système filtré : son nom dans le nom du fichier.
        var systeme = filtre.Systemes is [var seul]
            ? seul.StartsWith(FiltreMeps.PrefixeLibre, StringComparison.Ordinal) ? seul[FiltreMeps.PrefixeLibre.Length..]
                : Guid.TryParse(seul, out var id) ? catalogue.GetValueOrDefault(id) : null
            : null;
        return new(VuesExcel.ListeMeps(meps, modeles, description, equipes, maintenant, palette, filtre, echecs, nomPersonne),
            VuesExcel.NomFichierListe(modeles, maintenant, systeme));
    }

    /// <summary>Historique des MEP d'un système du catalogue en classeur Excel (route et <c>gestion-mep systemes historique --excel</c>).</summary>
    public static async Task<Fichier> Historique(MepDbContext db, Guid systemeId, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        var h = await HistoriqueSystemes.Historique(db, systemeId, DateOnly.FromDateTime(ExcelCommun.Locale(maintenant, null)));
        return new(VuesExcel.Historique(h, maintenant, palette), VuesExcel.NomFichierHistorique(h.Nom, maintenant));
    }

    /// <summary>
    /// Vue d'une MEP en classeur Excel. Journal : <paramref name="famille"/> (préfixe du type d'événement, « etape »…) et
    /// <paramref name="recherche"/> reprennent les filtres de l'onglet.
    /// </summary>
    public static async Task<Fichier> Vue(MepDbContext db, ServiceMep svc, Guid id, VuesExcel.Vue vue, string? famille = null, string? recherche = null,
        PaletteExcel? palette = null)
    {
        var entite = await svc.Charger(id);
        var mep = await svc.VersDto(entite, null);
        var maintenant = svc.Maintenant;
        byte[] contenu;
        switch (vue)
        {
            case VuesExcel.Vue.Preparation:
                contenu = VuesExcel.Preparation(mep, maintenant, palette);
                break;
            case VuesExcel.Vue.Etapes:
            case VuesExcel.Vue.RetourArriere:
                var ids = mep.Etapes.SelectMany(e => e.ResolutionIds).Distinct().ToList();
                var titres = await db.ResolutionsTypes.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Titre);
                contenu = VuesExcel.Plan(mep, vue == VuesExcel.Vue.Etapes ? TypePlan.Deploiement : TypePlan.RetourArriere, titres, maintenant, palette);
                break;
            default:
                var journal = VuesExcel.FiltrerJournal(await MepsEndpoints.Journal(db, id), famille, recherche);
                var filtres = new List<string>();
                if (!string.IsNullOrWhiteSpace(recherche)) filtres.Add($"recherche « {recherche.Trim()} »");
                if (!string.IsNullOrWhiteSpace(famille) && famille != "tous")
                    filtres.Add($"type {(VuesExcel.FamillesEvenement.TryGetValue(famille, out var l) ? l : famille)}");
                contenu = VuesExcel.Journal(mep, journal, filtres, MepMapping.TestsUnitaires(entite), maintenant, palette);
                break;
        }
        return new(contenu, VuesExcel.NomFichier(mep, vue));
    }

    public static void MapExportsExcel(this RouteGroupBuilder api)
    {
        // ?modele=true : modèles ; autres paramètres : filtres de la page d'accueil (FiltreMeps : systeme, environnement, equipe,
        // personne, role, etat, phase, type, du, au, echec, recherche).
        api.MapGet("/meps/excel", async (bool? modele, [AsParameters] ParametresFiltreMeps filtres, MepDbContext db, TimeProvider temps,
            ServiceCharte charte) =>
        {
            var f = await Liste(db, modele == true, FiltreMeps.Lire(filtres), temps.GetUtcNow(), charte.Courant.Palette);
            return Results.File(f.Contenu, VuesExcel.TypeContenu, f.Nom);
        }).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/catalogue/systemes/{id:guid}/historique/excel", async (Guid id, MepDbContext db, TimeProvider temps, ServiceCharte charte) =>
            Telecharger(await Historique(db, id, temps.GetUtcNow(), charte.Courant.Palette))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/preparation/excel", async (Guid id, MepDbContext db, ServiceMep svc, ServiceCharte charte) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Preparation, palette: charte.Courant.Palette))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/etapes/excel", async (Guid id, MepDbContext db, ServiceMep svc, ServiceCharte charte) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Etapes, palette: charte.Courant.Palette))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        api.MapGet("/meps/{id:guid}/retour-arriere/excel", async (Guid id, MepDbContext db, ServiceMep svc, ServiceCharte charte) =>
            Telecharger(await Vue(db, svc, id, VuesExcel.Vue.RetourArriere, palette: charte.Courant.Palette))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);

        // ?type : famille d'événement (etape, recette…) ; ?recherche : texte cherché dans le détail, l'auteur et son compte.
        api.MapGet("/meps/{id:guid}/journal/excel", async (Guid id, string? type, string? recherche, MepDbContext db, ServiceMep svc,
            ServiceCharte charte) => Telecharger(await Vue(db, svc, id, VuesExcel.Vue.Journal, type, recherche, charte.Courant.Palette))).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);
    }

    private static IResult Telecharger(Fichier f) => Results.File(f.Contenu, VuesExcel.TypeContenu, f.Nom);
}
