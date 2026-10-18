using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using GestionMep.Tableur;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>Lecture du fichier charte.json : absent, valide (avec ses avertissements) ou invalide (erreurs).</summary>
public sealed record FichierCharteLu(string Chemin, bool Present, Charte? Charte, List<ProblemeCharte> Erreurs, List<ProblemeCharte> Avertissements)
{
    /// <summary>Erreurs en une phrase, pour le journal du serveur et le bandeau de la console.</summary>
    public string? Erreur => Erreurs.Count == 0 ? null : string.Join(" ", Erreurs.Select(e => e.Message));
}

/// <summary>
/// Charte en vigueur : celle de la console (en base) si elle existe, sinon celle du fichier, sinon la charte par défaut.
/// <paramref name="Origine"/> : « console », « fichier » ou « defaut ».
/// </summary>
public sealed record EtatCharte(Charte Charte, string Origine, DateTimeOffset? ModifieLe, string? ModifiePar, FichierCharteLu Fichier)
{
    private readonly Lazy<(string Css, string Empreinte)> _feuille = new(() => CharteVisuelle.FeuilleCss(Charte));
    private readonly Lazy<PaletteExcel> _palette = new(() => CharteVisuelle.PaletteExcel(Charte));

    public string Css => _feuille.Value.Css;
    public string Empreinte => _feuille.Value.Empreinte;
    public PaletteExcel Palette => _palette.Value;
}

/// <summary>Charte telle que la lit l'interface avant connexion (couleurs résolues, origine, empreinte de la feuille de style).</summary>
public record CharteDto(CharteFichier Charte, string Origine, string Empreinte);

/// <summary>Fichier charte.json vu de la console : chemin, présence, erreurs (charte par défaut appliquée à la place).</summary>
public record FichierCharteDto(string Chemin, bool Present, string? Erreur, List<ProblemeCharte> Erreurs, List<ProblemeCharte> Avertissements, CharteFichier? Charte);

/// <summary>Charte en vigueur vue de la console d'administration.</summary>
public record EtatCharteDto(CharteFichier Charte, string Origine, string Empreinte, DateTimeOffset? ModifieLe, string? ModifiePar,
    FichierCharteDto Fichier, List<ProblemeCharte> Avertissements, string Commande);

/// <summary>Contenu d'un fichier charte.json à vérifier (import à blanc).</summary>
public record VerificationCharteSaisie(string? Contenu);

/// <summary>Résultat de la vérification d'un fichier : erreurs bloquantes, avertissements (clés inconnues, lisibilité) et charte lue.</summary>
public record VerificationCharteDto(bool Valide, List<ProblemeCharte> Erreurs, List<ProblemeCharte> Avertissements, CharteFichier? Charte);

/// <summary>
/// Charte visuelle de l'installation : fichier <c>charte.json</c> (valeur de départ, monté à côté de docker-compose.yml ou
/// posé à côté de gestion-mep.conf), remplacé par la charte enregistrée depuis la console ou <c>gestion-mep charte</c>
/// (table Reglages, clé <see cref="CleBase"/>) ; « rétablir » efface celle de la console et revient au fichier, sinon à la
/// charte par défaut. Comme les réglages, la base et le fichier sont relus toutes les 10 secondes : une commande s'applique
/// sans redémarrage.
/// </summary>
public sealed class ServiceCharte
{
    /// <summary>Clé de la charte de la console dans la table Reglages (hors catalogue des réglages).</summary>
    public const string CleBase = "Charte:Console";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<ServiceCharte> _journal;
    private string? _derniereErreur;

    public ServiceCharte(IServiceScopeFactory scopes, IConfiguration config, ILogger<ServiceCharte> journal)
    {
        _scopes = scopes;
        _config = config;
        _journal = journal;
        // Au démarrage : le fichier seul (la base n'est peut-être pas encore migrée) ; la base est lue au premier rafraîchissement.
        var fichier = LireFichier(CheminFichier(config));
        Signaler(fichier);
        Courant = Resoudre(null, fichier);
    }

    /// <summary>Charte en vigueur (feuille de style, empreinte, palette Excel comprises).</summary>
    public EtatCharte Courant { get; private set; }

