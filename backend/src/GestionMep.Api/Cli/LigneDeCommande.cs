using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// Commandes d'exploitation et d'administration, sans navigateur, pour l'équipe informatique. Dans le conteneur :
/// <c>docker compose exec app gestion-mep &lt;commande&gt;</c>. Elles lisent la même configuration que l'application
/// (variables d'environnement, appsettings, réglages en base), appliquent les mêmes règles que la console (méthodes
/// partagées des Endpoints) et inscrivent leurs modifications dans le journal d'administration (auteur « Ligne de commande »).
/// </summary>
public static partial class LigneDeCommande
{
    private static readonly string[] Commandes =
    [
        "aide", "--help", "-h", "version", "--version", "migrer", "config", "comptes", "sauvegarder", "importer", "sso", "reglages", "reglage",
        "systemes", "categories", "criteres", "resolutions", "journal", "sauvegardes", "mettre-a-jour", "meps", "demo", "equipes", "charte",
    ];

    private const string Aide = """
        gestion-mep : commandes d'exploitation et d'administration

        Comptes
          gestion-mep comptes lister                          Liste les comptes (login, nom, admin, état)
          gestion-mep comptes afficher <login>                Détail d'un compte : rôle, contact, expertises
          gestion-mep comptes creer <login> [--nom "Nom"] [--admin] [--role "Rôle"] [--contact "Contact"]
                                                              Crée un compte avec un mot de passe provisoire
          gestion-mep comptes modifier <login> [options]      Modifie un compte (seules les options données changent) :
              --nom "Nom"                  Nom affiché (ne peut pas imiter le nom ou le login d'un autre compte)
              --admin oui|non              Rôle administrateur (le dernier administrateur actif le garde)
              --role "Rôle"  --contact "Contact"
              --ajouter-expertise <SYSTÈME> [--detail "texte"]   Ajoute (ou met à jour) une expertise sur un système du catalogue
              --retirer-expertise <SYSTÈME>                      Retire une expertise
          gestion-mep comptes expertises <login>              Expertises du compte
          gestion-mep comptes reinitialiser-mdp <login>       Nouveau mot de passe provisoire (à changer à la connexion)
          gestion-mep comptes desactiver <login>              Désactive le compte et ferme ses sessions
          gestion-mep comptes reactiver <login>               Réactive le compte
          gestion-mep comptes relier <login> <login-principal> [--desactiver]
                                                              Relie deux comptes d'une même personne : elle s'affiche sous le
                                                              nom du compte principal, rien n'est fusionné ni supprimé ;
                                                              --desactiver : désactive aussi le compte relié
          gestion-mep comptes delier <login>                  Défait la liaison (le compte reprend son propre nom)

        Équipes (une personne peut appartenir à plusieurs équipes)
          gestion-mep equipes lister                          Équipes et nombre de membres
          gestion-mep equipes afficher <équipe>               Description et membres d'une équipe
          gestion-mep equipes creer "<nom>" [--description "…"]
          gestion-mep equipes modifier <équipe> [--nom "…"] [--description "…"]
          gestion-mep equipes ajouter <équipe> <login> [<login>…]   Ajoute des comptes à l'équipe
          gestion-mep equipes retirer <équipe> <login> [<login>…]   Retire des comptes de l'équipe
          gestion-mep equipes supprimer <équipe>              Supprime l'équipe (comptes et MEP inchangés)

        Catalogue des systèmes, go / no-go et réglages
          gestion-mep systemes lister                         Systèmes, environnements (* production) et nombre d'objets
          gestion-mep systemes afficher <SYSTÈME>             Fiche : environnements, catégories et objets
          gestion-mep systemes historique <SYSTÈME> [--excel fichier.xlsx]
                                                              MEP qui ont touché le système : date, issue, échecs d'étape
          gestion-mep systemes ajouter <nom> [--description "…"] [--cible "…"] [--actif oui|non]
          gestion-mep systemes modifier <nom> [--nom "…"] [--description "…"] [--cible "…"] [--actif oui|non]
          gestion-mep systemes supprimer <nom>                Supprime, ou désactive s'il est utilisé (MEP, résolution type)
          gestion-mep systemes environnement <SYSTÈME> <nom> [--cible "…"] [--production oui|non] [--retirer]
                                                              Ajoute, modifie ou retire un environnement de la fiche
          gestion-mep systemes categorie <SYSTÈME> "<catégorie>" [--renommer "…"] [--retirer]
                                                              Ajoute, renomme ou retire (avec ses objets) une catégorie
          gestion-mep systemes objet <SYSTÈME> "<catégorie>" "<nom>" [--type "…"] [--libelle "…"] [--precision "…"] [--retirer]
                                                              Ajoute, modifie ou retire un objet de la fiche
          gestion-mep categories lister | ajouter "<nom>" | retirer "<nom>"
                                                              Catégories d'objets proposées par défaut à chaque système
          gestion-mep criteres lister                         Critères go / no-go, numérotés (n°)
          gestion-mep criteres ajouter "<libellé>" [--ordre n] [--actif oui|non]
          gestion-mep criteres modifier <n°|id> [--libelle "…"] [--ordre n] [--actif oui|non]
          gestion-mep criteres supprimer <n°|id>
          gestion-mep reglages lister                         Réglages d'exploitation : valeur, origine (console, .env, défaut)
          gestion-mep reglages definir <nom> <valeur>         Modifie un réglage, appliqué sans redémarrage (sous 10 secondes) ;
                                                              la valeur remplace celle du fichier .env
                                                              (dont approbations-minimum : approbations exigées avant le Go)
          gestion-mep reglages retablir <nom>                 Revient à la valeur du fichier .env (ou à la valeur par défaut)

        Charte visuelle (couleurs de l'interface et des exports Excel ; console > fichier charte.json > charte par défaut)
          gestion-mep charte afficher                         Charte en vigueur, origine (console, fichier, défaut) et lisibilité
          gestion-mep charte exporter [fichier.json|-]        Charte en vigueur au format du fichier (défaut : sortie standard)
          gestion-mep charte importer <fichier.json|-> [--essai]
                                                              Remplace la charte de la console ; --essai : contrôle sans écrire
          gestion-mep charte verifier [fichier.json]          Valide un fichier (ou la charte en vigueur et le fichier charte.json) :
                                                              format et lisibilité ; code de retour 1 en cas d'erreur
          gestion-mep charte definir <rôle> <RRGGBB>          Rôles : primaire, secondaire, tertiaire (ou « primaire » pour la
                                                              lier), succes, avertissement, erreur, info
          gestion-mep charte retablir                         Efface la charte de la console : retour au fichier, sinon défaut

        Résolutions types
          gestion-mep resolutions lister [--systeme <SYSTÈME>] [--inactives]
          gestion-mep resolutions afficher <id|titre>
          gestion-mep resolutions supprimer <id|titre>        Supprime, ou désactive si elle a déjà servi
          gestion-mep resolutions exporter [fichier.json|-]   Résolutions actives en JSON (défaut : sortie standard)
          gestion-mep resolutions importer <fichier.json|-> [--remplacer|--ignorer-existantes] [--essai]
                                                              Crée les résolutions d'un export ; une résolution du même titre
                                                              est refusée, remplacée ou ignorée ; --essai : contrôle sans écrire

        MEP et journal
          gestion-mep meps lister [--recherche "…"] [--systeme <SYSTÈME,…>] [--environnement <env,…>] [--echec]
                                  [--equipe <équipe,…>] [--personne <login> [--role <rôle,…>]] [--etat <état,…>]
                                  [--du AAAA-MM-JJ] [--au AAAA-MM-JJ] [--type complete|simplifiee] [--phase <phase>]
                                  [--modeles] [--excel fichier.xlsx]
                                                              Mêmes filtres que la page d'accueil, les plus récentes d'abord.
                                                              États : preparation, execution, recette, retour-arriere, reussie,
                                                              incidents, repliee ; rôles : pilote, suppleant, responsable,
                                                              executant, approbateur ; --echec : un échec d'étape survenu (sur
                                                              les systèmes filtrés) ; période sur le jour du Go (à défaut, de
                                                              la création) ; --excel : la liste filtrée et ses échecs en Excel
          gestion-mep meps exporter <id> [fichier.json|-]     MEP complète et son journal, en JSON (archive, audit)
          gestion-mep meps excel <id> <vue> [fichier.xlsx]    Une vue de la MEP en classeur Excel : preparation, etapes,
                                                              retour-arriere, recette, objets, journal, ou rapport (rapport
                                                              de fin d'une MEP clôturée, figé à la clôture)
          gestion-mep journal [--depuis AAAA-MM-JJ] [--limite n]
                                                              Journal d'administration (200 derniers événements par défaut)

        Connexion unique
          gestion-mep sso afficher                            Réglages de la connexion unique (Microsoft Entra ID ou OpenID Connect)
          gestion-mep sso configurer [options]                Modifie les réglages (seules les options données changent) :
              --annuaire <id>          Microsoft : ID de l'annuaire (locataire), GUID ou domaine
              --emetteur <url>         Autre fournisseur OpenID Connect : adresse https de l'émetteur
              --client <id>            ID d'application (client)
              --secret-stdin           Lit le secret client sur l'entrée standard (ou le demande sans l'afficher)
              --expiration <date|aucune>   Date d'expiration du secret (AAAA-MM-JJ), pour être prévenu à temps
              --role-admin <valeur|aucun>  Rôle d'application du fournisseur qui donne le rôle administrateur
              --creation-auto <oui|non>    Crée le compte à la première connexion (défaut oui)
              --libelle "texte"        Texte du bouton de la page de connexion
          gestion-mep sso tester                              Vérifie que le fournisseur reconnaît l'application et le secret
          gestion-mep sso activer | desactiver                Affiche ou retire le bouton de connexion unique (sans redémarrage)
          gestion-mep sso comptes                             Comptes reliés à un compte du fournisseur
          gestion-mep sso delier <login>                      Supprime la liaison d'un compte

        Exploitation
          gestion-mep config afficher                         Configuration lue au démarrage (secrets masqués)
          gestion-mep config verifier                         Vérifie base, migrations, dossiers, comptes admin
          gestion-mep migrer                                  Applique les migrations de la base sans démarrer l'application
                                                              (sauvegarde préalable, supprimée si les contrôles passent)
          gestion-mep sauvegarder [fichier.tar.gz]            Archive la base et les pièces jointes (défaut : dossier des sauvegardes)
          gestion-mep sauvegardes lister                      Archives du dossier des sauvegardes
          gestion-mep sauvegardes restaurer <archive> --oui   Remplace base et pièces jointes par l'archive (nom dans le dossier
                                                              des sauvegardes ou chemin) ; application arrêtée, voir ci-dessous
          gestion-mep importer <fichier.tar.gz>               Comme « sauvegardes restaurer », sans confirmation (déménagement)
          gestion-mep mettre-a-jour [--oui] [--version x.y.z] Dernière version publiée ; avec --oui, demande son installation au
                                                              service de mise à jour (refusée pendant une MEP en cours)
          gestion-mep version                                 Version de l'application

        Mode démonstration (DEMO=true uniquement)
          gestion-mep demo reinitialiser --oui                Remet la base dans l'état initial de la démonstration (comptes, MEP,
                                                              catalogue, réglages, journaux ; pièces jointes supprimées) ;
                                                              les sessions ouvertes sont fermées. Refusée hors démonstration.

        Option --json : sortie JSON pour les scripts (commandes lister, afficher, expertises, journal, mettre-a-jour, charte afficher).
        Une équipe se désigne par son nom (guillemets s'il contient des espaces) ou son identifiant.
        Exemples : gestion-mep reglages definir url-publique https://mep.entreprise.fr
                   gestion-mep reglages definir lien-signalement ""      (valeur vide : lien masqué)
                   gestion-mep comptes modifier jdupont --ajouter-expertise BDD --detail "Base clients"
                   gestion-mep charte definir primaire 0F766E           (« #0F766E » entre guillemets dans un shell)
        Codes de retour : 0 succès, 1 échec, 2 commande mal formée.
        """;

