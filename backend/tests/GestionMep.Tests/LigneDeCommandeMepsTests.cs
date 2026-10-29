using System.Text.Json;
using System.Text.Json.Nodes;
using GestionMep.Domain;
using static GestionMep.Tests.LigneDeCommandeConventionsTests;

namespace GestionMep.Tests;

/// <summary>
/// MEP en ligne de commande : résumé, journal, exportation et importation (même serveur ou autre serveur), duplication,
/// annulation, suppression et pilotage de secours, avec les règles de l'application et la confirmation des gestes destructifs.
/// </summary>
[Collection("Api")]
public class LigneDeCommandeMepsTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<string> IdMep(string outil, bool modeles = false)
    {
        var meps = (await Cli(modeles ? ["meps", "lister", "--modeles", "--json"] : ["meps", "lister", "--json"])).Json.EnumerateArray();
        return meps.First(m => m.GetProperty("outil").GetString()!.Contains(outil, StringComparison.Ordinal)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Mep(string id) => (await Cli("meps", "afficher", id, "--json")).Json;

    private static async Task<string> IdCompte(string login) => (await Cli("comptes", "afficher", login, "--json")).Json.GetProperty("id").GetString()!;

    private static async Task<List<JsonElement>> JournalAdmin(string type) =>
        (await Cli("journal", "--type", type, "--json")).Json.EnumerateArray().ToList();

    [Fact]
    public async Task Une_mep_s_affiche_et_son_journal_se_suit_depuis_un_script()
    {
        var id = await IdMep(DonneesDemo.OutilExecution);
        var resume = await Cli("meps", "afficher", id[..8]);
        Assert.Equal(0, resume.Code);
        Assert.Contains($"MEP « {DonneesDemo.OutilExecution}", resume.Sortie);
        Assert.Contains("Pilote        : ", resume.Sortie);
        Assert.Contains("Phase         : exécution", resume.Sortie);
        Assert.Equal(id, (await Mep(id[..8])).GetProperty("id").GetString());
        Assert.Equal(5, (await Cli("meps", "afficher", "ffffffff")).Code);

        var journal = (await Cli("meps", "journal", id[..8], "--limite", "3", "--json")).Json.EnumerateArray().ToList();
        Assert.Equal(3, journal.Count);
        var dernier = journal[^1].GetProperty("id").GetInt64();
        var suite = (await Cli("meps", "journal", id[..8], "--apres", dernier.ToString(), "--json")).Json.EnumerateArray().ToList();
        Assert.Equal(2, suite.Count);
        Assert.All(suite, e => Assert.True(e.GetProperty("id").GetInt64() > dernier));
        var etapes = (await Cli("meps", "journal", id, "--type", "etape", "--json")).Json.EnumerateArray().ToList();
        Assert.NotEmpty(etapes);
        Assert.All(etapes, e => Assert.StartsWith("etape.", e.GetProperty("type").GetString()));
        Assert.Contains("[etape.", (await Cli("meps", "journal", id, "--limite", "5")).Sortie);
        Assert.Equal(1, (await Cli("meps", "journal", id, "--apres", "hier")).Code);
    }

    [Fact]
    public async Task Un_modele_exporte_se_reimporte_sur_le_meme_serveur_comme_une_duplication()
    {
        var modele = await IdMep("déploiement API standard", modeles: true);
        var fichier = Path.Combine(_api.DossierPieces, "modele.json");
        Directory.CreateDirectory(_api.DossierPieces);
        Assert.Equal(0, (await Cli("meps", "exporter", modele[..8], fichier)).Code);
        var export = JsonNode.Parse(await File.ReadAllTextAsync(fichier))!;
        Assert.Equal(2, export["format"]!.GetValue<int>());
        Assert.NotNull(export["contenu"]);
        Assert.NotNull(export["references"]!["comptes"]);

        var avant = (await Cli("meps", "lister", "--modeles", "--json")).Json.GetArrayLength();
        var essai = await Cli("meps", "importer", fichier, "--pour", "claire", "--modele", "--essai");
        Assert.Equal(0, essai.Code);
        Assert.Contains("Essai, rien n'a été créé", essai.Sortie);
        Assert.Equal(avant, (await Cli("meps", "lister", "--modeles", "--json")).Json.GetArrayLength());

        var import = await Cli("meps", "importer", fichier, "--pour", "claire", "--modele", "--json");
        Assert.Equal(0, import.Code);
        Assert.Empty(import.Json.GetProperty("avertissements").EnumerateArray());
        var id = import.Json.GetProperty("resultat").GetProperty("id").GetString()!;
        Assert.True(import.Json.GetProperty("resultat").GetProperty("estModele").GetBoolean());
        Assert.Equal(avant + 1, (await Cli("meps", "lister", "--modeles", "--json")).Json.GetArrayLength());
        var mep = await Mep(id);
        var source = await Mep(modele);
        Assert.Equal(source.GetProperty("etapes").GetArrayLength(), mep.GetProperty("etapes").GetArrayLength());
        Assert.Equal(source.GetProperty("outil").GetString(), mep.GetProperty("outil").GetString());
        var claire = await IdCompte("claire");
        Assert.Contains(mep.GetProperty("participants").EnumerateArray(), p => p.GetProperty("utilisateurId").GetString() == claire);
        var journal = (await Cli("meps", "journal", id, "--json")).Json.EnumerateArray().ToList();
        Assert.Contains(journal, e => e.GetProperty("detail").GetString()!.Contains("importé depuis modele.json", StringComparison.Ordinal)
            && e.GetProperty("auteur").GetString() == "Ligne de commande");

        // MEP depuis le modèle (entrée standard non utilisée ici) : outil sans « Modèle : ».
        var mepDepuisModele = await Cli("meps", "importer", fichier, "--pour", "bob", "--json");
        Assert.Equal(0, mepDepuisModele.Code);
        Assert.False((await Mep(mepDepuisModele.Json.GetProperty("resultat").GetProperty("id").GetString()!)).GetProperty("estModele").GetBoolean());

        Assert.Equal(2, (await Cli("meps", "importer", fichier)).Code); // --pour manquant
        Assert.Equal(5, (await Cli("meps", "importer", fichier, "--pour", "inconnu")).Code);
        Assert.Equal(5, (await Cli("meps", "importer", Path.Combine(_api.DossierPieces, "absent.json"), "--pour", "bob")).Code);
        export["format"] = 1;
        export.AsObject().Remove("contenu");
        await File.WriteAllTextAsync(fichier, export.ToJsonString());
        var ancien = await Cli("meps", "importer", fichier, "--pour", "bob");
        Assert.Equal(1, ancien.Code);
        Assert.Contains("format 1", ancien.Erreur);
        await File.WriteAllTextAsync(fichier, "{ pas du json");
        Assert.Equal(1, (await Cli("meps", "importer", fichier, "--pour", "bob")).Code);
    }

    [Fact]
    public async Task Venant_d_un_autre_serveur_les_comptes_et_systemes_sont_retrouves_par_login_et_nom()
    {
        var id = await IdMep(DonneesDemo.OutilExecution);
        var fichier = Path.Combine(_api.DossierPieces, "mep.json");
        Directory.CreateDirectory(_api.DossierPieces);
        Assert.Equal(0, (await Cli("meps", "exporter", id, fichier)).Code);
        var bob = await IdCompte("bob");
        var bdd = (await Cli("systemes", "afficher", "BDD", "--json")).Json.GetProperty("systeme").GetProperty("id").GetString()!;
        // Autre serveur : mêmes logins et mêmes noms de systèmes, autres identifiants.
        var autreBob = Guid.NewGuid().ToString();
        var autreBdd = Guid.NewGuid().ToString();
        var texte = (await File.ReadAllTextAsync(fichier)).Replace(bob, autreBob, StringComparison.Ordinal).Replace(bdd, autreBdd, StringComparison.Ordinal);
        await File.WriteAllTextAsync(fichier, texte);

        var import = await Cli("meps", "importer", fichier, "--pour", "alice", "--json");
        Assert.Equal(0, import.Code);
        Assert.True(import.Json.GetProperty("avertissements").GetArrayLength() == 0, import.Sortie);
        var mep = await Mep(import.Json.GetProperty("resultat").GetProperty("id").GetString()!);
        Assert.Equal("Preparation", mep.GetProperty("phase").GetString());
        Assert.Contains(mep.GetProperty("participants").EnumerateArray(), p => p.GetProperty("utilisateurId").GetString() == bob);
        Assert.Contains(mep.GetProperty("systemes").EnumerateArray(), s => s.GetProperty("catalogueId").GetString() == bdd);
        Assert.All(mep.GetProperty("approbations").EnumerateArray(), a => Assert.Equal(JsonValueKind.Null, a.GetProperty("decision").ValueKind));

        // Un login absent de ce serveur : la personne est gardée sans compte, et c'est signalé.
        var export = JsonNode.Parse(texte)!;
        foreach (var c in export["references"]!["comptes"]!.AsArray().Where(c => c!["id"]!.GetValue<string>() == autreBob))
            c!["login"] = "fantome";
        await File.WriteAllTextAsync(fichier, export.ToJsonString());
        var ecarts = await Cli("meps", "importer", fichier, "--pour", "alice", "--json");
        Assert.Equal(0, ecarts.Code);
        Assert.Contains(ecarts.Json.GetProperty("avertissements").EnumerateArray(), a => a.GetString()!.Contains("pas de compte sur ce serveur", StringComparison.Ordinal));
        var sansCompte = await Mep(ecarts.Json.GetProperty("resultat").GetProperty("id").GetString()!);
        Assert.DoesNotContain(sansCompte.GetProperty("participants").EnumerateArray(), p => p.GetProperty("utilisateurId").GetString() == bob);
    }

    [Fact]
    public async Task Une_mep_se_duplique_depuis_un_modele_pour_un_compte()
    {
        var modele = await IdMep("déploiement API standard", modeles: true);
        var copie = await Cli("meps", "dupliquer", modele[..8], "--pour", "claire");
        Assert.Equal(0, copie.Code);
        var id = copie.Sortie.Split('\n')[0].Trim();
        var mep = await Mep(id);
        Assert.False(mep.GetProperty("estModele").GetBoolean());
        Assert.DoesNotContain("Modèle", mep.GetProperty("outil").GetString());
        var claire = await IdCompte("claire");
        Assert.Contains(mep.GetProperty("participants").EnumerateArray(), p => p.GetProperty("utilisateurId").GetString() == claire);
        // --quiet : l'identifiant seul, pour un script.
        var seul = await Cli("meps", "dupliquer", id, "--pour", "bob", "--modele", "-q");
        Assert.True(Guid.TryParse(seul.Sortie.Trim(), out _));
        Assert.Equal(2, (await Cli("meps", "dupliquer", id)).Code);
        Assert.Equal(5, (await Cli("meps", "dupliquer", id, "--pour", "personne")).Code);
        Assert.Equal(0, (await Cli("comptes", "desactiver", "claire")).Code);
        Assert.Equal(1, (await Cli("meps", "dupliquer", id, "--pour", "claire")).Code);
    }

    [Fact]
    public async Task Annuler_et_supprimer_demandent_confirmation_et_suivent_les_regles_de_l_application()
    {
        var id = (await Cli("meps", "dupliquer", await IdMep("déploiement API standard", modeles: true), "--pour", "bob", "-q")).Sortie.Trim();

        Assert.Equal(2, (await Cli("meps", "annuler", id, "--oui")).Code); // --motif manquant
        var sansOui = await Cli("meps", "annuler", id, "--motif", "Doublon");
        Assert.Equal(4, sansOui.Code);
        Assert.Contains("--oui", sansOui.Erreur);
        Assert.Equal("Preparation", (await Mep(id)).GetProperty("phase").GetString());
        Assert.Equal(4, (await Cli("meps", "annuler", id, "--motif", "Doublon", "--non-interactif")).Code);
        Assert.Equal(0, (await Cli("meps", "annuler", id, "--motif", "Doublon", "--oui")).Code);
        Assert.Equal("Annulee", (await Mep(id)).GetProperty("phase").GetString());
        var encore = await Cli("meps", "annuler", id, "--motif", "Doublon", "--oui");
        Assert.Equal(0, encore.Code);
        Assert.Contains("déjà annulée", encore.Sortie);
        Assert.Single(await JournalAdmin("mep.annulee"));

        var aSupprimer = (await Cli("meps", "dupliquer", id, "--pour", "bob", "-q")).Sortie.Trim();
        Assert.Equal(2, (await Cli("meps", "supprimer", aSupprimer, "--oui")).Code);
        Assert.Equal(4, (await Cli("meps", "supprimer", aSupprimer, "--motif", "Créée par erreur")).Code);
        Assert.Equal(0, (await Cli("meps", "afficher", aSupprimer)).Code);
        Assert.Equal(0, (await Cli("meps", "supprimer", aSupprimer, "--motif", "Créée par erreur", "--oui")).Code);
        Assert.Equal(5, (await Cli("meps", "afficher", aSupprimer)).Code);
        var trace = Assert.Single(await JournalAdmin("mep.supprimee"));
        Assert.Equal("Ligne de commande", trace.GetProperty("auteur").GetString());
        Assert.Contains("Créée par erreur", trace.GetProperty("detail").GetString());

        // Une MEP qui a eu un Go ne se supprime pas : refus avant toute question.
        var lancee = await IdMep(DonneesDemo.OutilExecution);
        var refus = await Cli("meps", "supprimer", lancee, "--motif", "x");
        Assert.Equal(1, refus.Code);
        Assert.Contains("Go", refus.Erreur);
        Assert.Equal(1, (await Cli("meps", "annuler", lancee, "--motif", "x", "--oui")).Code);
    }

    [Fact]
    public async Task Le_pilotage_de_secours_confie_une_mep_lancee_a_un_autre_compte()
    {
        var id = await IdMep(DonneesDemo.OutilExecution);
        Assert.Equal(2, (await Cli("meps", "piloter", id, "claire")).Code);
        var fait = await Cli("meps", "piloter", id, "claire", "--motif", "Pilote injoignable", "--json");
        Assert.Equal(0, fait.Code);
        var mep = await Mep(id);
        var pilote = mep.GetProperty("participants").EnumerateArray().First(p => p.GetProperty("id").GetString() == mep.GetProperty("piloteId").GetString());
        Assert.Equal(await IdCompte("claire"), pilote.GetProperty("utilisateurId").GetString());
        var journal = (await Cli("meps", "journal", id, "--limite", "5", "--json")).Json.EnumerateArray();
        Assert.Contains(journal, e => e.GetProperty("detail").GetString()!.Contains("Pilote injoignable", StringComparison.Ordinal));
        Assert.Equal(5, (await Cli("meps", "piloter", id, "inconnu", "--motif", "x")).Code);

        var enPreparation = await IdMep(DonneesDemo.OutilPrete);
        Assert.Equal(1, (await Cli("meps", "piloter", enPreparation, "claire", "--motif", "x")).Code);
    }
}
