using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>
/// Migration « EnvironnementsEtObjetsImpactes » sur une base qui a des MEP avec un environnement et des livrables : rien ne
/// se perd (environnement repris par système et par étape, livrables devenus objets impactés repris, variable
/// « livrables » des modèles de texte renommée).
/// </summary>
[Collection("Api")]
public class MigrationObjetsImpactesTests
{
    private const string Base = "gestion_mep_tests_migration_objets";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    [Fact]
    public async Task L_environnement_et_les_livrables_sont_repris_sans_perte()
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
            await db.GetService<IMigrator>().MigrateAsync("BibliothequeEtapesModeles");

        Guid equipe = Guid.NewGuid(), modeleTexte = Guid.NewGuid();
        Guid mep1 = Guid.NewGuid(), mep2 = Guid.NewGuid(), mep3 = Guid.NewGuid(), catalogue = Guid.NewGuid();
        Guid api1 = Guid.NewGuid(), libre1 = Guid.NewGuid(), api2 = Guid.NewGuid();
        Guid e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), e3 = Guid.NewGuid();
        Guid l1 = Guid.NewGuid(), l2 = Guid.NewGuid(), l3 = Guid.NewGuid(), l4 = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            await MigrationJalon4Tests.Inserer(cn, "SystemesCatalogue", new() { ["Id"] = catalogue, ["Nom"] = "API" });
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep1, ["Outil"] = "En recette", ["Environnement"] = " Recette " });
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep2, ["Outil"] = "En production", ["Environnement"] = "Production" });
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep3, ["Outil"] = "Sans environnement", ["Environnement"] = "" });
            await MigrationJalon4Tests.Inserer(cn, "Systemes", new() { ["Id"] = api1, ["MepId"] = mep1, ["Nom"] = "API", ["CatalogueId"] = catalogue });
            await MigrationJalon4Tests.Inserer(cn, "Systemes", new() { ["Id"] = libre1, ["MepId"] = mep1, ["Nom"] = "ERP" });
            await MigrationJalon4Tests.Inserer(cn, "Systemes", new() { ["Id"] = api2, ["MepId"] = mep2, ["Nom"] = "API", ["CatalogueId"] = catalogue });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e1, ["MepId"] = mep1, ["Code"] = "E0001", ["SystemeIds"] = new[] { api1, libre1 } });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e2, ["MepId"] = mep1, ["Code"] = "E0002", ["SystemeIds"] = Array.Empty<Guid>() });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new() { ["Id"] = e3, ["MepId"] = mep3, ["Code"] = "E0001", ["SystemeIds"] = Array.Empty<Guid>() });
            async Task Livrable(Guid id, Guid mep, Guid? systeme, string objet, string version, string emplacement) =>
                await MigrationJalon4Tests.Inserer(cn, "Livrables", new()
                {
                    ["Id"] = id, ["MepId"] = mep, ["SystemeId"] = systeme, ["Objet"] = objet, ["Version"] = version, ["Emplacement"] = emplacement,
                });
            await Livrable(l1, mep1, api1, "Service commandes", "2.0", "Nexus");
            await Livrable(l2, mep2, api2, "service COMMANDES", "2.1", "");
            await Livrable(l3, mep1, libre1, "Paquet ERP", "9", "partage");
            await Livrable(l4, mep1, null, "Notes de version", "", "");
            await MigrationJalon4Tests.Inserer(cn, "Equipes", new() { ["Id"] = equipe, ["Nom"] = "Exploitation" });
            await MigrationJalon4Tests.Inserer(cn, "ModelesTexte", new()
            {
                ["Id"] = modeleTexte, ["EquipeId"] = equipe, ["Nom"] = "Demande", ["Objet"] = "Livrables : {{livrables}}",
                ["Corps"] = "**Livrables**\n{{livrables}}\n{{systemes}}",
            });
        }

        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var lecture = new MepDbContext(options);
        var systemes = await lecture.Systemes.AsNoTracking().ToDictionaryAsync(s => s.Id);
        Assert.Equal([("Recette", false)], systemes[api1].Environnements.Select(e => (e.Nom, e.Production)).ToArray());
        Assert.Equal([("Recette", false)], systemes[libre1].Environnements.Select(e => (e.Nom, e.Production)).ToArray());
        Assert.Equal([("Production", true)], systemes[api2].Environnements.Select(e => (e.Nom, e.Production)).ToArray());

        var etapes = await lecture.Etapes.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.Equal([(api1, "Recette"), (libre1, "Recette")], etapes[e1].Environnements.Select(e => (e.SystemeId, e.Nom)).ToArray());
        Assert.Empty(etapes[e2].Environnements);
        Assert.Empty(etapes[e3].Environnements);

        // La fiche du catalogue reçoit les environnements rencontrés et un objet « Packages » par nom (sans tenir compte de la casse).
        var fiche = await lecture.SystemesCatalogue.AsNoTracking().SingleAsync(s => s.Id == catalogue);
        Assert.Equal([("Production", true), ("Recette", false)], fiche.Environnements.Select(e => (e.Nom, e.Production)).ToArray());
        Assert.Equal(["Packages"], fiche.Categories);
        var objet = Assert.Single(fiche.Objets);
        Assert.Equal(("Packages", "Package"), (objet.Categorie, objet.Type));

        var objets = await lecture.ObjetsImpactes.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Assert.Equal(4, objets.Count);
        Assert.All(objets.Values, o => Assert.True(o.Repris));
        Assert.Equal(("Service commandes", "2.0", "Nexus", (Guid?)objet.Id), (objets[l1].Nom, objets[l1].Version, objets[l1].Precision, objets[l1].ObjetCatalogueId));
        Assert.Equal(("service COMMANDES", "2.1", (Guid?)objet.Id), (objets[l2].Nom, objets[l2].Version, objets[l2].ObjetCatalogueId));
        Assert.Equal(("Paquet ERP", "9", "partage", (Guid?)null, (Guid?)libre1), (objets[l3].Nom, objets[l3].Version, objets[l3].Precision, objets[l3].ObjetCatalogueId, objets[l3].SystemeId));
        Assert.Equal(((Guid?)null, mep1), (objets[l4].SystemeId, objets[l4].MepId));

        // La colonne d'origine est gardée.
