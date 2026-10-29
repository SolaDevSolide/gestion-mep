using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// Commandes d'exploitation et d'administration, sans navigateur, pour l'équipe informatique. Dans le conteneur :
/// <c>docker compose exec app gestion-mep &lt;commande&gt;</c>. Elles lisent la même configuration que l'application
/// (variables d'environnement, appsettings, fichier de configuration, réglages en base), travaillent directement sur la base
/// (aucun appel HTTP, donc aucun jeton), appliquent les mêmes règles que la console (méthodes partagées des Endpoints) et
/// inscrivent leurs modifications dans le journal d'administration (auteur « Ligne de commande »).
/// <para>
/// Conventions communes à toutes les commandes (docs/Ligne-de-commande.md) : forme « groupe verbe » (les anciens noms restent
/// des alias), aide sur chaque commande (<c>--help</c>), sortie lisible par défaut et JSON stable avec <c>--json</c>, résultats
/// sur la sortie standard et messages sur la sortie d'erreur, <c>--quiet</c> / <c>--verbose</c>, confirmation des opérations
/// destructives (<c>--oui</c>, jamais de question hors terminal), codes de retour documentés (<see cref="Codes"/>).
/// </para>
/// </summary>
public static partial class LigneDeCommande
{
    /// <summary>Codes de retour, identiques pour toutes les commandes.</summary>
    public static class Codes
    {
        public const int Succes = 0;
        /// <summary>Échec : règle refusée, contrôle en échec, fichier invalide…</summary>
        public const int Echec = 1;
        /// <summary>Commande mal formée : commande ou option inconnue, argument manquant.</summary>
        public const int Usage = 2;
        /// <summary>Base de données injoignable, ou configuration absente ou illisible.</summary>
        public const int Environnement = 3;
        /// <summary>Opération destructive sans confirmation (hors terminal sans --oui, ou réponse autre que « oui »).</summary>
        public const int Confirmation = 4;
        /// <summary>Élément désigné introuvable (compte, équipe, système, MEP…).</summary>
        public const int Introuvable = 5;
    }

    /// <summary>Même forme que les réponses de l'API (camelCase, énumérations en texte), lisible et sans échappement des accents.</summary>
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    // ---------- Session : options globales et sorties de la commande en cours ----------

    /// <summary>État d'une exécution : options globales, messages et résultat de l'enveloppe JSON.</summary>
    private sealed class Session
    {
        public bool Json;
        public bool Silencieux;
        public bool Bavard;
        public bool NonInteractif;
        public Commande? Commande;
        public bool DonneesEcrites;
        public object? Resultat;
        public readonly List<string> Messages = [];
        public readonly List<string> Avertissements = [];
    }

    private static readonly AsyncLocal<Session?> SessionCourante = new();

    private static Session S => SessionCourante.Value ??= new Session();

    /// <summary>Sortie JSON demandée (option --json), pour la commande en cours.</summary>
    private static bool EnJson => S.Json;

    /// <summary>Résultat demandé (tableau, détail, mot de passe provisoire…) : toujours affiché, même avec --quiet.</summary>
    private static void Ecrire(string ligne = "")
    {
        if (S.Json) S.Messages.Add(ligne);
        else Console.WriteLine(ligne);
    }

    /// <summary>Compte rendu d'une modification (« Compte créé. ») : sortie standard, sauf avec --quiet.</summary>
    private static void Informer(string ligne)
    {
        if (S.Json) S.Messages.Add(ligne);
        else if (!S.Silencieux) Console.WriteLine(ligne);
    }

    /// <summary>Avertissement (rien n'est bloqué) : sortie d'erreur, sauf avec --quiet ; repris dans l'enveloppe JSON.</summary>
    private static void Avertir(string ligne)
    {
        if (S.Json) S.Avertissements.Add(ligne);
        if (!S.Silencieux) Console.Error.WriteLine(ligne);
    }

    /// <summary>Détail de fonctionnement (configuration lue, durée) : sortie d'erreur, avec --verbose seulement.</summary>
    private static void Detailler(string ligne)
    {
        if (S.Bavard) Console.Error.WriteLine(ligne);
    }

    /// <summary>Données de la commande au format JSON (lectures) : remplacent l'enveloppe.</summary>
    private static void EcrireJson(object valeur)
    {
        S.DonneesEcrites = true;
        Console.WriteLine(JsonSerializer.Serialize(valeur, OptionsJson));
    }

