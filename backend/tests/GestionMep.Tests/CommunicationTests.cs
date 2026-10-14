using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>Section Communication d'une MEP, modèles de texte des équipes et valeurs des variables (API de bout en bout).</summary>
[Collection("Api")]
public class CommunicationTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<int> Version(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}"))).GetProperty("version").GetInt32();

    private static object Communication(int version, string reference = "CHG-1", object? preuve = null, bool validee = false) => new
    {
        version, referenceDemande = reference, niveauRisque = "Eleve", benefices = "Plus rapide\r\n\r\n- Plus simple  ", utilisateursImpactes = "Agences",
        arretApplicatif = true, actionUtilisateurs = "quitter l'application", heureLimite = "19:30:00", dateAnnoncee = "2026-10-20",
        heureDebutAnnoncee = "20:00:00", heureFinAnnoncee = "23:00:00", valideeParUtilisateurs = validee, valideePar = "Claire",
        valideeLe = "2026-10-15", preuveValidationId = preuve,
    };

    private static Dictionary<string, JsonElement> Valeurs(JsonElement variables) =>
        variables.GetProperty("valeurs").EnumerateArray().ToDictionary(v => v.GetProperty("cle").GetString()!);

    private static string? Texte(Dictionary<string, JsonElement> v, string cle) =>
        v[cle].GetProperty("texte").ValueKind == JsonValueKind.String ? v[cle].GetProperty("texte").GetString() : null;

    private static List<string> Liste(Dictionary<string, JsonElement> v, string cle) =>
        v[cle].GetProperty("liste").ValueKind == JsonValueKind.Array ? v[cle].GetProperty("liste").EnumerateArray().Select(x => x.GetString()!).ToList() : [];

    [Fact]
    public async Task La_communication_se_modifie_en_preparation_puis_par_le_pilote_jusqu_a_la_cloture()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilIncomplete);
        var avant = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Modere", avant.GetProperty("communication").GetProperty("niveauRisque").GetString());
        var approuvees = avant.GetProperty("approbations").EnumerateArray().Count(a => a.GetProperty("decision").ValueKind == JsonValueKind.String);

        var mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/communication", Communication(avant.GetProperty("version").GetInt32())));
        var c = mep.GetProperty("communication");
        Assert.Equal("CHG-1", c.GetProperty("referenceDemande").GetString());
        Assert.Equal("Eleve", c.GetProperty("niveauRisque").GetString());
        Assert.Equal("Plus rapide\n\n- Plus simple", c.GetProperty("benefices").GetString());
        Assert.Equal("19:30:00", c.GetProperty("heureLimite").GetString());
        // Sans validation, ni auteur ni date ne sont gardés.
        Assert.Equal("", c.GetProperty("valideePar").GetString());
        Assert.Equal(JsonValueKind.Null, c.GetProperty("valideeLe").ValueKind);
        // Les approbations restent valables : la communication ne change pas le plan.
        Assert.Equal(approuvees, mep.GetProperty("approbations").EnumerateArray().Count(a => a.GetProperty("decision").ValueKind == JsonValueKind.String));
        var journal = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().First();
        Assert.Equal("communication.modifiee", journal.GetProperty("type").GetString());
        Assert.Contains("référence de la demande", journal.GetProperty("detail").GetString());

        // Version périmée : conflit ; preuve qui n'est pas une pièce jointe de la MEP : refusée.
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}/communication", Communication(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/communication",
            Communication(mep.GetProperty("version").GetInt32(), preuve: Guid.NewGuid(), validee: true))).StatusCode);

        // Après le Go : le pilote ou son suppléant (Bob) seulement ; MEP clôturée : plus personne.
        var execution = await AidesApi.IdMep(alice, DonneesDemo.OutilExecution);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await claire.PutAsJsonAsync($"/api/meps/{execution}/communication", Communication(await Version(claire, execution)))).StatusCode);
        var parBob = await Ok(await bob.PutAsJsonAsync($"/api/meps/{execution}/communication", Communication(await Version(bob, execution), "CHG-2")));
        Assert.Equal("CHG-2", parBob.GetProperty("communication").GetProperty("referenceDemande").GetString());
        var cloturee = await AidesApi.IdMep(alice, DonneesDemo.OutilReussie);
        Assert.Equal((HttpStatusCode)422,
            (await alice.PutAsJsonAsync($"/api/meps/{cloturee}/communication", Communication(await Version(alice, cloturee)))).StatusCode);
    }

    [Fact]
    public async Task Les_variables_d_une_mep_reprennent_la_communication_et_l_equipe_du_pilote()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var equipes = (await Ok(await alice.GetAsync("/api/equipes"))).EnumerateArray().ToList();
        var demo = equipes.Single(e => e.GetProperty("nom").GetString() == DonneesDemo.NomEquipe).GetProperty("id").GetGuid();

        var variables = await Ok(await alice.GetAsync($"/api/meps/{id}/variables-texte"));
        // Alice, pilote de la MEP de démonstration, est dans l'équipe de démonstration : c'est l'équipe proposée.
        Assert.Equal(demo, variables.GetProperty("equipeProposeeId").GetGuid());
        Assert.Equal(demo, variables.GetProperty("equipeId").GetGuid());
        var v = Valeurs(variables);
        Assert.Equal(VariablesTexte.Toutes.Select(x => x.Cle), v.Keys);
        Assert.Equal(DonneesDemo.OutilPortail, Texte(v, "application"));
        Assert.Equal("CHG-DEMO-0412", Texte(v, "reference_demande"));
        Assert.Equal("modéré", Texte(v, "niveau_risque"));
        Assert.Equal("19h30", Texte(v, "heure_limite"));
        Assert.Equal(3, Liste(v, "benefices").Count);
        Assert.Equal(5, Liste(v, "systemes").Count);
        Assert.Equal("Alice", Texte(v, "pilote"));
        Assert.Equal(DonneesDemo.NomEquipe, Texte(v, "equipe"));
        Assert.Equal("L'équipe Exploitation applicative", Texte(v, "signature"));
        Assert.EndsWith($"/meps/{id}", Texte(v, "lien_mep"));
        // Information absente de la MEP : ni texte ni liste.
        Assert.Null(Texte(v, "indisponibilite"));
        Assert.Empty(Liste(v, "indisponibilite"));
        Assert.Null(Texte(v, "issue"));

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{id}/variables-texte?equipe={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(VariablesTexte.Toutes.Count, (await Ok(await alice.GetAsync("/api/modeles-texte/variables"))).GetArrayLength());
    }

    [Fact]
    public async Task Chaque_equipe_recoit_les_modeles_fournis_qui_se_modifient_se_retablissent_et_ne_se_suppriment_pas()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");

        // Nouvelle équipe : les quatre modèles fournis, dans leur ordre.
        var equipe = (await Ok(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "Infrastructure" }))).GetProperty("id").GetGuid();
        var modeles = (await Ok(await claire.GetAsync($"/api/modeles-texte?equipe={equipe}"))).EnumerateArray().ToList();
        Assert.Equal(ModelesTexteFournis.Tous.Select(f => f.Nom), modeles.Select(m => m.GetProperty("nom").GetString()));
        Assert.All(modeles, m => Assert.True(m.GetProperty("fourni").GetBoolean()));
        Assert.All(modeles, m => Assert.False(m.GetProperty("modifie").GetBoolean()));
        // Claire n'est pas membre : elle consulte, ne modifie pas.
        Assert.All(modeles, m => Assert.False(m.GetProperty("peutModifier").GetBoolean()));
        var annonce = modeles[0];
        var idAnnonce = annonce.GetProperty("id").GetGuid();
        var saisie = new { version = annonce.GetProperty("version").GetInt32(), nom = "Annonce maison", usage = "Annonce", objet = "Objet\r\n{{application}}", corps = "Bonjour {{pilote}}" };
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PutAsJsonAsync($"/api/modeles-texte/{idAnnonce}", saisie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/modeles-texte/{idAnnonce}", saisie)).StatusCode);

        // Un administrateur (ou un membre) modifie ; le modèle se rétablit et ne se supprime pas.
        var modifie = await Ok(await alice.PutAsJsonAsync($"/api/modeles-texte/{idAnnonce}", saisie));
        Assert.True(modifie.GetProperty("modifie").GetBoolean());
        Assert.Equal("Objet {{application}}", modifie.GetProperty("objet").GetString());
        Assert.Equal("Alice", modifie.GetProperty("modifiePar").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/modeles-texte/{idAnnonce}", saisie)).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await alice.DeleteAsync($"/api/modeles-texte/{idAnnonce}")).StatusCode);
        var retabli = await Ok(await alice.PostAsync($"/api/modeles-texte/{idAnnonce}/retablir", null));
        Assert.False(retabli.GetProperty("modifie").GetBoolean());
        Assert.Equal(ModelesTexteFournis.Tous[0].Corps, retabli.GetProperty("corps").GetString());

        // Modèle de l'équipe : créé par un membre, supprimable ; l'équipe ne change pas.
        var idBob = (await Ok(await alice.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == "bob").GetProperty("id").GetGuid();
        await Ok(await alice.PutAsJsonAsync($"/api/admin/equipes/{equipe}", new { nom = "Infrastructure", description = "", membreIds = new[] { idBob } }));
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync("/api/modeles-texte", new { equipeId = equipe, nom = " ", corps = "" })).StatusCode);
        var astreinte = await Ok(await bob.PostAsJsonAsync("/api/modeles-texte", new { equipeId = equipe, nom = "Prévenir l'astreinte", usage = "Autre", objet = "MEP {{application}}", corps = "{{systemes}}" }));
        Assert.False(astreinte.GetProperty("fourni").GetBoolean());
        Assert.True(astreinte.GetProperty("peutModifier").GetBoolean());
        var idAstreinte = astreinte.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync($"/api/modeles-texte/{idAstreinte}",
            new { version = 1, equipeId = Guid.NewGuid(), nom = "X", usage = "Autre", objet = "", corps = "" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await bob.PostAsync($"/api/modeles-texte/{idAstreinte}/retablir", null)).StatusCode);
        Assert.Equal(5, (await Ok(await bob.GetAsync($"/api/modeles-texte?equipe={equipe}"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.DeleteAsync($"/api/modeles-texte/{idAstreinte}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"/api/modeles-texte/{idAstreinte}")).StatusCode);

        // Informations communes de l'équipe : membres et administrateurs.
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PutAsJsonAsync($"/api/equipes/{equipe}/communication", new { contactSupport = "x", signature = "" })).StatusCode);
        var infos = await Ok(await bob.PutAsJsonAsync($"/api/equipes/{equipe}/communication", new { contactSupport = " astreinte@exemple.invalid ", signature = "L'infra" }));
        Assert.Equal("astreinte@exemple.invalid", infos.GetProperty("contactSupport").GetString());
        var v = Valeurs(await Ok(await bob.GetAsync($"/api/meps/{await AidesApi.IdDemo(bob)}/variables-texte?equipe={equipe}")));
        Assert.Equal("astreinte@exemple.invalid", Texte(v, "contact_support"));
        Assert.Equal("L'infra", Texte(v, "signature"));

        var types = (await Ok(await alice.GetAsync("/api/admin/journal"))).EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("modele-texte.modifie", types);
        Assert.Contains("modele-texte.retabli", types);
        Assert.Contains("modele-texte.cree", types);
        Assert.Contains("modele-texte.supprime", types);

        // La suppression de l'équipe emporte ses modèles.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/admin/equipes/{equipe}")).StatusCode);
        Assert.Equal(0, (await Ok(await alice.GetAsync($"/api/modeles-texte?equipe={equipe}"))).GetArrayLength());
    }

    [Fact]
    public async Task Les_equipes_existantes_recoivent_les_modeles_fournis_manquants()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        // Équipe d'avant les modèles de texte (aucun modèle), et équipe à qui il en manque un.
        var ancienne = new Equipe { Nom = "Ancienne", CreeeLe = DateTimeOffset.UtcNow };
        db.Equipes.Add(ancienne);
        await db.SaveChangesAsync();
        var demo = await db.Equipes.SingleAsync(e => e.Nom == DonneesDemo.NomEquipe);
        await db.ModelesTexte.Where(m => m.EquipeId == demo.Id && m.CleFournie == "rappel").ExecuteDeleteAsync();

        Assert.Equal(5, await CommunicationEndpoints.CompleterModelesFournis(db, DateTimeOffset.UtcNow));
        Assert.Equal(0, await CommunicationEndpoints.CompleterModelesFournis(db, DateTimeOffset.UtcNow));
        Assert.Equal(4, await db.ModelesTexte.CountAsync(m => m.EquipeId == ancienne.Id));
        Assert.Equal(5, await db.ModelesTexte.CountAsync(m => m.EquipeId == demo.Id)); // quatre fournis et « Prévenir l'astreinte »
    }
}
