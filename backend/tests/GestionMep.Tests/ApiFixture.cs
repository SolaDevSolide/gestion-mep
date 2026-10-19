using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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

    private readonly WebApplicationFactory<Program> _racine;
    private WebApplicationFactory<Program> _usine;
    private readonly bool _demo;

    /// <summary>Dossier temporaire des pièces jointes, supprimé à la fin.</summary>
    public string DossierPieces { get; } = Path.Combine(Path.GetTempPath(), "gestion-mep-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>Dossier d'échange avec le service de mise à jour (etat.json, demande.json).</summary>
    public string DossierMiseAJour => Path.Combine(DossierPieces, "mise-a-jour");
    /// <summary>Fichier charte.json lu par l'application de test (absent tant qu'un test ne l'écrit pas).</summary>
    public string FichierCharte => Path.Combine(DossierPieces, "charte.json");

    /// <param name="demo">false : base neuve hors mode démonstration (aucun compte, routes /api/demo absentes).</param>
    /// <param name="environnement">Environnement d'hébergement (par défaut celui de la fabrique de test : Development).</param>
    /// <param name="hote">Réglage supplémentaire de l'hôte (services de test, intergiciels d'observation).</param>
    public ApiFixture(bool demo = true, string? environnement = null, Action<IWebHostBuilder>? hote = null)
    {
        _demo = demo;
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
        Environment.SetEnvironmentVariable("Demo__Activer", demo ? "true" : "false");
        Environment.SetEnvironmentVariable("Surveillance__Activer", "false"); // la surveillance d'arrière-plan ne doit pas interférer avec les tests
        Environment.SetEnvironmentVariable("Stockage__Dossier", DossierPieces);
        Environment.SetEnvironmentVariable("Cles__Dossier", Path.Combine(DossierPieces, "cles")); // /data/cles n'existe pas hors Docker
        Environment.SetEnvironmentVariable("Sauvegardes__Dossier", Path.Combine(DossierPieces, "sauvegardes"));
        Environment.SetEnvironmentVariable("Stockage__TailleMaxMo", "1");
        Environment.SetEnvironmentVariable("MisesAJour__Verifier", "false"); // pas d'appel à GitHub pendant les tests
        Environment.SetEnvironmentVariable("MisesAJour__Dossier", DossierMiseAJour);
        Environment.SetEnvironmentVariable("Connexion__TentativesParMinute", "1000"); // les tests se connectent sans cesse depuis la même « adresse »
        // Fichier de charte visuelle propre au test (absent par défaut) : jamais celui de la machine (/etc/gestion-mep/charte.json).
        Environment.SetEnvironmentVariable("Charte__Fichier", FichierCharte);
        _usine = _racine = new WebApplicationFactory<Program>();
        // L'usine dérivée est libérée avec celle d'origine.
        if (environnement is not null) _usine = _usine.WithWebHostBuilder(b => b.UseEnvironment(environnement));
        if (hote is not null) _usine = _usine.WithWebHostBuilder(hote);
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

    /// <summary>Client qui ne suit pas les redirections (pour lire l'en-tête Location), avec l'en-tête anti-CSRF.</summary>
    public HttpClient ClientSansRedirection()
    {
        var client = _usine.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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

    /// <summary>
    /// Nouveau compte créé comme dans la console d'administration (mot de passe provisoire, par Alice), puis mot de passe
    /// choisi par la personne : renvoie son client connecté, prêt à travailler.
    /// </summary>
    public async Task<HttpClient> NouveauCompte(string login, string nomAffiche, bool estAdmin = false, string motDePasse = "motdepasse",
        string role = "", string contact = "")
    {
        var admin = await ClientConnecte("alice");
        var creation = await admin.PostAsJsonAsync("/api/admin/utilisateurs", new { login, nomAffiche, estAdmin, role, contact });
        var corps = await creation.Content.ReadAsStringAsync();
        if (!creation.IsSuccessStatusCode) throw new InvalidOperationException($"Création du compte {login} : {(int)creation.StatusCode} {corps}");
        var provisoire = System.Text.Json.JsonDocument.Parse(corps).RootElement.GetProperty("motDePasseProvisoire").GetString()!;
        var client = await ClientConnecte(login, provisoire);
        (await client.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = provisoire, nouveau = motDePasse })).EnsureSuccessStatusCode();
        return client;
    }

    public void Dispose()
    {
        _racine.Dispose();
        // Les autres tests (et ceux qui démarrent leur propre application) supposent la démonstration active.
        if (!_demo) Environment.SetEnvironmentVariable("Demo__Activer", "true");
        NpgsqlConnection.ClearAllPools();
        if (Directory.Exists(DossierPieces)) Directory.Delete(DossierPieces, recursive: true);
    }
}