    /// <summary>Objet créé ou modifié, rendu dans le champ <c>resultat</c> de l'enveloppe JSON.</summary>
    private static void Resultat(object? valeur) => S.Resultat = valeur;

    /// <summary>
    /// Enveloppe JSON des commandes qui n'écrivent pas de données : <c>{ code, messages, avertissements, resultat }</c>.
    /// </summary>
    private static void TerminerJson(int code)
    {
        if (!S.Json || S.DonneesEcrites) return;
        Console.WriteLine(JsonSerializer.Serialize(new EnveloppeJson(code, S.Messages, S.Avertissements, S.Resultat), OptionsJson));
    }

    private sealed record EnveloppeJson(int Code, List<string> Messages, List<string> Avertissements, object? Resultat);

    private sealed record ErreurJson(int Code, string Erreur, IReadOnlyList<string>? Details);

    // ---------- Options globales ----------

    /// <summary>Options valables pour toutes les commandes, à placer n'importe où (avant ou après la commande).</summary>
    internal sealed record OptionsGlobales(bool Json, bool Silencieux, bool Bavard, bool NonInteractif, string? Config, bool Aide, string[] Reste);

    internal static readonly (string Option, string Description)[] DescriptionOptionsGlobales =
    [
        ("--json", "Sortie JSON pour les scripts (données, ou enveloppe { code, messages, avertissements, resultat })"),
        ("--quiet, -q", "N'affiche que l'essentiel : données demandées et erreurs (ni compte rendu ni avertissement)"),
        ("--verbose, -v", "Détaille sur la sortie d'erreur la configuration lue, la base utilisée et la durée"),
        ("--non-interactif", "Ne pose jamais de question (implicite hors terminal) : une confirmation manquante échoue (code 4)"),
        ("--config <fichier>", "Fichier de configuration à lire (installation sans Docker), au lieu de GESTION_MEP_CONFIG"),
        ("--help, -h", "Aide de la commande : usage, options, exemples"),
    ];

