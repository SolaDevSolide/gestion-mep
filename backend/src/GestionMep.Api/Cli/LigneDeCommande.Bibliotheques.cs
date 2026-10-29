using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// Bibliothèques partagées : étapes modèles, modèles de recette et modèles de texte des équipes. Consultation, suppression et
/// rétablissement ; la rédaction se fait dans l'application (éditeur avec aperçu et verrou de version).
/// </summary>
public static partial class LigneDeCommande
{
    private static async Task<Guid?> IdSysteme(MepDbContext db, Options o) =>
        o.Valeur("--systeme") is { } s ? (await TrouverSysteme(db, s)).Id : null;

    private static async Task<Dictionary<Guid, string>> NomsSystemes(MepDbContext db) =>
        await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);

    // ---------- Étapes modèles ----------

    private static async Task<Guid> IdEtapeModele(MepDbContext db, string texte) =>
        IdentifiantOuPrefixe(texte, await db.ModelesEtape.AsNoTracking().Select(m => m.Id).ToListAsync(), "étapes modèles")
            ?? throw ErreurHttp.NonTrouve($"Étape modèle « {texte} » introuvable (gestion-mep etapes-modeles lister).");

    private static async Task<int> EtapesModelesLister(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--systeme"]);
        await using var db = Base(Configuration());
        var liste = await EtapesModelesEndpoints.Lister(db, await IdSysteme(db, o));
        if (EnJson)
        {
            EcrireJson(liste);
            return 0;
        }
        if (liste.Count == 0)
        {
            Informer("Aucune étape modèle (elles se créent dans l'application, Bibliothèques > Étapes modèles).");
            return 0;
        }
        var systemes = await NomsSystemes(db);
        Ecrire($"{"ID",-8}  {"UTIL.",5}  {"TYPE",-11}  ACTION  [SYSTÈMES]");
        foreach (var m in liste)
            Ecrire($"{m.Id.ToString()[..8]}  {m.Utilisations,5}  {m.Type,-11}  {m.Action}"
                + (m.SystemeCatalogueIds.Count > 0 ? $"  [{string.Join(", ", m.SystemeCatalogueIds.Select(id => systemes.GetValueOrDefault(id, "?")))}]" : ""));
        return 0;
    }

    private static async Task<int> EtapesModelesAfficher(string texte)
    {
        await using var db = Base(Configuration());
        var m = await EtapesModelesEndpoints.Detail(db, await IdEtapeModele(db, texte));
        if (EnJson)
        {
            EcrireJson(m);
            return 0;
        }
        var systemes = await NomsSystemes(db);
        var resolutions = await db.ResolutionsTypes.AsNoTracking().Where(r => m.ResolutionIds.Contains(r.Id)).Select(r => r.Titre).ToListAsync();
        Ecrire($"Étape modèle    : {m.Action}");
        Ecrire($"Identifiant     : {m.Id}");
        Ecrire($"Type            : {m.Type}{(m.Criticite is { } c ? $", criticité {c}" : "")}{(m.DureeEstimeeMinutes is { } d ? $", {d} min" : "")}");
        Ecrire($"Systèmes        : {(m.SystemeCatalogueIds.Count == 0 ? "tous" : string.Join(", ", m.SystemeCatalogueIds.Select(id => systemes.GetValueOrDefault(id, "?"))))}");
        Ecrire($"Fin             : {m.Fin}");
        Ecrire($"Instructions    : {Texte(m.Instructions)}");
        Ecrire($"Résultat attendu: {Texte(m.ResultatAttendu)}");
        if (m.Type == TypeEtape.Sauvegarde)
        {
            Ecrire($"Sauvegarde      : {Texte(m.SauvegardeEmplacement)}");
            Ecrire($"Récupération    : {Texte(m.SauvegardeMethodeRecuperation)}");
        }
        if (resolutions.Count > 0) Ecrire($"Résolutions     : {string.Join(", ", resolutions)}");
        Ecrire($"Utilisations    : {m.Utilisations} MEP");
        Ecrire($"Version         : {m.Version}, modifiée le {Date(m.ModifieLe)}{(m.ModifiePar is { } p ? $" par {p}" : "")}");
        return 0;
    }

    private static async Task<int> EtapesModelesSupprimer(string texte)
    {
        await using var db = Base(Configuration());
        var action = await EtapesModelesEndpoints.Supprimer(db, null, await IdEtapeModele(db, texte));
        Resultat(new { Action = action });
        Informer($"Étape modèle « {action} » supprimée (les étapes déjà insérées dans des MEP ne changent pas).");
        return 0;
    }

    // ---------- Modèles de recette ----------

    private static async Task<Guid> IdModeleRecette(MepDbContext db, string texte)
    {
        var modeles = await db.ModelesRecette.AsNoTracking().Select(m => new { m.Id, m.Titre }).ToListAsync();
        var parTitre = modeles.Where(m => m.Titre.Equals(texte.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (parTitre.Count > 1)
            throw ErreurHttp.Saisie($"Plusieurs modèles de recette s'appellent « {texte} » (un par système) : donner l'identifiant (gestion-mep modeles-recette lister).");
        if (parTitre.Count == 1) return parTitre[0].Id;
        return IdentifiantOuPrefixe(texte, modeles.Select(m => m.Id), "modèles de recette")
            ?? throw ErreurHttp.NonTrouve($"Modèle de recette « {texte} » introuvable (gestion-mep modeles-recette lister).");
    }

    private static async Task<int> ModelesRecetteLister(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--systeme"]);
        await using var db = Base(Configuration());
        var liste = await RecetteEndpoints.ListerModeles(db, await IdSysteme(db, o));
        if (EnJson)
        {
            EcrireJson(liste);
            return 0;
        }
        if (liste.Count == 0)
        {
            Informer("Aucun modèle de recette (ils se créent dans l'application, Bibliothèques > Modèles de recette).");
            return 0;
        }
        var systemes = await NomsSystemes(db);
        Ecrire($"{"ID",-8}  {"UTIL.",5}  {"ÉTAPES",6}  {"SYSTÈME",-16}  TITRE");
        foreach (var m in liste)
            Ecrire($"{m.Id.ToString()[..8]}  {m.Utilisations,5}  {m.Etapes.Count,6}  {systemes.GetValueOrDefault(m.SystemeCatalogueId, "?"),-16}  {m.Titre}");
        return 0;
    }

    private static async Task<int> ModelesRecetteAfficher(string texte)
    {
        await using var db = Base(Configuration());
        var m = await RecetteEndpoints.DetailModele(db, await IdModeleRecette(db, texte));
        if (EnJson)
        {
            EcrireJson(m);
            return 0;
        }
        var systemes = await NomsSystemes(db);
        Ecrire($"Modèle de recette : {m.Titre}");
        Ecrire($"Identifiant       : {m.Id}");
        Ecrire($"Système           : {systemes.GetValueOrDefault(m.SystemeCatalogueId, "?")}");
        Ecrire($"Utilisations      : {m.Utilisations} MEP");
        Ecrire($"Version           : {m.Version}, modifié le {Date(m.ModifieLe)}{(m.ModifiePar is { } p ? $" par {p}" : "")}");
        Ecrire("Étapes de test    :");
        foreach (var e in m.Etapes)
            Ecrire($"  {e.Ordre,2}. {e.Instructions}{(e.ResultatAttendu.Length > 0 ? $"  →  {e.ResultatAttendu}" : "")}");
        return 0;
    }

    private static async Task<int> ModelesRecetteSupprimer(string texte)
    {
        await using var db = Base(Configuration());
        var titre = await RecetteEndpoints.SupprimerModele(db, null, await IdModeleRecette(db, texte));
        Resultat(new { Titre = titre });
        Informer($"Modèle de recette « {titre} » supprimé (les fiches déjà insérées dans des MEP ne changent pas).");
        return 0;
    }

    // ---------- Modèles de texte ----------

    private static async Task<(ModeleTexte Modele, Equipe Equipe)> ModeleTexte(MepDbContext db, string texte)
    {
        var id = IdentifiantOuPrefixe(texte, await db.ModelesTexte.AsNoTracking().Select(m => m.Id).ToListAsync(), "modèles de texte")
            ?? throw ErreurHttp.NonTrouve($"Modèle de texte « {texte} » introuvable (gestion-mep modeles-texte lister).");
        var m = await db.ModelesTexte.FirstAsync(m => m.Id == id);
        return (m, await db.Equipes.FirstAsync(e => e.Id == m.EquipeId));
    }

    private sealed record ModeleTexteCliDto(Guid Id, string Equipe, string Nom, UsageModeleTexte Usage, string Objet, string Corps, bool Fourni,
        bool Modifie, int Version, DateTimeOffset ModifieLe, string? ModifiePar);

    private static async Task<List<ModeleTexteCliDto>> ModelesTexte(MepDbContext db, Guid? equipe)
    {
        var equipes = await db.Equipes.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nom);
        return (await CommunicationEndpoints.Lister(db, Auth.Acteur.LigneDeCommande, equipe))
            .OrderBy(m => equipes.GetValueOrDefault(m.EquipeId, ""), StringComparer.OrdinalIgnoreCase)
            .Select(m => new ModeleTexteCliDto(m.Id, equipes.GetValueOrDefault(m.EquipeId, "?"), m.Nom, m.Usage, m.Objet, m.Corps, m.Fourni, m.Modifie,
                m.Version, m.ModifieLe, m.ModifiePar)).ToList();
    }

    private static async Task<int> ModelesTexteLister(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--equipe"]);
        await using var db = Base(Configuration());
        Guid? equipe = o.Valeur("--equipe") is { } nom ? (await EquipesEndpoints.Trouver(db, nom)).Id : null;
        var liste = await ModelesTexte(db, equipe);
        if (EnJson)
        {
            EcrireJson(liste.Select(m => new { m.Id, m.Equipe, m.Nom, m.Usage, m.Fourni, m.Modifie, m.Version, m.ModifieLe }));
            return 0;
        }
        if (liste.Count == 0)
        {
            Informer("Aucun modèle de texte (chaque équipe reçoit les modèles fournis à sa création).");
            return 0;
        }
        var largeur = Math.Max(6, liste.Max(m => m.Equipe.Length));
        Ecrire($"{"ID",-8}  {"ÉQUIPE".PadRight(largeur)}  {"USAGE",-11}  NOM");
        foreach (var m in liste)
            Ecrire($"{m.Id.ToString()[..8]}  {m.Equipe.PadRight(largeur)}  {m.Usage,-11}  {m.Nom}{(m.Fourni ? m.Modifie ? "  (fourni, modifié)" : "  (fourni)" : "")}");
        return 0;
    }

    private static async Task<int> ModelesTexteAfficher(string texte)
    {
        await using var db = Base(Configuration());
        var (modele, equipe) = await ModeleTexte(db, texte);
        var m = (await ModelesTexte(db, equipe.Id)).First(x => x.Id == modele.Id);
        if (EnJson)
        {
            EcrireJson(m);
            return 0;
        }
        Ecrire($"Modèle de texte : {m.Nom}{(m.Fourni ? m.Modifie ? " (fourni, modifié)" : " (fourni)" : "")}");
        Ecrire($"Identifiant     : {m.Id}");
        Ecrire($"Équipe          : {m.Equipe}");
        Ecrire($"Usage           : {m.Usage}");
        Ecrire($"Version         : {m.Version}, modifié le {Date(m.ModifieLe)}{(m.ModifiePar is { } p ? $" par {p}" : "")}");
        Ecrire($"Objet           : {Texte(m.Objet)}");
        Ecrire("");
        Ecrire(m.Corps);
        return 0;
    }

    private static async Task<int> ModelesTexteSupprimer(string texte)
    {
        await using var db = Base(Configuration());
        var (m, e) = await ModeleTexte(db, texte);
        await CommunicationEndpoints.SupprimerModele(db, null, m, e);
        Resultat(new { m.Id, m.Nom, Equipe = e.Nom });
        Informer($"Modèle de texte « {m.Nom} » de l'équipe « {e.Nom} » supprimé.");
        return 0;
    }

    private static async Task<int> ModelesTexteRetablir(string texte)
    {
        await using var db = Base(Configuration());
        var (m, e) = await ModeleTexte(db, texte);
        if (m.CleFournie is null)
            throw new RegleMetierException($"« {m.Nom} » a été créé par l'équipe : il n'a pas de texte d'origine à rétablir.");
        if (!ModelesTexteFournis.EstModifie(m))
        {
            Informer($"Le modèle « {m.Nom} » de l'équipe « {e.Nom} » a déjà son texte d'origine : rien à faire.");
            Resultat(new { m.Id, m.Nom, Equipe = e.Nom, Modifie = false });
            return 0;
        }
        await CommunicationEndpoints.RetablirModele(db, null, m, e, DateTimeOffset.UtcNow);
        Resultat(new { m.Id, m.Nom, Equipe = e.Nom, Modifie = true });
        Informer($"Texte d'origine du modèle « {m.Nom} » de l'équipe « {e.Nom} » rétabli.");
        return 0;
    }

    private static Task<int> ModelesTexteVariables()
    {
        var variables = VariablesTexte.Toutes.Select(v => new VariableTexteDto(v.Cle, v.Libelle, v.Groupe, v.Description, v.Liste)).ToList();
        if (EnJson)
        {
            EcrireJson(variables);
            return Fait(0);
        }
        foreach (var groupe in variables.GroupBy(v => v.Groupe))
        {
            Ecrire($"{groupe.Key} :");
            foreach (var v in groupe) Ecrire($"  {{{{{v.Cle}}}}}".PadRight(32) + $"{v.Libelle}{(v.Description.Length > 0 ? $" : {v.Description}" : "")}");
        }
        return Fait(0);
    }
}