    /// <summary>Fin de l'aide propre au type d'installation : comment lancer une commande, restaurer, passer un fichier.</summary>
    private static string AideInstallation() => Installation.Mode switch
    {
        Installation.Docker => """
            Dans Docker : docker compose exec app gestion-mep <commande>
            Restaurer (application arrêtée) : docker compose stop app
                                              docker compose run --rm app gestion-mep sauvegardes restaurer <archive> --oui
                                              docker compose up -d app
            Secret depuis un fichier : docker compose exec -T app gestion-mep sso configurer --secret-stdin < secret.txt
            Fichier depuis l'hôte : docker compose exec -T app gestion-mep resolutions importer - < resolutions.json
            Charte depuis l'hôte : docker compose exec -T app gestion-mep charte importer - < charte.json
            """,
        Installation.Linux => $"""
            Installation sans Docker (Linux) : sudo gestion-mep <commande> ; configuration : {FichierInstallation.Chemin}
            Restaurer (application arrêtée) : sudo systemctl stop gestion-mep
                                              sudo gestion-mep sauvegardes restaurer <archive> --oui
                                              sudo systemctl start gestion-mep
            Secret depuis un fichier : sudo gestion-mep sso configurer --secret-stdin < secret.txt
            """,
        _ => $"""
            Installation sans Docker (Windows, PowerShell administrateur) : gestion-mep <commande> ; configuration : {FichierInstallation.Chemin}
            Restaurer (application arrêtée) : Stop-Service GestionMep
                                              gestion-mep sauvegardes restaurer <archive> --oui
                                              Start-Service GestionMep
            Secret depuis un fichier : Get-Content secret.txt | gestion-mep sso configurer --secret-stdin
            """,
    };

