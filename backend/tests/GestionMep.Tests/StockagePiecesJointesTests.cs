using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.Extensions.Configuration;

namespace GestionMep.Tests;

/// <summary>Stockage des pièces jointes : noms assainis et fichiers sans enregistrement en base.</summary>
public class StockagePiecesJointesTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "pj-" + Guid.NewGuid().ToString("N"));
    private readonly StockagePiecesJointes _stockage;

    public StockagePiecesJointesTests()
    {
        Directory.CreateDirectory(_dossier);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stockage:Dossier"] = _dossier }).Build();
        _stockage = new StockagePiecesJointes(config);
    }

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    [Fact]
    public void Le_nom_perd_les_caracteres_invisibles_d_inversion_d_ecriture()
    {
        // « rapport » + U+202E + « fdp.exe » s'afficherait « rapportexe.pdf ».
        Assert.Equal("rapportfdp.exe", StockagePiecesJointes.AssainirNom("rapport‮fdp.exe"));
        Assert.Equal("a b.txt", StockagePiecesJointes.AssainirNom("a​ b.txt"));
    }

    [Fact]
    public void Les_fichiers_sans_piece_en_base_et_anciens_sont_mis_de_cote()
    {
        var connu = Guid.NewGuid();
        var orphelinAncien = Guid.NewGuid();
        var orphelinRecent = Guid.NewGuid();
        var maintenant = DateTimeOffset.UtcNow;
        void Creer(Guid id, TimeSpan age)
        {
            var chemin = Path.Combine(_dossier, id.ToString("N"));
            File.WriteAllText(chemin, "x");
            File.SetLastWriteTimeUtc(chemin, (maintenant - age).UtcDateTime);
        }
        Creer(connu, TimeSpan.FromDays(1));
        Creer(orphelinAncien, TimeSpan.FromDays(1));
        Creer(orphelinRecent, TimeSpan.FromMinutes(5));
        File.WriteAllText(Path.Combine(_dossier, "autre.txt"), "x");

        var n = _stockage.IsolerOrphelins(new HashSet<Guid> { connu }, TimeSpan.FromHours(1), maintenant);

        Assert.Equal(1, n);
        Assert.True(File.Exists(Path.Combine(_dossier, connu.ToString("N"))));
        Assert.True(File.Exists(Path.Combine(_dossier, orphelinRecent.ToString("N"))), "un dépôt récent n'est jamais touché");
        Assert.True(File.Exists(Path.Combine(_dossier, "autre.txt")));
        Assert.False(File.Exists(Path.Combine(_dossier, orphelinAncien.ToString("N"))));
        Assert.True(File.Exists(Path.Combine(_dossier, StockagePiecesJointes.DossierOrphelins, orphelinAncien.ToString("N"))));
    }
}

