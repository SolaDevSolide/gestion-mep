using System.Reflection;
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
/// Commandes d'exploitation, sans navigateur, pour l'équipe informatique. Dans le conteneur :
/// <c>docker compose exec app gestion-mep &lt;commande&gt;</c>. Elles lisent la même configuration que l'application
/// (variables d'environnement, appsettings) et inscrivent leurs actions dans le journal d'administration.
/// </summary>
public static class LigneDeCommande
{
    private static readonly string[] Commandes = ["aide", "--help", "-h", "version", "--version", "migrer", "config", "comptes", "sauvegarder", "importer", "sso", "reglages"];

    private const string Aide = """
        gestion-mep : commandes d'exploitation

          gestion-mep comptes lister                          Liste les comptes (login, nom, admin, état)
          gestion-mep comptes creer <login> [--nom "Nom"] [--admin]
                                                              Crée un compte avec un mot de passe provisoire
          gestion-mep comptes reinitialiser-mdp <login>       Nouveau mot de passe provisoire (à changer à la connexion)
          gestion-mep comptes desactiver <login>              Désactive le compte et ferme ses sessions
          gestion-mep comptes reactiver <login>               Réactive le compte
          gestion-mep config verifier                         Vérifie base, migrations, dossiers, comptes admin
          gestion-mep migrer                                  Applique les migrations de la base sans démarrer l'application
                                                              (sauvegarde préalable, supprimée si les contrôles passent)
          gestion-mep sauvegarder [fichier.tar.gz]            Archive la base et les pièces jointes (défaut : dossier des sauvegardes)
          gestion-mep importer <fichier.tar.gz>               Remplace base et pièces jointes par une archive (application arrêtée) ;
                                                              sauvegarde préalable, supprimée si les contrôles passent
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
          gestion-mep reglages lister                         Réglages d'exploitation : valeur, origine (console, .env, défaut)
          gestion-mep reglages definir <nom> <valeur>         Modifie un réglage, appliqué sans redémarrage (sous 10 secondes) ;
                                                              la valeur remplace celle du fichier .env
          gestion-mep reglages retablir <nom>                 Revient à la valeur du fichier .env (ou à la valeur par défaut)
          gestion-mep version                                 Version de l'application

        Dans Docker : docker compose exec app gestion-mep <commande>
        Exemples : gestion-mep reglages definir url-publique https://mep.entreprise.fr
                   gestion-mep reglages definir lien-signalement ""      (valeur vide : lien masqué)
        Secret depuis un fichier : docker compose exec -T app gestion-mep sso configurer --secret-stdin < secret.txt
        Codes de retour : 0 succès, 1 échec, 2 commande mal formée.
        """;

    public static bool EstCommande(string[] args) => args.Length > 0 && Commandes.Contains(args[0]);

    public static async Task<int> Executer(string[] args)
    {
        try
        {
            return args switch
            {
                ["version" or "--version"] => Version(),
                ["migrer"] => await Migrer(),
                ["sauvegarder"] => await Sauvegarder(null),
                ["sauvegarder", var fichier] when !fichier.StartsWith('-') => await Sauvegarder(fichier),
                ["importer", var archive] when !archive.StartsWith('-') => await Importer(archive),
                ["config", "verifier"] => await VerifierConfig(),
                ["comptes", "lister"] => await Lister(),
                ["comptes", "creer", var login, .. var options] => await Creer(login, options),
                ["comptes", "reinitialiser-mdp", var login] => await Reinitialiser(login),
                ["comptes", "desactiver", var login] => await ChangerEtat(login, actif: false),
                ["comptes", "reactiver", var login] => await ChangerEtat(login, actif: true),
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
                ["aide" or "--help" or "-h"] => AfficherAide(0),
                _ => AfficherAide(2),
            };
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
    }

    private static int AfficherAide(int code)
    {
        (code == 0 ? Console.Out : Console.Error).WriteLine(Aide);
        return code;
    }

    /// <summary>Même configuration que l'application : fichiers, variables d'environnement, puis réglages modifiés en base.</summary>
    private static IConfiguration Configuration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables();
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

    private static async Task<Utilisateur> Compte(MepDbContext db, string login)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        return await db.Utilisateurs.FirstOrDefaultAsync(u => u.Login == l) ?? throw new ErreurHttp(1, $"Compte « {l} » introuvable.");
    }

    private static async Task<int> Lister()
    {
        await using var db = Base(Configuration());
        var comptes = await db.Utilisateurs.AsNoTracking().OrderBy(u => u.Login).ToListAsync();
        var largeur = Math.Max(5, comptes.Count == 0 ? 0 : comptes.Max(u => u.Login.Length));
        Console.WriteLine($"{"LOGIN".PadRight(largeur)}  ADMIN  ÉTAT        NOM");
        foreach (var u in comptes)
        {
            var etat = !u.Actif ? "désactivé" : u.DoitChangerMotDePasse ? "provisoire" : "actif";
            Console.WriteLine($"{u.Login.PadRight(largeur)}  {(u.EstAdmin ? "oui" : "non"),-5}  {etat,-10}  {u.NomAffiche}");
        }
        return 0;
    }

