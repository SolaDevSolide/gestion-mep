using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionMep.Api.Securite;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestionMep.Tests;

/// <summary>En-têtes de sécurité (CSP, cadres, type de contenu) et taille maximale des requêtes.</summary>
[Collection("Api")]
public class SecuriteHttpTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    [Fact]
    public async Task Toute_reponse_porte_les_en_tetes_de_securite()
    {
        var rep = await _api.Client().GetAsync("/api/sante");
        var csp = rep.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", rep.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", rep.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", rep.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public void Script_en_ligne_d_index_html_autorise_par_son_empreinte()
    {
        var dossier = Directory.CreateTempSubdirectory("gestion-mep-csp-");
        try
        {
            const string script = "\n  document.documentElement.classList.add('dark');\n";
            var index = Path.Combine(dossier.FullName, "index.html");
            File.WriteAllText(index, $"<html><head><script>{script}</script><script type=\"module\" src=\"/assets/app.js\"></script></head></html>");
            var empreinte = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

            var csp = EnTetesSecurite.PolitiqueContenu(index);

            Assert.Contains($"script-src 'self' 'sha256-{empreinte}';", csp, StringComparison.Ordinal);
            Assert.Equal("script-src 'self';", EnTetesSecurite.PolitiqueContenu(null).Split("; ")[1] + ";");
        }
        finally
        {
            dossier.Delete(true);
        }
    }

    [Fact]
    public void Index_html_en_crlf_garde_l_empreinte_du_script_tel_que_le_navigateur_le_lit()
    {
        // Dépôt extrait sous Windows (core.autocrlf) : le navigateur ramène CRLF et CR seuls à LF avant de calculer
        // l'empreinte du script ; le serveur doit faire de même, sinon le script de thème est bloqué.
        var dossier = Directory.CreateTempSubdirectory("gestion-mep-csp-");
        try
        {
            const string scriptLf = "\n  const t = 1;\n  document.documentElement.classList.add('dark');\n";
            var index = Path.Combine(dossier.FullName, "index.html");
            var lf = $"<html>\n<head>\n<script>{scriptLf}</script>\n</head>\n</html>\n";
            File.WriteAllText(index, lf.Replace("\n", "\r\n", StringComparison.Ordinal));
            var empreinte = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scriptLf)));

            Assert.Contains($"script-src 'self' 'sha256-{empreinte}';", EnTetesSecurite.PolitiqueContenu(index), StringComparison.Ordinal);

            // Retour chariot seul : même lecture.
            File.WriteAllText(index, lf.Replace("\n", "\r", StringComparison.Ordinal));
            Assert.Contains($"'sha256-{empreinte}'", EnTetesSecurite.PolitiqueContenu(index), StringComparison.Ordinal);
        }
        finally
        {
            dossier.Delete(true);
        }
    }

    [Fact]
    public async Task Corps_json_de_plus_d_un_mo_refuse_en_413()
    {
        // La limite est appliquée par Kestrel : le serveur de test en mémoire l'ignore, on démarre donc un vrai serveur
        // (même base et même configuration que la fixture).
        using var usine = new WebApplicationFactory<Program>();
        usine.UseKestrel(0);
        usine.StartServer();
        var alice = usine.CreateClient();
        alice.DefaultRequestHeaders.Add(ProtectionsHttp.EnTeteCsrf, "fetch");
        (await alice.PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = "demo" })).EnsureSuccessStatusCode();
        var id = await AidesApi.IdDemo(alice);

        var rep = await alice.PutAsJsonAsync($"/api/meps/{id}", new { objet = new string('x', (int)LimitesRequetes.CorpsParDefaut + 1) });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rep.StatusCode);
        Assert.Equal("Requête trop volumineuse.", await Message(rep));
    }

    [Fact]
    public async Task Depot_d_un_fichier_au_dela_d_un_mo_reste_accepte()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        using var form = new MultipartFormDataContent();
        // 1 Mo exactement : la taille maximale des pièces jointes dans les tests ; le corps dépasse donc 1 Mo.
        form.Add(new ByteArrayContent(new byte[1024 * 1024]), "fichier", "limite.bin");
        var rep = await alice.PostAsync($"/api/meps/{id}/pieces-jointes", form);
        Assert.True(rep.IsSuccessStatusCode, $"{(int)rep.StatusCode} {await rep.Content.ReadAsStringAsync()}");
    }
}
