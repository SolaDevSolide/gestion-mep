using System.Text.Json;
using GestionMep.Domain;
using static GestionMep.Tests.LigneDeCommandeConventionsTests;

namespace GestionMep.Tests;

/// <summary>
/// Périodes de gel et calendrier en ligne de commande (#129) : création dans le fuseau donné (enregistrée en UTC), liste,
/// suppression confirmée, export iCalendar ; sortie JSON et codes de retour des conventions.
/// </summary>
[Collection("Api")]
public class LigneDeCommandeCalendrierTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Une_periode_de_gel_se_cree_se_liste_et_se_supprime_avec_confirmation()
    {
        var creee = await Cli("periodes-gel", "creer", "Clôture comptable", "--debut", "2026-11-28 18:00", "--fin", "2026-12-02 08:00",
            "--systeme", "bdd", "--environnement", "Production", "--repetition", "fin-de-mois", "--motif", "Arrêté des comptes", "--json");
        Assert.Equal(0, creee.Code);
        var periode = creee.Json.GetProperty("resultat");
        // 18h à Paris en novembre : 17h UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 17, 0, 0, TimeSpan.Zero), periode.GetProperty("debut").GetDateTimeOffset());
        Assert.Equal("FinDeMois", periode.GetProperty("recurrence").GetString());
        Assert.Equal(["BDD"], periode.GetProperty("systemes").EnumerateArray().Select(s => s.GetString()));
        var id = periode.GetProperty("id").GetString()!;

        var liste = await Cli("periodes-gel", "lister");
        Assert.Equal(0, liste.Code);
        Assert.Contains("Clôture comptable", liste.Sortie);
        Assert.Contains("2026-11-28 18:00", liste.Sortie);
        Assert.Contains("fin-de-mois", liste.Sortie);
        Assert.Single((await Cli("periodes-gel", "lister", "--json")).Json.EnumerateArray());

        // Saisie invalide : 1 ; option inconnue : 2 ; sans confirmation : 4 ; introuvable : 5.
        Assert.Equal(1, (await Cli("periodes-gel", "creer", "CLOTURE comptable", "--debut", "2026-12-28", "--fin", "2026-12-29")).Code);
        Assert.Equal(1, (await Cli("periodes-gel", "creer", "À l'envers","--debut", "2026-12-02", "--fin", "2026-12-01")).Code);
        Assert.Equal(1, (await Cli("periodes-gel", "creer", "Répétition", "--debut", "2026-12-01", "--fin", "2026-12-02", "--repetition", "quotidienne")).Code);
        Assert.Equal(2, (await Cli("periodes-gel", "creer", "Sans fin", "--debut", "2026-12-01", "--durée", "2")).Code);
        Assert.Equal(5, (await Cli("periodes-gel", "creer", "Système inconnu", "--debut", "2026-12-01", "--fin", "2026-12-02", "--systeme", "ZZZ")).Code);
        Assert.Equal(4, (await Cli("periodes-gel", "supprimer", id[..8])).Code);
        Assert.Equal(5, (await Cli("periodes-gel", "supprimer", "ffffffff", "--oui")).Code);
        Assert.Equal(0, (await Cli("periodes-gel", "supprimer", "Clôture comptable", "--oui")).Code);
        Assert.Empty((await Cli("periodes-gel", "lister", "--json")).Json.EnumerateArray());

        var journal = (await Cli("journal", "--type", "gel", "--json")).Json.EnumerateArray().ToList();
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "gel.cree" && e.GetProperty("auteur").GetString() == "Ligne de commande");
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "gel.supprime");
    }

    [Fact]
    public async Task Le_calendrier_s_exporte_en_ics_dans_un_fichier_ou_sur_la_sortie_et_en_json()
    {
        var jour = Fuseaux.Jour(DateTimeOffset.UtcNow, Fuseaux.ParDefaut);
        var du = jour.AddDays(-400).ToString("yyyy-MM-dd");
        var au = jour.AddDays(-50).ToString("yyyy-MM-dd");
        Assert.Equal(0, (await Cli("periodes-gel", "creer", "Gel passé", "--debut", jour.AddDays(-60).ToString("yyyy-MM-dd"),
            "--fin", jour.AddDays(-59).ToString("yyyy-MM-dd"))).Code);

        var sortie = await Cli("calendrier", "exporter", "--du", du, "--au", au);
        Assert.Equal(0, sortie.Code);
        var evenements = ICalendarTests.Verifier(sortie.Sortie);
        Assert.Contains(evenements, e => e.V("UID").StartsWith("mep-", StringComparison.Ordinal));
        Assert.Contains(evenements, e => ICalendarTests.Texte(e.V("SUMMARY")) == "Gel : Gel passé");

        var fichier = Path.Combine(_api.DossierPieces, "export", "mep.ics");
        var ecrit = await Cli("calendrier", "exporter", "--du", du, "--au", au, "--sans-gels", "--sortie", fichier, "--json");
        Assert.Equal(0, ecrit.Code);
        var resultat = ecrit.Json.GetProperty("resultat");
        Assert.Equal(0, resultat.GetProperty("gels").GetInt32());
        Assert.Equal(evenements.Count(e => e.V("UID").StartsWith("mep-", StringComparison.Ordinal)), resultat.GetProperty("meps").GetInt32());
        ICalendarTests.Verifier(await File.ReadAllTextAsync(fichier));

        var json = (await Cli("calendrier", "exporter", "--du", du, "--au", au, "--etat", "reussie", "--json")).Json;
        Assert.All(json.GetProperty("meps").EnumerateArray(), m => Assert.Equal("Reussie", m.GetProperty("etat").GetString()));
        Assert.Equal(JsonValueKind.Array, json.GetProperty("gels").ValueKind);

        Assert.Equal(1, (await Cli("calendrier", "exporter", "--du", "2026-01-01", "--au", "2027-06-01")).Code);
        Assert.Equal(1, (await Cli("calendrier", "exporter", "--etat", "inconnu")).Code);
        Assert.Equal(5, (await Cli("calendrier", "exporter", "--systeme", "ZZZ")).Code);
        Assert.Equal(2, (await Cli("calendrier", "exporter", "--format", "csv")).Code);
    }
}
