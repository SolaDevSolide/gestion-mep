using System.Data;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>
/// Sauvegardes de la base et des pièces jointes, dans une archive .tar.gz autonome (sans pg_dump) : <c>manifest.json</c>
/// (dernière migration, colonnes et nombre de lignes de chaque table, empreinte de chaque fichier), une copie binaire
/// (COPY) par table, puis les fichiers des pièces jointes.
/// <para>
/// Avant une mise à jour du schéma (<see cref="MigrerAvecSauvegarde"/>) et avant une importation (<see cref="Importer"/>),
/// une sauvegarde est faite automatiquement. Une fois l'opération terminée, des contrôles comparent les données à celles
/// de la sauvegarde (aucune table disparue, aucune ligne en moins, aucun fichier manquant) : la sauvegarde n'est
/// supprimée que s'ils passent ; sinon elle est conservée et l'écart inscrit dans le journal d'administration.
/// </para>
/// </summary>
public sealed partial class Sauvegardes(string dossier, string dossierPieces, Action<string> info, Action<string> alerte)
{
    public const string DossierParDefaut = "/data/sauvegardes";
    private const int FormatArchive = 1;
    private static readonly JsonSerializerOptions JsonLisible = new() { WriteIndented = true };

    public string Dossier { get; } = dossier;