    /// <summary>Sortie JSON demandée (option --json), pour la commande en cours.</summary>
    private static readonly AsyncLocal<bool> SortieJson = new();

    private static bool EnJson => SortieJson.Value;

    /// <summary>Même forme que les réponses de l'API (camelCase, énumérations en texte), lisible et sans échappement des accents.</summary>
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static bool EstCommande(string[] args) => args.FirstOrDefault(a => a != "--json") is { } c && Commandes.Contains(c);

    public static async Task<int> Executer(string[] arguments)
    {
        SortieJson.Value = arguments.Contains("--json");
        var args = arguments.Where(a => a != "--json").ToArray();
        try
        {
            return args switch
            {
                ["version" or "--version"] => Version(),
                ["migrer"] => await Migrer(),
                ["sauvegarder"] => await Sauvegarder(null),
                ["sauvegarder", var fichier] when !fichier.StartsWith('-') => await Sauvegarder(fichier),
                ["importer", var archive] when !archive.StartsWith('-') => await Importer(archive),
                ["sauvegardes", "lister"] => await SauvegardesLister(),
                ["sauvegardes", "restaurer", .. var options] => await SauvegardesRestaurer(options),
                ["config", "verifier"] => await VerifierConfig(),
                ["config", "afficher"] => await ConfigAfficher(),
                ["mettre-a-jour", .. var options] => await MettreAJour(options),
                ["comptes", "lister"] => await Lister(),
                ["comptes", "afficher", var login] => await AfficherCompte(login),
                ["comptes", "creer", var login, .. var options] => await Creer(login, options),
                ["comptes", "modifier", var login, .. var options] when options.Length > 0 => await ModifierCompte(login, options),
                ["comptes", "expertises", var login] => await Expertises(login),
                ["comptes", "reinitialiser-mdp", var login] => await Reinitialiser(login),
                ["comptes", "desactiver", var login] => await ChangerEtat(login, actif: false),
                ["comptes", "reactiver", var login] => await ChangerEtat(login, actif: true),
                ["comptes", "relier", var login, var principal, .. var options] => await Relier(login, principal, options),
                ["comptes", "delier", var login] => await Delier(login),
                ["equipes", "lister"] => await EquipesLister(),
                ["equipes", "afficher", var equipe] => await EquipesAfficher(equipe),
                ["equipes", "creer", var nom, .. var options] => await EquipesCreer(nom, options),
                ["equipes", "modifier", var equipe, .. var options] when options.Length > 0 => await EquipesModifier(equipe, options),
                ["equipes", "ajouter", var equipe, .. var logins] when logins.Length > 0 => await EquipesMembres(equipe, logins, ajouter: true),
                ["equipes", "retirer", var equipe, .. var logins] when logins.Length > 0 => await EquipesMembres(equipe, logins, ajouter: false),
                ["equipes", "supprimer", var equipe] => await EquipesSupprimer(equipe),
                ["systemes", "lister"] => await SystemesLister(),
                ["systemes", "ajouter", var nom, .. var options] => await SystemesAjouter(nom, options),
                ["systemes", "modifier", var nom, .. var options] when options.Length > 0 => await SystemesModifier(nom, options),
                ["systemes", "supprimer", var nom] => await SystemesSupprimer(nom),
                ["systemes", "afficher", var nom] => await SystemesAfficher(nom),
                ["systemes", "historique", var nom, .. var options] => await SystemesHistorique(nom, options),
                ["systemes", "environnement", var nom, var env, .. var options] => await SystemesEnvironnement(nom, env, options),
                ["systemes", "categorie", var nom, var categorie, .. var options] => await SystemesCategorie(nom, categorie, options),
                ["systemes", "objet", var nom, var categorie, var objet, .. var options] => await SystemesObjet(nom, categorie, objet, options),
                ["categories", "lister"] => await CategoriesLister(),
                ["categories", "ajouter", var categorie] => await CategoriesModifier(categorie, ajouter: true),
                ["categories", "retirer", var categorie] => await CategoriesModifier(categorie, ajouter: false),
                ["criteres", "lister"] => await CriteresLister(),
                ["criteres", "ajouter", var libelle, .. var options] => await CriteresAjouter(libelle, options),
                ["criteres", "modifier", var critere, .. var options] when options.Length > 0 => await CriteresModifier(critere, options),
                ["criteres", "supprimer", var critere] => await CriteresSupprimer(critere),
                ["resolutions", "lister", .. var options] => await ResolutionsLister(options),
                ["resolutions", "afficher", var resolution] => await ResolutionsAfficher(resolution),
                ["resolutions", "supprimer", var resolution] => await ResolutionsSupprimer(resolution),
                ["resolutions", "exporter"] => await ResolutionsExporter(null),
                ["resolutions", "exporter", var fichier] => await ResolutionsExporter(fichier),
                ["resolutions", "importer", var fichier, .. var options] => await ResolutionsImporter(fichier, options),
                ["meps", "lister", .. var options] => await MepsLister(options),
                ["meps", "exporter", var mep] => await MepsExporter(mep, null),
                ["meps", "exporter", var mep, var fichier] => await MepsExporter(mep, fichier),
                ["meps", "excel", var mep, var vue] => await MepsExcel(mep, vue, null),
                ["meps", "excel", var mep, var vue, var fichier] => await MepsExcel(mep, vue, fichier),
                ["journal", .. var options] => await Journal(options),
                ["demo", "reinitialiser", .. var options] => await DemoReinitialiser(options),
                ["sso", "afficher"] => await SsoAfficher(),
                ["sso", "configurer", .. var options] when options.Length > 0 => await SsoConfigurer(options),
                ["sso", "tester"] => await SsoTester(),
                ["sso", "activer"] => await SsoChangerEtat(true),
                ["sso", "desactiver"] => await SsoChangerEtat(false),
                ["sso", "comptes"] => await SsoComptes(),
                ["sso", "delier", var login] => await SsoDelier(login),
                ["reglages" or "reglage", "lister"] => await ReglagesLister(),
                ["reglages" or "reglage", "definir", var nom, var valeur] => await ReglagesDefinir(nom, valeur),
                ["reglages" or "reglage", "retablir", var nom] => await ReglagesRetablir(nom),
                ["charte", "afficher"] => await CharteAfficher(),
                ["charte", "exporter"] => await CharteExporter(null),
                ["charte", "exporter", var fichier] => await CharteExporter(fichier),
                ["charte", "importer", var fichier, .. var options] => await CharteImporter(fichier, options),
                ["charte", "verifier"] => await CharteVerifier(null),
                ["charte", "verifier", var fichier] => await CharteVerifier(fichier),
                ["charte", "definir", var role, var couleur] => await CharteDefinir(role, couleur),
                ["charte", "retablir"] => await CharteRetablir(),
                ["aide" or "--help" or "-h"] => AfficherAide(0),
                _ => AfficherAide(2),
            };
        }
        catch (ErreurUsage e)
        {
            Console.Error.WriteLine($"Commande mal formée : {e.Message} (« gestion-mep aide » pour la liste des commandes).");
            return 2;
        }
        catch (ErreurHttp e)
        {
            if (e.Erreurs is { Count: > 1 } erreurs)
            {
                Console.Error.WriteLine("Erreur :");
                foreach (var m in erreurs) Console.Error.WriteLine($"  - {m}");
            }
            else Console.Error.WriteLine($"Erreur : {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is RegleMetierException or ConfigurationIllisibleException)
        {
            Console.Error.WriteLine($"Erreur : {e.Message}");
            return 1;
        }
        catch (Exception e) when (BaseInjoignable(e) is { } cause)
        {
            // Base arrêtée ou mal renseignée : un message clair plutôt qu'une pile d'appels.
            Console.Error.WriteLine($"Erreur : base de données injoignable ({cause.Message.TrimEnd('.')}). Vérifier que PostgreSQL tourne "
                + $"et la connexion à la base dans le {Installation.NomFichier}.");
            return 1;
        }
    }

    private static int AfficherAide(int code)
    {
        (code == 0 ? Console.Out : Console.Error).WriteLine(Aide + Environment.NewLine + AideInstallation());
        return code;
    }

    /// <summary>Même configuration que l'application : fichiers, variables d'environnement, puis réglages modifiés en base.</summary>
    private static IConfiguration Configuration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .AjouterFichierInstallation();
        return builder.Add(new SourceReglagesBase(builder.Build().GetConnectionString("Mep"))).Build();
    }

    private static MepDbContext Base(IConfiguration config)
    {
        var chaine = config.GetConnectionString("Mep")
            ?? throw new ErreurHttp(1, "Chaîne de connexion ConnectionStrings:Mep absente (variable d'environnement ConnectionStrings__Mep).");
        return new MepDbContext(new DbContextOptionsBuilder<MepDbContext>().UseNpgsql(chaine).Options);
    }

    private static int Version()
    {
        var a = typeof(LigneDeCommande).Assembly;
        Console.WriteLine(a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString());
        return 0;
    }

    // ---------- Outils communs ----------

    /// <summary>Commande mal formée (option inconnue, valeur manquante) : code de retour 2.</summary>
    private static Npgsql.NpgsqlException? BaseInjoignable(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is Npgsql.NpgsqlException n and not Npgsql.PostgresException) return n;
        return null;
    }

