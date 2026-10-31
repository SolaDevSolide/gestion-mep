using System.Globalization;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GestionMep.Api.Services;

public enum TypeReglage { Texte, Adresse, Entier, Booleen, Reseaux }

/// <summary>
/// Réglage d'exploitation modifiable sans redémarrage. <paramref name="Nom"/> est le nom de la ligne de commande, tiré de
/// la variable du fichier .env (<c>URL_PUBLIQUE</c> → <c>url-publique</c>) ; <paramref name="Cle"/> est la clé de
/// configuration lue par l'application.
/// </summary>
public sealed record DefinitionReglage(string Nom, string Cle, string Groupe, string Libelle, string Aide, TypeReglage Type, string Defaut,
    int Min = 0, int Max = 0)
{
    public string Variable => Nom.Replace('-', '_').ToUpperInvariant();
}

/// <summary>Réglage tel que l'affichent la console et la ligne de commande.</summary>
/// <param name="Origine">« console » (modifié dans l'application), « fichier » (fichier .env) ou « defaut ».</param>
public record ReglageDto(string Nom, string Variable, string Groupe, string Libelle, string Aide, TypeReglage Type, int? Min, int? Max,
    string Valeur, string Origine, string? ValeurFichier, string Defaut, DateTimeOffset? ModifieLe, string? ModifiePar, List<string> Alertes);

public record ReglageSaisie(string? Valeur);

/// <summary>
/// Réglages d'exploitation : valeur de départ dans le fichier .env (variables d'environnement), modifiable ensuite depuis
/// la console d'administration ou <c>gestion-mep reglages</c>. Une valeur modifiée est enregistrée en base (table
/// Reglages) et remplace celle du fichier, sans redémarrage ; « rétablir » revient à la valeur du fichier.
/// Les réglages lus au démarrage seulement (base, port, adresse d'écoute, démonstration…) restent dans le fichier .env.
/// </summary>
public static class Reglages
{
    public const string HttpsObligatoire = "Securite:HttpsObligatoire";
    /// <summary>Taille maximale d'une pièce jointe acceptée par le réglage, en Mo (le serveur web est réglé sur ce plafond).</summary>
    public const int PlafondPieceMo = 1024;
    private const string GroupeReseau = "Adresse et réseau";
    private const string GroupePieces = "Pièces jointes";
    private const string GroupeConnexions = "Connexions";
    private const string GroupeMisesAJour = "Mises à jour et signalement";
    private const string GroupeApi = "API externes";
    private const string GroupeLiaison = "Liaison avec Noosphere";

