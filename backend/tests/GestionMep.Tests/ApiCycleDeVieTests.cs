using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Routes des sorties du cycle de vie (#93) : annuler, reporter et reprendre, no-go avec report, retour en préparation avec
/// motif, retour en exécution depuis la recette, étape corrective, clôture en échec, passation et pilotage de secours, personne
/// ajoutée après le Go, suppression définitive ; droits (403) et refus métier (422) compris.
/// </summary>
[Collection("Api")]
public class ApiCycleDeVieTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static bool Droit(JsonElement mep, string nom) => mep.GetProperty("droits").GetProperty(nom).GetBoolean();

    private static async Task<List<(string Type, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static async Task<List<(string Type, string Detail)>> JournalAdmin(HttpClient admin) =>
        (await Ok(await admin.GetAsync("/api/admin/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static async Task<JsonElement> Resume(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);

    // ---------- Annuler ----------

    [Fact]
    public async Task Annuler_par_le_pilote_lecture_seule_journal_liste_et_duplication()
    {
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilPrete); // Bob pilote, Alice suppléante
        Assert.True(Droit(await Ok(await bob.GetAsync($"/api/meps/{id}")), "peutAnnuler"));
        Assert.False(Droit(await Ok(await claire.GetAsync($"/api/meps/{id}")), "peutAnnuler"));
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "x" })).StatusCode);
        var sansMotif = await bob.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = " " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);

        var mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "Projet arrêté par la direction" }));
        Assert.Equal("Annulee", mep.GetProperty("phase").GetString());
        Assert.Equal("Annulee", mep.GetProperty("etat").GetString());
        var annulation = mep.GetProperty("annulation");
        Assert.Equal(("Projet arrêté par la direction", "", "Bob"),
            (annulation.GetProperty("motif").GetString(), annulation.GetProperty("etatProduction").GetString(), annulation.GetProperty("par").GetString()));
        Assert.False(Droit(mep, "peutAnnuler"));
        Assert.Contains(("mep.annulee", "MEP annulée : Projet arrêté par la direction"), await Journal(bob, id));

        // Lecture seule : le plan ne se modifie plus.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsJsonAsync($"/api/meps/{id}/etapes", new { })).StatusCode);
        // Liste et recherche : état « Annulée », date d'annulation.
        var resume = await Resume(bob, id);
        Assert.Equal("Annulee", resume.GetProperty("etat").GetString());
        Assert.NotEqual(JsonValueKind.Null, resume.GetProperty("annuleeLe").ValueKind);
        var annulees = (await Ok(await bob.GetAsync("/api/meps?modele=false&etat=annulee"))).EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(id, annulees);
        Assert.DoesNotContain(await AidesApi.IdMep(bob, DonneesDemo.OutilIncomplete), annulees);
        // Duplicable pour la reprogrammer.
        var copie = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/dupliquer", new { commeModele = false }));
        Assert.Equal("Preparation", copie.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, copie.GetProperty("annulation").ValueKind);
    }

    [Fact]
    public async Task Un_administrateur_annule_une_mep_qu_il_ne_pilote_pas()
    {
        var bob = await _api.ClientConnecte("bob");
        var alice = await _api.ClientConnecte("alice");
        var mep = await Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Brouillon de Bob" }));
        var id = mep.GetProperty("id").GetGuid();
        // Bob, créateur sans être pilote, ne peut pas l'annuler ; Alice, administratrice, le peut.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "x" })).StatusCode);
        var vue = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.True(Droit(vue, "peutAnnuler"));
        Assert.True(Droit(vue, "estAdmin"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "Doublon" }));
        Assert.Equal("Alice", mep.GetProperty("annulation").GetProperty("par").GetString());
    }

    [Fact]
    public async Task Apres_un_go_retour_en_preparation_avec_motif_puis_annulation_avec_etat_de_production()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.Go(_api, alice, id);
        var go = mep.GetProperty("premierGo").GetDateTimeOffset();
        var refus = await alice.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "Abandon", etatProduction = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("Revenir d'abord en préparation", await Message(refus));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Preparation" })).StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Preparation", motif = "Base injoignable" }));
        // Base au millionième de seconde : on compare à la milliseconde.
        Assert.Equal(go.ToUnixTimeMilliseconds(), mep.GetProperty("premierGo").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Contains(("phase.preparation", "Retour en préparation : Base injoignable"), await Journal(alice, id));

        var sansEtat = await alice.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "Abandon" });
        Assert.Contains("état laissé en production", await Message(sansEtat));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/annuler", new { motif = "Abandon", etatProduction = "Sauvegardes faites" }));
        Assert.Equal("Sauvegardes faites", mep.GetProperty("annulation").GetProperty("etatProduction").GetString());
        Assert.Contains(("mep.annulee", "MEP annulée : Abandon. État laissé en production : Sauvegardes faites"), await Journal(alice, id));
        // Annulée après un Go : elle ne se supprime pas.
        Assert.False(Droit(mep, "peutSupprimer"));
    }

    // ---------- Reporter ----------

    [Fact]
    public async Task Reporter_reprendre_et_no_go_avec_report_puis_go_qui_leve_le_report()
    {
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilPrete);
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/reporter", new { motif = "x" })).StatusCode);
        var mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/reporter", new { motif = "Gel des changements", dateVisee = "2026-11-05" }));
        Assert.Equal("Preparation", mep.GetProperty("phase").GetString());
        Assert.Equal("Reportee", mep.GetProperty("etat").GetString());
        Assert.Equal("2026-11-05", mep.GetProperty("report").GetProperty("dateVisee").GetString());
        Assert.Equal("2026-11-05", (await Resume(bob, id)).GetProperty("reporteeAu").GetString());
        Assert.Contains(("mep.reportee", "MEP reportée au 05/11/2026 : Gel des changements"), await Journal(bob, id));
        Assert.Contains(id, (await Ok(await bob.GetAsync("/api/meps?modele=false&etat=reportee"))).EnumerateArray().Select(m => m.GetProperty("id").GetGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsync($"/api/meps/{id}/reprendre", null)).StatusCode);
        mep = await Ok(await bob.PostAsync($"/api/meps/{id}/reprendre", null));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("report").ValueKind);
        Assert.Equal("Preparation", mep.GetProperty("etat").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsync($"/api/meps/{id}/reprendre", null)).StatusCode);

        // No-go qui reporte : deux événements, même motif, sans date.
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/no-go", new { motif = "Astreinte absente", reporter = true }));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("report").GetProperty("dateVisee").ValueKind);
        var journal = await Journal(bob, id);
        Assert.Contains(("gonogo.nogo", "No-go : Astreinte absente"), journal);
        Assert.Contains(("mep.reportee", "MEP reportée sans date : Astreinte absente"), journal);

        // Le Go lève le report.
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(bob) }));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("report").ValueKind);
        Assert.Contains(("mep.reprise", "Report levé par le Go"), await Journal(bob, id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsJsonAsync($"/api/meps/{id}/reporter", new { motif = "x" })).StatusCode);
    }

    // ---------- Corriger en avant ----------

    [Fact]
    public async Task Revenir_en_execution_depuis_la_recette_puis_ajouter_une_etape_corrective()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var avant = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var api = avant.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("id").GetGuid();
        var corrective = new
        {
            action = "Préchauffer le cache du service congés", systemeIds = new[] { api }, instructions = "Appeler /v1/conges une fois.",
            resultatAttendu = "Réponse en moins de 1 s.",
        };
        // En recette, l'étape corrective attend le retour en exécution.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes-correctives", corrective)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution" })).StatusCode);

        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", motif = "Cache froid" }));
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        var tests = mep.GetProperty("fichesRecette").EnumerateArray().SelectMany(f => f.GetProperty("etapes").EnumerateArray()).ToList();
        Assert.DoesNotContain(tests, t => t.GetProperty("resultat").GetString() == "Echec");
        Assert.Contains(await Journal(alice, id), e => e.Type == "phase.execution" && e.Detail.StartsWith("Retour en exécution pour corriger : Cache froid. Tests à refaire : "));

        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/etapes-correctives", corrective)).StatusCode);
        var incomplete = await alice.PostAsJsonAsync($"/api/meps/{id}/etapes-correctives", new { action = "x", systemeIds = new[] { api } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes-correctives", corrective));
        var ajoutee = mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("ajouteeApresGo").GetBoolean());
        var code = ajoutee.GetProperty("code").GetString()!;
        Assert.Equal("AFaire", ajoutee.GetProperty("etat").GetString());
        Assert.Contains(($"etape.creee", $"{code} ajoutée après le Go (étape corrective) : Préchauffer le cache du service congés (retour en exécution : Cache froid)"),
            await Journal(alice, id));
    }

    // ---------- Clôturer en échec ----------

    [Fact]
    public async Task Cloture_en_echec_refusee_en_recette_si_le_retour_arriere_est_possible()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var rep = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new
        {
            echec = true, etatService = "Dégradé", ceQuiAMarche = "a", incidents = "b",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Contains("retour arrière est encore possible", await Message(rep));
    }

    [Fact]
    public async Task Cloture_en_echec_depuis_le_retour_arriere_etat_du_service_bilan_liste_et_rapport()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var go = await AidesApi.Go(_api, alice, id);
        // Une étape corrective avant le retour arrière : le rapport de fin la compte.
        var api = go.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("id").GetGuid();
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes-correctives", new
        {
            action = "Relancer le service", systemeIds = new[] { api }, instructions = "systemctl restart api", resultatAttendu = "Service actif.",
        }));
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Base corrompue" }));

        var sansEtat = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { echec = true, ceQuiAMarche = "a", incidents = "b" });
        Assert.Contains("état du service", await Message(sansEtat));
        var sansBilan = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { echec = true, etatService = "Dégradé" });
        Assert.Contains("Bilan obligatoire", await Message(sansBilan));
        // Sans échec, le service n'est pas rétabli : clôture refusée.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "a", incidents = "b" })).StatusCode);

        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new
        {
            echec = true, etatService = "Lecture seule", referenceIncident = "INC-42", ceQuiAMarche = "Sauvegardes", incidents = "Restauration impossible", actions = "",
        }));
        Assert.Equal(("Cloturee", "Echec", "Echec"), (mep.GetProperty("phase").GetString(), mep.GetProperty("issue").GetString(), mep.GetProperty("etat").GetString()));
        var cloture = mep.GetProperty("cloture");
        Assert.Equal(("Lecture seule", "INC-42"), (cloture.GetProperty("etatService").GetString(), cloture.GetProperty("referenceIncident").GetString()));
        Assert.Contains(("mep.cloturee", "MEP clôturée : échec (service dégradé). État du service : Lecture seule (incident INC-42)"), await Journal(alice, id));
        Assert.Equal("Echec", (await Resume(alice, id)).GetProperty("etat").GetString());
        Assert.Contains(id, (await Ok(await alice.GetAsync("/api/meps?modele=false&etat=echec"))).EnumerateArray().Select(m => m.GetProperty("id").GetGuid()));
        var rapport = await Ok(await alice.GetAsync($"/api/meps/{id}/rapport"));
        Assert.Equal("Lecture seule", rapport.GetProperty("mep").GetProperty("cloture").GetProperty("etatService").GetString());
        Assert.Equal(1, rapport.GetProperty("synthese").GetProperty("etapesAjouteesApresGo").GetInt32());
        Assert.Equal(0, rapport.GetProperty("synthese").GetProperty("retoursEnPreparation").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/meps/{id}/rapport/excel")).StatusCode);
    }

    // ---------- Pilotage ----------

    [Fact]
    public async Task Passer_le_pilotage_puis_pilotage_de_secours_par_un_administrateur()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilExecution); // Alice pilote, Bob suppléant
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Guid pAlice = Participant(mep, "Alice"), pBob = Participant(mep, "Bob"), pClaire = Participant(mep, "Claire");
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { participantId = pClaire, motif = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { motif = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bob.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { participantId = pClaire })).StatusCode);

        // Passation par Bob (suppléant) à Claire ; Alice et Bob sont retirés des suppléants.
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/pilotage", new
        {
            participantId = pClaire, motif = "Relève de nuit", ancienPiloteSuppleant = false, suppleantsGardes = Array.Empty<Guid>(),
        }));
        Assert.Equal(pClaire, mep.GetProperty("piloteId").GetGuid());
        Assert.Empty(mep.GetProperty("suppleantIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("pilotageSecours").ValueKind);
        Assert.Contains(("pilotage.passe", "Pilotage passé : Claire remplace Alice comme pilote. Motif : Relève de nuit."), await Journal(alice, id));

        // Alice, administratrice, n'est plus ni pilote ni suppléante : elle reprend le pilotage en secours.
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.False(Droit(mep, "estPilote"));
        Assert.True(Droit(mep, "peutChangerPilote"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { participantId = pAlice, motif = "Claire injoignable" }));
        Assert.Equal(pAlice, mep.GetProperty("piloteId").GetGuid());
        Assert.Equal([pClaire], mep.GetProperty("suppleantIds").EnumerateArray().Select(s => s.GetGuid()));
        var secours = mep.GetProperty("pilotageSecours");
        Assert.Equal(("Alice", "Claire injoignable", "Claire", "Alice"), (secours.GetProperty("par").GetString(), secours.GetProperty("motif").GetString(),
            secours.GetProperty("ancienPilote").GetString(), secours.GetProperty("nouveauPilote").GetString()));
        Assert.Contains(("pilotage.secours", "Pilotage de secours par Alice : Alice remplace Claire comme pilote. Motif : Claire injoignable. Suppléant(s) : Claire."),
            await Journal(alice, id));
        Assert.Contains(await JournalAdmin(alice), e => e.Type == "mep.pilotage-secours"
            && e.Detail == "MEP « Paiement mobile 4.2 » : pilotage repris à la place de Claire. Motif : Claire injoignable");
        _ = pBob;
    }

    [Fact]
    public async Task Le_pilotage_se_confie_a_un_compte_hors_mep_et_se_refuse_une_fois_la_mep_terminee()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilExecution);
        // Un compte qui ne fait pas partie de la MEP y est ajouté.
        var dave = (await Ok(await alice.PostAsJsonAsync("/api/admin/utilisateurs", new { login = "dave", nomAffiche = "Dave" }))).GetProperty("utilisateur");
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { utilisateurId = dave.GetProperty("id").GetGuid(), motif = "Astreinte" }));
        Assert.Equal(Participant(mep, "Dave"), mep.GetProperty("piloteId").GetGuid());
        var journal = await Journal(alice, id);
        Assert.Contains(("participant.ajoute", "Participant ajouté après le Go : Dave"), journal);
        Assert.Contains(journal, e => e.Type == "pilotage.passe" && e.Detail.StartsWith("Pilotage passé : Dave remplace Alice comme pilote."));

        var close = await AidesApi.IdMep(alice, DonneesDemo.OutilReussie);
        var rep = await alice.PostAsJsonAsync($"/api/meps/{close}/pilotage", new { participantId = Participant(await Ok(await alice.GetAsync($"/api/meps/{close}")), "Bob"), motif = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.False(Droit(await Ok(await bob.GetAsync($"/api/meps/{close}")), "peutChangerPilote"));
    }

    [Fact]
    public async Task Une_personne_s_ajoute_apres_le_go_par_le_pilote_ou_un_administrateur()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilExecution);
        Assert.False(Droit(await Ok(await claire.GetAsync($"/api/meps/{id}")), "peutAjouterPersonne"));
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/participants", new { nom = "Astreinte", role = "DBA" })).StatusCode);
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/participants", new { nom = "Astreinte BDD", role = "DBA", contact = "poste 99" }));
        Assert.Contains("Astreinte BDD", mep.GetProperty("participants").EnumerateArray().Select(p => p.GetProperty("nom").GetString()));
        Assert.Contains(("participant.ajoute", "Participant ajouté après le Go : Astreinte BDD"), await Journal(alice, id));
    }

    // ---------- Supprimer ----------

    [Fact]
    public async Task Suppression_definitive_administrateur_seulement_nom_saisi_et_journal_d_administration()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilIncomplete);
        var nom = $"{DonneesDemo.OutilIncomplete} 3.5";
        Assert.False(Droit(await Ok(await bob.GetAsync($"/api/meps/{id}")), "peutSupprimer"));
        Assert.True(Droit(await Ok(await alice.GetAsync($"/api/meps/{id}")), "peutSupprimer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await AidesApi.Supprimer(bob, id, nom)).StatusCode);
        var mauvaisNom = await AidesApi.Supprimer(alice, id, DonneesDemo.OutilIncomplete);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mauvaisNom.StatusCode);
        Assert.Contains(nom, await Message(mauvaisNom));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AidesApi.Supprimer(alice, id, nom, motif: " ")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await AidesApi.Supprimer(alice, id, nom, "Remplacée par la MEP 3.6")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{id}")).StatusCode);
        Assert.Contains(await JournalAdmin(alice), e => e.Type == "mep.supprimee"
            && e.Detail.StartsWith($"MEP « {nom} » supprimée (jamais lancée, créée par Alice le ")
            && e.Detail.EndsWith(", 5 étapes, 0 pièce jointe). Motif : Remplacée par la MEP 3.6"));
    }

    [Fact]
    public async Task Une_mep_qui_a_eu_un_go_ne_se_supprime_pas()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilAnnulee); // annulée après un Go (démonstration)
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Annulee", mep.GetProperty("phase").GetString());
        Assert.False(Droit(mep, "peutSupprimer"));
        var rep = await AidesApi.Supprimer(alice, id, $"{DonneesDemo.OutilAnnulee} 2.2");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Contains("déjà eu un Go", await Message(rep));
    }
}
