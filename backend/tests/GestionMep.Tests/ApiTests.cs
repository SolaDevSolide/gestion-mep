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

    private static Task<Guid> IdDemo(HttpClient c) => AidesApi.IdDemo(c);

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
    public async Task Creation_d_utilisateur_reservee_aux_admins_par_la_console_seulement()
    {
        var bob = await _api.ClientConnecte("bob");
        var corps = new { login = "dave", nomAffiche = "Dave", estAdmin = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/utilisateurs", corps)).StatusCode);

        var alice = await _api.ClientConnecte("alice");
        // L'ancienne route, qui posait un mot de passe choisi et non provisoire, n'existe plus.
        Assert.Equal(HttpStatusCode.NotFound,
            (await alice.PostAsJsonAsync("/api/utilisateurs", new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false })).StatusCode);
        var cree = await Ok(await alice.PostAsJsonAsync("/api/admin/utilisateurs", corps));
        Assert.True(cree.GetProperty("utilisateur").GetProperty("doitChangerMotDePasse").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/admin/utilisateurs", corps)).StatusCode);
        Assert.Equal(4, (await Ok(await alice.GetAsync("/api/utilisateurs"))).GetArrayLength());
        // Mot de passe provisoire : la personne doit le changer avant de travailler.
        var dave = await _api.ClientConnecte("dave", cree.GetProperty("motDePasseProvisoire").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await dave.GetAsync("/api/meps")).StatusCode);
    }

    // ---------- Lecture ----------
    [Fact]
    public async Task Demo_chargee_avec_disponibilites()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));

        Assert.Equal("Preparation", mep.GetProperty("phase").GetString());
        // 9 étapes de déploiement + 3 de retour arrière.
        Assert.Equal(12, mep.GetProperty("etapes").GetArrayLength());
        Assert.Equal("RetourArriere", Etape(mep, "R0001").GetProperty("plan").GetString());
        Assert.Equal("Si retour arrière", Dispo(mep, "R0001"));
        Assert.Equal(1, Etape(mep, "E0005").GetProperty("resolutionIds").GetArrayLength());
        Assert.Equal(30, mep.GetProperty("planRepli").GetProperty("dureeTesteeMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("issue").ValueKind);
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("retourArriere").ValueKind);
        Assert.Equal(5, mep.GetProperty("systemes").EnumerateArray().Count(s => s.GetProperty("catalogueId").ValueKind == JsonValueKind.String));
        Assert.Equal(0, mep.GetProperty("resolutions").GetArrayLength());
        Assert.Equal("Disponible", Etape(mep, "E0004").GetProperty("disponibilite").GetProperty("statut").GetString());
        Assert.Equal("Attend E0004", Dispo(mep, "E0007"));
        Assert.Equal("Terminée", Dispo(mep, "E0001"));
        // Seule l'approbation de Claire manque.
        Assert.Equal(["Il faut 1 approbation(s)."], mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.True(mep.GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.True(mep.GetProperty("droits").GetProperty("peutModifierPlan").GetBoolean());
        Assert.Equal("Bob", mep.GetProperty("procedureRelue").GetProperty("par").GetString());

        var resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        Assert.True(resume.GetProperty("toucheProduction").GetBoolean());
        Assert.Equal("Alice", resume.GetProperty("pilote").GetString());
        Assert.Equal(9, resume.GetProperty("nbEtapes").GetInt32());
        Assert.Equal(3, resume.GetProperty("nbReussies").GetInt32());

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

        var mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        Assert.Equal("Alice", mep.GetProperty("demarrageAutorise").GetProperty("par").GetString());
        Assert.False(mep.GetProperty("droits").GetProperty("peutModifierPlan").GetBoolean());

        // Alice démarre E0004 et en devient l'exécutante ; Claire ne peut plus ni la démarrer ni la terminer.
        mep = await Ok(await Action(alice, id, mep, "E0004", "demarrer"));
        Assert.Equal("En cours", Dispo(mep, "E0004"));
        Assert.Equal(mep.GetProperty("piloteId").GetGuid(), Etape(mep, "E0004").GetProperty("executantId").GetGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0004", "demarrer")).StatusCode);
        var refus = await Action(claire, id, mep, "E0004", "terminer",
            new { revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "x", resultatVerifie = true });
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Contains("Prendre la main", await Message(refus));
        mep = await Ok(await Action(alice, id, mep, "E0004", "terminer",
            new { revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "Schéma migré", resultatVerifie = true }));
        Assert.Equal("Terminée", Dispo(mep, "E0004"));
        Assert.Equal("Attend E0005", Dispo(mep, "E0007"));

        mep = await Ok(await Action(claire, id, mep, "E0006", "demarrer"));
        mep = await Ok(await Action(claire, id, mep, "E0006", "terminer",
            new { revision = Revision(mep, "E0006"), resultat = "Reussie", preuve = "Portail publié", resultatVerifie = true }));
        Assert.Equal("Attend E0005", Dispo(mep, "E0007"));

        // Étape en échec : pas de démarrage avant la reprise, autorisée par le pilote ou un suppléant seulement.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(bob, id, mep, "E0005", "demarrer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Action(claire, id, mep, "E0005", "autoriser-reprise")).StatusCode);
        mep = await Ok(await Action(alice, id, mep, "E0005", "autoriser-reprise"));
        Assert.Equal("Disponible", Dispo(mep, "E0005"));

        var evenements = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();
        Assert.Equal("etape.reprise", evenements[0].GetProperty("type").GetString());
        var details = evenements.Select(e => e.GetProperty("detail").GetString()).ToList();
        Assert.Contains("E0004 démarrée par Alice", details);
        Assert.Contains("E0004 réussie : Schéma migré", details);
        Assert.Contains("E0006 réussie : Portail publié", details);
        Assert.Contains("Plan passé en exécution", details);
        Assert.Contains(evenements, e => e.GetProperty("type").GetString() == "etape.demarree" && e.GetProperty("auteur").GetString() == "Alice");
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
        var e4 = Etape(mep, "E0004");
        object Saisie(int revision, string action) => new
        {
            revision, action, systemeIds = e4.GetProperty("systemeIds"),
            criticite = "Moderee", instructions = "Instr.", resultatAttendu = "Attendu", dependances = new[] { EtapeId(mep, "E0001") },
        };
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}";

        var modifiee = await Ok(await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0004"), "Action modifiée")));
        Assert.Equal("Action modifiée", Etape(modifiee, "E0004").GetProperty("action").GetString());
        Assert.Equal(Revision(mep, "E0004") + 1, Revision(modifiee, "E0004"));
        Assert.Equal(mep.GetProperty("version").GetInt32() + 1, modifiee.GetProperty("version").GetInt32());

        var perime = await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0004"), "Autre"));
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
        Assert.NotEmpty(await Message(perime));

        var identite = new
        {
            version = mep.GetProperty("version").GetInt32(), outil = "Portail", versionActuelle = "1", versionCible = "2", objet = "Objet",
            impacts = "", dateMep = (string?)null, heureDebut = (string?)null, heureFin = (string?)null, fuseauHoraire = "Europe/Paris",
            indisponibiliteMinutes = 15, piloteId = mep.GetProperty("piloteId").GetGuid(), suppleantIds = mep.GetProperty("suppleantIds"),
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
        var mep = await AidesApi.Go(_api, alice, id);
        var e4 = Etape(mep, "E0004");

        var modif = await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}", new
        {
            revision = Revision(mep, "E0004"), action = "x", systemeIds = e4.GetProperty("systemeIds"),
            criticite = "Faible", instructions = "i", resultatAttendu = "r",
            dependances = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, modif.StatusCode);
        Assert.Contains("préparation", await Message(modif));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/systemes", new { nom = "X" })).StatusCode);
        // Une MEP qui a eu un Go ne se supprime plus, même par un administrateur : elle s'annule.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AidesApi.Supprimer(alice, id, "Portail de démonstration 2.0-démo")).StatusCode);
    }

    [Fact]
    public async Task Suppression_d_une_etape_rattache_ses_suivants_a_la_ligne_au_dessus()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var revisionE8 = Revision(mep, "E0008");

        // E0008 n'attendait que E0007 : elle attend désormais toute la ligne au-dessus, et la ligne vide disparaît.
        var apres = await Ok(await alice.DeleteAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0007")}"));
        Assert.Equal(11, apres.GetProperty("etapes").GetArrayLength());
        Assert.Equal([EtapeId(apres, "E0004"), EtapeId(apres, "E0005"), EtapeId(apres, "E0006")],
            Etape(apres, "E0008").GetProperty("dependances").EnumerateArray().Select(d => d.GetGuid()).ToArray());
        Assert.Equal(2, Etape(apres, "E0008").GetProperty("ligne").GetInt32());
        Assert.Equal(revisionE8 + 1, Revision(apres, "E0008"));
        Assert.Equal(["La procédure doit être relue par un opérateur.", "Il faut 1 approbation(s)."], apres.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToArray());
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
        // Le créateur n'est plus pilote : il est participant et responsable technique.
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("piloteId").ValueKind);
        Assert.Equal(mep.GetProperty("participants")[0].GetProperty("id").GetGuid(), mep.GetProperty("droits").GetProperty("participantId").GetGuid());
        Assert.Equal(mep.GetProperty("droits").GetProperty("participantId").GetGuid(), mep.GetProperty("responsablesTechniques")[0].GetProperty("participantId").GetGuid());
        Assert.True(mep.GetProperty("droits").GetProperty("estResponsableTechnique").GetBoolean());
        Assert.Contains("Renseigner le pilote.", mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()));
        Assert.True(mep.GetProperty("erreursPlan").GetArrayLength() > 0);

        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }));
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes", new { apres = EtapeId(mep, "E0001") }));
        Assert.Equal(["E0001", "E0002"], mep.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray());
        Assert.Equal("Moderee", Etape(mep, "E0001").GetProperty("criticite").GetString());
        // « + sous une carte » : la nouvelle étape l'attend ; sa ligne (lecture seule) est calculée.
        Assert.Equal(1, Etape(mep, "E0002").GetProperty("ligne").GetInt32());
        Assert.Equal(EtapeId(mep, "E0001"), Etape(mep, "E0002").GetProperty("dependances")[0].GetGuid());

        // E0001 ne peut pas attendre E0002 : cycle.
        object Saisie(JsonElement m, string code, Guid dep) => new { revision = Revision(m, code), action = "a", dependances = new[] { dep } };
        var cycle = await bob.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0001")}", Saisie(mep, "E0001", EtapeId(mep, "E0002")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycle.StatusCode);
        Assert.StartsWith("Opération refusée : elle créerait un cycle", await Message(cycle));

        // Glisser E0001 après E0002 : elle quitte sa place, E0002 n'attend plus rien et E0001 l'attend.
        var placee = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0001")}/deplacer", new { apres = EtapeId(mep, "E0002") }));
        Assert.Equal("E0002", placee.GetProperty("etapes")[0].GetProperty("code").GetString());
        Assert.Empty(Etape(placee, "E0002").GetProperty("dependances").EnumerateArray());
        Assert.Equal([EtapeId(mep, "E0002")], Etape(placee, "E0001").GetProperty("dependances").EnumerateArray().Select(d => d.GetGuid()));

        // Seul un administrateur supprime définitivement une MEP, même son créateur ne le peut pas (#93).
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal(HttpStatusCode.Forbidden, (await AidesApi.Supprimer(claire, id, "Outil neuf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AidesApi.Supprimer(bob, id, "Outil neuf")).StatusCode);
        var alice = await _api.ClientConnecte("alice");
        Assert.Equal(HttpStatusCode.NoContent, (await AidesApi.Supprimer(alice, id, "Outil neuf")).StatusCode);
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
        // Le flux envoie aussi la présence (event: presence) : on cherche l'événement maj.
        string? ligne;
        do { ligne = await lecteur.ReadLineAsync(delai.Token); } while (ligne is not null && ligne != "event: maj");
        ligne = await lecteur.ReadLineAsync(delai.Token);
        Assert.Equal($"data: {{\"version\":{apres.GetProperty("version").GetInt32()}}}", ligne);
    }
}