    public static readonly IReadOnlyList<DefinitionReglage> Catalogue =
    [
        new("url-publique", "Application:UrlPublique", GroupeReseau, "Adresse publique",
            "Adresse par laquelle on ouvre l'application, par exemple https://mep.entreprise.fr. Elle sert à l'adresse de retour de la connexion Microsoft. Vide : déduite de chaque requête.",
            TypeReglage.Adresse, ""),
        new("proxy-reseaux", "ProxyInverse:Reseaux", GroupeReseau, "Réseaux du reverse proxy",
            "Adresses autorisées à transmettre l'adresse du client (CIDR, séparées par des virgules), par exemple 10.0.5.12/32. Une adresse seule vaut pour elle seule.",
            TypeReglage.Reseaux, ""),
        new("https-obligatoire", HttpsObligatoire, GroupeReseau, "HTTPS obligatoire",
            "Désactivé, la session fonctionne aussi en http simple, pour essayer l'application depuis un autre poste sans reverse proxy. Les mots de passe circulent alors en clair : à réserver aux essais.",
            TypeReglage.Booleen, "true"),
        new("pieces-taille-max-mo", "Stockage:TailleMaxMo", GroupePieces, "Taille maximale d'un fichier (Mo)",
            $"De 1 à {PlafondPieceMo} Mo.", TypeReglage.Entier, "20", 1, PlafondPieceMo),
        new("pieces-quota-mep-mo", "Stockage:QuotaMepMo", GroupePieces, "Total par MEP (Mo)",
            "Somme des pièces jointes d'une MEP. 0 = sans limite.", TypeReglage.Entier, "1024", 0, 1_000_000),
        new("pieces-quota-compte-jour-mo", "Stockage:QuotaCompteJourMo", GroupePieces, "Dépôts d'un compte sur 24 heures (Mo)",
            "0 = sans limite.", TypeReglage.Entier, "1024", 0, 1_000_000),
        new("pieces-espace-libre-min-mo", "Stockage:EspaceLibreMinMo", GroupePieces, "Espace disque toujours laissé libre (Mo)",
            "En dessous, les dépôts sont refusés pour protéger la base et les sauvegardes. 0 = pas de contrôle.", TypeReglage.Entier, "2048", 0, 1_000_000),
        new("pieces-compression", "Stockage:Compression", GroupePieces, "Compresser les fichiers déposés",
            "Sans perte : journaux, textes, CSV, JSON, PDF ou images non compressées prennent moins de place sur le serveur et dans les quotas. Le téléchargement rend toujours le fichier d'origine. Les fichiers déjà déposés se compressent avec la commande pieces-jointes compresser.",
            TypeReglage.Booleen, "true"),
        new("pieces-compression-gain-min", "Stockage:CompressionGainMin", GroupePieces, "Gain minimal de la compression (%)",
            "Un fichier qui gagne moins reste tel quel (documents Office, archives, photos, vidéos sont déjà compressés). De 1 à 90.",
            TypeReglage.Entier, "10", 1, 90),
        new("connexions-par-minute", "Connexion:TentativesParMinute", GroupeConnexions, "Tentatives de connexion par minute",
            "Par adresse IP, en plus du blocage d'un compte après 5 échecs depuis la même adresse.", TypeReglage.Entier, "20", 1, 10_000),
        new("flux-max", "Flux:ConnexionsMax", GroupeConnexions, "Écrans suivis en temps réel",
            "Nombre maximal de MEP ouvertes en même temps sur tout le serveur (20 au plus par compte).", TypeReglage.Entier, "1000", 1, 100_000),
        new("verifier-mises-a-jour", "MisesAJour:Verifier", GroupeMisesAJour, "Signaler les nouvelles versions",
            "Consulte les versions publiées sur GitHub. Désactivé : le serveur ne contacte jamais GitHub.", TypeReglage.Booleen, "true"),
        new("lien-signalement", "Signalement:Lien", GroupeMisesAJour, "Lien « Signaler un problème ou une idée »",
            "Adresse ouverte depuis le menu utilisateur, par exemple un outil interne. Vide : lien masqué.",
            TypeReglage.Adresse, ApplicationEndpoints.LienSignalementParDefaut),
        new("api-reseaux-autorises", ReseauSortant.CleReseaux, GroupeApi, "Réseaux internes autorisés",
            "Adresses internes que les connexions aux API externes peuvent joindre (CIDR, séparés par des virgules), par exemple 10.20.0.0/16. Vide : aucune adresse interne (privée, locale, de métadonnées) ; http n'est accepté que vers ces réseaux.",
            TypeReglage.Reseaux, ""),
        new("api-proxy", ReseauSortant.CleProxy, GroupeApi, "Proxy sortant",
            "Proxy par lequel passent les appels vers Internet, par exemple http://proxy.entreprise.fr:3128. Les réseaux internes autorisés passent sans lui. Vide : aucun proxy.",
            TypeReglage.Adresse, ""),
        new("api-appels-conservation-jours", EnvoiAppelsApi.CleConservation, GroupeApi, "Historique des appels (jours)",
            "Durée de conservation du détail des appels aux API externes, de 1 à 3650 jours.", TypeReglage.Entier, "90", 1, 3650),
        new("api-envois-actifs", EnvoiAppelsApi.CleEnvoisActifs, GroupeApi, "Envois actifs",
            "Désactivé, les appels sont mis en file sans partir : utile après une restauration ou sur un serveur de recette. Ils partent à la réactivation.",
            TypeReglage.Booleen, "true"),
        new("liaison-nom-instance", ServiceLiaisons.CleNomInstance, GroupeLiaison, "Nom de cette instance",
            "Nom de gestion-mep montré dans Noosphere (fiche de la liaison), par exemple « MEP Alizé ». Vide : « gestion-mep ».",
            TypeReglage.Texte, ""),
    ];

