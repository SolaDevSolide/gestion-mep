using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GestionMep.Tests;

/// <summary>
/// Connexions aux API externes (#111) de bout en bout : droits de la console, secrets jamais relus, réseau sortant protégé
/// (adresses internes, changement d'adresse DNS, redirections), file d'envoi (transaction, reprise, nouvelles tentatives,
/// Retry-After, ordre), envoi depuis une MEP et renvoi, import et export en ligne de commande. Un vrai serveur HTTP local
/// (127.0.0.1) joue l'outil appelé.
/// </summary>
[Collection("Api")]
public class ConnexionsApiTests : IAsyncLifetime, IDisposable
{
    private readonly ResolveurTest _noms = new();
    private readonly ApiFixture _api;
    private Recepteur _outil = null!;

    public ConnexionsApiTests() =>
        _api = new ApiFixture(hote: b => b.ConfigureTestServices(s => s.AddSingleton<IResolveurNoms>(_noms)));

    public async Task InitializeAsync() => _outil = await Recepteur.Demarrer();
    public async Task DisposeAsync() => await _outil.DisposeAsync();
    public void Dispose() => _api.Dispose();

    // ---------- Aides ----------

    /// <summary>Résolution de noms simulée : un nom connu donne les adresses choisies par le test.</summary>
    private sealed class ResolveurTest : IResolveurNoms
    {
        public ConcurrentDictionary<string, IPAddress[]> Noms { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<IPAddress[]> Resoudre(string hote, CancellationToken ct) =>
            Noms.TryGetValue(hote, out var a) ? Task.FromResult(a) : Dns.GetHostAddressesAsync(hote, ct);
    }

    private sealed record RequeteRecue(string Methode, string Chemin, Dictionary<string, string> Entetes, string Corps);

    /// <summary>L'outil appelé : un serveur HTTP sur 127.0.0.1 qui garde les requêtes reçues et répond ce que le test lui dit.</summary>
    private sealed class Recepteur : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public ConcurrentQueue<RequeteRecue> Recues { get; } = new();
        public Func<HttpContext, Task> Repondre { get; set; } = ctx => ctx.Response.WriteAsJsonAsync(new { recu = true });
        public string Adresse { get; private set; } = "";
        public int Port => new Uri(Adresse).Port;

        private Recepteur(WebApplication app) => _app = app;

        public static async Task<Recepteur> Demarrer()
        {
            var b = WebApplication.CreateSlimBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            b.Logging.ClearProviders();
            var r = new Recepteur(b.Build());
            r._app.Run(async ctx =>
            {
                using var lecteur = new StreamReader(ctx.Request.Body);
                var corps = await lecteur.ReadToEndAsync();
                r.Recues.Enqueue(new RequeteRecue(ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString,
                    ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase), corps));
                await r.Repondre(ctx);
            });
            await r._app.StartAsync();
            r.Adresse = r._app.Urls.First().TrimEnd('/');
            return r;
        }

        public static Func<HttpContext, Task> Code(int code, string? retryAfter = null) => ctx =>
        {
            ctx.Response.StatusCode = code;
            if (retryAfter is not null) ctx.Response.Headers.RetryAfter = retryAfter;
            return Task.CompletedTask;
        };

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static object Saisie(string nom, string url, string[]? declencheurs = null, bool envoiManuel = false, int tentatives = 3,
        int? version = null, string authentification = "Aucune", string? secret = null, object[]? entetes = null) => new
    {
        version, nom, modele = "WebhookJson", methode = "Post", url, urlSecrete = false, authentification, secret,
        entetes = entetes ?? [], format = "Json", corps = "{\"evenement\": \"{{evenement_cle}}\", \"mep\": \"{{mep_id}}\", \"commentaire\": \"{{commentaire}}\"}",
        declencheurs = declencheurs ?? [], envoiManuel, libelleEnvoiManuel = envoiManuel ? "Prévenir l'outil" : "", tentatives, delaiSecondes = 5,
    };

    private static async Task<Guid> Creer(HttpClient admin, object saisie)
    {
        var rep = await admin.PostAsJsonAsync("/api/admin/connexions", saisie);
        Assert.Equal(HttpStatusCode.Created, rep.StatusCode);
        return (await AidesApi.Ok(rep)).GetProperty("id").GetGuid();
    }

    private static async Task AutoriserBouclage(HttpClient admin, string valeur = "127.0.0.0/8") =>
        await AidesApi.Ok(await admin.PutAsJsonAsync("/api/admin/reglages/api-reseaux-autorises", new { valeur }));

