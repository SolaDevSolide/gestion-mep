using System.Globalization;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>Catalogue des systèmes, critères go / no-go, résolutions types, MEP et journal d'administration.</summary>
public static partial class LigneDeCommande
{
    // ---------- Catalogue des systèmes ----------

    /// <summary>Système du catalogue désigné par son nom (sans tenir compte de la casse) ou son identifiant.</summary>
    private static SystemeCatalogue TrouverSysteme(IReadOnlyList<SystemeCatalogue> catalogue, string nomOuId) =>
        catalogue.FirstOrDefault(s => s.Nom.Equals(nomOuId.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? (Guid.TryParse(nomOuId, out var id) ? catalogue.FirstOrDefault(s => s.Id == id) : null)
        ?? throw new ErreurHttp(1, $"Système « {nomOuId} » introuvable dans le catalogue (gestion-mep systemes lister).");

    private static async Task<SystemeCatalogue> TrouverSysteme(MepDbContext db, string nomOuId) =>
        TrouverSysteme(await db.SystemesCatalogue.AsNoTracking().ToListAsync(), nomOuId);

    private static async Task<int> SystemesLister()
    {
        await using var db = Base(Configuration());
        var systemes = await CatalogueEndpoints.Lister(db);
        if (EnJson)
        {
            EcrireJson(systemes);
            return 0;
        }
        if (systemes.Count == 0)
        {
            Console.WriteLine("Catalogue vide : « gestion-mep systemes ajouter <nom> ».");
            return 0;
        }
        var largeur = Math.Max(3, systemes.Max(s => s.Nom.Length));
        Console.WriteLine($"{"NOM".PadRight(largeur)}  ACTIF  {"ENVIRONNEMENTS",-40}  {"OBJETS",-6}  {"CIBLE",-20}  DESCRIPTION");
        foreach (var s in systemes)
        {
            var envs = string.Join(", ", s.Environnements.Select(e => e.Production ? e.Nom + "*" : e.Nom));
            Console.WriteLine($"{s.Nom.PadRight(largeur)}  {(s.Actif ? "oui" : "non"),-5}  {envs,-40}  {s.NbObjets,-6}  {s.Cible,-20}  {s.Description}");
        }
        Console.WriteLine("* environnement de production");
        return 0;
    }

    private static async Task<int> SystemesAfficher(string nom)
    {
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, nom);
        var objets = CatalogueEndpoints.Objets(s);
        if (EnJson)
        {
            EcrireJson(new { Systeme = CatalogueEndpoints.Dto(s), Objets = objets });
            return 0;
        }
        Console.WriteLine($"{s.Nom}{(s.Actif ? "" : " (désactivé)")} : {(s.Description.Length > 0 ? s.Description : "sans description")}");
        Console.WriteLine();
        Console.WriteLine("Environnements" + (s.Environnements.Count == 0 ? " : aucun" : ""));
        foreach (var e in s.Environnements)
            Console.WriteLine($"  {e.Nom,-20}  {(e.Production ? "production" : ""),-10}  {e.Cible}");
        Console.WriteLine();
        Console.WriteLine("Objets par catégorie" + (s.Categories.Count == 0 ? " : aucune catégorie" : ""));
        foreach (var c in s.Categories)
        {
            var liste = objets.Where(o => o.Categorie == c).ToList();
            Console.WriteLine($"  {c} ({liste.Count})");
            foreach (var o in liste)
                Console.WriteLine($"    {o.Nom,-30}  {o.Type,-14}  {o.Libelle}{(o.Precision.Length > 0 ? $" ({o.Precision})" : "")}");
        }
        return 0;
    }

    /// <summary>Ajoute ou modifie un environnement de la fiche (par son nom), ou le retire avec --retirer.</summary>
    private static async Task<int> SystemesEnvironnement(string systeme, string nom, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--cible", "--production"], "--retirer");
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, systeme);
        var liste = s.Environnements.Select(e => new EnvironnementCatalogueSaisie(e.Nom, e.Cible, e.Production)).ToList();
        var i = liste.FindIndex(e => string.Equals(e.Nom, nom.Trim(), StringComparison.OrdinalIgnoreCase));
        if (o.A("--retirer"))
        {
            if (i < 0) throw new ErreurHttp(1, $"Environnement « {nom} » introuvable dans la fiche {s.Nom}.");
            liste.RemoveAt(i);
        }
        else if (i < 0) liste.Add(new EnvironnementCatalogueSaisie(nom, o.Valeur("--cible"), o.OuiNon("--production")));
        else liste[i] = new EnvironnementCatalogueSaisie(nom, o.Valeur("--cible") ?? liste[i].Cible, o.OuiNon("--production") ?? liste[i].Production);
        var r = await CatalogueEndpoints.DefinirEnvironnements(db, null, s.Id, new EnvironnementsCatalogueSaisie(liste));
        Console.WriteLine($"Environnements de {r.Nom} : {(r.Environnements.Count == 0 ? "aucun" : string.Join(", ", r.Environnements.Select(e => e.Nom)))}.");
        return 0;
    }

