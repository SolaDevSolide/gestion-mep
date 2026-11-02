using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Jetons d'accès à l'API (#128) : jetons personnels et de service, portées, expiration, révocation, compte désactivé,
/// empreinte seule en base, aucune session ni contrôle CSRF pour un appel par jeton, journal, limite de débit.
/// </summary>
[Collection("Api")]
public class JetonsApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<JsonElement> Erreur(HttpResponseMessage rep, HttpStatusCode statut)
    {
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.True(rep.StatusCode == statut, $"{(int)rep.StatusCode} au lieu de {(int)statut} : {texte}");
        Assert.Equal("application/problem+json", rep.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(texte).RootElement.Clone();
    }

    /// <summary>Client sans cookie ni en-tête anti-CSRF, qui présente le jeton (comme un script ou un outil de CI).</summary>
    private HttpClient ParJeton(string jeton)
    {
        var c = _api.ClientSansEnTete();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return c;
    }

    private static async Task<(string Secret, JsonElement Jeton)> Creer(HttpClient session, string nom, string[] portees, int? dureeJours = null,
        string route = "/api/jetons")
    {
        var rep = await session.PostAsJsonAsync(route, new { nom, portees, dureeJours });
        Assert.Equal(HttpStatusCode.Created, rep.StatusCode);
        var corps = await Ok(rep);
        return (corps.GetProperty("secret").GetString()!, corps.GetProperty("jeton"));
    }

    private async Task<T> EnBase<T>(Func<MepDbContext, Task<T>> lecture)
    {
        using var scope = _api.Services.CreateScope();
        return await lecture(scope.ServiceProvider.GetRequiredService<MepDbContext>());
    }

    [Fact]
    public async Task Un_jeton_personnel_agit_au_nom_de_la_personne_sans_session_et_n_est_stocke_que_hache()
    {
        var bob = await _api.ClientConnecte("bob");
        var (secret, jeton) = await Creer(bob, "Script de Bob", [PorteesJeton.MepsLecture]);
        Assert.StartsWith("gmep_", secret);
        Assert.Equal(5 + AuthentificationJeton.LongueurSecret, secret.Length);
        Assert.Equal(secret[..13], jeton.GetProperty("prefixe").GetString());
        Assert.Equal("Personnel", jeton.GetProperty("type").GetString());
        Assert.Equal("Actif", jeton.GetProperty("etat").GetString());
        var expire = jeton.GetProperty("expireLe").GetDateTimeOffset();
        Assert.InRange(expire - DateTimeOffset.UtcNow, TimeSpan.FromDays(89.9), TimeSpan.FromDays(90.1));

        // Empreinte SHA-256 et préfixe seulement : le secret n'apparaît ni en base ni dans la liste.
        var ligne = await EnBase(db => db.JetonsApi.AsNoTracking().SingleAsync(j => j.Nom == "Script de Bob"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), ligne.Empreinte);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(ligne));
        var brut = await EnBase(async db =>
        {
            var cn = db.Database.GetDbConnection();
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "select row_to_json(j)::text from \"JetonsApi\" j";
            return (string)(await cmd.ExecuteScalarAsync())!;
        });
        Assert.DoesNotContain(secret[5..], brut);
        var liste = await bob.GetStringAsync("/api/jetons");
        Assert.DoesNotContain(secret, liste);
        Assert.Contains("Script de Bob", liste);

        // Appel par jeton : la personne est Bob, aucun cookie n'est posé.
        var script = ParJeton(secret);
        var rep = await script.GetAsync("/api/auth/moi");
        var moi = await Ok(rep);
        Assert.Equal("bob", moi.GetProperty("login").GetString());
        Assert.False(rep.Headers.Contains("Set-Cookie"));
        await Ok(await script.GetAsync("/api/meps"));

        // Dernière utilisation enregistrée (date et adresse).
        var vu = (await Ok(await bob.GetAsync($"/api/jetons/{jeton.GetProperty("id").GetGuid()}")));
        Assert.NotEqual(JsonValueKind.Null, vu.GetProperty("derniereUtilisationLe").ValueKind);
        Assert.True(vu.TryGetProperty("derniereAdresseIp", out _)); // adresse du client (absente sous le serveur de test)

        // Journal d'administration : création, au nom de Bob.
        var creation = await EnBase(db => db.JournalAdmin.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync(e => e.Type == "jeton.cree"));
        Assert.Contains(ligne.Prefixe, creation.Detail);
        Assert.DoesNotContain(secret, creation.Detail);
    }

    [Fact]
    public async Task Hors_de_ses_portees_un_jeton_recoit_403_et_ecrit_sans_en_tete_anti_csrf()
    {
        var bob = await _api.ClientConnecte("bob");
        var (catalogue, _) = await Creer(bob, "Lecture du catalogue", [PorteesJeton.CatalogueLecture]);
        var lecteur = ParJeton(catalogue);
        await Ok(await lecteur.GetAsync("/api/catalogue/systemes"));
        var refus = await Erreur(await lecteur.GetAsync("/api/meps"), HttpStatusCode.Forbidden);
        Assert.Contains("meps:lecture", refus.GetProperty("message").GetString());
        var ecriture = await lecteur.PostAsJsonAsync("/api/catalogue/systemes", new { nom = "NOUVEAU" });
        Assert.Contains("catalogue:ecriture", (await Erreur(ecriture, HttpStatusCode.Forbidden)).GetProperty("message").GetString());
        Assert.Contains("insufficient_scope", ecriture.Headers.WwwAuthenticate.ToString());

        // meps:ecriture comprend la lecture, pas l'exécution des étapes.
        var (redacteur, _) = await Creer(bob, "Rédaction", [PorteesJeton.MepsEcriture]);
        var r = ParJeton(redacteur);
        var mep = await Ok(await r.PostAsJsonAsync("/api/meps", new { outil = "Créée par un script" }));
        Assert.Equal("Créée par un script", mep.GetProperty("outil").GetString());
        var id = mep.GetProperty("id").GetGuid();
        await Ok(await r.GetAsync($"/api/meps/{id}"));
        var demarrer = await r.PostAsJsonAsync($"/api/meps/{id}/etapes/{Guid.NewGuid()}/demarrer", new { revision = 0 });
        Assert.Contains("execution", (await Erreur(demarrer, HttpStatusCode.Forbidden)).GetProperty("message").GetString());

        // Avec la portée execution, la route est ouverte : seules les règles habituelles répondent (étape inconnue).
        var (executant, _) = await Creer(bob, "Exécution", [PorteesJeton.MepsLecture, PorteesJeton.Execution]);
        var etape = await ParJeton(executant).PostAsJsonAsync($"/api/meps/{id}/etapes/{Guid.NewGuid()}/demarrer", new { revision = 0 });
        Assert.NotEqual(HttpStatusCode.Forbidden, etape.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_n_ouvre_ni_session_ni_gestion_des_jetons_ni_droits_d_administrateur()
    {
        var alice = await _api.ClientConnecte("alice");
        // Personne n'obtient la portée admin sur un jeton personnel, même un administrateur.
        var admin = await alice.PostAsJsonAsync("/api/jetons", new { nom = "Admin", portees = new[] { "admin" } });
        Assert.Contains("réservée aux jetons de service", (await Erreur(admin, HttpStatusCode.BadRequest)).GetProperty("message").GetString());
        var (tout, _) = await Creer(alice, "Tout sauf admin",
            [PorteesJeton.MepsEcriture, PorteesJeton.Execution, PorteesJeton.CatalogueEcriture]);
        var c = ParJeton(tout);
        Assert.Contains("« admin »", (await Erreur(await c.GetAsync("/api/admin/utilisateurs"), HttpStatusCode.Forbidden)).GetProperty("message").GetString());
        await Erreur(await c.PostAsJsonAsync("/api/jetons", new { nom = "Autre", portees = new[] { "meps:lecture" } }), HttpStatusCode.Forbidden);
        await Erreur(await c.PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = "demo" }), HttpStatusCode.Forbidden);
        await Erreur(await c.PutAsJsonAsync("/api/utilisateurs/moi/profil", new { role = "x" }), HttpStatusCode.Forbidden);
        await Erreur(await c.GetAsync("/api/jetons"), HttpStatusCode.Forbidden);

        // Saisie contrôlée : portée inconnue, durée hors bornes, nom vide.
        await Erreur(await alice.PostAsJsonAsync("/api/jetons", new { nom = "x", portees = new[] { "tout" } }), HttpStatusCode.BadRequest);
        await Erreur(await alice.PostAsJsonAsync("/api/jetons", new { nom = "x", portees = new[] { "meps:lecture" }, dureeJours = 366 }), HttpStatusCode.BadRequest);
        await Erreur(await alice.PostAsJsonAsync("/api/jetons", new { nom = " ", portees = new[] { "meps:lecture" } }), HttpStatusCode.BadRequest);
        // Un jeton n'est pas un mot de passe : la connexion à l'interface le refuse.
        await Erreur(await _api.Client().PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = tout }), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_invalide_expire_ou_revoque_recoit_401_meme_sur_une_route_publique()
    {
        var inconnu = ParJeton("gmep_" + new string('x', AuthentificationJeton.LongueurSecret));
        var rep = await inconnu.GetAsync("/api/sante");
        Assert.Equal("Jeton d'accès invalide.", (await Erreur(rep, HttpStatusCode.Unauthorized)).GetProperty("message").GetString());
        Assert.Contains("invalid_token", rep.Headers.WwwAuthenticate.ToString());
        await Erreur(await ParJeton("pas-un-jeton").GetAsync("/api/meps"), HttpStatusCode.Unauthorized);

        var bob = await _api.ClientConnecte("bob");
        var (secret, jeton) = await Creer(bob, "Court", [PorteesJeton.MepsLecture], dureeJours: 1);
        var id = jeton.GetProperty("id").GetGuid();
        await Ok(await ParJeton(secret).GetAsync("/api/meps"));

        // Expiré : 401, et la liste le montre expiré.
        await EnBase(db => db.JetonsApi.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ExpireLe, DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Contains("expiré", (await Erreur(await ParJeton(secret).GetAsync("/api/meps"), HttpStatusCode.Unauthorized)).GetProperty("message").GetString());
        Assert.Equal("Expire", (await Ok(await bob.GetAsync($"/api/jetons/{id}"))).GetProperty("etat").GetString());

        // Révoqué : 401, une seconde révocation répond 422 ; un autre compte ne voit pas ce jeton (404).
        var (autre, autreJeton) = await Creer(bob, "À révoquer", [PorteesJeton.MepsLecture]);
        var autreId = autreJeton.GetProperty("id").GetGuid();
        var claire = await _api.ClientConnecte("claire");
        await Erreur(await claire.PostAsync($"/api/jetons/{autreId}/revoquer", null), HttpStatusCode.NotFound);
        var revoque = await Ok(await bob.PostAsync($"/api/jetons/{autreId}/revoquer", null));
        Assert.Equal("Revoque", revoque.GetProperty("etat").GetString());
        Assert.Contains("révoqué", (await Erreur(await ParJeton(autre).GetAsync("/api/meps"), HttpStatusCode.Unauthorized)).GetProperty("message").GetString());
        await Erreur(await bob.PostAsync($"/api/jetons/{autreId}/revoquer", null), (HttpStatusCode)422);
        var evenement = await EnBase(db => db.JournalAdmin.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync(e => e.Type == "jeton.revoque"));
        Assert.Contains("À révoquer", evenement.Detail);
    }

    [Fact]
    public async Task Le_jeton_d_un_compte_desactive_ne_fonctionne_plus_et_le_cookie_est_ignore()
    {
        var bob = await _api.ClientConnecte("bob");
        var (secret, _) = await Creer(bob, "Script", [PorteesJeton.MepsLecture]);
        var alice = await _api.ClientConnecte("alice");
        var idBob = (await Ok(await alice.GetAsync("/api/admin/utilisateurs"))).EnumerateArray()
            .Single(u => u.GetProperty("login").GetString() == "bob").GetProperty("id").GetGuid();

        // Session d'Alice et jeton de Bob sur la même requête : seul le jeton compte.
        var melange = new HttpRequestMessage(HttpMethod.Get, "/api/auth/moi");
        melange.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        Assert.Equal("bob", (await Ok(await alice.SendAsync(melange))).GetProperty("login").GetString());
        var invalide = new HttpRequestMessage(HttpMethod.Get, "/api/auth/moi");
        invalide.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "gmep_inconnu");
        await Erreur(await alice.SendAsync(invalide), HttpStatusCode.Unauthorized);

        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idBob}/desactiver", null));
        Assert.Contains("désactivé", (await Erreur(await ParJeton(secret).GetAsync("/api/meps"), HttpStatusCode.Unauthorized)).GetProperty("message").GetString());
        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idBob}/reactiver", null));
        await Ok(await ParJeton(secret).GetAsync("/api/meps"));
    }

    [Fact]
    public async Task Un_jeton_de_service_agit_sous_son_compte_technique_et_signe_le_journal_de_son_nom()
    {
        var bob = await _api.ClientConnecte("bob");
        await Erreur(await bob.PostAsJsonAsync("/api/admin/jetons", new { nom = "CI", portees = new[] { "admin" } }), HttpStatusCode.Forbidden);
        var alice = await _api.ClientConnecte("alice");
        // Le nom ne peut pas imiter un compte existant.
        await Erreur(await alice.PostAsJsonAsync("/api/admin/jetons", new { nom = "bob", portees = new[] { "meps:lecture" } }), HttpStatusCode.BadRequest);
        var (secret, jeton) = await Creer(alice, "Supervision", [PorteesJeton.Admin, PorteesJeton.MepsLecture], 365, "/api/admin/jetons");
        Assert.Equal("Service", jeton.GetProperty("type").GetString());
        var login = jeton.GetProperty("compteLogin").GetString()!;
        Assert.StartsWith("service-", login);

        var outil = ParJeton(secret);
        var moi = await Ok(await outil.GetAsync("/api/auth/moi"));
        Assert.Equal("Supervision", moi.GetProperty("nomAffiche").GetString());
        Assert.True(moi.GetProperty("estTechnique").GetBoolean());
        await Ok(await outil.GetAsync("/api/admin/utilisateurs"));
        var equipe = await outil.PostAsJsonAsync("/api/admin/equipes", new { nom = "Créée par la supervision", description = "", membreIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, equipe.StatusCode);
        var trace = await EnBase(db => db.JournalAdmin.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync(e => e.Type == "equipe.creee"));
        Assert.Equal("Supervision", trace.Auteur);
        // Sans la portée catalogue, le catalogue reste fermé ; et un jeton ne gère pas les jetons, même administrateur.
        await Erreur(await outil.GetAsync("/api/catalogue/systemes"), HttpStatusCode.Forbidden);
        await Erreur(await outil.GetAsync("/api/admin/jetons"), HttpStatusCode.Forbidden);

        // Le compte technique ne se connecte pas et ne reçoit pas de mot de passe.
        var compteId = jeton.GetProperty("compteId").GetGuid();
        await Erreur(await alice.PostAsync($"/api/admin/utilisateurs/{compteId}/reinitialiser-mot-de-passe", null), (HttpStatusCode)422);
        await Erreur(await _api.Client().PostAsJsonAsync("/api/auth/connexion", new { login, motDePasse = "" }), HttpStatusCode.Unauthorized);

        // L'administrateur voit tous les jetons et peut révoquer celui de n'importe qui.
        var (perso, persoJeton) = await Creer(bob, "Perso de Bob", [PorteesJeton.MepsLecture]);
        var tous = await Ok(await alice.GetAsync("/api/admin/jetons"));
        Assert.Equal(2, tous.GetArrayLength());
        Assert.Single((await Ok(await alice.GetAsync("/api/admin/jetons?type=Service"))).EnumerateArray());
        await Ok(await alice.PostAsync($"/api/admin/jetons/{persoJeton.GetProperty("id").GetGuid()}/revoquer", null));
        await Erreur(await ParJeton(perso).GetAsync("/api/meps"), HttpStatusCode.Unauthorized);
        await Ok(await alice.PostAsync($"/api/admin/jetons/{jeton.GetProperty("id").GetGuid()}/revoquer", null));
        await Erreur(await outil.GetAsync("/api/meps"), HttpStatusCode.Unauthorized);
        Assert.False(await EnBase(db => db.Utilisateurs.Where(u => u.Id == compteId).Select(u => u.Actif).SingleAsync()));
    }

    [Fact]
    public async Task Au_dela_de_la_limite_par_minute_un_jeton_recoit_429()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/jetons-par-minute", new { valeur = "3" }));
        var (secret, _) = await Creer(alice, "Pressé", [PorteesJeton.MepsLecture]);
        var c = ParJeton(secret);
        for (var i = 0; i < 3; i++) await Ok(await c.GetAsync("/api/auth/moi"));
        var refus = await Erreur(await c.GetAsync("/api/auth/moi"), HttpStatusCode.TooManyRequests);
        Assert.Contains("jeton", refus.GetProperty("message").GetString());
        // Les sessions ne sont pas concernées.
        for (var i = 0; i < 5; i++) await Ok(await alice.GetAsync("/api/auth/moi"));
    }

    /// <summary>
    /// Toute route /api a une décision : fermée aux jetons, ou ouverte avec ses portées. Une route ajoutée sans y penser
    /// demande meps:lecture (lecture) ou meps:ecriture (écriture), jamais rien ; les routes d'administration, admin.
    /// </summary>
    [Fact]
    public void Chaque_route_a_sa_portee()
    {
        var routes = _api.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api", StringComparison.Ordinal)).ToList();
        Assert.True(routes.Count > 150, $"{routes.Count} routes seulement");
        var sansPortee = new List<string>();
        foreach (var r in routes)
        {
            var methodes = r.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var m in methodes)
            {
                var d = PorteesRoutes.Requises(m, r);
                var forme = PorteesRoutes.Forme(r.RoutePattern.RawText!);
                if (forme.StartsWith("/admin", StringComparison.Ordinal) && d.Ouverte) Assert.Equal(new[] { PorteesJeton.Admin }, d.UneParmi);
                if (forme.StartsWith("/jetons", StringComparison.Ordinal) || forme.StartsWith("/admin/jetons", StringComparison.Ordinal)) Assert.False(d.Ouverte);
                if (forme == "/calendrier/abonnement") Assert.False(d.Ouverte);
                if (d.Ouverte && d.UneParmi.Count == 0) sansPortee.Add($"{m} {forme}");
            }
        }
        var publiques = new[] { "/sante", "/sante/prete", "/version", "/charte", "/charte/schema.json", "/calendrier/abonnement/*" };
        Assert.All(sansPortee, s => Assert.True(s == "GET /auth/moi" || s.EndsWith(" /application") || s.EndsWith(" /openapi.json") || s.EndsWith(" /*")
            || publiques.Any(p => s.EndsWith(" " + p, StringComparison.Ordinal)), $"Route ouverte sans portée : {s}"));
    }

    [Theory]
    [InlineData("GET", "/api/meps/{id:guid}", false, "meps:lecture")]
    [InlineData("PUT", "/api/meps/{id:guid}", false, "meps:ecriture")]
    [InlineData("POST", "/api/meps/{id:guid}/etapes/{eid:guid}/terminer", false, "execution")]
    [InlineData("POST", "/api/meps/{id:guid}/fiches-recette/{fid:guid}/etapes/{tid:guid}/resultat", false, "execution")]
    [InlineData("POST", "/api/meps/{id:guid}/etapes/{eid:guid}/pieces-jointes", false, "execution|meps:ecriture")]
    [InlineData("GET", "/api/catalogue/systemes", false, "catalogue:lecture")]
    [InlineData("DELETE", "/api/catalogue/systemes/{id:guid}", true, "catalogue:ecriture")]
    [InlineData("PUT", "/api/parametres", true, "admin")]
    [InlineData("GET", "/api/admin/journal", true, "admin")]
    [InlineData("POST", "/api/auth/mot-de-passe", false, null)]
    [InlineData("POST", "/api/demo/connexion", false, null)]
    public void Table_des_portees(string methode, string modele, bool admin, string? attendu)
    {
        var d = PorteesRoutes.Requises(methode, modele, admin, anonyme: false);
        if (attendu is null) Assert.False(d.Ouverte);
        else Assert.Equal(attendu, string.Join('|', d.UneParmi));
    }

    [Fact]
    public void Regles_des_jetons()
    {
        var maintenant = DateTimeOffset.UtcNow;
        var (nom, portees, expire) = Regles.ValiderJeton(TypeJeton.Service, "  CI  ", ["execution", "MEPS:LECTURE", "admin"], null, maintenant);
        Assert.Equal("CI", nom);
        Assert.Equal(["meps:lecture", "execution", "admin"], portees);
        Assert.Equal(maintenant.AddDays(90), expire);
        Assert.Equal(maintenant.AddDays(365), Regles.ValiderJeton(TypeJeton.Personnel, "x", ["meps:lecture"], 365, maintenant).ExpireLe);
        Assert.Throws<SaisieInvalideException>(() => Regles.ValiderJeton(TypeJeton.Personnel, "x", [], null, maintenant));
        Assert.Throws<SaisieInvalideException>(() => Regles.ValiderJeton(TypeJeton.Personnel, "x", ["meps:lecture"], 0, maintenant));
        Assert.True(PorteesJeton.Couvre(["meps:ecriture"], "meps:lecture"));
        Assert.False(PorteesJeton.Couvre(["meps:lecture"], "meps:ecriture"));
        Assert.False(PorteesJeton.Couvre(["meps:ecriture"], "execution"));
        Assert.True(Regles.UtilisationAEnregistrer(null, maintenant));
        Assert.False(Regles.UtilisationAEnregistrer(maintenant.AddMinutes(-4), maintenant));
        Assert.True(Regles.UtilisationAEnregistrer(maintenant.AddMinutes(-5), maintenant));
        var jeton = new JetonApi { ExpireLe = maintenant.AddDays(1) };
        Regles.RevoquerJeton(jeton, "Alice", maintenant);
        Assert.Equal(EtatJeton.Revoque, jeton.Etat(maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.RevoquerJeton(jeton, "Alice", maintenant));
    }

    [Fact]
    public async Task La_ligne_de_commande_cree_liste_et_revoque_les_jetons()
    {
        var cree = await LigneDeCommandeConventionsTests.Cli("jetons", "creer", "CI GitLab", "--portees", "meps:lecture,execution", "--duree", "30", "--json");
        Assert.True(cree.Code == 0, cree.Erreur);
        var secret = cree.Json.GetProperty("resultat").GetProperty("secret").GetString()!;
        var prefixe = cree.Json.GetProperty("resultat").GetProperty("jeton").GetProperty("prefixe").GetString()!;
        Assert.Equal("Ligne de commande", cree.Json.GetProperty("resultat").GetProperty("jeton").GetProperty("creePar").GetString());
        Assert.Equal("CI GitLab", (await Ok(await ParJeton(secret).GetAsync("/api/auth/moi"))).GetProperty("nomAffiche").GetString());

        var texte = await LigneDeCommandeConventionsTests.Cli("jetons", "creer", "Autre", "--portees", "meps:lecture");
        Assert.Equal(0, texte.Code);
        Assert.Contains("Jeton : gmep_", texte.Sortie);

        var liste = await LigneDeCommandeConventionsTests.Cli("jetons", "liste", "--type", "service", "--json");
        Assert.Equal(0, liste.Code);
        Assert.Equal(2, liste.Json.GetArrayLength());
        Assert.DoesNotContain(secret, liste.Sortie);
        Assert.Contains(prefixe, (await LigneDeCommandeConventionsTests.Cli("jetons", "liste")).Sortie);

        Assert.Equal(2, (await LigneDeCommandeConventionsTests.Cli("jetons", "creer", "Sans portée")).Code);
        Assert.Equal(1, (await LigneDeCommandeConventionsTests.Cli("jetons", "creer", "Mauvaise", "--portees", "tout")).Code);
        Assert.Equal(2, (await LigneDeCommandeConventionsTests.Cli("jetons", "liste", "--type", "autre")).Code);
        Assert.Equal(5, (await LigneDeCommandeConventionsTests.Cli("jetons", "revoquer", "gmep_inconnu", "--oui")).Code);
        Assert.Equal(4, (await LigneDeCommandeConventionsTests.Cli("jetons", "revoquer", prefixe, "--non-interactif")).Code);
        await Ok(await ParJeton(secret).GetAsync("/api/meps"));

        var revoque = await LigneDeCommandeConventionsTests.Cli("jetons", "revoquer", prefixe, "--oui", "--json");
        Assert.True(revoque.Code == 0, revoque.Erreur);
        Assert.Equal("Revoque", revoque.Json.GetProperty("resultat").GetProperty("etat").GetString());
        await Erreur(await ParJeton(secret).GetAsync("/api/meps"), HttpStatusCode.Unauthorized);
        Assert.Equal(1, (await LigneDeCommandeConventionsTests.Cli("jetons", "revoquer", prefixe, "--oui")).Code);
        var actifs = await LigneDeCommandeConventionsTests.Cli("jetons", "liste", "--actifs", "--json");
        Assert.Equal(1, actifs.Json.GetArrayLength());
    }
}