    private sealed class ErreurUsage(string message) : Exception(message);

    /// <summary>Options d'une commande, dans l'ordre donné (« --nom valeur » ou drapeau seul).</summary>
    private sealed class Options
    {
        private readonly List<(string Nom, string? Valeur)> _liste = [];

        public IReadOnlyList<(string Nom, string? Valeur)> Liste => _liste;

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
                else throw new ErreurUsage($"option inattendue « {args[i]} »");
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

    private static void EcrireJson(object valeur) => Console.WriteLine(JsonSerializer.Serialize(valeur, OptionsJson));

    private static string Date(DateTimeOffset d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Contenu d'un fichier, ou de l'entrée standard pour « - ».</summary>
    private static async Task<string> LireFichierOuEntree(string fichier)
    {
        if (fichier == "-") return await Console.In.ReadToEndAsync();
        if (!File.Exists(fichier)) throw new ErreurHttp(1, $"Fichier introuvable : {fichier}");
        return await File.ReadAllTextAsync(fichier);
    }

    /// <summary>Écrit le JSON dans le fichier, ou sur la sortie standard (absent ou « - ») ; renvoie vrai pour un fichier.</summary>
    private static async Task<bool> EcrireFichierOuSortie(string? fichier, object valeur)
    {
        var json = JsonSerializer.Serialize(valeur, OptionsJson);
        if (fichier is null or "-")
        {
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

    // ---------- Exploitation ----------

    private static async Task<int> Migrer()
    {
        var config = Configuration();
        await using var db = Base(config);
        var enAttente = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (enAttente.Count == 0)
        {
            Console.WriteLine("Base à jour : aucune migration à appliquer.");
            return 0;
        }
        var ok = await ServiceSauvegardes(config).MigrerAvecSauvegarde(db, JournalAdmin.AuteurLigneDeCommande);
        Console.WriteLine($"{enAttente.Count} migration(s) appliquée(s) : {string.Join(", ", enAttente)}.");
        return ok ? 0 : 1;
    }

    private static Sauvegardes ServiceSauvegardes(IConfiguration config) =>
        new(Sauvegardes.DossierConfigure(config), new StockagePiecesJointes(config).Dossier,
            Console.WriteLine, m => Console.Error.WriteLine($"Erreur : {m}"));

    private static async Task<int> Sauvegarder(string? fichier)
    {
        var config = Configuration();
        await using var db = Base(config);
        var sauvegardes = ServiceSauvegardes(config);
        await sauvegardes.Exporter(db, fichier ?? Path.Combine(sauvegardes.Dossier, $"sauvegarde-{Sauvegardes.Horodatage()}.tar.gz"));
        return 0;
    }

    private static async Task<int> Importer(string archive)
    {
        var config = Configuration();
        await using var db = Base(config);
        return await ServiceSauvegardes(config).Importer(db, archive, JournalAdmin.AuteurLigneDeCommande) ? 0 : 1;
    }


    // ---------- Comptes ----------

    private static async Task<Utilisateur> Compte(MepDbContext db, string login)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        return await db.Utilisateurs.FirstOrDefaultAsync(u => u.Login == l) ?? throw new ErreurHttp(1, $"Compte « {l} » introuvable.");
    }

    private static async Task<int> Lister()
    {
        await using var db = Base(Configuration());
        var comptes = await db.Utilisateurs.AsNoTracking().OrderBy(u => u.Login).ToListAsync();
        var annuaire = await Personnes.Charger(db);
        if (EnJson)
        {
            EcrireJson(comptes.Select(u => Personnes.Dto(u, annuaire)));
            return 0;
        }
        var logins = comptes.ToDictionary(u => u.Id, u => u.Login);
        var largeur = Math.Max(5, comptes.Count == 0 ? 0 : comptes.Max(u => u.Login.Length));
        Console.WriteLine($"{"LOGIN".PadRight(largeur)}  ADMIN  ÉTAT        NOM");
        foreach (var u in comptes)
        {
            var etat = !u.Actif ? "désactivé" : u.DoitChangerMotDePasse ? "provisoire" : "actif";
            var relie = u.ComptePrincipalId is { } p && logins.TryGetValue(p, out var lp) ? $" (relié à {lp})" : "";
            Console.WriteLine($"{u.Login.PadRight(largeur)}  {(u.EstAdmin ? "oui" : "non"),-5}  {etat,-10}  {annuaire.NomPersonne(u.Id) ?? u.NomAffiche}{relie}");
        }
        return 0;
    }

    private static async Task<int> AfficherCompte(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var annuaire = await Personnes.Charger(db);
        if (EnJson)
        {
            EcrireJson(Personnes.Dto(u, annuaire));
            return 0;
        }
        Console.WriteLine($"Login          : {u.Login}");
        Console.WriteLine($"Nom affiché    : {u.NomAffiche}");
        var autres = annuaire.ComptesDeLaPersonne(u.Id).Where(c => c.Id != u.Id).ToList();
        if (u.ComptePrincipalId is not null && autres.Count > 0)
            Console.WriteLine($"Relié à        : {autres[0].NomAffiche} ({autres[0].Login}), compte principal : affiché sous ce nom");
        else if (autres.Count > 0)
            Console.WriteLine($"Comptes reliés : {string.Join(", ", autres.Select(c => c.Login + (c.Actif ? "" : " (désactivé)")))}");
        var equipes = (await db.Equipes.AsNoTracking().ToListAsync()).Where(e => e.MembreIds.Contains(u.Id)).Select(e => e.Nom).Order().ToList();
        Console.WriteLine($"Équipes        : {(equipes.Count == 0 ? "aucune" : string.Join(", ", equipes))}");
        Console.WriteLine($"Administrateur : {(u.EstAdmin ? "oui" : "non")}");
        Console.WriteLine($"État           : {(!u.Actif ? "désactivé" : u.DoitChangerMotDePasse ? "actif, mot de passe provisoire à changer" : "actif")}");
        Console.WriteLine($"Rôle           : {Texte(u.Role)}");
        Console.WriteLine($"Contact        : {Texte(u.Contact)}");
        await EcrireExpertises(db, u);
        return 0;
    }

    private static async Task EcrireExpertises(MepDbContext db, Utilisateur u)
    {
        if (u.Expertises.Count == 0)
        {
            Console.WriteLine("Expertises     : aucune");
            return;
        }
        var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
        Console.WriteLine("Expertises     :");
        foreach (var e in u.Expertises)
            Console.WriteLine($"  {noms.GetValueOrDefault(e.SystemeCatalogueId, "(système supprimé)")}{(e.Detail.Length > 0 ? $" : {e.Detail}" : "")}");
    }

    private static async Task<int> Expertises(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        if (EnJson)
        {
            var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
            EcrireJson(u.Expertises.Select(e => new { e.SystemeCatalogueId, Systeme = noms.GetValueOrDefault(e.SystemeCatalogueId), e.Detail }));
            return 0;
        }
        await EcrireExpertises(db, u);
        return 0;
    }

    private static async Task<int> Creer(string login, string[] arguments)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        if (l.Length == 0 || l.StartsWith('-')) return AfficherAide(2);
        var o = Options.Lire(arguments, ["--nom", "--role", "--contact"], "--admin");
        var nom = o.Valeur("--nom")?.Trim();
        var admin = o.A("--admin");
        await using var db = Base(Configuration());
        if (await db.Utilisateurs.AnyAsync(u => u.Login == l)) throw new ErreurHttp(1, $"Le compte « {l} » existe déjà.");
        var (_, mdp) = await AdminEndpoints.Creer(db, null,
            new CompteSaisie(l, string.IsNullOrEmpty(nom) ? l : nom, admin, o.Valeur("--role"), o.Valeur("--contact")), new PasswordHasher<Utilisateur>());
        Console.WriteLine($"Compte « {l} » créé{(admin ? " (administrateur)" : "")}.");
        Console.WriteLine($"Mot de passe provisoire : {mdp}");
        Console.WriteLine("À transmettre par un canal sûr ; il devra être changé à la première connexion.");
        return 0;
    }

    /// <summary>
    /// Nom, rôle administrateur, rôle et contact (mêmes règles que la console), puis expertises (mêmes règles que le profil) :
    /// « --detail » s'applique au « --ajouter-expertise » qui le précède.
    /// </summary>
    private static async Task<int> ModifierCompte(string login, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--nom", "--admin", "--role", "--contact", "--ajouter-expertise", "--detail", "--retirer-expertise"]);
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var resultats = new List<string>();

        if (o.A("--nom") || o.A("--admin") || o.A("--role") || o.A("--contact"))
        {
            var changements = await AdminEndpoints.Modifier(db, null, u, new CompteModification(
                o.Valeur("--nom") ?? u.NomAffiche, o.OuiNon("--admin") ?? u.EstAdmin, o.Valeur("--role") ?? u.Role, o.Valeur("--contact") ?? u.Contact));
            resultats.AddRange(changements);
        }

        if (o.A("--ajouter-expertise") || o.A("--retirer-expertise") || o.A("--detail"))
        {
            var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
            var expertises = u.Expertises.Select(e => new ExpertiseSaisie(e.SystemeCatalogueId, e.Detail)).ToList();
            var details = new List<string>();
            ExpertiseSaisie? derniere = null;
            foreach (var (nom, valeur) in o.Liste)
            {
                switch (nom)
                {
                    case "--ajouter-expertise":
                        var ajout = TrouverSysteme(catalogue, valeur!);
                        var existante = expertises.FindIndex(e => e.SystemeCatalogueId == ajout.Id);
                        derniere = new ExpertiseSaisie(ajout.Id, existante >= 0 ? expertises[existante].Detail : "");
                        if (existante >= 0) expertises[existante] = derniere;
                        else expertises.Add(derniere);
                        details.Add($"expertise {ajout.Nom} {(existante >= 0 ? "mise à jour" : "ajoutée")}");
                        break;
                    case "--detail":
                        if (derniere is null) throw new ErreurUsage("--detail suit un --ajouter-expertise");
                        var i = expertises.IndexOf(derniere);
                        expertises[i] = derniere = derniere with { Detail = valeur };
                        break;
                    case "--retirer-expertise":
                        var retrait = TrouverSysteme(catalogue, valeur!);
                        if (expertises.RemoveAll(e => e.SystemeCatalogueId == retrait.Id) == 0)
                            throw ErreurHttp.Saisie($"« {u.Login} » n'a pas d'expertise sur {retrait.Nom}.");
                        details.Add($"expertise {retrait.Nom} retirée");
                        derniere = null;
                        break;
                }
            }
            await AuthEndpoints.ModifierProfil(db, null, u, new ProfilSaisie(null, u.Role, u.Contact, expertises), string.Join(", ", details));
            resultats.AddRange(details);
        }

        Console.WriteLine(resultats.Count == 0 ? $"Compte « {u.Login} » : aucun changement." : $"Compte « {u.Login} » modifié : {string.Join(", ", resultats)}.");
        return 0;
    }

    private static async Task<int> Reinitialiser(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var mdp = await AdminEndpoints.ReinitialiserMotDePasse(db, null, u, new PasswordHasher<Utilisateur>());
        Console.WriteLine($"Mot de passe provisoire de « {u.Login} » : {mdp}");
        Console.WriteLine("Ses sessions ouvertes sont fermées ; il devra être changé à la prochaine connexion.");
        if (!u.Actif) Console.WriteLine($"Attention : le compte est désactivé (gestion-mep comptes reactiver {u.Login}).");
        return 0;
    }

    private static async Task<int> ChangerEtat(string login, bool actif)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var change = actif ? await AdminEndpoints.Reactiver(db, null, u) : await AdminEndpoints.Desactiver(db, null, u);
        Console.WriteLine(change
            ? $"Compte « {u.Login} » {(actif ? "réactivé" : "désactivé")}."
            : $"Le compte « {u.Login} » est déjà {(actif ? "actif" : "désactivé")}.");
        return 0;
    }

