using Microsoft.Extensions.Configuration;

namespace GestionMep.Api.Services;

/// <summary>
/// Type d'installation : image Docker (référence) ou installation autonome, sans Docker, sur un serveur Linux ou Windows
/// (archive de la version, script d'installation et service système). Sert aux messages de la console et de la ligne de
/// commande, qui donnent la commande adaptée.
/// </summary>
public static class Installation
{
    public const string Docker = "docker";
    public const string Linux = "linux";
    public const string Windows = "windows";

    /// <summary>Variable posée par les images .NET de Microsoft, dont part l'image de l'application.</summary>
    public static bool EstDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    public static string Mode => EstDocker ? Docker : OperatingSystem.IsWindows() ? Windows : Linux;

    /// <summary>Commande d'exploitation complète, à taper sur le serveur : <c>gestion-mep reglages lister</c> hors Docker.</summary>
    public static string Commande(string arguments) => Mode switch
    {
        Docker => $"docker compose exec app gestion-mep {arguments}",
        Linux => $"sudo gestion-mep {arguments}",
        _ => $"gestion-mep {arguments}",
    };

    /// <summary>Arrêt, puis redémarrage de l'application (restauration d'une sauvegarde, application arrêtée).</summary>
    public static (string Arreter, string Lancer, string Executer) Service => Mode switch
    {
        Docker => ("docker compose stop app", "docker compose up -d app", "docker compose run --rm app gestion-mep"),
        Linux => ("sudo systemctl stop gestion-mep", "sudo systemctl start gestion-mep", "sudo gestion-mep"),
        _ => ("Stop-Service GestionMep", "Start-Service GestionMep", "gestion-mep"),
    };

    /// <summary>Fichier qui donne la valeur de départ des réglages, pour les messages.</summary>
    public static string NomFichier => EstDocker ? "fichier .env" : "fichier de configuration";

    /// <summary>Où changer un réglage lu au démarrage, et comment le prendre en compte.</summary>
    public static string FichierDemarrage => Mode switch
    {
        Docker => "fichier .env, puis « docker compose up -d »",
        Linux => $"fichier {FichierInstallation.Chemin}, puis « sudo systemctl restart gestion-mep »",
        _ => $"fichier {FichierInstallation.Chemin}, puis « Restart-Service GestionMep »",
    };

    /// <summary>Installation d'une version hors Docker : le script fourni dans l'archive de chaque version.</summary>
    public static string CommandeMiseAJour(string version) => Mode == Windows
        ? $"powershell -ExecutionPolicy Bypass -File \"$env:ProgramFiles\\GestionMep\\installer.ps1\" -Version {version}"
        : $"sudo /opt/gestion-mep/installer.sh --version {version}";
}

