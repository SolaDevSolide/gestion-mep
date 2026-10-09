using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Tests d'intégration du jalon 3.1 : responsables techniques, droits d'édition (403), avertissements, motif de prise de main.</summary>
[Collection("Api")]
public class ApiJalon31Tests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();
    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static List<string> Liste(JsonElement mep, string champ) => mep.GetProperty(champ).EnumerateArray().Select(e => e.GetString()!).ToList();
    private static JsonElement Etape(JsonElement mep, string code) => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private async Task<(HttpClient Alice, Guid Id, JsonElement Mep)> Demo()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        return (alice, id, await Ok(await alice.GetAsync($"/api/meps/{id}")));
    }

    [Fact]
    public async Task La_demo_expose_ses_responsables_techniques_sans_avertissement()
    {
        var (_, _, mep) = await Demo();
        var rts = mep.GetProperty("responsablesTechniques").EnumerateArray().ToList();
        Assert.Equal(2, rts.Count);
        var bob = rts.Single(r => r.GetProperty("participantId").GetGuid() == Participant(mep, "Bob"));
        Assert.Equal("Joignable", bob.GetProperty("disponibilite").GetString());
        Assert.Equal(1, bob.GetProperty("systemeIds").GetArrayLength());
        Assert.Empty(Liste(mep, "avertissements"));
        Assert.False(mep.GetProperty("droits").GetProperty("estResponsableTechnique").GetBoolean()); // Alice, pilote
        Assert.True(mep.GetProperty("droits").GetProperty("peutEditerContenu").GetBoolean());
    }

    [Fact]
    public async Task Une_mep_creee_a_son_createur_responsable_technique_sans_pilote_et_avec_avertissement()
    {
        var bob = await _api.ClientConnecte("bob");
        var mep = await Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Neuf" }));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("piloteId").ValueKind);
        var rt = mep.GetProperty("responsablesTechniques").EnumerateArray().Single();
        Assert.Equal(Participant(mep, "Bob"), rt.GetProperty("participantId").GetGuid());
        Assert.Equal("Joignable", rt.GetProperty("disponibilite").GetString());
        Assert.False(Liste(mep, "avertissements").Contains("Aucun responsable technique renseigné."));
        Assert.Contains("Renseigner le pilote.", Liste(mep, "erreursPlan"));
        Assert.False(mep.GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.True(mep.GetProperty("droits").GetProperty("peutEditerContenu").GetBoolean());
    }

    [Fact]
    public async Task Modifier_les_responsables_techniques_met_a_jour_avertissements_et_journal()
    {
        var (alice, id, mep) = await Demo();
        var bob = Participant(mep, "Bob");
        var api = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("id").GetGuid();

        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new
        {
            version = Version(mep),
            responsables = new[] { new { participantId = bob, systemeIds = new[] { api }, perimetre = "API", disponibilite = "Absent", contactJourJ = "06 00 00 00 00" } },
        }));
        var avertissements = Liste(mep, "avertissements");
        Assert.Contains("E0002 : Bob sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.", avertissements);
        Assert.DoesNotContain(Liste(mep, "erreursPlan"), e => e.Contains("sera absent"));
        Assert.Equal("06 00 00 00 00", mep.GetProperty("responsablesTechniques")[0].GetProperty("contactJourJ").GetString());

        var journal = await Ok(await alice.GetAsync($"/api/meps/{id}/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "responsables-techniques.modifies"
            && e.GetProperty("detail").GetString() == "Responsables techniques : Bob");

        // Liste vide : avertissement dédié.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new { version = Version(mep), responsables = Array.Empty<object>() }));
        Assert.Equal(["Aucun responsable technique renseigné."], Liste(mep, "avertissements"));
    }

    [Fact]
    public async Task Responsables_techniques_refus_doublon_participant_inconnu_et_version_perimee()
    {
        var (alice, id, mep) = await Demo();
        var bob = Participant(mep, "Bob");
        var doublon = await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new
        {
            version = Version(mep), responsables = new[] { new { participantId = bob, disponibilite = "Joignable" }, new { participantId = bob, disponibilite = "Absent" } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, doublon.StatusCode);
        var inconnu = await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new
        {
            version = Version(mep), responsables = new[] { new { participantId = Guid.NewGuid(), disponibilite = "Joignable" } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inconnu.StatusCode);
        var perimee = await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new { version = Version(mep) - 1, responsables = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        var sansDispo = await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new
        {
            version = Version(mep), responsables = new[] { new { participantId = bob } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, sansDispo.StatusCode);
    }

    [Fact]
    public async Task Un_responsable_technique_ne_peut_pas_etre_supprime_des_participants()
    {
        var (alice, id, mep) = await Demo();
        var rep = await alice.DeleteAsync($"/api/meps/{id}/participants/{Participant(mep, "Claire")}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Contains("responsable technique", await Message(rep));
    }

    [Fact]
    public async Task Un_non_editeur_recoit_403_avec_un_message_clair_sur_toutes_les_modifications_de_contenu()
    {
        var (alice, id, mep) = await Demo();
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false })).StatusCode);
        var dave = await _api.ClientConnecte("dave", "motdepasse");
        const string attendu = "Seuls les responsables techniques, le pilote, ses suppléants ou un administrateur peuvent modifier cette MEP.";

        var requetes = new List<Task<HttpResponseMessage>>
        {
            dave.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }),
            dave.PostAsJsonAsync($"/api/meps/{id}/participants", new { nom = "X" }),
            dave.PostAsJsonAsync($"/api/meps/{id}/systemes", new { nom = "X" }),
            dave.PostAsJsonAsync($"/api/meps/{id}/livrables", new { objet = "X" }),
            dave.PostAsJsonAsync($"/api/meps/{id}/tests-recette", new { libelle = "X" }),
            dave.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { version = Version(mep) }),
            dave.PutAsJsonAsync($"/api/meps/{id}/sauvegarde", new { version = Version(mep) }),
            dave.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new { version = Version(mep), responsables = Array.Empty<object>() }),
            dave.PutAsJsonAsync($"/api/meps/{id}", new { version = Version(mep), outil = "Pirate" }),
        };
        foreach (var requete in requetes)
        {
            var rep = await requete;
            Assert.Equal(HttpStatusCode.Forbidden, rep.StatusCode);
            Assert.Equal(attendu, await Message(rep));
        }
        Assert.False((await Ok(await dave.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("peutEditerContenu").GetBoolean());

        // Exception : « procédure relue » reste ouverte aux participants (Dave n'en est pas un : 403 pour une autre raison), Claire approuve toujours.
        var claire = await _api.ClientConnecte("claire");
        var rep2 = await claire.PostAsJsonAsync($"/api/meps/{id}/procedure-relue", new { relue = true });
        Assert.True(rep2.IsSuccessStatusCode);
        await AidesApi.Approuver(_api, id);
    }

    [Fact]
    public async Task Claire_responsable_technique_peut_editer_et_perd_ce_droit_une_fois_retiree()
    {
        var (alice, id, mep) = await Demo();
        var claire = await _api.ClientConnecte("claire");
        Assert.True((await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }))).GetProperty("etapes").GetArrayLength() > 0);

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var bob = Participant(mep, "Bob");
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques",
            new { version = Version(mep), responsables = new[] { new { participantId = bob, disponibilite = "Joignable" } } }));
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/etapes", new { })).StatusCode);
    }

    [Fact]
    public async Task Prendre_la_main_exige_un_motif_conserve_dans_le_journal()
    {
        var (alice, id, mep) = await Demo();
        // Passage en exécution.
        mep = await AidesApi.Go(_api, alice, id);

        var sansMotif = await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{Etape(mep, "E0002").GetProperty("id").GetGuid()}/prendre-la-main",
            new { revision = Etape(mep, "E0002").GetProperty("revision").GetInt32() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);
        Assert.Equal("Indiquer pourquoi vous prenez la main.", await Message(sansMotif));

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{Etape(mep, "E0002").GetProperty("id").GetGuid()}/prendre-la-main",
            new { revision = Etape(mep, "E0002").GetProperty("revision").GetInt32(), motif = "  Bob est en congé  " }));
        var journal = await Ok(await alice.GetAsync($"/api/meps/{id}/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "etape.prise-en-main"
            && e.GetProperty("detail").GetString() == "Alice a pris la main sur E0002 (prévu : Bob) : Bob est en congé");
    }

    [Fact]
    public async Task Revenir_en_preparation_est_refuse_par_le_serveur_apres_le_point_de_non_retour()
    {
        var (alice, id, mep) = await Demo();
        mep = await AidesApi.Go(_api, alice, id);
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");

        async Task<JsonElement> Reussir(HttpClient c, string code)
        {
            var e = Etape(mep, code);
            var eid = e.GetProperty("id").GetGuid();
            mep = await Ok(await c.PostAsJsonAsync($"/api/meps/{id}/etapes/{eid}/demarrer", new { revision = e.GetProperty("revision").GetInt32() }));
            e = Etape(mep, code);
            return mep = await Ok(await c.PostAsJsonAsync($"/api/meps/{id}/etapes/{eid}/terminer",
                new { revision = e.GetProperty("revision").GetInt32(), resultat = "Reussie", preuve = "OK", resultatVerifie = true }));
        }
        await Reussir(bob, "E0002");
        await Reussir(claire, "E0003");
        var e4 = Etape(mep, "E0004");
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{e4.GetProperty("id").GetGuid()}/autoriser-non-retour", new { revision = e4.GetProperty("revision").GetInt32() }));
        await Reussir(claire, "E0004");

        var rep = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Preparation" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Contains("point de non-retour", await Message(rep));
    }

    [Fact]
    public async Task Duplication_copie_les_responsables_techniques_et_nomme_la_copie()
    {
        var (alice, id, _) = await Demo();
        var copie = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/dupliquer", new { environnement = "", commeModele = false }));
        Assert.Equal("Portail de démonstration (copie)", copie.GetProperty("outil").GetString());
        Assert.Equal("Production", copie.GetProperty("environnement").GetString());
        Assert.Equal(2, copie.GetProperty("responsablesTechniques").GetArrayLength());
        Assert.Equal(Participant(copie, "Bob"), copie.GetProperty("responsablesTechniques")[0].GetProperty("participantId").GetGuid());

        // Un non-participant qui duplique devient responsable technique de sa copie (il peut donc la modifier).
        var modele = (await Ok(await alice.GetAsync("/api/meps?modele=true"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        await alice.PostAsJsonAsync("/api/utilisateurs", new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false });
        var dave = await _api.ClientConnecte("dave", "motdepasse");
        var depuisModele = await Ok(await dave.PostAsJsonAsync($"/api/meps/{modele}/dupliquer", new { environnement = "Recette", commeModele = false }));
        Assert.Equal("déploiement API standard", depuisModele.GetProperty("outil").GetString());
        Assert.True(depuisModele.GetProperty("droits").GetProperty("estResponsableTechnique").GetBoolean());
        Assert.True(depuisModele.GetProperty("droits").GetProperty("peutEditerContenu").GetBoolean());
    }
}
