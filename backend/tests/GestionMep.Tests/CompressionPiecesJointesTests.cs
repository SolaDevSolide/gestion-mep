using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Compression des pièces jointes (#112), de bout en bout : un dépôt compressible est stocké compressé et téléchargé à
/// l'identique, les quotas comptent la place réelle, les réglages s'appliquent sans redémarrage, la commande d'exploitation
/// compresse les pièces déjà déposées (essai, reprise, décompression) et une sauvegarde restaurée garde les fichiers lisibles.
/// </summary>
[Collection("Api")]
public class CompressionPiecesJointesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static MultipartFormDataContent Fichier(string nom, byte[] contenu, string type)
    {
        var fichier = new ByteArrayContent(contenu);
        fichier.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { fichier, "fichier", nom } };
    }

    private static async Task<JsonElement> Deposer(HttpClient c, Guid mep, string nom, byte[] contenu, string type = "text/plain")
    {
        await AidesApi.Ok(await c.PostAsync($"/api/meps/{mep}/pieces-jointes", Fichier(nom, contenu, type)));
        return (await AidesApi.Ok(await c.GetAsync($"/api/meps/{mep}/pieces-jointes"))).EnumerateArray()
            .Last(p => p.GetProperty("nomFichier").GetString() == nom);
    }

    private string SurDisque(Guid id) => Path.Combine(_api.DossierPieces, id.ToString("N"));

    private static async Task Reglage(HttpClient admin, string nom, string valeur) =>
        await AidesApi.Ok(await admin.PutAsJsonAsync($"/api/admin/reglages/{nom}", new { valeur }));

    private static async Task<(int Code, string Sortie, string Erreur)> Cli(params string[] args)
    {
        var sortie = new StringWriter();
        var erreur = new StringWriter();
        var (avant, avantErreur) = (Console.Out, Console.Error);
        Console.SetOut(sortie);
        Console.SetError(erreur);
        try
        {
            return (await LigneDeCommande.Executer(args), sortie.ToString(), erreur.ToString());
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
        }
    }

    [Fact]
    public async Task Un_journal_depose_est_stocke_compresse_et_telecharge_a_l_identique()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var journal = CompressionStockageTests.Journal(600 * 1024);

        var piece = await Deposer(alice, id, "deploiement.log", journal);

        Assert.Equal(journal.Length, piece.GetProperty("taille").GetInt64());
        Assert.Equal("gzip", piece.GetProperty("compression").GetString());
        var stockee = piece.GetProperty("tailleStockee").GetInt64();
        Assert.True(stockee < journal.Length / 5, $"{stockee} octets stockés");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(journal)), piece.GetProperty("sha256").GetString());
        var pid = piece.GetProperty("id").GetGuid();
        Assert.Equal(stockee, new FileInfo(SurDisque(pid)).Length);

        // Téléchargement : octets, nom, type et taille d'origine.
        var rep = await alice.GetAsync($"/api/pieces-jointes/{pid}/contenu");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        Assert.Equal(journal, await rep.Content.ReadAsByteArrayAsync());
        Assert.Equal(journal.Length, rep.Content.Headers.ContentLength);
        Assert.Equal("text/plain", rep.Content.Headers.ContentType?.MediaType);
        Assert.Equal("deploiement.log", rep.Content.Headers.ContentDisposition?.FileNameStar ?? rep.Content.Headers.ContentDisposition?.FileName);
        Assert.Null(rep.Content.Headers.ContentEncoding.FirstOrDefault());

        // Fichier incompressible : stocké tel quel.
        var aleatoire = CompressionStockageTests.Aleatoire(100 * 1024);
        var brut = await Deposer(alice, id, "photo.jpg", aleatoire, "image/jpeg");
        Assert.Equal(JsonValueKind.Null, brut.GetProperty("compression").ValueKind);
        Assert.Equal(aleatoire.Length, brut.GetProperty("tailleStockee").GetInt64());
        Assert.Equal(aleatoire, await alice.GetByteArrayAsync($"/api/pieces-jointes/{brut.GetProperty("id").GetGuid()}/contenu"));

        // Occupation vue de la console d'administration.
        var etat = await AidesApi.Ok(await alice.GetAsync("/api/admin/pieces-jointes/stockage"));
        Assert.Equal(2, etat.GetProperty("pieces").GetInt32());
        Assert.Equal(1, etat.GetProperty("compressees").GetInt32());
        Assert.Equal(journal.Length + aleatoire.Length, etat.GetProperty("tailleFichiers").GetInt64());
        Assert.Equal(stockee + aleatoire.Length, etat.GetProperty("tailleStockee").GetInt64());
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.GetAsync("/api/admin/pieces-jointes/stockage")).StatusCode);
    }

    [Fact]
    public async Task Les_quotas_comptent_la_place_reellement_occupee()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        await Reglage(alice, "pieces-quota-mep-mo", "1");

        // Trois journaux de 700 Ko (2,1 Mo) tiennent dans 1 Mo une fois compressés.
        for (var i = 0; i < 3; i++) await Deposer(alice, id, $"journal-{i}.log", CompressionStockageTests.Journal(700 * 1024));
        var occupe = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray()
            .Sum(p => p.GetProperty("tailleStockee").GetInt64());
        Assert.True(occupe < 1024 * 1024 / 2, $"{occupe} octets occupés");

        // Sans compression, un fichier un peu plus gros que la place restante est refusé.
        await Reglage(alice, "pieces-compression", "false");
        var refus = await alice.PostAsync($"/api/meps/{id}/pieces-jointes",
            Fichier("journal-3.log", CompressionStockageTests.Journal((int)(1024 * 1024 - occupe + 10 * 1024)), "text/plain"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);

        // Un petit fichier passe, stocké tel quel et jamais examiné (la commande d'exploitation le reprendra).
        var petit = await Deposer(alice, id, "petit.log", CompressionStockageTests.Journal(50 * 1024));
        Assert.Equal(JsonValueKind.Null, petit.GetProperty("compression").ValueKind);
        Assert.Equal(50 * 1024, petit.GetProperty("tailleStockee").GetInt64());
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        Assert.Null((await db.PiecesJointes.SingleAsync(p => p.Id == petit.GetProperty("id").GetGuid())).Compression);
        Assert.Equal(3, await db.PiecesJointes.CountAsync(p => p.Compression == CompressionPiece.Gzip));
    }

    [Fact]
    public async Task La_commande_compresse_les_pieces_deja_deposees_a_l_essai_puis_pour_de_bon_et_sait_les_decompresser()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        await Reglage(alice, "pieces-compression", "false");
        var journal = CompressionStockageTests.Journal(400 * 1024);
        var aleatoire = CompressionStockageTests.Aleatoire(80 * 1024);
        var pjJournal = (await Deposer(alice, id, "a.log", journal)).GetProperty("id").GetGuid();
        var pjAleatoire = (await Deposer(alice, id, "b.bin", aleatoire, "application/octet-stream")).GetProperty("id").GetGuid();
        var pjCsv = (await Deposer(alice, id, "c.csv", journal[..(200 * 1024)], "text/csv")).GetProperty("id").GetGuid();
        var etat = await Cli("pieces-jointes", "etat", "--json");
        Assert.Equal(0, etat.Code);
        Assert.Equal(3, JsonDocument.Parse(etat.Sortie).RootElement.GetProperty("nonExaminees").GetInt32());

        // Essai : le gain est annoncé, rien ne change.
        var essai = await Cli("pieces-jointes", "compresser", "--essai");
        Assert.Equal(0, essai.Code);
        Assert.Contains("Essai, rien n'a été modifié : 3 pièce(s) examinée(s) sur 3, 2 à compresser, 1 laissée(s) telle(s) quelle(s).", essai.Sortie);
        Assert.Equal(journal.Length, new FileInfo(SurDisque(pjJournal)).Length);

        // Pour de bon : avancement sur la sortie d'erreur, bilan, journal d'administration.
        var passage = await Cli("pieces-jointes", "compresser");
        Assert.Equal(0, passage.Code);
        Assert.Contains("3 pièce(s) examinée(s) sur 3, 2 compressée(s), 1 laissée(s) telle(s) quelle(s).", passage.Sortie);
        Assert.Contains("3/3 pièce(s) examinée(s) (100 %)", passage.Erreur);
        Assert.True(new FileInfo(SurDisque(pjJournal)).Length < journal.Length / 5);
        Assert.Equal(aleatoire.Length, new FileInfo(SurDisque(pjAleatoire)).Length);
        Assert.Equal(journal, await alice.GetByteArrayAsync($"/api/pieces-jointes/{pjJournal}/contenu"));
        Assert.Equal(journal[..(200 * 1024)], await alice.GetByteArrayAsync($"/api/pieces-jointes/{pjCsv}/contenu"));
        var liste = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray().ToList();
        Assert.Equal("gzip", liste.Single(p => p.GetProperty("id").GetGuid() == pjJournal).GetProperty("compression").GetString());
        var journalAdmin = (await AidesApi.Ok(await alice.GetAsync("/api/admin/journal"))).EnumerateArray().ToList();
        Assert.Contains(journalAdmin, e => e.GetProperty("type").GetString() == "pieces.compression"
            && e.GetProperty("detail").GetString()!.Contains("2 compressée(s) sur 3 examinée(s)"));

        // Idempotente : plus rien à examiner ; --tout réexamine la pièce laissée telle quelle, sans la changer.
        Assert.Contains("Aucune pièce jointe à examiner", (await Cli("pieces-jointes", "compresser")).Sortie);
        var tout = JsonDocument.Parse((await Cli("pieces-jointes", "compresser", "--tout", "--json")).Sortie).RootElement;
        Assert.Equal(1, tout.GetProperty("examinees").GetInt32());
        Assert.Equal(1, tout.GetProperty("inchangees").GetInt32());

        // Décompression : fichiers d'origine sur le disque, pièces de nouveau « jamais examinées ».
        var retour = await Cli("pieces-jointes", "decompresser");
        Assert.Equal(0, retour.Code);
        Assert.Contains("2 décompressée(s)", retour.Sortie);
        Assert.Equal(journal, File.ReadAllBytes(SurDisque(pjJournal)));
        Assert.Equal(journal, await alice.GetByteArrayAsync($"/api/pieces-jointes/{pjJournal}/contenu"));
        etat = await Cli("pieces-jointes", "etat", "--json");
        var e = JsonDocument.Parse(etat.Sortie).RootElement;
        Assert.Equal(0, e.GetProperty("compressees").GetInt32());
        Assert.Equal(e.GetProperty("tailleFichiers").GetInt64(), e.GetProperty("tailleStockee").GetInt64());

        Assert.Equal(2, (await Cli("pieces-jointes", "compresser", "--inconnue")).Code);
        Assert.Equal(2, (await Cli("pieces-jointes")).Code);
    }

    [Fact]
    public async Task Une_piece_reste_lisible_si_la_base_ignore_que_son_fichier_est_compresse()
    {
        // Arrêt de la commande entre le remplacement du fichier et l'écriture en base : la base dit « non examinée ».
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var journal = CompressionStockageTests.Journal(300 * 1024);
        var pid = (await Deposer(alice, id, "a.log", journal)).GetProperty("id").GetGuid();
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.PiecesJointes.Where(p => p.Id == pid)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Compression, (string?)null).SetProperty(p => p.TailleStockee, journal.Length));
        }
        Assert.Equal(journal, await alice.GetByteArrayAsync($"/api/pieces-jointes/{pid}/contenu"));

        // La commande remet la base d'accord avec le fichier, sans le recompresser.
        var passage = await Cli("pieces-jointes", "compresser");
        Assert.Contains("1 compressée(s)", passage.Sortie);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var piece = await db.PiecesJointes.SingleAsync(p => p.Id == pid);
            Assert.Equal(CompressionPiece.Gzip, piece.Compression);
            Assert.Equal(new FileInfo(SurDisque(pid)).Length, piece.TailleStockee);
        }
        Assert.Equal(journal, await alice.GetByteArrayAsync($"/api/pieces-jointes/{pid}/contenu"));
    }

    [Fact]
    public async Task Une_sauvegarde_restauree_garde_les_pieces_compressees_lisibles()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var journal = CompressionStockageTests.Journal(300 * 1024);
        var pid = (await Deposer(alice, id, "a.log", journal)).GetProperty("id").GetGuid();
        var archive = Path.Combine(_api.DossierPieces, "sauvegardes", "compression.tar.gz");
        var messages = new List<string>();
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var sauvegardes = new Sauvegardes(Path.GetDirectoryName(archive)!, _api.DossierPieces, messages.Add, messages.Add);
            await sauvegardes.Exporter(db, archive);
            File.Delete(SurDisque(pid));
            Assert.True(await sauvegardes.Importer(db, archive, "test"), string.Join("\n", messages));
        }
        Assert.Equal(journal, await alice.GetByteArrayAsync($"/api/pieces-jointes/{pid}/contenu"));
        var piece = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray().Single();
        Assert.Equal("gzip", piece.GetProperty("compression").GetString());
    }
}