    private static async Task<JsonElement> Essayer(HttpClient admin, string url) =>
        await AidesApi.Ok(await admin.PostAsJsonAsync("/api/admin/connexions/essai",
            new { brouillon = Saisie("Essai", url), connexionId = (Guid?)null, mepId = (Guid?)null, evenement = "etape.echec" }));

    private async Task<int> Traiter() => await _api.Services.GetRequiredService<EnvoiAppelsApi>().Traiter(CancellationToken.None);

    private async Task<List<AppelApi>> Appels(Guid connexionId)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        return await db.AppelsApi.AsNoTracking().Where(a => a.ConnexionId == connexionId).OrderBy(a => a.CreeLe).ToListAsync();
    }

    /// <summary>Les nouvelles tentatives programmées partent tout de suite (au lieu de 30 s, 2 min…).</summary>
    private async Task Avancer(Guid connexionId)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        await db.AppelsApi.Where(a => a.ConnexionId == connexionId && a.Etat == EtatAppel.EnAttente)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProchaineTentative, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    // ---------- Droits et secrets ----------

    [Fact]
    public async Task Console_reservee_aux_administrateurs()
    {
        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/connexions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/connexions", Saisie("x", "https://outil.example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/connexions/essai",
            new { brouillon = Saisie("x", "https://outil.example.com") })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/connexions/appels")).StatusCode);

        var alice = await _api.ClientConnecte("alice");
        var liste = await AidesApi.Ok(await alice.GetAsync("/api/admin/connexions"));
        Assert.Contains(liste.EnumerateArray(), c => c.GetProperty("nom").GetString() == "Canal Teams (démonstration)");
    }

    [Fact]
    public async Task Secrets_jamais_relus_et_effaces_quand_le_domaine_change()
    {
        var alice = await _api.ClientConnecte("alice");
        object[] entetes = [new { nom = "X-Cle", valeur = "", secret = true, valeurSecrete = "cle-tres-secrete-17" }];
        var rep = await alice.PostAsJsonAsync("/api/admin/connexions",
            Saisie("Tickets", "https://outil.example.com/changements", authentification: "Jeton", secret: "jeton-tres-secret-42", entetes: entetes));
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, rep.StatusCode);
        Assert.DoesNotContain("jeton-tres-secret-42", texte);
        Assert.DoesNotContain("cle-tres-secrete-17", texte);
        var cree = JsonDocument.Parse(texte).RootElement;
        var id = cree.GetProperty("id").GetGuid();
        Assert.True(cree.GetProperty("secret").GetProperty("enregistre").GetBoolean());
        Assert.True(cree.GetProperty("entetes")[0].GetProperty("valeurSecrete").GetProperty("enregistre").GetBoolean());

        foreach (var lu in new[] { await alice.GetStringAsync($"/api/admin/connexions/{id}"), await alice.GetStringAsync("/api/admin/connexions") })
        {
            Assert.DoesNotContain("jeton-tres-secret-42", lu);
            Assert.DoesNotContain("cle-tres-secrete-17", lu);
        }
        using (var scope = _api.Services.CreateScope())
        {
            var c = await scope.ServiceProvider.GetRequiredService<MepDbContext>().ConnexionsApi.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.DoesNotContain("jeton-tres-secret-42", c.Secret!.Chiffre); // chiffré en base
        }

        // Même domaine : les secrets laissés vides sont gardés.
        var v1 = await AidesApi.Ok(await alice.PutAsJsonAsync($"/api/admin/connexions/{id}",
            Saisie("Tickets", "https://outil.example.com/v2/changements", version: 1, authentification: "Jeton",
                entetes: [new { nom = "X-Cle", valeur = "", secret = true }])));
        Assert.True(v1.GetProperty("secret").GetProperty("enregistre").GetBoolean());

        // Autre domaine : les secrets enregistrés ne partent pas vers lui, il faut les ressaisir.
        var version = v1.GetProperty("version").GetInt32();
        var refus = await alice.PutAsJsonAsync($"/api/admin/connexions/{id}",
            Saisie("Tickets", "https://autre.example.net/changements", version: version, authentification: "Jeton",
                entetes: [new { nom = "X-Cle", valeur = "", secret = true }]));
        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        var v2 = await AidesApi.Ok(await alice.PutAsJsonAsync($"/api/admin/connexions/{id}",
            Saisie("Tickets", "https://autre.example.net/changements", version: version, authentification: "Jeton", secret: "nouveau-jeton-99",
                entetes: [new { nom = "X-Cle", valeur = "", secret = true, valeurSecrete = "nouvelle-cle-31" }])));
        Assert.Equal("https://autre.example.net/changements", v2.GetProperty("url").GetString());
        Assert.DoesNotContain("nouveau-jeton-99", v2.GetRawText());

        // Version périmée : 409.
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/admin/connexions/{id}",
            Saisie("Tickets", "https://autre.example.net/changements", version: version, authentification: "Jeton"))).StatusCode);

        // Le journal d'administration dit ce qui a changé, jamais la valeur.
        using (var scope = _api.Services.CreateScope())
        {
            var journal = await scope.ServiceProvider.GetRequiredService<MepDbContext>().JournalAdmin.AsNoTracking()
                .Where(e => e.Type.StartsWith("connexion-api")).Select(e => e.Detail).ToListAsync();
            Assert.NotEmpty(journal);
            Assert.All(journal, d => Assert.DoesNotContain("secret-", d));
        }
    }

    // ---------- Réseau sortant ----------

    [Fact]
    public async Task Adresses_internes_refusees_sauf_reseau_autorise()
    {
        var alice = await _api.ClientConnecte("alice");
        var refus = await Essayer(alice, $"{_outil.Adresse}/hook");
        Assert.False(refus.GetProperty("reussi").GetBoolean());
        Assert.Equal("adresse", refus.GetProperty("tentative").GetProperty("typeErreur").GetString());
        Assert.Empty(_outil.Recues);

        // Métadonnées des clouds : jamais, même en https.
        var metadonnees = await Essayer(alice, "https://169.254.169.254/latest/meta-data");
        Assert.Equal("adresse", metadonnees.GetProperty("tentative").GetProperty("typeErreur").GetString());

        await AutoriserBouclage(alice);
        var essai = await Essayer(alice, $"{_outil.Adresse}/hook");
        Assert.True(essai.GetProperty("reussi").GetBoolean(), essai.GetRawText());
        var recue = Assert.Single(_outil.Recues);
        Assert.Equal("POST", recue.Methode);
        Assert.Equal(essai.GetProperty("appelId").GetGuid().ToString(), recue.Entetes["X-Gestion-Mep-Appel"]);
        Assert.Contains("\"etape.echec\"", recue.Corps);
        Assert.False(recue.Entetes.ContainsKey("Cookie"));

        // L'autorisation ne vaut que pour le réseau donné : 169.254/16 reste refusé.
        metadonnees = await Essayer(alice, "https://169.254.169.254/latest/meta-data");
        Assert.Equal("adresse", metadonnees.GetProperty("tentative").GetProperty("typeErreur").GetString());
    }

    [Fact]
    public async Task Controle_sur_l_adresse_reellement_jointe()
    {
        var alice = await _api.ClientConnecte("alice");
        // Un nom qui pointe vers le réseau interne (changement d'adresse DNS après la saisie, par exemple) : refusé.
        _noms.Noms["outil.example.org"] = [IPAddress.Loopback];
        var refus = await Essayer(alice, $"https://outil.example.org:{_outil.Port}/hook");
        Assert.Equal("adresse", refus.GetProperty("tentative").GetProperty("typeErreur").GetString());
        Assert.Empty(_outil.Recues);

        // Le contrôle porte sur l'adresse résolue au moment de la connexion, et c'est elle qui est jointe.
        await AutoriserBouclage(alice);
        var essai = await Essayer(alice, $"http://outil.example.org:{_outil.Port}/hook");
        Assert.True(essai.GetProperty("reussi").GetBoolean(), essai.GetRawText());
        Assert.Equal($"outil.example.org:{_outil.Port}", Assert.Single(_outil.Recues).Entetes["Host"]);

        // HTTP hors réseau autorisé : refusé.
        _noms.Noms["public.example.org"] = [IPAddress.Parse("203.0.113.10")];
        var http = await Essayer(alice, "http://public.example.org/hook");
        Assert.Equal("adresse", http.GetProperty("tentative").GetProperty("typeErreur").GetString());
    }

    [Fact]
    public async Task Redirection_jamais_suivie()
    {
        var alice = await _api.ClientConnecte("alice");
        await AutoriserBouclage(alice);
        _outil.Repondre = ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = $"{_outil.Adresse}/ailleurs";
            return Task.CompletedTask;
        };
        var essai = await Essayer(alice, $"{_outil.Adresse}/hook");
        Assert.False(essai.GetProperty("reussi").GetBoolean());
        Assert.Equal(302, essai.GetProperty("tentative").GetProperty("code").GetInt32());
        Assert.Equal("redirection", essai.GetProperty("tentative").GetProperty("typeErreur").GetString());
        Assert.Equal("/hook", Assert.Single(_outil.Recues).Chemin);
    }

    // ---------- File d'envoi ----------

    [Fact]
    public async Task Evenement_de_la_MEP_met_l_appel_en_file_puis_il_part()
    {
        var alice = await _api.ClientConnecte("alice");
        await AutoriserBouclage(alice);
        var cid = await Creer(alice, Saisie("Outil local", $"{_outil.Adresse}/hook", ["gonogo.go"]));
        var mepId = await AidesApi.IdDemo(alice);

        // Action refusée : rien en file (l'appel s'enregistre dans la même transaction que l'événement).
        var refusee = await alice.PostAsJsonAsync($"/api/meps/{mepId}/phase", new { phase = "Execution", criteresCoches = Array.Empty<Guid>() });
        Assert.False(refusee.IsSuccessStatusCode);
        Assert.Empty(await Appels(cid));

        await AidesApi.Go(_api, alice, mepId);
        var appel = Assert.Single(await Appels(cid));
        Assert.Equal(EtatAppel.EnAttente, appel.Etat);
        Assert.Equal(OrigineAppel.Automatique, appel.Origine);
        Assert.Equal(mepId, appel.MepId);
        Assert.NotNull(appel.EvenementId);
        Assert.Empty(_outil.Recues);

        Assert.True(await Traiter() >= 1);
        var recue = Assert.Single(_outil.Recues);
        Assert.Contains("\"gonogo.go\"", recue.Corps);
        Assert.Contains(mepId.ToString(), recue.Corps);
        Assert.Equal(EtatAppel.Reussi, Assert.Single(await Appels(cid)).Etat);

        // La connexion de démonstration (même déclencheur) est arrivée au récepteur intégré.
        var demo = await AidesApi.Ok(await alice.GetAsync("/api/demo/recepteur"));
        Assert.Contains(demo.EnumerateArray(), m => m.GetProperty("canal").GetString() == "teams");

        // Une ligne au journal de la MEP, sans rien changer d'autre.
        var journal = await alice.GetStringAsync($"/api/meps/{mepId}/journal");
        Assert.Contains("Outil local", journal);

        // Appel resté « en cours » (serveur arrêté pendant l'envoi) : remis en file au démarrage, puis envoyé.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.AppelsApi.Where(a => a.ConnexionId == cid).ExecuteUpdateAsync(s => s.SetProperty(a => a.Etat, EtatAppel.EnCours));
        }
        Assert.Equal(0, await Traiter());
        Assert.True(await _api.Services.GetRequiredService<EnvoiAppelsApi>().Reprendre(CancellationToken.None) >= 1);
        await Traiter();
        Assert.Equal(2, _outil.Recues.Count);
        Assert.Equal(EtatAppel.Reussi, Assert.Single(await Appels(cid)).Etat);
    }

    [Fact]
    public async Task Nouvelles_tentatives_retry_after_et_ordre_des_appels()
    {
        var alice = await _api.ClientConnecte("alice");
        await AutoriserBouclage(alice);
        var cid = await Creer(alice, Saisie("Outil fragile", $"{_outil.Adresse}/hook", envoiManuel: true, tentatives: 3));
        var mepId = await AidesApi.IdDemo(alice);

        _outil.Repondre = Recepteur.Code(503, "120");
        var premier = (await AidesApi.Ok(await alice.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/envoyer", new { commentaire = "premier" })))
            .GetProperty("appelId").GetGuid();
        var avant = DateTimeOffset.UtcNow;
        await Traiter();
        var a = Assert.Single(await Appels(cid));
        Assert.Equal(EtatAppel.EnAttente, a.Etat);
        Assert.Equal(503, a.DernierCode);
        Assert.InRange((a.ProchaineTentative!.Value - avant).TotalSeconds, 100, 140); // Retry-After : 120 s

        // Un second envoi pour la même MEP attend que le premier soit terminé (ordre gardé).
        var second = (await AidesApi.Ok(await alice.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/envoyer", new { commentaire = "second" })))
            .GetProperty("appelId").GetGuid();
        await Avancer(cid);
        _outil.Repondre = Recepteur.Code(503, "86400");
        avant = DateTimeOffset.UtcNow;
        await Traiter();
        Assert.Equal(2, _outil.Recues.Count);
        Assert.All(_outil.Recues, r => Assert.Contains("premier", r.Corps));
        var p = (await Appels(cid)).Single(x => x.Id == premier);
        Assert.InRange((p.ProchaineTentative!.Value - avant).TotalMinutes, 9, 11); // Retry-After plafonné à 10 min

        // Troisième et dernière tentative : échec définitif, montré dans la MEP.
        await Avancer(cid);
        _outil.Repondre = Recepteur.Code(500);
        await Traiter();
        p = (await Appels(cid)).Single(x => x.Id == premier);
        Assert.Equal(EtatAppel.Echec, p.Etat);
        Assert.Equal(3, p.Tentatives.Count);
        var mep = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/connexions"));
        Assert.Equal(premier, Assert.Single(mep.GetProperty("echecs").EnumerateArray()).GetProperty("appelId").GetGuid());

        // Le second part ensuite ; sans Retry-After, la tentative suivante est à 30 s.
        avant = DateTimeOffset.UtcNow;
        await Traiter();
        var s = (await Appels(cid)).Single(x => x.Id == second);
        Assert.Equal(EtatAppel.EnAttente, s.Etat);
        Assert.InRange((s.ProchaineTentative!.Value - avant).TotalSeconds, 25, 40);
        Assert.Contains("second", _outil.Recues.Last().Corps);

        // Une erreur définitive (400) n'est pas réessayée.
        await Avancer(cid);
        _outil.Repondre = Recepteur.Code(400);
        await Traiter();
        s = (await Appels(cid)).Single(x => x.Id == second);
        Assert.Equal(EtatAppel.Echec, s.Etat);
        Assert.Equal(2, s.Tentatives.Count);

        // Renvoi : même message et même identifiant d'appel pour l'outil (doublons écartés), puis l'échec disparaît.
        _outil.Repondre = Recepteur.Code(200);
        var renvoi = (await AidesApi.Ok(await alice.PostAsync($"/api/meps/{mepId}/appels/{premier}/renvoyer", null))).GetProperty("appelId").GetGuid();
        Assert.NotEqual(premier, renvoi);
        await Traiter();
        var dernier = _outil.Recues.Last();
        Assert.Contains("premier", dernier.Corps);
        Assert.Equal(premier.ToString(), dernier.Entetes["X-Gestion-Mep-Appel"]);
        var r = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/appels/{renvoi}"));
        Assert.Equal("Reussi", r.GetProperty("etat").GetString());
        mep = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/connexions"));
        Assert.Empty(mep.GetProperty("echecs").EnumerateArray());
    }

    [Fact]
    public async Task Envoi_depuis_la_MEP_reserve_au_pilote_et_aux_administrateurs()
    {
        var alice = await _api.ClientConnecte("alice");
        var mepId = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/connexions"));
        Assert.True(mep.GetProperty("peutEnvoyer").GetBoolean());
        var teams = mep.GetProperty("envois").EnumerateArray().Single(c => c.GetProperty("nom").GetString() == "Canal Teams (démonstration)");
        var cid = teams.GetProperty("id").GetGuid();
        Assert.False(teams.TryGetProperty("url", out _)); // ni adresse ni réglage côté MEP

        var dora = await _api.NouveauCompte("dora", "Dora");
        var vu = await AidesApi.Ok(await dora.GetAsync($"/api/meps/{mepId}/connexions"));
        Assert.False(vu.GetProperty("peutEnvoyer").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await dora.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/envoyer", new { commentaire = "" })).StatusCode);

        // Aperçu pour le pilote : le message, sans l'adresse ni les en-têtes.
        var apercu = await AidesApi.Ok(await alice.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/apercu", new { commentaire = "Fenêtre décalée" }));
        Assert.Contains("Fenêtre décalée", apercu.GetProperty("corps").GetString());

        var envoi = await alice.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/envoyer", new { commentaire = "Fenêtre décalée" });
        Assert.Equal(HttpStatusCode.Accepted, envoi.StatusCode);
        var appelId = (await AidesApi.Ok(envoi)).GetProperty("appelId").GetGuid();
        await Traiter();
        var appel = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/appels/{appelId}"));
        Assert.Equal("Reussi", appel.GetProperty("etat").GetString());
        Assert.Equal("Manuel", appel.GetProperty("origine").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await dora.PostAsync($"/api/meps/{mepId}/appels/{appelId}/renvoyer", null)).StatusCode);

        // Connexion désactivée : plus proposée, plus envoyable.
        await AidesApi.Ok(await alice.PostAsync($"/api/admin/connexions/{cid}/desactiver", null));
        mep = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{mepId}/connexions"));
        Assert.DoesNotContain(mep.GetProperty("envois").EnumerateArray(), c => c.GetProperty("id").GetGuid() == cid);
        Assert.False((await alice.PostAsJsonAsync($"/api/meps/{mepId}/connexions/{cid}/envoyer", new { commentaire = "" })).IsSuccessStatusCode);
    }

    // ---------- Ligne de commande ----------

    private static async Task<(int Code, string Sortie, string Erreur)> Cli(string? entree, params string[] args)
    {
        var sortie = new StringWriter();
        var erreur = new StringWriter();
        var (avant, avantErreur, avantEntree) = (Console.Out, Console.Error, Console.In);
        Console.SetOut(sortie);
        Console.SetError(erreur);
        if (entree is not null) Console.SetIn(new StringReader(entree));
        try
        {
            return (await LigneDeCommande.Executer(args), sortie.ToString(), erreur.ToString());
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
            Console.SetIn(avantEntree);
        }
    }

    [Fact]
    public async Task Export_sans_secret_import_desactive_puis_secret_par_l_entree_standard()
    {
        var alice = await _api.ClientConnecte("alice");
        await Creer(alice, Saisie("Tickets", "https://outil.example.com/changements", ["etape.echec"], authentification: "Jeton", secret: "jeton-tres-secret-42"));
        var fichier = Path.Combine(_api.DossierPieces, "connexions.json");
        Directory.CreateDirectory(_api.DossierPieces);

        var export = await Cli(null, "connexions", "exporter", "Tickets", fichier);
        Assert.Equal(0, export.Code);
        var texte = await File.ReadAllTextAsync(fichier);
        Assert.DoesNotContain("jeton-tres-secret-42", texte);
        Assert.Contains(LigneDeCommande.SecretASaisir, texte);

        // Même nom : refusé sans --remplacer ni --ignorer-existantes ; --essai n'enregistre rien.
        Assert.Equal(1, (await Cli(null, "connexions", "importer", fichier)).Code);
        await File.WriteAllTextAsync(fichier, texte.Replace("\"Tickets\"", "\"Tickets (recette)\""));
        Assert.Equal(0, (await Cli(null, "connexions", "importer", fichier, "--essai")).Code);
        Assert.DoesNotContain("Tickets (recette)", await alice.GetStringAsync("/api/admin/connexions"));

        // Import : créée désactivée, la commande dit quel secret saisir.
        var import = await Cli(null, "connexions", "importer", fichier, "--json");
        Assert.Equal(0, import.Code);
        Assert.Contains("authentification", import.Sortie);
        var importee = (await AidesApi.Ok(await alice.GetAsync("/api/admin/connexions"))).EnumerateArray()
            .Single(c => c.GetProperty("nom").GetString() == "Tickets (recette)");
        Assert.False(importee.GetProperty("active").GetBoolean());
        var id = importee.GetProperty("id").GetGuid();
        Assert.False((await alice.PostAsync($"/api/admin/connexions/{id}/activer", null)).IsSuccessStatusCode);

        // Le secret ne passe jamais en argument.
        Assert.Equal(2, (await Cli(null, "connexions", "secret", "Tickets (recette)", "--champ", "authentification", "jeton-en-clair")).Code);
        Assert.Equal(0, (await Cli("jeton-de-recette-77\n", "connexions", "secret", "Tickets (recette)", "--champ", "authentification", "--secret-stdin", "--non-interactif")).Code);
        var lue = await AidesApi.Ok(await alice.GetAsync($"/api/admin/connexions/{id}"));
        Assert.True(lue.GetProperty("secret").GetProperty("enregistre").GetBoolean());
        Assert.DoesNotContain("jeton-de-recette-77", lue.GetRawText());

        Assert.Equal(0, (await Cli(null, "connexions", "activer", "Tickets (recette)")).Code);
        var afficher = await Cli(null, "connexions", "afficher", "Tickets (recette)");
        Assert.Equal(0, afficher.Code);
        Assert.DoesNotContain("jeton-de-recette-77", afficher.Sortie);
    }
}
