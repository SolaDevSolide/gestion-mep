using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GestionMep.Tests;

/// <summary>
/// Le document OpenAPI servi par l'API doit être identique à la copie versionnée <c>docs/openapi.json</c>, dont le front
/// génère ses types. Après un changement de contrat : <c>MAJ_OPENAPI=1 dotnet test --filter OpenApi</c>, puis
/// <c>npm run api:types</c> dans <c>frontend</c>.
/// </summary>
[Collection("Api")]
public class OpenApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static readonly JsonSerializerOptions Ecriture = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // garde les accents lisibles dans le fichier versionné
    };

    [Fact]
    public async Task Document_reserve_aux_utilisateurs_connectes()
    {
        var rep = await _api.Client().GetAsync("/api/openapi.json");
        Assert.Equal(HttpStatusCode.Unauthorized, rep.StatusCode);
    }

    [Fact]
    public async Task Document_identique_a_la_copie_versionnee()
    {
        var client = await _api.ClientConnecte("alice");
        var rep = await client.GetAsync("/api/openapi.json");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        var actuel = JsonNode.Parse(await rep.Content.ReadAsStringAsync())!.ToJsonString(Ecriture).ReplaceLineEndings("\n") + "\n";

        var fichier = Path.Combine(RacineDepot(), "docs", "openapi.json");
        if (Environment.GetEnvironmentVariable("MAJ_OPENAPI") == "1")
            await File.WriteAllTextAsync(fichier, actuel);

        Assert.True(File.Exists(fichier), "docs/openapi.json absent : lancer MAJ_OPENAPI=1 dotnet test --filter OpenApi.");
        var versionne = (await File.ReadAllTextAsync(fichier)).ReplaceLineEndings("\n");
        Assert.True(versionne == actuel,
            "docs/openapi.json n'est plus à jour : lancer MAJ_OPENAPI=1 dotnet test --filter OpenApi, puis npm run api:types dans frontend.");
    }

    private static string RacineDepot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "docs")) && Directory.Exists(Path.Combine(d.FullName, "backend")))
                return d.FullName;
        throw new InvalidOperationException("Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }
}
