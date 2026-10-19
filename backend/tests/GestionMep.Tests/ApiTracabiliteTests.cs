using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// API de l'approbation et de la traçabilité (#94) : séparation des tâches visible et bloquante, échéance modifiée par sa route
/// (motif, ancienne et nouvelle valeur dans le fuseau de la MEP), échéance seulement signalée en recette, incidents de recette.
/// </summary>
[Collection("Api")]
public class ApiTracabiliteTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();
    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static List<string> Erreurs(JsonElement mep) => mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToList();
    private static bool Droit(JsonElement mep, string droit) => mep.GetProperty("droits").GetProperty(droit).GetBoolean();

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static async Task<List<(string Type, string Auteur, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("auteur").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static Task<HttpResponseMessage> Echeance(HttpClient c, Guid id, JsonElement mep, DateTimeOffset? echeance, string motif) =>
        c.PutAsJsonAsync($"/api/meps/{id}/echeance", new { version = Version(mep), echeanceRepli = echeance, motif });

    private async Task EcheancePasseeEnBase(Guid id)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var m = await db.Meps.SingleAsync(x => x.Id == id);
        m.EcheanceRepli = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Un_approbateur_devenu_suppleant_reste_affiche_avec_la_raison_et_bloque_le_go()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("approbations")[0].GetProperty("empechement").ValueKind);

        // Claire approuve, puis devient suppléante : sa décision est gardée, mais elle ne compte plus.
        mep = await AidesApi.Approuver(_api, id);
        Assert.Empty(Erreurs(mep));
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", new
        {
            version = Version(mep), outil = mep.GetProperty("outil").GetString(), versionActuelle = mep.GetProperty("versionActuelle").GetString(),
            versionCible = mep.GetProperty("versionCible").GetString(), objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(),
            fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(), indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"),
            piloteId = mep.GetProperty("piloteId").GetGuid(), suppleantIds = new[] { Participant(mep, "Bob"), Participant(mep, "Claire") },
        }));
        var approbation = mep.GetProperty("approbations")[0];
        Assert.Equal("suppléant du pilote", approbation.GetProperty("empechement").GetString());
        const string anomalie = "Claire ne peut pas approuver cette MEP (suppléant du pilote). Le retirer des approbateurs.";
        Assert.Contains(anomalie, Erreurs(mep));

        // Le Go est refusé et dit pourquoi ; Claire ne peut plus se prononcer.
        var go = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(alice) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, go.StatusCode);
        Assert.Contains(anomalie, await Message(go));
        var decision = await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep), decision = "Approuvee", commentaire = "" });
        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        Assert.Equal("Vous ne pouvez pas approuver cette MEP (suppléant du pilote) : demander au pilote de désigner un autre approbateur.", await Message(decision));

        // Désigner un suppléant ou un responsable technique comme approbateur est refusé d'emblée.
        var bob = (await Ok(await alice.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == "bob").GetProperty("id").GetGuid();
        var refus = await alice.PutAsJsonAsync($"/api/meps/{id}/approbateurs", new { version = Version(mep), utilisateurIds = new[] { bob } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.StartsWith("Bob ne peut pas approuver cette MEP (suppléant du pilote).", await Message(refus));
    }

    [Fact]
    public async Task L_echeance_se_modifie_par_sa_route_avec_un_motif_trace_dans_le_fuseau_de_la_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Europe/Paris", mep.GetProperty("fuseauHoraire").GetString());
        Assert.True(Droit(mep, "peutModifierEcheance"));
        Assert.False(Droit(await Ok(await claire.GetAsync($"/api/meps/{id}")), "peutModifierEcheance"));
        var ancienne = mep.GetProperty("echeanceRepli").GetDateTimeOffset();

        // Heure d'hiver à Paris : 21 h 30 UTC = 22 h 30.
        var nouvelle = new DateTimeOffset(2027, 1, 15, 21, 30, 0, TimeSpan.Zero);
        var sansMotif = await Echeance(alice, id, mep, nouvelle, " ");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);
        Assert.Contains("motif", await Message(sansMotif));
        Assert.Equal(HttpStatusCode.Forbidden, (await Echeance(claire, id, mep, nouvelle, "Pas mon rôle")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Echeance(alice, id, mep, DateTimeOffset.UtcNow.AddMinutes(-1), "Passée")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}/echeance", new { version = 0, echeanceRepli = nouvelle, motif = "x" })).StatusCode);

        mep = await Ok(await Echeance(alice, id, mep, nouvelle, "Créneau décalé"));
        Assert.Equal(nouvelle, mep.GetProperty("echeanceRepli").GetDateTimeOffset());
        var (type, auteur, detail) = (await Journal(alice, id))[0];
        Assert.Equal(("echeance.modifiee", "Alice"), (type, auteur));
        Assert.Equal(Regles.DetailEcheance(ancienne, nouvelle, "Europe/Paris", "Créneau décalé"), detail);
        Assert.EndsWith(" → 15/01/2027 22:30 (Europe/Paris) : Créneau décalé", detail);

        // L'ancienne route de l'identité ne la change plus.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", new
        {
            version = Version(mep), outil = mep.GetProperty("outil").GetString(), versionActuelle = mep.GetProperty("versionActuelle").GetString(),
            versionCible = mep.GetProperty("versionCible").GetString(), objet = "Objet revu", impacts = mep.GetProperty("impacts").GetString(),
            fuseauHoraire = "Europe/Paris", piloteId = mep.GetProperty("piloteId").GetGuid(), echeanceRepli = (DateTimeOffset?)null,
        }));
        Assert.Equal(nouvelle, mep.GetProperty("echeanceRepli").GetDateTimeOffset());

        // Retour arrière déclenché, puis MEP clôturée : l'échéance ne se modifie plus.
        mep = await AidesApi.Go(_api, alice, id);
        Assert.True(Droit(mep, "peutModifierEcheance"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Exercice" }));
        Assert.False(Droit(mep, "peutModifierEcheance"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Echeance(alice, id, mep, nouvelle.AddHours(1), "Trop tard")).StatusCode);
        var close = await AidesApi.IdMep(alice, DonneesDemo.OutilReussie);
        var cloturee = await Ok(await alice.GetAsync($"/api/meps/{close}"));
        Assert.False(Droit(cloturee, "peutModifierEcheance"));
        var refus = await Echeance(alice, close, cloturee, DateTimeOffset.UtcNow.AddDays(1), "Après coup");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("terminée", await Message(refus));
    }

    [Fact]
    public async Task En_recette_l_echeance_atteinte_est_signalee_une_fois_sans_retour_arriere()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        await EcheancePasseeEnBase(id);

        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Recette", mep.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("retourArriere").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, mep.GetProperty("echeanceSignalee").ValueKind);
        await alice.GetAsync($"/api/meps/{id}");
        var signalements = (await Journal(alice, id)).Where(e => e.Type == "echeance.depassee").ToList();
        Assert.Equal([("echeance.depassee", "Système", Regles.DetailEcheanceSignalee)], signalements);

        // Revenir en exécution attend un report ; le report (motif) relance la surveillance.
        var retour = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", motif = "Corriger" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retour.StatusCode);
        mep = await Ok(await Echeance(alice, id, mep, DateTimeOffset.UtcNow.AddHours(2), "Recette prolongée"));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("echeanceSignalee").ValueKind);

        // Le pilote décide : retour arrière à la main.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Recette trop longue" }));
        Assert.Equal("RetourArriere", mep.GetProperty("phase").GetString());
        Assert.Equal("Alice", mep.GetProperty("retourArriere").GetProperty("par").GetString());
    }

    [Fact]
    public async Task Un_test_de_recette_en_echec_compte_comme_incident_jusque_dans_la_liste()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        // La démonstration a déjà un échec d'étape (E0002) et un test de recette en échec.
        Assert.Equal((1, 1), (mep.GetProperty("echecsSurvenus").GetInt32(), mep.GetProperty("echecsRecette").GetInt32()));

        var fiche = mep.GetProperty("fichesRecette").EnumerateArray().First(f => f.GetProperty("titre").GetString() == "Demande de congé");
        var test = fiche.GetProperty("etapes")[0];
        var url = $"/api/meps/{id}/fiches-recette/{fiche.GetProperty("id").GetGuid()}";
        await Ok(await alice.PostAsync($"{url}/etapes/{test.GetProperty("id").GetGuid()}/refaire", null));
        mep = await Ok(await alice.PostAsJsonAsync($"{url}/etapes/{test.GetProperty("id").GetGuid()}/resultat", new { resultat = "Echec", observation = "Solde faux" }));
        Assert.Equal(2, mep.GetProperty("echecsRecette").GetInt32());
        var resume = (await Ok(await alice.GetAsync("/api/meps"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        Assert.Equal(2, resume.GetProperty("echecsRecette").GetInt32());
    }
}
