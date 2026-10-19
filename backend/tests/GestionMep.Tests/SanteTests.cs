using System.Net;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionMep.Tests;

/// <summary>Sondes « vivant » (/api/sante) et « prête » (/api/sante/prete : base, schéma, surveillance des échéances).</summary>
[Collection("Api")]
public class SanteTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private sealed class Horloge : TimeProvider
    {
        public DateTimeOffset Maintenant { get; set; } = DateTimeOffset.Parse("2026-10-19T08:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Maintenant;
    }

    private static IConfiguration Config(bool surveillance) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Surveillance:Activer"] = surveillance ? "true" : "false" }).Build();

    [Fact]
    public async Task Les_deux_sondes_repondent_sans_session_quand_tout_va_bien()
    {
        var anonyme = _api.Client();
        Assert.Equal(HttpStatusCode.OK, (await anonyme.GetAsync("/api/sante")).StatusCode);

        var rep = await anonyme.GetAsync("/api/sante/prete");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        var corps = JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("prete", corps.GetProperty("statut").GetString());
        Assert.Equal("ok", corps.GetProperty("base").GetString());
        Assert.Equal("ok", corps.GetProperty("schema").GetString());
        // Surveillance coupée pendant les tests (ApiFixture) : signalée, sans rendre l'application indisponible.
        Assert.Equal("desactivee", corps.GetProperty("echeances").GetString());
    }

    [Fact]
    public async Task Une_base_mise_a_jour_par_une_version_plus_recente_rend_l_application_indisponible()
    {
        // Retour à une version précédente sans recharger la sauvegarde : la base connaît une migration que ce code ignore.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.Database.ExecuteSqlRawAsync("""insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion") values ('29991231000000_Future', '10.0.0')""");
        }
        try
        {
            var rep = await _api.Client().GetAsync("/api/sante/prete");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rep.StatusCode);
            var corps = JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("indisponible", corps.GetProperty("statut").GetString());
            Assert.Equal("plus-recent", corps.GetProperty("schema").GetString());
            // La sonde « vivant » ne regarde pas la base.
            Assert.Equal(HttpStatusCode.OK, (await _api.Client().GetAsync("/api/sante")).StatusCode);
        }
        finally
        {
            using var scope = _api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MepDbContext>().Database
                .ExecuteSqlRawAsync("""delete from "__EFMigrationsHistory" where "MigrationId" = '29991231000000_Future'""");
        }
    }

    [Fact]
    public async Task Base_injoignable_signalee_par_la_sonde_prete()
    {
        var options = new DbContextOptionsBuilder<MepDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=absente;Username=personne;Password=x;Timeout=2").Options;
        await using var db = new MepDbContext(options);

        var etat = await new SantePrete(TimeProvider.System, Config(surveillance: false)).Verifier(db, CancellationToken.None);

        Assert.Equal(new SantePreteDto("indisponible", "injoignable", "inconnu", "desactivee"), etat);
    }

    [Fact]
    public async Task Surveillance_des_echeances_arretee_depuis_plus_de_cinq_minutes_rend_l_application_indisponible()
    {
        var horloge = new Horloge();
        var disponibilite = new SantePrete(horloge, Config(surveillance: true));
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();

        // Juste après le démarrage, la surveillance n'a pas encore tourné : elle a cinq minutes pour le faire.
        Assert.Equal("prete", (await disponibilite.Verifier(db, CancellationToken.None)).Statut);
        horloge.Maintenant += TimeSpan.FromMinutes(6);
        var enRetard = await disponibilite.Verifier(db, CancellationToken.None);
        Assert.Equal(("indisponible", "en-retard"), (enRetard.Statut, enRetard.Echeances));

        // Un passage de la surveillance (réussi, même sans MEP à basculer) rétablit la sonde.
        var surveillance = new SurveillanceEcheances(_api.Services.GetRequiredService<IServiceScopeFactory>(), horloge,
            NullLogger<SurveillanceEcheances>.Instance, disponibilite);
        await surveillance.Verifier(CancellationToken.None);
        Assert.Equal(new SantePreteDto("prete", "ok", "ok", "ok"), await disponibilite.Verifier(db, CancellationToken.None));
    }
}
