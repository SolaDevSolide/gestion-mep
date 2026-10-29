using System.Globalization;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// MEP : consultation (résumé, journal), importation d'un export, duplication depuis un modèle, annulation, suppression et
/// pilotage de secours. Mêmes règles que l'application (méthodes partagées des Endpoints), avec les droits d'un
/// administrateur ; les journaux indiquent l'auteur « Ligne de commande ». Les gestes de préparation et d'exécution restent
/// dans l'application : ils sont signés par une personne.
/// </summary>
public static partial class LigneDeCommande
{
    /// <summary>Format du fichier de « meps exporter » : 2 ajoute le contenu réimportable (<c>contenu</c>, <c>references</c>).</summary>
    private const int FormatExportMep = 2;

    private static async Task<Guid> IdMep(MepDbContext db, string texte) =>
        IdentifiantOuPrefixe(texte, await db.Meps.AsNoTracking().Select(m => m.Id).ToListAsync(), "MEP")
            ?? throw ErreurHttp.NonTrouve($"MEP « {texte} » introuvable (gestion-mep meps lister).");

    private static ServiceMep ServiceMep(MepDbContext db, IConfiguration config) => new(db, TimeProvider.System, new DiffuseurMep(config));

    private static string LibellePhase(Mep m) => m.EstModele ? "modèle" : m.Phase switch
    {
        Phase.Cloturee => m.Issue switch
        {
            IssueMep.Reussie => "clôturée, réussie",
            IssueMep.Repliee => "clôturée après retour arrière",
            IssueMep.Echec => "clôturée en échec",
            _ => "clôturée",
        },
        Phase.Preparation when m.Report is not null => "préparation (reportée)",
        var p => Libelle(p),
    };

    /// <summary>Objet compact rendu par les commandes qui créent une MEP (champ <c>resultat</c> de l'enveloppe JSON).</summary>
    private sealed record MepCreeeDto(Guid Id, string Outil, bool EstModele);

    // ---------- Consultation ----------

