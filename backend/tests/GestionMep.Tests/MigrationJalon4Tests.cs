using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace GestionMep.Tests;

/// <summary>
/// Migration Jalon4 sur une base du jalon 3.1 contenant des données : systèmes des étapes, sauvegarde, tests de recette
/// devenus fiche, puis lignes du graphe calculées au démarrage (Amorcage).
/// </summary>
[Collection("Api")]
public class MigrationJalon4Tests
{
    private const string Base = "gestion_mep_tests_migration";

    private static string Serveur =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private record Json(string Texte);

    /// <summary>Insère une ligne : les colonnes obligatoires non fournies reçoivent une valeur neutre selon leur type.</summary>
    private static async Task Inserer(NpgsqlConnection cn, string table, Dictionary<string, object?> valeurs)
    {
        var obligatoires = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand(
            "select column_name, data_type from information_schema.columns where table_name = @t and is_nullable = 'NO' and column_default is null", cn))
        {
            cmd.Parameters.AddWithValue("t", table);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) obligatoires[r.GetString(0)] = r.GetString(1);
        }
        static string Neutre(string type) => type switch
        {
            "uuid" => "gen_random_uuid()",
            "integer" or "bigint" or "smallint" => "0",
            "boolean" => "false",
            "jsonb" => "'[]'::jsonb",
            "timestamp with time zone" => "now()",
            "ARRAY" => "'{}'",
            _ => "''",
        };
        var noms = valeurs.Keys.Union(obligatoires.Keys).ToList();
        var sql = $"insert into \"{table}\" ({string.Join(", ", noms.Select(n => $"\"{n}\""))}) values ("
            + string.Join(", ", noms.Select((n, i) => valeurs.ContainsKey(n) ? $"@p{i}" : Neutre(obligatoires[n]))) + ")";
        await using var ins = new NpgsqlCommand(sql, cn);
        for (var i = 0; i < noms.Count; i++)
        {
            if (!valeurs.TryGetValue(noms[i], out var v)) continue;
            ins.Parameters.Add(v is Json j
                ? new NpgsqlParameter($"p{i}", NpgsqlDbType.Jsonb) { Value = j.Texte }
                : new NpgsqlParameter($"p{i}", v ?? DBNull.Value));
        }
        await ins.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Une_base_du_jalon_3_1_est_migree_sans_perte()
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
            await db.GetService<IMigrator>().MigrateAsync("Jalon31");

        Guid mep = Guid.NewGuid(), systeme = Guid.NewGuid(), compte = Guid.NewGuid(), test1 = Guid.NewGuid(), test2 = Guid.NewGuid();
        Guid e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), e3 = Guid.NewGuid(), e4 = Guid.NewGuid(), r1 = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection(chaine))
        {
            await cn.OpenAsync();
            await Inserer(cn, "Utilisateurs", new() { ["Id"] = compte, ["Login"] = "ancien", ["NomAffiche"] = "Ancien" });
            await Inserer(cn, "Meps", new()
            {
                ["Id"] = mep, ["Outil"] = "MEP existante", ["Sauvegarde_Mode"] = (int)ModeSauvegarde.PendantMep,
                ["TestsRecette"] = new Json($$"""
                    [{"id":"{{test2}}","ordre":2,"libelle":"Appeler l'API","resultatAttendu":"200","responsableId":null,"resultat":0,"observation":"OK","parId":"{{compte}}","le":"2026-10-09T10:00:00+00:00"},
                     {"id":"{{test1}}","ordre":1,"libelle":"Ouvrir la page","resultatAttendu":"Page affichée","responsableId":null,"resultat":null,"observation":"","parId":null,"le":null}]
                    """),
            });
            await Inserer(cn, "Systemes", new() { ["Id"] = systeme, ["MepId"] = mep, ["Nom"] = "API" });
            async Task Etape(Guid id, string code, int ordre, int plan, Guid? systemeId, params Guid[] deps) =>
                await Inserer(cn, "Etapes", new()
                {
                    ["Id"] = id, ["MepId"] = mep, ["Code"] = code, ["Ordre"] = ordre, ["Plan"] = plan, ["SystemeId"] = systemeId, ["Dependances"] = deps,
                });
            await Etape(e1, "E0001", 1, 0, systeme);
            await Etape(e2, "E0002", 2, 0, null, e1);
            await Etape(e3, "E0003", 3, 0, systeme);
            await Etape(e4, "E0004", 4, 0, null, e2, e3);
            await Etape(r1, "R0001", 1, 1, systeme);
        }

        // Démarrage de l'API : migration Jalon4 puis calcul des lignes (Amorcage).
        Environment.SetEnvironmentVariable("ConnectionStrings__Mep", chaine);
        Environment.SetEnvironmentVariable("Surveillance__Activer", "false");
        Environment.SetEnvironmentVariable("Stockage__Dossier", Path.Combine(Path.GetTempPath(), "gestion-mep-migration-" + Guid.NewGuid().ToString("N")));
        using (var usine = new WebApplicationFactory<Program>()) _ = usine.Server;

        await using var lecture = new MepDbContext(options);
        var etapes = await lecture.Etapes.AsNoTracking().Where(e => e.MepId == mep).ToDictionaryAsync(e => e.Code);
        Assert.Equal((0, 0), (etapes["E0001"].Ligne, etapes["E0001"].Ordre));
        Assert.Equal((0, 1), (etapes["E0003"].Ligne, etapes["E0003"].Ordre));
        Assert.Equal((1, 0), (etapes["E0002"].Ligne, etapes["E0002"].Ordre));
        Assert.Equal((2, 0), (etapes["E0004"].Ligne, etapes["E0004"].Ordre));
        Assert.Equal((0, 0), (etapes["R0001"].Ligne, etapes["R0001"].Ordre));
        Assert.Equal([systeme], etapes["E0001"].SystemeIds);
        Assert.Empty(etapes["E0002"].SystemeIds);
        Assert.Equal([e2, e3], etapes["E0004"].Dependances);

        var s = await lecture.Systemes.AsNoTracking().SingleAsync(x => x.Id == systeme);
        Assert.Equal((StatutSauvegarde.ARenseigner, ModeSauvegarde.PendantMep), (s.Sauvegarde.Statut, s.Sauvegarde.Moment));

        var m = await lecture.Meps.AsNoTracking().SingleAsync(x => x.Id == mep);
        var fiche = Assert.Single(m.FichesRecette);
        Assert.Equal(("Recette", 1, (Guid?)null), (fiche.Titre, fiche.Ordre, fiche.SystemeId));
        Assert.Equal([test1, test2], fiche.Etapes.Select(t => t.Id).ToArray());
        Assert.Equal([1, 2], fiche.Etapes.Select(t => t.Ordre).ToArray());
        Assert.Equal(("Ouvrir la page", "Page affichée", (ResultatControle?)null), (fiche.Etapes[0].Instructions, fiche.Etapes[0].ResultatAttendu, fiche.Etapes[0].Resultat));
        var t2 = fiche.Etapes[1];
        Assert.Equal(("Appeler l'API", ResultatControle.Conforme, "OK", compte), (t2.Instructions, t2.Resultat, t2.Observation, t2.ParId!.Value));
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero), t2.Le);

        var u = await lecture.Utilisateurs.AsNoTracking().SingleAsync(x => x.Id == compte);
        Assert.Equal(("", ""), (u.Role, u.Contact));
        Assert.Empty(u.Expertises);
    }
}
