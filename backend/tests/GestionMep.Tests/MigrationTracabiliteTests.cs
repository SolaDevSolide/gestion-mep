using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration « ApprobationEtTracabilite » (#94) sur une base qui a des MEP : les compteurs d'incidents (échecs d'étape, tests de
/// recette en échec saisis ou refusés à l'import Excel) et l'étape des événements d'exécution sont repris une fois du journal ;
/// l'état « réussie avec incidents » est ensuite calculé par la même règle, à partir des compteurs.
/// </summary>
[Collection("Api")]
public class MigrationTracabiliteTests
{
    private const string Base = "gestion_mep_tests_migration_tracabilite";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task Les_compteurs_d_incidents_et_les_etapes_du_journal_sont_repris_une_fois()
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
            await db.GetService<IMigrator>().MigrateAsync("AnnulationReportEchecEtPilotage");

        Guid avecEchec = Guid.NewGuid(), recetteKo = Guid.NewGuid(), sansIncident = Guid.NewGuid();
        Guid e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), autreE1 = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
        long id = 0;
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            async Task Journal(Guid mep, string type, string detail) =>
                await MigrationJalon4Tests.Inserer(cn, "Journal", new() { ["Id"] = ++id, ["MepId"] = mep, ["Quand"] = t.AddMinutes(id), ["Type"] = type, ["Detail"] = detail });

            // Une étape en échec puis reprise et réussie : réussie avec incidents.
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = avecEchec, ["Outil"] = "Avec échec", ["Phase"] = (int)Phase.Cloturee, ["Issue"] = (int)IssueMep.Reussie,
            });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e1, ["MepId"] = avecEchec, ["Code"] = "E0001", ["Etat"] = (int)EtatEtape.Reussie });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e2, ["MepId"] = avecEchec, ["Code"] = "E0002", ["Etat"] = (int)EtatEtape.Reussie });
            await Journal(avecEchec, "etape.demarree", "E0001 démarrée par Bob");
            await Journal(avecEchec, "etape.echec", "E0001 en échec : erreur 503");
            await Journal(avecEchec, "etape.reprise", "E0001 : reprise autorisée");
            await Journal(avecEchec, "etape.demarree", "E0001 démarrée par Bob");
            await Journal(avecEchec, "etape.reussie", "E0001 réussie : OK");
            await Journal(avecEchec, "etape.demarree", "E0002 démarrée par Alice");
            await Journal(avecEchec, "etape.reussie", "E0002 réussie : OK");

            // Étapes sans échec, mais des tests de recette en échec (saisi, puis refusés dans un classeur importé).
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = recetteKo, ["Outil"] = "Recette KO", ["Phase"] = (int)Phase.Cloturee, ["Issue"] = (int)IssueMep.Reussie,
            });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = autreE1, ["MepId"] = recetteKo, ["Code"] = "E0001", ["Etat"] = (int)EtatEtape.Reussie });
            await Journal(recetteKo, "etape.reussie", "E0001 réussie : OK");
            await Journal(recetteKo, "recette.test", "Recette « Portail » étape 1 en échec : page blanche");
            await Journal(recetteKo, "recette.test", "Recette « Portail » étape 1 conforme : page affichée");
            await Journal(recetteKo, "recette.import", "Validation de la recette importée d'Excel : 3 étape(s) validée(s), 2 refusée(s)");
            await Journal(recetteKo, "recette.import", "Validation de la recette importée d'Excel : 1 étape(s) validée(s), 0 refusée(s)");

            // Rien d'anormal : réussie.
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = sansIncident, ["Outil"] = "Sans incident", ["Phase"] = (int)Phase.Cloturee, ["Issue"] = (int)IssueMep.Reussie,
            });
            await Journal(sansIncident, "recette.test", "Recette « API » étape 2 conforme : en échec : rien (le mot ne compte pas ici)");
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var lecture = new MepDbContext(options);
        var meps = await lecture.Meps.ToDictionaryAsync(m => m.Id);
        Assert.Equal((1, 0), (meps[avecEchec].EchecsEtapes, meps[avecEchec].EchecsRecette));
        Assert.Equal((0, 3), (meps[recetteKo].EchecsEtapes, meps[recetteKo].EchecsRecette));
        Assert.Equal((0, 0), (meps[sansIncident].EchecsEtapes, meps[sansIncident].EchecsRecette));
        Assert.Equal(EtatMep.AvecIncidents, Regles.EtatDeLaMep(meps[avecEchec]));
        Assert.Equal(EtatMep.AvecIncidents, Regles.EtatDeLaMep(meps[recetteKo]));
        Assert.Equal(EtatMep.Reussie, Regles.EtatDeLaMep(meps[sansIncident]));
        Assert.All(meps.Values, m =>
        {
            Assert.Empty(m.ResolutionsFigees);
            Assert.Null(m.ResolutionsFigeesLe);
            Assert.Null(m.EcheanceSignaleeLe);
        });

        // Étape des événements d'exécution : retrouvée par son code, dans la MEP de l'événement.
        var journal = await lecture.Journal.OrderBy(j => j.Id).ToListAsync();
        Assert.All(journal.Where(j => j.MepId == avecEchec && j.Type is "etape.demarree" or "etape.reussie" or "etape.echec"),
            j => Assert.Equal(j.Detail.StartsWith("E0001") ? e1 : e2, j.EtapeId));
        Assert.Equal(autreE1, journal.Single(j => j.MepId == recetteKo && j.Type == "etape.reussie").EtapeId);
        Assert.Null(journal.Single(j => j.Type == "etape.reprise").EtapeId);
        Assert.All(journal.Where(j => j.Type.StartsWith("recette.")), j => Assert.Null(j.EtapeId));
    }
}