    private static async Task<int> Relier(string login, string loginPrincipal, string[] arguments)
    {
        var o = Options.Lire(arguments, [], "--desactiver");
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var principal = await Compte(db, loginPrincipal);
        var etaitActif = u.Actif;
        await AdminEndpoints.Relier(db, null, u, principal, o.A("--desactiver"));
        Console.WriteLine($"Compte « {u.Login} » relié à « {principal.Login} » : il s'affiche désormais sous le nom « {principal.NomAffiche} »"
            + (etaitActif && !u.Actif ? " et il est désactivé." : "."));
        return 0;
    }

    private static async Task<int> Delier(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        await AdminEndpoints.Delier(db, null, u);
        Console.WriteLine($"Liaison du compte « {u.Login} » défaite : il s'affiche de nouveau sous le nom « {u.NomAffiche} ».");
        return 0;
    }

    /// <summary>Mêmes clés que l'application (dossier Cles:Dossier) : le secret chiffré ici se déchiffre là-bas.</summary>
    private static IDataProtectionProvider Cles(IConfiguration config) =>
        config["Cles:Dossier"] is { Length: > 0 } dossier
            ? DataProtectionProvider.Create(new DirectoryInfo(dossier), b => b.SetApplicationName("gestion-mep"))
            : DataProtectionProvider.Create("gestion-mep");

