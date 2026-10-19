using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration « AnnulationReportEchecEtPilotage » (#93) sur une base qui a des MEP : le premier Go est repris du journal (le plus
/// ancien), à défaut du dernier Go connu ; les phases et issues existantes ne changent pas ; rien n'est annulé, reporté ni repris
/// en secours, et aucune étape n'est marquée « ajoutée après le Go ».
/// </summary>
[Collection("Api")]
public class MigrationCycleDeVieTests
{
    private const string Base = "gestion_mep_tests_migration_cycle_de_vie";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task Le_premier_go_est_repris_du_journal_et_les_etats_existants_ne_changent_pas()
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
            await db.GetService<IMigrator>().MigrateAsync("IndexRechercheAvancee");

        Guid relancee = Guid.NewGuid(), jamais = Guid.NewGuid(), repliee = Guid.NewGuid(), sansJournal = Guid.NewGuid(), etape = Guid.NewGuid();
        var premierGo = new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
        var dernierGo = premierGo.AddHours(2);
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            // Go à 20 h, retour en préparation, nouveau Go à 22 h : DemarrageAutoriseLe ne garde que le second.
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = relancee, ["Outil"] = "Relancée", ["Phase"] = (int)Phase.Execution, ["DemarrageAutoriseLe"] = dernierGo,
            });
            await MigrationJalon4Tests.Inserer(cn, "Journal", new() { ["Id"] = 1L, ["MepId"] = relancee, ["Quand"] = premierGo, ["Type"] = "gonogo.go", ["Detail"] = "Go" });
            await MigrationJalon4Tests.Inserer(cn, "Journal", new()
            {
                ["Id"] = 2L, ["MepId"] = relancee, ["Quand"] = premierGo.AddHours(1), ["Type"] = "phase.preparation", ["Detail"] = "Retour en préparation",
            });
            await MigrationJalon4Tests.Inserer(cn, "Journal", new() { ["Id"] = 3L, ["MepId"] = relancee, ["Quand"] = dernierGo, ["Type"] = "gonogo.go", ["Detail"] = "Go" });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = etape, ["MepId"] = relancee, ["Code"] = "E0001", ["Etat"] = (int)EtatEtape.Reussie });
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = jamais, ["Outil"] = "Jamais lancée", ["Phase"] = (int)Phase.Preparation });
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = repliee, ["Outil"] = "Repliée", ["Phase"] = (int)Phase.Cloturee, ["Issue"] = (int)IssueMep.Repliee, ["DemarrageAutoriseLe"] = premierGo,
                ["ClotureeLe"] = dernierGo,
            });
            // Go connu sans événement au journal (données anciennes) : le dernier Go tient lieu de premier.
            await MigrationJalon4Tests.Inserer(cn, "Meps", new()
            {
                ["Id"] = sansJournal, ["Outil"] = "Sans journal", ["Phase"] = (int)Phase.Recette, ["DemarrageAutoriseLe"] = dernierGo,
            });
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var lecture = new MepDbContext(options);
        var meps = await lecture.Meps.Include(m => m.Etapes).ToDictionaryAsync(m => m.Id);
        Assert.Equal(premierGo, meps[relancee].PremierGoLe);
        Assert.Equal(dernierGo, meps[relancee].DemarrageAutoriseLe);
        Assert.Null(meps[jamais].PremierGoLe);
        Assert.Equal(premierGo, meps[repliee].PremierGoLe);
        Assert.Equal(dernierGo, meps[sansJournal].PremierGoLe);

        Assert.Equal(Phase.Execution, meps[relancee].Phase);
        Assert.Equal(Phase.Preparation, meps[jamais].Phase);
        Assert.Equal((Phase.Cloturee, IssueMep.Repliee), (meps[repliee].Phase, meps[repliee].Issue));
        Assert.Equal(Phase.Recette, meps[sansJournal].Phase);
        Assert.All(meps.Values, m =>
        {
            Assert.Null(m.Annulation);
            Assert.Null(m.Report);
            Assert.Null(m.PilotageSecours);
            Assert.Equal("", m.EtatService);
            Assert.Equal("", m.ReferenceIncident);
        });
        Assert.False(meps[relancee].Etapes.Single().AjouteeApresGo);
        Assert.Equal(EtatMep.Repliee, Regles.EtatDeLaMep(meps[repliee]));
        Assert.Equal(EtatMep.Preparation, Regles.EtatDeLaMep(meps[jamais]));
    }
}
