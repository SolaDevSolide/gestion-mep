using GestionMep.Api.Donnees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration BibliothequeEtapesModeles sur une base qui contient des résolutions liées à des étapes : dates de création et
/// compteurs d'utilisations initialisés, MEP notées comme déjà comptées (hors modèles), rien de perdu.
/// </summary>
[Collection("Api")]
public class MigrationBibliothequesTests
{
    private const string Base = "gestion_mep_tests_migration_bibliotheques";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task Les_resolutions_existantes_recoivent_date_de_creation_et_compteur()
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
            await db.GetService<IMigrator>().MigrateAsync("CommunicationEtModelesTexte");

        Guid mep1 = Guid.NewGuid(), mep2 = Guid.NewGuid(), modeleMep = Guid.NewGuid();
        Guid utilisee = Guid.NewGuid(), libre = Guid.NewGuid(), modeleRecette = Guid.NewGuid(), catalogue = Guid.NewGuid();
        var creation = new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var modification = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            await MigrationJalon4Tests.Inserer(cn, "SystemesCatalogue", new() { ["Id"] = catalogue, ["Nom"] = "BDD" });
            await MigrationJalon4Tests.Inserer(cn, "ResolutionsTypes", new() { ["Id"] = utilisee, ["Titre"] = "Utilisée", ["ModifieeLe"] = modification });
            await MigrationJalon4Tests.Inserer(cn, "ResolutionsTypes", new() { ["Id"] = libre, ["Titre"] = "Libre", ["ModifieeLe"] = modification });
            await MigrationJalon4Tests.Inserer(cn, "VersionsResolutionType", new() { ["Id"] = Guid.NewGuid(), ["ResolutionTypeId"] = utilisee, ["Version"] = 1, ["ModifieeLe"] = creation });
            await MigrationJalon4Tests.Inserer(cn, "VersionsResolutionType",
                new() { ["Id"] = Guid.NewGuid(), ["ResolutionTypeId"] = utilisee, ["Version"] = 2, ["ModifieeLe"] = creation.AddDays(10) });
            await MigrationJalon4Tests.Inserer(cn, "ModelesRecette",
                new() { ["Id"] = modeleRecette, ["Titre"] = "Connexion", ["SystemeCatalogueId"] = catalogue, ["ModifieLe"] = modification });
            foreach (var (id, estModele) in new[] { (mep1, false), (mep2, false), (modeleMep, true) })
            {
                await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = id, ["Outil"] = $"MEP {id}", ["EstModele"] = estModele });
                // Deux étapes liées à la même résolution : une seule utilisation pour la MEP.
                for (var i = 1; i <= 2; i++)
                    await MigrationJalon4Tests.Inserer(cn, "Etapes",
                        new() { ["Id"] = Guid.NewGuid(), ["MepId"] = id, ["Code"] = $"E000{i}", ["ResolutionIds"] = new[] { utilisee } });
            }
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var lecture = new MepDbContext(options);
        var resolutions = await lecture.ResolutionsTypes.AsNoTracking().ToDictionaryAsync(r => r.Titre);
        Assert.Equal((2, creation), (resolutions["Utilisée"].Utilisations, resolutions["Utilisée"].CreeeLe));
        Assert.Equal((0, modification), (resolutions["Libre"].Utilisations, resolutions["Libre"].CreeeLe));
        var recette = await lecture.ModelesRecette.AsNoTracking().SingleAsync();
        Assert.Equal((0, modification), (recette.Utilisations, recette.CreeLe));
        var meps = await lecture.Meps.AsNoTracking().ToDictionaryAsync(m => m.Id);
        Assert.Equal([utilisee], meps[mep1].ElementsBibliothequeComptes);
        Assert.Equal([utilisee], meps[mep2].ElementsBibliothequeComptes);
        Assert.Empty(meps[modeleMep].ElementsBibliothequeComptes);
        Assert.Equal(6, await lecture.Etapes.CountAsync());
        Assert.Empty(await lecture.ModelesEtape.ToListAsync());
    }
}
