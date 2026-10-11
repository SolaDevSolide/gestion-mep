using GestionMep.Api.Services;
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