    public static DefinitionReglage Trouver(string nom) =>
        Catalogue.FirstOrDefault(d => d.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))
        ?? throw ErreurHttp.NonTrouve($"Réglage inconnu : « {nom} ». Réglages : {string.Join(", ", Catalogue.Select(d => d.Nom))}.");

    /// <summary>
    /// Valeur donnée au démarrage par une variable d'environnement (fichier .env) ; null si elle n'est pas définie.
    /// Les valeurs d'appsettings.json sont les valeurs par défaut de l'image.
    /// </summary>
    public static string? ValeurFichier(IConfiguration config, string cle) =>
        Valeur(config, cle, p => p is not FournisseurReglagesBase and not FileConfigurationProvider);

    /// <summary>Valeur en cours sans tenir compte de la base : celle du fichier .env, sinon la valeur par défaut.</summary>
    public static string ValeurSansConsole(IConfiguration config, DefinitionReglage d) =>
        Valeur(config, d.Cle, p => p is not FournisseurReglagesBase) ?? d.Defaut;

    private static string? Valeur(IConfiguration config, string cle, Func<IConfigurationProvider, bool> retenu) =>
        config is IConfigurationRoot racine
            ? racine.Providers.Where(retenu).Reverse().Select(p => p.TryGet(cle, out var v) ? v : null).FirstOrDefault(v => v is not null)
            : config[cle];

    public static async Task<List<ReglageDto>> Lister(MepDbContext db, IConfiguration config)
    {
        var enBase = await db.Reglages.AsNoTracking().ToDictionaryAsync(r => r.Cle, StringComparer.OrdinalIgnoreCase);
        return Catalogue.Select(d => Dto(d, enBase.GetValueOrDefault(d.Cle), config)).ToList();
    }

    private static ReglageDto Dto(DefinitionReglage d, Reglage? enBase, IConfiguration config)
    {
        var fichier = ValeurFichier(config, d.Cle);
        var valeur = enBase?.Valeur ?? ValeurSansConsole(config, d);
        var origine = enBase is not null ? "console" : fichier is not null ? "fichier" : "defaut";
        return new ReglageDto(d.Nom, d.Variable, d.Groupe, d.Libelle, d.Aide, d.Type,
            d.Type == TypeReglage.Entier ? d.Min : null, d.Type == TypeReglage.Entier ? d.Max : null,
            valeur, origine, fichier, Valeur(config, d.Cle, p => p is FileConfigurationProvider) ?? d.Defaut, enBase?.ModifieLe, enBase?.ModifiePar, Alertes(d, valeur));
    }

    /// <summary>Points d'attention sur une valeur acceptée (affichés dans la console et par la ligne de commande).</summary>
    public static List<string> Alertes(DefinitionReglage d, string valeur)
    {
        var alertes = new List<string>();
        switch (d.Nom)
        {
            case "https-obligatoire" when valeur == "false":
                alertes.Add("Mode d'essai : la session fonctionne en http simple et les mots de passe peuvent circuler en clair. À réactiver avant la mise en service.");
                break;
            case "url-publique" when Uri.TryCreate(valeur, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp && !u.IsLoopback:
                alertes.Add("Adresse en http : Microsoft refuse ce type d'adresse de retour, la connexion Microsoft ne fonctionnera pas.");
                break;
            case "api-reseaux-autorises" when valeur.Split(',').Any(r => r.Trim().EndsWith("/0", StringComparison.Ordinal)):
                alertes.Add("Toutes les adresses sont autorisées : les connexions aux API externes peuvent joindre tout le réseau interne, y compris les adresses de métadonnées.");
                break;
            case "api-envois-actifs" when valeur == "false":
                alertes.Add("Les appels aux API externes restent en file et ne partent pas.");
                break;
            case "proxy-reseaux" when valeur.Split(',').Any(r => r.Trim().EndsWith("/0", StringComparison.Ordinal)):
                alertes.Add("Tout le monde peut choisir son adresse IP : la limite des tentatives de connexion par adresse devient contournable.");
                break;
        }
        return alertes;
    }

    /// <summary>Vérifie et met en forme une saisie (erreur 400 si elle est invalide).</summary>
    public static string Normaliser(DefinitionReglage d, string? saisie)
    {
        var texte = (saisie ?? "").Trim();
        switch (d.Type)
        {
            case TypeReglage.Entier:
                if (!int.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < d.Min || n > d.Max)
                    throw ErreurHttp.Saisie($"{d.Libelle} : nombre entier de {d.Min} à {d.Max} attendu.");
                return n.ToString(CultureInfo.InvariantCulture);
            case TypeReglage.Booleen:
                return texte.ToLowerInvariant() switch
                {
                    "true" or "oui" or "1" => "true",
                    "false" or "non" or "0" => "false",
                    _ => throw ErreurHttp.Saisie($"{d.Libelle} : oui ou non attendu."),
                };
            case TypeReglage.Adresse:
                if (texte.Length == 0) return "";
                if (texte.Length > 2000 || !Uri.TryCreate(texte, UriKind.Absolute, out var url)
                    || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) || url.UserInfo.Length > 0)
                    throw ErreurHttp.Saisie($"{d.Libelle} : adresse complète attendue, commençant par https:// (ou http://).");
                if (d.Nom == "url-publique")
                {
                    if (url.Query.Length > 0 || url.Fragment.Length > 0)
                        throw ErreurHttp.Saisie($"{d.Libelle} : adresse sans « ? » ni « # » attendue, par exemple https://mep.entreprise.fr.");
                    return texte.TrimEnd('/');
                }
                return texte;
            case TypeReglage.Reseaux:
                var reseaux = new List<string>();
                foreach (var r in texte.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (System.Net.IPNetwork.TryParse(r, out var reseau)) reseaux.Add(reseau.ToString());
                    else if (System.Net.IPAddress.TryParse(r, out var ip)) reseaux.Add(new System.Net.IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128).ToString());
                    else throw ErreurHttp.Saisie($"{d.Libelle} : « {r} » n'est ni une adresse IP ni un réseau (CIDR, par exemple 10.0.5.0/24).");
                }
                return string.Join(", ", reseaux);
            default:
                return texte.Length <= 2000 ? texte : throw ErreurHttp.Saisie($"{d.Libelle} : 2000 caractères au plus.");
        }
    }

    /// <summary>
    /// Enregistre une valeur (qui remplace celle du fichier .env) et l'inscrit au journal d'administration.
    /// <paramref name="acteur"/> null : ligne de commande.
    /// </summary>
    public static async Task<ReglageDto> Definir(MepDbContext db, IConfiguration config, DefinitionReglage d, string? saisie, Acteur? acteur,
        DateTimeOffset maintenant)
    {
        var valeur = Normaliser(d, saisie);
        var ligne = await db.Reglages.FirstOrDefaultAsync(r => r.Cle == d.Cle);
        var avant = ligne?.Valeur ?? ValeurSansConsole(config, d);
        if (ligne is null) db.Reglages.Add(ligne = new Reglage { Cle = d.Cle });
        ligne.Valeur = valeur;
        ligne.ModifieLe = maintenant;
        ligne.ModifiePar = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande;
        JournalAdmin.Ecrire(db, acteur, "reglage.modifie", $"{d.Libelle} ({d.Nom}) : {Afficher(avant)} → {Afficher(valeur)}");
        await db.SaveChangesAsync();
        return Dto(d, ligne, config);
    }

    /// <summary>Supprime la valeur modifiée : le réglage reprend la valeur du fichier .env (ou la valeur par défaut).</summary>
    public static async Task<ReglageDto> Retablir(MepDbContext db, IConfiguration config, DefinitionReglage d, Acteur? acteur)
    {
        var ligne = await db.Reglages.FirstOrDefaultAsync(r => r.Cle == d.Cle);
        if (ligne is not null)
        {
            db.Reglages.Remove(ligne);
            var source = ValeurFichier(config, d.Cle) is null ? "valeur par défaut" : "valeur du fichier .env";
            JournalAdmin.Ecrire(db, acteur, "reglage.retabli", $"{d.Libelle} ({d.Nom}) : {Afficher(ligne.Valeur)} → {Afficher(ValeurSansConsole(config, d))} ({source})");
            await db.SaveChangesAsync();
        }
        return Dto(d, null, config);
    }

    private static string Afficher(string valeur) => valeur.Length == 0 ? "(vide)" : $"« {valeur} »";
}

