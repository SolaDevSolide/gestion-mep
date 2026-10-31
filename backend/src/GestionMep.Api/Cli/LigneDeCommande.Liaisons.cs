using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Api.Cli;

/// <summary>
/// « gestion-mep liaison … » : liaison avec Noosphere (#123), avec les mêmes règles et le même journal que la console
/// (auteur « Ligne de commande »). Les commandes qui parlent à Noosphere l'appellent directement depuis ce poste.
/// </summary>
public static partial class LigneDeCommande
{
    private static GroupeCommandes GroupeLiaisons => new("liaison", "Liaison avec Noosphere",
        "Une liaison se désigne par son identifiant (8 premiers caractères suffisent) ; sans identifiant, la seule liaison en service.");

    private static Commande[] CommandesLiaisons =>
    [
        new("liaison appairer", "<url> <code>", "Appaire avec Noosphere par le code à usage unique créé dans Noosphere (valable 15 minutes)",
            a => { a.Rien(2); return LiaisonAppairer(a.Arg(0, "<url>"), a.Arg(1, "<code>")); })
        {
            Details = "Affiche l'empreinte en quatre mots : la comparer avec celle de Noosphere, puis « liaison confirmer ». Adresse annoncée à "
                + "Noosphere : le réglage url-publique.",
            Json = "resultat : la liaison, comme GET /api/admin/liaisons/{id}",
            Exemples = ["gestion-mep liaison appairer https://noosphere.entreprise.fr 7K4Q-29XD-M3PA"],
        },
        new("liaison liste", "", "Liaisons : instance, état, confirmation des deux côtés, empreinte, dernière synchronisation", a => { a.Rien(0); return LiaisonListe(); })
        {
            Json = "tableau des liaisons, comme GET /api/admin/liaisons",
            Exemples = ["gestion-mep liaison liste", "gestion-mep liaison liste --json"],
        },
        new("liaison confirmer", "<id> --empreinte \"…\"", "Confirme l'empreinte lue sur l'écran de Noosphere (code 1 si elle diffère)",
            a => LiaisonConfirmer(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Options = [("--empreinte \"…\"", "Les quatre mots affichés par Noosphere, entre guillemets (casse indifférente)")],
            Exemples = ["gestion-mep liaison confirmer 3f2a9c1e --empreinte \"orbit cedar 0712 lumen\""],
        },
        new("liaison synchroniser", "[<id>]", "Relit l'état de Noosphere et son catalogue, et met à jour les systèmes suivis",
            a => { a.Rien(1); return LiaisonSynchroniser(a.ArgFacultatif(0)); })
        {
            Details = "Le serveur le fait aussi toutes les 15 minutes et après chaque avis de changement de Noosphere. Code 1 si Noosphere refuse "
                + "ou ne répond pas (par exemple tant qu'il n'a pas confirmé l'empreinte).",
            Json = "resultat : { inchange, resume, crees, rattaches, misAJour, desactives, reactives, avertissements, liaison }",
            Exemples = ["gestion-mep liaison synchroniser", "gestion-mep liaison synchroniser 3f2a9c1e --json"],
        },
        new("liaison renouveler", "<id>", "Renouvelle les clés de gestion-mep (l'empreinte change, sans nouvelle confirmation)",
            a => { a.Rien(1); return LiaisonRenouveler(a.Arg(0, "<id>")); })
        {
            Details = "La demande part signée avec l'ancienne clé ; Noosphere accepte encore l'ancienne clé 5 minutes.",
            Exemples = ["gestion-mep liaison renouveler 3f2a9c1e"],
        },
        new("liaison couper", "<id> [--oui]", "Coupe la liaison : Noosphere est prévenu, les clés effacées, les systèmes suivis redeviennent locaux",
            a => LiaisonCouper(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [OptionOui],
            Exemples = ["gestion-mep liaison couper 3f2a9c1e --oui"],
        },
        new("liaison options", "<id> [--envoyer-meps oui|non] [--suivi-automatique oui|non]", "Interrupteurs de la liaison",
            a => LiaisonOptions(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Options = [("--envoyer-meps oui|non", "Envoyer à Noosphere les MEP des systèmes suivis"),
                ("--suivi-automatique oui|non", "Suivre automatiquement les nouvelles applications des domaines suivis")],
            Exemples = ["gestion-mep liaison options 3f2a9c1e --envoyer-meps non"],
        },
        new("liaison echanges", "[<id>] [--refus] [--limite n]", "Journal des échanges avec Noosphere (90 jours), du plus récent au plus ancien",
            a => LiaisonEchanges(a.Args))
        {
            Options = [("--refus", "Seulement les requêtes refusées (signature, rejeu, horloge, clé inconnue…)"), ("--limite n", "Nombre d'échanges (défaut 50, 500 au plus)")],
            Json = "tableau des échanges, comme GET /api/admin/liaisons/echanges",
            Exemples = ["gestion-mep liaison echanges", "gestion-mep liaison echanges --refus --limite 20"],
        },
    ];

    private static ServiceLiaisons ServiceLiaisonsCli(MepDbContext db, Microsoft.Extensions.Configuration.IConfiguration config) =>
        new(db, config, TimeProvider.System, Cles(config), new TransportLiaison(), new SignalLiaisons());

    private static string LibelleEtat(LiaisonDto l) => l.Etat switch
    {
        EtatLiaison.Coupee => "Coupée",
        EtatLiaison.EnAttente => "À confirmer ici",
        _ => l.ConfirmeeParNoosphere switch { true => "Active", false => "À confirmer dans Noosphere", null => "Confirmée ici" },
    };

    private static void AfficherLiaison(LiaisonDto l)
    {
        Ecrire($"{l.NomInstance} ({l.Id})");
        Ecrire($"Adresse        : {l.Url}{(l.Demo ? " (démonstration, aucun appel)" : "")}");
        Ecrire($"État           : {LibelleEtat(l)}");
        Ecrire($"Empreinte      : {l.Empreinte}");
        Ecrire($"Capacités      : {(l.Capacites.Count == 0 ? "aucune" : string.Join(", ", l.Capacites))}");
        Ecrire($"Envoi des MEP  : {(l.EnvoyerMeps ? "oui" : "non")} ; suivi automatique : {(l.SuiviAutomatique ? "oui" : "non")}");
        if (l.DerniereSynchro is { } d) Ecrire($"Synchronisée   : {Date(d)} ; {l.SystemesSuivis} système(s) suivi(s)");
        if (l.ClesRenouveleesLe is { } r) Ecrire($"Clés renouvelées le {Date(r)}");
        if (l.DerniereErreur.Length > 0) Ecrire($"Dernière erreur: {l.DerniereErreur}");
    }

    private static async Task<int> LiaisonAppairer(string url, string code)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var urlLocale = svc.UrlLocale();
        if (urlLocale.Length == 0) Avertir("Réglage url-publique vide : Noosphere ne connaîtra pas l'adresse de gestion-mep (liens des MEP).");
        var l = await svc.Appairer(url, code, null, JournalAdmin.AuteurLigneDeCommande, urlLocale, CancellationToken.None);
        var dto = await svc.Dto(l);
        Resultat(dto);
        Informer("Liaison appairée. Comparer l'empreinte avec celle de Noosphere, puis la confirmer des deux côtés :");
        Ecrire($"  {l.Empreinte}");
        Informer($"gestion-mep liaison confirmer {l.Id.ToString()[..8]} --empreinte \"{l.Empreinte}\"");
        return Codes.Succes;
    }

    private static async Task<int> LiaisonListe()
    {
        var config = Configuration();
        await using var db = Base(config);
        var liste = await ServiceLiaisonsCli(db, config).Lister();
        if (EnJson)
        {
            EcrireJson(liste);
            return Codes.Succes;
        }
        if (liste.Count == 0)
        {
            Ecrire("Aucune liaison. Appairer : console d'administration › Liaisons, ou « gestion-mep liaison appairer <url> <code> ».");
            return Codes.Succes;
        }
        var largeur = Math.Max(8, liste.Max(l => l.NomInstance.Length));
        Ecrire($"{"ID",-8}  {"INSTANCE".PadRight(largeur)}  {"ÉTAT",-27}  {"EMPREINTE",-28}  {"SYSTÈMES",-8}  SYNCHRONISÉE");
        foreach (var l in liste)
            Ecrire($"{l.Id.ToString()[..8]}  {l.NomInstance.PadRight(largeur)}  {LibelleEtat(l),-27}  {l.Empreinte,-28}  {l.SystemesSuivis,-8}  "
                + (l.DerniereSynchro is { } d ? Date(d) : "—"));
        return Codes.Succes;
    }

    private static async Task<int> LiaisonConfirmer(string id, string[] args)
    {
        var o = Options.Lire(args, ["--empreinte"]);
        var empreinte = o.Valeur("--empreinte") ?? throw new ErreurUsage("--empreinte \"…\" attendu");
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var l = await svc.Trouver(id);
        await svc.Confirmer(l.Id, empreinte, null, JournalAdmin.AuteurLigneDeCommande);
        var dto = await svc.Dto(l);
        Resultat(dto);
        Informer("Empreinte confirmée : la liaison est active ici. Le serveur relit le catalogue dès que Noosphere l'a confirmée aussi.");
        return Codes.Succes;
    }

    private static async Task<int> LiaisonSynchroniser(string? id)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var l = await svc.Trouver(id);
        var (bilan, inchange) = await svc.Synchroniser(l, null, JournalAdmin.AuteurLigneDeCommande, CancellationToken.None);
        var dto = Endpoints.LiaisonEndpoints.Synchro(bilan, inchange, await svc.Dto(l));
        Resultat(dto);
        Informer($"{l.NomInstance} : {dto.Resume}");
        foreach (var a in bilan.Avertissements) Avertir(a);
        return Codes.Succes;
    }

    private static async Task<int> LiaisonRenouveler(string id)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var l = await svc.Trouver(id);
        await svc.Renouveler(l, null, JournalAdmin.AuteurLigneDeCommande, CancellationToken.None);
        Resultat(await svc.Dto(l));
        Informer($"Clés renouvelées. Nouvelle empreinte : {l.Empreinte}");
        return Codes.Succes;
    }

