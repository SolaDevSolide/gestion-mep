using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Tests d'intégration du jalon 2 : retour arrière, prise de main, catalogue, résolutions types.</summary>
[Collection("Api")]
public class ApiJalon2Tests : IDisposable
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
    private static string Etat(JsonElement mep, string code) => Etape(mep, code).GetProperty("etat").GetString()!;

    private static Task<HttpResponseMessage> Action(HttpClient c, Guid mepId, JsonElement mep, string code, string action, object? corps = null) =>
        c.PostAsJsonAsync($"/api/meps/{mepId}/etapes/{EtapeId(mep, code)}/{action}", corps ?? new { revision = Revision(mep, code) });

    private static async Task<JsonElement> Reussir(HttpClient c, Guid id, JsonElement mep, string code, string preuve)
    {
        mep = await Ok(await Action(c, id, mep, code, "demarrer"));
        return await Ok(await Action(c, id, mep, code, "terminer",
            new { revision = Revision(mep, code), resultat = "Reussie", preuve, resultatVerifie = true }));
    }

    private static async Task<List<(string Type, string Auteur, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("auteur").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private async Task<(HttpClient Alice, Guid Id, JsonElement Mep)> DemoEnExecution()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await AidesApi.Go(_api, alice, id);
        return (alice, id, mep);
    }

    private static async Task<JsonElement> ResolutionDe(HttpClient c, JsonElement mep, string code) =>
        await Ok(await c.GetAsync($"/api/resolutions/{Etape(mep, code).GetProperty("resolutionIds")[0].GetGuid()}"));

    // ---------- Prise de main ----------
    [Fact]
    public async Task Bob_prend_la_main_sur_E0004_demarree_par_Claire_puis_la_termine()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var claire = await _api.ClientConnecte("claire");
        var bob = await _api.ClientConnecte("bob");
        var participantBob = mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == "Bob").GetProperty("id").GetGuid();

        // Claire démarre E0004 puis s'absente : Bob prend la main et termine.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Action(bob, id, mep, "E0004", "prendre-la-main", new { revision = Revision(mep, "E0004"), motif = "x" })).StatusCode); // pas démarrée
        mep = await Ok(await Action(claire, id, mep, "E0004", "demarrer"));
        mep = await Ok(await Action(bob, id, mep, "E0004", "prendre-la-main", new { revision = Revision(mep, "E0004"), motif = "Claire est absente" }));
        Assert.Equal(participantBob, Etape(mep, "E0004").GetProperty("executantId").GetGuid());

        // Claire n'est plus l'exécutante (et n'est ni pilote ni suppléante).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0004", "terminer",
            new { revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "x", resultatVerifie = true })).StatusCode);

        mep = await Ok(await Action(bob, id, mep, "E0004", "terminer",
            new { revision = Revision(mep, "E0004"), resultat = "Reussie", preuve = "Schéma migré", resultatVerifie = true }));
        Assert.Equal("Reussie", Etat(mep, "E0004"));
        // Une étape réussie ne peut plus être reprise en main.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0004", "prendre-la-main", new { revision = Revision(mep, "E0004"), motif = "x" })).StatusCode);

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "etape.prise-en-main" && e.Auteur == "Bob" && e.Detail == "Bob a pris la main sur E0004 (à la place de Claire) : Claire est absente");
        Assert.Contains(journal, e => e.Type == "etape.demarree" && e.Detail == "E0004 démarrée par Claire");
        Assert.Contains(journal, e => e.Type == "etape.reussie" && e.Auteur == "Bob");
    }

    [Fact]
    public async Task Un_non_participant_ne_prend_pas_la_main_mais_un_administrateur_le_peut()
    {
        var (alice, id, mep) = await DemoEnExecution();
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "eve", nomAffiche = "Eve", motDePasse = "motdepasse", estAdmin = true })).StatusCode);
        var dave = await _api.ClientConnecte("dave", "motdepasse");
        var eve = await _api.ClientConnecte("eve", "motdepasse");
        Assert.Equal(JsonValueKind.Null, (await Ok(await dave.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("participantId").ValueKind);

        // Dave n'est pas participant : ni démarrage, ni prise de main (403, aucun participant créé).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(dave, id, mep, "E0004", "demarrer")).StatusCode);
        var refus = await Action(dave, id, mep, "E0005", "prendre-la-main", new { revision = Revision(mep, "E0005"), motif = "Renfort" });
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Equal("Seuls les participants de la MEP, le pilote, ses suppléants ou un administrateur peuvent prendre la main.", await Message(refus));
        Assert.Equal(3, (await Ok(await alice.GetAsync($"/api/meps/{id}"))).GetProperty("participants").GetArrayLength());

        // Eve, administratrice non participante, prend la main : elle devient participante « Prise en main ».
        mep = await Ok(await Action(eve, id, mep, "E0005", "prendre-la-main", new { revision = Revision(mep, "E0005"), motif = "Renfort" }));
        var participants = mep.GetProperty("participants").EnumerateArray().ToList();
        Assert.Equal(4, participants.Count);
        var nouveau = participants.Single(p => p.GetProperty("nom").GetString() == "Eve");
        Assert.Equal("Prise en main", nouveau.GetProperty("role").GetString());
        Assert.Equal("Eve", nouveau.GetProperty("utilisateurNom").GetString());
        Assert.Equal(nouveau.GetProperty("id").GetGuid(), Etape(mep, "E0005").GetProperty("executantId").GetGuid());
        Assert.Equal(nouveau.GetProperty("id").GetGuid(), mep.GetProperty("droits").GetProperty("participantId").GetGuid());
        Assert.Contains(await Journal(alice, id), e => e.Type == "etape.prise-en-main" && e.Auteur == "Eve" && e.Detail.EndsWith(" : Renfort"));

        // Un refus ne crée pas de participant : le déploiement d'une MEP en préparation n'accepte pas de prise de main.
        var claire = await _api.ClientConnecte("claire");
        var autre = await Ok(await claire.PostAsJsonAsync("/api/meps", new { outil = "Autre", objet = "" }));
        Assert.Equal(1, autre.GetProperty("participants").GetArrayLength());
    }

    // ---------- Résolutions ----------
    [Fact]
    public async Task Resolution_conforme_sur_E0005_remet_l_etape_a_faire_et_la_rend_redemarrable()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var bob = await _api.ClientConnecte("bob");
        Assert.Equal("Arrêt / résolution", Dispo(mep, "E0005"));
        var type = await ResolutionDe(alice, mep, "E0005");
        var typeId = type.GetProperty("id").GetGuid();

        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0005")}/resolutions",
            new { resolutionTypeId = typeId, revision = Revision(mep, "E0005") }));
        Assert.Equal("Résolution en cours", Dispo(mep, "E0005"));
        var resolution = mep.GetProperty("resolutions").EnumerateArray().Single();
        Assert.Equal("EnCours", resolution.GetProperty("statut").GetString());
        Assert.Equal("Bob", resolution.GetProperty("demarreePar").GetString());
        var rid = resolution.GetProperty("id").GetGuid();

        // Vérification avant la correction, ou correction par un autre exécutant : refusées.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/verification",
            new { resultat = "Conforme", observation = "ok" })).StatusCode);
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/corrections/1",
            new { resultat = "Conforme", observation = "ok" })).StatusCode);

        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/corrections/1", new { resultat = "Conforme", observation = "Réponse fictive lue" }));
        var correction = mep.GetProperty("resolutions")[0].GetProperty("corrections")[0];
        Assert.Equal("Conforme", correction.GetProperty("resultat").GetString());
        Assert.Equal("Bob", correction.GetProperty("par").GetString());

        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/verification", new { resultat = "Conforme", observation = "Correction conforme" }));
        Assert.Equal("Reussie", mep.GetProperty("resolutions")[0].GetProperty("statut").GetString());
        Assert.Equal("AFaire", Etat(mep, "E0005"));
        Assert.Equal("Disponible", Dispo(mep, "E0005"));
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());

        mep = await Ok(await Action(bob, id, mep, "E0005", "demarrer"));
        Assert.Equal("EnCours", Etat(mep, "E0005"));

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "resolution.demarree" && e.Detail == $"Résolution « {type.GetProperty("titre").GetString()} » démarrée sur E0005");
        Assert.Contains(journal, e => e.Type == "resolution.correction" && e.Detail.StartsWith("Correction 1 conforme"));
        Assert.Contains(journal, e => e.Type == "resolution.reussie");
    }

    [Fact]
    public async Task Resolution_en_echec_declenche_le_retour_arriere()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var bob = await _api.ClientConnecte("bob");
        var typeId = (await ResolutionDe(alice, mep, "E0005")).GetProperty("id").GetGuid();
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0005")}/resolutions",
            new { resolutionTypeId = typeId, revision = Revision(mep, "E0005") }));
        var rid = mep.GetProperty("resolutions")[0].GetProperty("id").GetGuid();

        // Un échec sans observation est refusé.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/corrections/1",
            new { resultat = "Echec", observation = "" })).StatusCode);
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/resolutions/{rid}/corrections/1",
            new { resultat = "Echec", observation = "Toujours pas de réponse" }));

        Assert.Equal("RetourArriere", mep.GetProperty("phase").GetString());
        Assert.Equal("Echouee", mep.GetProperty("resolutions")[0].GetProperty("statut").GetString());
        var repli = mep.GetProperty("retourArriere");
        Assert.Equal(JsonValueKind.Null, repli.GetProperty("par").ValueKind);
        Assert.Contains("correction 1 en échec", repli.GetProperty("motif").GetString());
        Assert.Equal("Disponible", Dispo(mep, "R0001"));

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "resolution.echouee");
        Assert.Contains(journal, e => e.Type == "repli.declenche" && e.Auteur == "Système" && e.Detail.StartsWith("Retour arrière déclenché automatiquement : "));
    }

    // ---------- Retour arrière manuel et clôture ----------
    [Fact]
    public async Task Retour_arriere_manuel_puis_etapes_de_repli_et_cloture()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal("Si retour arrière", Dispo(mep, "R0001"));

        // Claire n'est ni pilote ni suppléante ; le motif est obligatoire.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = " " })).StatusCode);
        // La clôture est impossible tant que rien n'est terminé.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { })).StatusCode);

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Service dégradé" }));
        Assert.Equal("RetourArriere", mep.GetProperty("phase").GetString());
        Assert.Equal("Alice", mep.GetProperty("retourArriere").GetProperty("par").GetString());
        Assert.Equal("Service dégradé", mep.GetProperty("retourArriere").GetProperty("motif").GetString());
        Assert.Equal("Retour arrière déclenché", Dispo(mep, "E0006"));
        Assert.Equal("Disponible", Dispo(mep, "R0001"));
        Assert.Equal("Disponible", Dispo(mep, "R0002"));
        Assert.Equal("Attend R0001", Dispo(mep, "R0003"));

        // Le déploiement est arrêté.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0006", "demarrer")).StatusCode);

        // Clôture refusée tant que le repli n'est pas terminé.
        var refus = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("R0001", await Message(refus));

        mep = await Reussir(bob, id, mep, "R0001", "version fictive 1.0 rétablie");
        mep = await Reussir(alice, id, mep, "R0002", "Base restaurée");
        mep = await Reussir(claire, id, mep, "R0003", "Service 1.0 OK");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { })).StatusCode);

        // Bilan activé : obligatoire, aussi pour une MEP repliée.
        var sansBilan = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansBilan.StatusCode);
        Assert.Contains("Bilan", await Message(sansBilan));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer",
            new { ceQuiAMarche = "Le repli", incidents = "Service dégradé", actions = "Analyser la cause" }));
        Assert.Equal("Cloturee", mep.GetProperty("phase").GetString());
        Assert.Equal("Repliee", mep.GetProperty("issue").GetString());
        Assert.Equal("Service dégradé", mep.GetProperty("bilan").GetProperty("incidents").GetString());
        Assert.Equal("Alice", mep.GetProperty("bilan").GetProperty("redigePar").GetString());
        Assert.Equal("Alice", mep.GetProperty("cloture").GetProperty("par").GetString());

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "repli.declenche" && e.Detail == "Retour arrière déclenché par Alice : Service dégradé");
        Assert.Contains(journal, e => e.Type == "mep.cloturee" && e.Detail == "MEP clôturée : repliée");
        // La liste des MEP porte aussi l'issue (affichée « Clôturée : repliée »).
        var liste = await Ok(await alice.GetAsync("/api/meps"));
        Assert.Equal("Repliee", liste.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id).GetProperty("issue").GetString());
    }

    [Fact]
    public async Task Cloture_reussie_apres_deploiement_complet_et_recette()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        // E0004 échoue puis est reprise avec succès : la MEP sera « réussie avec incidents » (#91).
        mep = await Ok(await Action(alice, id, mep, "E0004", "demarrer"));
        mep = await Ok(await Action(alice, id, mep, "E0004", "terminer",
            new { revision = Revision(mep, "E0004"), resultat = "Echec", preuve = "Verrou sur la table", resultatVerifie = false }));
        Assert.Equal("Execution", mep.GetProperty("etat").GetString());
        Assert.Equal(1, mep.GetProperty("echecsSurvenus").GetInt32());
        mep = await Ok(await Action(alice, id, mep, "E0004", "autoriser-reprise"));
        mep = await Reussir(alice, id, mep, "E0004", "Schéma migré");
        mep = await Ok(await Action(alice, id, mep, "E0005", "autoriser-reprise"));
        mep = await Reussir(bob, id, mep, "E0005", "Incident corrigé");
        mep = await Reussir(claire, id, mep, "E0006", "Portail publié");
        mep = await Ok(await Action(alice, id, mep, "E0007", "autoriser-non-retour"));
        mep = await Reussir(bob, id, mep, "E0007", "Données basculées");
        mep = await Reussir(claire, id, mep, "E0008", "Portail basculé");
        mep = await Reussir(alice, id, mep, "E0009", "Contrôles verts");
        var refus = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Rien à signaler" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("Passer d'abord en recette", await Message(refus));

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" }));
        foreach (var f in mep.GetProperty("fichesRecette").EnumerateArray().ToList())
            foreach (var t in f.GetProperty("etapes").EnumerateArray().ToList())
                mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/{f.GetProperty("id").GetGuid()}/etapes/{t.GetProperty("id").GetGuid()}/resultat",
                    new { resultat = "Conforme", observation = "OK" }));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Rien à signaler" }));
        Assert.Equal("Reussie", mep.GetProperty("issue").GetString());
        Assert.Equal("Cloturee", mep.GetProperty("phase").GetString());
        // Réussie avec incidents (#91), jusque dans le journal.
        Assert.Equal("AvecIncidents", mep.GetProperty("etat").GetString());
        Assert.Equal(1, mep.GetProperty("echecsSurvenus").GetInt32());
        Assert.Contains(await Journal(alice, id), e => e.Detail == "MEP clôturée : réussie avec incidents");
    }

    // ---------- Échéance automatique ----------
    [Fact]
    public async Task Echeance_depassee_bascule_automatiquement_a_la_lecture()
    {
        var (alice, id, mep) = await DemoEnExecution();
        var echeance = DateTimeOffset.UtcNow.AddHours(-1);
        var identite = new
        {
            version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil"), versionActuelle = mep.GetProperty("versionActuelle"),
            versionCible = mep.GetProperty("versionCible"), objet = mep.GetProperty("objet"), impacts = mep.GetProperty("impacts"),
            dateMep = mep.GetProperty("dateMep"), heureDebut = mep.GetProperty("heureDebut"), heureFin = mep.GetProperty("heureFin"),
            fuseauHoraire = mep.GetProperty("fuseauHoraire"), indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"),
            piloteId = mep.GetProperty("piloteId"), suppleantIds = mep.GetProperty("suppleantIds"), echeanceRepli = echeance,
        };
        Assert.Equal("Execution", (await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", identite))).GetProperty("phase").GetString());

        // La lecture suivante applique l'échéance avant de répondre.
        var lu = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("RetourArriere", lu.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, lu.GetProperty("retourArriere").GetProperty("par").ValueKind);
        Assert.Equal("Échéance de retour arrière atteinte.", lu.GetProperty("retourArriere").GetProperty("motif").GetString());
        Assert.Equal("Disponible", Dispo(lu, "R0001"));
        Assert.Equal(mep.GetProperty("version").GetInt32() + 2, lu.GetProperty("version").GetInt32());

        // Une seule bascule dans le journal, même après d'autres lectures.
        await alice.GetAsync($"/api/meps/{id}");
        var bascules = (await Journal(alice, id)).Where(e => e.Type == "repli.declenche").ToList();
        Assert.Single(bascules);
        Assert.Equal("Système", bascules[0].Auteur);
        Assert.Equal("Retour arrière déclenché automatiquement : Échéance de retour arrière atteinte.", bascules[0].Detail);
    }

    // ---------- Plan de retour arrière ----------
    [Fact]
    public async Task Plan_de_repli_modifiable_en_preparation_seulement()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var corps = new
        {
            version = mep.GetProperty("version").GetInt32(), quandDeclencher = "  Si l'API ne répond plus ", dureeTesteeMinutes = 45,
            versionARestaurer = "1.0", donneesDepuisSauvegarde = "Sauvegarde de 19h", siRepliEchoue = "Appeler l'astreinte",
        };
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", corps));
        Assert.Equal("Si l'API ne répond plus", mep.GetProperty("planRepli").GetProperty("quandDeclencher").GetString());
        Assert.Equal(45, mep.GetProperty("planRepli").GetProperty("dureeTesteeMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", corps)).StatusCode);
        Assert.Contains(await Journal(alice, id), e => e.Type == "repli.modifie");

        mep = await AidesApi.Go(_api, alice, id);
        var refus = await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", corps with { version = mep.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "RetourArriere" })).StatusCode);
    }

    [Fact]
    public async Task Etapes_de_repli_codes_R_dependances_du_meme_plan_et_placement_dans_le_plan()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));

        // Dépendance vers l'autre plan : 422.
        var croise = await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { plan = "RetourArriere", dependances = new[] { EtapeId(mep, "E0001") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, croise.StatusCode);
        Assert.Contains("autre plan", await Message(croise));
        // Idem dans l'autre sens, sur une étape existante.
        var e2 = Etape(mep, "E0002");
        var inverse = await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}", new
        {
            revision = Revision(mep, "E0002"), action = "x", systemeIds = e2.GetProperty("systemeIds"), responsableId = e2.GetProperty("responsableId"),
            criticite = "Moderee", instructions = "i", resultatAttendu = "r", dependances = new[] { EtapeId(mep, "R0001") },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inverse.StatusCode);

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { plan = "RetourArriere", action = "Nouveau repli", apres = EtapeId(mep, "R0003") }));
        Assert.Equal("RetourArriere", Etape(mep, "R0004").GetProperty("plan").GetString());
        Assert.Equal(2, Etape(mep, "R0004").GetProperty("ligne").GetInt32());
        Assert.Equal(0, Etape(mep, "R0004").GetProperty("ordre").GetInt32());
        Assert.Equal(EtapeId(mep, "R0003"), Etape(mep, "R0004").GetProperty("dependances").EnumerateArray().Single().GetGuid());
        // Une place dans l'autre plan : 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { apres = EtapeId(mep, "R0001") })).StatusCode);
        // Sans place : étape isolée, en haut.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { }));
        Assert.Equal(0, Etape(mep, "E0010").GetProperty("ligne").GetInt32());
        Assert.Empty(Etape(mep, "E0010").GetProperty("dependances").EnumerateArray());

        // R0004 glissée en parallèle de R0001 : mêmes préalables (aucun) et mêmes suivants (R0003), sans toucher au déploiement.
        var e1 = (Etape(mep, "E0001").GetProperty("ligne").GetInt32(), Etape(mep, "E0001").GetProperty("ordre").GetInt32());
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "R0004")}/deplacer", new { paralleleA = EtapeId(mep, "R0001") }));
        // Rangée juste à droite de R0001.
        Assert.Equal((0, 1), (Etape(mep, "R0004").GetProperty("ligne").GetInt32(), Etape(mep, "R0004").GetProperty("ordre").GetInt32()));
        Assert.Equal((0, 2), (Etape(mep, "R0001").GetProperty("ordre").GetInt32(), Etape(mep, "R0002").GetProperty("ordre").GetInt32()));
        Assert.Equal(e1, (Etape(mep, "E0001").GetProperty("ligne").GetInt32(), Etape(mep, "E0001").GetProperty("ordre").GetInt32()));
        Assert.Equal(3, Etape(mep, "R0003").GetProperty("dependances").GetArrayLength()); // R0001, R0002 et R0004
        // Les opérations par ligne n'existent plus.
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "R0001")}/placer", new { ligne = 0, position = 0 })).StatusCode);
    }

    // ---------- Catalogue des systèmes ----------
    [Fact]
    public async Task Catalogue_ecriture_reservee_aux_admins_et_ajout_a_une_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await IdDemo(alice);

        var liste = (await Ok(await bob.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Select(s => s.GetProperty("nom").GetString()).ToList();
        Assert.Equal(["API", "BATCH", "BDD", "LDAP", "MDM", "WEB"], liste);

        var corps = new { nom = "CACHE", description = "Cache distribué", cible = "cache-01", actif = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/catalogue/systemes", corps)).StatusCode);
        var cree = await alice.PostAsJsonAsync("/api/catalogue/systemes", corps);
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var cacheId = (await Lire(cree)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/catalogue/systemes", corps with { nom = "cache" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/catalogue/systemes", corps with { nom = " " })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/catalogue/systemes/{cacheId}", corps)).StatusCode);
        var modifie = await Ok(await alice.PutAsJsonAsync($"/api/catalogue/systemes/{cacheId}", corps with { description = "Cache Redis" }));
        Assert.Equal("Cache Redis", modifie.GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/catalogue/systemes/{cacheId}")).StatusCode);

        // Ajout à la MEP depuis le catalogue : nom et cible copiés, catalogueId renseigné ; une seule fois.
        var mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/systemes/depuis-catalogue", new { catalogueId = cacheId, impact = "Purge du cache" }));
        var systeme = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "CACHE");
        Assert.Equal("cache-01", systeme.GetProperty("cible").GetString());
        Assert.Equal("Purge du cache", systeme.GetProperty("impact").GetString());
        Assert.Equal(cacheId, systeme.GetProperty("catalogueId").GetGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await bob.PostAsJsonAsync($"/api/meps/{id}/systemes/depuis-catalogue", new { catalogueId = cacheId, impact = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bob.PostAsJsonAsync($"/api/meps/{id}/systemes/depuis-catalogue", new { catalogueId = Guid.NewGuid(), impact = "" })).StatusCode);

        // Utilisé par une MEP : la suppression se limite à une désactivation. Un système inutilisé disparaît.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/catalogue/systemes/{cacheId}")).StatusCode);
        var apres = (await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == cacheId);
        Assert.False(apres.GetProperty("actif").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await bob.PostAsJsonAsync($"/api/meps/{id}/systemes/depuis-catalogue", new { catalogueId = cacheId, impact = "" })).StatusCode);

        var inutile = (await Lire(await alice.PostAsJsonAsync("/api/catalogue/systemes", corps with { nom = "TEMP" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/catalogue/systemes/{inutile}")).StatusCode);
        Assert.DoesNotContain((await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray(), s => s.GetProperty("id").GetGuid() == inutile);
    }

    // ---------- Résolutions types ----------
    [Fact]
    public async Task Resolutions_types_creation_invalide_valide_et_conflit_de_version()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var api = (await Ok(await bob.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("id").GetGuid();

        var invalide = await bob.PostAsJsonAsync("/api/resolutions", new
        {
            titre = "", symptomes = "", corrections = new[] { new { ordre = 1, instruction = "x", resultatAttendu = "" } },
            verificationInstruction = "", verificationAttendu = "",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalide.StatusCode);
        var erreurs = (await Lire(invalide)).GetProperty("erreurs").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains("Donner un titre à la résolution.", erreurs);
        Assert.Contains("Correction 1 : préciser l'instruction et le résultat attendu.", erreurs);
        Assert.Contains("Préciser la vérification finale et son résultat attendu.", erreurs);
        Assert.Contains("Donner un titre", await Message(invalide));
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync("/api/resolutions", new
        {
            titre = "T", symptomes = "S", systemeCatalogueIds = new[] { api, Guid.NewGuid() },
            corrections = new[] { new { ordre = 1, instruction = "i", resultatAttendu = "a" } }, verificationInstruction = "v", verificationAttendu = "a",
        })).StatusCode);

        object Saisie(string titre, int? version, Guid? systeme) => new
        {
            version, titre, symptomes = "Erreur 500", diagnostic = "Lire le journal", systemeCatalogueIds = systeme is { } s ? new[] { s, s } : Array.Empty<Guid>(),
            corrections = new[] { new { ordre = 2, instruction = "Redémarrer", resultatAttendu = "OK" }, new { ordre = 1, instruction = "Vider le cache", resultatAttendu = "Vidé" } },
            verificationInstruction = "Appeler /sante", verificationAttendu = "200",
        };
        var creee = await bob.PostAsJsonAsync("/api/resolutions", Saisie("Cache corrompu", null, api));
        Assert.Equal(HttpStatusCode.Created, creee.StatusCode);
        var r = await Lire(creee);
        var rid = r.GetProperty("id").GetGuid();
        Assert.Equal(1, r.GetProperty("version").GetInt32());
        Assert.True(r.GetProperty("actif").GetBoolean());
        Assert.Equal(["Vider le cache", "Redémarrer"], r.GetProperty("corrections").EnumerateArray().Select(c => c.GetProperty("instruction").GetString()!).ToArray());
        Assert.Equal([1, 2], r.GetProperty("corrections").EnumerateArray().Select(c => c.GetProperty("ordre").GetInt32()).ToArray());

        // Modification avec la bonne version, puis avec la version périmée.
        var modifiee = await Ok(await alice.PutAsJsonAsync($"/api/resolutions/{rid}", Saisie("Cache corrompu v2", 1, api)));
        Assert.Equal(2, modifiee.GetProperty("version").GetInt32());
        Assert.Equal("Cache corrompu v2", modifiee.GetProperty("titre").GetString());
        var perimee = await bob.PutAsJsonAsync($"/api/resolutions/{rid}", Saisie("Autre", 1, api));
        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync($"/api/resolutions/{rid}", Saisie("Autre", null, api))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PutAsJsonAsync($"/api/resolutions/{rid}", Saisie("", 2, api))).StatusCode);

        Assert.Equal([api], modifiee.GetProperty("systemeCatalogueIds").EnumerateArray().Select(x => x.GetGuid()).ToArray()); // doublons ignorés

        // Filtre par système du catalogue : les résolutions dont l'un des systèmes est celui-ci (dont « API + BDD »).
        var pourApi = (await Ok(await bob.GetAsync($"/api/resolutions?systemeCatalogueId={api}"))).EnumerateArray().Select(x => x.GetProperty("titre").GetString()).ToList();
        Assert.Equal(["API + BDD : connexion refusée après bascule", "API : réponse de test absente", "Cache corrompu v2"], pourApi.Select(t => t!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(8, (await Ok(await bob.GetAsync("/api/resolutions"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/resolutions/{Guid.NewGuid()}")).StatusCode);

        // Suppression : admin seulement ; une résolution jamais utilisée disparaît.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/resolutions/{rid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/resolutions/{rid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/resolutions/{rid}")).StatusCode);
    }

    [Fact]
    public async Task Resolution_liee_a_une_etape_est_desactivee_et_les_ids_inconnus_sont_refuses()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var type = await ResolutionDe(alice, mep, "E0005");
        var typeId = type.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/resolutions/{typeId}")).StatusCode);
        Assert.DoesNotContain((await Ok(await alice.GetAsync("/api/resolutions"))).EnumerateArray(), x => x.GetProperty("id").GetGuid() == typeId);
        var desactivee = await Ok(await alice.GetAsync($"/api/resolutions/{typeId}"));
        Assert.False(desactivee.GetProperty("actif").GetBoolean());
        Assert.Equal(2, desactivee.GetProperty("version").GetInt32());

        // Étape : une résolution inconnue est refusée (400), la liaison existante (désactivée) reste admise.
        var e5 = Etape(mep, "E0005");
        object Saisie(int revision, Guid[] resolutions) => new
        {
            revision, action = e5.GetProperty("action").GetString(), systemeIds = e5.GetProperty("systemeIds"), responsableId = e5.GetProperty("responsableId"),
            criticite = "Moderee", instructions = e5.GetProperty("instructions").GetString(), resultatAttendu = e5.GetProperty("resultatAttendu").GetString(),
            dependances = new[] { EtapeId(mep, "E0001") }, resolutionIds = resolutions,
        };
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0005")}";
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0005"), [Guid.NewGuid()]))).StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0005"), [typeId])));
        Assert.Equal(typeId, Etape(mep, "E0005").GetProperty("resolutionIds")[0].GetGuid());
        mep = await Ok(await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0005"), [])));
        Assert.Equal(0, Etape(mep, "E0005").GetProperty("resolutionIds").GetArrayLength());
        // Désormais désactivée et non liée : on ne peut plus la lier.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(url, Saisie(Revision(mep, "E0005"), [typeId]))).StatusCode);
    }
}
