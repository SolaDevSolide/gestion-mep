using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Bibliothèque d'étapes modèles et compteurs d'utilisations des bibliothèques, par l'API.</summary>
[Collection("Api")]
public class ApiBibliothequesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<JsonElement> Corps(HttpResponseMessage rep) => JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.Clone();
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static int Utilisations(JsonElement e) => e.GetProperty("utilisations").GetInt32();
    private static JsonElement Etape(JsonElement mep, string code) => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private static async Task<Func<string, Guid>> Catalogue(HttpClient c)
    {
        var catalogue = (await Ok(await c.GetAsync("/api/catalogue/systemes"))).EnumerateArray().ToList();
        return nom => catalogue.Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    }

    private static object Saisie(int? version, string action, Guid[] systemes, string type = "Standard", Guid[]? resolutions = null) => new
    {
        version, action, systemeCatalogueIds = systemes, type, criticite = "Elevee", dureeEstimeeMinutes = 10,
        instructions = "```\nvacuumdb --all\n```", resultatAttendu = "Fin sans erreur", fin = "Verification",
        sauvegardeEmplacement = "/sauvegardes", sauvegardeMethodeRecuperation = "pg_restore", resolutionIds = resolutions ?? [],
    };

    private static async Task<JsonElement> Modele(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/etapes-modeles/{id}"));

    [Fact]
    public async Task Etapes_modeles_triees_par_utilisations_creees_par_tous_supprimees_par_un_admin()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var cat = await Catalogue(bob);

        // Démonstration : triées par utilisations décroissantes, puis par ordre de création ; filtre « contient » par système.
        var liste = (await Ok(await bob.GetAsync("/api/etapes-modeles"))).EnumerateArray().ToList();
        Assert.True(liste.Count >= 20);
        var cles = liste.Select(m => (Utilisations(m), m.GetProperty("creeLe").GetDateTimeOffset())).ToList();
        Assert.Equal(cles.OrderByDescending(c => c.Item1).ThenBy(c => c.Item2), cles);
        var bdd = (await Ok(await bob.GetAsync($"/api/etapes-modeles?systemeCatalogueId={cat("BDD")}"))).EnumerateArray().ToList();
        Assert.All(bdd, m => Assert.Contains(cat("BDD"), m.GetProperty("systemeCatalogueIds").EnumerateArray().Select(x => x.GetGuid())));
        Assert.Contains(bdd, m => m.GetProperty("systemeCatalogueIds").GetArrayLength() > 1);

        // Bob (non administrateur) crée : sans système, refusé avec la liste des anomalies ; système inconnu : 400.
        var sansSysteme = await bob.PostAsJsonAsync("/api/etapes-modeles", Saisie(null, "", []));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansSysteme.StatusCode);
        Assert.Equal(2, (await Corps(sansSysteme)).GetProperty("erreurs").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync("/api/etapes-modeles", Saisie(null, "x", [Guid.NewGuid()]))).StatusCode);
        var cree = await bob.PostAsJsonAsync("/api/etapes-modeles", Saisie(null, " Vacuum de la base ", [cat("BDD"), cat("BDD")]));
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var m = await Corps(cree);
        Assert.Equal(("Vacuum de la base", 1, 0, "Bob"), (m.GetProperty("action").GetString(), m.GetProperty("version").GetInt32(), Utilisations(m), m.GetProperty("modifiePar").GetString()));
        Assert.Equal([cat("BDD")], m.GetProperty("systemeCatalogueIds").EnumerateArray().Select(x => x.GetGuid()));
        // Étape normale : les champs de sauvegarde sont effacés.
        Assert.Equal("", m.GetProperty("sauvegardeEmplacement").GetString());

        // Modification : version obligatoire, conflit si périmée.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/etapes-modeles/{Id(m)}", Saisie(null, "x", [cat("BDD")]))).StatusCode);
        m = await Ok(await alice.PutAsJsonAsync($"/api/etapes-modeles/{Id(m)}", Saisie(1, "Vacuum complet", [cat("BDD"), cat("API")], "Sauvegarde")));
        Assert.Equal((2, "Sauvegarde", "/sauvegardes", "Alice"),
            (m.GetProperty("version").GetInt32(), m.GetProperty("type").GetString(), m.GetProperty("sauvegardeEmplacement").GetString(), m.GetProperty("modifiePar").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PutAsJsonAsync($"/api/etapes-modeles/{Id(m)}", Saisie(1, "x", [cat("BDD")]))).StatusCode);

        // Un système du catalogue utilisé par une étape modèle est seulement désactivé.
        var ldap = await Ok(await alice.PostAsJsonAsync("/api/catalogue/systemes", new { nom = "LDAP2", description = "", cible = "" }));
        await Ok(await bob.PutAsJsonAsync($"/api/etapes-modeles/{Id(m)}", Saisie(2, "Vacuum complet", [cat("BDD"), Id(ldap)])));
        await alice.DeleteAsync($"/api/catalogue/systemes/{Id(ldap)}");
        Assert.False((await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => Id(s) == Id(ldap)).GetProperty("actif").GetBoolean());

        // Suppression réservée aux administrateurs, notée au journal d'administration.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/etapes-modeles/{Id(m)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/etapes-modeles/{Id(m)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/etapes-modeles/{Id(m)}")).StatusCode);
        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "etape-modele.supprimee");
    }

    [Fact]
    public async Task Transformer_une_etape_en_etape_modele()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(bob);
        var mep = await Ok(await bob.GetAsync($"/api/meps/{id}"));
        var pnr = mep.GetProperty("etapes").EnumerateArray().First(e => e.GetProperty("pointDeNonRetour").GetBoolean());

        var m = await Ok(await bob.PostAsJsonAsync("/api/etapes-modeles/depuis-etape", new { mepId = id, etapeId = Id(pnr), action = " Bascule type " }));
        Assert.Equal(("Bascule type", 0), (m.GetProperty("action").GetString(), Utilisations(m)));
        Assert.Equal(pnr.GetProperty("instructions").GetString(), m.GetProperty("instructions").GetString());
        Assert.False(m.TryGetProperty("pointDeNonRetour", out _));
        Assert.NotEqual(0, m.GetProperty("systemeCatalogueIds").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync("/api/etapes-modeles/depuis-etape", new { mepId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.PostAsJsonAsync("/api/etapes-modeles/depuis-etape", new { mepId = id, etapeId = Guid.NewGuid(), action = "x" })).StatusCode);
    }

    [Fact]
    public async Task Inserer_une_copie_compte_une_utilisation_par_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var cat = await Catalogue(alice);
        var demo = await AidesApi.IdDemo(alice);
        var prete = await AidesApi.IdMep(alice, DonneesDemo.OutilPrete);

        // Résolution neuve liée au modèle, et un système absent de la MEP de démonstration (BATCH).
        var res = await Corps(await alice.PostAsJsonAsync("/api/resolutions", new
        {
            titre = "Vacuum interrompu", symptomes = "Verrou", diagnostic = "pg_locks", systemeCatalogueIds = new[] { cat("BDD") },
            corrections = new[] { new { ordre = 1, instruction = "Relancer", resultatAttendu = "OK" } }, verificationInstruction = "v", verificationAttendu = "a",
        }));
        Assert.Equal(0, Utilisations(res));
        var m = await Corps(await alice.PostAsJsonAsync("/api/etapes-modeles", Saisie(null, "Vacuum de la base", [cat("BDD"), cat("BATCH")], resolutions: [Id(res)])));

        // Ligne inexistante : 400 ; sauvegarde dans le retour arrière : 422.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{demo}/etapes/depuis-modele", new { modeleId = Id(m), ligne = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{demo}/etapes/depuis-modele", new { })).StatusCode);
        var avant = await Ok(await alice.GetAsync($"/api/meps/{demo}"));
        var bddMep = avant.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "BDD");

        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{demo}/etapes/depuis-modele", new { modeleId = Id(m), ligne = 0, position = 0 }));
        var copie = mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("modeleEtapeId").ValueKind == JsonValueKind.String
            && e.GetProperty("modeleEtapeId").GetGuid() == Id(m));
        Assert.Equal((0, 0, false), (copie.GetProperty("ligne").GetInt32(), copie.GetProperty("ordre").GetInt32(), copie.GetProperty("pointDeNonRetour").GetBoolean()));
        Assert.Equal([Id(bddMep)], copie.GetProperty("systemeIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.Equal([Id(res)], copie.GetProperty("resolutionIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.Equal(1, Utilisations(await Modele(alice, Id(m))));
        Assert.Equal(1, Utilisations(await Ok(await alice.GetAsync($"/api/resolutions/{Id(res)}"))));

        // Deuxième copie dans la même MEP, posée entre deux lignes : elle ouvre une ligne à elle ; pas de nouvelle utilisation.
        var lignesAvant = mep.GetProperty("etapes").EnumerateArray().Where(e => e.GetProperty("plan").GetString() == "Deploiement")
            .Select(e => e.GetProperty("ligne").GetInt32()).Distinct().Count();
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{demo}/etapes/depuis-modele", new { modeleId = Id(m), ligne = 1, nouvelleLigne = true }));
        var deploiement = mep.GetProperty("etapes").EnumerateArray().Where(e => e.GetProperty("plan").GetString() == "Deploiement").ToList();
        var seconde = deploiement.Single(e => e.GetProperty("modeleEtapeId").ValueKind == JsonValueKind.String
            && e.GetProperty("modeleEtapeId").GetGuid() == Id(m) && e.GetProperty("ligne").GetInt32() != 0);
        Assert.Equal((1, 0), (seconde.GetProperty("ligne").GetInt32(), seconde.GetProperty("ordre").GetInt32()));
        Assert.Single(deploiement, e => e.GetProperty("ligne").GetInt32() == 1);
        Assert.Equal(lignesAvant + 1, deploiement.Select(e => e.GetProperty("ligne").GetInt32()).Distinct().Count());
        Assert.Equal(1, Utilisations(await Modele(alice, Id(m))));
        // Dans une autre MEP : une utilisation de plus.
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{prete}/etapes/depuis-modele", new { modeleId = Id(m) }));
        Assert.Equal(2, Utilisations(await Modele(alice, Id(m))));
        Assert.Equal(2, Utilisations(await Ok(await alice.GetAsync($"/api/resolutions/{Id(res)}"))));

        // Le modèle est proposé pour chacun de ses systèmes, même absent de la MEP.
        var liste = (await Ok(await alice.GetAsync($"/api/etapes-modeles?systemeCatalogueId={cat("BATCH")}"))).EnumerateArray().ToList();
        Assert.Contains(liste, x => Id(x) == Id(m));

        // Retour arrière : une étape modèle de sauvegarde est refusée.
        var sauvegarde = await Corps(await alice.PostAsJsonAsync("/api/etapes-modeles", Saisie(null, "Copie des fichiers", [cat("API")], "Sauvegarde")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await alice.PostAsJsonAsync($"/api/meps/{demo}/etapes/depuis-modele", new { modeleId = Id(sauvegarde), plan = "RetourArriere" })).StatusCode);

        // Une résolution liée seulement à une étape modèle n'est pas supprimée : elle est désactivée.
        var seule = await Corps(await alice.PostAsJsonAsync("/api/resolutions", new
        {
            titre = "Résolution du modèle", symptomes = "s", diagnostic = "d", systemeCatalogueIds = Array.Empty<Guid>(),
            corrections = new[] { new { ordre = 1, instruction = "i", resultatAttendu = "r" } }, verificationInstruction = "v", verificationAttendu = "a",
        }));
        await Ok(await alice.PutAsJsonAsync($"/api/etapes-modeles/{Id(sauvegarde)}", Saisie(1, "Copie des fichiers", [cat("API")], "Sauvegarde", [Id(seule)])));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/resolutions/{Id(seule)}")).StatusCode);
        Assert.False((await Ok(await alice.GetAsync($"/api/resolutions/{Id(seule)}"))).GetProperty("actif").GetBoolean());
    }

    [Fact]
    public async Task Inserer_un_modele_de_recette_compte_une_utilisation()
    {
        var alice = await _api.ClientConnecte("alice");
        var cat = await Catalogue(alice);
        var id = await AidesApi.IdDemo(alice);
        var modeles = (await Ok(await alice.GetAsync($"/api/modeles-recette?systemeCatalogueId={cat("WEB")}"))).EnumerateArray().ToList();
        // Triés par utilisations.
        Assert.Equal(modeles.Select(Utilisations).OrderByDescending(n => n), modeles.Select(Utilisations));
        var modele = modeles.Single(m => m.GetProperty("titre").GetString() == "Affichage sur mobile");
        var avant = Utilisations(modele);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var web = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "WEB");

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/depuis-modele", new { modeleId = Id(modele), systemeId = Id(web) }));
        Assert.Contains(mep.GetProperty("fichesRecette").EnumerateArray(), f => f.GetProperty("titre").GetString() == "Affichage sur mobile");
        Assert.Equal(avant + 1, Utilisations(await Ok(await alice.GetAsync($"/api/modeles-recette/{Id(modele)}"))));
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/depuis-modele", new { modeleId = Id(modele), systemeId = Id(web) }));
        Assert.Equal(avant + 1, Utilisations(await Ok(await alice.GetAsync($"/api/modeles-recette/{Id(modele)}"))));
    }
}
