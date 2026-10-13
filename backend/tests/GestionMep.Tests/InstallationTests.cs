using GestionMep.Api.Services;

namespace GestionMep.Tests;

/// <summary>Fichier de configuration de l'installation sans Docker : mêmes noms que le fichier .env.</summary>
public class InstallationTests : IDisposable
{
    private readonly string _fichier = Path.Combine(Path.GetTempPath(), $"gestion-mep-{Guid.NewGuid():N}.conf");
    public void Dispose() => File.Delete(_fichier);

    private Dictionary<string, string> Lire(string contenu)
    {
        File.WriteAllText(_fichier, contenu);
        return FichierInstallation.Lire(_fichier);
    }

    [Fact]
    public void Le_fichier_se_lit_comme_un_env_commentaires_et_guillemets_compris()
    {
        var valeurs = Lire("""
            # commentaire
            BASE_DE_DONNEES=Host=localhost;Port=5432;Database=gestion_mep;Username=mep;Password=a=b
              DEMO = true
            URL_PUBLIQUE="https://mep.entreprise.fr"
            LIEN_SIGNALEMENT=''
            ligne sans égal
            """);
        Assert.Equal("Host=localhost;Port=5432;Database=gestion_mep;Username=mep;Password=a=b", valeurs["BASE_DE_DONNEES"]);
        Assert.Equal("true", valeurs["DEMO"]);
        Assert.Equal("https://mep.entreprise.fr", valeurs["URL_PUBLIQUE"]);
        Assert.Equal("", valeurs["LIEN_SIGNALEMENT"]);
        Assert.Equal(4, valeurs.Count);
    }

    [Fact]
    public void Un_fichier_absent_ne_change_rien()
    {
        Assert.Empty(FichierInstallation.Lire(_fichier));
        Assert.Null(FichierInstallation.AdresseEcoute(new Dictionary<string, string>()));
    }

    [Fact]
    public void Les_noms_du_env_donnent_les_reglages_de_l_application()
    {
        var reglages = FichierInstallation.Reglages(Lire("""
            BASE_DE_DONNEES=Host=db
            PIECES_TAILLE_MAX_MO=50
            HTTPS_OBLIGATOIRE=false
            Logging__LogLevel__Default=Warning
            INCONNU=1
            DOSSIER_DONNEES=/var/lib/gestion-mep
            """));
        Assert.Equal("Host=db", reglages["ConnectionStrings:Mep"]);
        Assert.Equal("50", reglages["Stockage:TailleMaxMo"]);
        Assert.Equal("false", reglages["Securite:HttpsObligatoire"]);
        Assert.Equal("Warning", reglages["Logging:LogLevel:Default"]);
        Assert.Equal(Path.Combine("/var/lib/gestion-mep", "pieces-jointes"), reglages["Stockage:Dossier"]);
        Assert.Equal(Path.Combine("/var/lib/gestion-mep", "cles"), reglages["Cles:Dossier"]);
        Assert.Equal(Path.Combine("/var/lib/gestion-mep", "sauvegardes"), reglages["Sauvegardes:Dossier"]);
        Assert.Equal(Path.Combine("/var/lib/gestion-mep", "mise-a-jour"), reglages["MisesAJour:Dossier"]);
        Assert.DoesNotContain(reglages.Keys, k => k.Contains("INCONNU"));
    }

    [Theory]
    [InlineData("DEMO=false", "http://127.0.0.1:8080")]
    [InlineData("ADRESSE_ECOUTE=0.0.0.0\nPORT=9000", "http://0.0.0.0:9000")]
    [InlineData("ADRESSE_ECOUTE=::1", "http://[::1]:8080")]
    public void L_adresse_d_ecoute_vient_du_fichier(string contenu, string attendue) =>
        Assert.Equal(attendue, FichierInstallation.AdresseEcoute(Lire(contenu)));

    [Fact]
    public void Les_commandes_affichees_suivent_le_type_d_installation()
    {
        var commande = Installation.Commande("reglages lister");
        Assert.EndsWith("gestion-mep reglages lister", commande);
        if (Installation.EstDocker) Assert.StartsWith("docker compose exec app", commande);
        Assert.Contains("1.4.0", Installation.CommandeMiseAJour("1.4.0"));
    }
}
