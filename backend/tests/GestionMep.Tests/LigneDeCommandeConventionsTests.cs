using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionMep.Api.Cli;

namespace GestionMep.Tests;

/// <summary>
/// Conventions communes de la ligne de commande : aide et exemples sur chaque commande, codes de retour, sortie --json
/// stable (enveloppe et erreurs), --quiet, suggestions, --config, complétion des shells et référence de la documentation.
/// </summary>
[Collection("Api")]
public partial class LigneDeCommandeConventionsTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    internal record Resultat(int Code, string Sortie, string Erreur)
    {
        public JsonElement Json => JsonDocument.Parse(Sortie).RootElement;
        public JsonElement JsonErreur => JsonDocument.Parse(Erreur).RootElement;
    }

    internal static async Task<Resultat> Cli(params string[] args)
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

    [GeneratedRegex(@"^#### `(?<nom>[^`]+)`$", RegexOptions.Multiline)]
    private static partial Regex TitreCommande();

    /// <summary>Toutes les commandes, d'après la référence générée (une section par commande).</summary>
    private static async Task<List<string>> Commandes() =>
        TitreCommande().Matches((await Cli("aide", "--markdown")).Sortie).Select(m => m.Groups["nom"].Value).ToList();

    [Fact]
    public async Task Chaque_commande_a_son_aide_avec_usage_exemples_et_codes_de_retour()
    {
        var commandes = await Commandes();
        Assert.True(commandes.Count > 80, $"{commandes.Count} commandes seulement dans la référence");
        foreach (var commande in commandes)
        {
            var aide = await Cli([.. commande.Split(' '), "--help"]);
            Assert.True(aide.Code == 0, $"{commande} --help : code {aide.Code} {aide.Erreur}");
            Assert.Contains($"Usage : gestion-mep {commande}", aide.Sortie);
            Assert.True(aide.Sortie.Contains("Exemple", StringComparison.Ordinal), $"{commande} --help : pas d'exemple");
            Assert.Contains("Codes de retour : 0 succès, 1 échec, 2 commande mal formée", aide.Sortie);
            Assert.Equal("", aide.Erreur);
            Assert.Equal(aide.Sortie, (await Cli(["aide", .. commande.Split(' ')])).Sortie);
        }
        // Aide d'un groupe ; aide globale avec toutes les commandes ; options communes n'importe où.
        var groupe = await Cli("meps", "--help");
        Assert.Equal(0, groupe.Code);
        Assert.Contains("gestion-mep meps importer", groupe.Sortie);
        var globale = await Cli("--help");
        foreach (var commande in commandes) Assert.Contains($"gestion-mep {commande}", globale.Sortie);
        Assert.Equal(globale.Sortie, (await Cli("-h")).Sortie);
        Assert.Equal(globale.Sortie, (await Cli()).Sortie);
        Assert.Equal(2, (await Cli("aide", "inconnue")).Code);
    }

    [Fact]
    public async Task Une_commande_mal_formee_rend_2_sur_la_sortie_d_erreur_avec_une_suggestion()
    {
        var faute = await Cli("meps", "lsiter");
        Assert.Equal(2, faute.Code);
        Assert.Equal("", faute.Sortie);
        Assert.Contains("vouliez-vous dire « gestion-mep meps lister » ?", faute.Erreur);

        var groupe = await Cli("comptse", "lister");
        Assert.Equal(2, groupe.Code);
        Assert.Contains("gestion-mep comptes", groupe.Erreur);

        var option = await Cli("comptes", "lister", "--jsno");
        Assert.Equal(2, option.Code);
        Assert.Contains("--json", option.Erreur);
        Assert.Contains("gestion-mep comptes lister --help", option.Erreur);

        Assert.Equal(2, (await Cli("comptes", "lister", "en-trop")).Code);
        Assert.Equal(2, (await Cli("comptes", "afficher")).Code);
        Assert.Equal(2, (await Cli("comptes", "lister", "--quiet", "--verbose")).Code);
        Assert.Equal(2, (await Cli("systemes", "historique", "BDD", "--limite")).Code);
    }

    [Fact]
    public async Task La_sortie_json_est_une_enveloppe_stable_et_les_erreurs_json_vont_sur_la_sortie_d_erreur()
    {
        var cree = await Cli("--json", "systemes", "ajouter", "JSON1", "--description", "Essai");
        Assert.Equal(0, cree.Code);
        Assert.Equal("", cree.Erreur);
        var enveloppe = cree.Json;
        Assert.Equal(0, enveloppe.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Array, enveloppe.GetProperty("messages").ValueKind);
        Assert.Equal(JsonValueKind.Array, enveloppe.GetProperty("avertissements").ValueKind);
        Assert.Equal("JSON1", enveloppe.GetProperty("resultat").GetProperty("nom").GetString());

        // Une liste : le tableau seul, comme l'API.
        Assert.Equal(JsonValueKind.Array, (await Cli("systemes", "lister", "--json")).Json.ValueKind);

        var introuvable = await Cli("comptes", "afficher", "zorro", "--json");
        Assert.Equal(5, introuvable.Code);
        Assert.Equal("", introuvable.Sortie);
        Assert.Equal(5, introuvable.JsonErreur.GetProperty("code").GetInt32());
        Assert.Contains("zorro", introuvable.JsonErreur.GetProperty("erreur").GetString());

        var usage = await Cli("comptes", "afficher", "--json");
        Assert.Equal(2, usage.Code);
        Assert.Equal(2, usage.JsonErreur.GetProperty("code").GetInt32());

        var regle = await Cli("systemes", "ajouter", "json1", "--json");
        Assert.Equal(1, regle.Code);
        Assert.Equal(1, regle.JsonErreur.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Quiet_ne_garde_que_les_donnees_et_verbose_detaille_sur_la_sortie_d_erreur()
    {
        var silencieux = await Cli("systemes", "ajouter", "QUIET", "-q");
        Assert.Equal(0, silencieux.Code);
        Assert.Equal("", silencieux.Sortie);
        Assert.Contains("QUIET", (await Cli("systemes", "lister", "--quiet")).Sortie);

        var bavard = await Cli("comptes", "lister", "--verbose");
        Assert.Equal(0, bavard.Code);
        Assert.Contains("alice", bavard.Sortie);
        Assert.Contains("Base :", bavard.Erreur);
        Assert.DoesNotContain("Password=", bavard.Erreur, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres;", bavard.Erreur, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Config_designe_le_fichier_de_configuration_qui_l_emporte_sur_l_environnement()
    {
        Assert.Equal(3, (await Cli("comptes", "lister", "--config", "/inexistant/gestion-mep.conf")).Code);

        // Le fichier d'installation l'emporte sur les variables d'environnement : base injoignable, code 3.
        var fichier = Path.Combine(_api.DossierPieces, "gestion-mep.conf");
        Directory.CreateDirectory(_api.DossierPieces);
        await File.WriteAllTextAsync(fichier, "# essai\nBASE_DE_DONNEES=Host=127.0.0.1;Port=1;Database=x;Username=x;Password=secret-a-masquer;Timeout=2\n");
        var injoignable = await Cli("comptes", "lister", "--config", fichier, "-v");
        Assert.Equal(3, injoignable.Code);
        Assert.Contains("base de données injoignable", injoignable.Erreur);
        Assert.DoesNotContain("secret-a-masquer", injoignable.Erreur);
        // La variable GESTION_MEP_CONFIG est rétablie après la commande.
        Assert.Equal(0, (await Cli("comptes", "lister")).Code);
    }

    [Fact]
    public async Task Version_et_sante_repondent_sans_base()
    {
        var version = await Cli("version", "--json");
        Assert.Equal(0, version.Code);
        Assert.False(string.IsNullOrEmpty(version.Json.GetProperty("version").GetString()));
        Assert.Equal(version.Sortie, (await Cli("--version", "--json")).Sortie);

        var sante = await Cli("sante", "--url", "http://127.0.0.1:1", "--json");
        Assert.Equal(1, sante.Code);
        Assert.Equal("injoignable", sante.Json.GetProperty("statut").GetString());
    }

    [Fact]
    public async Task La_completion_couvre_toutes_les_commandes_pour_bash_zsh_et_powershell()
    {
        var commandes = await Commandes();
        var bash = await Cli("completion", "bash");
        Assert.Equal(0, bash.Code);
        foreach (var groupe in commandes.Select(c => c.Split(' ')[0]).Distinct()) Assert.Contains(groupe, bash.Sortie);
        Assert.Contains("complete -F _gestion_mep gestion-mep", bash.Sortie);
        Assert.Contains("importer", bash.Sortie);
        Assert.Contains("--pour", bash.Sortie);
        await VerifierSyntaxe("bash", bash.Sortie);

        var zsh = await Cli("completion", "zsh");
        Assert.Contains("bashcompinit", zsh.Sortie);
        if (File.Exists("/usr/bin/zsh") || File.Exists("/bin/zsh")) await VerifierSyntaxe("zsh", zsh.Sortie);

        var powershell = await Cli("completion", "powershell");
        Assert.Contains("Register-ArgumentCompleter", powershell.Sortie);
        Assert.Contains("'meps'", powershell.Sortie);

        Assert.Equal(2, (await Cli("completion", "fish")).Code);
    }

    private async Task VerifierSyntaxe(string shell, string script)
    {
        var fichier = Path.Combine(_api.DossierPieces, $"completion.{shell}");
        Directory.CreateDirectory(_api.DossierPieces);
        await File.WriteAllTextAsync(fichier, script);
        using var p = Process.Start(new ProcessStartInfo(shell, ["-n", fichier]) { RedirectStandardError = true })!;
        var erreur = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, $"{shell} -n : {erreur}");
    }

    /// <summary>
    /// La référence de docs/Ligne-de-commande.md (entre les repères) est générée par « gestion-mep aide --markdown ».
    /// Après un changement de commande : <c>MAJ_REFERENCE_CLI=1 dotnet test --filter ReferenceCli</c>.
    /// </summary>
    [Fact]
    public async Task La_reference_de_la_documentation_est_a_jour_ReferenceCli()
    {
        const string debut = "<!-- reference-cli:debut (généré par « gestion-mep aide --markdown », ne pas modifier à la main) -->";
        const string fin = "<!-- reference-cli:fin -->";
        var reference = (await Cli("aide", "--markdown")).Sortie.ReplaceLineEndings("\n");
        var fichier = Path.Combine(OpenApiTests.RacineDepot(), "docs", "Ligne-de-commande.md");
        var texte = (await File.ReadAllTextAsync(fichier)).ReplaceLineEndings("\n");
        var i = texte.IndexOf(debut, StringComparison.Ordinal);
        var j = texte.IndexOf(fin, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i, "Repères de la référence absents de docs/Ligne-de-commande.md.");
        var attendu = texte[..(i + debut.Length)] + "\n\n" + reference.TrimEnd('\n') + "\n\n" + texte[j..];
        if (Environment.GetEnvironmentVariable("MAJ_REFERENCE_CLI") == "1")
        {
            await File.WriteAllTextAsync(fichier, attendu);
            texte = attendu;
        }
        Assert.True(texte == attendu,
            "docs/Ligne-de-commande.md n'est plus à jour : lancer MAJ_REFERENCE_CLI=1 dotnet test --filter ReferenceCli.");
    }
}
