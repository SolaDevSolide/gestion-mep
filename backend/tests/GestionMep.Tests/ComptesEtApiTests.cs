using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Comptes et API harmonisée (#98) : annuaire des comptes réduit, format et codes des erreurs (403 droits, 400 saisie,
/// 422 règle métier), contrôle administrateur unique, fuseau horaire contrôlé.
/// </summary>
[Collection("Api")]
public class ComptesEtApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    /// <summary>Vérifie le statut et le corps « problem details » d'une erreur ; renvoie le corps.</summary>
    private static async Task<JsonElement> Erreur(HttpResponseMessage rep, HttpStatusCode statut, string titre)
    {
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.True(rep.StatusCode == statut, $"{(int)rep.StatusCode} au lieu de {(int)statut} : {texte}");
        Assert.Equal("application/problem+json", rep.Content.Headers.ContentType?.MediaType);
        var corps = JsonDocument.Parse(texte).RootElement.Clone();
        Assert.Equal((int)statut, corps.GetProperty("status").GetInt32());
        Assert.Equal(titre, corps.GetProperty("title").GetString());
        Assert.Equal(corps.GetProperty("message").GetString(), corps.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(corps.GetProperty("message").GetString()));
        return corps;
    }

    // ---------- Annuaire des comptes ----------

    [Fact]
    public async Task L_annuaire_ne_dit_ni_qui_est_administrateur_ni_qui_a_un_mot_de_passe_provisoire()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PostAsJsonAsync("/api/admin/utilisateurs", new { login = "dave", nomAffiche = "Dave", estAdmin = true }));
        var bob = await _api.ClientConnecte("bob");
        foreach (var client in new[] { bob, alice })
        {
            var comptes = (await Ok(await client.GetAsync("/api/utilisateurs"))).EnumerateArray().ToList();
            Assert.Equal(4, comptes.Count);
            foreach (var c in comptes)
            {
                Assert.False(c.TryGetProperty("estAdmin", out _));
                Assert.False(c.TryGetProperty("doitChangerMotDePasse", out _));
                // De quoi choisir une personne : identifiant, noms, état, équipes, expertises, compte principal.
                foreach (var champ in new[] { "id", "login", "nomAffiche", "nomPersonne", "role", "contact", "actif", "equipeIds", "expertises", "comptePrincipalId" })
                    Assert.True(c.TryGetProperty(champ, out _), champ);
            }
        }
        // La console garde le détail complet.
        var console = (await Ok(await alice.GetAsync("/api/admin/utilisateurs"))).EnumerateArray().Single(c => c.GetProperty("login").GetString() == "dave");
        Assert.True(console.GetProperty("estAdmin").GetBoolean());
        Assert.True(console.GetProperty("doitChangerMotDePasse").GetBoolean());
        var refus = await Erreur(await bob.GetAsync("/api/admin/utilisateurs"), HttpStatusCode.Forbidden, "Droits insuffisants");
        Assert.Equal("Réservé aux administrateurs.", refus.GetProperty("message").GetString());
        // Son propre compte, complet.
        Assert.False((await Ok(await bob.GetAsync("/api/auth/moi"))).GetProperty("estAdmin").GetBoolean());
    }

    // ---------- Codes et format des erreurs ----------

    [Fact]
    public async Task Un_refus_de_droits_repond_403_meme_quand_il_vient_d_une_regle_metier()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilExecution); // Alice pilote, Bob suppléant, Claire participante
        var refus = await Erreur(await claire.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Panne" }), HttpStatusCode.Forbidden, "Droits insuffisants");
        Assert.Equal("Seul le pilote ou un suppléant peut déclencher le retour arrière.", refus.GetProperty("message").GetString());
        Assert.False(refus.TryGetProperty("erreurs", out _));
        // Route réservée aux administrateurs : même format.
        await Erreur(await claire.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = 2 }), HttpStatusCode.Forbidden, "Droits insuffisants");
        await Erreur(await claire.PostAsJsonAsync("/api/catalogue/systemes", new { nom = "X" }), HttpStatusCode.Forbidden, "Droits insuffisants");
        await Erreur(await claire.DeleteAsync($"/api/resolutions/{Guid.NewGuid()}"), HttpStatusCode.Forbidden, "Droits insuffisants");
    }

    [Fact]
    public async Task Une_saisie_invalide_repond_400_et_une_regle_metier_422()
    {
        var alice = await _api.ClientConnecte("alice");
        // Saisie : champ obligatoire vide, doublon, formulaire incomplet (liste des anomalies).
        var vide = await Erreur(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = " ", description = "" }), HttpStatusCode.BadRequest, "Saisie invalide");
        Assert.Equal("Le nom de l'équipe est obligatoire.", vide.GetProperty("message").GetString());
        await Ok(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "Réseau", description = "" }));
        await Erreur(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "réseau", description = "" }), HttpStatusCode.BadRequest, "Saisie invalide");
        var incomplete = await Erreur(await alice.PostAsJsonAsync("/api/resolutions", new { titre = "" }), HttpStatusCode.BadRequest, "Saisie invalide");
        Assert.True(incomplete.GetProperty("erreurs").GetArrayLength() > 1);
        await Erreur(await alice.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = "faux", nouveau = "un-nouveau-mot-de-passe" }),
            HttpStatusCode.BadRequest, "Saisie invalide");
        // Corps illisible.
        await Erreur(await alice.PostAsync("/api/admin/equipes", new StringContent("{", System.Text.Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest, "Saisie invalide");

        // Règle métier : demande valide, refusée par l'état de la MEP (en préparation).
        var preparation = await AidesApi.IdDemo(alice);
        var regle = await Erreur(await alice.PostAsJsonAsync($"/api/meps/{preparation}/retour-arriere", new { motif = "Essai" }), HttpStatusCode.UnprocessableEntity, "Règle métier");
        Assert.Equal("Le retour arrière se déclenche pendant l'exécution ou la recette.", regle.GetProperty("message").GetString());

        // Session absente, route inconnue.
        await Erreur(await _api.Client().GetAsync("/api/meps"), HttpStatusCode.Unauthorized, "Authentification requise");
        await Erreur(await alice.GetAsync("/api/inconnue"), HttpStatusCode.NotFound, "Introuvable");
    }

    // ---------- Fuseau horaire ----------

    private static object Identite(JsonElement mep, string fuseau) => new
    {
        version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil").GetString(),
        versionActuelle = mep.GetProperty("versionActuelle").GetString(), versionCible = mep.GetProperty("versionCible").GetString(),
        objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(), fuseauHoraire = fuseau,
        indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"), piloteId = mep.GetProperty("piloteId"),
        suppleantIds = mep.GetProperty("suppleantIds"), echeanceRepli = mep.GetProperty("echeanceRepli"),
    };

    [Fact]
    public async Task Le_fuseau_horaire_d_une_mep_est_un_identifiant_iana_connu()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        foreach (var inconnu in new[] { "Paris", "CET+1", "Europe/Pariss" })
        {
            var refus = await Erreur(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, inconnu)), HttpStatusCode.BadRequest, "Saisie invalide");
            Assert.StartsWith($"Fuseau horaire inconnu : « {inconnu} »", refus.GetProperty("message").GetString());
        }
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, "America/New_York")));
        Assert.Equal("America/New_York", mep.GetProperty("fuseauHoraire").GetString());
        // Identifiant Windows : enregistré sous son équivalent IANA.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, "Romance Standard Time")));
        Assert.Equal("Europe/Paris", mep.GetProperty("fuseauHoraire").GetString());
        // Vide : inchangé.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, "")));
        Assert.Equal("Europe/Paris", mep.GetProperty("fuseauHoraire").GetString());
    }
}