/// <summary>
/// Compression des pièces jointes sur disque (#112) : décision (format déjà compressé, gain minimal), aller-retour sans perte,
/// essai sans écriture, décompression, lecture juste quel que soit l'état de la base.
/// </summary>
public class CompressionStockageTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "pj-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _reglages = new();
    private readonly StockagePiecesJointes _stockage;

    public CompressionStockageTests()
    {
        Directory.CreateDirectory(_dossier);
        _reglages["Stockage:Dossier"] = _dossier;
        _reglages["Stockage:TailleMaxMo"] = "50";
        _stockage = new StockagePiecesJointes(new ConfigurationBuilder().AddInMemoryCollection(_reglages).Build());
    }

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    /// <summary>Journal d'exécution : très répétitif, comme les fichiers que la compression vise.</summary>
    public static byte[] Journal(int octets)
    {
        var texte = new System.Text.StringBuilder();
        for (var i = 0; texte.Length < octets; i++)
            texte.Append($"2026-10-14T21:{i % 60:00}:{i * 7 % 60:00}.{i % 1000:000}Z [INFO ] Batch.Facturation - Lot {i * 37 % 9973} traité en {i * 13 % 4000} ms\n");
        return System.Text.Encoding.UTF8.GetBytes(texte.ToString())[..octets];
    }

    public static byte[] Aleatoire(int octets)
    {
        var b = new byte[octets];
        new Random(112).NextBytes(b);
        return b;
    }

    private async Task<(Guid Id, long Taille, string Sha)> Deposer(byte[] contenu)
    {
        var id = Guid.NewGuid();
        var (taille, sha) = await _stockage.Ecrire(id, new MemoryStream(contenu), CancellationToken.None);
        return (id, taille, sha);
    }

    private async Task<byte[]> Lire(Guid id, long taille)
    {
        await using var flux = _stockage.Ouvrir(id, taille);
        Assert.NotNull(flux);
        using var copie = new MemoryStream();
        await flux!.CopyToAsync(copie);
        return copie.ToArray();
    }

    private byte[] SurDisque(Guid id) => File.ReadAllBytes(Path.Combine(_dossier, id.ToString("N")));

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x14, 0x00 }, true)] // zip : docx, xlsx, pptx, odt
    [InlineData(new byte[] { 0x1f, 0x8b, 0x08, 0x00 }, true)] // gzip
    [InlineData(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }, true)] // 7z
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6f, 0x6d }, true)] // mp4 (ftyp)
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, true)] // webp
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04 }, true)] // mp3 (ID3)
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d, 0x31 }, false)] // pdf : souvent compressible (polices, pages)
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a }, false)] // png : les captures d'écran gagnent souvent 10 %
    [InlineData(new byte[] { 0xff, 0xd8, 0xff, 0xe0 }, false)] // jpeg : laissé à l'essai (gain presque toujours insuffisant)
    [InlineData(new byte[] { 0x42, 0x4d, 0x36, 0x00 }, false)] // bmp
    [InlineData(new byte[] { 0x32, 0x30, 0x32, 0x36 }, false)] // texte
    public void Les_formats_deja_compresses_sont_reconnus_a_leurs_premiers_octets(byte[] debut, bool compresse) =>
        Assert.Equal(compresse, StockagePiecesJointes.FormatDejaCompresse(debut));

    [Theory]
    [InlineData(1000, 900, 10, true)]
    [InlineData(1000, 901, 10, false)]
    [InlineData(1000, 1000, 1, false)]
    [InlineData(1000, 1200, 1, false)]
    [InlineData(0, 0, 10, false)]
    public void Un_fichier_n_est_garde_compresse_que_s_il_gagne_le_minimum(long original, long compresse, int gain, bool attendu) =>
        Assert.Equal(attendu, StockagePiecesJointes.VautLaPeine(original, compresse, gain));

    [Theory]
    [InlineData(300 * 1024)] // tenu en mémoire
    [InlineData(3 * 1024 * 1024)] // au-delà du Mo d'essai : fichier de travail
    public async Task Un_journal_est_stocke_compresse_et_relu_a_l_identique(int octets)
    {
        var contenu = Journal(octets);
        var (id, taille, sha) = await Deposer(contenu);

        var r = await _stockage.Compresser(id, taille, sha, CancellationToken.None);

        Assert.Equal(CompressionPiece.Gzip, r.Compression);
        Assert.True(r.TailleStockee < taille / 5, $"{r.TailleStockee} octets stockés pour {taille}");
        var disque = SurDisque(id);
        Assert.Equal(r.TailleStockee, disque.Length);
        Assert.Equal(new byte[] { 0x1f, 0x8b }, disque[..2]);
        Assert.Equal(contenu, await Lire(id, taille));
        Assert.Empty(Directory.GetFiles(_dossier, "*" + StockagePiecesJointes.SuffixeTravail));
        // Relisible hors de l'application (gzip -d) : flux gzip standard.
        await using var gz = new System.IO.Compression.GZipStream(new MemoryStream(disque), System.IO.Compression.CompressionMode.Decompress);
        using var copie = new MemoryStream();
        await gz.CopyToAsync(copie);
        Assert.Equal(contenu, copie.ToArray());
    }

    [Fact]
    public async Task Un_fichier_incompressible_ou_deja_compresse_reste_tel_quel()
    {
        var aleatoire = Aleatoire(200 * 1024);
        var (id, taille, sha) = await Deposer(aleatoire);
        Assert.Equal(new StockagePiecesJointes.Stockage(taille, CompressionPiece.Aucune), await _stockage.Compresser(id, taille, sha, CancellationToken.None));
        Assert.Equal(aleatoire, SurDisque(id));

        // Un « docx » (zip) très compressible en apparence n'est même pas essayé.
        var zip = (byte[])[0x50, 0x4b, 0x03, 0x04, .. Journal(100 * 1024)];
        (id, taille, sha) = await Deposer(zip);
        Assert.Equal(CompressionPiece.Aucune, (await _stockage.Compresser(id, taille, sha, CancellationToken.None)).Compression);
        Assert.Equal(zip, SurDisque(id));

        // Premier Mo incompressible : le reste n'est pas compressé, même s'il s'y prêterait.
        var mixte = (byte[])[.. Aleatoire(1024 * 1024), .. Journal(4 * 1024 * 1024)];
        (id, taille, sha) = await Deposer(mixte);
        Assert.Equal(CompressionPiece.Aucune, (await _stockage.Compresser(id, taille, sha, CancellationToken.None)).Compression);
        Assert.Equal(mixte.Length, SurDisque(id).Length);
    }

    [Fact]
    public async Task Le_gain_minimal_vient_du_reglage()
    {
        // Moitié texte, moitié aléatoire : environ 45 % de gain.
        var contenu = (byte[])[.. Journal(100 * 1024), .. Aleatoire(100 * 1024)];
        var (id, taille, sha) = await Deposer(contenu);
        _reglages["Stockage:CompressionGainMin"] = "60";
        var stockage = new StockagePiecesJointes(new ConfigurationBuilder().AddInMemoryCollection(_reglages).Build());
        Assert.Equal(60, stockage.GainMinimal);
        Assert.Equal(CompressionPiece.Aucune, (await stockage.Compresser(id, taille, sha, CancellationToken.None)).Compression);
        Assert.Equal(CompressionPiece.Gzip, (await _stockage.Compresser(id, taille, sha, CancellationToken.None)).Compression);
        Assert.Equal(contenu, await Lire(id, taille));

        _reglages["Stockage:Compression"] = "false";
        _reglages["Stockage:CompressionGainMin"] = "500";
        stockage = new StockagePiecesJointes(new ConfigurationBuilder().AddInMemoryCollection(_reglages).Build());
        Assert.False(stockage.CompressionActive);
        Assert.Equal(90, stockage.GainMinimal);
    }

    [Fact]
    public async Task L_essai_n_ecrit_rien_et_la_decompression_rend_le_fichier_d_origine()
    {
        var contenu = Journal(2 * 1024 * 1024);
        var (id, taille, sha) = await Deposer(contenu);

        var essai = await _stockage.Compresser(id, taille, sha, CancellationToken.None, essai: true);
        Assert.Equal(CompressionPiece.Gzip, essai.Compression);
        Assert.Equal(contenu, SurDisque(id));
        Assert.Empty(Directory.GetFiles(_dossier, "*" + StockagePiecesJointes.SuffixeTravail));

        var r = await _stockage.Compresser(id, taille, sha, CancellationToken.None);
        Assert.Equal(essai.TailleStockee, r.TailleStockee);
        // Déjà compressé (la base n'en sait rien, après un arrêt entre le fichier et la base) : reconnu, rien n'est refait.
        Assert.Equal(r, await _stockage.Compresser(id, taille, sha, CancellationToken.None));
        Assert.Equal(contenu, await Lire(id, taille));

        Assert.True(await _stockage.Decompresser(id, taille, sha, CancellationToken.None));
        Assert.Equal(contenu, SurDisque(id));
        Assert.False(await _stockage.Decompresser(id, taille, sha, CancellationToken.None));
        Assert.Equal(contenu, await Lire(id, taille));
    }

    [Fact]
    public async Task Une_compression_qui_ne_redonne_pas_l_empreinte_laisse_le_fichier_tel_quel()
    {
        var contenu = Journal(100 * 1024);
        var (id, taille, _) = await Deposer(contenu);
        var r = await _stockage.Compresser(id, taille, new string('0', 64), CancellationToken.None);
        Assert.Equal(new StockagePiecesJointes.Stockage(taille, null), r);
        Assert.Equal(contenu, SurDisque(id));
        Assert.Empty(Directory.GetFiles(_dossier, "*" + StockagePiecesJointes.SuffixeTravail));
    }

    [Fact]
    public void Un_fichier_de_travail_abandonne_est_supprime_au_demarrage()
    {
        var maintenant = DateTimeOffset.UtcNow;
        var ancien = Path.Combine(_dossier, Guid.NewGuid().ToString("N") + StockagePiecesJointes.SuffixeTravail);
        var recent = Path.Combine(_dossier, Guid.NewGuid().ToString("N") + StockagePiecesJointes.SuffixeTravail);
        File.WriteAllText(ancien, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTimeUtc(ancien, maintenant.AddDays(-1).UtcDateTime);

        Assert.Equal(0, _stockage.IsolerOrphelins(new HashSet<Guid>(), TimeSpan.FromHours(1), maintenant));
        Assert.False(File.Exists(ancien));
        Assert.True(File.Exists(recent), "une compression en cours n'est jamais touchée");
    }
}

/// <summary>Comparaison des numéros de version pour l'indicateur de mise à jour.</summary>
public class VersionApplicationTests
{
    [Theory]
    [InlineData("1.3.0", "1.2.9", true)]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("1.3.0", "0.0.0-dev", false)] // version de développement : pas de comparaison
    [InlineData("1.3.0-rc1", "1.2.0", false)] // préversion publiée : ignorée
    [InlineData(null, "1.2.0", false)]
    public void Une_mise_a_jour_est_disponible_seulement_pour_une_version_publiee_plus_recente(string? derniere, string courante, bool attendu) =>
        Assert.Equal(attendu, GestionMep.Api.Services.VersionApplication.EstPlusRecente(derniere, courante));
}