/// <summary>
/// Fichier de configuration de l'installation autonome (sans Docker) : mêmes noms que le fichier .env de Docker
/// (<c>URL_PUBLIQUE</c>, <c>PIECES_TAILLE_MAX_MO</c>…), plus la connexion à la base, le dossier des données et l'adresse
/// d'écoute. Lignes <c>NOM=valeur</c>, commentaires <c>#</c>. Un nom de la forme <c>Section__Cle</c> est aussi accepté.
/// Absent (cas de Docker), il n'a aucun effet.
/// </summary>
public static class FichierInstallation
{
    /// <summary>Noms du fichier .env et réglages correspondants (les mêmes que dans docker-compose.yml).</summary>
    public static readonly IReadOnlyDictionary<string, string> Correspondances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["BASE_DE_DONNEES"] = "ConnectionStrings:Mep",
        ["DEMO"] = "Demo:Activer",
        ["ADMIN_LOGIN"] = "Admin:Login",
        ["ADMIN_MOT_DE_PASSE"] = "Admin:MotDePasse",
        ["PIECES_TAILLE_MAX_MO"] = "Stockage:TailleMaxMo",
        ["PIECES_QUOTA_MEP_MO"] = "Stockage:QuotaMepMo",
        ["PIECES_QUOTA_COMPTE_JOUR_MO"] = "Stockage:QuotaCompteJourMo",
        ["PIECES_ESPACE_LIBRE_MIN_MO"] = "Stockage:EspaceLibreMinMo",
        ["PROXY_RESEAUX"] = "ProxyInverse:Reseaux",
        ["CONNEXIONS_PAR_MINUTE"] = "Connexion:TentativesParMinute",
        ["VERIFIER_MISES_A_JOUR"] = "MisesAJour:Verifier",
        ["LIEN_SIGNALEMENT"] = "Signalement:Lien",
        ["URL_PUBLIQUE"] = "Application:UrlPublique",
        ["FLUX_MAX"] = "Flux:ConnexionsMax",
        ["HTTPS_OBLIGATOIRE"] = "Securite:HttpsObligatoire",
    };

    /// <summary>Emplacement : variable GESTION_MEP_CONFIG, sinon l'emplacement installé par le script de chaque système.</summary>
    public static string Chemin =>
        Environment.GetEnvironmentVariable("GESTION_MEP_CONFIG") is { Length: > 0 } chemin ? chemin
        : OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GestionMep", "gestion-mep.conf")
            : "/etc/gestion-mep/gestion-mep.conf";

    /// <summary>Lignes <c>NOM=valeur</c> du fichier (vide s'il n'existe pas). Une valeur entre guillemets est retirée de ses guillemets.</summary>
    public static Dictionary<string, string> Lire(string chemin)
    {
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] lignes;
        try
        {
            if (!File.Exists(chemin))
            {
                // Dossier présent mais fermé à ce compte : le fichier y est sans doute, illisible.
                if (Path.GetDirectoryName(chemin) is { Length: > 0 } dossier && Directory.Exists(dossier))
                    _ = Directory.EnumerateFileSystemEntries(dossier).Any();
                return valeurs;
            }
            lignes = File.ReadAllLines(chemin);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ConfigurationIllisibleException(
                $"Fichier de configuration {chemin} illisible pour ce compte : lancer la commande avec sudo (Linux) ou dans une console en administrateur (Windows).");
        }
        foreach (var brute in lignes)
        {
            var ligne = brute.Trim();
            if (ligne.Length == 0 || ligne.StartsWith('#')) continue;
            var egal = ligne.IndexOf('=');
            if (egal <= 0) continue;
            var valeur = ligne[(egal + 1)..].Trim();
            if (valeur.Length >= 2 && (valeur[0] == '"' || valeur[0] == '\'') && valeur[^1] == valeur[0])
                valeur = valeur[1..^1];
            valeurs[ligne[..egal].Trim()] = valeur;
        }
        return valeurs;
    }

    /// <summary>Réglages de l'application déduits du fichier (clés de configuration ASP.NET Core).</summary>
    public static Dictionary<string, string?> Reglages(IReadOnlyDictionary<string, string> fichier)
    {
        var reglages = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nom, valeur) in fichier)
        {
            if (Correspondances.TryGetValue(nom, out var cle)) reglages[cle] = valeur;
            else if (nom.Contains("__")) reglages[nom.Replace("__", ":")] = valeur;
        }
        // Un seul dossier de données, découpé comme les volumes Docker.
        if (fichier.TryGetValue("DOSSIER_DONNEES", out var dossier) && dossier.Length > 0)
        {
            reglages["Stockage:Dossier"] = Path.Combine(dossier, "pieces-jointes");
            reglages["Cles:Dossier"] = Path.Combine(dossier, "cles");
            reglages["Sauvegardes:Dossier"] = Path.Combine(dossier, "sauvegardes");
            reglages["MisesAJour:Dossier"] = Path.Combine(dossier, "mise-a-jour");
        }
        return reglages;
    }

    /// <summary>Adresse d'écoute (ADRESSE_ECOUTE, 127.0.0.1 par défaut) et port (PORT, 8080 par défaut) ; null hors installation autonome.</summary>
    public static string? AdresseEcoute(IReadOnlyDictionary<string, string> fichier)
    {
        if (fichier.Count == 0) return null;
        var adresse = fichier.TryGetValue("ADRESSE_ECOUTE", out var a) && a.Length > 0 ? a : "127.0.0.1";
        var port = fichier.TryGetValue("PORT", out var p) && p.Length > 0 ? p : "8080";
        return $"http://{(adresse.Contains(':') ? $"[{adresse}]" : adresse)}:{port}";
    }

    /// <summary>Ajoute le fichier aux sources de configuration (après les variables d'environnement : il les remplace).</summary>
    public static IConfigurationBuilder AjouterFichierInstallation(this IConfigurationBuilder builder) =>
        builder.AddInMemoryCollection(Reglages(Lire(Chemin)));
}

/// <summary>Fichier de configuration présent mais illisible pour le compte qui lance l'application ou la commande.</summary>
public sealed class ConfigurationIllisibleException(string message) : Exception(message);
