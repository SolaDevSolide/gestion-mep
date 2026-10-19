using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Fuseaux horaires (#98) : contrôle de la saisie et conversion unique, y compris aux changements d'heure.</summary>
public class FuseauxTests
{
    [Theory]
    [InlineData("Europe/Paris", "Europe/Paris")]
    [InlineData(" America/New_York ", "America/New_York")]
    [InlineData("UTC", "Etc/UTC")]
    [InlineData("Romance Standard Time", "Europe/Paris")] // identifiant Windows, converti
    public void Un_fuseau_connu_est_accepte_sous_son_identifiant_iana(string saisie, string attendu) =>
        Assert.Equal(attendu, Fuseaux.Normaliser(saisie));

    [Theory]
    [InlineData("Paris")]
    [InlineData("CET+1")]
    [InlineData("Europe/Pariss")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Un_fuseau_inconnu_est_refuse(string? saisie) => Assert.Null(Fuseaux.Normaliser(saisie));

    [Fact]
    public void Heure_locale_de_part_et_d_autre_du_passage_a_l_heure_d_ete()
    {
        // Paris, 29 mars 2026 : 02:00 → 03:00.
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), Fuseaux.Local(new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), "Europe/Paris"));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), Fuseaux.Local(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), "Europe/Paris"));
        Assert.Equal(DateTimeKind.Unspecified, Fuseaux.Local(DateTimeOffset.UtcNow, "Europe/Paris").Kind);
    }

    [Fact]
    public void Heure_locale_de_part_et_d_autre_du_retour_a_l_heure_d_hiver()
    {
        // Paris, 25 octobre 2026 : 03:00 → 02:00 ; 00:30 et 01:30 UTC s'affichent tous deux 02:30.
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), Fuseaux.Local(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), "Europe/Paris"));
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), Fuseaux.Local(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), "Europe/Paris"));
        // Le jour change à minuit local, pas à minuit UTC.
        Assert.Equal(new DateOnly(2026, 10, 26), Fuseaux.Jour(new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), "Europe/Paris"));
        Assert.Equal(new DateOnly(2026, 10, 25), Fuseaux.Jour(new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), "America/New_York"));
    }

    [Fact]
    public void Instant_d_une_heure_locale_aux_changements_d_heure()
    {
        var paris = "Europe/Paris";
        // Heures ordinaires : décalage d'hiver puis d'été.
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 20, 0, 0, TimeSpan.FromHours(1)), Fuseaux.Instant(new DateOnly(2026, 3, 28), new TimeOnly(20, 0), paris));
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 20, 0, 0, TimeSpan.FromHours(2)), Fuseaux.Instant(new DateOnly(2026, 3, 29), new TimeOnly(20, 0), paris));
        // 02:30 n'existe pas le 29 mars : décalé à 03:30 (heure d'été), soit 01:30 UTC.
        var sautee = Fuseaux.Instant(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), paris);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), sautee.ToUniversalTime());
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), Fuseaux.Local(sautee, paris));
        // 02:30 existe deux fois le 25 octobre : la première (heure d'été), soit 00:30 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), Fuseaux.Instant(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), paris).ToUniversalTime());
        // Sans heure : minuit local.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2)), Fuseaux.Instant(new DateOnly(2026, 10, 25), null, paris));
    }

    [Fact]
    public void Un_fuseau_enregistre_avant_le_controle_et_inconnu_retombe_sur_celui_par_defaut()
    {
        var hiver = new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(Fuseaux.Local(hiver, Fuseaux.ParDefaut), Fuseaux.Local(hiver, "Paris"));
        Assert.Equal(new DateOnly(2026, 1, 16), Fuseaux.Jour(hiver, null));
        Assert.Equal(Fuseaux.Trouver(Fuseaux.ParDefaut).BaseUtcOffset, Fuseaux.Trouver("CET+1").BaseUtcOffset);
    }
}
