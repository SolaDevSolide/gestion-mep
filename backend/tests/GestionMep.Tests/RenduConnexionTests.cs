using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Connexions aux API externes (#111), règles pures : rendu des gabarits (mêmes cas que le front,
/// <c>frontend/src/vecteurs-gabarits.json</c>), modèles de départ, déclencheurs, requête rendue et forme lisible.
/// </summary>
public class RenduConnexionTests
{
    private static readonly JsonElement Vecteurs = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "frontend", "src", "vecteurs-gabarits.json"))).RootElement;

    public static TheoryData<string> Cas() => [.. Vecteurs.GetProperty("cas").EnumerateArray().Select(c => c.GetProperty("nom").GetString()!)];

    private static List<string> Textes(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetString()!)];

    [Fact]
    public void Les_variables_des_vecteurs_sont_celles_du_catalogue()
    {
        foreach (var v in Vecteurs.GetProperty("variables").EnumerateObject())
        {
            var variable = VariablesEvenement.Trouver(v.Name);
            Assert.True(variable is not null, $"{v.Name} n'existe pas");
            Assert.Equal(variable.Liste, v.Value.GetBoolean());
        }
    }

    [Theory]
    [MemberData(nameof(Cas))]
    public void Rendu_identique_au_front(string nom)
    {
        var cas = Vecteurs.GetProperty("cas").EnumerateArray().Single(c => c.GetProperty("nom").GetString() == nom);
        var valeurs = cas.GetProperty("valeurs").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Array
            ? new ValeurVariable(null, Textes(p.Value)) : new ValeurVariable(p.Value.GetString(), null));
        var gabarit = cas.GetProperty("gabarit").GetString()!;
        var rendu = cas.GetProperty("format").GetString() switch
        {
            "json" => RenduConnexion.Json(gabarit, valeurs),
            "url" => RenduConnexion.Url(gabarit, valeurs),
            _ => RenduConnexion.Texte(gabarit, valeurs),
        };
        var attendu = cas.GetProperty("attendu");
        Assert.Equal(attendu.GetProperty("texte").GetString(), rendu.Texte);
        Assert.Equal(Textes(attendu.GetProperty("manquantes")), rendu.Manquantes);
        Assert.Equal(Textes(attendu.GetProperty("inconnues")), rendu.Inconnues);
        Assert.Equal(
            [.. attendu.GetProperty("erreurs").EnumerateArray().Select(e => new ErreurGabarit(e.GetProperty("ligne").GetInt32(), e.GetProperty("colonne").GetInt32(), e.GetProperty("message").GetString()!))],
            rendu.Erreurs);
    }

    [Fact]
    public void Un_message_valide_l_est_aussi_pour_le_serveur_JSON()
    {
        // Le rendu échappe ce qu'il faut : le texte produit se lit avec le lecteur JSON standard, valeurs comprises.
        var valeurs = new Dictionary<string, ValeurVariable>
        {
            ["evenement_detail"] = new("guillemets \" barre \\ tab \t retour \r\n fin \u0001 ☃", null),
            ["etape_systemes"] = new(null, ["A \"B\"", "C\\D"]),
        };
        var rendu = RenduConnexion.Json("""{"d": "{{evenement_detail}}", "s": {{etape_systemes}}, "j": "{{etape_systemes}}"}""", valeurs);
        Assert.True(rendu.Valide, string.Join(" ; ", rendu.Erreurs));
        using var doc = JsonDocument.Parse(rendu.Texte);
        Assert.Equal(valeurs["evenement_detail"].Texte, doc.RootElement.GetProperty("d").GetString());
        Assert.Equal(["A \"B\"", "C\\D"], doc.RootElement.GetProperty("s").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("A \"B\", C\\D", doc.RootElement.GetProperty("j").GetString());
    }

    [Fact]
    public void Le_validateur_suit_le_lecteur_JSON_standard()
    {
        string[] valides = ["{}", "[]", "[1, -0.5, 2e10, 3E-2]", """{"a": {"b": [true, false, null, "\u00e9\n"]}}""", " \n\"seul\" "];
        string[] invalides = ["{", "[1,]", "{\"a\":1,}", "01", "1.", "-", "\"a", "{'a': 1}", "tru", "[1 2]", "{\"a\" 1}", "\"\\x\"", "{} []"];
        foreach (var t in valides)
        {
            Assert.Null(ValidateurJson.Valider(t));
            using var _ = JsonDocument.Parse(t);
        }
        foreach (var t in invalides)
        {
            Assert.NotNull(ValidateurJson.Valider(t));
            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(t));
        }
    }

    [Fact]
    public void Les_modeles_de_depart_donnent_un_message_valide()
    {
        var exemple = VariablesEvenement.ValeursExemple();
        foreach (var m in ModelesConnexion.Tous)
        {
            if (m.Format == FormatCorps.Json && m.Corps.Length > 0)
            {
                var r = RenduConnexion.Json(m.Corps, exemple);
                Assert.True(r.Valide, $"{m.Libelle} : {string.Join(" ; ", r.Erreurs)} {string.Join(", ", r.Inconnues)}");
            }
            Assert.All(m.Declencheurs, d => Assert.NotNull(DeclencheursConnexion.Trouver(d)));
        }
        Assert.Equal(Enum.GetValues<ModeleConnexion>().Length, ModelesConnexion.Tous.Count);
    }

    [Fact]
    public void Chaque_variable_d_evenement_est_documentee()
    {
        var doc = File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "docs", "Modèles-de-texte.md"));
        Assert.Contains($"### {VariablesEvenement.Groupe}", doc);
        Assert.All(VariablesEvenement.Toutes, v => Assert.Contains($"`{{{{{v.Cle}}}}}`", doc));
        var api = File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "docs", "API-externes.md"));
        Assert.All(DeclencheursConnexion.Tous, d => Assert.Contains(d.Libelle, api));
    }

    [Fact]
    public void Declencheurs_d_un_evenement_du_journal()
    {
        Assert.Equal("pilotage", DeclencheursConnexion.DeEvenement("pilotage.secours")?.Cle);
        Assert.Equal(DeclencheursConnexion.RecetteNonConforme, DeclencheursConnexion.DeEvenement("recette.test", DeclencheursConnexion.RecetteNonConforme)?.Cle);
        Assert.Null(DeclencheursConnexion.DeEvenement("recette.test"));
        Assert.Null(DeclencheursConnexion.DeEvenement("etape.modifiee"));
        Assert.Equal("Point d'étape", DeclencheursConnexion.Libelle(DeclencheursConnexion.EnvoiManuel));
        Assert.All(DeclencheursConnexion.Tous.Where(d => d.Frequent), d => Assert.StartsWith("etape.", d.Cle));
    }

    private static ConnexionApi Connexion() => new()
    {
        Nom = "Supervision",
        Url = "https://outil.example.com/hook/{{mep_id}}?e={{evenement_cle}}",
        Format = FormatCorps.Json,
        Corps = """{"titre": "{{essai}}{{evenement}}", "systemes": {{etape_systemes}}}""",
        Entetes = [new EnteteConnexion { Nom = "X-Source", Valeur = "gestion-mep/{{application}}\r\nX-Injecte: 1" }],
    };

    [Fact]
    public void Requete_rendue_adresse_message_et_en_tetes()
    {
        var valeurs = new Dictionary<string, ValeurVariable>
        {
            ["mep_id"] = new("a b", null),
            ["evenement_cle"] = new("etape.echec", null),
            ["evenement"] = new("Étape en échec", null),
            ["application"] = new("Paiement", null),
        };
        var r = RequeteConnexion.Rendre(Connexion(), valeurs);
        Assert.True(r.Valide, string.Join(" ; ", r.Erreurs.Select(e => e.Message)));
        Assert.Equal("https://outil.example.com/hook/a%20b?e=etape.echec", r.Url);
        Assert.Equal("""{"titre": "Étape en échec", "systemes": []}""", r.Corps);
        Assert.Equal(["essai", "etape_systemes"], r.Manquantes);
        // Pas de retour à la ligne dans un en-tête : rien ne peut s'y glisser.
        Assert.DoesNotContain(r.Entetes, e => e.Valeur.Contains('\n') || e.Valeur.Contains('\r'));
        Assert.Equal("application/json; charset=utf-8", r.TypeContenu);
    }

    [Fact]
    public void Requete_invalide_erreur_par_champ()
    {
        var c = Connexion();
        c.Url = "https://{{application}}.example.com/x";
        c.Corps = """{"a": {{evenement}}}""";
        c.Entetes = [new EnteteConnexion { Nom = "X-Source", Valeur = "{{pilot}}" }];
        var r = RequeteConnexion.Rendre(c, new Dictionary<string, ValeurVariable>());
        Assert.False(r.Valide);
        Assert.Contains(r.Erreurs, e => e.Champ == "adresse");
        Assert.Contains(r.Erreurs, e => e.Champ == "corps" && e.Ligne == 1 && e.Colonne == 7);
        Assert.Contains("pilot", r.Inconnues);
    }

    [Fact]
    public void Noms_d_en_tetes_reserves_ou_invalides_refuses()
    {
        Assert.NotNull(RequeteConnexion.RefusNomEntete("X-Gestion-Mep-Appel", AuthentificationApi.Aucune));
        Assert.NotNull(RequeteConnexion.RefusNomEntete("Authorization", AuthentificationApi.Jeton));
        Assert.Null(RequeteConnexion.RefusNomEntete("Authorization", AuthentificationApi.Aucune));
        Assert.NotNull(RequeteConnexion.RefusNomEntete("Host", AuthentificationApi.Aucune));
        Assert.NotNull(RequeteConnexion.RefusNomEntete("Mon en-tête", AuthentificationApi.Aucune));
        Assert.Null(RequeteConnexion.RefusNomEntete("X-Source", AuthentificationApi.Aucune));
    }

    [Fact]
    public void Origine_des_secrets()
    {
        Assert.Equal("https://outil.example.com", RequeteConnexion.Origine("HTTPS://Outil.Example.com/a/b?c"));
        Assert.Equal("https://outil.example.com:8443", RequeteConnexion.Origine("https://outil.example.com:8443/a"));
        Assert.Equal("https://api.example.com https://login.example.com",
            RequeteConnexion.OrigineSecrets("https://api.example.com/x", AuthentificationApi.OAuth2, "https://login.example.com/token"));
        Assert.Equal("https://hooks.example.com/•••", RequeteConnexion.UrlAffichee("https://hooks.example.com/secret/123", true));
    }

    [Fact]
    public void Forme_lisible_d_une_carte_Teams()
    {
        var teams = ModelesConnexion.Trouver(ModeleConnexion.Teams);
        var valeurs = VariablesEvenement.ValeursExemple();
        valeurs["pilote"] = new("Alice", null);
        var rendu = RenduConnexion.Json(teams.Corps, valeurs);
        Assert.True(rendu.Valide);
        var lisible = RequeteConnexion.Lisible(FormatCorps.Json, rendu.Texte);
        Assert.Contains("Pilote : Alice", lisible);
        Assert.DoesNotContain("AdaptiveCard", lisible);
        Assert.DoesNotContain("$schema", lisible);
        Assert.Equal("a : 1 2", RequeteConnexion.Lisible(FormatCorps.Formulaire, "a=1%202"));
        Assert.DoesNotContain("wrap", lisible);
    }
}