/// <summary>Ajoute les réglages enregistrés en base à la configuration, après les variables d'environnement (ils priment).</summary>
public sealed class SourceReglagesBase(string? chaineConnexion) : IConfigurationSource
{
    public FournisseurReglagesBase Fournisseur { get; } = new(chaineConnexion);
    public IConfigurationProvider Build(IConfigurationBuilder builder) => Fournisseur;
}

/// <summary>
/// Lit la table Reglages (seulement les clés du <see cref="Reglages.Catalogue"/> : la base ne peut pas remplacer la chaîne
/// de connexion ni un dossier). Base injoignable ou table absente (avant la première migration) : on garde ce qui était lu.
/// </summary>
public sealed class FournisseurReglagesBase(string? chaineConnexion) : ConfigurationProvider
{
    private static readonly string[] Cles = [.. Reglages.Catalogue.Select(d => d.Cle)];
    private readonly string? _chaine = chaineConnexion is null ? null
        : new NpgsqlConnectionStringBuilder(chaineConnexion) { Timeout = 5, CommandTimeout = 5 }.ConnectionString;

    public override void Load() => Lire();

    /// <summary>Relit la base ; prévient la configuration (et donc les options) si une valeur a changé.</summary>
    public void Recharger()
    {
        var avant = Data;
        if (Lire() && (avant.Count != Data.Count || avant.Any(kv => !Data.TryGetValue(kv.Key, out var v) || v != kv.Value)))
            OnReload();
    }

    private bool Lire()
    {
        if (_chaine is null) return false;
        try
        {
            using var cn = new NpgsqlConnection(_chaine);
            cn.Open();
            using var cmd = new NpgsqlCommand("""select "Cle", "Valeur" from "Reglages" where "Cle" = any(@cles)""", cn);
            cmd.Parameters.AddWithValue("cles", Cles);
            var lus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            using (var r = cmd.ExecuteReader())
                while (r.Read()) lus[r.GetString(0)] = r.GetString(1);
            Data = lus;
            return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }
}

/// <summary>Relit les réglages en base toutes les 10 secondes : un changement fait par la ligne de commande s'applique sans redémarrage.</summary>
public sealed class RafraichissementReglages(FournisseurReglagesBase fournisseur, TimeProvider temps) : BackgroundService
{
    public static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var minuterie = new PeriodicTimer(Intervalle, temps);
        while (await minuterie.WaitForNextTickAsync(stoppingToken))
            await Task.Run(fournisseur.Recharger, stoppingToken);
    }
}
