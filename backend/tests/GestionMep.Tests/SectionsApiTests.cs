using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Services;
using Microsoft.Extensions.Configuration;

namespace GestionMep.Tests;

/// <summary>Verrou d'édition d'une étape dans le diffuseur (sans HTTP) : prise, renouvellement, expiration, prise de main, relâche.</summary>
public class VerrousEtapesTests
{
    private sealed class Horloge(DateTimeOffset depart) : TimeProvider
    {
        public DateTimeOffset Maintenant { get; set; } = depart;
        public override DateTimeOffset GetUtcNow() => Maintenant;
    }

    [Fact]
    public async Task Le_verrou_se_prend_se_renouvelle_expire_et_se_reprend()
    {
        var horloge = new Horloge(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        var diffuseur = new DiffuseurMep(new ConfigurationBuilder().Build(), horloge);
        Guid mep = Guid.NewGuid(), etape = Guid.NewGuid(), alice = Guid.NewGuid(), bob = Guid.NewGuid();
        using var fluxBob = diffuseur.Abonner(mep, bob, "Bob", "onglet-bob")!;
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await fluxBob.AttendreAsync(delai.Token));
        fluxBob.Prendre();

        // Alice ouvre le panneau : verrou pris, diffusé.
        var (pris, verrou) = diffuseur.PrendreVerrou(mep, etape, alice, "Alice", "onglet-alice", false);
        Assert.True(pris);
        Assert.Equal(("Alice", horloge.Maintenant + DiffuseurMep.DureeVerrou), (verrou.Nom, verrou.ExpireLe));
        Assert.True(await fluxBob.AttendreAsync(delai.Token));
        Assert.Equal(new DiffuseurMep.Envoi(null, false, true), fluxBob.Prendre());

        // Bob ouvre la même étape : « Alice est dessus ».
        (pris, verrou) = diffuseur.PrendreVerrou(mep, etape, bob, "Bob", "onglet-bob", false);
        Assert.False(pris);
        Assert.Equal(alice, verrou.UtilisateurId);
        // Alice renouvelle : l'échéance recule, sans diffusion.
        horloge.Maintenant += TimeSpan.FromSeconds(50);
        (pris, verrou) = diffuseur.PrendreVerrou(mep, etape, alice, "Alice", "onglet-alice", false);
        Assert.True(pris);
        Assert.Equal(horloge.Maintenant + DiffuseurMep.DureeVerrou, verrou.ExpireLe);
        Assert.Equal(new DiffuseurMep.Envoi(null, false, false), fluxBob.Prendre());
        // Le même compte dans un autre onglet n'a pas le verrou.
        Assert.False(diffuseur.PrendreVerrou(mep, etape, alice, "Alice", "autre-onglet", false).Pris);

        // Bob prend la main, sans confirmation : le verrou garde qui l'avait.
        (pris, verrou) = diffuseur.PrendreVerrou(mep, etape, bob, "Bob", "onglet-bob", true);
        Assert.True(pris);
        Assert.Equal((bob, alice, "Alice", "onglet-alice"), (verrou.UtilisateurId, verrou.ReprisDeUtilisateurId, verrou.ReprisDeNom, verrou.ReprisDeSession));
        // Alice ne peut plus le relâcher ; Bob, si.
        Assert.False(diffuseur.RelacherVerrou(mep, etape, alice, "onglet-alice"));
        Assert.True(diffuseur.RelacherVerrou(mep, etape, bob, "onglet-bob"));
        Assert.Empty(diffuseur.Verrous(mep));

        // Sans signe de vie, il expire : n'importe qui le reprend, et l'expiration est diffusée.
        diffuseur.PrendreVerrou(mep, etape, alice, "Alice", "onglet-alice", false);
        fluxBob.Prendre();
        horloge.Maintenant += DiffuseurMep.DureeVerrou;
        Assert.Empty(diffuseur.Verrous(mep));
        Assert.Equal(new DiffuseurMep.Envoi(null, false, true), fluxBob.Prendre());
        (pris, verrou) = diffuseur.PrendreVerrou(mep, etape, bob, "Bob", "onglet-bob", false);
        Assert.True(pris);
        Assert.Null(verrou.ReprisDeUtilisateurId);

        // Fermer le flux de son onglet relâche ses verrous.
        fluxBob.Dispose();
        Assert.Empty(diffuseur.Verrous(mep));
    }
}

