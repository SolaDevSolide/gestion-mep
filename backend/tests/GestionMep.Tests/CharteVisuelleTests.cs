using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GestionMep.Api.Services;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Tests;

/// <summary>
/// Charte visuelle, calculs du serveur : mêmes résultats que l'aperçu de la console (cas partagés
/// <c>frontend/src/charte/vecteurs-charte.json</c>), lecture du fichier charte.json, ordre console &gt; fichier &gt; défaut,
/// feuille /charte.css et palette des exports Excel.
/// </summary>
public class CharteVisuelleTests
{
    private static readonly JsonElement Vecteurs = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "frontend", "src", "charte", "vecteurs-charte.json"))).RootElement;

    public static TheoryData<string> Derivations() => [.. Vecteurs.GetProperty("derivations").EnumerateArray().Select(d => d.GetProperty("nom").GetString()!)];
    public static TheoryData<string> Lectures() => [.. Vecteurs.GetProperty("lectures").EnumerateArray().Select(d => d.GetProperty("nom").GetString()!)];

    private static JsonElement Cas(string liste, string nom) =>
        Vecteurs.GetProperty(liste).EnumerateArray().Single(d => d.GetProperty("nom").GetString() == nom);

    private static Charte Lue(string json) => CharteVisuelle.Lire(json).Charte ?? throw new InvalidOperationException("charte invalide");

    private static string Hex(string x) => "#" + x.TrimStart('#').ToLowerInvariant();

    [Theory]
    [MemberData(nameof(Derivations))]
    public void Le_serveur_calcule_comme_la_console(string nom)
    {
        var cas = Cas("derivations", nom);
        var c = Lue(cas.GetProperty("charte").GetRawText());

        foreach (var role in CharteVisuelle.Roles)
            Assert.Equal(cas.GetProperty("couleursSombre").GetProperty(role).GetString(), CharteVisuelle.CouleurRole(c, role, ModeCharte.Sombre));

        foreach (var (mode, cle) in new[] { (ModeCharte.Clair, "clair"), (ModeCharte.Sombre, "sombre") })
        {
            var attendus = cas.GetProperty("ecarts").GetProperty(cle).EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)).ToList();
            Assert.Equal(attendus, CharteVisuelle.Ecarts(c, mode));
        }

        var controles = CharteVisuelle.Controles(c);
        var attendus2 = cas.GetProperty("controles").EnumerateArray().ToList();
        Assert.Equal(attendus2.Select(x => x.GetProperty("id").GetString()), controles.Select(x => x.Id));
        foreach (var (x, k) in attendus2.Zip(controles))
        {
            Assert.Equal(x.GetProperty("type").GetString(), k.Type);
            Assert.Equal(x.GetProperty("role").GetString(), k.Role);
            Assert.Equal(x.GetProperty("rapport").GetDouble(), Math.Round(k.Rapport, 6), 6);
            Assert.Equal(x.GetProperty("minimum").GetDouble(), k.Minimum);
            Assert.Equal(x.GetProperty("reussi").GetBoolean(), k.Reussi);
            if (x.TryGetProperty("correction", out var corr))
            {
                Assert.NotNull(k.Correction);
                Assert.Equal(corr.GetProperty("cible").GetString(), k.Correction.Cible);
                Assert.Equal(corr.GetProperty("couleur").GetString(), k.Correction.Couleur);
            }
            else Assert.Null(k.Correction);
        }

        var excel = cas.GetProperty("excel");
        var p = CharteVisuelle.PaletteExcel(c);
        string E(string cle) => excel.GetProperty(cle).GetString()!;
        string[] Paire(string cle) => [.. excel.GetProperty(cle).EnumerateArray().Select(v => v.GetString()!)];
        Assert.Equal(E("primaire"), Hex(p.Primaire));
        Assert.Equal(E("secondaire"), Hex(p.Secondaire));
        Assert.Equal(E("tertiaire"), Hex(p.Tertiaire));
        Assert.Equal(E("fondEnTete"), Hex(p.FondEnTete));
        Assert.Equal(E("texteEnTete"), Hex(p.TexteEnTete));
        Assert.Equal(E("texteTitre"), Hex(p.TexteTitre));
        Assert.Equal(E("texteLien"), Hex(p.TexteLien));
        Assert.Equal(E("fondSection"), Hex(p.FondSection));
        Assert.Equal(E("texteSection"), Hex(p.TexteSection));
        Assert.Equal(Paire("succes"), new[] { Hex(p.Succes.Fond), Hex(p.Succes.Texte) });
        Assert.Equal(Paire("echec"), new[] { Hex(p.Echec.Fond), Hex(p.Echec.Texte) });
        Assert.Equal(Paire("enCours"), new[] { Hex(p.EnCours.Fond), Hex(p.EnCours.Texte) });
        Assert.Equal(Paire("attention"), new[] { Hex(p.Attention.Fond), Hex(p.Attention.Texte) });
    }

    [Theory]
    [MemberData(nameof(Lectures))]
    public void Le_serveur_lit_les_fichiers_comme_la_console(string nom)
    {
        var cas = Cas("lectures", nom);
        var l = CharteVisuelle.Lire(cas.GetProperty("texte").GetString()!);
        static List<(string, string)> Codes(JsonElement e) =>
            [.. e.EnumerateArray().Select(x => (x.GetProperty("code").GetString()!, x.GetProperty("champ").GetString()!))];
        Assert.Equal(Codes(cas.GetProperty("erreurs")), l.Erreurs.Select(e => (e.Code, e.Champ)).ToList());
        Assert.Equal(Codes(cas.GetProperty("avertissements")), l.Avertissements.Select(e => (e.Code, e.Champ)).ToList());
        if (cas.GetProperty("charte").ValueKind == JsonValueKind.Null) Assert.Null(l.Charte);
        else
        {
            Assert.NotNull(l.Charte);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(cas.GetProperty("charte").GetRawText()), JsonNode.Parse(CharteVisuelle.SerialiserCompact(l.Charte))),
                CharteVisuelle.SerialiserCompact(l.Charte));
        }
    }

    [Fact]
    public void La_charte_par_defaut_ne_change_rien()
    {
        var d = CharteVisuelle.Defaut;
        Assert.Empty(CharteVisuelle.Ecarts(d, ModeCharte.Clair));
        Assert.Empty(CharteVisuelle.Ecarts(d, ModeCharte.Sombre));
        var (css, empreinte) = CharteVisuelle.FeuilleCss(d);
        Assert.DoesNotContain(":root:root", css, StringComparison.Ordinal);
        Assert.Contains($"--charte-empreinte:\"{empreinte}\"", css, StringComparison.Ordinal);
        Assert.Empty(CharteVisuelle.Avertissements(d));
        // Les classeurs gardent exactement les couleurs d'avant la charte.
        Assert.Equal(PaletteExcel.Defaut, CharteVisuelle.PaletteExcel(d));
    }

    /// <summary>Valeurs de index.css, résolues (var(--x)) : les tables du serveur doivent les reprendre à l'identique.</summary>
    private static Dictionary<string, string> JetonsIndexCss(string selecteur)
    {
        var css = File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "frontend", "src", "index.css"));
        var debut = css.IndexOf(selecteur + " {", StringComparison.Ordinal);
        var bloc = css[(debut + selecteur.Length + 2)..css.IndexOf('}', debut)];
        bloc = Regex.Replace(bloc, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var brut = Regex.Matches(bloc, @"--([a-z0-9-]+):\s*([^;]+);").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        return brut;
    }

    [Fact]
    public void Les_couleurs_par_defaut_du_serveur_sont_celles_de_index_css()
    {
        var clair = JetonsIndexCss(":root");
        var sombre = JetonsIndexCss(":root.dark");
        foreach (var (mode, propres) in new[] { (ModeCharte.Clair, clair), (ModeCharte.Sombre, sombre) })
        {
            string Resoudre(string cle)
            {
                var v = propres.GetValueOrDefault(cle) ?? clair[cle];
                return Regex.Match(v, @"^var\(--([a-z0-9-]+)\)$") is { Success: true } m ? Resoudre(m.Groups[1].Value) : v;
            }
            foreach (var (cle, valeur) in CharteVisuelle.JetonsDefaut(mode))
                Assert.True(Resoudre(cle) == valeur, $"{mode} --{cle} : index.css {Resoudre(cle)}, serveur {valeur}");
        }
    }

    [Fact]
    public void Le_fichier_accepte_les_codes_courts_sans_diese_et_signale_les_lignes()
    {
        var c = Lue("""{ "couleurs": { "primaire": "0f766e", "secondaire": "#123", "tertiaire": "PRIMAIRE" } }""");
        Assert.Equal("#0f766e", c.Primaire);
        Assert.Equal("#112233", c.Secondaire);
        Assert.True(c.TertiaireLiee);
        Assert.Equal(CharteVisuelle.NomDefaut, c.Nom);

        var l = CharteVisuelle.Lire("{\n  \"version\": 1,\n  \"couleurs\": { \"primaire\": \"#0F766E\" },\n  \"police\": \"Marianne\",\n  \"menuLateral\": \"sombre\"\n}");
        Assert.Null(l.Charte);
        var e = Assert.Single(l.Erreurs);
        Assert.Equal(("valeur", "menuLateral", 5), (e.Code, e.Champ, e.Ligne));
        Assert.EndsWith("(ligne 5).", e.Message, StringComparison.Ordinal);
        var a = Assert.Single(l.Avertissements);
        Assert.Equal(("cle-inconnue", "police", 4), (a.Code, a.Champ, a.Ligne));

        var json = CharteVisuelle.Lire("{\n  \"couleurs\": {\n    \"primaire\": \"#0F766E\",\n  }\n}");
        Assert.Equal("json", Assert.Single(json.Erreurs).Code);
        Assert.Contains("ligne", json.Erreurs[0].Message, StringComparison.Ordinal);

        Assert.Equal("longueur", Assert.Single(CharteVisuelle.Lire($$"""{ "nom": "{{new string('x', 81)}}", "couleurs": { "primaire": "#000" } }""").Erreurs).Code);
        Assert.Equal("obligatoire", Assert.Single(CharteVisuelle.Lire("""{ "couleurs": { } }""").Erreurs).Code);
    }

    [Fact]
    public void Le_fichier_exporte_se_relit_a_l_identique()
    {
        var c = Lue("""
            { "nom": "Groupe Horizon", "couleurs": { "primaire": "#0F766E", "secondaire": "#1B2A41", "tertiaire": "#C026D3" },
              "menuLateral": "secondaire", "gris": "teintes", "sombre": { "tertiaire": "#E879F9" }, "etats": { "succes": "#15803D" } }
            """);
        var texte = CharteVisuelle.Serialiser(c, "https://mep.exemple.fr/api/charte/schema.json");
        Assert.Contains("\"$schema\": \"https://mep.exemple.fr/api/charte/schema.json\"", texte, StringComparison.Ordinal);
        Assert.Contains("\"primaire\": \"#0F766E\"", texte, StringComparison.Ordinal);
        Assert.True(CharteVisuelle.Identiques(c, Lue(texte)));
        Assert.Equal("#e879f9", CharteVisuelle.CouleurRole(c, "tertiaire", ModeCharte.Sombre));
    }

    [Fact]
    public void La_console_l_emporte_sur_le_fichier_qui_l_emporte_sur_le_defaut()
    {
        var fichier = new FichierCharteLu("/etc/gestion-mep/charte.json", true, Lue("""{ "nom": "Fichier", "couleurs": { "primaire": "#1D4ED8" } }"""), [], []);
        var absent = new FichierCharteLu("/etc/gestion-mep/charte.json", false, null, [], []);
        var invalide = new FichierCharteLu("/etc/gestion-mep/charte.json", true, null, [new("couleur", "couleurs.primaire", "invalide")], []);
        var ligne = new Reglage
        {
            Cle = ServiceCharte.CleBase, Valeur = """{"nom":"Console","couleurs":{"primaire":"#0F766E"}}""",
            ModifieLe = DateTimeOffset.UtcNow, ModifiePar = "Alice",
        };

        var console = ServiceCharte.Resoudre(ligne, fichier);
        Assert.Equal(("console", "Console", "Alice"), (console.Origine, console.Charte.Nom, console.ModifiePar));
        Assert.Equal(("fichier", "Fichier"), (ServiceCharte.Resoudre(null, fichier).Origine, ServiceCharte.Resoudre(null, fichier).Charte.Nom));
        Assert.Equal("defaut", ServiceCharte.Resoudre(null, absent).Origine);
        var ignore = ServiceCharte.Resoudre(null, invalide);
        Assert.Equal("defaut", ignore.Origine);
        Assert.Equal("invalide", ignore.Fichier.Erreur);
        // Valeur en base abîmée à la main : ignorée, le fichier reprend la main.
        Assert.Equal("fichier", ServiceCharte.Resoudre(new Reglage { Cle = ServiceCharte.CleBase, Valeur = "{" }, fichier).Origine);
    }

    [Fact]
    public void La_feuille_ne_redefinit_que_les_jetons_qui_changent()
    {
        var c = Lue("""{ "couleurs": { "primaire": "#0F766E" } }""");
        var (css, empreinte) = CharteVisuelle.FeuilleCss(c);
        Assert.Contains(":root:root:not(.dark){--accent:#0f766e;", css, StringComparison.Ordinal);
        Assert.Contains(":root:root.dark{", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--bg:", css, StringComparison.Ordinal); // gris inchangés
        Assert.DoesNotContain("--nav-bg:", css, StringComparison.Ordinal); // menu comme les pages
        Assert.NotEqual(CharteVisuelle.FeuilleCss(CharteVisuelle.Defaut).Empreinte, empreinte);
        Assert.Equal(empreinte, CharteVisuelle.FeuilleCss(Lue("""{ "nom": "Autre nom", "couleurs": { "primaire": "#0f766e" } }""")).Empreinte);
    }

    [Fact]
    public void Les_exports_excel_prennent_les_couleurs_de_la_charte()
    {
        var p = CharteVisuelle.PaletteExcel(Lue("""{ "couleurs": { "primaire": "#0F766E", "secondaire": "#1B2A41", "tertiaire": "#C026D3" } }"""));
        Assert.Equal(("0F766E", "FFFFFF", "1B2A41"), (p.FondEnTete, p.TexteEnTete, p.TexteTitre));
        Assert.Equal("0F766E", p.TexteLien);
        // Primaire claire : texte foncé sur les en-têtes, liens dans la couleur des titres (texte d'accent, assombri pour être lisible).
        var pale = CharteVisuelle.PaletteExcel(Lue("""{ "couleurs": { "primaire": "#FACC15" } }"""));
        Assert.Equal(("FACC15", "16161A"), (pale.FondEnTete, pale.TexteEnTete));
        Assert.Equal(pale.TexteTitre, pale.TexteLien);
        Assert.True(CouleursCharte.Contraste("#" + pale.TexteLien, "#ffffff") >= 4.5);
        // Couleur d'état personnalisée : reprise dans les cellules d'état.
        var etats = CharteVisuelle.PaletteExcel(Lue("""{ "couleurs": { "primaire": "#4F46E5" }, "etats": { "erreur": "#9F1239" } }"""));
        Assert.NotEqual(PaletteExcel.Defaut.Echec, etats.Echec);
        Assert.Equal(PaletteExcel.Defaut.Succes, etats.Succes);
    }

    [Fact]
    public void Le_schema_publie_decrit_le_fichier()
    {
        var schema = JsonDocument.Parse(GestionMep.Api.Endpoints.CharteEndpoints.Schema).RootElement;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.GetProperty("$schema").GetString());
        var proprietes = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["$schema", "version", "nom", "couleurs", "menuLateral", "gris", "sombre", "etats"], proprietes);
        Assert.Equal(CharteVisuelle.MenusLateraux, schema.GetProperty("properties").GetProperty("menuLateral").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(CharteVisuelle.GrisConnus, schema.GetProperty("properties").GetProperty("gris").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(CharteVisuelle.VersionFichier, schema.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32());
    }

    [Fact]
    public void L_exemple_fourni_est_valide_et_lisible()
    {
        // installation/charte.exemple.json, cité par la documentation et joint aux archives sans Docker.
        var lu = CharteVisuelle.Lire(File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "installation", "charte.exemple.json")));
        Assert.Empty(lu.Erreurs);
        Assert.Empty(lu.Avertissements);
        Assert.Empty(CharteVisuelle.Avertissements(lu.Charte!));
        Assert.Equal("Groupe Horizon", lu.Charte!.Nom);
    }
}
