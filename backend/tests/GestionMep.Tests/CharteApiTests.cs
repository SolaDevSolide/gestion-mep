using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using GestionMep.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Charte visuelle par l'API : lecture publique, feuille /charte.css et empreinte de /api/version, réglage réservé aux
/// administrateurs et journalisé, fichier charte.json remplacé par la console puis rétabli, exports Excel aux couleurs.
/// </summary>
[Collection("Api")]
public class CharteApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static readonly object Horizon = new
    {
        version = 1,
        nom = "Groupe Horizon",
        couleurs = new { primaire = "#0F766E", secondaire = "#1B2A41", tertiaire = "#C026D3" },
        menuLateral = "secondaire",
        gris = "teintes",
    };

    private Task Recharger() => _api.Services.GetRequiredService<ServiceCharte>().RechargerAsync();

    private static async Task<(string Css, string? ETag)> Feuille(HttpClient c)
    {
        var rep = await c.GetAsync("/charte.css");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        Assert.Equal("text/css", rep.Content.Headers.ContentType?.MediaType);
        return (await rep.Content.ReadAsStringAsync(), rep.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task La_charte_par_defaut_est_publique_et_ne_change_aucune_couleur()
    {
        var anonyme = _api.Client();
        var charte = await Ok(await anonyme.GetAsync("/api/charte"));
        Assert.Equal("defaut", charte.GetProperty("origine").GetString());
        Assert.Equal("#4F46E5", charte.GetProperty("charte").GetProperty("couleurs").GetProperty("primaire").GetString());

        var (css, etag) = await Feuille(anonyme);
        Assert.DoesNotContain(":root:root", css, StringComparison.Ordinal);
        var empreinte = charte.GetProperty("empreinte").GetString();
        Assert.Equal($"\"{empreinte}\"", etag);
        Assert.Equal(empreinte, (await Ok(await anonyme.GetAsync("/api/version"))).GetProperty("charte").GetString());

        // Revalidation : 304 tant que la charte ne change pas.
        using var requete = new HttpRequestMessage(HttpMethod.Get, "/charte.css");
        requete.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!));
        var rep = await anonyme.SendAsync(requete);
        Assert.Equal(HttpStatusCode.NotModified, rep.StatusCode);
        Assert.Equal("no-cache", rep.Headers.CacheControl?.ToString());

        var schema = await anonyme.GetAsync("/api/charte/schema.json");
        Assert.Equal("application/schema+json", schema.Content.Headers.ContentType?.MediaType);
        Assert.Contains("menuLateral", await schema.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seul_un_administrateur_regle_la_charte()
    {
        var anonyme = _api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.GetAsync("/api/admin/charte")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.PutAsJsonAsync("/api/admin/charte", Horizon)).StatusCode);
        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/charte")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync("/api/admin/charte", Horizon)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync("/api/admin/charte")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/charte/verifier", new { contenu = "{}" })).StatusCode);
        // Lecture publique : la page de connexion est aux couleurs de la charte.
        await Ok(await bob.GetAsync("/api/charte"));
    }

    [Fact]
    public async Task La_charte_enregistree_s_applique_tout_de_suite_et_est_journalisee()
    {
        var alice = await _api.ClientConnecte("alice");
        var (_, etagAvant) = await Feuille(alice);

        var invalide = await alice.PutAsJsonAsync("/api/admin/charte", new { couleurs = new { primaire = "bleu" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalide.StatusCode);
        Assert.Contains("couleurs.primaire", await invalide.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var etat = await Ok(await alice.PutAsJsonAsync("/api/admin/charte", Horizon));
        Assert.Equal("console", etat.GetProperty("origine").GetString());
        Assert.Equal("Alice", etat.GetProperty("modifiePar").GetString());
        Assert.Equal("Groupe Horizon", etat.GetProperty("charte").GetProperty("nom").GetString());
        Assert.Equal(0, etat.GetProperty("avertissements").GetArrayLength());

        // Feuille et empreinte changent dès la requête suivante (les onglets ouverts la comparent à /api/version).
        var (css, etag) = await Feuille(_api.Client());
        Assert.NotEqual(etagAvant, etag);
        Assert.Contains("--accent:#0f766e;", css, StringComparison.Ordinal);
        Assert.Contains("--nav-bg:#1b2a41;", css, StringComparison.Ordinal);
        Assert.Contains("--tertiaire:#c026d3;", css, StringComparison.Ordinal);
        Assert.Equal(etag, $"\"{(await Ok(await alice.GetAsync("/api/version"))).GetProperty("charte").GetString()}\"");

        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "charte.modifiee"
            && e.GetProperty("detail").GetString()!.Contains("primaire #4F46E5 → #0F766E", StringComparison.Ordinal)
            && e.GetProperty("detail").GetString()!.Contains("menu latéral pages → secondaire", StringComparison.Ordinal));

        var retablie = await Ok(await alice.DeleteAsync("/api/admin/charte"));
        Assert.Equal("defaut", retablie.GetProperty("origine").GetString());
        Assert.Equal(etagAvant, (await Feuille(alice)).ETag);
        journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "charte.retablie");
    }

    [Fact]
    public async Task Le_fichier_donne_la_charte_de_depart_et_la_console_le_remplace()
    {
        var alice = await _api.ClientConnecte("alice");
        await File.WriteAllTextAsync(_api.FichierCharte, """
            {
              "nom": "Charte du fichier",
              "couleurs": { "primaire": "#1D4ED8" },
              "police": "Marianne"
            }
            """);
        await Recharger();
        var etat = await Ok(await alice.GetAsync("/api/admin/charte"));
        Assert.Equal("fichier", etat.GetProperty("origine").GetString());
        Assert.Equal("Charte du fichier", etat.GetProperty("charte").GetProperty("nom").GetString());
        var avertissement = Assert.Single(etat.GetProperty("fichier").GetProperty("avertissements").EnumerateArray());
        Assert.Equal(4, avertissement.GetProperty("ligne").GetInt32());

        await Ok(await alice.PutAsJsonAsync("/api/admin/charte", Horizon));
        etat = await Ok(await alice.GetAsync("/api/admin/charte"));
        Assert.Equal("console", etat.GetProperty("origine").GetString());
        Assert.Equal("Charte du fichier", etat.GetProperty("fichier").GetProperty("charte").GetProperty("nom").GetString());

        // « Charte par défaut » efface la charte de la console : le fichier reprend la main.
        etat = await Ok(await alice.DeleteAsync("/api/admin/charte"));
        Assert.Equal("fichier", etat.GetProperty("origine").GetString());

        // Fichier invalide : ignoré, charte par défaut, erreur visible dans la console ; jamais bloquant.
        await File.WriteAllTextAsync(_api.FichierCharte, "{ \"couleurs\": { \"primaire\": \"#0F766E\" }, \"menuLateral\": \"sombre\" }");
        await Recharger();
        etat = await Ok(await alice.GetAsync("/api/admin/charte"));
        Assert.Equal("defaut", etat.GetProperty("origine").GetString());
        Assert.Contains("menuLateral", etat.GetProperty("fichier").GetProperty("erreur").GetString(), StringComparison.Ordinal);
        Assert.Equal("defaut", (await Ok(await _api.Client().GetAsync("/api/charte"))).GetProperty("origine").GetString());
    }

    [Fact]
    public async Task Un_fichier_se_verifie_sans_rien_enregistrer()
    {
        var alice = await _api.ClientConnecte("alice");
        var ok = await Ok(await alice.PostAsJsonAsync("/api/admin/charte/verifier",
            new { contenu = "{\n  \"couleurs\": { \"primaire\": \"#2F6FE0\", \"tertiaire\": \"#F0A6F5\" },\n  \"police\": 1\n}" }));
        Assert.True(ok.GetProperty("valide").GetBoolean());
        var codes = ok.GetProperty("avertissements").EnumerateArray().Select(a => a.GetProperty("code").GetString()).ToList();
        Assert.Contains("cle-inconnue", codes);
        Assert.Contains("contraste", codes); // cadre des sections trop pâle
        Assert.Contains("etat", codes); // primaire proche du bleu « En cours »

        var ko = await Ok(await alice.PostAsJsonAsync("/api/admin/charte/verifier", new { contenu = "{\n  \"version\": 2\n}" }));
        Assert.False(ko.GetProperty("valide").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ko.GetProperty("charte").ValueKind);
        Assert.Contains(ko.GetProperty("erreurs").EnumerateArray(), e => e.GetProperty("code").GetString() == "version" && e.GetProperty("ligne").GetInt32() == 2);
        Assert.Equal("defaut", (await Ok(await alice.GetAsync("/api/admin/charte"))).GetProperty("origine").GetString());
    }

    [Fact]
    public async Task Les_exports_excel_suivent_la_charte()
    {
        var alice = await _api.ClientConnecte("alice");
        async Task<IXLWorksheet> Liste()
        {
            var rep = await alice.GetAsync("/api/meps/excel");
            Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
            return new XLWorkbook(new MemoryStream(await rep.Content.ReadAsByteArrayAsync())).Worksheet(1);
        }
        var avant = await Liste();
        Assert.Equal("FF4F46E5", avant.Cell(4, 1).Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8"));

        await Ok(await alice.PutAsJsonAsync("/api/admin/charte", Horizon));
        var f = await Liste();
        Assert.Equal("FF0F766E", f.Cell(4, 1).Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")); // en-têtes en primaire
        Assert.Equal("FFFFFFFF", f.Cell(4, 1).Style.Font.FontColor.Color.ToArgb().ToString("X8"));
        Assert.Equal("FF1B2A41", f.Cell(1, 1).Style.Font.FontColor.Color.ToArgb().ToString("X8")); // titre en secondaire
    }
}