    private static async Task<int> Creer(string login, string[] options)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        if (l.Length == 0 || l.StartsWith('-')) return AfficherAide(2);
        string? nom = null;
        var admin = false;
        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--admin": admin = true; break;
                case "--nom" when i + 1 < options.Length: nom = options[++i].Trim(); break;
                default: return AfficherAide(2);
            }
        }
        await using var db = Base(Configuration());
        if (await db.Utilisateurs.AnyAsync(u => u.Login == l)) throw new ErreurHttp(1, $"Le compte « {l} » existe déjà.");
        var u = new Utilisateur { Login = l, NomAffiche = string.IsNullOrEmpty(nom) ? l : nom, EstAdmin = admin };
        var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, new PasswordHasher<Utilisateur>());
        db.Utilisateurs.Add(u);
        JournalAdmin.Ecrire(db, null, "compte.cree", $"Compte {JournalAdmin.Compte(u)} créé{(admin ? ", administrateur" : "")}");
        await db.SaveChangesAsync();
        Console.WriteLine($"Compte « {l} » créé{(admin ? " (administrateur)" : "")}.");
        Console.WriteLine($"Mot de passe provisoire : {mdp}");
        Console.WriteLine("À transmettre par un canal sûr ; il devra être changé à la première connexion.");
        return 0;
    }

    private static async Task<int> Reinitialiser(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var mdp = AuthentificationLocale.PoserMotDePasseProvisoire(u, new PasswordHasher<Utilisateur>());
        JournalAdmin.Ecrire(db, null, "compte.mdp-reinitialise", $"Mot de passe de {JournalAdmin.Compte(u)} réinitialisé");
        await db.SaveChangesAsync();
        Console.WriteLine($"Mot de passe provisoire de « {u.Login} » : {mdp}");
        Console.WriteLine("Ses sessions ouvertes sont fermées ; il devra être changé à la prochaine connexion.");
        if (!u.Actif) Console.WriteLine($"Attention : le compte est désactivé (gestion-mep comptes reactiver {u.Login}).");
        return 0;
    }

    private static async Task<int> ChangerEtat(string login, bool actif)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        if (u.Actif == actif)
        {
            Console.WriteLine($"Le compte « {u.Login} » est déjà {(actif ? "actif" : "désactivé")}.");
            return 0;
        }
        if (!actif) await AdminEndpoints.ExigerAutreAdmin(db, u, AdminEndpoints.MessageDernierAdmin);
        u.Actif = actif;
        if (!actif) u.TamponSecurite = Guid.NewGuid();
        JournalAdmin.Ecrire(db, null, actif ? "compte.reactive" : "compte.desactive", $"Compte {JournalAdmin.Compte(u)} {(actif ? "réactivé" : "désactivé")}");
        await db.SaveChangesAsync();
        Console.WriteLine($"Compte « {u.Login} » {(actif ? "réactivé" : "désactivé")}.");
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
        var largeur = Reglages.Catalogue.Max(d => d.Nom.Length) + 2;
        foreach (var groupe in (await Reglages.Lister(db, config)).GroupBy(r => r.Groupe))
        {
            Console.WriteLine(groupe.Key);
            foreach (var r in groupe)
            {
                var origine = r.Origine switch
                {
                    "console" => $"modifié le {r.ModifieLe:yyyy-MM-dd HH:mm} par {r.ModifiePar}" + (r.ValeurFichier is { } f ? $", .env : {Texte(f)}" : ""),
                    "fichier" => $"fichier .env ({r.Variable})",
                    _ => "valeur par défaut",
                };
                Console.WriteLine($"  {r.Nom.PadRight(largeur)}{Texte(r.Valeur)}   [{origine}]");
                foreach (var a in r.Alertes) Console.WriteLine($"  {"".PadRight(largeur)}Attention : {a}");
            }
        }
        Console.WriteLine("Approbations");
        Console.WriteLine($"  {Approbations.PadRight(largeur)}{(await db.Parametres.AsNoTracking().Select(p => (int?)p.ApprobationsMinimum).FirstOrDefaultAsync() ?? 1)}");
        Console.WriteLine();
        Console.WriteLine("Lus au démarrage, dans le fichier .env (le modifier puis « docker compose up -d ») : PORT, ADRESSE_ECOUTE,");
        Console.WriteLine("POSTGRES_USER, POSTGRES_PASSWORD, DEMO, ADMIN_LOGIN, ADMIN_MOT_DE_PASSE, MEP_IMAGE, COMPOSE_PROFILES.");
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
        Console.WriteLine($"Réglage rétabli : {r.Nom} = {Texte(r.Valeur)} ({(r.Origine == "fichier" ? "fichier .env" : "valeur par défaut")}).");
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
        if (config["Admin:MotDePasse"] is { Length: > 0 })
            Attention("ADMIN_MOT_DE_PASSE est encore défini : il ne sert qu'au premier démarrage, le retirer de .env.");
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
