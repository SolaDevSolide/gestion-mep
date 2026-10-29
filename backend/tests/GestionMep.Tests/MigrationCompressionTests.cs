using System.Security.Cryptography;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration « CompressionPiecesJointes » (#112) sur une base qui a déjà des pièces jointes : leur place sur disque est celle
/// du fichier, elles restent « jamais examinées », lisibles telles quelles, puis la commande d'exploitation les compresse.
/// </summary>
[Collection("Api")]
public class MigrationCompressionTests : IDisposable
{
    private const string Base = "gestion_mep_tests_migration_compression";
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "pj-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task Les_pieces_existantes_gardent_leur_taille_puis_sont_compressees_par_la_commande()
    {
        using (var cn = new NpgsqlConnection(Serveur + ";Database=postgres"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"drop database if exists {Base} with (force)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"create database {Base}";
            cmd.ExecuteNonQuery();
        }
        var chaine = $"{Serveur};Database={Base}";
        var options = new DbContextOptionsBuilder<MepDbContext>().UseNpgsql(chaine).Options;
        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("RevisionsDesBlocs");

        // Deux pièces déposées avec la version précédente : un journal et un fichier incompressible, stockés tels quels.
        Directory.CreateDirectory(_dossier);
        var journal = CompressionStockageTests.Journal(500 * 1024);
        var aleatoire = CompressionStockageTests.Aleatoire(50 * 1024);
        Guid pjJournal = Guid.NewGuid(), pjAleatoire = Guid.NewGuid(), pjPerdue = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            var t = new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
            foreach (var (id, contenu, nom) in new[] { (pjJournal, journal, "a.log"), (pjAleatoire, aleatoire, "b.bin"), (pjPerdue, journal, "perdue.log") })
            {
                await MigrationJalon4Tests.Inserer(cn, "PiecesJointes", new()
                {
                    ["Id"] = id, ["NomFichier"] = nom, ["TypeContenu"] = "text/plain", ["Taille"] = (long)contenu.Length,
                    ["Sha256"] = Convert.ToHexStringLower(SHA256.HashData(contenu)), ["DeposeLe"] = t,
                });
                if (id != pjPerdue) await File.WriteAllBytesAsync(Path.Combine(_dossier, id.ToString("N")), contenu);
            }
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        var stockage = new StockagePiecesJointes(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stockage:Dossier"] = _dossier }).Build());
        await using (var db = new MepDbContext(options))
        {
            var pieces = await db.PiecesJointes.AsNoTracking().ToListAsync();
            Assert.All(pieces, p =>
            {
                Assert.Equal(p.Taille, p.TailleStockee);
                Assert.Null(p.Compression);
            });
            var etat = await CompressionPiecesJointes.Etat(db);
            Assert.Equal(new EtatStockageDto(3, 2L * journal.Length + aleatoire.Length, 2L * journal.Length + aleatoire.Length, 0, 3), etat);

            var bilan = await CompressionPiecesJointes.Compresser(db, stockage, essai: false, tout: false, null, CancellationToken.None);
            Assert.Equal((3, 1, 1, 1, 0, false), (bilan.Examinees, bilan.Traitees, bilan.Inchangees, bilan.Absentes, bilan.Echecs, bilan.Interrompu));
        }
        await using (var db = new MepDbContext(options))
        {
            var pieces = await db.PiecesJointes.AsNoTracking().ToDictionaryAsync(p => p.Id);
            Assert.Equal(CompressionPiece.Gzip, pieces[pjJournal].Compression);
            Assert.Equal(new FileInfo(Path.Combine(_dossier, pjJournal.ToString("N"))).Length, pieces[pjJournal].TailleStockee);
            Assert.Equal(CompressionPiece.Aucune, pieces[pjAleatoire].Compression);
            Assert.Null(pieces[pjPerdue].Compression); // fichier absent : laissée pour un passage suivant
            await using var flux = stockage.Ouvrir(pjJournal, journal.Length);
            using var copie = new MemoryStream();
            await flux!.CopyToAsync(copie);
            Assert.Equal(journal, copie.ToArray());
        }
    }
}
