using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Ligne de commande d'administration (« gestion-mep ») : chaque groupe de commandes, contre la base de test, avec les mêmes
/// règles que la console et l'inscription au journal d'administration.
/// </summary>
[Collection("Api")]
public class LigneDeCommandeTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private record Resultat(int Code, string Sortie, string Erreur)
    {
        public JsonElement Json => JsonDocument.Parse(Sortie).RootElement;
    }

    private static async Task<Resultat> Cli(params string[] args)
    {
        var sortie = new StringWriter();
        var erreur = new StringWriter();
        var (avant, avantErreur) = (Console.Out, Console.Error);
        Console.SetOut(sortie);
        Console.SetError(erreur);
        try
        {
            return new Resultat(await LigneDeCommande.Executer(args), sortie.ToString(), erreur.ToString());
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
        }
    }

    private static async Task<List<JsonElement>> JournalAdmin(int limite = 50) =>
        (await Cli("journal", "--limite", limite.ToString(), "--json")).Json.EnumerateArray().ToList();

    private static async Task<bool> Journalise(string type, string contient) =>
        (await JournalAdmin()).Any(e => e.GetProperty("type").GetString() == type && e.GetProperty("auteur").GetString() == "Ligne de commande"
            && e.GetProperty("detail").GetString()!.Contains(contient, StringComparison.Ordinal));

    private string Fichier(string nom)
    {
        Directory.CreateDirectory(_api.DossierPieces);
        return Path.Combine(_api.DossierPieces, nom);
    }

    [Fact]
    public async Task L_aide_regroupe_toutes_les_commandes_et_json_se_place_n_importe_ou()
    {
        var aide = await Cli("aide");
        Assert.Equal(0, aide.Code);
        foreach (var commande in new[] { "comptes modifier", "comptes expertises", "systemes ajouter", "criteres modifier", "reglages definir",
                     "resolutions importer", "meps exporter", "journal [--depuis", "sauvegardes restaurer", "mettre-a-jour", "config afficher" })
            Assert.Contains($"gestion-mep {commande}", aide.Sortie);
        Assert.True(LigneDeCommande.EstCommande(["--json", "comptes", "lister"]));
        Assert.False(LigneDeCommande.EstCommande(["--urls", "http://+:8080"]));
        var comptes = await Cli("--json", "comptes", "lister");
        Assert.Equal(0, comptes.Code);
        Assert.Contains(comptes.Json.EnumerateArray(), u => u.GetProperty("login").GetString() == "alice" && u.GetProperty("estAdmin").GetBoolean());
        Assert.Equal(2, (await Cli("comptes", "creer", "x", "--inconnue")).Code);
        Assert.Equal(2, (await Cli("comptes", "modifier", "bob")).Code);
    }

    [Fact]
    public async Task Les_comptes_se_modifient_avec_les_regles_de_la_console()
    {
        Assert.Equal(0, (await Cli("comptes", "creer", "dupont", "--nom", "Jean Dupont", "--role", "DBA", "--contact", "poste 12")).Code);
        var cree = (await Cli("comptes", "afficher", "dupont", "--json")).Json;
        Assert.Equal("DBA", cree.GetProperty("role").GetString());
        Assert.True(cree.GetProperty("doitChangerMotDePasse").GetBoolean());

        // Nom affiché qui imite un autre compte, dernier administrateur : refusés comme dans la console.
        var imitation = await Cli("comptes", "modifier", "dupont", "--nom", "bob");
        Assert.Equal(1, imitation.Code);
        Assert.Contains("déjà le nom ou le login d'un autre compte", imitation.Erreur);
        var dernierAdmin = await Cli("comptes", "modifier", "alice", "--admin", "non");
        Assert.Equal(1, dernierAdmin.Code);
        Assert.Contains("dernier administrateur", dernierAdmin.Erreur);
        Assert.Equal(1, (await Cli("comptes", "modifier", "alice", "--admin", "peut-etre")).Code);
        Assert.Equal(1, (await Cli("comptes", "modifier", "inconnu", "--role", "x")).Code);

        var modifie = await Cli("comptes", "modifier", "dupont", "--nom", "J. Dupont", "--admin", "oui", "--contact", "poste 13");
        Assert.Equal(0, modifie.Code);
        Assert.Contains("devient administrateur", modifie.Sortie);
        Assert.True(await Journalise("compte.modifie", "devient administrateur"));

        // Expertises : ajout avec détail, mise à jour, retrait ; système inconnu refusé.
        Assert.Equal(0, (await Cli("comptes", "modifier", "dupont", "--ajouter-expertise", "bdd", "--detail", "Base clients", "--ajouter-expertise", "API")).Code);
        Assert.Equal(0, (await Cli("comptes", "modifier", "dupont", "--ajouter-expertise", "API", "--detail", "Commandes", "--retirer-expertise", "BDD")).Code);
        var expertises = (await Cli("comptes", "expertises", "dupont", "--json")).Json.EnumerateArray().ToList();
        Assert.Single(expertises);
        Assert.Equal("API", expertises[0].GetProperty("systeme").GetString());
        Assert.Equal("Commandes", expertises[0].GetProperty("detail").GetString());
        Assert.Equal(1, (await Cli("comptes", "modifier", "dupont", "--ajouter-expertise", "INCONNU")).Code);
        Assert.Equal(1, (await Cli("comptes", "modifier", "dupont", "--retirer-expertise", "WEB")).Code);
        Assert.Equal(2, (await Cli("comptes", "modifier", "dupont", "--detail", "sans expertise")).Code);
        Assert.True(await Journalise("compte.modifie", "expertise BDD retirée"));
        Assert.Contains("API : Commandes", (await Cli("comptes", "afficher", "dupont")).Sortie);

        Assert.Equal(0, (await Cli("comptes", "desactiver", "dupont")).Code);
        Assert.Contains("déjà désactivé", (await Cli("comptes", "desactiver", "dupont")).Sortie);
    }

    [Fact]
    public async Task Le_catalogue_des_systemes_se_gere_et_un_systeme_utilise_est_desactive()
    {
        Assert.Equal(0, (await Cli("systemes", "ajouter", "CRM", "--description", "Relation client", "--cible", "crm01")).Code);
        var doublon = await Cli("systemes", "ajouter", "crm");
        Assert.Equal(1, doublon.Code);
        Assert.Contains("existe déjà", doublon.Erreur);
        Assert.Equal(0, (await Cli("systemes", "modifier", "crm", "--nom", "CRM2", "--actif", "non")).Code);
        var crm = (await Cli("systemes", "lister", "--json")).Json.EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "CRM2");
        Assert.False(crm.GetProperty("actif").GetBoolean());
        Assert.Equal("crm01", crm.GetProperty("cible").GetString());

        Assert.Contains("supprimé du catalogue", (await Cli("systemes", "supprimer", "CRM2")).Sortie);
        Assert.Contains("désactivé au lieu d'être supprimé", (await Cli("systemes", "supprimer", "API")).Sortie);
        Assert.Equal(1, (await Cli("systemes", "supprimer", "CRM2")).Code);
        var systemes = (await Cli("systemes", "lister", "--json")).Json.EnumerateArray().ToList();
        Assert.DoesNotContain(systemes, s => s.GetProperty("nom").GetString() == "CRM2");
        Assert.False(systemes.Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("actif").GetBoolean());
        Assert.True(await Journalise("systeme.cree", "CRM"));
        Assert.True(await Journalise("systeme.desactive", "API"));
    }

    [Fact]
    public async Task Les_criteres_go_no_go_se_designent_par_numero_ou_identifiant()
    {
        var avant = (await Cli("criteres", "lister", "--json")).Json.GetArrayLength();
        Assert.Equal(0, (await Cli("criteres", "ajouter", "Astreinte prévenue", "--ordre", "999")).Code);
        var criteres = (await Cli("criteres", "lister", "--json")).Json.EnumerateArray().ToList();
        Assert.Equal(avant + 1, criteres.Count);
        Assert.Equal("Astreinte prévenue", criteres[^1].GetProperty("libelle").GetString());

        Assert.Equal(0, (await Cli("criteres", "modifier", criteres.Count.ToString(), "--libelle", "Astreinte prévenue et joignable", "--actif", "non")).Code);
        var modifie = (await Cli("criteres", "lister", "--json")).Json.EnumerateArray().Last();
        Assert.False(modifie.GetProperty("actif").GetBoolean());
        Assert.Equal(999, modifie.GetProperty("ordre").GetInt32());

        Assert.Equal(1, (await Cli("criteres", "supprimer", "0")).Code);
        Assert.Equal(1, (await Cli("criteres", "ajouter", "  ")).Code);
        Assert.Equal(1, (await Cli("criteres", "modifier", "1", "--ordre", "premier")).Code);
        Assert.Equal(0, (await Cli("criteres", "supprimer", modifie.GetProperty("id").GetString()!)).Code);
        Assert.Equal(avant, (await Cli("criteres", "lister", "--json")).Json.GetArrayLength());
        Assert.True(await Journalise("critere.supprime", "Astreinte prévenue et joignable"));
    }

    [Fact]
    public async Task Les_reglages_donnent_le_nombre_d_approbations_en_json()
    {
        Assert.Equal(0, (await Cli("reglages", "definir", "approbations-minimum", "3")).Code);
        var reglages = (await Cli("reglages", "lister", "--json")).Json;
        Assert.Equal(3, reglages.GetProperty("approbationsMinimum").GetInt32());
        Assert.Contains(reglages.GetProperty("reglages").EnumerateArray(), r => r.GetProperty("nom").GetString() == "url-publique");
    }

    [Fact]
    public async Task Les_resolutions_types_s_exportent_et_s_importent_avec_les_regles_de_l_application()
    {
        var fichier = Fichier("resolutions.json");
        Assert.Equal(0, (await Cli("resolutions", "exporter", fichier)).Code);
        var export = JsonDocument.Parse(await File.ReadAllTextAsync(fichier)).RootElement;
        var nombre = export.GetProperty("resolutions").GetArrayLength();
        Assert.True(nombre >= 4);
        Assert.Contains(export.GetProperty("resolutions").EnumerateArray(),
            r => r.GetProperty("systemes").EnumerateArray().Any(s => s.GetString() == "BDD"));

        // Mêmes titres : refusées par défaut, ignorées sur demande.
        var refus = await Cli("resolutions", "importer", fichier);
        Assert.Equal(1, refus.Code);
        Assert.Contains($"0 créée(s), 0 remplacée(s), 0 ignorée(s), {nombre} refusée(s)", refus.Sortie);
        Assert.Equal(0, (await Cli("resolutions", "importer", fichier, "--ignorer-existantes")).Code);

        // Une nouvelle, une avec un système inconnu, une incomplète, une en double dans le fichier.
        var autre = new
        {
            format = 1, application = "test", exporteeLe = DateTimeOffset.UtcNow,
            resolutions = new object[]
            {
                new { titre = "LDAP : compte de service verrouillé", symptomes = "Connexions refusées", diagnostic = "Journal LDAP", systemes = new[] { "ldap" },
                    corrections = new[] { new { instruction = "Déverrouiller le compte", resultatAttendu = "Compte actif" } },
                    verificationInstruction = "Se connecter", verificationAttendu = "Connexion acceptée" },
                new { titre = "Système absent", symptomes = "s", diagnostic = "d", systemes = new[] { "INCONNU" },
                    corrections = new[] { new { instruction = "i", resultatAttendu = "r" } }, verificationInstruction = "v", verificationAttendu = "a" },
                new { titre = "Incomplète", symptomes = "", diagnostic = "", systemes = Array.Empty<string>(), corrections = Array.Empty<object>(),
                    verificationInstruction = "", verificationAttendu = "" },
                new { titre = "ldap : COMPTE de service verrouillé", symptomes = "s", diagnostic = "d", systemes = Array.Empty<string>(),
                    corrections = new[] { new { instruction = "i", resultatAttendu = "r" } }, verificationInstruction = "v", verificationAttendu = "a" },
            },
        };
        var fichierAutre = Fichier("autres.json");
        await File.WriteAllTextAsync(fichierAutre, JsonSerializer.Serialize(autre));
        var essai = await Cli("resolutions", "importer", fichierAutre, "--essai", "--json");
        Assert.Equal(1, essai.Code);
        Assert.Equal(["creee", "refusee", "refusee", "refusee"], essai.Json.EnumerateArray().Select(r => r.GetProperty("resultat").GetString()!).ToArray());
        Assert.Equal(1, (await Cli("resolutions", "afficher", "LDAP : compte de service verrouillé")).Code); // essai : rien d'écrit

        var import = await Cli("resolutions", "importer", fichierAutre);
        Assert.Equal(1, import.Code);
        Assert.Contains("[créée    ] LDAP : compte de service verrouillé", import.Sortie);
        Assert.Contains("système « INCONNU » inconnu du catalogue", import.Sortie);
        Assert.True(await Journalise("resolutions.importees", "1 créée(s)"));
        var ldap = (await Cli("resolutions", "afficher", "ldap : compte de service verrouillé", "--json")).Json;
        Assert.Equal(1, ldap.GetProperty("version").GetInt32());
        var parSysteme = (await Cli("resolutions", "lister", "--systeme", "LDAP", "--json")).Json.EnumerateArray().ToList();
        Assert.Single(parSysteme);

        // Remplacer : nouvelle version de la fiche existante.
        Assert.Equal(0, (await Cli("resolutions", "importer", fichier, "--remplacer")).Code);
        var api = (await Cli("resolutions", "afficher", "API : réponse de test absente", "--json")).Json;
        Assert.Equal(2, api.GetProperty("version").GetInt32());
        Assert.Equal(2, (await Cli("resolutions", "importer", fichier, "--remplacer", "--ignorer-existantes")).Code);

        // Suppression : la fiche jamais utilisée disparaît, celle liée à une étape est désactivée.
        Assert.Contains("supprimée", (await Cli("resolutions", "supprimer", ldap.GetProperty("id").GetString()![..8])).Sortie);
        Assert.Contains("désactivée", (await Cli("resolutions", "supprimer", "API : réponse de test absente")).Sortie);
        Assert.DoesNotContain((await Cli("resolutions", "lister", "--json")).Json.EnumerateArray(), r => r.GetProperty("titre").GetString() == "API : réponse de test absente");
        Assert.Contains((await Cli("resolutions", "lister", "--inactives", "--json")).Json.EnumerateArray(),
            r => r.GetProperty("titre").GetString() == "API : réponse de test absente" && !r.GetProperty("actif").GetBoolean());
        Assert.True(await Journalise("resolution.desactivee", "API : réponse de test absente"));

        await File.WriteAllTextAsync(fichierAutre, "{ pas du json");
        Assert.Contains("JSON illisible", (await Cli("resolutions", "importer", fichierAutre)).Erreur);
    }

    [Fact]
    public async Task Les_mep_se_listent_et_s_exportent_avec_leur_journal()
    {
        var meps = (await Cli("meps", "lister", "--json")).Json.EnumerateArray().ToList();
        Assert.NotEmpty(meps);
        Assert.All(meps, m => Assert.False(m.GetProperty("estModele").GetBoolean()));
        Assert.NotEmpty((await Cli("meps", "lister", "--modeles", "--json")).Json.EnumerateArray());
        Assert.Equal(DonneesDemo.OutilRetourArriere,
            Assert.Single((await Cli("meps", "lister", "--phase", "retour-arriere", "--json")).Json.EnumerateArray()).GetProperty("outil").GetString());
        Assert.Equal(1, (await Cli("meps", "lister", "--phase", "demain")).Code);

        var id = meps[0].GetProperty("id").GetString()!;
        var fichier = Fichier("mep.json");
        Assert.Equal(0, (await Cli("meps", "exporter", id[..8], fichier)).Code);
        var export = JsonDocument.Parse(await File.ReadAllTextAsync(fichier)).RootElement;
        Assert.Equal(id, export.GetProperty("mep").GetProperty("id").GetString());
        // Type de fin et résultat constaté de chaque étape, et suivi des tests unitaires.
        Assert.True(export.GetProperty("mep").GetProperty("etapes")[0].TryGetProperty("resultatConstate", out _));
        Assert.True(export.GetProperty("testsUnitaires").TryGetProperty("nonFaits", out _));
        var journal = export.GetProperty("journal").EnumerateArray().ToList();
        Assert.NotEmpty(journal);
        Assert.True(journal[0].GetProperty("id").GetInt64() < journal[^1].GetProperty("id").GetInt64() || journal.Count == 1);
        Assert.Equal(1, (await Cli("meps", "exporter", "ffffffff")).Code);
        Assert.True(await Journalise("mep.exportee", id));
    }

    [Fact]
    public async Task Le_journal_se_filtre_par_date_et_par_nombre()
    {
        await Cli("systemes", "ajouter", "JOURNAL");
        await Cli("systemes", "ajouter", "JOURNAL2");
        Assert.Equal(2, (await JournalAdmin(2)).Count);
        Assert.Contains("Aucun événement", (await Cli("journal", "--depuis", "2999-01-01")).Sortie);
        Assert.NotEmpty((await Cli("journal", "--depuis", DateTime.UtcNow.ToString("yyyy-MM-dd"), "--json")).Json.EnumerateArray());
        Assert.Equal(1, (await Cli("journal", "--depuis", "hier")).Code);
        Assert.Contains("[systeme.cree]", (await Cli("journal", "--limite", "5")).Sortie);
    }

    [Fact]
    public async Task Les_sauvegardes_se_listent_et_la_restauration_exige_confirmation_et_application_arretee()
    {
        Assert.Equal(0, (await Cli("sauvegarder")).Code);
        var archives = (await Cli("sauvegardes", "lister", "--json")).Json.EnumerateArray().ToList();
        var archive = Assert.Single(archives);
        Assert.True(archive.GetProperty("lisible").GetBoolean());
        Assert.True(archive.GetProperty("lignes").GetInt64() > 0);
        var nom = archive.GetProperty("nom").GetString()!;
        Assert.Contains(nom, (await Cli("sauvegardes", "lister")).Sortie);

        var sansConfirmation = await Cli("sauvegardes", "restaurer", nom);
        Assert.Equal(1, sansConfirmation.Code);
        Assert.Contains("--oui", sansConfirmation.Erreur);
        Assert.Equal(1, (await Cli("sauvegardes", "restaurer", "absente.tar.gz", "--oui")).Code);
        Assert.Equal(2, (await Cli("sauvegardes", "restaurer")).Code);

        // Une autre session ouverte sur la base (l'application en marche) : refus, rien n'est touché.
        var chaine = Environment.GetEnvironmentVariable("ConnectionStrings__Mep")!;
        await using (var autre = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(chaine) { Pooling = false }.ConnectionString))
        {
            await autre.OpenAsync();
            var refus = await Cli("sauvegardes", "restaurer", nom, "--oui");
            Assert.Equal(1, refus.Code);
            Assert.Contains("semble en marche", refus.Erreur);
        }
        Assert.Equal(0, (await Cli("comptes", "lister")).Code);
    }

    [Fact]
    public async Task La_configuration_s_affiche_sans_secret()
    {
        var texte = await Cli("config", "afficher");
        Assert.Equal(0, texte.Code);
        Assert.Contains("défini (masqué)", texte.Sortie);
        Assert.Contains("gestion-mep reglages lister", texte.Sortie);
        var json = await Cli("config", "afficher", "--json");
        var demarrage = json.Json.GetProperty("demarrage");
        Assert.Equal("défini (masqué)", demarrage.GetProperty("baseMotDePasse").GetString());
        Assert.Equal("oui", demarrage.GetProperty("demonstration").GetString());
        Assert.NotEmpty(json.Json.GetProperty("reglages").EnumerateArray());
        var motDePasse = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__Mep")).Password!;
        Assert.DoesNotContain($"Password={motDePasse}", texte.Sortie + json.Sortie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secretChiffre", json.Sortie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_mise_a_jour_en_ligne_de_commande_suit_les_regles_de_la_console()
    {
        var etat = await Cli("mettre-a-jour");
        Assert.Equal(0, etat.Code);
        Assert.Contains("vérification désactivée", etat.Sortie); // pas d'appel à GitHub pendant les tests
        // Sans Docker (tests lancés hors conteneur), il n'y a pas de service de mise à jour à afficher.
        if (Installation.EstDocker) Assert.Contains("absent", etat.Sortie);
        var refus = await Cli("mettre-a-jour", "--oui");
        Assert.Equal(1, refus.Code);
        Assert.Contains(Installation.EstDocker ? "pas actif" : "Installation sans Docker", refus.Erreur);

        // Méthode partagée avec la console : service présent, dernière version publiée, aucune MEP en cours.
        Directory.CreateDirectory(_api.DossierMiseAJour);
        await File.WriteAllTextAsync(Path.Combine(_api.DossierMiseAJour, "etat.json"),
            $$"""{"statut":"pret","version":"","message":"prêt","le":"{{DateTimeOffset.UtcNow:O}}","battement":"{{DateTimeOffset.UtcNow:O}}"}""");
        var publiee = new EtatMiseAJour(true, "depot", "99.0.0", null, null, DateTimeOffset.UtcNow, null);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var installation = scope.ServiceProvider.GetRequiredService<InstallationMiseAJour>();
        // Les MEP de démonstration en cours (exécution, recette, retour arrière) sont clôturées : on part d'aucune MEP en cours.
        foreach (var demoEnCours in await db.Meps.Where(m => m.Phase == Phase.Execution || m.Phase == Phase.Recette || m.Phase == Phase.RetourArriere).ToListAsync())
            demoEnCours.Phase = Phase.Cloturee;
        await db.SaveChangesAsync();
        var mep = await db.Meps.FirstAsync(m => !m.EstModele && m.Phase == Phase.Preparation);
        var phase = mep.Phase;
        mep.Phase = Phase.Execution;
        await db.SaveChangesAsync();
        var enCours = await Assert.ThrowsAsync<ErreurHttp>(() => ApplicationEndpoints.DemanderInstallation(db, null, "99.0.0", publiee, installation));
        Assert.Contains("MEP en cours", enCours.Message);
        mep.Phase = phase;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ErreurHttp>(() => ApplicationEndpoints.DemanderInstallation(db, null, "98.0.0", publiee, installation));

        var demande = await ApplicationEndpoints.DemanderInstallation(db, null, "99.0.0", publiee, installation);
        Assert.Equal("demandee", demande.Installateur.Statut);
        Assert.Contains("\"99.0.0\"", await File.ReadAllTextAsync(Path.Combine(_api.DossierMiseAJour, "demande.json")));
        Assert.True(await Journalise("mise-a-jour.demandee", "99.0.0"));
    }
}
