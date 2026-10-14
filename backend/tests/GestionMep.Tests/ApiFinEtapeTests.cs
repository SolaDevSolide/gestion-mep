using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionMep.Tests;

/// <summary>API de la fin d'étape typée (test unitaire ou simple vérification) et des sauvegardes complètes ; migration de l'existant.</summary>
[Collection("Api")]
public class ApiFinEtapeTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();
    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();
    private static int Revision(JsonElement mep, string code) => Etape(mep, code).GetProperty("revision").GetInt32();
    private static Guid Systeme(JsonElement mep, string nom) =>
        mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static List<string> Erreurs(JsonElement mep) => mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToList();

    /// <summary>Saisie complète d'une étape d'après son état, sans les champs de fin ni de sauvegarde (absents : inchangés).</summary>
    private static Dictionary<string, object?> Saisie(JsonElement mep, string code)
    {
        var e = Etape(mep, code);
        return new()
        {
            ["revision"] = e.GetProperty("revision").GetInt32(), ["action"] = e.GetProperty("action").GetString(), ["systemeIds"] = e.GetProperty("systemeIds"),
            ["criticite"] = e.GetProperty("criticite").GetString(), ["instructions"] = e.GetProperty("instructions").GetString(),
            ["resultatAttendu"] = e.GetProperty("resultatAttendu").GetString(), ["dependances"] = e.GetProperty("dependances"),
            ["resolutionIds"] = e.GetProperty("resolutionIds"), ["type"] = e.GetProperty("type").GetString(),
            ["pointDeNonRetour"] = e.GetProperty("pointDeNonRetour").GetBoolean(),
        };
    }

    [Fact]
    public async Task Fin_d_etape_et_sauvegarde_d_une_etape_se_saisissent_en_preparation()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("TestUnitaire", Etape(mep, "E0004").GetProperty("fin").GetString());
        Assert.Equal("Verification", Etape(mep, "E0003").GetProperty("fin").GetString());
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0001")}";

        var saisie = Saisie(mep, "E0001");
        saisie["fin"] = "Verification";
        saisie["sauvegardeEmplacement"] = " ";
        saisie["sauvegardeMethodeRecuperation"] = "";
        mep = await Ok(await alice.PutAsJsonAsync(url, saisie));
        Assert.Equal("Verification", Etape(mep, "E0001").GetProperty("fin").GetString());
        Assert.Contains("E0001 : compléter Emplacement de la sauvegarde, Méthode de récupération.", Erreurs(mep));

        saisie = Saisie(mep, "E0001");
        saisie["sauvegardeEmplacement"] = "/sauvegardes/clients.sql";
        saisie["sauvegardeMethodeRecuperation"] = "psql -d clients < clients.sql";
        mep = await Ok(await alice.PutAsJsonAsync(url, saisie));
        Assert.DoesNotContain(Erreurs(mep), e => e.StartsWith("E0001"));
        // Champs absents : inchangés.
        mep = await Ok(await alice.PutAsJsonAsync(url, Saisie(mep, "E0001")));
        Assert.Equal(("Verification", "/sauvegardes/clients.sql"),
            (Etape(mep, "E0001").GetProperty("fin").GetString(), Etape(mep, "E0001").GetProperty("sauvegardeEmplacement").GetString()));

        // Une nouvelle étape est un test unitaire.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { action = "Contrôle final" }));
        Assert.Equal("TestUnitaire", Etape(mep, "E0010").GetProperty("fin").GetString());
    }

    [Fact]
    public async Task Une_sauvegarde_sans_emplacement_ni_methode_de_recuperation_bloque_le_go()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var url = $"/api/meps/{id}/systemes/{Systeme(mep, "BDD")}/sauvegarde";
        mep = await Ok(await alice.PutAsJsonAsync(url, new
        {
            version = Version(mep), statut = "Requise", moment = "PendantMep", contenu = "pg_dump", justification = "", emplacement = "", methodeRecuperation = " ",
        }));
        Assert.Contains("Sauvegarde de BDD : compléter Emplacement, Méthode de récupération.", Erreurs(mep));

        // Le plan incomplet empêche aussi l'approbation : le Go est refusé pour les deux raisons.
        var go = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(alice) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, go.StatusCode);
        Assert.Contains("Sauvegarde de BDD : compléter Emplacement, Méthode de récupération.", await Message(go));

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        mep = await Ok(await alice.PutAsJsonAsync(url, new
        {
            version = Version(mep), statut = "Requise", moment = "PendantMep", contenu = "pg_dump", justification = "",
            emplacement = "/sauvegardes/clients.sql", methodeRecuperation = "psql -d clients < clients.sql",
        }));
        var bdd = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "BDD").GetProperty("sauvegarde");
        Assert.Equal("/sauvegardes/clients.sql", bdd.GetProperty("emplacement").GetString());
        Assert.DoesNotContain(Erreurs(mep), e => e.StartsWith("Sauvegarde"));
        await AidesApi.Go(_api, alice, id);
    }

    [Fact]
    public async Task Les_tests_unitaires_d_une_mep_se_listent_avec_leur_resultat_constate()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var suivi = await Ok(await alice.GetAsync($"/api/meps/{id}/tests-unitaires"));
        Assert.Equal((3, 5, 1), (suivi.GetProperty("faits").GetInt32(), suivi.GetProperty("nonFaits").GetInt32(), suivi.GetProperty("verifications").GetInt32()));

        var mep = await AidesApi.Go(_api, alice, id);
        var e4 = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}";
        mep = await Ok(await alice.PostAsJsonAsync($"{e4}/demarrer", new { revision = Revision(mep, "E0004") }));
        var refus = await alice.PostAsJsonAsync($"{e4}/terminer", new { revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "", resultatVerifie = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Equal("Test unitaire : saisir le résultat constaté.", await Message(refus));
        mep = await Ok(await alice.PostAsJsonAsync($"{e4}/terminer", new
        {
            revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "Sortie jointe", resultatVerifie = true, resultatConstate = "12 migrations appliquées",
        }));
        Assert.Equal("12 migrations appliquées", Etape(mep, "E0004").GetProperty("resultatConstate").GetString());

        suivi = await Ok(await alice.GetAsync($"/api/meps/{id}/tests-unitaires"));
        Assert.Equal((4, 4), (suivi.GetProperty("faits").GetInt32(), suivi.GetProperty("nonFaits").GetInt32()));
        var t = suivi.GetProperty("tests").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "E0004");
        Assert.True(t.GetProperty("fait").GetBoolean());
        Assert.Equal(("12 migrations appliquées", "Alice"), (t.GetProperty("resultatConstate").GetString(), t.GetProperty("executant").GetString()));
        Assert.DoesNotContain(suivi.GetProperty("tests").EnumerateArray(), x => x.GetProperty("code").GetString() == "E0003");

        var journal = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().Select(e => e.GetProperty("detail").GetString()).ToList();
        Assert.Contains("E0004 réussie : Sortie jointe (test unitaire, constaté : 12 migrations appliquées)", journal);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{Guid.NewGuid()}/tests-unitaires")).StatusCode);
    }

}