    private static async Task<int> MepsAfficher(string texte)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceMep(db, config);
        var mep = await svc.Charger(await IdMep(db, texte));
        if (EnJson)
        {
            EcrireJson(await svc.VersDto(mep, null));
            return 0;
        }
        var noms = await Personnes.Noms(db);
        string Participant(Guid? id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "aucun";
        Ecrire($"{(mep.EstModele ? "Modèle" : "MEP")} « {Regles.NomComplet(mep)} »");
        Ecrire($"Identifiant   : {mep.Id}");
        Ecrire($"Type          : {(mep.Type == TypeMep.Simplifiee ? "simplifiée" : "complète")}");
        Ecrire($"Phase         : {LibellePhase(mep)}");
        if (mep.Objet.Length > 0) Ecrire($"Objet         : {mep.Objet}");
        if (mep.DateMep is { } jour)
            Ecrire($"Jour          : {jour:yyyy-MM-dd}{(mep.HeureDebut is { } debut ? $" {debut:HH\\:mm}" : "")}{(mep.HeureFin is { } fin ? $"–{fin:HH\\:mm}" : "")} ({mep.FuseauHoraire})");
        Ecrire($"Pilote        : {Participant(mep.PiloteId)}");
        var suppleants = Regles.Suppleants(mep).Select(id => Participant(id)).ToList();
        if (suppleants.Count > 0) Ecrire($"Suppléants    : {string.Join(", ", suppleants)}");
        Ecrire($"Personnes     : {(mep.Participants.Count == 0 ? "aucune" : string.Join(", ", mep.Participants.Select(p => p.Nom)))}");
        Ecrire($"Systèmes      : {(mep.Systemes.Count == 0 ? "aucun" : string.Join(", ", mep.Systemes.Select(s =>
            s.Environnements.Count == 0 ? s.Nom : $"{s.Nom} ({string.Join(", ", s.Environnements.Select(e => e.Production ? e.Nom + "*" : e.Nom))})")))}");
        var etapes = mep.Etapes.Where(e => e.Plan == TypePlan.Deploiement).ToList();
        Ecrire($"Étapes        : {etapes.Count(e => e.Etat == EtatEtape.Reussie)}/{etapes.Count} réussie(s)"
            + (mep.EchecsEtapes > 0 ? $", {mep.EchecsEtapes} échec(s) survenu(s)" : "")
            + $", {mep.Etapes.Count(e => e.Plan == TypePlan.RetourArriere)} au plan de retour arrière");
        if (mep.Approbations.Count > 0)
            Ecrire($"Approbations  : {string.Join(", ", mep.Approbations.Select(a => $"{a.Nom} ({a.Decision switch { DecisionApprobation.Approuvee => "approuvée", DecisionApprobation.Refusee => "refusée", _ => "en attente" }})"))}");
        Ecrire($"Créée le      : {Date(mep.CreeLe)}{(mep.CreeParUtilisateurId is { } c && noms.TryGetValue(c, out var createur) ? $" par {createur}" : "")}");
        if (mep.Report is { } r) Ecrire($"Report        : {r.Motif}{(r.DateVisee is { } v ? $" (date visée {v:yyyy-MM-dd})" : "")}");
        if (mep.Annulation is { } a) Ecrire($"Annulation    : {a.Motif} (le {Date(a.Le)})");
        if (mep.PilotageSecours is { } ps) Ecrire($"Secours       : pilotage repris le {Date(ps.Le)} : {ps.Motif}");
        Informer($"Journal : gestion-mep meps journal {mep.Id.ToString()[..8]} ; détail complet : gestion-mep meps afficher {mep.Id.ToString()[..8]} --json");
        return 0;
    }

    private static async Task<int> MepsJournal(string texte, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--type", "--recherche", "--limite", "--apres"]);
        long? apres = o.Valeur("--apres") is { } a
            ? long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw ErreurHttp.Saisie("--apres attend un n° d'événement (colonne N° du journal).")
            : null;
        await using var db = Base(Configuration());
        var id = await IdMep(db, texte);
        var evenements = await MepsEndpoints.Journal(db, id, new FiltreJournal(o.Entier("--limite"), apres, null, o.Valeur("--type"), o.Valeur("--recherche")));
        if (EnJson)
        {
            EcrireJson(evenements);
            return 0;
        }
        if (evenements.Count == 0)
        {
            Informer("Aucun événement.");
            return 0;
        }
        foreach (var e in evenements)
            Ecrire($"{e.Id,7}  {Date(e.Quand)}  {e.Auteur}{(e.AuteurLogin is { } l ? $" ({l})" : "")}  [{e.Type}]  {e.Detail}");
        return 0;
    }

    // ---------- Exportation (archive, audit, importation) ----------

    /// <summary>Comptes, systèmes du catalogue et résolutions cités par la MEP : de quoi les retrouver sur un autre serveur.</summary>
    private sealed record ReferencesMep(List<ReferenceCompte> Comptes, List<ReferenceSysteme> Systemes, List<ReferenceResolution> Resolutions);
    private sealed record ReferenceCompte(Guid Id, string Login, string Nom);
    private sealed record ReferenceSysteme(Guid Id, string Nom, List<ReferenceObjet> Objets);
    private sealed record ReferenceObjet(Guid Id, string Categorie, string Nom);
    private sealed record ReferenceResolution(Guid Id, string Titre);

    private static async Task<ReferencesMep> References(MepDbContext db, Mep mep)
    {
        var comptes = mep.Participants.Select(p => p.UtilisateurId).Concat(mep.Approbations.Select(a => (Guid?)a.UtilisateurId))
            .Concat(mep.Sections.Select(s => s.AuteurId)).OfType<Guid>().Distinct().ToList();
        var systemes = mep.Systemes.Select(s => s.CatalogueId).OfType<Guid>().Distinct().ToList();
        var resolutions = mep.Etapes.SelectMany(e => e.ResolutionIds).Distinct().ToList();
        return new ReferencesMep(
            await db.Utilisateurs.AsNoTracking().Where(u => comptes.Contains(u.Id)).Select(u => new ReferenceCompte(u.Id, u.Login, u.NomAffiche)).ToListAsync(),
            (await db.SystemesCatalogue.AsNoTracking().Where(s => systemes.Contains(s.Id)).ToListAsync())
                .Select(s => new ReferenceSysteme(s.Id, s.Nom, s.Objets.Select(x => new ReferenceObjet(x.Id, x.Categorie, x.Nom)).ToList())).ToList(),
            await db.ResolutionsTypes.AsNoTracking().Where(r => resolutions.Contains(r.Id)).Select(r => new ReferenceResolution(r.Id, r.Titre)).ToListAsync());
    }

    /// <summary>
    /// MEP complète (comme la renvoie l'API), suivi de ses tests unitaires et journal dans l'ordre chronologique, pour l'archive
    /// ou l'audit ; plus le contenu réimportable (<c>contenu</c>) et les références à retrouver (<c>references</c>).
    /// </summary>
    private static async Task<int> MepsExporter(string mepTexte, string? fichier)
    {
        var config = Configuration();
        await using var db = Base(config);
        var id = await IdMep(db, mepTexte);
        var service = ServiceMep(db, config);
        var mep = await service.Charger(id);
        var journal = await MepsEndpoints.Journal(db, id);
        journal.Reverse();
        var export = new
        {
            Format = FormatExportMep,
            Application = VersionApplication.Courante,
            ExporteeLe = DateTimeOffset.UtcNow,
            Mep = await service.VersDto(mep, null),
            TestsUnitaires = MepMapping.TestsUnitaires(mep),
            Journal = journal,
            Contenu = mep,
            References = await References(db, mep),
        };
        JournalAdmin.Ecrire(db, null, "mep.exportee", $"{(mep.EstModele ? "Modèle" : "MEP")} « {mep.Outil} » ({mep.Id}) exporté{(mep.EstModele ? "" : "e")} avec son journal");
        await db.SaveChangesAsync();
        if (await EcrireFichierOuSortie(fichier, export))
        {
            Resultat(new { Fichier = Path.GetFullPath(fichier!), Evenements = journal.Count });
            Informer($"MEP « {mep.Outil} » exportée dans {fichier} ({journal.Count} événement(s) de journal ; les fichiers des pièces jointes sont dans les sauvegardes).");
        }
        return 0;
    }

    // ---------- Création : duplication et importation ----------

    /// <summary>Compte qui reçoit la MEP créée (--pour) : actif, il en devient participant et responsable technique.</summary>
    private static async Task<Acteur> Proprietaire(MepDbContext db, Options o)
    {
        var login = o.Valeur("--pour") ?? throw new ErreurUsage("--pour <login> attendu (compte qui reçoit la MEP et pourra la modifier)");
        var compte = await Compte(db, login);
        if (!compte.Actif) throw new RegleMetierException($"Le compte « {compte.Login} » est désactivé : il ne pourrait pas modifier la MEP.");
        return new Acteur(compte.Id, compte.NomAffiche, compte.EstAdmin);
    }

    private static async Task<int> MepsDupliquer(string texte, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--pour"], "--modele");
        var config = Configuration();
        await using var db = Base(config);
        var proprietaire = await Proprietaire(db, o);
        var svc = ServiceMep(db, config);
        var source = await svc.Charger(await IdMep(db, texte));
        var modele = o.A("--modele");
        var detail = (source.EstModele && !modele ? $"MEP créée depuis le modèle « {source.Outil} »" : $"{(modele ? "Modèle" : "MEP")} créé{(modele ? "" : "e")} par duplication de « {source.Outil} »")
            + $" pour {proprietaire.Nom}";
        var dto = await MepsEndpoints.Dupliquer(svc, db, source, modele, Acteur.LigneDeCommande, proprietaire, detail);
        Resultat(new MepCreeeDto(dto.Id, dto.Outil, modele));
        Ecrire(dto.Id.ToString());
        Informer($"{(modele ? "Modèle" : "MEP")} « {dto.Outil} » créé{(modele ? "" : "e")} en préparation pour {proprietaire.Nom} (identifiant ci-dessus).");
        return 0;
    }

    /// <summary>
    /// Nouvelle MEP (ou nouveau modèle) depuis un fichier de « meps exporter » : le contenu exporté est copié comme une
    /// duplication, après avoir retrouvé sur ce serveur les comptes (par login), les systèmes du catalogue (par nom) et les
    /// résolutions types (par titre) ; ce qui n'existe pas ici est détaché et signalé.
    /// </summary>
    private static async Task<int> MepsImporter(string fichier, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--pour"], "--modele", "--essai");
        var texte = await LireFichierOuEntree(fichier);
        Mep source;
        ReferencesMep references;
        string application;
        try
        {
            using var document = JsonDocument.Parse(texte);
            var racine = document.RootElement;
            if (!racine.TryGetProperty("contenu", out var contenu) || contenu.ValueKind != JsonValueKind.Object)
                throw ErreurHttp.Saisie("Fichier sans contenu réimportable : il vient de « meps exporter » d'une version antérieure (format 1). "
                    + "Exporter de nouveau la MEP avec cette version.");
            source = contenu.Deserialize<Mep>(OptionsJson) ?? throw ErreurHttp.Saisie("Contenu de la MEP illisible.");
            references = racine.TryGetProperty("references", out var r) ? r.Deserialize<ReferencesMep>(OptionsJson) ?? new([], [], []) : new([], [], []);
            application = racine.TryGetProperty("application", out var a) ? a.GetString() ?? "?" : "?";
        }
        catch (JsonException e)
        {
            throw ErreurHttp.Saisie($"Fichier JSON illisible : {e.Message}");
        }
        var config = Configuration();
        await using var db = Base(config);
        var proprietaire = await Proprietaire(db, o);
        var ecarts = await Rattacher(db, source, references);
        foreach (var e in ecarts) Avertir($"Attention : {e}");
        var modele = o.A("--modele");
        var outil = source.EstModele && !modele ? Regles.NomCopie(source, commeModele: false) : source.Outil;
        if (o.A("--essai"))
        {
            Informer($"Essai, rien n'a été créé : « {outil} » ({source.Etapes.Count} étape(s), {source.Systemes.Count} système(s), "
                + $"{source.Participants.Count} personne(s)) serait importé{(modele ? " comme modèle" : "e")} pour {proprietaire.Nom}, {ecarts.Count} écart(s).");
            Resultat(new { Outil = outil, Ecarts = ecarts });
            return 0;
        }
        var svc = ServiceMep(db, config);
        var source1 = fichier == "-" ? "l'entrée standard" : Path.GetFileName(fichier);
        var dto = await MepsEndpoints.Dupliquer(svc, db, source, modele, Acteur.LigneDeCommande, proprietaire,
            $"{(modele ? "Modèle importé" : "MEP importée")} depuis {source1} (« {source.Outil} », version {application}) pour {proprietaire.Nom}", outil);
        Resultat(new MepCreeeDto(dto.Id, dto.Outil, modele));
        Ecrire(dto.Id.ToString());
        Informer($"{(modele ? "Modèle" : "MEP")} « {dto.Outil} » importé{(modele ? "" : "e")} en préparation pour {proprietaire.Nom} (identifiant ci-dessus)"
            + (ecarts.Count > 0 ? $", {ecarts.Count} écart(s) signalé(s)." : "."));
        return 0;
    }

    /// <summary>
    /// Rattache le contenu importé aux données de ce serveur. Un identifiant connu ici est gardé (même serveur) ; sinon le
    /// compte est retrouvé par son login, le système par son nom (et ses objets par catégorie et nom), la résolution par son
    /// titre. Ce qui reste introuvable est détaché : personne sans compte, système saisi à la main, résolution retirée.
    /// </summary>
    private static async Task<List<string>> Rattacher(MepDbContext db, Mep mep, ReferencesMep references)
    {
        var ecarts = new List<string>();
        var comptes = await db.Utilisateurs.AsNoTracking().ToListAsync();
        Guid? CompteIci(Guid? id)
        {
            if (id is not { } g || comptes.Any(u => u.Id == g)) return id;
            var login = references.Comptes.FirstOrDefault(c => c.Id == g)?.Login;
            return login is null ? null : comptes.FirstOrDefault(u => u.Login == login)?.Id;
        }
        foreach (var p in mep.Participants.Where(p => p.UtilisateurId is not null))
        {
            var ici = CompteIci(p.UtilisateurId);
            if (ici is null) ecarts.Add($"{p.Nom} n'a pas de compte sur ce serveur : gardé(e) comme personne sans compte.");
            p.UtilisateurId = ici;
        }
        foreach (var a in mep.Approbations.ToList())
        {
            if (CompteIci(a.UtilisateurId) is { } ici) a.UtilisateurId = ici;
            else
            {
                mep.Approbations.Remove(a);
                ecarts.Add($"Approbateur {a.Nom} retiré : pas de compte sur ce serveur.");
            }
        }
        foreach (var s in mep.Sections) s.AuteurId = CompteIci(s.AuteurId);

        var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
        foreach (var s in mep.Systemes.Where(s => s.CatalogueId is not null))
        {
            var avant = s.CatalogueId!.Value;
            if (catalogue.Any(c => c.Id == avant)) continue;
            var nom = references.Systemes.FirstOrDefault(r => r.Id == avant)?.Nom ?? s.Nom;
            var ici = catalogue.FirstOrDefault(c => c.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase));
            s.CatalogueId = ici?.Id;
            if (ici is null) ecarts.Add($"Système {s.Nom} absent du catalogue de ce serveur : gardé comme système saisi à la main.");
            // Objets impactés du catalogue : retrouvés par catégorie et nom dans la fiche de ce serveur.
            var objets = references.Systemes.FirstOrDefault(r => r.Id == avant)?.Objets ?? [];
            foreach (var o in mep.ObjetsImpactes.Where(o => o.SystemeId == s.Id && o.ObjetCatalogueId is not null))
            {
                var ref_ = objets.FirstOrDefault(x => x.Id == o.ObjetCatalogueId);
                o.ObjetCatalogueId = ici?.Objets.FirstOrDefault(x => ref_ is not null && x.Categorie.Equals(ref_.Categorie, StringComparison.OrdinalIgnoreCase)
                    && x.Nom.Equals(ref_.Nom, StringComparison.OrdinalIgnoreCase))?.Id;
            }
        }
        var connus = catalogue.SelectMany(c => c.Objets).Select(x => x.Id).ToHashSet();
        foreach (var o in mep.ObjetsImpactes.Where(o => o.ObjetCatalogueId is { } x && !connus.Contains(x))) o.ObjetCatalogueId = null;

        var resolutions = await db.ResolutionsTypes.AsNoTracking().Select(r => new { r.Id, r.Titre }).ToListAsync();
        foreach (var e in mep.Etapes)
        {
            e.ResolutionIds = e.ResolutionIds.Select(id =>
            {
                if (resolutions.Any(r => r.Id == id)) return (Guid?)id;
                var titre = references.Resolutions.FirstOrDefault(r => r.Id == id)?.Titre;
                var ici = titre is null ? null : resolutions.FirstOrDefault(r => r.Titre.Equals(titre, StringComparison.OrdinalIgnoreCase));
                if (ici is null) ecarts.Add($"Résolution type {titre ?? id.ToString()} absente de ce serveur : retirée de l'étape {e.Code}.");
                return ici?.Id;
            }).OfType<Guid>().Distinct().ToList();
        }
        // Liens vers les bibliothèques (provenance seulement) : gardés s'ils existent ici.
        var modelesEtape = await db.ModelesEtape.AsNoTracking().Select(m => m.Id).ToListAsync();
        foreach (var e in mep.Etapes.Where(e => e.ModeleEtapeId is { } m && !modelesEtape.Contains(m))) e.ModeleEtapeId = null;
        var modelesRecette = await db.ModelesRecette.AsNoTracking().Select(m => m.Id).ToListAsync();
        foreach (var f in mep.FichesRecette.Where(f => f.ModeleRecetteId is { } m && !modelesRecette.Contains(m))) f.ModeleRecetteId = null;
        return ecarts;
    }

    // ---------- Annuler, supprimer, pilotage de secours ----------

    private static async Task<int> MepsAnnuler(string texte, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--motif", "--etat-production"], "--oui");
        var motif = o.Valeur("--motif") ?? throw new ErreurUsage("--motif \"…\" attendu (motif de l'annulation)");
        var config = Configuration();
        await using var db = Base(config);
        var id = await IdMep(db, texte);
        var svc = ServiceMep(db, config);
        var mep = await svc.Charger(id);
        if (mep.Phase == Phase.Annulee)
        {
            Informer($"La MEP « {Regles.NomComplet(mep)} » est déjà annulée : rien à faire.");
            Resultat(new { mep.Id, mep.Outil, mep.Phase, Modifie = false });
            return 0;
        }
        Confirmer(o.A("--oui"), [$"La MEP « {Regles.NomComplet(mep)} » ({LibellePhase(mep)}) sera annulée : elle reste consultable mais ne se modifie plus."],
            $"gestion-mep meps annuler {texte} --motif \"…\" --oui");
        var dto = await CycleDeVieEndpoints.Annuler(svc, id, Acteur.LigneDeCommande, new AnnulationSaisie(motif, o.Valeur("--etat-production")));
        JournalAdmin.Ecrire(db, null, "mep.annulee", $"MEP « {Regles.NomComplet(mep)} » ({id}) annulée. Motif : {motif.Trim()}");
        await db.SaveChangesAsync();
        Resultat(new { dto.Id, dto.Outil, dto.Phase, Modifie = true });
        Informer($"MEP « {Regles.NomComplet(mep)} » annulée.");
        return 0;
    }

    private static async Task<int> MepsSupprimer(string texte, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--motif"], "--oui");
        var motif = o.Valeur("--motif") ?? throw new ErreurUsage("--motif \"…\" attendu (motif de la suppression)");
        var config = Configuration();
        await using var db = Base(config);
        var id = await IdMep(db, texte);
        var svc = ServiceMep(db, config);
        var mep = await svc.Charger(id);
        var nom = Regles.NomComplet(mep);
        var pieces = await db.PiecesJointes.CountAsync(p => p.MepId == id);
        // Mêmes refus que l'application avant la question (MEP qui a eu un Go, motif vide).
        Regles.VerifierSuppression(mep, motif, nom);
        Confirmer(o.A("--oui"),
            [$"{(mep.EstModele ? "Le modèle" : "La MEP")} « {nom} », son journal et ses {pieces} pièce(s) jointe(s) seront supprimés définitivement."],
            $"gestion-mep meps supprimer {texte} --motif \"…\" --oui");
        await CycleDeVieEndpoints.Supprimer(svc, db, new StockagePiecesJointes(config), new DiffuseurMep(config), id, Acteur.LigneDeCommande,
            new SuppressionMepSaisie(motif, nom));
        Resultat(new { Id = id, Outil = mep.Outil, Pieces = pieces });
        Informer($"{(mep.EstModele ? "Modèle" : "MEP")} « {nom} » supprimé{(mep.EstModele ? "" : "e")} (journal d'administration : mep.supprimee).");
        return 0;
    }

    private static async Task<int> MepsPiloter(string texte, string login, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--motif", "--ancien-pilote-suppleant"]);
        var motif = o.Valeur("--motif") ?? throw new ErreurUsage("--motif \"…\" attendu (motif du changement de pilote)");
        var config = Configuration();
        await using var db = Base(config);
        var id = await IdMep(db, texte);
        var compte = await Compte(db, login);
        var dto = await CycleDeVieEndpoints.ChangerPilote(ServiceMep(db, config), db, id, Acteur.LigneDeCommande,
            new PilotageSaisie(null, compte.Id, motif, o.OuiNon("--ancien-pilote-suppleant")));
        Resultat(new { dto.Id, dto.Outil, Pilote = compte.Login });
        Informer($"Pilotage de secours : {compte.NomAffiche} ({compte.Login}) pilote désormais « {dto.Outil} ». "
            + "Inscrit au journal de la MEP et au journal d'administration.");
        return 0;
    }
}
