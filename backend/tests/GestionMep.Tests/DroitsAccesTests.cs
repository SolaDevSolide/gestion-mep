using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Décisions de droits de la revue de code : pilote et suppléants, pièces jointes, suivi des résolutions types.</summary>
[Collection("Api")]
public class DroitsAccesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

    /// <summary>Corps de <c>PUT /meps/{id}</c> reprenant l'état actuel, avec le pilote et les suppléants donnés.</summary>
    private static object Identite(JsonElement mep, Guid? pilote, IEnumerable<Guid> suppleants) => new
    {
        version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil").GetString(),
        versionActuelle = mep.GetProperty("versionActuelle").GetString(), versionCible = mep.GetProperty("versionCible").GetString(),
        objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(),
        fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(), indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"),
        piloteId = pilote, suppleantIds = suppleants.ToArray(), echeanceRepli = mep.GetProperty("echeanceRepli"),
    };

    private async Task<HttpClient> Compte(HttpClient admin, string login, bool estAdmin)
    {
        _ = admin;
        return await _api.NouveauCompte(login, char.ToUpper(login[0]) + login[1..], estAdmin);
    }

    [Fact]
    public async Task Seuls_le_pilote_un_suppleant_ou_un_admin_changent_le_pilote_et_les_suppleants()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.ClaireResponsableTechnique(alice, id);
        Guid pAlice = Participant(mep, "Alice"), pBob = Participant(mep, "Bob"), pClaire = Participant(mep, "Claire");

        // Claire est responsable technique (éditrice) mais ni pilote ni suppléante.
        Assert.Contains(pClaire, mep.GetProperty("responsablesTechniques").EnumerateArray().Select(r => r.GetProperty("participantId").GetGuid()));
        var refus = await claire.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, pClaire, []));
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Equal("Seul le pilote, un suppléant ou un administrateur peut changer le pilote ou les suppléants.", await Message(refus));
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, pAlice, [pBob, pClaire]))).StatusCode);

        // Les autres champs d'identité restent modifiables par Claire.
        var corps = JsonSerializer.SerializeToNode(Identite(mep, pAlice, [pBob]))!;
        corps["objet"] = "Objet revu par Claire";
        mep = await Ok(await claire.PutAsJsonAsync($"/api/meps/{id}", corps));
        Assert.Equal("Objet revu par Claire", mep.GetProperty("objet").GetString());

        // Bob, suppléant, change les suppléants ; Alice, pilote, change le pilote.
        mep = await Ok(await bob.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, pAlice, [pBob, pClaire])));
        Assert.Equal([pBob, pClaire], mep.GetProperty("suppleantIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, pBob, [pClaire])));
        Assert.Equal(pBob, mep.GetProperty("piloteId").GetGuid());
    }

    [Fact]
    public async Task Sans_pilote_un_responsable_technique_le_designe()
    {
        var bob = await _api.ClientConnecte("bob");
        var mep = await Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Outil neuf", objet = "Test" }));
        var id = mep.GetProperty("id").GetGuid();
        mep = await Ok(await bob.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, Participant(mep, "Bob"), [])));
        Assert.Equal(Participant(mep, "Bob"), mep.GetProperty("piloteId").GetGuid());
    }

    [Fact]
    public async Task Un_administrateur_supprime_une_piece_jointe_d_une_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var dave = await Compte(alice, "dave", estAdmin: false);
        var eve = await Compte(alice, "eve", estAdmin: true);
        var id = await AidesApi.IdDemo(alice);

        var fichier = new ByteArrayContent([1, 2, 3]);
        fichier.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mep = await Ok(await claire.PostAsync($"/api/meps/{id}/pieces-jointes", new MultipartFormDataContent { { fichier, "fichier", "note.txt" } }));
        var pid = mep.GetProperty("piecesJointes")[0].GetProperty("id").GetGuid();

        var refus = await dave.DeleteAsync($"/api/meps/{id}/pieces-jointes/{pid}");
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Equal("Seul le déposant, le pilote, un suppléant ou un administrateur peut supprimer cette pièce jointe.", await Message(refus));

        // Eve est administratrice, ni déposante ni pilote.
        mep = await Ok(await eve.DeleteAsync($"/api/meps/{id}/pieces-jointes/{pid}"));
        Assert.Empty(mep.GetProperty("piecesJointes").EnumerateArray());
        Assert.Empty(Directory.GetFiles(_api.DossierPieces));
    }

    [Fact]
    public async Task Les_modifications_d_une_resolution_type_sont_tracees()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");

        object Saisie(string titre, int? version, string instruction) => new
        {
            version, titre, symptomes = "Erreur 500", diagnostic = "Lire le journal",
            corrections = new[] { new { ordre = 1, instruction, resultatAttendu = "OK" } },
            verificationInstruction = "Appeler /sante", verificationAttendu = "200",
        };

        var creee = await bob.PostAsJsonAsync("/api/resolutions", Saisie("Cache corrompu", null, "Vider le cache"));
        Assert.Equal(HttpStatusCode.Created, creee.StatusCode);
        var r = await Ok(creee);
        var rid = r.GetProperty("id").GetGuid();
        Assert.Equal("Bob", r.GetProperty("modifieeParNom").GetString());

        r = await Ok(await claire.PutAsJsonAsync($"/api/resolutions/{rid}", Saisie("Cache corrompu", 1, "rm -rf /var/cache/api")));
        Assert.Equal(2, r.GetProperty("version").GetInt32());
        Assert.Equal("Claire", r.GetProperty("modifieeParNom").GetString());
        Assert.Equal("Claire", (await Ok(await bob.GetAsync($"/api/resolutions/{rid}"))).GetProperty("modifieeParNom").GetString());

        // Historique, du plus récent au plus ancien : chaque version garde son contenu et son auteur.
        var versions = (await Ok(await bob.GetAsync($"/api/resolutions/{rid}/versions"))).EnumerateArray().ToList();
        Assert.Equal([2, 1], versions.Select(v => v.GetProperty("version").GetInt32()).ToArray());
        Assert.Equal(["modifiee", "creee"], versions.Select(v => v.GetProperty("action").GetString()!).ToArray());
        Assert.Equal(["Claire", "Bob"], versions.Select(v => v.GetProperty("modifieeParNom").GetString()!).ToArray());
        Assert.Equal(["rm -rf /var/cache/api", "Vider le cache"],
            versions.Select(v => v.GetProperty("corrections")[0].GetProperty("instruction").GetString()!).ToArray());

        // La suppression d'une fiche liée à une étape la désactive : version tracée elle aussi.
        var mep = await Ok(await alice.GetAsync($"/api/meps/{await AidesApi.IdDemo(alice)}"));
        var lieeAUneEtape = mep.GetProperty("etapes").EnumerateArray().SelectMany(e => e.GetProperty("resolutionIds").EnumerateArray())
            .Select(x => x.GetGuid()).First();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/resolutions/{lieeAUneEtape}")).StatusCode);
        var desactivee = (await Ok(await bob.GetAsync($"/api/resolutions/{lieeAUneEtape}/versions"))).EnumerateArray().ToList();
        Assert.Equal(("desactivee", "Alice", false), (desactivee[0].GetProperty("action").GetString(), desactivee[0].GetProperty("modifieeParNom").GetString(),
            desactivee[0].GetProperty("actif").GetBoolean()));
        // Fiche de démonstration : sa création est tracée sans auteur.
        Assert.Equal(("creee", JsonValueKind.Null), (desactivee[^1].GetProperty("action").GetString(), desactivee[^1].GetProperty("modifieeParNom").ValueKind));

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/resolutions/{Guid.NewGuid()}/versions")).StatusCode);
    }
}