    /// <summary>Ajoute une catégorie à la fiche, la renomme (--renommer) ou la retire avec ses objets (--retirer).</summary>
    private static async Task<int> SystemesCategorie(string systeme, string categorie, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--renommer"], "--retirer");
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, systeme);
        var liste = s.Categories.Select(c => new CategorieSaisie(c)).ToList();
        var i = liste.FindIndex(c => string.Equals(c.Nom, categorie.Trim(), StringComparison.OrdinalIgnoreCase));
        if (o.A("--retirer") || o.Valeur("--renommer") is not null)
        {
            if (i < 0) throw new ErreurHttp(1, $"Catégorie « {categorie} » introuvable dans la fiche {s.Nom}.");
            if (o.A("--retirer")) liste.RemoveAt(i);
            else liste[i] = new CategorieSaisie(o.Valeur("--renommer"), liste[i].Nom);
        }
        else if (i < 0) liste.Add(new CategorieSaisie(categorie));
        var r = await CatalogueEndpoints.DefinirCategories(db, null, s.Id, new CategoriesSystemeSaisie(liste));
        Console.WriteLine($"Catégories de {r.Nom} : {(r.Categories.Count == 0 ? "aucune" : string.Join(", ", r.Categories))}.");
        return 0;
    }

    /// <summary>Ajoute ou modifie un objet (repéré par sa catégorie et son nom), ou le retire avec --retirer.</summary>
    private static async Task<int> SystemesObjet(string systeme, string categorie, string nom, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--type", "--libelle", "--precision"], "--retirer");
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, systeme);
        var existant = s.Objets.FirstOrDefault(x => x.Categorie.Equals(categorie.Trim(), StringComparison.OrdinalIgnoreCase)
            && x.Nom.Equals(nom.Trim(), StringComparison.OrdinalIgnoreCase));
        if (o.A("--retirer"))
        {
            if (existant is null) throw new ErreurHttp(1, $"Objet « {nom} » introuvable dans la catégorie {categorie} de {s.Nom}.");
            await CatalogueEndpoints.SupprimerObjet(db, null, s.Id, existant.Id);
            Console.WriteLine($"Objet « {existant.Nom} » retiré de la fiche {s.Nom}.");
            return 0;
        }
        var r = await CatalogueEndpoints.EnregistrerObjet(db, null, s.Id, existant?.Id, new ObjetCatalogueSaisie(categorie, o.Valeur("--type") ?? existant?.Type,
            nom, o.Valeur("--libelle") ?? existant?.Libelle, o.Valeur("--precision") ?? existant?.Precision));
        Console.WriteLine($"Objet « {r.Nom} » {(existant is null ? "ajouté à" : "modifié dans")} la catégorie {r.Categorie} de {s.Nom}.");
        return 0;
    }

    // ---------- Catégories d'objets par défaut ----------

    private static async Task<int> CategoriesLister()
    {
        await using var db = Base(Configuration());
        var liste = await CatalogueEndpoints.CategoriesDefaut(db);
        if (EnJson)
        {
            EcrireJson(liste);
            return 0;
        }
        if (liste.Count == 0) Console.WriteLine("Aucune catégorie par défaut : « gestion-mep categories ajouter \"<nom>\" ».");
        foreach (var c in liste) Console.WriteLine(c);
        return 0;
    }

    private static async Task<int> CategoriesModifier(string nom, bool ajouter)
    {
        await using var db = Base(Configuration());
        var liste = await CatalogueEndpoints.CategoriesDefaut(db);
        var existante = liste.FirstOrDefault(c => c.Equals(nom.Trim(), StringComparison.OrdinalIgnoreCase));
        if (ajouter && existante is not null) throw new ErreurHttp(1, $"La catégorie « {existante} » est déjà dans la liste par défaut.");
        if (!ajouter && existante is null) throw new ErreurHttp(1, $"Catégorie « {nom} » introuvable dans la liste par défaut.");
        var nouvelle = ajouter ? [.. liste, nom] : liste.Where(c => c != existante).ToList();
        await CatalogueEndpoints.DefinirCategoriesDefaut(db, null, new CategoriesDefautSaisie(nouvelle));
        Console.WriteLine(ajouter ? $"Catégorie « {nom.Trim()} » ajoutée à la liste par défaut."
            : $"Catégorie « {existante} » retirée de la liste par défaut (les systèmes qui l'utilisent la gardent).");
        return 0;
    }

    private static async Task<int> SystemesAjouter(string nom, string[] arguments)
    {
        if (nom.StartsWith('-')) throw new ErreurUsage("nom du système attendu");
        var o = Options.Lire(arguments, ["--description", "--cible", "--actif"]);
        await using var db = Base(Configuration());
        var s = await CatalogueEndpoints.Creer(db, null, new SystemeCatalogueSaisie(nom, o.Valeur("--description"), o.Valeur("--cible"), o.OuiNon("--actif")));
        Console.WriteLine($"Système « {s.Nom} » ajouté au catalogue{(s.Actif ? "" : " (désactivé)")}.");
        return 0;
    }

    private static async Task<int> SystemesModifier(string nom, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--nom", "--description", "--cible", "--actif"]);
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, nom);
        var modifie = await CatalogueEndpoints.Modifier(db, null, s.Id, new SystemeCatalogueSaisie(
            o.Valeur("--nom") ?? s.Nom, o.Valeur("--description") ?? s.Description, o.Valeur("--cible") ?? s.Cible, o.OuiNon("--actif")));
        Console.WriteLine($"Système « {modifie.Nom} » modifié{(modifie.Actif ? "" : " (désactivé)")}.");
        return 0;
    }

    private static async Task<int> SystemesSupprimer(string nom)
    {
        await using var db = Base(Configuration());
        var s = await TrouverSysteme(db, nom);
        Console.WriteLine(await CatalogueEndpoints.Supprimer(db, null, s.Id)
            ? $"Système « {s.Nom} » supprimé du catalogue."
            : $"Système « {s.Nom} » utilisé par une MEP ou une résolution type : désactivé au lieu d'être supprimé.");
        return 0;
    }

    // ---------- Critères go / no-go ----------

    /// <summary>Critère désigné par son n° dans « criteres lister » (1, 2…) ou par son identifiant.</summary>
    private static async Task<CritereGoNoGoDto> TrouverCritere(MepDbContext db, string numeroOuId)
    {
        var criteres = await ParametresEndpoints.ListerCriteres(db);
        if (Guid.TryParse(numeroOuId, out var id))
            return criteres.FirstOrDefault(c => c.Id == id) ?? throw new ErreurHttp(1, "Critère introuvable.");
        if (int.TryParse(numeroOuId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n >= 1 && n <= criteres.Count)
            return criteres[n - 1];
        throw new ErreurHttp(1, $"Critère « {numeroOuId} » introuvable : donner son n° (gestion-mep criteres lister) ou son identifiant.");
    }

    private static async Task<int> CriteresLister()
    {
        await using var db = Base(Configuration());
        var criteres = await ParametresEndpoints.ListerCriteres(db);
        if (EnJson)
        {
            EcrireJson(criteres);
            return 0;
        }
        if (criteres.Count == 0)
        {
            Console.WriteLine("Aucun critère go / no-go : « gestion-mep criteres ajouter \"<libellé>\" ».");
            return 0;
        }
        Console.WriteLine("N°  ORDRE  ACTIF  LIBELLÉ");
        for (var i = 0; i < criteres.Count; i++)
            Console.WriteLine($"{i + 1,-2}  {criteres[i].Ordre,5}  {(criteres[i].Actif ? "oui" : "non"),-5}  {criteres[i].Libelle}");
        return 0;
    }

    private static async Task<int> CriteresAjouter(string libelle, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--ordre", "--actif"]);
        await using var db = Base(Configuration());
        var c = await ParametresEndpoints.CreerCritere(db, null, new CritereGoNoGoSaisie(libelle, o.Entier("--ordre"), o.OuiNon("--actif")));
        Console.WriteLine($"Critère « {c.Libelle} » créé (ordre {c.Ordre}{(c.Actif ? "" : ", inactif")}).");
        return 0;
    }

    private static async Task<int> CriteresModifier(string critere, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--libelle", "--ordre", "--actif"]);
        await using var db = Base(Configuration());
        var c = await TrouverCritere(db, critere);
        var modifie = await ParametresEndpoints.ModifierCritere(db, null, c.Id,
            new CritereGoNoGoSaisie(o.Valeur("--libelle") ?? c.Libelle, o.Entier("--ordre"), o.OuiNon("--actif")));
        Console.WriteLine($"Critère « {modifie.Libelle} » modifié (ordre {modifie.Ordre}{(modifie.Actif ? "" : ", inactif")}).");
        return 0;
    }

    private static async Task<int> CriteresSupprimer(string critere)
    {
        await using var db = Base(Configuration());
        var c = await TrouverCritere(db, critere);
        await ParametresEndpoints.SupprimerCritere(db, null, c.Id);
        Console.WriteLine($"Critère « {c.Libelle} » supprimé (les MEP gardent leur copie des critères déjà cochés).");
        return 0;
    }

    // ---------- Résolutions types ----------

    /// <summary>Résolution type désignée par son identifiant (complet ou 8 premiers caractères) ou par son titre exact.</summary>
    private static async Task<ResolutionTypeDto> TrouverResolution(MepDbContext db, string idOuTitre)
    {
        var toutes = await ResolutionsEndpoints.Lister(db, null, inactives: true);
        if (IdentifiantOuPrefixe(idOuTitre, toutes.Select(r => r.Id), "résolutions") is { } id && toutes.FirstOrDefault(r => r.Id == id) is { } parId)
            return parId;
        var parTitre = toutes.Where(r => r.Titre.Equals(idOuTitre.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return parTitre.Count switch
        {
            1 => parTitre[0],
            0 => throw new ErreurHttp(1, $"Résolution type « {idOuTitre} » introuvable (gestion-mep resolutions lister --inactives)."),
            _ => throw new ErreurHttp(1, $"Plusieurs résolutions types s'appellent « {idOuTitre} » : donner l'identifiant ({string.Join(", ", parTitre.Select(r => r.Id))})."),
        };
    }

    private static async Task<int> ResolutionsLister(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--systeme"], "--inactives");
        await using var db = Base(Configuration());
        var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
        Guid? systeme = o.Valeur("--systeme") is { } nom ? TrouverSysteme(catalogue, nom).Id : null;
        var resolutions = await ResolutionsEndpoints.Lister(db, systeme, o.A("--inactives"));
        if (EnJson)
        {
            EcrireJson(resolutions);
            return 0;
        }
        if (resolutions.Count == 0)
        {
            Console.WriteLine("Aucune résolution type.");
            return 0;
        }
        var noms = catalogue.ToDictionary(s => s.Id, s => s.Nom);
        Console.WriteLine($"{"ID",-8}  ACTIVE  VERSION  {"SYSTÈMES",-20}  TITRE");
        foreach (var r in resolutions)
        {
            var systemes = string.Join(", ", r.SystemeCatalogueIds.Select(id => noms.GetValueOrDefault(id, "?")));
            Console.WriteLine($"{r.Id.ToString()[..8]}  {(r.Actif ? "oui" : "non"),-6}  {r.Version,7}  {systemes,-20}  {r.Titre}");
        }
        return 0;
    }

    private static async Task<int> ResolutionsAfficher(string idOuTitre)
    {
        await using var db = Base(Configuration());
        var r = await TrouverResolution(db, idOuTitre);
        if (EnJson)
        {
            EcrireJson(r);
            return 0;
        }
        var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
        Console.WriteLine($"Titre        : {r.Titre}");
        Console.WriteLine($"Identifiant  : {r.Id}");
        Console.WriteLine($"État         : {(r.Actif ? "active" : "désactivée")}, version {r.Version}, modifiée le {Date(r.ModifieeLe)}{(r.ModifieeParNom is { } n ? $" par {n}" : "")}");
        Console.WriteLine($"Systèmes     : {(r.SystemeCatalogueIds.Count == 0 ? "tous" : string.Join(", ", r.SystemeCatalogueIds.Select(id => noms.GetValueOrDefault(id, "?"))))}");
        Console.WriteLine($"Symptômes    : {r.Symptomes}");
        Console.WriteLine($"Diagnostic   : {r.Diagnostic}");
        Console.WriteLine("Corrections  :");
        foreach (var c in r.Corrections) Console.WriteLine($"  {c.Ordre}. {c.Instruction}  →  {c.ResultatAttendu}");
        Console.WriteLine($"Vérification : {r.VerificationInstruction}  →  {r.VerificationAttendu}");
        return 0;
    }

    private static async Task<int> ResolutionsSupprimer(string idOuTitre)
    {
        var config = Configuration();
        await using var db = Base(config);
        var r = await TrouverResolution(db, idOuTitre);
        Console.WriteLine(await ResolutionsEndpoints.Supprimer(db, null, r.Id, DateTimeOffset.UtcNow, new StockagePiecesJointes(config))
            ? $"Résolution type « {r.Titre} » supprimée."
            : $"Résolution type « {r.Titre} » liée à une étape ou déjà exécutée : désactivée au lieu d'être supprimée.");
        return 0;
    }

    private static async Task<int> ResolutionsExporter(string? fichier)
    {
        await using var db = Base(Configuration());
        var export = await EchangeResolutions.Exporter(db, DateTimeOffset.UtcNow);
        if (await EcrireFichierOuSortie(fichier, export))
            Console.WriteLine($"{export.Resolutions!.Count} résolution(s) type(s) exportée(s) dans {fichier}.");
        return 0;
    }

    private static async Task<int> ResolutionsImporter(string fichier, string[] arguments)
    {
        var o = Options.Lire(arguments, [], "--remplacer", "--ignorer-existantes", "--essai");
        if (o.A("--remplacer") && o.A("--ignorer-existantes")) throw new ErreurUsage("--remplacer et --ignorer-existantes s'excluent");
        var mode = o.A("--remplacer") ? ModeImportResolutions.Remplacer
            : o.A("--ignorer-existantes") ? ModeImportResolutions.Ignorer : ModeImportResolutions.Refuser;
        ResolutionsExportees donnees;
        try
        {
            donnees = JsonSerializer.Deserialize<ResolutionsExportees>(await LireFichierOuEntree(fichier), OptionsJson)
                ?? throw new ErreurHttp(1, "Fichier vide.");
        }
        catch (JsonException e)
        {
            throw new ErreurHttp(1, $"Fichier JSON illisible : {e.Message}");
        }
        await using var db = Base(Configuration());
        var source = fichier == "-" ? "l'entrée standard" : Path.GetFileName(fichier);
        var resultats = await EchangeResolutions.Importer(db, null, donnees, mode, o.A("--essai"), source, DateTimeOffset.UtcNow);
        if (EnJson) EcrireJson(resultats);
        else
        {
            foreach (var r in resultats)
            {
                var libelle = r.Resultat switch { "creee" => "créée", "remplacee" => "remplacée", "ignoree" => "ignorée", _ => "refusée" };
                Console.WriteLine($"[{libelle,-9}] {r.Titre}{(r.Motif is { } m ? $" : {m}" : "")}");
            }
            Console.WriteLine($"{(o.A("--essai") ? "Essai, rien n'a été enregistré : " : "")}{resultats.Count(r => r.Resultat == "creee")} créée(s), "
                + $"{resultats.Count(r => r.Resultat == "remplacee")} remplacée(s), {resultats.Count(r => r.Resultat == "ignoree")} ignorée(s), "
                + $"{resultats.Count(r => r.Resultat == "refusee")} refusée(s).");
        }
        return resultats.Any(r => r.Resultat == "refusee") ? 1 : 0;
    }

    // ---------- MEP ----------

    private static string Libelle(Phase phase) => phase switch
    {
        Phase.Preparation => "préparation",
        Phase.Execution => "exécution",
        Phase.Recette => "recette",
        Phase.RetourArriere => "retour arrière",
        Phase.Cloturee => "clôturée",
        Phase.Annulee => "annulée",
        _ => phase.ToString(),
    };

    private static async Task<int> MepsLister(string[] arguments)
    {
        var o = Options.Lire(arguments,
            ["--phase", "--equipe", "--type", "--recherche", "--excel", "--systeme", "--environnement", "--personne", "--role", "--etat", "--du", "--au"],
            "--modeles", "--echec");
        var config = Configuration();
        await using var db = Base(config);
        var filtre = await FiltreDesOptions(db, o);
        if (o.Valeur("--excel") is { } classeur)
        {
            var f = await ExportsExcelEndpoints.Liste(db, o.A("--modeles"), filtre, DateTimeOffset.UtcNow, (await ServiceCharte.Charger(db, config)).Palette);
            await EcrireClasseur(classeur, f.Contenu);
            Console.WriteLine($"Liste exportée dans {classeur}.");
            return 0;
        }
        var meps = filtre.Appliquer(await MepsEndpoints.Lister(db, o.A("--modeles")));
        if (EnJson)
        {
            EcrireJson(meps);
            return 0;
        }
        if (meps.Count == 0)
        {
            Console.WriteLine(o.A("--modeles") ? "Aucun modèle." : "Aucune MEP.");
            return 0;
        }
        Console.WriteLine($"{"ID",-8}  {"ÉTAT",-22}  {"JOUR",-10}  {"PROD.",-5}  {"TYPE",-11}  ÉTAPES   ÉCHECS  {"SYSTÈMES",-20}  OUTIL ET OBJET");
        foreach (var m in meps)
        {
            var systemes = string.Join(",", m.Systemes.Select(s => s.Nom));
            Console.WriteLine($"{m.Id.ToString()[..8]}  {FiltreMeps.Libelle(m.Etat),-22}  {m.Jour:yyyy-MM-dd}  {(m.ToucheProduction ? "oui" : ""),-5}  {(m.Type == TypeMep.Simplifiee ? "simplifiée" : "complète"),-11}  {$"{m.NbReussies}/{m.NbEtapes}",-7}  {(m.EchecsSurvenus > 0 ? m.EchecsSurvenus.ToString(CultureInfo.InvariantCulture) : ""),-6}  {systemes,-20}  "
                + $"{m.Outil}{(m.VersionCible.Length > 0 ? $" {m.VersionCible}" : "")}{(m.Objet.Length > 0 ? $" : {m.Objet}" : "")}");
        }
        return 0;
    }

    /// <summary>
    /// Filtre de <c>meps lister</c> : mêmes règles et mêmes valeurs que l'API (<see cref="FiltreMeps"/>), mais systèmes, équipes
    /// et personne se donnent aussi par leur nom (ou login) ; un système absent du catalogue est cherché parmi les systèmes
    /// saisis à la main.
    /// </summary>
    private static async Task<FiltreMeps> FiltreDesOptions(MepDbContext db, Options o)
    {
        static IEnumerable<string> Liste(string? v) => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
        var systemes = Liste(o.Valeur("--systeme")).Select(s =>
            s.StartsWith(FiltreMeps.PrefixeLibre, StringComparison.Ordinal) ? s
                : (catalogue.FirstOrDefault(c => c.Nom.Equals(s, StringComparison.OrdinalIgnoreCase))
                    ?? (Guid.TryParse(s, out var id) ? catalogue.FirstOrDefault(c => c.Id == id) : null)) is { } c ? c.Id.ToString()
                : FiltreMeps.PrefixeLibre + s).ToList();
        var equipes = new List<string>();
        foreach (var e in Liste(o.Valeur("--equipe"))) equipes.Add((await EquipesEndpoints.Trouver(db, e)).Id.ToString());
        string? personne = null;
        if (o.Valeur("--personne") is { } p)
        {
            var comptes = await db.Utilisateurs.AsNoTracking().ToListAsync();
            var annuaire = await Personnes.Charger(db);
            var compte = comptes.FirstOrDefault(u => u.Login.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? (Guid.TryParse(p, out var id) ? comptes.FirstOrDefault(u => u.Id == id) : null)
                ?? comptes.FirstOrDefault(u => u.NomAffiche.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ErreurHttp(1, $"Personne « {p} » introuvable (login, nom ou identifiant : gestion-mep utilisateurs lister).");
            personne = annuaire.Personne(compte.Id).ToString();
        }
        return FiltreMeps.Lire(new ParametresFiltreMeps(o.Valeur("--recherche"), string.Join(',', systemes), o.Valeur("--environnement"),
            string.Join(',', equipes), personne, o.Valeur("--role"), o.Valeur("--etat"), o.Valeur("--phase"), o.Valeur("--type"),
            o.Valeur("--du"), o.Valeur("--au"), o.A("--echec") ? "1" : null));
    }

    private static async Task<int> SystemesHistorique(string nom, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--excel"]);
        var config = Configuration();
        await using var db = Base(config);
        var s = await TrouverSysteme(db, nom);
        if (o.Valeur("--excel") is { } classeur)
        {
            var f = await ExportsExcelEndpoints.Historique(db, s.Id, DateTimeOffset.UtcNow, (await ServiceCharte.Charger(db, config)).Palette);
            await EcrireClasseur(classeur, f.Contenu);
            Console.WriteLine($"Historique de {s.Nom} exporté dans {classeur}.");
            return 0;
        }
        var h = await HistoriqueSystemes.Historique(db, s.Id, Fuseaux.Jour(DateTimeOffset.UtcNow, null));
        if (EnJson)
        {
            EcrireJson(h);
            return 0;
        }
        var r = h.Resume;
        Console.WriteLine($"{h.Nom} : {r.Total} MEP ({r.DouzeMois} sur les 12 derniers mois), {r.EnCours} en cours, {r.Reussies} réussie(s), "
            + $"{r.AvecIncidents} avec incidents, {r.Repliees} repliée(s), {r.EnEchec} en échec, {r.Annulees} annulée(s) ; "
            + $"{r.Echecs} échec(s) d'étape sur ce système, {r.EchecsRepris} repris.");
        if (h.Meps.Count == 0) return 0;
        Console.WriteLine();
        Console.WriteLine($"{"JOUR",-10}  {"ÉTAT",-22}  {"ENVIRONNEMENTS",-24}  ÉCHECS  OUTIL ET OBJET");
        foreach (var m in h.Meps)
        {
            Console.WriteLine($"{m.Jour:yyyy-MM-dd}  {FiltreMeps.Libelle(m.Etat),-22}  {string.Join(", ", m.Environnements.Select(e => e.Nom)),-24}  {(m.Echecs.Count > 0 ? m.Echecs.Count.ToString(CultureInfo.InvariantCulture) : ""),-6}  "
                + $"{m.Outil}{(m.VersionCible.Length > 0 ? $" {m.VersionCible}" : "")}{(m.Objet.Length > 0 ? $" : {m.Objet}" : "")}  [{m.Id.ToString()[..8]}]");
            foreach (var e in m.Echecs)
                Console.WriteLine($"            {e.Code} {e.Action} : {e.Motif}{(e.Repris ? " (repris)" : "")}{(e.Resolution is { } res ? $" · résolution « {res} »" : "")}");
        }
        return 0;
    }

    /// <summary>
    /// MEP complète (comme la renvoie l'API), suivi de ses tests unitaires et journal dans l'ordre chronologique, pour l'archive
    /// ou l'audit.
    /// </summary>
    private static async Task<int> MepsExporter(string mepTexte, string? fichier)
    {
        var config = Configuration();
        await using var db = Base(config);
        var id = IdentifiantOuPrefixe(mepTexte, await db.Meps.AsNoTracking().Select(m => m.Id).ToListAsync(), "MEP")
            ?? throw new ErreurHttp(1, $"MEP « {mepTexte} » introuvable (gestion-mep meps lister).");
        var service = new ServiceMep(db, TimeProvider.System, new DiffuseurMep(config));
        var mep = await service.Charger(id);
        var journal = await MepsEndpoints.Journal(db, id);
        journal.Reverse();
        var export = new
        {
            Format = 1,
            Application = VersionApplication.Courante,
            ExporteeLe = DateTimeOffset.UtcNow,
            Mep = await service.VersDto(mep, null),
            TestsUnitaires = MepMapping.TestsUnitaires(mep),
            Journal = journal,
        };
        JournalAdmin.Ecrire(db, null, "mep.exportee", $"{(mep.EstModele ? "Modèle" : "MEP")} « {mep.Outil} » ({mep.Id}) exporté{(mep.EstModele ? "" : "e")} avec son journal");
        await db.SaveChangesAsync();
        if (await EcrireFichierOuSortie(fichier, export))
            Console.WriteLine($"MEP « {mep.Outil} » exportée dans {fichier} ({journal.Count} événement(s) de journal ; les fichiers des pièces jointes sont dans les sauvegardes).");
        return 0;
    }

    private static async Task EcrireClasseur(string fichier, byte[] contenu)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(fichier)) is { } dossier) Directory.CreateDirectory(dossier);
        await File.WriteAllBytesAsync(fichier, contenu);
    }

    /// <summary>
    /// Une vue d'une MEP en classeur Excel, comme le bouton « Exporter en Excel » de l'application (journal : sans filtre).
    /// Sans fichier, le nom proposé par l'application, dans le dossier courant.
    /// </summary>
    private static async Task<int> MepsExcel(string mepTexte, string vueTexte, string? fichier)
    {
        var config = Configuration();
        await using var db = Base(config);
        var id = IdentifiantOuPrefixe(mepTexte, await db.Meps.AsNoTracking().Select(m => m.Id).ToListAsync(), "MEP")
            ?? throw new ErreurHttp(1, $"MEP « {mepTexte} » introuvable (gestion-mep meps lister).");
        var service = new ServiceMep(db, TimeProvider.System, new DiffuseurMep(config));
        // Couleurs de la charte visuelle en vigueur (console, sinon fichier charte.json, sinon par défaut).
        var palette = (await ServiceCharte.Charger(db, config)).Palette;
        ExportsExcelEndpoints.Fichier export;
        string libelle;
        switch (vueTexte.ToLowerInvariant())
        {
            case "recette":
            {
                var mep = await service.Charger(id);
                var noms = await db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NomAffiche);
                export = new(RecetteExcel.Exporter(mep, noms, DateTimeOffset.UtcNow, palette), RecetteExcel.NomFichier(mep));
                libelle = "Recette";
                break;
            }
            case "objets":
            {
                var mep = await service.Charger(id);
                var ids = mep.Systemes.Select(s => s.CatalogueId).OfType<Guid>().Distinct().ToList();
                var fiches = await db.SystemesCatalogue.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                export = new(ObjetsExcel.Exporter(mep, fiches, modele: false, DateTimeOffset.UtcNow, palette), ObjetsExcel.NomFichier(mep, modele: false));
                libelle = "Objets impactés";
                break;
            }
            case "rapport":
                // Rapport de fin figé à la clôture (une MEP clôturée avant son existence obtient le sien maintenant).
                export = await SuiviEndpoints.RapportExcel(new RapportsFin(db, service), id, DateTimeOffset.UtcNow, palette);
                libelle = "Rapport de fin";
                break;
            default:
                (var vue, libelle) = vueTexte.ToLowerInvariant() switch
                {
                    "preparation" => (VuesExcel.Vue.Preparation, "Préparation"),
                    "etapes" => (VuesExcel.Vue.Etapes, "Étapes"),
                    "retour-arriere" => (VuesExcel.Vue.RetourArriere, "Retour arrière"),
                    "journal" => (VuesExcel.Vue.Journal, "Journal"),
                    _ => throw new ErreurUsage($"vue inconnue « {vueTexte} » : preparation, etapes, retour-arriere, recette, objets, journal ou rapport"),
                };
                export = await ExportsExcelEndpoints.Vue(db, service, id, vue, palette: palette);
                break;
        }
        fichier ??= export.Nom;
        await EcrireClasseur(fichier, export.Contenu);
        var outil = await db.Meps.AsNoTracking().Where(m => m.Id == id).Select(m => m.Outil).FirstAsync();
        JournalAdmin.Ecrire(db, null, "mep.exportee", $"Vue « {libelle} » de la MEP « {outil} » ({id}) exportée en Excel");
        await db.SaveChangesAsync();
        Console.WriteLine($"Vue « {libelle} » de la MEP « {outil} » exportée dans {fichier}.");
        return 0;
    }

    // ---------- Équipes ----------

    private static async Task<int> EquipesLister()
    {
        await using var db = Base(Configuration());
        var equipes = await EquipesEndpoints.Lister(db);
        if (EnJson)
        {
            EcrireJson(equipes);
            return 0;
        }
        if (equipes.Count == 0)
        {
            Console.WriteLine("Aucune équipe : « gestion-mep equipes creer \"<nom>\" ».");
            return 0;
        }
        var largeur = Math.Max(3, equipes.Max(e => e.Nom.Length));
        Console.WriteLine($"{"NOM".PadRight(largeur)}  MEMBRES  DESCRIPTION");
        foreach (var e in equipes) Console.WriteLine($"{e.Nom.PadRight(largeur)}  {e.MembreIds.Count,7}  {e.Description}");
        return 0;
    }

    private static async Task<int> EquipesAfficher(string nomOuId)
    {
        await using var db = Base(Configuration());
        var e = await EquipesEndpoints.Trouver(db, nomOuId);
        var annuaire = await Personnes.Charger(db);
        var membres = await db.Utilisateurs.AsNoTracking().Where(u => e.MembreIds.Contains(u.Id)).OrderBy(u => u.Login).ToListAsync();
        if (EnJson)
        {
            EcrireJson(new { Equipe = EquipesEndpoints.Dto(e), Membres = membres.Select(u => Personnes.Dto(u, annuaire)) });
            return 0;
        }
        Console.WriteLine($"Équipe      : {e.Nom}");
        Console.WriteLine($"Description : {Texte(e.Description)}");
        Console.WriteLine($"Membres     : {(membres.Count == 0 ? "aucun" : "")}");
        foreach (var u in membres)
            Console.WriteLine($"  {u.Login} : {annuaire.NomPersonne(u.Id) ?? u.NomAffiche}{(u.Actif ? "" : " (désactivé)")}");
        return 0;
    }

    private static async Task<int> EquipesCreer(string nom, string[] arguments)
    {
        if (nom.StartsWith('-')) throw new ErreurUsage("nom de l'équipe attendu");
        var o = Options.Lire(arguments, ["--description"]);
        await using var db = Base(Configuration());
        var e = await EquipesEndpoints.Creer(db, null, new EquipeSaisie(nom, o.Valeur("--description"), null), DateTimeOffset.UtcNow);
        Console.WriteLine($"Équipe « {e.Nom} » créée : « gestion-mep equipes ajouter \"{e.Nom}\" <login> » pour y ajouter des comptes.");
        return 0;
    }

    private static async Task<int> EquipesModifier(string nomOuId, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--nom", "--description"]);
        await using var db = Base(Configuration());
        var e = await EquipesEndpoints.Trouver(db, nomOuId);
        var modifiee = await EquipesEndpoints.Modifier(db, null, e.Id, new EquipeSaisie(o.Valeur("--nom") ?? e.Nom, o.Valeur("--description") ?? e.Description, null));
        Console.WriteLine($"Équipe « {modifiee.Nom} » modifiée.");
        return 0;
    }

    private static async Task<int> EquipesMembres(string nomOuId, string[] logins, bool ajouter)
    {
        if (logins.Any(l => l.StartsWith('-'))) throw new ErreurUsage("logins attendus après le nom de l'équipe");
        await using var db = Base(Configuration());
        var e = await EquipesEndpoints.Trouver(db, nomOuId);
        var comptes = new List<Utilisateur>();
        foreach (var l in logins) comptes.Add(await Compte(db, l));
        var ids = comptes.Select(c => c.Id).ToHashSet();
        var membres = ajouter ? e.MembreIds.Concat(ids.Where(i => !e.MembreIds.Contains(i))).ToList() : e.MembreIds.Where(i => !ids.Contains(i)).ToList();
        if (ajouter ? comptes.All(c => e.MembreIds.Contains(c.Id)) : comptes.All(c => !e.MembreIds.Contains(c.Id)))
        {
            Console.WriteLine($"Équipe « {e.Nom} » : aucun changement ({(ajouter ? "déjà membres" : "aucun n'en est membre")}).");
            return 0;
        }
        await EquipesEndpoints.Modifier(db, null, e.Id, new EquipeSaisie(e.Nom, e.Description, membres));
        Console.WriteLine($"Équipe « {e.Nom} » : {string.Join(", ", comptes.Select(c => c.Login))} {(ajouter ? "ajouté(s)" : "retiré(s)")} ({membres.Count} membre(s)).");
        return 0;
    }

    private static async Task<int> EquipesSupprimer(string nomOuId)
    {
        await using var db = Base(Configuration());
        var e = await EquipesEndpoints.Trouver(db, nomOuId);
        await EquipesEndpoints.Supprimer(db, null, e.Id);
        Console.WriteLine($"Équipe « {e.Nom} » supprimée (les comptes et les MEP ne changent pas).");
        return 0;
    }

    // ---------- Journal d'administration ----------

    private static async Task<int> Journal(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--depuis", "--limite"]);
        DateTimeOffset? depuis = o.Valeur("--depuis") is { } d
            ? DateOnly.TryParseExact(d, "yyyy-MM-dd", out var jour) ? new DateTimeOffset(jour.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : throw ErreurHttp.Saisie("--depuis attend une date AAAA-MM-JJ.")
            : null;
        var limite = o.Entier("--limite") ?? 200;
        if (limite < 1) throw ErreurHttp.Saisie("--limite attend un nombre entier positif.");
        await using var db = Base(Configuration());
        var evenements = await AdminEndpoints.Journal(db, Math.Min(limite, 100_000), depuis);
        if (EnJson)
        {
            EcrireJson(evenements);
            return 0;
        }
        if (evenements.Count == 0)
        {
            Console.WriteLine("Aucun événement.");
            return 0;
        }
        foreach (var e in evenements)
            Console.WriteLine($"{Date(e.Quand)}  {e.Auteur}{(e.AuteurLogin is { } l ? $" ({l})" : "")}  [{e.Type}]  {e.Detail}");
        return 0;
    }
}
