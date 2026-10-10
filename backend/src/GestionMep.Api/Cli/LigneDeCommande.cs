using System.Reflection;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
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
    private static readonly string[] Commandes = ["aide", "--help", "-h", "version", "--version", "migrer", "config", "comptes", "sauvegarder", "importer"];

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
          gestion-mep version                                 Version de l'application

        Dans Docker : docker compose exec app gestion-mep <commande>
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
                ["aide" or "--help" or "-h"] => AfficherAide(0),
                _ => AfficherAide(2),
            };
        }
        catch (ErreurHttp e)
        {
            Console.Error.WriteLine($"Erreur : {e.Message}");
            return 1;
        }
    }

    private static int AfficherAide(int code)
    {
        (code == 0 ? Console.Out : Console.Error).WriteLine(Aide);
        return code;
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

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

        if (config.GetConnectionString("Mep") is not { Length: > 0 })
        {
            Erreur("ConnectionStrings:Mep absente (variable ConnectionStrings__Mep).");
            return 1;
        }
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
