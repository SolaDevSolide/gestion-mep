using System.Globalization;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Api.Cli;

/// <summary>
/// « gestion-mep periodes-gel … » et « gestion-mep calendrier exporter » (#129) : mêmes règles et même journal
/// d'administration que la console (auteur « Ligne de commande »).
/// </summary>
public static partial class LigneDeCommande
{
    private static GroupeCommandes GroupePeriodesGel => new("periodes-gel", "Périodes de gel (aucun Go sans dérogation)",
        "Une période se désigne par son identifiant (8 premiers caractères suffisent) ou son nom exact. Heures saisies dans le fuseau --fuseau "
        + $"(celui de l'instance par défaut, {Fuseaux.ParDefaut}), enregistrées en UTC.");

    private static GroupeCommandes GroupeCalendrier => new("calendrier", "Calendrier des MEP");

    private static readonly Dictionary<string, RecurrenceGel> NomsRepetitions = new()
    {
        ["aucune"] = RecurrenceGel.Aucune, ["hebdomadaire"] = RecurrenceGel.Hebdomadaire, ["mensuelle"] = RecurrenceGel.Mensuelle,
        ["fin-de-mois"] = RecurrenceGel.FinDeMois, ["annuelle"] = RecurrenceGel.Annuelle,
    };

    private static Commande[] CommandesCalendrier =>
    [
        new("periodes-gel lister", "", "Périodes de gel : nom, début, fin, répétition, systèmes et environnements concernés", a => { a.Rien(0); return GelsLister(); })
        {
            Json = "tableau des périodes, comme GET /api/periodes-gel",
            Exemples = ["gestion-mep periodes-gel lister", "gestion-mep periodes-gel lister --json"],
        },
        new("periodes-gel creer", "\"<nom>\" --debut \"AAAA-MM-JJ HH:MM\" --fin \"AAAA-MM-JJ HH:MM\" [options]",
            "Crée une période de gel : aucun Go n'y est donné sans dérogation accordée", a => GelsCreer(a.Arg(0, "<nom>"), a.Depuis(1)))
        {
            Options = [("--debut \"AAAA-MM-JJ HH:MM\"", "Début, inclus (AAAA-MM-JJ seul : minuit)"), ("--fin \"AAAA-MM-JJ HH:MM\"", "Fin, exclue"),
                ("--motif \"…\"", "Motif affiché aux pilotes"), ("--systeme <SYSTÈME>", "Système du catalogue concerné (répétable ; absent : tous)"),
                ("--environnement <nom>", "Environnement concerné (répétable ; absent : tous)"), ("--production", "Seulement les MEP qui touchent la production"),
                ("--repetition aucune|hebdomadaire|mensuelle|fin-de-mois|annuelle", "Répétition (fin-de-mois : même écart avec le dernier jour du mois)"),
                ("--jusqu-au AAAA-MM-JJ", "Dernier jour où une répétition peut commencer"), ("--fuseau <IANA>", $"Fuseau des heures saisies (défaut {Fuseaux.ParDefaut})"),
                ("--approbateur <login>", "Compte qui décide des dérogations (répétable ; absent, comme --equipe-approbatrice : les administrateurs)"),
                ("--equipe-approbatrice <équipe>", "Équipe dont chaque membre décide des dérogations (répétable)")],
            Json = "resultat : la période, comme GET /api/periodes-gel",
            Exemples = ["gestion-mep periodes-gel creer \"Fêtes de fin d'année\" --debut 2026-12-20 --fin 2027-01-04 --production --motif \"Activité commerciale\"",
                "gestion-mep periodes-gel creer \"Clôture comptable\" --debut \"2026-11-28 18:00\" --fin \"2026-12-02 08:00\" --systeme BDD --repetition fin-de-mois --approbateur claire"],
        },
        new("periodes-gel supprimer", "<période> [--oui]", "Supprime une période de gel (les dérogations déjà inscrites dans les MEP restent)",
            a => GelsSupprimer(a.Arg(0, "<période>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [OptionOui],
            Exemples = ["gestion-mep periodes-gel supprimer \"Clôture comptable\" --oui"],
        },
        new("calendrier exporter", "[--du AAAA-MM-JJ] [--au AAAA-MM-JJ] [filtres] [--sortie <fichier.ics>]",
            "Exporte le calendrier des MEP au format iCalendar (.ics), avec les périodes de gel", a => CalendrierExporter(a.Args))
        {
            Options = [("--du AAAA-MM-JJ", "Premier jour (défaut : début du mois en cours)"), ("--au AAAA-MM-JJ", $"Dernier jour inclus (défaut : un mois ; {ServiceCalendrier.JoursMax} jours au plus)"),
                ("--fuseau <IANA>", $"Fuseau des jours --du et --au (défaut {Fuseaux.ParDefaut})"), ("--systeme <SYSTÈME>", "Système du catalogue (répétable)"),
                ("--etat <état>", $"État (répétable) : {string.Join(", ", FiltreMeps.NomsEtats.Keys)}"), ("--equipe <équipe>", "Équipe (répétable)"),
                ("--sans-gels", "N'exporte pas les périodes de gel"), ("--sortie <fichier.ics>", "Fichier à écrire (défaut : sortie standard)")],
            Details = "Heures en UTC dans le fichier (l'agenda les affiche dans son fuseau), journées entières en dates locales ; UID stable par MEP.",
            Json = "sans --sortie : { du, au, fuseauHoraire, meps, gels }, comme GET /api/calendrier ; avec --sortie : resultat { fichier, meps, gels }",
            Exemples = ["gestion-mep calendrier exporter --du 2026-11-01 --au 2026-12-31 --sortie mep.ics",
                "gestion-mep calendrier exporter --systeme BDD --etat preparation > mep-bdd.ics"],
        },
    ];

    private static ServiceCalendrier Calendrier(MepDbContext db) => new(db, TimeProvider.System);

    private static string Local(DateTimeOffset t, string fuseau) => Fuseaux.Local(t, fuseau).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static async Task<int> GelsLister()
    {
        await using var db = Base(Configuration());
        var periodes = await Calendrier(db).ListerPeriodes();
        if (EnJson)
        {
            EcrireJson(periodes);
            return Codes.Succes;
        }
        if (periodes.Count == 0)
        {
            Informer("Aucune période de gel : « gestion-mep periodes-gel creer \"<nom>\" --debut … --fin … ».");
            return Codes.Succes;
        }
        var largeur = Math.Max(3, periodes.Max(p => p.Nom.Length));
        Ecrire($"{"ID",-8}  {"NOM".PadRight(largeur)}  {"DÉBUT",-16}  {"FIN",-16}  {"RÉPÉTITION",-12}  PORTÉE ; DÉROGATIONS");
        foreach (var p in periodes)
        {
            var portee = p.Systemes.Count == 0 ? "tous les systèmes" : string.Join(", ", p.Systemes);
            if (p.Environnements.Count > 0) portee += " ; " + string.Join(", ", p.Environnements);
            if (p.ProductionSeulement) portee += " ; production";
            portee += " ; dérogations : " + p.Decideurs;
            var repetition = NomsRepetitions.First(x => x.Value == p.Recurrence).Key + (p.FinRecurrence is { } f ? $" → {f:yyyy-MM-dd}" : "");
            Ecrire($"{p.Id.ToString()[..8]}  {p.Nom.PadRight(largeur)}  {Local(p.Debut, p.FuseauHoraire),-16}  {Local(p.Fin, p.FuseauHoraire),-16}  {repetition,-12}  {portee}");
        }
        return Codes.Succes;
    }

    /// <summary>« AAAA-MM-JJ HH:MM » (ou « AAAA-MM-JJ », minuit) dans le fuseau, en instant.</summary>
    private static DateTimeOffset InstantLocal(string? texte, string option, string fuseau)
    {
        if (texte is null) throw new ErreurUsage($"{option} est obligatoire");
        var t = texte.Trim().Replace('T', ' ');
        string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd"];
        if (!DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw ErreurHttp.Saisie($"{option} attend une date AAAA-MM-JJ ou « AAAA-MM-JJ HH:MM ».");
        return Fuseaux.Instant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), fuseau);
    }

    private static IEnumerable<string> Repetees(Options o, string nom) => o.Liste.Where(x => x.Nom == nom && x.Valeur is not null).Select(x => x.Valeur!);

    private static async Task<int> GelsCreer(string nom, string[] args)
    {
        var o = Options.Lire(args, ["--debut", "--fin", "--motif", "--systeme", "--environnement", "--repetition", "--jusqu-au", "--fuseau", "--approbateur",
            "--equipe-approbatrice"], "--production");
        var fuseau = ServiceCalendrier.LireFuseau(o.Valeur("--fuseau"));
        var debut = InstantLocal(o.Valeur("--debut"), "--debut", fuseau);
        var fin = InstantLocal(o.Valeur("--fin"), "--fin", fuseau);
        var repetition = o.Valeur("--repetition") is { } r
            ? NomsRepetitions.TryGetValue(r.Trim().ToLowerInvariant(), out var rec) ? rec
                : throw ErreurHttp.Saisie($"--repetition inconnue « {r} » : {string.Join(", ", NomsRepetitions.Keys)}.")
            : RecurrenceGel.Aucune;
        DateOnly? jusquau = o.Valeur("--jusqu-au") is { } j
            ? DateOnly.TryParseExact(j, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour) ? jour
                : throw ErreurHttp.Saisie("--jusqu-au attend une date AAAA-MM-JJ.")
            : null;
        await using var db = Base(Configuration());
        var systemes = new List<Guid>();
        foreach (var s in Repetees(o, "--systeme")) systemes.Add((await TrouverSysteme(db, s)).Id);
        var approbateurs = new List<Guid>();
        foreach (var login in Repetees(o, "--approbateur")) approbateurs.Add((await Compte(db, login)).Id);
        var equipes = new List<Guid>();
        foreach (var e in Repetees(o, "--equipe-approbatrice")) equipes.Add((await EquipesEndpoints.Trouver(db, e)).Id);
        var p = await Calendrier(db).Creer(null, new PeriodeGelSaisie(nom, debut, fin, o.Valeur("--motif"), systemes, [.. Repetees(o, "--environnement")],
            o.A("--production"), repetition, jusquau, fuseau, ApprobateurIds: approbateurs, EquipeApprobatriceIds: equipes));
        Resultat(p);
        Informer($"Période de gel « {p.Nom} » créée ({p.Id.ToString()[..8]}) : du {Local(p.Debut, p.FuseauHoraire)} au {Local(p.Fin, p.FuseauHoraire)} ({p.FuseauHoraire}) ; "
            + $"dérogations décidées par {p.Decideurs}.");
        return Codes.Succes;
    }

    private static async Task<int> GelsSupprimer(string designation, string[] args)
    {
        var o = Options.Lire(args, [], "--oui");
        await using var db = Base(Configuration());
        var cal = Calendrier(db);
        var p = await cal.TrouverPeriode(designation);
        Confirmer(o.A("--oui"), [$"La période de gel « {p.Nom} » sera supprimée : les Go n'y seront plus bloqués."],
            $"gestion-mep periodes-gel supprimer {p.Id.ToString()[..8]} --oui");
        await cal.Supprimer(null, p.Id);
        Informer($"Période de gel « {p.Nom} » supprimée.");
        return Codes.Succes;
    }

    private static async Task<int> CalendrierExporter(string[] args)
    {
        var o = Options.Lire(args, ["--du", "--au", "--fuseau", "--systeme", "--etat", "--equipe", "--sortie"], "--sans-gels");
        await using var db = Base(Configuration());
        var systemes = new List<string>();
        foreach (var s in Repetees(o, "--systeme")) systemes.Add((await TrouverSysteme(db, s)).Id.ToString());
        var equipes = new List<string>();
        foreach (var e in Repetees(o, "--equipe")) equipes.Add((await EquipesEndpoints.Trouver(db, e)).Id.ToString());
        var parametres = new ParametresCalendrier(o.Valeur("--du"), o.Valeur("--au"), o.Valeur("--fuseau"),
            Systeme: systemes.Count == 0 ? null : string.Join(",", systemes), Equipe: equipes.Count == 0 ? null : string.Join(",", equipes),
            Etat: string.Join(",", Repetees(o, "--etat")));
        var cal = Calendrier(db);
        var plage = cal.LirePlage(parametres.Du, parametres.Au, parametres.Fuseau);
        var filtre = ServiceCalendrier.Filtre(parametres);
        var avecGels = !o.A("--sans-gels");
        var sortie = o.Valeur("--sortie");
        if (EnJson && sortie is null)
        {
            var (lignes, gels) = await cal.Lire(plage, filtre);
            EcrireJson(new { plage.Du, plage.Au, FuseauHoraire = plage.Fuseau, Meps = lignes.Select(l => l.Element),
                Gels = avecGels ? gels.Select(g => ServiceCalendrier.Dto(g)) : [] });
            return Codes.Succes;
        }
        var ics = await cal.Ics(plage, filtre, avecGels, ServiceCalendrier.AdresseBase(Configuration(), null));
        if (sortie is null or "-")
        {
            S.DonneesEcrites = true;
            Console.Out.Write(ics);
            return Codes.Succes;
        }
        if (Path.GetDirectoryName(Path.GetFullPath(sortie)) is { } dossier) Directory.CreateDirectory(dossier);
        await File.WriteAllTextAsync(sortie, ics, new System.Text.UTF8Encoding(false));
        var meps = ics.Split("\r\nUID:mep-").Length - 1;
        var nbGels = ics.Split("\r\nUID:gel-").Length - 1;
        Resultat(new { Fichier = Path.GetFullPath(sortie), Meps = meps, Gels = nbGels });
        Informer($"{meps} MEP et {nbGels} période(s) de gel exportées dans {sortie} (du {plage.Du:yyyy-MM-dd} au {plage.Au:yyyy-MM-dd}).");
        return Codes.Succes;
    }
}
