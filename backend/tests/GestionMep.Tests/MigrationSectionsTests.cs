using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration « SectionsEtGrapheLibre » sur une base qui a des MEP avec des étapes liées : aucune donnée ne change (préalables,
/// lignes, ordres), les nouvelles colonnes sont vides, et l'exécution voit exactement le même graphe.
/// </summary>
[Collection("Api")]
public class MigrationSectionsTests
{
    private const string Base = "gestion_mep_tests_migration_sections";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task Les_etapes_existantes_gardent_leurs_liens_et_n_ont_aucune_section()
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
            await db.GetService<IMigrator>().MigrateAsync("MepSimplifiee");

        Guid mep = Guid.NewGuid(), e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), e3 = Guid.NewGuid(), r1 = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep, ["Outil"] = "Existante", ["Phase"] = (int)Phase.Execution });
            // Placement à la main d'avant le graphe libre : E0003 sur la ligne 2 alors que sa profondeur est 1.
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e1, ["MepId"] = mep, ["Code"] = "E0001", ["Ligne"] = 0, ["Ordre"] = 0 });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new()
            {
                ["Id"] = e2, ["MepId"] = mep, ["Code"] = "E0002", ["Ligne"] = 1, ["Ordre"] = 0, ["Dependances"] = new[] { e1 }, ["Etat"] = (int)EtatEtape.Reussie,
            });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new()
            {
                ["Id"] = e3, ["MepId"] = mep, ["Code"] = "E0003", ["Ligne"] = 2, ["Ordre"] = 0, ["Dependances"] = new[] { e1 },
            });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new()
            {
                ["Id"] = r1, ["MepId"] = mep, ["Code"] = "R0001", ["Plan"] = (int)TypePlan.RetourArriere, ["Ligne"] = 0, ["Ordre"] = 0,
            });
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var lecture = new MepDbContext(options);
        var m = await lecture.Meps.Include(x => x.Etapes).SingleAsync(x => x.Id == mep);
        Assert.Empty(m.Sections);
        var parId = m.Etapes.ToDictionary(e => e.Id);
        Assert.Equal([e1], parId[e2].Dependances);
        Assert.Equal([e1], parId[e3].Dependances);
        Assert.Empty(parId[e1].Dependances);
        Assert.Equal([(0, 0), (1, 0), (2, 0), (0, 0)], new[] { e1, e2, e3, r1 }.Select(id => (parId[id].Ligne, parId[id].Ordre)));
        Assert.All(m.Etapes, e =>
        {
            Assert.Null(e.SectionId);
            Assert.Empty(e.DependancesDerivees);
            Assert.Empty(e.PortsAttendus);
        });
        // L'exécution voit le même graphe : E0003 disponible dès que E0001 est faite (ici à faire), la compilation ne change rien.
        Regles.CompilerSections(m, TypePlan.Deploiement);
        Assert.Equal([e1], parId[e3].Dependances);
        Assert.Null(Regles.ErreurGraphe(m, TypePlan.Deploiement));

        // Une MEP enregistrée avec une section se relit à l'identique.
        m.Sections.Add(new Section { Titre = "Repère", Placee = false, Ports = [new PortSection { Sens = SensPort.Entree, Titre = "Début", Etapes = [e3] }] });
        parId[e3].SectionId = m.Sections[0].Id;
        await lecture.SaveChangesAsync();
        await using var relue = new MepDbContext(options);
        var r = await relue.Meps.Include(x => x.Etapes).SingleAsync(x => x.Id == mep);
        Assert.Equal("Repère", r.Sections.Single().Titre);
        Assert.Equal([e3], r.Sections.Single().Ports.Single().Etapes);
        Assert.Equal(r.Sections.Single().Id, r.Etapes.Single(e => e.Id == e3).SectionId);

        // Aucune table ni ligne perdue par la migration (contrôle d'après sauvegarde).
        Assert.Empty(Sauvegardes.Comparer(new Sauvegardes.Releve(new() { ["Meps"] = 1, ["Etapes"] = 4 }, []),
            new Sauvegardes.Releve(new() { ["Meps"] = await relue.Meps.CountAsync(), ["Etapes"] = await relue.Etapes.CountAsync() }, [])));
    }
}
