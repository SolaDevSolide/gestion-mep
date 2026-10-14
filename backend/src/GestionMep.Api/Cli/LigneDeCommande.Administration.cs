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
        Console.WriteLine($"{"NOM".PadRight(largeur)}  ACTIF  {"CIBLE",-20}  DESCRIPTION");
        foreach (var s in systemes)
            Console.WriteLine($"{s.Nom.PadRight(largeur)}  {(s.Actif ? "oui" : "non"),-5}  {s.Cible,-20}  {s.Description}");
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
        _ => phase.ToString(),
    };

    private static async Task<int> MepsLister(string[] arguments)
    {
        var o = Options.Lire(arguments, ["--phase"], "--modeles");
        Phase? phase = o.Valeur("--phase") is { } p
            ? Enum.TryParse<Phase>(p.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out var lue) && Enum.IsDefined(lue) ? lue
                : throw ErreurHttp.Saisie("--phase attend preparation, execution, recette, retour-arriere ou cloturee.")
            : null;
        await using var db = Base(Configuration());
        var meps = (await MepsEndpoints.Lister(db, o.A("--modeles"))).Where(m => phase is null || m.Phase == phase).ToList();
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
        Console.WriteLine($"{"ID",-8}  {"PHASE",-24}  {"CRÉÉE LE",-10}  {"ENV.",-10}  ÉTAPES   OUTIL ET OBJET");
        foreach (var m in meps)
        {
            var phaseTexte = Libelle(m.Phase) + (m.Issue is { } issue ? issue == IssueMep.Reussie ? ", réussie" : ", repliée" : "");
            Console.WriteLine($"{m.Id.ToString()[..8]}  {phaseTexte,-24}  {m.CreeLe:yyyy-MM-dd}  {m.Environnement,-10}  {$"{m.NbReussies}/{m.NbEtapes}",-7}  "
                + $"{m.Outil}{(m.VersionCible.Length > 0 ? $" {m.VersionCible}" : "")}{(m.Objet.Length > 0 ? $" : {m.Objet}" : "")}");
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