/// <summary>Sections, cercles, liens par les cercles et verrous d'édition par l'API (journal, droits, temps réel).</summary>
[Collection("Api")]
public class SectionsApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();
    private static string[] Prealables(JsonElement mep, string code) =>
        Etape(mep, code).GetProperty("dependances").EnumerateArray()
            .Select(d => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == d.GetGuid()).GetProperty("code").GetString()!)
            .Order().ToArray();
    private static JsonElement Section(JsonElement mep, Guid sid) => mep.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == sid);
    private static Guid Port(JsonElement section, string sens, int rang = 0) =>
        section.GetProperty("ports").EnumerateArray().Where(p => p.GetProperty("sens").GetString() == sens).ElementAt(rang).GetProperty("id").GetGuid();
    private static List<string> Liste(JsonElement mep, string champ) => mep.GetProperty(champ).EnumerateArray().Select(e => e.GetString()!).ToList();
    private static async Task<List<(string Type, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    [Fact]
    public async Task Une_section_se_cree_dans_le_bac_se_relie_par_ses_cercles_se_place_puis_se_supprime()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var api = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "API").GetProperty("id").GetGuid();
        Assert.Empty(mep.GetProperty("sections").EnumerateArray());
        Assert.All(mep.GetProperty("etapes").EnumerateArray(), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("sectionId").ValueKind));

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "X", systemeIds = new[] { Guid.NewGuid() } })).StatusCode);
        var cree = await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "Service commandes", description = "Déploiement de l'API", systemeIds = new[] { api } });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var corps = await Ok(cree);
        var sid = corps.GetProperty("sectionId").GetGuid();
        mep = corps.GetProperty("mep");
        var section = Section(mep, sid);
        Assert.False(section.GetProperty("placee").GetBoolean()); // dans le bac par défaut
        Assert.Equal(("Deploiement", 0), (section.GetProperty("plan").GetString(), section.GetProperty("revision").GetInt32()));
        Assert.Equal(["Début", "Fin"], section.GetProperty("ports").EnumerateArray().Select(p => p.GetProperty("titre").GetString()));
        Assert.Equal(JsonValueKind.String, section.GetProperty("auteur").ValueKind);
        Assert.Contains("Section « Service commandes » dans le bac : la placer dans le plan ou la supprimer.", Liste(mep, "erreursPlan"));

        // Création rapide dans la section : reliée d'office à Début et à Fin ; Début attend E0003, E0007 attend Fin.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { sectionId = sid, action = "Déployer l'API" }));
        var e10 = Etape(mep, "E0010");
        Assert.Equal(sid, e10.GetProperty("sectionId").GetGuid());
        var (debut, fin) = (Port(Section(mep, sid), "Entree"), Port(Section(mep, sid), "Sortie"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = EtapeId(mep, "E0003"), vers = debut }));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = fin, vers = EtapeId(mep, "E0007") }));
        Assert.Equal(["E0003"], Prealables(mep, "E0010"));
        Assert.Equal(["E0003"], Etape(mep, "E0010").GetProperty("dependancesDerivees").EnumerateArray()
            .Select(d => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == d.GetGuid()).GetProperty("code").GetString()));
        Assert.Contains("E0010", Prealables(mep, "E0007"));
        Assert.Equal([fin], Etape(mep, "E0007").GetProperty("portsAttendus").EnumerateArray().Select(p => p.GetGuid()));
        Assert.Equal([EtapeId(mep, "E0003")], Section(mep, sid).GetProperty("ports")[0].GetProperty("sources").EnumerateArray().Select(x => x.GetGuid()));
        var cycle = await alice.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = EtapeId(mep, "E0008"), vers = debut });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycle.StatusCode);
        Assert.StartsWith("Opération refusée : elle créerait un cycle", await Message(cycle));

        // Fiche de la section : révision exigée, journal du renommage.
        var revision = Section(mep, sid).GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}", new { titre = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}", new { revision = revision + 5, titre = "X" })).StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}", new { revision, titre = "API commandes", description = "", systemeIds = Array.Empty<Guid>() }));
        Assert.Equal(("API commandes", revision + 1), (Section(mep, sid).GetProperty("titre").GetString(), Section(mep, sid).GetProperty("revision").GetInt32()));

        // Cercles : ajout, renommage, ordre, suppression.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/sections/{sid}/ports", new { sens = "Sortie", titre = "Sauvegarde" }));
        var sauvegarde = Port(Section(mep, sid), "Sortie", 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/sections/{sid}/ports", new { titre = "Sans sens" })).StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}/ports/{debut}", new { titre = "Validation", description = "Base prête" }));
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}/ports/ordre", new { sens = "Sortie", ports = new[] { sauvegarde, fin } }));
        Assert.Equal(sauvegarde, Port(Section(mep, sid), "Sortie"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}/ports/ordre", new { sens = "Sortie", ports = new[] { fin } })).StatusCode);
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/sections/{sid}/ports/{sauvegarde}"));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/meps/{id}/sections/{sid}/ports/{sauvegarde}")).StatusCode);

        // Placement depuis le bac : le Go n'est plus bloqué par la section.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sections/{sid}/placement", new { placee = true }));
        Assert.True(Section(mep, sid).GetProperty("placee").GetBoolean());
        Assert.DoesNotContain(Liste(mep, "erreursPlan"), e => e.Contains("dans le bac", StringComparison.Ordinal));

        // Déplacer une carte hors de la section.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0010")}/deplacer", new { sortirDeSection = true }));
        Assert.Equal(JsonValueKind.Null, Etape(mep, "E0010").GetProperty("sectionId").ValueKind);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0010")}/deplacer", new { sectionId = sid }));
        Assert.Equal(["E0003"], Prealables(mep, "E0010"));

        // Suppression : l'étape reste, avec ses préalables effectifs devenus directs.
        var avant = mep.GetProperty("etapes").EnumerateArray().ToDictionary(e => e.GetProperty("code").GetString()!, e => Prealables(mep, e.GetProperty("code").GetString()!));
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/sections/{sid}"));
        Assert.Empty(mep.GetProperty("sections").EnumerateArray());
        foreach (var (code, prealables) in avant) Assert.Equal(prealables, Prealables(mep, code));
        Assert.Empty(Etape(mep, "E0010").GetProperty("dependancesDerivees").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/meps/{id}/sections/{sid}")).StatusCode);

        var journal = await Journal(alice, id);
        foreach (var attendu in new[]
                 {
                     ("section.creee", "Section « Service commandes » créée, dans le bac"),
                     ("etape.creee", "E0010 créée dans la section « Service commandes »"),
                     ("plan.lien-ajoute", "Lien ajouté : le cercle « Début » de la section « Service commandes » attend E0003"),
                     ("section.renommee", "Section « Service commandes » renommée « API commandes »"),
                     ("section.modifiee", "Section « API commandes » modifiée : description, systèmes"),
                     ("section.cercle-ajoute", "Cercle de sortie « Sauvegarde » ajouté à la section « API commandes »"),
                     ("section.cercle-modifie", "Cercle « Début » de la section « API commandes » renommé « Validation »"),
                     ("section.cercle-supprime", "Cercle de sortie « Sauvegarde » supprimé de la section « API commandes »"),
                     ("section.placee", "Section « API commandes » placée dans le plan depuis le bac"),
                     ("etape.deplacee", "E0010 sortie de la section « API commandes »"),
                     ("section.supprimee", "Section « API commandes » supprimée : ses 1 étape(s) restent dans le plan, avec leurs liens"),
                 })
            Assert.Contains(attendu, journal);
    }

    [Fact]
    public async Task Les_sections_suivent_les_droits_la_phase_la_duplication_et_les_modeles_de_texte()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var dave = await _api.NouveauCompte("dave", "Dave");
        Assert.Equal(HttpStatusCode.Forbidden, (await dave.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dave.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = Guid.NewGuid(), vers = Guid.NewGuid() })).StatusCode);

        var corps = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "Base", placee = true }));
        var sid = corps.GetProperty("sectionId").GetGuid();
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { sectionId = sid }));
        Assert.Equal(HttpStatusCode.Forbidden, (await dave.DeleteAsync($"/api/meps/{id}/sections/{sid}")).StatusCode);

        // {{sections}} dans les modèles de texte.
        var variables = await Ok(await alice.GetAsync($"/api/meps/{id}/variables-texte"));
        var sections = variables.GetProperty("valeurs").EnumerateArray().Single(v => v.GetProperty("cle").GetString() == "sections");
        Assert.Equal(["Base"], sections.GetProperty("liste").EnumerateArray().Select(x => x.GetString()));

        // Duplication : la section et son étape suivent, avec de nouveaux identifiants.
        var copie = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/dupliquer", new { commeModele = false }));
        var section = copie.GetProperty("sections").EnumerateArray().Single();
        Assert.Equal("Base", section.GetProperty("titre").GetString());
        Assert.NotEqual(sid, section.GetProperty("id").GetGuid());
        Assert.Equal(section.GetProperty("id").GetGuid(), Etape(copie, "E0010").GetProperty("sectionId").GetGuid());

        // Après le Go : plus rien ne change.
        await Ok(await alice.DeleteAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0010")}"));
        await AidesApi.Go(_api, alice, id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "Trop tard" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.DeleteAsync($"/api/meps/{id}/sections/{sid}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0001")}/verrou", new { session = "s" })).StatusCode);
    }

    private static async Task<string> ProchainEvenement(StreamReader lecteur, string type, CancellationToken ct)
    {
        while (true)
        {
            var ligne = await lecteur.ReadLineAsync(ct) ?? throw new InvalidOperationException("Flux fermé.");
            if (ligne != $"event: {type}") continue;
            return (await lecteur.ReadLineAsync(ct))!["data: ".Length..];
        }
    }

    [Fact]
    public async Task Le_verrou_d_une_etape_est_diffuse_et_se_prend_en_main()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}/verrou";
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var flux = await alice.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/meps/{id}/flux?session=onglet-alice"), HttpCompletionOption.ResponseHeadersRead);
        using var _ = flux;
        using var lecteur = new StreamReader(await flux.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(url, new { session = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{Guid.NewGuid()}/verrou", new { session = "s" })).StatusCode);
        var prise = await Ok(await alice.PostAsJsonAsync(url, new { session = "onglet-alice" }));
        Assert.True(prise.GetProperty("pris").GetBoolean());
        var diffuse = JsonDocument.Parse(await ProchainEvenement(lecteur, "verrous", delai.Token)).RootElement;
        Assert.Equal(EtapeId(mep, "E0004"), diffuse[0].GetProperty("etapeId").GetGuid());

        // Bob ouvre la même étape : lecture seule ; puis il prend la main et Alice le voit.
        var refus = await Ok(await bob.PostAsJsonAsync(url, new { session = "onglet-bob" }));
        Assert.False(refus.GetProperty("pris").GetBoolean());
        Assert.Equal(prise.GetProperty("verrou").GetProperty("nom").GetString(), refus.GetProperty("verrou").GetProperty("nom").GetString());
        var main = await Ok(await bob.PostAsJsonAsync(url, new { session = "onglet-bob", prendreLaMain = true }));
        Assert.True(main.GetProperty("pris").GetBoolean());
        diffuse = JsonDocument.Parse(await ProchainEvenement(lecteur, "verrous", delai.Token)).RootElement;
        Assert.Equal("onglet-bob", diffuse[0].GetProperty("session").GetString());
        Assert.Equal("onglet-alice", diffuse[0].GetProperty("reprisDeSession").GetString());
        Assert.Single((await Ok(await alice.GetAsync($"/api/meps/{id}/verrous"))).EnumerateArray());

        // Relâché par Bob à la fermeture du panneau (Alice n'y peut rien).
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"{url}?session=onglet-alice")).StatusCode);
        Assert.Single((await Ok(await alice.GetAsync($"/api/meps/{id}/verrous"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"{url}?session=onglet-bob")).StatusCode);
        Assert.Empty((await Ok(await alice.GetAsync($"/api/meps/{id}/verrous"))).EnumerateArray());
        Assert.Equal(0, JsonDocument.Parse(await ProchainEvenement(lecteur, "verrous", delai.Token)).RootElement.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{Guid.NewGuid()}/verrous")).StatusCode);
    }

    [Fact]
    public async Task L_ordre_du_graphe_se_choisit_pour_tout_le_monde_sans_toucher_aux_liens()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.All(mep.GetProperty("etapes").EnumerateArray(), e => Assert.Equal(0, e.GetProperty("rang").GetInt32()));
        var prealables = Prealables(mep, "E0007");
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/ordre", new { plan = "Deploiement" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PutAsJsonAsync($"/api/meps/{id}/ordre", new { plan = "Deploiement", ids = new[] { Guid.NewGuid() } })).StatusCode);

        // E0003 passe à gauche de E0001 et E0002 : rangs 1, 2, 3, les autres suivent ; aucun lien ne change.
        var ids = new[] { EtapeId(mep, "E0003"), EtapeId(mep, "E0001"), EtapeId(mep, "E0002") };
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/ordre", new { plan = "Deploiement", ids }));
        Assert.Equal((1, 2, 3), (Etape(mep, "E0003").GetProperty("rang").GetInt32(), Etape(mep, "E0001").GetProperty("rang").GetInt32(),
            Etape(mep, "E0002").GetProperty("rang").GetInt32()));
        Assert.All(mep.GetProperty("etapes").EnumerateArray().Where(e => e.GetProperty("plan").GetString() == "Deploiement"),
            e => Assert.True(e.GetProperty("rang").GetInt32() > 0));
        Assert.Equal(prealables, Prealables(mep, "E0007"));
        var journal = await Journal(alice, id);
        Assert.Equal(("graphe.ordre", "Ordre du graphe de déploiement modifié"), journal[0]);
        Assert.DoesNotContain(journal, j => j.Type == "approbations.reinitialisees");

        // Une étape posée en parallèle de E0003 se range juste à sa droite ; une section créée va en bout.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { paralleleA = EtapeId(mep, "E0003") }));
        Assert.Equal(2, Etape(mep, "E0010").GetProperty("rang").GetInt32());
        Assert.Equal(3, Etape(mep, "E0001").GetProperty("rang").GetInt32());
        var corps = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/sections", new { titre = "Fin de chantier", placee = true }));
        var max = corps.GetProperty("mep").GetProperty("etapes").EnumerateArray().Max(e => e.GetProperty("rang").GetInt32());
        Assert.Equal(max + 1, Section(corps.GetProperty("mep"), corps.GetProperty("sectionId").GetGuid()).GetProperty("rang").GetInt32());
    }
}