    private static string? AlerteExpiration(ConfigurationSso c)
    {
        if (c.SecretExpireLe is not { } fin) return null;
        var jours = fin.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        return jours < 0 ? $"Le secret client a expiré le {fin:yyyy-MM-dd} : les connexions par le fournisseur échouent."
            : jours <= 30 ? $"Le secret client expire le {fin:yyyy-MM-dd} (dans {jours} jour(s)) : prévoyez son renouvellement."
            : null;
    }

    private static async Task<int> SsoAfficher()
    {
        var config = Configuration();
        await using var db = Base(config);
        var c = await SsoEndpoints.Charger(db);
        var cles = Cles(config);
        if (EnJson)
        {
            EcrireJson(SsoEndpoints.Dto(c, cles, ConnexionUnique.UrlRetour(config, null)));
            return 0;
        }
        var secret = c.SecretChiffre.Length == 0 ? "absent"
            : ConnexionUnique.Secret(c, cles) is null ? "illisible avec les clés actuelles : à saisir de nouveau"
            : "enregistré (chiffré)";
        if (c.SecretExpireLe is { } fin) secret += $", expire le {fin:yyyy-MM-dd}";
        var microsoft = c.Fournisseur == FournisseurSso.Microsoft;
        Console.WriteLine($"Connexion unique   : {(c.Active ? "activée" : "désactivée")}");
        Console.WriteLine($"Fournisseur        : {(microsoft ? "Microsoft Entra ID" : "OpenID Connect")}");
        Console.WriteLine(microsoft ? $"Annuaire           : {Valeur(c.Annuaire)}" : $"Émetteur           : {Valeur(c.Autorite)}");
        Console.WriteLine($"ID d'application   : {Valeur(c.ClientId)}");
        Console.WriteLine($"Secret             : {secret}");
        Console.WriteLine($"Comptes            : {(c.CreationAutomatique ? "créés à la première connexion" : "déjà liés seulement")}");
        Console.WriteLine($"Rôle administrateur: {(c.RoleAdmin.Length > 0 ? $"rôle d'application « {c.RoleAdmin} »" : "géré dans l'application")}");
        Console.WriteLine($"Bouton             : {ConnexionUnique.LibelleEffectif(c)}");
        Console.WriteLine($"Adresse de retour  : {ConnexionUnique.UrlRetour(config, null) ?? $"<adresse publique>{ConnexionUnique.CheminRetour} (adresse publique non définie : gestion-mep reglages definir url-publique <adresse>)"}");
        foreach (var a in ConnexionUnique.Anomalies(c, cles, pourActiver: true)) Console.WriteLine($"À compléter        : {a}");
        if (AlerteExpiration(c) is { } alerte) Console.WriteLine($"Attention          : {alerte}");
        return 0;

        static string Valeur(string v) => v.Length > 0 ? v : "(vide)";
    }

