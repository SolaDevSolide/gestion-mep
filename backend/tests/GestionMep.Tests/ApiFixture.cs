using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>Les classes de tests d'API partagent une base : elles s'exécutent l'une après l'autre.</summary>
[CollectionDefinition("Api", DisableParallelization = true)]
public class CollectionApi;

/// <summary>
/// Démarre l'API contre la base PostgreSQL <c>gestion_mep_tests</c>, recréée à chaque instanciation
/// (migration + données de démo chargées par l'API au démarrage).
/// </summary>
public class ApiFixture : IDisposable
{
    public const string Base = "gestion_mep_tests";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private readonly WebApplicationFactory<Program> _usine;

    /// <summary>Dossier temporaire des pièces jointes, supprimé à la fin.</summary>
    public string DossierPieces { get; } = Path.Combine(Path.GetTempPath(), "gestion-mep-tests-" + Guid.NewGuid().ToString("N"));

    public ApiFixture()
    {
        using (var cn = new NpgsqlConnection(Serveur + ";Database=postgres"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"drop database if exists {Base} with (force)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"create database {Base}";
            cmd.ExecuteNonQuery();
        }
        Environment.SetEnvironmentVariable("ConnectionStrings__Mep", $"{Serveur};Database={Base}");
        Environment.SetEnvironmentVariable("Demo__Activer", "true");
        Environment.SetEnvironmentVariable("Surveillance__Activer", "false"); // la surveillance d'arrière-plan ne doit pas interférer avec les tests
        Environment.SetEnvironmentVariable("Stockage__Dossier", DossierPieces);
        Environment.SetEnvironmentVariable("Cles__Dossier", Path.Combine(DossierPieces, "cles")); // /data/cles n'existe pas hors Docker
        Environment.SetEnvironmentVariable("Stockage__TailleMaxMo", "1");
        Environment.SetEnvironmentVariable("Connexion__TentativesParMinute", "1000"); // les tests se connectent sans cesse depuis la même « adresse »
        _usine = new WebApplicationFactory<Program>();
        _ = _usine.Server; // démarre l'hôte : migration + données de démo
    }

    /// <summary>Services de l'application (accès direct à la base, aux services d'arrière-plan).</summary>
    public IServiceProvider Services => _usine.Services;

    /// <summary>Client HTTP avec l'en-tête anti-CSRF, comme le front.</summary>
    public HttpClient Client()
    {
        var client = _usine.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        return client;
    }

    /// <summary>Client HTTP sans l'en-tête anti-CSRF (requête forgée depuis un autre site).</summary>
    public HttpClient ClientSansEnTete() => _usine.CreateClient();

    public async Task<HttpClient> ClientConnecte(string login, string motDePasse = "demo")
    {
        var client = Client();
        var rep = await client.PostAsJsonAsync("/api/auth/connexion", new { login, motDePasse });
        rep.EnsureSuccessStatusCode();
        return client;
    }

    public void Dispose()
    {
        _usine.Dispose();
        NpgsqlConnection.ClearAllPools();
        if (Directory.Exists(DossierPieces)) Directory.Delete(DossierPieces, recursive: true);
    }
}