    /// <summary>Relit la base et le fichier. Base injoignable : on garde la charte en vigueur.</summary>
    public async Task RechargerAsync(CancellationToken annulation = default)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var etat = await Charger(db, _config, annulation);
            Signaler(etat.Fichier);
            Courant = etat;
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            _journal.LogDebug(e, "Charte visuelle : base injoignable, charte en vigueur conservée.");
        }
    }

    private void Signaler(FichierCharteLu fichier)
    {
        if (fichier.Erreur == _derniereErreur) return;
        _derniereErreur = fichier.Erreur;
        if (fichier.Erreur is { } erreur)
            _journal.LogError("Fichier de charte {Chemin} invalide, ignoré (charte par défaut ou charte de la console) : {Erreur}", fichier.Chemin, erreur);
        else if (fichier.Present)
            _journal.LogInformation("Fichier de charte {Chemin} lu.", fichier.Chemin);
    }

    /// <summary>
    /// Emplacement du fichier : variable CHARTE_FICHIER (ou Charte:Fichier, ou CHARTE_FICHIER du fichier de configuration),
    /// sinon à côté du fichier de configuration : /etc/gestion-mep/charte.json (Linux et image Docker),
    /// C:\ProgramData\GestionMep\charte.json (Windows).
    /// </summary>
    public static string CheminFichier(IConfiguration config) =>
        config["Charte:Fichier"] is { Length: > 0 } c ? c
        : config["CHARTE_FICHIER"] is { Length: > 0 } v ? v
        : Path.Combine(Path.GetDirectoryName(FichierInstallation.Chemin) ?? ".", "charte.json");

    /// <summary>Lit le fichier ; absent, il ne compte pas. Illisible pour ce compte : erreur.</summary>
    public static FichierCharteLu LireFichier(string chemin)
    {
        string texte;
        try
        {
            if (!File.Exists(chemin)) return new(chemin, false, null, [], []);
            texte = File.ReadAllText(chemin);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(chemin, true, null, [new("lecture", "", $"Fichier {chemin} illisible : {e.Message}")], []);
        }
        var l = CharteVisuelle.Lire(texte);
        return new(chemin, true, l.Charte, l.Erreurs, l.Avertissements);
    }

    /// <summary>Charte en vigueur à partir de la base et du fichier.</summary>
    public static async Task<EtatCharte> Charger(MepDbContext db, IConfiguration config, CancellationToken annulation = default)
    {
        Reglage? ligne;
        try
        {
            ligne = await db.Reglages.AsNoTracking().FirstOrDefaultAsync(r => r.Cle == CleBase, annulation);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            ligne = null; // avant la première migration
        }
        return Resoudre(ligne, LireFichier(CheminFichier(config)));
    }

    /// <summary>Console &gt; fichier &gt; charte par défaut. Une valeur en base illisible (modifiée à la main) est ignorée.</summary>
    public static EtatCharte Resoudre(Reglage? ligne, FichierCharteLu fichier)
    {
        if (ligne is not null && CharteVisuelle.Lire(ligne.Valeur).Charte is { } console)
            return new(console, "console", ligne.ModifieLe, ligne.ModifiePar, fichier);
        if (fichier.Charte is { } duFichier) return new(duFichier, "fichier", null, null, fichier);
        return new(CharteVisuelle.Defaut, "defaut", null, null, fichier);
    }

    public static CharteDto Public(EtatCharte e) => new(CharteVisuelle.VersFichier(e.Charte), e.Origine, e.Empreinte);

    public static EtatCharteDto Dto(EtatCharte e) => new(CharteVisuelle.VersFichier(e.Charte), e.Origine, e.Empreinte, e.ModifieLe, e.ModifiePar,
        new(e.Fichier.Chemin, e.Fichier.Present, e.Fichier.Erreur, e.Fichier.Erreurs, e.Fichier.Avertissements,
            e.Fichier.Charte is { } c ? CharteVisuelle.VersFichier(c) : null),
        CharteVisuelle.Avertissements(e.Charte), Installation.Commande("charte exporter"));

    /// <summary>Vérifie un fichier sans rien enregistrer : erreurs, puis avertissements de lecture et de lisibilité.</summary>
    public static VerificationCharteDto Verifier(string? contenu)
    {
        var l = CharteVisuelle.Lire(contenu ?? "");
        var avertissements = l.Avertissements.Concat(l.Charte is { } c ? CharteVisuelle.Avertissements(c) : []).ToList();
        return new(l.Erreurs.Count == 0, l.Erreurs, avertissements, l.Charte is { } lue ? CharteVisuelle.VersFichier(lue) : null);
    }

    /// <summary>Lit une charte reçue de la console ou de la ligne de commande ; erreur 400 si elle est invalide.</summary>
    public static Charte Valider(LectureCharte lecture) =>
        lecture.Charte ?? throw new ErreurHttp(400, $"Charte invalide : {string.Join(" ", lecture.Erreurs.Select(e => e.Message))}",
            [.. lecture.Erreurs.Select(e => e.Message)]);

    /// <summary>
    /// Enregistre la charte de la console (elle remplace le fichier) et l'inscrit au journal d'administration avec ce qui
    /// change. <paramref name="acteur"/> null : ligne de commande.
    /// </summary>
    public static async Task<EtatCharte> Enregistrer(MepDbContext db, IConfiguration config, Charte charte, Acteur? acteur, DateTimeOffset maintenant)
    {
        var avant = await Charger(db, config);
        var json = CharteVisuelle.SerialiserCompact(charte);
        var ligne = await db.Reglages.FirstOrDefaultAsync(r => r.Cle == CleBase);
        if (ligne is null) db.Reglages.Add(ligne = new Reglage { Cle = CleBase });
        ligne.Valeur = json;
        ligne.ModifieLe = maintenant;
        ligne.ModifiePar = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande;
        JournalAdmin.Ecrire(db, acteur, "charte.modifiee", $"Charte visuelle « {charte.Nom} » : {Differences(avant.Charte, charte)}");
        await db.SaveChangesAsync();
        return Resoudre(ligne, avant.Fichier);
    }

    /// <summary>Efface la charte de la console : retour au fichier s'il est valide, sinon à la charte par défaut.</summary>
    public static async Task<EtatCharte> Retablir(MepDbContext db, IConfiguration config, Acteur? acteur)
    {
        var avant = await Charger(db, config);
        var ligne = await db.Reglages.FirstOrDefaultAsync(r => r.Cle == CleBase);
        var apres = Resoudre(null, avant.Fichier);
        if (ligne is not null)
        {
            db.Reglages.Remove(ligne);
            var source = apres.Origine == "fichier" ? $"charte du fichier {apres.Fichier.Chemin}" : "charte par défaut";
            JournalAdmin.Ecrire(db, acteur, "charte.retablie", $"Charte de la console effacée, retour à la {source} : {Differences(avant.Charte, apres.Charte)}");
            await db.SaveChangesAsync();
        }
        return apres;
    }

    /// <summary>« Primaire #4F46E5 → #0F766E, menu latéral pages → secondaire » ; « aucun changement de couleur ».</summary>
    public static string Differences(Charte avant, Charte apres)
    {
        var a = CharteVisuelle.VersFichier(avant);
        var b = CharteVisuelle.VersFichier(apres);
        var lignes = new List<string>();
        void Comparer(string libelle, string? x, string? y)
        {
            if (x != y) lignes.Add($"{libelle} {x ?? "calculée"} → {y ?? "calculée"}");
        }
        Comparer("primaire", a.Couleurs!.Primaire, b.Couleurs!.Primaire);
        Comparer("secondaire", a.Couleurs.Secondaire, b.Couleurs.Secondaire);
        Comparer("tertiaire", a.Couleurs.Tertiaire, b.Couleurs.Tertiaire);
        Comparer("menu latéral", a.MenuLateral, b.MenuLateral);
        Comparer("gris", a.Gris, b.Gris);
        foreach (var r in CharteVisuelle.Roles)
            Comparer($"{r} (thème sombre)", avant.Sombre.GetValueOrDefault(r)?.ToUpperInvariant(), apres.Sombre.GetValueOrDefault(r)?.ToUpperInvariant());
        foreach (var e in CharteVisuelle.EtatsConnus)
        {
            var x = avant.Etats.GetValueOrDefault(e)?.ToUpperInvariant();
            var y = apres.Etats.GetValueOrDefault(e)?.ToUpperInvariant();
            if (x != y) lignes.Add($"état {e} {x ?? "standard"} → {y ?? "standard"}");
        }
        if (a.Nom != b.Nom) lignes.Add($"nom « {a.Nom} » → « {b.Nom} »");
        return lignes.Count == 0 ? "aucun changement de couleur" : string.Join(", ", lignes);
    }
}

/// <summary>Relit la charte (base et fichier) au démarrage puis toutes les 10 secondes, comme les réglages.</summary>
public sealed class RafraichissementCharte(ServiceCharte charte, TimeProvider temps) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await charte.RechargerAsync(stoppingToken);
        using var minuterie = new PeriodicTimer(RafraichissementReglages.Intervalle, temps);
        while (await minuterie.WaitForNextTickAsync(stoppingToken))
            await charte.RechargerAsync(stoppingToken);
    }
}