    private static async Task<int> SsoConfigurer(string[] options)
    {
        var config = Configuration();
        await using var db = Base(config);
        var c = await SsoEndpoints.Charger(db);
        var fournisseur = c.Fournisseur;
        string annuaire = c.Annuaire, autorite = c.Autorite, client = c.ClientId, role = c.RoleAdmin, libelle = c.Libelle;
        var expiration = c.SecretExpireLe;
        var creation = c.CreationAutomatique;
        string? secret = null;
        var lireSecret = false;
        for (var i = 0; i < options.Length; i++)
        {
            string Suivant() => i + 1 < options.Length ? options[++i] : throw ErreurHttp.Saisie($"Valeur manquante après {options[i]}.");
            switch (options[i])
            {
                case "--annuaire": annuaire = Suivant(); fournisseur = FournisseurSso.Microsoft; break;
                case "--emetteur": autorite = Suivant(); fournisseur = FournisseurSso.OpenIdConnect; break;
                case "--client": client = Suivant(); break;
                case "--secret-stdin": lireSecret = true; break;
                case "--role-admin": role = Suivant() is "aucun" ? "" : options[i]; break;
                case "--libelle": libelle = Suivant(); break;
                case "--creation-auto":
                    creation = Suivant() switch
                    {
                        "oui" => true,
                        "non" => false,
                        _ => throw ErreurHttp.Saisie("--creation-auto attend oui ou non."),
                    };
                    break;
                case "--expiration":
                    var texte = Suivant();
                    expiration = texte == "aucune" ? null
                        : DateOnly.TryParseExact(texte, "yyyy-MM-dd", out var d) ? d
                        : throw ErreurHttp.Saisie("--expiration attend une date AAAA-MM-JJ ou « aucune ».");
                    break;
                default: return AfficherAide(2);
            }
        }
        if (lireSecret)
        {
            secret = LireSecret();
            if (string.IsNullOrWhiteSpace(secret)) throw ErreurHttp.Saisie("Secret vide.");
        }
        await SsoEndpoints.Enregistrer(db, Cles(config), null,
            new SsoSaisie(fournisseur, annuaire, autorite, client, secret, expiration, creation, role, libelle), DateTimeOffset.UtcNow);
        Console.WriteLine("Réglages enregistrés.");
        return await SsoAfficher();
    }

