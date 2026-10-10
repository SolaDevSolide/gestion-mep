using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du jalon 4.2 : plus de date ni d'heures saisies, plus de responsable par étape, durée facultative, résolutions multi-systèmes.</summary>
public class ReglesJalon42Tests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);

    [Fact]
    public void Ni_date_ni_heures_ni_responsable_ni_duree_ne_sont_exiges()
    {
        var m = Demo();
        Assert.Null(m.DateMep);
        m.HeureDebut = null;
        m.HeureFin = null;
        foreach (var e in m.Etapes)
        {
            Assert.Null(e.ResponsableId);
            e.DureeEstimeeMinutes = null;
        }
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "E0004"), Maintenant).Statut);
    }

    [Fact]
    public void La_demo_n_a_d_executant_que_pour_les_etapes_deja_demarrees()
    {
        var m = Demo();
        string Nom(Guid? id) => m.Participants.Single(p => p.Id == id).Nom;
        Assert.Equal(["Alice", "Bob", "Claire", "Bob"], new[] { "E0001", "E0002", "E0003", "E0005" }.Select(c => Nom(E(m, c).ExecutantId)).ToArray());
        Assert.All(m.Etapes.Where(e => e.Etat == EtatEtape.AFaire), e => Assert.Null(e.ExecutantId));
    }

    [Fact]
    public void Une_resolution_de_demo_couvre_deux_systemes()
    {
        var jeu = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var double_ = jeu.Resolutions.Single(r => r.SystemeCatalogueIds.Count == 2);
        Assert.Equal(["API", "BDD"], double_.SystemeCatalogueIds.Select(id => jeu.Systemes.Single(s => s.Id == id).Nom).Order().ToArray());
        Assert.Empty(Regles.ErreursResolutionType(double_));
        Assert.Contains(double_.Id, E(jeu.Mep, "E0007").ResolutionIds);
    }

    [Fact]
    public void Le_pilote_peut_terminer_et_derouler_une_resolution_a_la_place_de_l_executant()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = j.Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        var criteres = DonneesDemo.CriteresGoNoGo();
        Regles.PasserEnExecution(m, Alice, criteres.Select(c => c.Id).ToList(), criteres, 1, Maintenant);
        // E0005 a été exécutée par Bob ; Alice (pilote) lance et déroule la résolution.
        var exec = Regles.DemarrerResolution(m, E(m, "E0005"), j.Resolutions[0], Alice, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.ValiderCorrection(m, exec, 1, ResultatControle.Conforme, "", Claire, Maintenant));
        Regles.ValiderCorrection(m, exec, 1, ResultatControle.Conforme, "", Alice, Maintenant);
        Regles.ValiderVerification(m, exec, ResultatControle.Conforme, "OK", Bob, Maintenant); // exécutant et suppléant
        Assert.Equal(EtatEtape.AFaire, E(m, "E0005").Etat);
        // Reprise : n'importe quel participant relié peut la redémarrer et en devient l'exécutant.
        Regles.Demarrer(m, E(m, "E0005"), Claire, Maintenant);
        Assert.Equal(m.Participants.Single(p => p.UtilisateurId == Claire).Id, E(m, "E0005").ExecutantId);
    }
}

/// <summary>Tests d'intégration du jalon 4.2.</summary>
[Collection("Api")]
public class ApiJalon42Tests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    [Fact]
    public async Task Date_et_heures_ignorees_et_resume_avec_date_du_go_et_creation()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, resume.GetProperty("dateMep").ValueKind);
        var creeLe = resume.GetProperty("creeLe").GetDateTimeOffset();
        Assert.True(creeLe <= DateTimeOffset.UtcNow);

        // dateMep / heureDebut / heureFin envoyés : ignorés, sans modification ni journal.
        var corps = new
        {
            version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil").GetString(), versionActuelle = mep.GetProperty("versionActuelle").GetString(),
            versionCible = mep.GetProperty("versionCible").GetString(), objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(),
            dateMep = "2030-01-01", heureDebut = "20:00:00", heureFin = "23:00:00", fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(),
            indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"), piloteId = mep.GetProperty("piloteId").GetGuid(),
            suppleantIds = mep.GetProperty("suppleantIds"), echeanceRepli = mep.GetProperty("echeanceRepli"),
        };
        var apres = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", corps));
        Assert.Equal(mep.GetProperty("version").GetInt32(), apres.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, apres.GetProperty("dateMep").ValueKind);
        Assert.Equal(JsonValueKind.Null, apres.GetProperty("heureDebut").ValueKind);

        mep = await AidesApi.Go(_api, alice, id);
        resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single();
        var go = mep.GetProperty("demarrageAutorise").GetProperty("le").GetDateTimeOffset();
        var attendue = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(go, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).DateTime);
        Assert.Equal(attendue, DateOnly.Parse(resume.GetProperty("dateMep").GetString()!));
        Assert.Equal(creeLe, resume.GetProperty("creeLe").GetDateTimeOffset());
    }

    [Fact]
    public async Task Responsable_ignore_et_un_admin_non_participant_qui_demarre_devient_executant()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "root", nomAffiche = "Root", motDePasse = "motdepasse", estAdmin = true })).StatusCode);
        var root = await _api.ClientConnecte("root", "motdepasse");

        // responsableId envoyé : ignoré (ni contrôlé ni enregistré).
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { action = "Nouvelle", responsableId = Guid.NewGuid(), dureeEstimeeMinutes = (int?)null }));
        Assert.Equal(JsonValueKind.Null, Etape(mep, "E0010").GetProperty("responsableId").ValueKind);
        Assert.Equal(JsonValueKind.Null, Etape(mep, "E0010").GetProperty("dureeEstimeeMinutes").ValueKind);
        Assert.DoesNotContain(mep.GetProperty("erreursPlan").EnumerateArray(), e => e.GetString()!.Contains("Responsable"));
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/etapes/{Etape(mep, "E0010").GetProperty("id").GetGuid()}"));

        mep = await AidesApi.Go(_api, alice, id);
        var e4 = Etape(mep, "E0004");
        mep = await Ok(await root.PostAsJsonAsync($"/api/meps/{id}/etapes/{e4.GetProperty("id").GetGuid()}/demarrer", new { revision = e4.GetProperty("revision").GetInt32() }));
        var participant = mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == "Root");
        Assert.Equal("Exécutant", participant.GetProperty("role").GetString());
        Assert.Equal(participant.GetProperty("id").GetGuid(), Etape(mep, "E0004").GetProperty("executantId").GetGuid());
        var journal = await Ok(await alice.GetAsync($"/api/meps/{id}/journal"));
        Assert.Equal("E0004 démarrée par Root", journal[0].GetProperty("detail").GetString());
    }
}
