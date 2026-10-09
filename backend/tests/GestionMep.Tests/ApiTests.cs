using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GestionMep.Tests;

[Collection("Api")]
public class ApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    // ---------- Aides ----------
    private static async Task<JsonElement> Lire(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> Ok(HttpResponseMessage rep)
    {
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.True(rep.IsSuccessStatusCode, $"{(int)rep.StatusCode} : {texte}");
        return JsonDocument.Parse(texte).RootElement.Clone();
    }

    private static async Task<string> Message(HttpResponseMessage rep) => (await Lire(rep)).GetProperty("message").GetString()!;

    private static async Task<Guid> IdDemo(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/meps"))).EnumerateArray().Single().GetProperty("id").GetGuid();

    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private static string Dispo(JsonElement mep, string code) => Etape(mep, code).GetProperty("disponibilite").GetProperty("libelle").GetString()!;
    private static int Revision(JsonElement mep, string code) => Etape(mep, code).GetProperty("revision").GetInt32();
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> Action(HttpClient c, Guid mepId, JsonElement mep, string code, string action, object? corps = null) =>
        c.PostAsJsonAsync($"/api/meps/{mepId}/etapes/{EtapeId(mep, code)}/{action}", corps ?? new { revision = Revision(mep, code) });

    // ---------- Authentification ----------
    [Fact]
    public async Task Connexion_ok_ko_et_401_sans_cookie()
    {
        var anonyme = _api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.GetAsync("/api/meps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonyme.GetAsync("/api/sante")).StatusCode);

        var ko = await anonyme.PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = "faux" });
        Assert.Equal(HttpStatusCode.Unauthorized, ko.StatusCode);
        Assert.Contains("incorrect", await Message(ko));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonyme.PostAsJsonAsync("/api/auth/connexion", new { login = "inconnu", motDePasse = "demo" })).StatusCode);

        var ok = await anonyme.PostAsJsonAsync("/api/auth/connexion", new { login = "Alice", motDePasse = "demo" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("gestion-mep.auth", ok.Headers.GetValues("Set-Cookie").Single());
        var moi = await Ok(await anonyme.GetAsync("/api/auth/moi"));
        Assert.Equal("alice", moi.GetProperty("login").GetString());
        Assert.True(moi.GetProperty("estAdmin").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await anonyme.PostAsync("/api/auth/deconnexion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Creation_d_utilisateur_reservee_aux_admins()
    {
        var bob = await _api.ClientConnecte("bob");
        var corps = new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/utilisateurs", corps)).StatusCode);

        var alice = await _api.ClientConnecte("alice");
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/utilisateurs", corps)).StatusCode);
        Assert.Equal(4, (await Ok(await alice.GetAsync("/api/utilisateurs"))).GetArrayLength());
        await _api.ClientConnecte("dave", "motdepasse");
    }

    // ---------- Lecture ----------
    [Fact]
    public async Task Demo_chargee_avec_disponibilites()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));

        Assert.Equal("Preparation", mep.GetProperty("phase").GetString());
        // 5 étapes de déploiement + 2 de retour arrière.
        Assert.Equal(7, mep.GetProperty("etapes").GetArrayLength());
        Assert.Equal("RetourArriere", Etape(mep, "R0001").GetProperty("plan").GetString());
        Assert.Equal("Si retour arrière", Dispo(mep, "R0001"));
        Assert.Equal(1, Etape(mep, "E0005").GetProperty("resolutionIds").GetArrayLength());
        Assert.Equal(30, mep.GetProperty("planRepli").GetProperty("dureeTesteeMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("issue").ValueKind);
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("retourArriere").ValueKind);
        Assert.Equal(3, mep.GetProperty("systemes").EnumerateArray().Count(s => s.GetProperty("catalogueId").ValueKind == JsonValueKind.String));
        Assert.Equal(0, mep.GetProperty("resolutions").GetArrayLength());
        Assert.Equal("Disponible", Etape(mep, "E0002").GetProperty("disponibilite").GetProperty("statut").GetString());
        Assert.Equal("Attend E0002", Dispo(mep, "E0004"));
        Assert.Equal("Terminée", Dispo(mep, "E0001"));
        Assert.Equal(0, mep.GetProperty("erreursPlan").GetArrayLength());
        Assert.True(mep.GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.True(mep.GetProperty("droits").GetProperty("peutModifierPlan").GetBoolean());
        Assert.Equal("Bob", mep.GetProperty("procedureRelue").GetProperty("par").GetString());

        var resume = (await Ok(await alice.GetAsync("/api/meps"))).EnumerateArray().Single();
        Assert.Equal("Alice", resume.GetProperty("pilote").GetString());
        Assert.Equal(5, resume.GetProperty("nbEtapes").GetInt32());
        Assert.Equal(1, resume.GetProperty("nbReussies").GetInt32());

        var claire = await _api.ClientConnecte("claire");
        Assert.False((await Ok(await claire.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- Parcours complet ----------
    [Fact]
    public async Task Parcours_de_test_complet_et_journal()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await IdDemo(alice);

        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution" }));
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        Assert.Equal("Alice", mep.GetProperty("demarrageAutorise").GetProperty("par").GetString());
        Assert.False(mep.GetProperty("droits").GetProperty("peutModifierPlan").GetBoolean());

        // Claire n'est pas responsable de E0002.
        var refus = await Action(claire, id, mep, "E0002", "demarrer");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("Prendre la main", await Message(refus));

        mep = await Ok(await Action(bob, id, mep, "E0002", "demarrer"));
        Assert.Equal("En cours", Dispo(mep, "E0002"));
        mep = await Ok(await Action(bob, id, mep, "E0002", "terminer",
            new { revision = Revision(mep, "E0002"), resultat = "Reussie", preuve = "API démo OK", resultatVerifie = true }));
        Assert.Equal("Terminée", Dispo(mep, "E0002"));
        Assert.Equal("Attend E0003", Dispo(mep, "E0004"));

        mep = await Ok(await Action(claire, id, mep, "E0003", "demarrer"));
        mep = await Ok(await Action(claire, id, mep, "E0003", "terminer",
            new { revision = Revision(mep, "E0003"), resultat = "Reussie", preuve = "WEB démo OK", resultatVerifie = true }));
        Assert.Equal("Disponible", Dispo(mep, "E0004"));

        // Reprise de l'étape en échec : pilote ou suppléant seulement.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0005", "autoriser-reprise")).StatusCode);
        mep = await Ok(await Action(alice, id, mep, "E0005", "autoriser-reprise"));
        Assert.Equal("Disponible", Dispo(mep, "E0005"));

        var evenements = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();
        Assert.Equal("etape.reprise", evenements[0].GetProperty("type").GetString());
        var details = evenements.Select(e => e.GetProperty("detail").GetString()).ToList();
        Assert.Contains("E0002 démarrée", details);
        Assert.Contains("E0002 réussie : API démo OK", details);
        Assert.Contains("E0003 réussie : WEB démo OK", details);
        Assert.Contains("Plan passé en exécution", details);
        Assert.Contains(evenements, e => e.GetProperty("type").GetString() == "etape.demarree" && e.GetProperty("auteur").GetString() == "Bob");
        Assert.Contains(evenements, e => e.GetProperty("type").GetString() == "etape.reussie" && e.GetProperty("auteur").GetString() == "Claire");
        Assert.Contains(evenements, e => e.GetProperty("type").GetString() == "mep.creee");
    }

    // ---------- Conflits et règles ----------
    [Fact]
    public async Task Conflit_de_revision_et_de_version_donnent_409()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var e2 = Etape(mep, "E0002");
        object Saisie(int revision, string action) => new
        {
            revision, action, systemeId = e2.GetProperty("systemeId").GetGuid(), responsableId = e2.GetProperty("responsableId").GetGuid(),
            criticite = "Moderee", instructions = "Instr.", resultatAttendu = "Attendu", dependances = new[] { EtapeId(mep, "E0001") },
        };
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}";

        var modifiee = await Ok(await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0002"), "Action modifiée")));
        Assert.Equal("Action modifiée", Etape(modifiee, "E0002").GetProperty("action").GetString());
        Assert.Equal(Revision(mep, "E0002") + 1, Revision(modifiee, "E0002"));
        Assert.Equal(mep.GetProperty("version").GetInt32() + 1, modifiee.GetProperty("version").GetInt32());

        var perime = await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0002"), "Autre"));
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
        Assert.NotEmpty(await Message(perime));

        var identite = new
        {
            version = mep.GetProperty("version").GetInt32(), outil = "Portail", versionActuelle = "1", versionCible = "2", objet = "Objet",
            impacts = "", dateMep = (string?)null, heureDebut = (string?)null, heureFin = (string?)null, fuseauHoraire = "Europe/Paris",
            indisponibiliteMinutes = 15, piloteId = mep.GetProperty("piloteId").GetGuid(), suppleantId = mep.GetProperty("suppleantId").GetGuid(),
            echeanceRepli = mep.GetProperty("echeanceRepli").GetDateTimeOffset(),
        };
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}", identite)).StatusCode);
        var ok = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", identite with { version = modifiee.GetProperty("version").GetInt32() }));
        Assert.Equal(15, ok.GetProperty("indisponibiliteMinutes").GetInt32());
    }

    [Fact]
    public async Task Structure_non_modifiable_en_execution()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution" }));
        var e2 = Etape(mep, "E0002");

        var modif = await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}", new
        {
            revision = Revision(mep, "E0002"), action = "x", systemeId = e2.GetProperty("systemeId").GetGuid(),
            responsableId = e2.GetProperty("responsableId").GetGuid(), criticite = "Faible", instructions = "i", resultatAttendu = "r",
            dependances = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, modif.StatusCode);
        Assert.Contains("préparation", await Message(modif));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/systemes", new { nom = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.DeleteAsync($"/api/meps/{id}")).StatusCode);
    }

    [Fact]
    public async Task Suppression_d_une_etape_dont_une_autre_depend_est_refusee()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));

        var refus = await alice.DeleteAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("E0004 dépend de E0002", await Message(refus));

        // E0004 ne sert de préalable à personne : suppression possible.
        var apres = await Ok(await alice.DeleteAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}"));
        Assert.Equal(6, apres.GetProperty("etapes").GetArrayLength());
    }

    [Fact]
    public async Task Creation_de_mep_etapes_et_cycle()
    {
        var bob = await _api.ClientConnecte("bob");
        var cree = await bob.PostAsJsonAsync("/api/meps", new { outil = "Outil neuf", objet = "Test" });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var mep = await Ok(cree);
        var id = mep.GetProperty("id").GetGuid();
        Assert.Equal(1, mep.GetProperty("participants").GetArrayLength());
        Assert.Equal(mep.GetProperty("piloteId").GetGuid(), mep.GetProperty("droits").GetProperty("participantId").GetGuid());
        Assert.True(mep.GetProperty("erreursPlan").GetArrayLength() > 0);

        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }));
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }));
        Assert.Equal(["E0001", "E0002"], mep.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray());
        Assert.Equal("Moderee", Etape(mep, "E0001").GetProperty("criticite").GetString());

        // E0002 dépend de E0001, puis E0001 de E0002 : cycle refusé.
        object Saisie(JsonElement m, string code, Guid dep) => new { revision = Revision(m, code), action = "a", dependances = new[] { dep } };
        mep = await Ok(await bob.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}", Saisie(mep, "E0002", EtapeId(mep, "E0001"))));
        var cycle = await bob.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0001")}", Saisie(mep, "E0001", EtapeId(mep, "E0002")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycle.StatusCode);
        Assert.Contains("Cycle", await Message(cycle));

        var deplacee = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}/deplacer", new { sens = "haut" }));
        Assert.Equal("E0002", deplacee.GetProperty("etapes")[0].GetProperty("code").GetString());

        // Un autre utilisateur ne peut pas supprimer la MEP ; le pilote le peut.
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.DeleteAsync($"/api/meps/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"/api/meps/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/meps/{id}")).StatusCode);
    }

    [Fact]
    public async Task Corps_invalide_donne_400()
    {
        var alice = await _api.ClientConnecte("alice");
        var rep = await alice.PostAsync("/api/meps", new StringContent("{pas du json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, rep.StatusCode);
        Assert.Contains("Requête invalide", await Message(rep));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/meps", new { outil = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/inexistant")).StatusCode);
    }

    // ---------- Flux temps réel ----------
    [Fact]
    public async Task Flux_sse_notifie_chaque_modification()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);

        using var requete = new HttpRequestMessage(HttpMethod.Get, $"/api/meps/{id}/flux");
        using var reponse = await alice.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", reponse.Content.Headers.ContentType?.MediaType);
        using var lecteur = new StreamReader(await reponse.Content.ReadAsStreamAsync());
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.StartsWith(":", (await lecteur.ReadLineAsync(delai.Token))!);

        var apres = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/participants",
            new { nom = "Dave", role = "Testeur", contact = "", utilisateurId = (Guid?)null }));
        string? ligne;
        do { ligne = await lecteur.ReadLineAsync(delai.Token); } while (ligne is not null && !ligne.StartsWith("data:"));
        Assert.Equal($"data: {{\"version\":{apres.GetProperty("version").GetInt32()}}}", ligne);
    }
}