    /// <summary>Secret depuis l'entrée standard redirigée (fichier, tube), sinon saisi au clavier sans écho.</summary>
    private static string LireSecret()
    {
        if (Console.IsInputRedirected) return (Console.In.ReadToEnd()).Trim();
        Console.Write("Secret client : ");
        var saisie = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (saisie.Length > 0) saisie.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) saisie.Append(k.KeyChar);
        }
        Console.WriteLine();
        return saisie.ToString().Trim();
    }

    private static async Task<int> SsoTester()
    {
        var config = Configuration();
        await using var db = Base(config);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        var verifications = await ConnexionUnique.Tester(await SsoEndpoints.Charger(db), Cles(config), http);
        foreach (var v in verifications) Console.WriteLine($"[{(v.Ok ? "ok" : "erreur"),-6}] {v.Libelle} : {v.Detail}");
        return verifications.All(v => v.Ok) ? 0 : 1;
    }

    private static async Task<int> SsoChangerEtat(bool active)
    {
        var config = Configuration();
        await using var db = Base(config);
        await SsoEndpoints.ChangerEtat(db, Cles(config), null, active, DateTimeOffset.UtcNow);
        Console.WriteLine(active
            ? "Connexion unique activée : le bouton apparaît sur la page de connexion (le mot de passe local reste possible)."
            : "Connexion unique désactivée : seuls les comptes locaux se connectent. Les liaisons sont conservées.");
        return 0;
    }

    private static async Task<int> SsoComptes()
    {
        await using var db = Base(Configuration());
        var liens = await SsoEndpoints.Identites(db).ToListAsync();
        if (EnJson)
        {
            EcrireJson(liens);
            return 0;
        }
        if (liens.Count == 0) { Console.WriteLine("Aucun compte relié."); return 0; }
        var largeur = Math.Max(5, liens.Max(l => l.Login.Length));
        Console.WriteLine($"{"LOGIN".PadRight(largeur)}  {"DERNIÈRE CONNEXION",-18}  IDENTIFIANT CHEZ LE FOURNISSEUR");
        foreach (var l in liens)
            Console.WriteLine($"{l.Login.PadRight(largeur)}  {(l.DerniereConnexion is { } d ? d.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "jamais"),-18}  {l.Identifiant}");
        return 0;
    }

    private static async Task<int> SsoDelier(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var liens = await db.IdentitesExternes.Include(x => x.Utilisateur).Where(x => x.UtilisateurId == u.Id).ToListAsync();
        if (liens.Count == 0) { Console.WriteLine($"Le compte « {u.Login} » n'est relié à aucun compte du fournisseur."); return 0; }
        foreach (var l in liens) await SsoEndpoints.Delier(db, null, l);
        Console.WriteLine($"Liaison supprimée pour « {u.Login} ».");
        if (u.MotDePasseHash.Length == 0)
            Console.WriteLine($"Ce compte n'a pas de mot de passe : « gestion-mep comptes reinitialiser-mdp {u.Login} » pour lui en donner un.");
        return 0;
    }

    /// <summary>Le nombre d'approbations vit dans les paramètres métier, mais se règle avec les autres.</summary>
    private const string Approbations = "approbations-minimum";

    private static async Task<int> ReglagesLister()
    {
        var config = Configuration();
        await using var db = Base(config);
        if (EnJson)
        {
            EcrireJson(new
            {
                Reglages = await Reglages.Lister(db, config),
                ApprobationsMinimum = await new ServiceMep(db, TimeProvider.System, new DiffuseurMep(config)).ApprobationsMinimum(),
            });
            return 0;
        }
        var largeur = Reglages.Catalogue.Max(d => d.Nom.Length) + 2;
        foreach (var groupe in (await Reglages.Lister(db, config)).GroupBy(r => r.Groupe))
        {
            Console.WriteLine(groupe.Key);
            foreach (var r in groupe)
            {
                var origine = r.Origine switch
                {
                    "console" => $"modifié le {r.ModifieLe:yyyy-MM-dd HH:mm} par {r.ModifiePar}" + (r.ValeurFichier is { } f ? $", {Installation.NomFichier} : {Texte(f)}" : ""),
                    "fichier" => $"{Installation.NomFichier} ({r.Variable})",
                    _ => "valeur par défaut",
                };
                Console.WriteLine($"  {r.Nom.PadRight(largeur)}{Texte(r.Valeur)}   [{origine}]");
                foreach (var a in r.Alertes) Console.WriteLine($"  {"".PadRight(largeur)}Attention : {a}");
            }
        }
        Console.WriteLine("Approbations");
        Console.WriteLine($"  {Approbations.PadRight(largeur)}{(await db.Parametres.AsNoTracking().Select(p => (int?)p.ApprobationsMinimum).FirstOrDefaultAsync() ?? 1)}");
        Console.WriteLine();
        Console.WriteLine($"Lus au démarrage, dans le {Installation.FichierDemarrage} :");
        Console.WriteLine(Installation.EstDocker
            ? "PORT, ADRESSE_ECOUTE, POSTGRES_USER, POSTGRES_PASSWORD, DEMO, ADMIN_LOGIN, ADMIN_MOT_DE_PASSE, MEP_IMAGE, COMPOSE_PROFILES."
            : "BASE_DE_DONNEES, DOSSIER_DONNEES, PORT, ADRESSE_ECOUTE, DEMO, ADMIN_LOGIN, ADMIN_MOT_DE_PASSE.");
        return 0;
    }

    private static async Task<int> ReglagesDefinir(string nom, string valeur)
    {
        var config = Configuration();
        await using var db = Base(config);
        if (nom == Approbations)
        {
            if (!int.TryParse(valeur, System.Globalization.CultureInfo.InvariantCulture, out var n))
                throw ErreurHttp.Saisie("Le nombre minimum d'approbations doit être un entier positif ou nul.");
            Console.WriteLine($"Réglage enregistré : {Approbations} = {await ParametresEndpoints.EnregistrerApprobations(db, n, null)}.");
            return 0;
        }
        var r = await Reglages.Definir(db, config, Reglages.Trouver(nom), valeur, null, DateTimeOffset.UtcNow);
        Console.WriteLine($"Réglage enregistré : {r.Nom} = {Texte(r.Valeur)}. L'application l'applique sous 10 secondes, sans redémarrage.");
        foreach (var a in r.Alertes) Console.WriteLine($"Attention : {a}");
        return 0;
    }

    private static async Task<int> ReglagesRetablir(string nom)
    {
        var config = Configuration();
        await using var db = Base(config);
        var r = await Reglages.Retablir(db, config, Reglages.Trouver(nom), null);
        Console.WriteLine($"Réglage rétabli : {r.Nom} = {Texte(r.Valeur)} ({(r.Origine == "fichier" ? Installation.NomFichier : "valeur par défaut")}).");
        return 0;
    }

    private static string Texte(string valeur) => valeur.Length == 0 ? "(vide)" : valeur;

    /// <summary>Contrôles de démarrage, lisibles par un humain ; code 1 si un contrôle bloquant échoue.</summary>
    private static async Task<int> VerifierConfig()
    {
        var config = Configuration();
        var erreurs = 0;
        void Ok(string m) => Console.WriteLine($"[ok]        {m}");
        void Attention(string m) => Console.WriteLine($"[attention] {m}");
        void Erreur(string m) { Console.WriteLine($"[erreur]    {m}"); erreurs++; }

        Ok($"Environnement : {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}");
        if (config.GetValue<bool>("Demo:Activer")) Attention("Mode démonstration activé (DEMO=true) : comptes alice, bob, claire / demo. À désactiver sur un serveur partagé.");

        VerifierDossier("Pièces jointes", string.IsNullOrWhiteSpace(config["Stockage:Dossier"]) ? StockagePiecesJointes.DossierParDefaut : config["Stockage:Dossier"]!, obligatoire: true);
        VerifierDossier("Sauvegardes", Sauvegardes.DossierConfigure(config), obligatoire: true);
        if (config["Cles:Dossier"] is { Length: > 0 } cles) VerifierDossier("Clés de session", cles, obligatoire: true);
        else Attention("Cles:Dossier non défini : les sessions seront perdues à chaque recréation du conteneur.");

        if (config.GetConnectionString("Mep") is not { Length: > 0 } chaine)
        {
            Erreur("ConnectionStrings:Mep absente (variable ConnectionStrings__Mep).");
            return 1;
        }
        if (new Npgsql.NpgsqlConnectionStringBuilder(chaine).Password is { } mdpBase && AuthentificationLocale.MotDePasseInitialRefuse(mdpBase) is not null)
            Attention("Mot de passe de la base faible ou d'exemple (POSTGRES_PASSWORD) : à changer hors poste de développement.");
        if (!config.GetValue(Reglages.HttpsObligatoire, true))
            Attention("HTTPS non obligatoire (mode d'essai) : les mots de passe peuvent circuler en clair. « gestion-mep reglages definir https-obligatoire oui ».");
        var charte = ServiceCharte.LireFichier(ServiceCharte.CheminFichier(config));
        if (!charte.Present) Ok($"Charte visuelle : pas de fichier {charte.Chemin} (facultatif).");
        else if (charte.Erreur is { } erreurCharte) Attention($"Charte visuelle : fichier {charte.Chemin} invalide, ignoré : {erreurCharte} (« gestion-mep charte verifier »).");
        else Ok($"Charte visuelle : fichier {charte.Chemin} valide.");
        if (config["Admin:MotDePasse"] is { Length: > 0 })
            Attention($"ADMIN_MOT_DE_PASSE est encore défini : il ne sert qu'au premier démarrage, le retirer du {Installation.NomFichier}.");
        await using var db = Base(config);
        try
        {
            if (!await db.Database.CanConnectAsync()) { Erreur("Base PostgreSQL injoignable avec ConnectionStrings:Mep."); return 1; }
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException or ArgumentException)
        {
            Erreur($"Base PostgreSQL injoignable : {e.Message}");
            return 1;
        }
        Ok("Base PostgreSQL joignable.");
        var enAttente = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (enAttente.Count > 0)
        {
            Attention($"{enAttente.Count} migration(s) en attente (appliquées au démarrage, ou par « gestion-mep migrer »).");
            return erreurs > 0 ? 1 : 0;
        }
        Ok("Schéma de la base à jour.");
        var admins = await db.Utilisateurs.CountAsync(u => u.EstAdmin && u.Actif);
        if (admins == 0) Erreur("Aucun administrateur actif : « gestion-mep comptes creer <login> --admin ».");
        else Ok($"{admins} administrateur(s) actif(s).");
        var sso = await SsoEndpoints.Charger(db);
        if (!sso.Active) Ok("Connexion unique désactivée (facultative : « gestion-mep sso afficher »).");
        else if (ConnexionUnique.Anomalies(sso, Cles(config), pourActiver: true) is { Count: > 0 } anomalies)
            Erreur($"Connexion unique active mais inutilisable : {string.Join(" ", anomalies)}");
        else
        {
            Ok($"Connexion unique active ({ConnexionUnique.AutoriteEffective(sso)}).");
            if (AlerteExpiration(sso) is { } alerte) Attention(alerte);
        }
        return erreurs > 0 ? 1 : 0;

        void VerifierDossier(string libelle, string chemin, bool obligatoire)
        {
            try
            {
                Directory.CreateDirectory(chemin);
                var essai = Path.Combine(chemin, $".essai-{Guid.NewGuid():N}");
                File.WriteAllText(essai, "");
                File.Delete(essai);
                Ok($"{libelle} : {chemin} accessible en écriture.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (obligatoire) Erreur($"{libelle} : {chemin} non accessible en écriture ({e.Message}).");
                else Attention($"{libelle} : {chemin} non accessible en écriture.");
            }
        }
    }
}