#pragma warning disable CS0618
        Assert.Equal(" Recette ", (await lecture.Meps.AsNoTracking().SingleAsync(m => m.Id == mep1)).Environnement);
#pragma warning restore CS0618
        Assert.Equal(ParametresGlobaux.CategoriesObjetsParDefaut, (await lecture.Parametres.AsNoTracking().SingleAsync()).CategoriesObjets);
        var texte = await lecture.ModelesTexte.AsNoTracking().SingleAsync(m => m.Id == modeleTexte);
        Assert.Equal(("Livrables : {{objets_impactes}}", "**Livrables**\n{{objets_impactes}}\n{{systemes}}"), (texte.Objet, texte.Corps));

        // Retour en arrière : les objets impactés redeviennent des livrables.
        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("BibliothequeEtapesModeles");
        await using var cn2 = new NpgsqlConnection(chaine);
        await cn2.OpenAsync();
        await using var cmd2 = new NpgsqlCommand("select count(*) from \"Livrables\"", cn2);
        Assert.Equal(4L, (long)(await cmd2.ExecuteScalarAsync())!);
        await using var cmd3 = new NpgsqlCommand("select \"Corps\" from \"ModelesTexte\"", cn2);
        Assert.Equal("**Livrables**\n{{livrables}}\n{{systemes}}", (string)(await cmd3.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task La_sauvegarde_automatique_compte_les_livrables_repris_sans_fausse_alerte()
    {
        const string baseControle = Base + "_controle";
        using (var cn = new NpgsqlConnection(Serveur + ";Database=postgres"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"drop database if exists {baseControle} with (force)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"create database {baseControle}";
            cmd.ExecuteNonQuery();
        }
        var chaine = $"{Serveur};Database={baseControle}";
        var options = new DbContextOptionsBuilder<MepDbContext>().UseNpgsql(chaine).Options;
        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("BibliothequeEtapesModeles");
        var mep = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep, ["Outil"] = "Avec livrables", ["Environnement"] = "Recette" });
            foreach (var objet in new[] { "Service commandes", "Notes de version" })
                await MigrationJalon4Tests.Inserer(cn, "Livrables", new() { ["Id"] = Guid.NewGuid(), ["MepId"] = mep, ["Objet"] = objet, ["Version"] = "1", ["Emplacement"] = "" });
        }

        var dossier = Path.Combine(Path.GetTempPath(), $"gestion-mep-controle-{Guid.NewGuid():N}");
        try
        {
            var alertes = new List<string>();
            var sauvegardes = new Sauvegardes(Path.Combine(dossier, "sauvegardes"), Path.Combine(dossier, "pieces"), _ => { }, alertes.Add);
            await using (var db = new MepDbContext(options))
                Assert.True(await sauvegardes.MigrerAvecSauvegarde(db, "test"));

            Assert.Empty(alertes);
            Assert.Empty(Directory.GetFiles(Path.Combine(dossier, "sauvegardes"), "avant-mise-a-jour-*"));
            await using var lecture = new MepDbContext(options);
            Assert.Equal(2, await lecture.ObjetsImpactes.CountAsync(o => o.MepId == mep));
            var controle = await lecture.JournalAdmin.AsNoTracking().SingleAsync(e => e.Type.StartsWith("donnees.controle"));
            Assert.Equal("donnees.controle-reussi", controle.Type);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