/// <summary>Migration FinEtapeTypeeEtSauvegardes sur une base qui contient déjà une MEP.</summary>
[Collection("Api")]
public class MigrationFinEtapeTests
{
    [Fact]
    public async Task Les_etapes_existantes_deviennent_des_tests_unitaires_sans_resultat_constate()
    {
        const string nomBase = "gestion_mep_tests_migration_fin";
        var serveur = Environment.GetEnvironmentVariable("TEST_PG_SERVEUR") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        await using (var cn = new NpgsqlConnection(serveur + ";Database=postgres"))
        {
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"drop database if exists {nomBase} with (force)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = $"create database {nomBase}";
            await cmd.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<MepDbContext>().UseNpgsql($"{serveur};Database={nomBase}").Options;
        await using (var db = new MepDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("Reglages");

        Guid mep = Guid.NewGuid(), systeme = Guid.NewGuid(), etape = Guid.NewGuid();
        await using (var cn = new NpgsqlConnection($"{serveur};Database={nomBase}"))
        {
            await cn.OpenAsync();
            await MigrationJalon4Tests.Inserer(cn, "Meps", new() { ["Id"] = mep, ["Outil"] = "MEP clôturée", ["Phase"] = (int)Phase.Cloturee });
            await MigrationJalon4Tests.Inserer(cn, "Systemes", new()
            {
                ["Id"] = systeme, ["MepId"] = mep, ["Nom"] = "BDD", ["Sauvegarde_Statut"] = (int)StatutSauvegarde.Requise,
            });
            await MigrationJalon4Tests.Inserer(cn, "Etapes", new()
            {
                ["Id"] = etape, ["MepId"] = mep, ["Code"] = "E0001", ["Etat"] = (int)EtatEtape.Reussie, ["Preuve"] = "OK", ["Type"] = (int)TypeEtape.Sauvegarde,
            });
        }

        await using (var db = new MepDbContext(options))
            await db.Database.MigrateAsync();
        await using var lecture = new MepDbContext(options);
        var e = await lecture.Etapes.AsNoTracking().SingleAsync(x => x.Id == etape);
        Assert.Equal((FinEtape.TestUnitaire, "", "", ""), (e.Fin, e.ResultatConstate, e.SauvegardeEmplacement, e.SauvegardeMethodeRecuperation));
        var s = await lecture.Systemes.AsNoTracking().SingleAsync(x => x.Id == systeme);
        Assert.Equal(("", ""), (s.Sauvegarde.Emplacement, s.Sauvegarde.MethodeRecuperation));
    }
}