    internal static OptionsGlobales LireOptionsGlobales(string[] args)
    {
        bool json = false, silencieux = false, bavard = false, nonInteractif = false, aide = false;
        string? config = null;
        var reste = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--quiet" or "-q": silencieux = true; break;
                case "--verbose" or "-v": bavard = true; break;
                case "--non-interactif": nonInteractif = true; break;
                case "--help" or "-h": aide = true; break;
                case "--config":
                    if (i + 1 >= args.Length) throw new ErreurUsage("valeur manquante après --config");
                    config = args[++i];
                    break;
                default: reste.Add(args[i]); break;
            }
        }
        if (silencieux && bavard) throw new ErreurUsage("--quiet et --verbose s'excluent");
        return new OptionsGlobales(json, silencieux, bavard, nonInteractif, config, aide, [.. reste]);
    }

    /// <summary>
    /// « gestion-mep » en premier argument est ignoré : l'image lance directement l'application, si bien que
    /// « docker compose run --rm app gestion-mep sauvegardes … » (préfixe recopié de « docker compose exec ») doit faire
    /// la même chose que « docker compose run --rm app sauvegardes … », et non démarrer l'application web.
    /// </summary>
    private static string[] SansPrefixe(string[] args) => args is ["gestion-mep", .. var reste] ? (reste.Length == 0 ? ["aide"] : reste) : args;

    /// <summary>
    /// Vrai si les arguments désignent une commande : un premier mot qui n'est pas une option, même inconnu (la commande
    /// propose alors le bon nom au lieu de démarrer le serveur). Sinon (aucun argument, ou options « --cle valeur » d'ASP.NET),
    /// l'application web démarre avec ces arguments.
    /// </summary>
    public static bool EstCommande(string[] args)
    {
        OptionsGlobales g;
        try
        {
            g = LireOptionsGlobales(SansPrefixe(args));
        }
        catch (ErreurUsage)
        {
            return true; // « --config » sans valeur : la commande dira ce qui manque
        }
        return g.Reste.Length == 0 ? g.Aide : NomsDeTete.Contains(g.Reste[0]) || !g.Reste[0].StartsWith('-');
    }

    // ---------- Exécution ----------

    public static async Task<int> Executer(string[] arguments)
    {
        var session = SessionCourante.Value = new Session();
        var chrono = Stopwatch.StartNew();
        var configAvant = Environment.GetEnvironmentVariable("GESTION_MEP_CONFIG");
        var code = Codes.Echec;
        try
        {
            var g = LireOptionsGlobales(SansPrefixe(arguments));
            session.Json = g.Json;
            session.Silencieux = g.Silencieux;
            session.Bavard = g.Bavard;
            // Pas de question à poser : option, entrée redirigée (script, tube, cron) ou sortie JSON (programme).
            session.NonInteractif = g.NonInteractif || g.Json || Console.IsInputRedirected;
            if (g.Config is { } fichier)
            {
                if (!File.Exists(fichier)) throw new ConfigurationIllisibleException($"Fichier de configuration introuvable : {fichier} (option --config).");
                Environment.SetEnvironmentVariable("GESTION_MEP_CONFIG", Path.GetFullPath(fichier));
            }
            code = await Lancer(g);
            TerminerJson(code);
        }
        catch (ErreurUsage e)
        {
            code = Codes.Usage;
            SignalerErreur(code, $"Commande mal formée : {e.Message.TrimEnd('.')}{(e.Message.EndsWith('?') ? "" : ".")}", null, session.Commande is { } c
                ? [$"Usage : gestion-mep {c.Nom}{(c.Usage.Length > 0 ? " " + c.Usage : "")}", $"Aide : gestion-mep {c.Nom} --help"]
                : [e.Suite ?? "Liste des commandes : gestion-mep aide"]);
        }
        catch (ConfirmationRequise e)
        {
            code = Codes.Confirmation;
            SignalerErreur(code, e.Message, null, []);
        }
        catch (ErreurHttp e)
        {
            code = e.Statut == 404 ? Codes.Introuvable : Codes.Echec;
            SignalerErreur(code, e.Message, e.Erreurs is { Count: > 1 } ? e.Erreurs : null, []);
        }
        catch (ConfigurationIllisibleException e)
        {
            code = Codes.Environnement;
            SignalerErreur(code, e.Message, null, []);
        }
        catch (RegleMetierException e)
        {
            code = Codes.Echec;
            SignalerErreur(code, e.Message, null, []);
        }
        catch (Exception e) when (BaseInjoignable(e) is { } cause)
        {
            // Base arrêtée ou mal renseignée : un message clair plutôt qu'une pile d'appels.
            code = Codes.Environnement;
            SignalerErreur(code, $"base de données injoignable ({cause.Message.TrimEnd('.')}). Vérifier que PostgreSQL tourne "
                + $"et la connexion à la base dans le {Installation.NomFichier} (« gestion-mep config verifier »).", null, []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            code = Codes.Echec;
            SignalerErreur(code, $"fichier ou dossier inaccessible : {e.Message}", null, []);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Erreur imprévue : message court, pile d'appels avec --verbose.
            code = Codes.Echec;
            SignalerErreur(code, $"erreur inattendue ({e.GetType().Name}) : {e.Message}", null,
                session.Bavard ? [e.ToString()] : ["Relancer avec --verbose pour le détail."]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GESTION_MEP_CONFIG", configAvant);
            Detailler($"Terminé en {chrono.ElapsedMilliseconds} ms, code de retour {code}.");
        }
        return code;
    }

    /// <summary>Message d'erreur sur la sortie d'erreur : texte (« Erreur : … » et pistes), ou objet JSON avec --json.</summary>
    private static void SignalerErreur(int code, string message, IReadOnlyList<string>? details, IReadOnlyList<string> suite)
    {
        if (S.Json)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new ErreurJson(code, message, details), OptionsJson));
            return;
        }
        var texte = message.StartsWith("Commande mal formée", StringComparison.Ordinal) || message.StartsWith("Commande inconnue", StringComparison.Ordinal)
            || message.StartsWith("Confirmation", StringComparison.Ordinal) || message.StartsWith("Abandon", StringComparison.Ordinal)
            ? message : $"Erreur : {message}";
        if (details is not null)
        {
            Console.Error.WriteLine(texte.TrimEnd(' ', ':', '.') + " :");
            foreach (var d in details) Console.Error.WriteLine($"  - {d}");
        }
        else Console.Error.WriteLine(texte);
        foreach (var s in suite) Console.Error.WriteLine(s);
    }

    /// <summary>Trouve la commande (ou le groupe) désignée, puis l'exécute ou affiche son aide.</summary>
    private static async Task<int> Lancer(OptionsGlobales g)
    {
        var args = g.Reste;
        if (args is ["aide"] && g.Aide) return AfficherAide(["aide"]); // « aide --help » : l'aide de la commande aide
        if (args is [] or ["aide", ..])
            return AfficherAide(args.Length == 0 ? [] : args[1..]);
        var (commande, reste, groupe) = Resoudre(args);
        if (commande is null)
        {
            if (groupe is not null)
            {
                if (g.Aide) return AfficherAide([groupe]);
                var verbes = Registre.Where(c => c.Groupe == groupe).Select(c => c.Verbe).ToList();
                if (reste.Length == 0)
                    throw new ErreurUsage($"« {groupe} » attend une action : {string.Join(", ", verbes)}") { Suite = $"Aide : gestion-mep {groupe} --help" };
                throw new ErreurUsage($"action inconnue « {groupe} {reste[0]} »{Suggestion(reste[0], verbes, $"{groupe} ")}")
                    { Suite = $"Actions de « {groupe} » : {string.Join(", ", verbes)} (gestion-mep {groupe} --help)" };
            }
            if (g.Aide) return AfficherAide([]);
            throw new ErreurUsage($"commande inconnue « {args[0]} »{Suggestion(args[0], Groupes.Select(x => x.Nom).Concat(Registre.Where(c => c.Verbe.Length == 0).Select(c => c.Nom)), "")}");
        }
        S.Commande = commande;
        if (g.Aide) return AfficherAide(commande.Nom.Split(' '));
        Detailler($"Commande : gestion-mep {commande.Nom}");
        return await commande.Executer(new Appel(reste));
    }

    /// <summary>« reglage » est l'ancien nom du groupe « reglages ».</summary>
    private static string NomGroupe(string nom) => nom == "reglage" ? "reglages" : nom;

    private static (Commande? Commande, string[] Reste, string? Groupe) Resoudre(string[] args)
    {
        if (args.Length >= 2 && Trouver($"{NomGroupe(args[0])} {args[1]}") is { } c2) return (c2, args[2..], null);
        if (Trouver(NomGroupe(args[0])) is { } c1) return (c1, args[1..], null);
        var groupe = NomGroupe(args[0]);
        return Groupes.Any(x => x.Nom == groupe) ? (null, args[1..], groupe) : (null, args, null);
    }

    private static Commande? Trouver(string nom) => Registre.FirstOrDefault(c => c.Nom == nom || c.Alias.Contains(nom));

    /// <summary>« Vouliez-vous dire … ? » : le nom connu le plus proche (distance d'édition 2 au plus, ou préfixe).</summary>
    private static string Suggestion(string saisi, IEnumerable<string> connus, string prefixe)
    {
        var proche = connus.Distinct()
            .Select(c => (Nom: c, Distance: c.StartsWith(saisi, StringComparison.OrdinalIgnoreCase) && saisi.Length >= 3 ? 0 : Distance(saisi.ToLowerInvariant(), c)))
            .Where(x => x.Distance <= Math.Max(1, Math.Min(2, saisi.Length / 3)))
            .OrderBy(x => x.Distance).ThenBy(x => x.Nom, StringComparer.Ordinal).FirstOrDefault();
        return proche.Nom is null ? "" : $" : vouliez-vous dire « gestion-mep {prefixe}{proche.Nom} » ?";
    }

    /// <summary>Distance de Levenshtein.</summary>
    internal static int Distance(string a, string b)
    {
        var precedente = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var courante = new int[b.Length + 1];
            courante[0] = i;
            for (var j = 1; j <= b.Length; j++)
                courante[j] = Math.Min(Math.Min(courante[j - 1] + 1, precedente[j] + 1), precedente[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            precedente = courante;
        }
        return precedente[b.Length];
    }

    // ---------- Arguments d'une commande ----------

    /// <summary>Arguments placés après le nom de la commande (options globales déjà retirées).</summary>
    private sealed class Appel(string[] args)
    {
        public string[] Args => args;

        private static bool EstOption(string a) => a.Length > 1 && a[0] == '-' && !char.IsDigit(a[1]);

        /// <summary>Argument positionnel obligatoire (une option à sa place est une erreur d'usage).</summary>
        public string Arg(int i, string nom) =>
            i < args.Length && !EstOption(args[i]) ? args[i] : throw new ErreurUsage($"{nom} attendu{(i < args.Length ? $" avant « {args[i]} »" : "")}");

        /// <summary>Argument positionnel facultatif (null s'il manque ou si une option est à sa place).</summary>
        public string? ArgFacultatif(int i) => i < args.Length && !EstOption(args[i]) ? args[i] : null;

        /// <summary>Arguments à partir de la position <paramref name="i"/> (les options, le plus souvent).</summary>
        public string[] Depuis(int i) => i < args.Length ? args[i..] : [];

        /// <summary>Aucun argument après les <paramref name="n"/> premiers.</summary>
        public void Rien(int n)
        {
            if (args.Length > n) throw EstOption(args[n]) ? OptionInattendue(args[n], []) : new ErreurUsage($"argument inattendu « {args[n]} »");
        }
    }

    private static readonly string[] NomsOptionsGlobales = ["--json", "--quiet", "--verbose", "--non-interactif", "--config", "--help"];

    /// <summary>Option inconnue de la commande, avec l'option la plus proche (de la commande ou commune) s'il y en a une.</summary>
    private static ErreurUsage OptionInattendue(string option, IEnumerable<string> connues)
    {
        var candidates = connues.Concat(NomsOptionsGlobales).ToList();
        return new ErreurUsage($"option inattendue « {option} »" + (Suggestion(option, candidates, "") is { Length: > 0 }
            ? $" (vouliez-vous dire {candidates.OrderBy(c => Distance(option, c)).First()} ?)" : ""));
    }

    /// <summary>Commande mal formée (option inconnue, valeur manquante) : code de retour 2.</summary>
    private sealed class ErreurUsage(string message) : Exception(message)
    {
        /// <summary>Piste affichée après le message, à la place de l'usage de la commande.</summary>
        public string? Suite { get; init; }
    }

    /// <summary>Confirmation absente ou refusée : code de retour 4.</summary>
    private sealed class ConfirmationRequise(string message) : Exception(message);

    /// <summary>Options d'une commande, dans l'ordre donné (« --nom valeur » ou drapeau seul).</summary>
    private sealed class Options
    {
        private readonly List<(string Nom, string? Valeur)> _liste = [];

        public List<(string Nom, string? Valeur)> Liste => _liste;

        /// <summary>Lit les options ; toute option inconnue ou sans sa valeur est une erreur d'usage.</summary>
        public static Options Lire(string[] args, string[] avecValeur, params string[] drapeaux)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                if (avecValeur.Contains(args[i]))
                {
                    if (i + 1 >= args.Length) throw new ErreurUsage($"valeur manquante après {args[i]}");
                    o._liste.Add((args[i], args[++i]));
                }
                else if (drapeaux.Contains(args[i])) o._liste.Add((args[i], null));
                else if (args[i].StartsWith('-') && args[i].Length > 1)
                {
                    throw OptionInattendue(args[i], avecValeur.Concat(drapeaux));
                }
                else throw new ErreurUsage($"argument inattendu « {args[i]} »");
            }
            return o;
        }

        public bool A(string nom) => _liste.Any(x => x.Nom == nom);

        /// <summary>Dernière valeur donnée pour l'option (null si absente).</summary>
        public string? Valeur(string nom) => _liste.LastOrDefault(x => x.Nom == nom).Valeur;

        public bool? OuiNon(string nom) => Valeur(nom) switch
        {
            null => null,
            "oui" => true,
            "non" => false,
            _ => throw ErreurHttp.Saisie($"{nom} attend oui ou non."),
        };

        public int? Entier(string nom) => Valeur(nom) is not { } v ? null
            : int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
            : throw ErreurHttp.Saisie($"{nom} attend un nombre entier.");
    }

    /// <summary>
    /// Confirmation d'une opération destructive : <c>--oui</c> suffit. Sinon, au terminal, la question est posée (réponse
    /// « oui » attendue) ; hors terminal (script, tube, <c>--non-interactif</c>, <c>--json</c>), la commande échoue avec le
    /// code 4 et la commande complète à relancer.
    /// </summary>
    private static void Confirmer(bool oui, IEnumerable<string> explication, string commandeAvecOui)
    {
        if (oui) return;
        foreach (var ligne in explication) Console.Error.WriteLine(ligne);
        if (S.NonInteractif)
            throw new ConfirmationRequise($"Confirmation requise : relancer avec --oui ({commandeAvecOui}). Rien n'a été modifié.");
        Console.Error.Write("Confirmer ? Taper « oui » pour continuer : ");
        var reponse = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (reponse is not ("oui" or "o")) throw new ConfirmationRequise("Abandon : rien n'a été modifié.");
    }

    // ---------- Configuration et base ----------

    /// <summary>
    /// Même configuration que l'application, dans le même ordre (la dernière source l'emporte) : appsettings, variables
    /// d'environnement, fichier de configuration (installation sans Docker), puis réglages modifiés en base.
    /// </summary>
    private static IConfiguration Configuration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .AjouterFichierInstallation();
        var config = builder.Add(new SourceReglagesBase(builder.Build().GetConnectionString("Mep"))).Build();
        if (S.Bavard)
        {
            var fichier = FichierInstallation.Chemin;
            Detailler($"Fichier de configuration : {fichier} ({(File.Exists(fichier) ? "lu" : "absent")}).");
            Detailler($"Base : {DecrireBase(config.GetConnectionString("Mep"))}.");
        }
        return config;
    }

    /// <summary>Serveur, base et compte de la chaîne de connexion, sans le mot de passe.</summary>
    private static string DecrireBase(string? chaine)
    {
        if (chaine is null) return "chaîne de connexion absente";
        try
        {
            var c = new Npgsql.NpgsqlConnectionStringBuilder(chaine);
            return $"{c.Host}:{c.Port}/{c.Database} (compte {c.Username}, mot de passe {(string.IsNullOrEmpty(c.Password) ? "non défini" : "masqué")})";
        }
        catch (ArgumentException)
        {
            return "chaîne de connexion illisible";
        }
    }

    private static MepDbContext Base(IConfiguration config)
    {
        var chaine = config.GetConnectionString("Mep")
            ?? throw new ConfigurationIllisibleException($"Chaîne de connexion à la base absente : BASE_DE_DONNEES dans le {Installation.NomFichier} "
                + "(variable d'environnement ConnectionStrings__Mep).");
        return new MepDbContext(new DbContextOptionsBuilder<MepDbContext>().UseNpgsql(chaine).Options);
    }

    private static Npgsql.NpgsqlException? BaseInjoignable(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is Npgsql.NpgsqlException n and not Npgsql.PostgresException) return n;
        return null;
    }

    // ---------- Outils communs ----------

    private static string Date(DateTimeOffset d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Texte(string valeur) => valeur.Length == 0 ? "(vide)" : valeur;

    /// <summary>Contenu d'un fichier, ou de l'entrée standard pour « - ».</summary>
    private static async Task<string> LireFichierOuEntree(string fichier)
    {
        if (fichier == "-") return await Console.In.ReadToEndAsync();
        if (!File.Exists(fichier)) throw ErreurHttp.NonTrouve($"Fichier introuvable : {fichier}");
        return await File.ReadAllTextAsync(fichier);
    }

    /// <summary>Écrit le JSON dans le fichier, ou sur la sortie standard (absent ou « - ») ; renvoie vrai pour un fichier.</summary>
    private static async Task<bool> EcrireFichierOuSortie(string? fichier, object valeur)
    {
        var json = JsonSerializer.Serialize(valeur, OptionsJson);
        if (fichier is null or "-")
        {
            S.DonneesEcrites = true;
            Console.WriteLine(json);
            return false;
        }
        if (Path.GetDirectoryName(Path.GetFullPath(fichier)) is { } dossier) Directory.CreateDirectory(dossier);
        await File.WriteAllTextAsync(fichier, json + "\n");
        return true;
    }

    /// <summary>« 8 premiers caractères » ou plus d'un identifiant : un seul élément doit correspondre.</summary>
    private static Guid? IdentifiantOuPrefixe(string texte, IEnumerable<Guid> ids, string quoi)
    {
        if (Guid.TryParse(texte, out var id)) return id;
        var t = texte.Trim().ToLowerInvariant();
        if (t.Length < 4 || !t.All(Uri.IsHexDigit)) return null;
        var trouves = ids.Where(i => i.ToString().StartsWith(t, StringComparison.Ordinal)).ToList();
        return trouves.Count switch
        {
            0 => null,
            1 => trouves[0],
            _ => throw ErreurHttp.Saisie($"Plusieurs {quoi} commencent par « {texte} » : donner l'identifiant complet."),
        };
    }
}
