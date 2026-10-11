using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace GestionMep.Api.Services;

/// <summary>Version de l'application en cours d'exécution, fixée à la construction de l'image (argument VERSION).</summary>
public static class VersionApplication
{
    /// <summary>Version lisible, sans le suffixe de compilation (« 1.2.0 », ou « 0.0.0-dev » hors version publiée).</summary>
    public static string Courante { get; } = Lire();

    private static string Lire()
    {
        var a = typeof(VersionApplication).Assembly;
        var v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString() ?? "0.0.0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    /// <summary>
    /// Partie numérique d'un numéro de version (« v1.2.0 » → 1.2.0) ; null si ce n'est pas une version publiée
    /// (suffixe « -dev », « -rc1 »… ou texte libre).
    /// </summary>
    public static Version? Publiee(string? texte)
    {
        var t = (texte ?? "").Trim().TrimStart('v', 'V');
        return t.Contains('-') || !Version.TryParse(t, out var v) ? null : v;
    }

    /// <summary>Vrai si <paramref name="derniere"/> est une version publiée plus récente que <paramref name="courante"/>.</summary>
    public static bool EstPlusRecente(string? derniere, string courante) =>
        Publiee(derniere) is { } d && Publiee(courante) is { } c && d > c;
}

/// <summary>Dernière version publiée connue, lue sur les « releases » GitHub du dépôt.</summary>
public record EtatMiseAJour(
    bool Active,
    string Depot,
    string? DerniereVersion,
    DateTimeOffset? PublieeLe,
    string? NotesUrl,
    DateTimeOffset? VerifieeLe,
    string? Erreur);

/// <summary>
/// Vérifie régulièrement s'il existe une version plus récente de l'application (configuration <c>MisesAJour</c>) :
/// <c>Verifier</c> (défaut true), <c>Depot</c> (défaut SolaDevSolide/gestion-mep), <c>IntervalleHeures</c> (défaut 12).
/// Seule la page publique des versions du dépôt est lue : aucune donnée de l'application n'est envoyée. Sans accès à
/// Internet, l'erreur est simplement affichée dans la console d'administration.
/// </summary>
public class VerificationMisesAJour(IConfiguration config, IHttpClientFactory http, TimeProvider temps, ILogger<VerificationMisesAJour> journal)
    : BackgroundService
{
    public const string DepotParDefaut = "SolaDevSolide/gestion-mep";

    private readonly bool _active = config.GetValue("MisesAJour:Verifier", true);
    private readonly string _depot = string.IsNullOrWhiteSpace(config["MisesAJour:Depot"]) ? DepotParDefaut : config["MisesAJour:Depot"]!.Trim();
    private readonly TimeSpan _intervalle = TimeSpan.FromHours(Math.Max(1, config.GetValue("MisesAJour:IntervalleHeures", 12)));
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private EtatMiseAJour? _etat;

    public EtatMiseAJour Etat => _etat ?? new EtatMiseAJour(_active, _depot, null, null, null, null, null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_active) return;
        using var minuterie = new PeriodicTimer(_intervalle, temps);
        do await Verifier(stoppingToken);
        while (await minuterie.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Interroge GitHub maintenant (aussi appelé par le bouton « Vérifier » de la console).</summary>
    public async Task<EtatMiseAJour> Verifier(CancellationToken ct = default)
    {
        if (!_active) return Etat;
        await _verrou.WaitAsync(ct);
        try
        {
            using var client = http.CreateClient(nameof(VerificationMisesAJour));
            using var requete = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_depot}/releases/latest");
            requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("gestion-mep", VersionApplication.Courante.Replace('+', '-')));
            requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var reponse = await client.SendAsync(requete, ct);
            if (!reponse.IsSuccessStatusCode)
            {
                var message = reponse.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? $"Aucune version publiée trouvée sur {_depot}."
                    : $"GitHub a répondu {(int)reponse.StatusCode}.";
                return _etat = Etat with { VerifieeLe = temps.GetUtcNow(), Erreur = message };
            }
            using var json = JsonDocument.Parse(await reponse.Content.ReadAsStreamAsync(ct));
            var racine = json.RootElement;
            string? Texte(string nom) => racine.TryGetProperty(nom, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            DateTimeOffset? date = DateTimeOffset.TryParse(Texte("published_at"), out var d) ? d : null;
            _etat = new EtatMiseAJour(true, _depot, Texte("tag_name")?.TrimStart('v', 'V'), date, Texte("html_url"), temps.GetUtcNow(), null);
            return _etat;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            journal.LogInformation("Vérification des mises à jour impossible : {Message}", ex.Message);
            return _etat = Etat with { VerifieeLe = temps.GetUtcNow(), Erreur = "GitHub est injoignable depuis le serveur." };
        }
        finally
        {
            _verrou.Release();
        }
    }
}

/// <summary>État du service « mise-a-jour » (conteneur facultatif) tel qu'il l'écrit dans le dossier d'échange.</summary>
public record EtatInstallateur(bool Present, string? Statut, string? Version, string? Message, DateTimeOffset? Le);

/// <summary>
/// Dialogue avec le service « mise-a-jour » par le dossier partagé <c>MisesAJour:Dossier</c> (défaut
/// <c>/data/mise-a-jour</c>) : il y écrit <c>etat.json</c> toutes les 5 secondes ; l'application y dépose
/// <c>demande.json</c> avec le seul numéro de version à installer. Aucun port n'est ouvert entre les deux.
/// </summary>
public class InstallationMiseAJour(IConfiguration config, TimeProvider temps)
{
    public const string DossierParDefaut = "/data/mise-a-jour";

    /// <summary>Au-delà, le service est considéré comme absent (arrêté ou jamais activé).</summary>
    public static readonly TimeSpan BattementMaximal = TimeSpan.FromSeconds(30);

    public string Dossier { get; } = string.IsNullOrWhiteSpace(config["MisesAJour:Dossier"]) ? DossierParDefaut : config["MisesAJour:Dossier"]!;

    public EtatInstallateur Lire()
    {
        try
        {
            var chemin = Path.Combine(Dossier, "etat.json");
            if (!File.Exists(chemin)) return new EtatInstallateur(false, null, null, null, null);
            using var json = JsonDocument.Parse(File.ReadAllText(chemin));
            var r = json.RootElement;
            string? Texte(string nom) => r.TryGetProperty(nom, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s ? s : null;
            DateTimeOffset? Date(string nom) => DateTimeOffset.TryParse(Texte(nom), out var d) ? d : null;
            var present = Date("battement") is { } b && temps.GetUtcNow() - b < BattementMaximal;
            return new EtatInstallateur(present, Texte("statut"), Texte("version"), Texte("message"), Date("le"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fichier en cours de réécriture ou illisible : considéré comme absent jusqu'au prochain battement.
            return new EtatInstallateur(false, null, null, null, null);
        }
    }

    /// <summary>Dépose la demande d'installation de <paramref name="version"/> (déjà validée par l'appelant).</summary>
    public void Demander(string version, string demandeePar)
    {
        Directory.CreateDirectory(Dossier);
        var temporaire = Path.Combine(Dossier, $"demande.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaire, JsonSerializer.Serialize(new { version, demandeePar, le = temps.GetUtcNow() }));
        File.Move(temporaire, Path.Combine(Dossier, "demande.json"), overwrite: true);
    }
}