    private static async Task<int> LiaisonCouper(string id, string[] args)
    {
        var o = Options.Lire(args, [], "--oui");
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var l = await svc.Trouver(id);
        Confirmer(o.A("--oui"), [$"La liaison avec {l.NomInstance} ({l.Url}) sera coupée : clés effacées, systèmes suivis rendus locaux."],
            $"gestion-mep liaison couper {l.Id.ToString()[..8]} --oui");
        var prevenu = await svc.Couper(l, null, JournalAdmin.AuteurLigneDeCommande, CancellationToken.None);
        Resultat(await svc.Dto(l));
        Informer("Liaison coupée.");
        if (!prevenu && !svc.EstDemo(l)) Avertir("Noosphere n'a pas pu être prévenu : coupez aussi la liaison de son côté.");
        return Codes.Succes;
    }

    private static async Task<int> LiaisonOptions(string id, string[] args)
    {
        var o = Options.Lire(args, ["--envoyer-meps", "--suivi-automatique"]);
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        var l = await svc.Trouver(id);
        await svc.Options(l, new OptionsLiaisonSaisie(o.OuiNon("--envoyer-meps"), o.OuiNon("--suivi-automatique")), null, JournalAdmin.AuteurLigneDeCommande);
        var dto = await svc.Dto(l);
        Resultat(dto);
        Informer($"Envoi des MEP : {(dto.EnvoyerMeps ? "oui" : "non")} ; suivi automatique : {(dto.SuiviAutomatique ? "oui" : "non")}.");
        return Codes.Succes;
    }

    private static async Task<int> LiaisonEchanges(string[] args)
    {
        var id = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
        var o = Options.Lire(id is null ? args : args[1..], ["--limite"], "--refus");
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceLiaisonsCli(db, config);
        Guid? liaison = id is null ? null : (await svc.Trouver(id)).Id;
        var liste = await svc.Echanges(liaison, null, o.Entier("--limite") is { } n ? Math.Clamp(n, 1, 500) : 50, o.A("--refus"));
        if (EnJson)
        {
            EcrireJson(liste);
            return Codes.Succes;
        }
        if (liste.Count == 0)
        {
            Ecrire("Aucun échange.");
            return Codes.Succes;
        }
        foreach (var e in liste)
            Ecrire($"{Date(e.Quand)}  {e.Sens,-7}  {e.Operation,-11}  {e.Resultat,-6}  {(e.Code is { } c ? c.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—"),-3}  {e.Detail}");
        return Codes.Succes;
    }
}
