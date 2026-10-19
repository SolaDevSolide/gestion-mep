using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Beaucoup de MEP (#96) : liste paginée côté serveur avec les filtres de la recherche (pré-filtre SQL, puis la règle exacte
/// partagée avec le navigateur), comptes de la barre de filtres, journal lu par morceaux (nouveaux événements, pages plus
/// anciennes, filtres).
/// </summary>
[Collection("Api")]
public class PaginationTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static List<Guid> Ids(JsonElement liste) => [.. liste.EnumerateArray().Select(m => m.GetProperty("id").GetGuid())];

    private static async Task<Guid> Systeme(HttpClient c, string nom) =>
        (await Ok(await c.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

    private static async Task<Guid> Compte(HttpClient c, string login) =>
        (await Ok(await c.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == login).GetProperty("id").GetGuid();

    /// <summary>Filtres variés de la recherche, tels qu'ils s'écrivent dans l'adresse.</summary>
    private static async Task<List<string>> Filtres(HttpClient c)
    {
        var bdd = await Systeme(c, "BDD");
        var web = await Systeme(c, "WEB");
        var alice = await Compte(c, "alice");
        var equipe = (await Ok(await c.GetAsync("/api/equipes"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);
        return
        [
            "", "etat=reussie,incidents", $"systeme={bdd}", $"systeme={bdd},{web}&environnement=production", $"systeme={bdd}&echec=1", "echec=1",
            $"equipe={equipe}", $"personne={alice}", $"personne={alice}&role=pilote", "recherche=portail", "recherche=BDD", "type=simplifiee",
            "phase=Execution", "etat=preparation,reportee,annulee", "systeme=libre:Inconnu", $"du={aujourdhui.AddDays(-40):yyyy-MM-dd}",
            $"au={aujourdhui.AddDays(-3):yyyy-MM-dd}", $"du={aujourdhui.AddDays(-400):yyyy-MM-dd}&au={aujourdhui:yyyy-MM-dd}&etat=reussie",
        ];
    }

    private static FiltreMeps Lire(string requete)
    {
        var p = System.Web.HttpUtility.ParseQueryString(requete);
        return FiltreMeps.Lire(new ParametresFiltreMeps(p["recherche"], p["systeme"], p["environnement"], p["equipe"], p["personne"], p["role"],
            p["etat"], p["phase"], p["type"], p["du"], p["au"], p["echec"]));
    }

    /// <summary>Toutes les pages, de <paramref name="limite"/> MEP chacune, mises bout à bout.</summary>
    private static async Task<(List<Guid> Ids, int Total, int Ensemble, int Pages)> ToutesLesPages(HttpClient c, string requete, int limite)
    {
        var ids = new List<Guid>();
        string? curseur = null;
        int total = 0, ensemble = 0, pages = 0;
        do
        {
            var url = $"/api/meps/page?modele=false&limite={limite}&{requete}" + (curseur is null ? "" : $"&apres={Uri.EscapeDataString(curseur)}");
            var page = await Ok(await c.GetAsync(url));
            var meps = page.GetProperty("meps");
            Assert.True(meps.GetArrayLength() <= limite);
            ids.AddRange(Ids(meps));
            total = page.GetProperty("total").GetInt32();
            ensemble = page.GetProperty("ensemble").GetInt32();
            curseur = page.GetProperty("suivant").ValueKind == JsonValueKind.Null ? null : page.GetProperty("suivant").GetString();
            pages++;
        } while (curseur is not null && pages < 100);
        return (ids, total, ensemble, pages);
    }

    [Fact]
    public async Task La_liste_paginee_redonne_page_par_page_toute_la_liste_filtree_dans_le_meme_ordre()
    {
        var bob = await _api.ClientConnecte("bob");
        List<MepResumeDto> toutes;
        using (var scope = _api.Services.CreateScope())
            toutes = await MepsEndpoints.Lister(scope.ServiceProvider.GetRequiredService<MepDbContext>(), false);
        var ensemble = toutes.Count;

        foreach (var requete in await Filtres(bob))
        {
            // La règle exacte sur toutes les MEP, sans pré-filtre SQL : la référence.
            var attendus = Lire(requete).Appliquer(toutes).Select(m => m.Id).ToList();
            var liste = Ids(await Ok(await bob.GetAsync($"/api/meps?modele=false&{requete}")));
            Assert.True(attendus.ToHashSet().SetEquals(liste), $"GET /meps « {requete} »");
            var (ids, total, ens, pages) = await ToutesLesPages(bob, requete, 3);
            Assert.True(attendus.ToHashSet().SetEquals(ids), $"pages « {requete} »");
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Equal(attendus.Count, total);
            Assert.Equal(ensemble, ens);
            Assert.Equal(Math.Max(1, (int)Math.Ceiling(attendus.Count / 3.0)), pages);
            // Même ordre que la liste complète : jour décroissant, puis création décroissante.
            var parId = toutes.ToDictionary(m => m.Id);
            Assert.Equal(ids, ids.OrderByDescending(i => parId[i].Jour).ThenByDescending(i => parId[i].CreeLe).ThenByDescending(i => i));
        }
    }

    [Fact]
    public async Task Une_mep_creee_entre_deux_pages_ne_decale_pas_la_suite_et_les_parametres_invalides_sont_refuses()
    {
        var alice = await _api.ClientConnecte("alice");
        var premiere = await Ok(await alice.GetAsync("/api/meps/page?modele=false&limite=2"));
        var suivant = premiere.GetProperty("suivant").GetString()!;
        var deuxiemeAvant = Ids((await Ok(await alice.GetAsync($"/api/meps/page?modele=false&limite=2&apres={suivant}"))).GetProperty("meps"));
        await Ok(await alice.PostAsJsonAsync("/api/meps", new { outil = "Créée entre deux pages" }));
        var deuxieme = Ids((await Ok(await alice.GetAsync($"/api/meps/page?modele=false&limite=2&apres={suivant}"))).GetProperty("meps"));
        Assert.Equal(deuxiemeAvant, deuxieme);
        // La nouvelle MEP est en tête de la première page, et comptée.
        var nouvelle = await Ok(await alice.GetAsync("/api/meps/page?modele=false&limite=2"));
        Assert.Equal("Créée entre deux pages", nouvelle.GetProperty("meps")[0].GetProperty("outil").GetString());
        Assert.Equal(premiere.GetProperty("total").GetInt32() + 1, nouvelle.GetProperty("total").GetInt32());

        foreach (var url in new[] { "/api/meps/page?limite=0", "/api/meps/page?limite=201", "/api/meps/page?apres=pas-un-curseur", "/api/meps/page?etat=inconnu" })
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(url)).StatusCode);
        // Modèles : leur propre ensemble.
        var modeles = await Ok(await alice.GetAsync("/api/meps/page?modele=true"));
        Assert.All(modeles.GetProperty("meps").EnumerateArray(), m => Assert.True(m.GetProperty("estModele").GetBoolean()));
        Assert.Equal(modeles.GetProperty("total").GetInt32(), modeles.GetProperty("ensemble").GetInt32());
    }

    [Fact]
    public async Task Les_comptes_de_la_barre_de_filtres_suivent_exactement_la_regle_du_filtre()
    {
        var bob = await _api.ClientConnecte("bob");
        List<MepResumeDto> toutes;
        using (var scope = _api.Services.CreateScope())
            toutes = await MepsEndpoints.Lister(scope.ServiceProvider.GetRequiredService<MepDbContext>(), false);
        int Compter(FiltreMeps f) => f.Appliquer(toutes).Count;

        foreach (var requete in await Filtres(bob))
        {
            var f = Lire(requete);
            var facettes = await Ok(await bob.GetAsync($"/api/meps/facettes?modele=false&{requete}"));
            Assert.Equal(Compter(f), facettes.GetProperty("total").GetInt32());
            Assert.Equal(toutes.Count, facettes.GetProperty("ensemble").GetInt32());
            int Lu(string critere, string cle) => facettes.GetProperty(critere).TryGetProperty(cle, out var v) ? v.GetInt32() : 0;

            foreach (var s in toutes.SelectMany(m => m.Systemes).Select(s => s.CatalogueId is { } id ? id.ToString() : FiltreMeps.PrefixeLibre + VuesExcel.Normaliser(s.Nom.Trim())).Distinct())
                Assert.Equal(Compter(f with { Systemes = [s] }), Lu("systemes", s));
            foreach (var e in toutes.SelectMany(m => m.Systemes).SelectMany(s => s.Environnements).Select(e => VuesExcel.Normaliser(e.Nom.Trim())).Distinct())
                Assert.Equal(Compter(f with { Environnements = [e] }), Lu("environnements", e));
            foreach (var e in toutes.SelectMany(m => m.EquipeIds).Distinct())
                Assert.Equal(Compter(f with { Equipes = [e] }), Lu("equipes", e.ToString()));
            foreach (var p in toutes.SelectMany(m => m.Intervenants).Select(i => i.PersonneId).Distinct())
                Assert.Equal(Compter(f with { Personne = p, Roles = [] }), Lu("personnes", p.ToString()));
            foreach (var e in Enum.GetValues<EtatMep>())
                Assert.Equal(Compter(f with { Etats = [e] }), Lu("etats", e.ToString()));
            if (f.Recherche.Length > 0) Assert.Equal(Compter(f with { Recherche = "" }), facettes.GetProperty("sans").GetProperty("recherche").GetInt32());
            if (f.Du is not null || f.Au is not null)
                Assert.Equal(Compter(f with { Du = null, Au = null }), facettes.GetProperty("sans").GetProperty("periode").GetInt32());
        }
        // Les systèmes saisis à la main et les environnements proposés viennent des MEP.
        var tout = await Ok(await bob.GetAsync("/api/meps/facettes?modele=false"));
        Assert.Equal(toutes.SelectMany(m => m.Systemes).Where(s => s.CatalogueId is null).Select(s => VuesExcel.Normaliser(s.Nom.Trim())).Distinct().Count(),
            tout.GetProperty("libres").GetArrayLength());
        Assert.Contains(tout.GetProperty("environnementsProposes").EnumerateArray(), e => e.GetProperty("production").GetBoolean());
    }

    [Fact]
    public async Task Le_journal_se_lit_par_morceaux_nouveaux_evenements_pages_anciennes_et_filtres()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        // Quelques événements de plus, pour plusieurs pages.
        for (var n = 0; n < 7; n++)
        {
            var m = await Ok(await alice.GetAsync($"/api/meps/{id}"));
            await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli",
                new { revision = m.GetProperty("revisions").GetProperty("planRepli").GetInt32(), quandDeclencher = $"Seuil {n}" }));
        }
        var complet = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();
        Assert.True(complet.Count > 6, "journal trop court");
        long Id(JsonElement e) => e.GetProperty("id").GetInt64();

        // Pages de 4, du plus récent au plus ancien, bout à bout : tout le journal.
        var lus = new List<long>();
        long? avant = null;
        for (var n = 0; n < 100; n++)
        {
            var page = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal?limite=4" + (avant is { } a ? $"&avant={a}" : "")))).EnumerateArray().ToList();
            lus.AddRange(page.Select(Id));
            if (page.Count < 4) break;
            avant = Id(page[^1]);
        }
        Assert.Equal(complet.Select(Id), lus);

        // Après une écriture : seulement les nouveaux événements, avec le compte de leur auteur.
        var dernier = Id(complet[0]);
        Assert.Empty((await Ok(await alice.GetAsync($"/api/meps/{id}/journal?apres={dernier}"))).EnumerateArray());
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli",
            new { revision = mep.GetProperty("revisions").GetProperty("planRepli").GetInt32(), quandDeclencher = "Journal 50 % _x_" }));
        var nouveaux = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal?apres={dernier}"))).EnumerateArray().ToList();
        Assert.NotEmpty(nouveaux);
        Assert.All(nouveaux, e => Assert.True(Id(e) > dernier));
        Assert.Contains(nouveaux, e => e.GetProperty("type").GetString() == "repli.modifie" && e.GetProperty("auteurLogin").GetString() == "alice");

        // Filtres de l'onglet Journal, comme l'export : famille et texte (sans casse ; « % » et « _ » cherchés tels quels).
        complet = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();
        var dtos = complet.Select(e => e.Deserialize<EvenementDto>(JsonSerializerOptions.Web)!).ToList();
        foreach (var (type, recherche) in new[] { ("etape", ""), ("approbation", ""), ("participant", ""), ("tous", "ALICE"), ("", "modifi"), ("repli", "plan"), ("", "%"), ("", "_x_") })
        {
            var attendus = VuesExcel.FiltrerJournal(dtos, type, recherche).Select(e => e.Id).ToList();
            var obtenus = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal?type={type}&recherche={Uri.EscapeDataString(recherche)}"))).EnumerateArray().Select(Id);
            Assert.Equal(attendus, obtenus);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/meps/{id}/journal?limite=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/meps/{id}/journal?limite=501")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{Guid.NewGuid()}/journal?limite=5")).StatusCode);
    }
}