    public static string DossierConfigure(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Sauvegardes:Dossier"]) ? DossierParDefaut : config["Sauvegardes:Dossier"]!;

    public record Table(string Nom, string[] Colonnes, long Lignes);
    public record Fichier(string Nom, long Taille, string Sha256);
    /// <summary>
    /// <c>PiecesManquantes</c> : pièces jointes présentes en base mais dont le fichier manquait déjà sur disque lors de la
    /// sauvegarde (signalées à ce moment-là) ; null dans les archives qui n'en ont pas.
    /// </summary>
    public record Manifeste(int Format, string Application, string Migration, DateTimeOffset Le, List<Table> Tables, List<Fichier> Pieces,
        List<string>? PiecesManquantes = null);

    /// <summary>Limites de lecture d'une archive : nombre d'entrées, taille du manifeste, place laissée libre sur le disque.</summary>
    private const int EntreesMax = 1_000_000;
    private const long ManifesteMaxOctets = 64L * 1024 * 1024;
    private const long MargeDisqueOctets = 1024L * 1024 * 1024;
    /// <summary>Tentatives d'exportation quand une pièce jointe est supprimée pendant la sauvegarde.</summary>
    private const int TentativesExport = 3;

    /// <summary>État des données à un instant : lignes par table et fichiers de pièces jointes présents.</summary>
    public record Releve(Dictionary<string, long> Lignes, HashSet<string> Fichiers)
    {
        public static Releve De(Manifeste m) => new(m.Tables.ToDictionary(t => t.Nom, t => t.Lignes), m.Pieces.Select(p => p.Nom).ToHashSet());
    }

    public static string Horodatage() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    // ---- Mise à jour ----

    /// <summary>
    /// Applique les migrations en attente. Si la base contient déjà des données, sauvegarde d'abord, contrôle ensuite,
    /// et ne supprime la sauvegarde que si aucune donnée n'a été perdue. Renvoie false si un contrôle a échoué.
    /// </summary>
    public async Task<bool> MigrerAvecSauvegarde(MepDbContext db, string auteur)
    {
        var enAttente = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (enAttente.Count == 0) return true;
        if (!(await db.Database.GetAppliedMigrationsAsync()).Any())
        {
            await db.Database.MigrateAsync(); // base neuve : rien à protéger
            return true;
        }

        var fichier = Path.Combine(Dossier, $"avant-mise-a-jour-{Horodatage()}.tar.gz");
        info($"Mise à jour de la base ({enAttente.Count} migration(s)) : sauvegarde préalable dans {fichier}.");
        var avant = Releve.De(await Exporter(db, fichier));
        try
        {
            await db.Database.MigrateAsync();
        }
        catch
        {
            alerte($"La mise à jour de la base a échoué. Sauvegarde conservée : {fichier}. Pour revenir en arrière : reprendre la version précédente de l'application puis « gestion-mep importer {fichier} ».");
            throw;
        }
        var ecarts = Comparer(avant, await Relever(db));
        return await Conclure(db, fichier, ecarts, $"Mise à jour de la base ({string.Join(", ", enAttente)})", auteur);
    }

    // ---- Importation ----

    /// <summary>
    /// Remplace la base et les pièces jointes par le contenu d'une archive (restauration ou déménagement). Sauvegarde
    /// d'abord l'existant, remet le schéma à celui de l'archive, recharge les données, applique les migrations suivantes,
    /// puis contrôle que tout le contenu de l'archive est présent. L'application doit être arrêtée pendant l'opération.
    /// </summary>
    public async Task<bool> Importer(MepDbContext db, string archive, string auteur)
    {
        if (!File.Exists(archive)) throw new ErreurHttp(1, $"Archive introuvable : {archive}");
        var temporaire = Path.Combine(Dossier, $".import-{Guid.NewGuid():N}");
        try
        {
            info($"Lecture de l'archive {archive}…");
            var manifeste = await Extraire(archive, temporaire);
            if (!db.Database.GetMigrations().Contains(manifeste.Migration))
                throw new ErreurHttp(1, $"L'archive vient d'une version plus récente de l'application (migration {manifeste.Migration}) : mettre l'application à jour avant d'importer.");
            // Archive de la même version que la base : son contenu doit couvrir exactement le schéma actuel, vérifié avant
            // de toucher à quoi que ce soit (les autres cas sont vérifiés une fois le schéma de l'archive recréé, avant chargement).
            var appliquees = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            if (appliquees.LastOrDefault() == manifeste.Migration) await ExigerSchemaComplet(await Connexion(db), manifeste);

            string? sauvegarde = null;
            if (appliquees.Count > 0)
            {
                sauvegarde = Path.Combine(Dossier, $"avant-import-{Horodatage()}.tar.gz");
                info($"Sauvegarde des données actuelles dans {sauvegarde}.");
                await Exporter(db, sauvegarde);
            }

            info($"Remise de la base au schéma de l'archive ({manifeste.Migration}).");
            await ViderSchema(db);
            await db.GetService<IMigrator>().MigrateAsync(manifeste.Migration);
            await ChargerTables(db, manifeste, temporaire);
            RemplacerPieces(Path.Combine(temporaire, "pieces"));

            // Contrôle du chargement (contenu identique à l'archive), puis, même en cas d'écart pour que l'application
            // puisse démarrer, migrations postérieures à l'archive et nouveau contrôle.
            var attendu = Releve.De(manifeste);
            var ecarts = Comparer(attendu, await Relever(db), egalite: true);
            var enAttente = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (enAttente.Count > 0)
            {
                info($"Application de {enAttente.Count} migration(s) postérieure(s) à l'archive.");
                await db.Database.MigrateAsync();
                ecarts = ecarts.Union(Comparer(attendu, await Relever(db))).ToList();
            }
            // Chaque pièce jointe en base doit avoir son fichier (sauf celles déjà signalées manquantes à la sauvegarde).
            var sansFichier = (await PiecesSansFichier(db)).Except(manifeste.PiecesManquantes ?? []).ToList();
            if (sansFichier.Count > 0)
                ecarts.Add($"{sansFichier.Count} pièce(s) jointe(s) en base sans fichier : {string.Join(", ", sansFichier.Take(5))}{(sansFichier.Count > 5 ? "…" : "")}");
            var detail = $"Importation de {Path.GetFileName(archive)} ({manifeste.Tables.Sum(t => t.Lignes)} lignes, {manifeste.Pieces.Count} fichier(s))";
            return await Conclure(db, sauvegarde, ecarts, detail, auteur);
        }
        finally
        {
            if (Directory.Exists(temporaire)) Directory.Delete(temporaire, recursive: true);
        }
    }

    // ---- Exportation ----

    /// <summary>Écrit l'archive (dans un fichier « .partiel » renommé à la fin) ; renvoie son manifeste.</summary>
    public async Task<Manifeste> Exporter(MepDbContext db, string fichier)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fichier))!);
        var temporaire = Path.Combine(Dossier, $".export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaire);
        var partiel = fichier + ".partiel";
        try
        {
            var conn = await Connexion(db);
            List<Table> tables;
            List<Fichier> pieces;
            List<string> manquantes;
            string migration;
            // Base puis fichiers : une pièce supprimée entre les deux manquerait à l'archive. Chaque pièce de l'instantané doit
            // donc avoir son fichier ; sinon on recommence, et après plusieurs essais, les fichiers vraiment absents du disque
            // sont signalés et inscrits au manifeste.
            for (var essai = 1; ; essai++)
            {
                (migration, tables, var ids) = await Instantane(conn, temporaire);
                pieces = [];
                foreach (var chemin in FichiersPieces())
                {
                    await using var flux = File.OpenRead(chemin);
                    pieces.Add(new Fichier(Path.GetFileName(chemin), flux.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(flux))));
                }
                var presents = pieces.Select(p => p.Nom).ToHashSet();
                manquantes = ids.Where(id => !presents.Contains(id)).Order().ToList();
                if (manquantes.Count == 0 || essai >= TentativesExport) break;
                info($"Pièce(s) jointe(s) modifiée(s) pendant la sauvegarde : nouvel essai ({essai + 1}/{TentativesExport}).");
            }
            if (manquantes.Count > 0)
                alerte($"Sauvegarde : {manquantes.Count} pièce(s) jointe(s) en base sans fichier sur disque ({string.Join(", ", manquantes.Take(5))}{(manquantes.Count > 5 ? "…" : "")}). Elles sont signalées dans l'archive.");
            var manifeste = new Manifeste(FormatArchive, VersionApplication(), migration, DateTimeOffset.UtcNow, tables, pieces,
                manquantes.Count > 0 ? manquantes : null);

            await using (var cible = File.Create(partiel))
            await using (var gzip = new GZipStream(cible, CompressionLevel.Fastest))
            await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            {
                var json = Path.Combine(temporaire, "manifest.json");
                await File.WriteAllTextAsync(json, JsonSerializer.Serialize(manifeste, JsonLisible));
                await tar.WriteEntryAsync(json, "manifest.json");
                foreach (var t in tables) await tar.WriteEntryAsync(Path.Combine(temporaire, t.Nom + ".copy"), $"base/{t.Nom}.copy");
                foreach (var p in pieces) await tar.WriteEntryAsync(Path.Combine(dossierPieces, p.Nom), $"pieces/{p.Nom}");
            }
            File.Move(partiel, fichier, overwrite: true);
            info($"Sauvegarde écrite : {fichier} ({tables.Sum(t => t.Lignes)} lignes, {pieces.Count} fichier(s), {new FileInfo(fichier).Length / 1024} Ko).");
            return manifeste;
        }
        catch
        {
            File.Delete(partiel);
            throw;
        }
        finally
        {
            Directory.Delete(temporaire, recursive: true);
        }
    }

    /// <summary>
    /// Copie toutes les tables dans <paramref name="temporaire"/> au sein d'une même transaction (instantané cohérent) ; renvoie
    /// la dernière migration, les tables et les Id (format N) des pièces jointes de cet instantané.
    /// </summary>
    private static async Task<(string Migration, List<Table> Tables, List<string> Pieces)> Instantane(NpgsqlConnection conn, string temporaire)
    {
        var tables = new List<Table>();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var migration = await Scalaire<string?>(conn, """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1""")
            ?? throw new ErreurHttp(1, "Base sans migration appliquée : rien à sauvegarder.");
        var noms = await ListerTables(conn);
        foreach (var nom in noms)
        {
            var colonnes = await ListerColonnes(conn, nom);
            await using (var sortie = File.Create(Path.Combine(temporaire, nom + ".copy")))
            await using (var copie = await conn.BeginRawBinaryCopyAsync($"COPY {Q(nom)} ({string.Join(", ", colonnes.Select(Q))}) TO STDOUT (FORMAT BINARY)"))
                await copie.CopyToAsync(sortie);
            tables.Add(new Table(nom, colonnes, await Scalaire<long>(conn, $"SELECT count(*) FROM {Q(nom)}")));
        }
        var pieces = noms.Contains(TablePieces)
            ? (await Liste(conn, $"SELECT replace(\"Id\"::text, '-', '') FROM {Q(TablePieces)}")).ToList()
            : [];
        await tx.RollbackAsync();
        return (migration, tables, pieces);
    }

    /// <summary>Table des pièces jointes (nom du schéma EF Core).</summary>
    private const string TablePieces = "PiecesJointes";

    /// <summary>Id (format N) des pièces jointes en base dont le fichier est absent du disque.</summary>
    private async Task<List<string>> PiecesSansFichier(MepDbContext db)
    {
        var presents = FichiersPieces().Select(Path.GetFileName).ToHashSet();
        var ids = await Liste(await Connexion(db), $"SELECT replace(\"Id\"::text, '-', '') FROM {Q(TablePieces)}");
        return ids.Where(id => !presents.Contains(id)).Order().ToList();
    }

    /// <summary>
    /// L'archive doit couvrir exactement le schéma de sa migration : toutes les tables, chacune une seule fois, avec toutes
    /// ses colonnes (une archive incomplète viderait sinon les tables omises sans que les contrôles le voient).
    /// </summary>
    private static async Task ExigerSchemaComplet(NpgsqlConnection conn, Manifeste m)
    {
        var existantes = await ListerTables(conn);
        var doublon = m.Tables.GroupBy(t => t.Nom).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null) throw new ErreurHttp(1, $"Archive invalide : table {doublon.Key} présente deux fois.");
        var absentes = existantes.Except(m.Tables.Select(t => t.Nom)).ToList();
        if (absentes.Count > 0) throw new ErreurHttp(1, $"Archive incomplète : table(s) {string.Join(", ", absentes)} absente(s) du manifeste.");
        foreach (var t in m.Tables)
        {
            if (!existantes.Contains(t.Nom)) throw new ErreurHttp(1, $"Table {t.Nom} de l'archive absente du schéma {m.Migration}.");
            var colonnes = await ListerColonnes(conn, t.Nom);
            if (t.Colonnes.Length != t.Colonnes.Distinct().Count() || !colonnes.ToHashSet().SetEquals(t.Colonnes))
                throw new ErreurHttp(1, $"Archive invalide : colonnes de la table {t.Nom} différentes du schéma {m.Migration}.");
        }
    }

    // ---- Contrôles ----

    /// <summary>Lignes par table et fichiers de pièces jointes présents, lus dans la base et sur disque.</summary>
    public async Task<Releve> Relever(MepDbContext db)
    {
        var conn = await Connexion(db);
        var lignes = new Dictionary<string, long>();
        foreach (var nom in await ListerTables(conn)) lignes[nom] = await Scalaire<long>(conn, $"SELECT count(*) FROM {Q(nom)}");
        return new Releve(lignes, FichiersPieces().Select(Path.GetFileName).ToHashSet()!);
    }

    /// <summary>
    /// Écarts entre la référence et l'état constaté : table disparue, lignes en moins (ou différentes si
    /// <paramref name="egalite"/>), fichier de pièce jointe manquant. Liste vide : aucune donnée perdue.
    /// </summary>
    public static List<string> Comparer(Releve reference, Releve constate, bool egalite = false)
    {
        var ecarts = new List<string>();
        foreach (var (table, attendu) in reference.Lignes.OrderBy(l => l.Key))
        {
            if (!constate.Lignes.TryGetValue(table, out var n)) ecarts.Add($"table {table} disparue ({attendu} ligne(s))");
            else if (n < attendu || (egalite && n != attendu)) ecarts.Add($"table {table} : {attendu} ligne(s) attendue(s), {n} trouvée(s)");
        }
        var manquants = reference.Fichiers.Except(constate.Fichiers).ToList();
        if (manquants.Count > 0) ecarts.Add($"{manquants.Count} fichier(s) de pièce jointe manquant(s) : {string.Join(", ", manquants.Take(5))}{(manquants.Count > 5 ? "…" : "")}");
        return ecarts;
    }

    /// <summary>Supprime la sauvegarde si aucun écart, sinon la conserve ; trace le résultat dans le journal d'administration.</summary>
    private async Task<bool> Conclure(MepDbContext db, string? sauvegarde, List<string> ecarts, string operation, string auteur)
    {
        string type, detail;
        if (ecarts.Count == 0)
        {
            if (sauvegarde is not null) File.Delete(sauvegarde);
            type = "donnees.controle-reussi";
            detail = $"{operation} : contrôles réussis, aucune donnée perdue{(sauvegarde is null ? "" : " ; sauvegarde préalable supprimée")}.";
            info(detail);
        }
        else
        {
            type = "donnees.controle-echoue";
            detail = $"{operation} : contrôles en échec ({string.Join(" ; ", ecarts)}).{(sauvegarde is null ? "" : $" Sauvegarde conservée : {sauvegarde} (« gestion-mep importer » pour la recharger).")}";
            alerte(detail);
        }
        try
        {
            db.ChangeTracker.Clear();
            db.JournalAdmin.Add(new EvenementAdmin { Auteur = auteur, Type = type, Detail = detail });
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            alerte($"Résultat non inscrit dans le journal d'administration : {e.InnerException?.Message ?? e.Message}");
        }
        return ecarts.Count == 0;
    }

    // ---- Importation : étapes ----

    /// <summary>
    /// Extrait l'archive en n'acceptant que les entrées attendues, dans des limites de taille (nombre d'entrées, manifeste,
    /// espace disque : l'extraction s'arrête avant de remplir le disque), puis vérifie le manifeste et les empreintes.
    /// </summary>
    private static async Task<Manifeste> Extraire(string archive, string cible)
    {
        Directory.CreateDirectory(Path.Combine(cible, "base"));
        Directory.CreateDirectory(Path.Combine(cible, "pieces"));
        var budget = EspaceLibre(cible) is { } libre ? libre - MargeDisqueOctets : long.MaxValue;
        if (budget <= 0) throw new ErreurHttp(1, $"Espace disque insuffisant dans {cible} pour extraire l'archive (1 Go doit rester libre).");
        long extraits = 0;
        var entrees = 0;
        await using (var flux = File.OpenRead(archive))
        await using (var gzip = new GZipStream(flux, CompressionMode.Decompress))
        await using (var tar = new TarReader(gzip))
        {
            while (await tar.GetNextEntryAsync() is { } entree)
            {
                if (++entrees > EntreesMax) throw new ErreurHttp(1, $"Archive invalide : plus de {EntreesMax} entrées.");
                if (entree.EntryType is TarEntryType.Directory) continue;
                var nom = entree.Name;
                var valide = entree.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
                    && (nom == "manifest.json" || EntreeTable().IsMatch(nom) || EntreePiece().IsMatch(nom));
                if (!valide) throw new ErreurHttp(1, $"Archive invalide : entrée inattendue « {nom} ».");
                if (nom == "manifest.json" && entree.Length > ManifesteMaxOctets)
                    throw new ErreurHttp(1, $"Archive invalide : manifest.json dépasse {ManifesteMaxOctets / 1024 / 1024} Mo.");
                // La taille de chaque entrée est lue dans son en-tête, avant de l'écrire.
                extraits += entree.Length;
                if (extraits > budget)
                    throw new ErreurHttp(1, $"Espace disque insuffisant dans {cible} pour extraire l'archive (1 Go doit rester libre).");
                await entree.ExtractToFileAsync(Path.Combine(cible, nom), overwrite: false);
            }
        }
        var json = Path.Combine(cible, "manifest.json");
        if (!File.Exists(json)) throw new ErreurHttp(1, "Archive invalide : manifest.json absent.");
        Manifeste? lu;
        await using (var lecture = File.OpenRead(json))
        {
            try { lu = await JsonSerializer.DeserializeAsync<Manifeste>(lecture); }
            catch (JsonException) { lu = null; }
        }
        var m = lu ?? throw new ErreurHttp(1, "Archive invalide : manifest.json illisible.");
        if (m.Format != FormatArchive) throw new ErreurHttp(1, $"Format d'archive {m.Format} non pris en charge.");
        if (m.Tables is null || m.Pieces is null || m.Tables.Count == 0) throw new ErreurHttp(1, "Archive invalide : manifest.json incomplet.");
        if (m.Tables.Any(t => !EntreeTable().IsMatch($"base/{t.Nom}.copy")) || m.Pieces.Any(p => !NomPiece().IsMatch(p.Nom))
            || (m.PiecesManquantes ?? []).Any(p => !NomPiece().IsMatch(p)))
            throw new ErreurHttp(1, "Archive invalide : nom de table ou de pièce jointe inattendu dans manifest.json.");
        if (m.Tables.Select(t => t.Nom).Distinct().Count() != m.Tables.Count || m.Pieces.Select(p => p.Nom).Distinct().Count() != m.Pieces.Count)
            throw new ErreurHttp(1, "Archive invalide : table ou pièce jointe en double dans manifest.json.");
        foreach (var t in m.Tables)
            if (!File.Exists(Path.Combine(cible, "base", t.Nom + ".copy"))) throw new ErreurHttp(1, $"Archive incomplète : table {t.Nom} absente.");
        foreach (var p in m.Pieces)
        {
            var chemin = Path.Combine(cible, "pieces", p.Nom);
            if (!File.Exists(chemin)) throw new ErreurHttp(1, $"Archive incomplète : pièce jointe {p.Nom} absente.");
            await using var f = File.OpenRead(chemin);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(f)) != p.Sha256)
                throw new ErreurHttp(1, $"Archive altérée : empreinte de la pièce jointe {p.Nom} différente.");
        }
        return m;
    }

    /// <summary>Supprime toutes les tables du schéma (historique des migrations compris).</summary>
    private static async Task ViderSchema(MepDbContext db)
    {
        var conn = await Connexion(db);
        var tables = await Liste(conn, "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'");
        if (tables.Count == 0) return;
        await Executer(conn, $"DROP TABLE IF EXISTS {string.Join(", ", tables.Select(Q))} CASCADE");
    }

    /// <summary>Recharge chaque table (parents avant enfants, en une transaction), puis recale les compteurs d'identifiants.</summary>
    private static async Task ChargerTables(MepDbContext db, Manifeste m, string dossier)
    {
        var conn = await Connexion(db);
        await ExigerSchemaComplet(conn, m);
        var existantes = await ListerTables(conn);

        await using var tx = await conn.BeginTransactionAsync();
        await Executer(conn, $"TRUNCATE {string.Join(", ", existantes.Select(Q))} CASCADE");
        foreach (var t in await OrdreDeChargement(conn, m.Tables))
        {
            await using var source = File.OpenRead(Path.Combine(dossier, "base", t.Nom + ".copy"));
            await using var copie = await conn.BeginRawBinaryCopyAsync($"COPY {Q(t.Nom)} ({string.Join(", ", t.Colonnes.Select(Q))}) FROM STDIN (FORMAT BINARY)");
            await source.CopyToAsync(copie);
        }
        var compteurs = new List<(string Table, string Colonne)>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = current_schema() AND (is_identity = 'YES' OR column_default LIKE 'nextval(%')", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) compteurs.Add((r.GetString(0), r.GetString(1)));
        foreach (var (table, colonne) in compteurs)
        {
            await using var cmd = new NpgsqlCommand($"SELECT setval(pg_get_serial_sequence(@t, @c), COALESCE((SELECT MAX({Q(colonne)}) FROM {Q(table)}), 0) + 1, false)", conn);
            cmd.Parameters.AddWithValue("t", Q(table));
            cmd.Parameters.AddWithValue("c", colonne);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    /// <summary>Tables triées pour que chaque table référencée par une clé étrangère soit chargée avant celles qui la référencent.</summary>
    private static async Task<List<Table>> OrdreDeChargement(NpgsqlConnection conn, List<Table> tables)
    {
        var dependances = tables.ToDictionary(t => t.Nom, _ => new HashSet<string>());
        await using (var cmd = new NpgsqlCommand("""
            SELECT enfant.relname, parent.relname FROM pg_constraint k
            JOIN pg_class enfant ON enfant.oid = k.conrelid JOIN pg_class parent ON parent.oid = k.confrelid
            JOIN pg_namespace n ON n.oid = enfant.relnamespace
            WHERE k.contype = 'f' AND n.nspname = current_schema()
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                if (dependances.TryGetValue(r.GetString(0), out var d) && r.GetString(1) != r.GetString(0)) d.Add(r.GetString(1));

        var ordre = new List<Table>();
        var places = new HashSet<string>();
        while (ordre.Count < tables.Count)
        {
            var prete = tables.FirstOrDefault(t => !places.Contains(t.Nom) && dependances[t.Nom].All(p => places.Contains(p) || !dependances.ContainsKey(p)))
                ?? throw new ErreurHttp(1, "Clés étrangères circulaires : ordre de chargement impossible.");
            ordre.Add(prete);
            places.Add(prete.Nom);
        }
        return ordre;
    }

    /// <summary>Remplace les fichiers de pièces jointes par ceux de l'archive (les autres fichiers du dossier restent).</summary>
    private void RemplacerPieces(string source)
    {
        Directory.CreateDirectory(dossierPieces);
        foreach (var f in FichiersPieces()) File.Delete(f);
        foreach (var f in Directory.EnumerateFiles(source)) File.Move(f, Path.Combine(dossierPieces, Path.GetFileName(f)));
    }

    // ---- Outils ----

    /// <summary>Fichiers de pièces jointes : nommés d'après l'Id de la pièce (32 caractères hexadécimaux).</summary>
    private IEnumerable<string> FichiersPieces() =>
        Directory.Exists(dossierPieces)
            ? Directory.EnumerateFiles(dossierPieces).Where(f => NomPiece().IsMatch(Path.GetFileName(f))).Order()
            : [];

    private static async Task<NpgsqlConnection> Connexion(MepDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        return (NpgsqlConnection)db.Database.GetDbConnection();
    }

    /// <summary>Tables de données du schéma courant, hors tables internes d'EF Core (historique et verrou des migrations).</summary>
    private static async Task<List<string>> ListerTables(NpgsqlConnection conn) =>
        (await Liste(conn, "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE' ORDER BY table_name"))
        .Where(t => !t.StartsWith("__EF", StringComparison.Ordinal)).ToList();

    private static async Task<string[]> ListerColonnes(NpgsqlConnection conn, string table)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @t AND is_generated = 'NEVER' ORDER BY ordinal_position", conn);
        cmd.Parameters.AddWithValue("t", table);
        var colonnes = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) colonnes.Add(r.GetString(0));
        return [.. colonnes];
    }

    private static async Task<List<string>> Liste(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var valeurs = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) valeurs.Add(r.GetString(0));
        return valeurs;
    }

    private static async Task<T> Scalaire<T>(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? default! : (T)v;
    }

    private static async Task Executer(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Octets libres sur le disque d'un dossier ; null si on ne peut pas le savoir.</summary>
    private static long? EspaceLibre(string dossier)
    {
        try { return new DriveInfo(Path.GetFullPath(dossier)).AvailableFreeSpace; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>Identifiant SQL entre guillemets.</summary>
    private static string Q(string nom) => "\"" + nom.Replace("\"", "\"\"") + "\"";

    private static string VersionApplication()
    {
        var a = typeof(Sauvegardes).Assembly;
        return a.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "inconnue";
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex NomPiece();

    [GeneratedRegex("^base/[A-Za-z0-9_]{1,63}\\.copy$")]
    private static partial Regex EntreeTable();

    [GeneratedRegex("^pieces/[0-9a-f]{32}$")]
    private static partial Regex EntreePiece();
}
