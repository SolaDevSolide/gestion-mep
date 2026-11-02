using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// « gestion-mep jetons … » : jetons d'accès à l'API (#128), avec les mêmes règles et le même journal que la console (auteur
/// « Ligne de commande »). La commande crée des jetons de service ; les jetons personnels se créent depuis « Mon profil ».
/// </summary>
public static partial class LigneDeCommande
{
    private static GroupeCommandes GroupeJetons => new("jetons", "Jetons d'accès à l'API",
        "Un jeton se désigne par son préfixe (gmep_ et 8 caractères, colonne PRÉFIXE de « jetons liste ») ou son identifiant (8 premiers caractères suffisent).");

    private static Commande[] CommandesJetons =>
    [
        new("jetons creer", "\"<nom>\" --portees <portée,…> [--duree <jours>]",
            "Crée un jeton de service (outil CI/CD, supervision) et affiche son secret, une seule fois",
            a => JetonsCreer(a.Arg(0, "<nom>"), a.Depuis(1)))
        {
            Options = [("--portees <portée,…>", $"Portées séparées par des virgules : {string.Join(", ", PorteesJeton.Toutes)}"),
                ("--duree <jours>", $"Durée de validité, de 1 à {Regles.DureeJetonMaxJours} jours (défaut {Regles.DureeJetonParDefautJours})")],
            Details = "Le jeton agit sous un compte technique qui porte son nom (sans mot de passe) : ajouter ce compte à une MEP pour qu'il "
                + "puisse la modifier ou l'exécuter. Le secret n'est affiché qu'une fois (même avec --quiet) : le ranger aussitôt dans le "
                + "coffre de l'outil. Les jetons personnels se créent depuis « Mon profil ».",
            Json = "resultat : { jeton, secret }, jeton comme GET /api/admin/jetons/{id}",
            Exemples = ["gestion-mep jetons creer \"CI GitLab\" --portees meps:lecture,execution --duree 180",
                "gestion-mep jetons creer Supervision --portees meps:lecture --json   (pour un script : resultat.secret)"],
        },
        new("jetons liste", "[--type personnel|service] [--compte <login>] [--actifs]",
            "Jetons : préfixe, nom, type, compte, portées, état, expiration, dernière utilisation", a => JetonsListe(a.Args))
        {
            Options = [("--type personnel|service", "Seulement les jetons de ce type"), ("--compte <login>", "Seulement les jetons de ce compte"),
                ("--actifs", "Seulement les jetons utilisables (ni expirés, ni révoqués)")],
            Json = "tableau des jetons, comme GET /api/admin/jetons",
            Exemples = ["gestion-mep jetons liste", "gestion-mep jetons liste --type service --actifs --json"],
        },
        new("jetons revoquer", "<jeton> [--oui]", "Révoque un jeton, personnel ou de service : il ne fonctionne plus, définitivement",
            a => JetonsRevoquer(a.Arg(0, "<jeton>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [OptionOui],
            Details = "Le compte technique d'un jeton de service est aussi désactivé ; son nom reste dans l'historique des MEP.",
            Json = "resultat : le jeton révoqué",
            Exemples = ["gestion-mep jetons revoquer gmep_3fK9aQ2x --oui"],
        },
    ];

    private static List<string> ListePortees(string? texte) =>
        (texte ?? throw new ErreurUsage("--portees <portée,…> attendu"))
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task<int> JetonsCreer(string nom, string[] args)
    {
        var o = Options.Lire(args, ["--portees", "--duree"]);
        var portees = ListePortees(o.Valeur("--portees"));
        await using var db = Base(Configuration());
        var cree = await ServiceJetons.CreerService(db, null, new JetonSaisie(nom, portees, o.Entier("--duree")), DateTimeOffset.UtcNow);
        Resultat(cree);
        Informer($"Jeton de service « {cree.Jeton.Nom} » créé ({cree.Jeton.Prefixe}), compte technique {cree.Jeton.CompteLogin}, "
            + $"expire le {Date(cree.Jeton.ExpireLe)}.");
        Ecrire($"Jeton : {cree.Secret}");
        Informer("Il n'est affiché qu'une fois : le ranger aussitôt dans le coffre de l'outil. Utilisation : Authorization: Bearer <jeton>.");
        return Codes.Succes;
    }

    private static async Task<int> JetonsListe(string[] args)
    {
        var o = Options.Lire(args, ["--type", "--compte"], "--actifs");
        TypeJeton? type = o.Valeur("--type") switch
        {
            null => null,
            "personnel" => TypeJeton.Personnel,
            "service" => TypeJeton.Service,
            var v => throw new ErreurUsage($"--type attend personnel ou service, pas « {v} »"),
        };
        await using var db = Base(Configuration());
        Guid? compte = o.Valeur("--compte") is { } login ? (await Compte(db, login)).Id : null;
        var maintenant = DateTimeOffset.UtcNow;
        var actifs = o.A("--actifs");
        var liste = await ServiceJetons.Lister(db, j => (type == null || j.Type == type) && (compte == null || j.UtilisateurId == compte)
            && (!actifs || j.RevoqueLe == null && j.ExpireLe > maintenant), maintenant);
        if (EnJson)
        {
            EcrireJson(liste);
            return Codes.Succes;
        }
        if (liste.Count == 0)
        {
            Ecrire("Aucun jeton. Créer un jeton de service : gestion-mep jetons creer \"<nom>\" --portees meps:lecture");
            return Codes.Succes;
        }
        var largeur = Math.Max(3, liste.Max(j => j.Nom.Length));
        var largeurCompte = Math.Max(6, liste.Max(j => j.CompteLogin.Length));
        Ecrire($"{"PRÉFIXE",-13}  {"NOM".PadRight(largeur)}  {"TYPE",-9}  {"COMPTE".PadRight(largeurCompte)}  {"ÉTAT",-7}  {"EXPIRE",-16}  {"UTILISÉ",-16}  PORTÉES");
        foreach (var j in liste)
        {
            var etat = j.Etat switch { EtatJeton.Actif => "actif", EtatJeton.Expire => "expiré", _ => "révoqué" };
            Ecrire($"{j.Prefixe,-13}  {j.Nom.PadRight(largeur)}  {(j.Type == TypeJeton.Service ? "service" : "personnel"),-9}  {j.CompteLogin.PadRight(largeurCompte)}  "
                + $"{etat,-7}  {Date(j.ExpireLe),-16}  {(j.DerniereUtilisationLe is { } d ? Date(d) : "jamais"),-16}  {string.Join(",", j.Portees)}");
        }
        return Codes.Succes;
    }

    /// <summary>Jeton désigné par son préfixe visible (gmep_ et 8 caractères), son identifiant ou le début de celui-ci (404 sinon).</summary>
    private static async Task<JetonApi> TrouverJeton(MepDbContext db, string texte)
    {
        var t = texte.Trim();
        var parPrefixe = await db.JetonsApi.Where(j => j.Prefixe == t).ToListAsync();
        if (parPrefixe.Count == 1) return parPrefixe[0];
        if (parPrefixe.Count > 1) throw ErreurHttp.Saisie($"Plusieurs jetons ont le préfixe « {t} » : donner l'identifiant (jetons liste --json).");
        var id = IdentifiantOuPrefixe(t, await db.JetonsApi.Select(j => j.Id).ToListAsync(), "jetons");
        return id is { } i && await db.JetonsApi.FirstOrDefaultAsync(j => j.Id == i) is { } jeton ? jeton
            : throw ErreurHttp.NonTrouve($"Jeton « {t} » introuvable (gestion-mep jetons liste).");
    }

    private static async Task<int> JetonsRevoquer(string texte, string[] args)
    {
        var o = Options.Lire(args, [], "--oui");
        await using var db = Base(Configuration());
        var jeton = await TrouverJeton(db, texte);
        var proprietaire = await db.Utilisateurs.AsNoTracking().FirstAsync(u => u.Id == jeton.UtilisateurId);
        Confirmer(o.A("--oui"), [$"Le jeton « {jeton.Nom} » ({jeton.Prefixe}) de {proprietaire.NomAffiche} sera révoqué : les outils qui l'utilisent recevront 401."],
            $"gestion-mep jetons revoquer {jeton.Prefixe} --oui");
        var dto = await ServiceJetons.Revoquer(db, null, jeton, DateTimeOffset.UtcNow);
        Resultat(dto);
        Informer($"Jeton « {dto.Nom} » ({dto.Prefixe}) révoqué.");
        return Codes.Succes;
    }
}
