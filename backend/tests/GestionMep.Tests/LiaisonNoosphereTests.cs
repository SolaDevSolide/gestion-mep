using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>Horloge de test : l'heure réelle décalée, pour faire passer l'anti-rebond, les nouveaux essais et le délai de grâce.</summary>
public sealed class HorlogeTest : TimeProvider
{
    public TimeSpan Decalage { get; set; }
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Decalage;
}

/// <summary>
/// Liaison avec Noosphere (#123) de bout en bout, contre un Noosphere en mémoire (<see cref="PairNoosphereFactice"/>) :
/// appairage et confirmation, synchronisation du catalogue (ETag, avis), suivi des applications, événements de déploiement
/// (anti-rebond, nouvel essai), refus des requêtes signées, renouvellement des clés, coupure, appairage au démarrage,
/// démonstration et ligne de commande.
/// </summary>
[Collection("Api")]
public class LiaisonNoosphereTests : IDisposable
{
    private readonly PairNoosphereFactice _pair = new();
    private readonly HorlogeTest _horloge = new();
    private readonly ApiFixture _api;

    public LiaisonNoosphereTests()
    {
        _pair.Horloge = _horloge.GetUtcNow;
        _api = new ApiFixture(hote: b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton(new TransportLiaison(_pair));
            s.AddSingleton<TimeProvider>(_horloge);
        }));
    }

    public void Dispose()
    {
        _api.Dispose();
        _pair.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private async Task<T> Lire<T>(Func<MepDbContext, Task<T>> f)
    {
        using var scope = _api.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<MepDbContext>());
    }

    private Task<Liaison> Liaison() => Lire(db => db.Liaisons.AsNoTracking().FirstAsync(l => l.Id == _pair.LinkId));

    private TachesLiaisons Taches => _api.Services.GetRequiredService<TachesLiaisons>();

    private async Task<HttpClient> Appairer(bool confirmer = true)
    {
        var admin = await _api.ClientConnecte("alice");
        var rep = await admin.PostAsJsonAsync("/api/admin/liaisons/appairer", new { url = PairNoosphereFactice.Url + "/", code = _pair.Code.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Created, rep.StatusCode);
        if (confirmer)
        {
            (await admin.PostAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/confirmer", new { empreinte = _pair.Empreinte().ToUpperInvariant() })).EnsureSuccessStatusCode();
            _pair.Confirme = true;
        }
        return admin;
    }

    private async Task<HttpResponseMessage> Envoyer(HttpRequestMessage r) => await _api.ClientSansEnTete().SendAsync(r);

    private static async Task<string> Raison(HttpResponseMessage r)
    {
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("type").GetString()!["https://noosphere.dev/problems/".Length..];
    }

    // ---------- Appairage, confirmation, catalogue ----------

    [Fact]
    public async Task Appairage_confirmation_suivi_et_synchronisation_du_catalogue()
    {
        var admin = await _api.ClientConnecte("alice");
        // Code faux : 404 côté Noosphere, message clair, rien n'est créé.
        var faux = await admin.PostAsJsonAsync("/api/admin/liaisons/appairer", new { url = PairNoosphereFactice.Url, code = "7K4Q-AAAA-BBBB" });
        Assert.Equal(HttpStatusCode.NotFound, faux.StatusCode);
        Assert.Contains("Code inconnu ou expiré", await faux.Content.ReadAsStringAsync());

        var rep = await admin.PostAsJsonAsync("/api/admin/liaisons/appairer", new { url = PairNoosphereFactice.Url, code = "7k4q 29xd m3pa" });
        Assert.Equal(HttpStatusCode.Created, rep.StatusCode);
        var dto = (await rep.Content.ReadFromJsonAsync<LiaisonDto>(Web))!;
        Assert.Equal(_pair.LinkId, dto.Id);
        Assert.Equal(EtatLiaison.EnAttente, dto.Etat);
        Assert.Equal(_pair.Empreinte(), dto.Empreinte);
        Assert.Equal(["catalog:read", "notices:send", "deployments:receive"], dto.Capacites);
        // Code déjà utilisé : 409.
        var encore = await admin.PostAsJsonAsync("/api/admin/liaisons/appairer", new { url = "https://autre.test", code = _pair.Code });
        Assert.Equal(HttpStatusCode.Conflict, encore.StatusCode);

        // Clés privées chiffrées au repos (jamais en clair dans la base).
        var l = await Liaison();
        Assert.DoesNotContain("MIG", l.Cles!.SignaturePrivee, StringComparison.Ordinal);
        Assert.StartsWith("CfDJ8", l.Cles.SignaturePrivee, StringComparison.Ordinal);

        // Tant que l'empreinte n'est pas confirmée ici : /status répond (confirmed: false), les avis sont refusés (403, signé).
        var avant = await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status"));
        Assert.Equal(HttpStatusCode.OK, avant.StatusCode);
        Assert.False(JsonSerializer.Deserialize<StatutLiaison>(await _pair.LireReponse(avant))!.Confirmee);
        var statut = await Envoyer(_pair.Requete(HttpMethod.Post, "/link/v1/notices", new AvisNoosphere([])));
        Assert.Equal(HttpStatusCode.Forbidden, statut.StatusCode);
        Assert.True(statut.Headers.Contains("Signature"));
        Assert.Equal(RefusLiaison.NonConfirmee, await Raison(statut));

        var mauvaise = await admin.PostAsJsonAsync($"/api/admin/liaisons/{dto.Id}/confirmer", new { empreinte = "abbey acorn 0000 zinc" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mauvaise.StatusCode);
        var confirmee = (await (await admin.PostAsJsonAsync($"/api/admin/liaisons/{dto.Id}/confirmer", new { empreinte = "  " + _pair.Empreinte().ToUpperInvariant() }))
            .Content.ReadFromJsonAsync<LiaisonDto>(Web))!;
        Assert.Equal(EtatLiaison.Active, confirmee.Etat);
        Assert.True(confirmee.ConfirmeeIci);

        // Noosphere n'a pas encore confirmé : la synchronisation échoue (502) et l'état le dit.
        var refusee = await admin.PostAsync($"/api/admin/liaisons/{dto.Id}/synchroniser", null);
        Assert.Equal(HttpStatusCode.BadGateway, refusee.StatusCode);
        Assert.False((await Liaison()).ConfirmeeParPair);

        // État servi à Noosphere : signé, chiffré, avec la confirmation de ce côté.
        var etat = await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status"));
        Assert.Equal(HttpStatusCode.OK, etat.StatusCode);
        Assert.Equal("application/jose", etat.Content.Headers.ContentType!.MediaType);
        var s = JsonSerializer.Deserialize<StatutLiaison>(await _pair.LireReponse(etat))!;
        Assert.Equal(_pair.LinkId, s.LiaisonId);
        Assert.Equal("gestion-mep", s.Produit);
        Assert.True(s.Confirmee);

        _pair.Confirme = true;
        var synchro = (await (await admin.PostAsync($"/api/admin/liaisons/{dto.Id}/synchroniser", null)).Content.ReadFromJsonAsync<SynchroLiaisonDto>(Web))!;
        Assert.False(synchro.Inchange);
        Assert.True(synchro.Liaison.ConfirmeeParNoosphere);
        Assert.Equal(0, synchro.Liaison.SystemesSuivis);

        // Applications proposées, avec les systèmes locaux de même nom.
        var catalogue = (await admin.GetFromJsonAsync<CatalogueLiaisonDto>($"/api/admin/liaisons/{dto.Id}/catalogue", Web))!;
        Assert.Equal(3, catalogue.Applications.Count);
        var bdd = catalogue.Applications.Single(a => a.Id == "bdd");
        Assert.False(bdd.Suivie);
        Assert.Equal("Socle", bdd.Domaine);
        Assert.Equal("Équipe Socle", bdd.Equipe);
        Assert.Contains(bdd.Suggestions, x => x.Nom == "BDD" && x.MemeNom);

        // Suivre le domaine Socle (BDD et LDAP rattachés aux systèmes existants) et Facturation une par une (système créé).
        var suivi = await admin.PutAsJsonAsync($"/api/admin/liaisons/{dto.Id}/suivi", new
        {
            domainesSuivis = new[] { "socle" }, applications = new[] { new { applicationId = "billing", suivre = true } },
        });
        Assert.Equal(HttpStatusCode.OK, suivi.StatusCode);
        var apres = (await suivi.Content.ReadFromJsonAsync<CatalogueLiaisonDto>(Web))!;
        Assert.All(apres.Applications, a => Assert.True(a.Suivie));
        var systemes = await Lire(db => db.SystemesCatalogue.AsNoTracking().Where(x => x.Source != null).ToListAsync());
        var sysBdd = systemes.Single(x => x.Source!.ApplicationId == "bdd");
        Assert.Equal("BDD", sysBdd.Nom);
        Assert.Equal("Base de données (Noosphere)", sysBdd.Description);
        Assert.Equal($"{PairNoosphereFactice.Url}/applications/bdd", sysBdd.Source!.Url);
        Assert.NotEmpty(sysBdd.Environnements); // les environnements restent locaux
        var factu = systemes.Single(x => x.Source!.ApplicationId == "billing");
        Assert.Equal("Facturation", factu.Nom);
        Assert.Equal("Finance", factu.Source!.Domaine);

        // Fiche « Géré par Noosphere » : nom et description en lecture seule, suppression refusée.
        var fiche = (await admin.GetFromJsonAsync<List<SystemeCatalogueDto>>("/api/catalogue/systemes", Web))!.Single(x => x.Id == sysBdd.Id);
        Assert.NotNull(fiche.Source);
        Assert.Equal("bdd", fiche.Source.ApplicationId);
        var renommer = await admin.PutAsJsonAsync($"/api/catalogue/systemes/{sysBdd.Id}", new { nom = "Autre nom", description = sysBdd.Description, cible = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, renommer.StatusCode);
        Assert.Contains("géré par Noosphere", await renommer.Content.ReadAsStringAsync());
        var cible = await admin.PutAsJsonAsync($"/api/catalogue/systemes/{sysBdd.Id}", new { nom = "BDD", description = sysBdd.Description, cible = "Serveur local" });
        Assert.Equal(HttpStatusCode.OK, cible.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/catalogue/systemes/{sysBdd.Id}")).StatusCode);

        // Même ETag : 304, rien ne change.
        var inchange = (await (await admin.PostAsync($"/api/admin/liaisons/{dto.Id}/synchroniser", null)).Content.ReadFromJsonAsync<SynchroLiaisonDto>(Web))!;
        Assert.True(inchange.Inchange);

        // Noosphere change : Facturation retirée, LDAP devenue restreinte (absente), BDD renommée ; il poste un avis.
        _pair.Catalogue = _pair.Catalogue with
        {
            Applications =
            [
                _pair.Catalogue.Applications![0] with { Nom = "Base clients" },
                _pair.Catalogue.Applications[2] with { Cycle = "retired" },
            ],
        };
        _pair.Etag = "\"v2\"";
        var avis = await Envoyer(_pair.Requete(HttpMethod.Post, "/link/v1/notices", new AvisNoosphere(
            [new("application", "billing", "upsert", "2026-10-31T12:00:00Z"), new("application", "ldap", "delete", "2026-10-31T12:00:00Z")])));
        Assert.Equal(HttpStatusCode.Accepted, avis.StatusCode);
        Assert.Empty(await _pair.LireReponse(avis));
        // Relu peu après (2 s) par les tâches de fond.
        _horloge.Decalage = TimeSpan.FromSeconds(3);
        await Taches.Passage(CancellationToken.None);
        systemes = await Lire(db => db.SystemesCatalogue.AsNoTracking().Where(x => x.Source != null).ToListAsync());
        Assert.Equal("Base clients", systemes.Single(x => x.Source!.ApplicationId == "bdd").Nom);
        var retiree = systemes.Single(x => x.Source!.ApplicationId == "billing");
        Assert.False(retiree.Actif);
        Assert.Equal("retired", retiree.Source!.Cycle);
        var absente = systemes.Single(x => x.Source!.ApplicationId == "ldap");
        Assert.False(absente.Actif);
        Assert.True(absente.Source!.Absente);
        Assert.Equal(3, systemes.Count(x => x.Source!.LiaisonId == _pair.LinkId));

        // Liste et journal des échanges.
        var liste = (await admin.GetFromJsonAsync<List<LiaisonDto>>("/api/admin/liaisons", Web))!;
        Assert.Contains(liste, x => x.Id == dto.Id && x.SystemesSuivis == 3);
        var echanges = (await admin.GetFromJsonAsync<List<EchangeLiaisonDto>>($"/api/admin/liaisons/{dto.Id}/echanges", Web))!;
        Assert.Contains(echanges, e => e.Operation == "pair" && e.Resultat == "ok");
        Assert.Contains(echanges, e => e.Operation == "notices" && e.Sens == "entrant" && e.Resultat == "ok");
        Assert.Contains(echanges, e => e.Operation == "catalog" && e.Code == 304);
        Assert.Contains(echanges, e => e.Operation == "notices" && e.Resultat == "refuse" && e.Detail.Contains("link-not-confirmed"));
        // Réservé aux administrateurs.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await _api.ClientConnecte("bob")).GetAsync("/api/admin/liaisons")).StatusCode);
    }

    [Fact]
    public async Task Reponse_de_Noosphere_mal_signee_refusee_et_journalisee()
    {
        var admin = await Appairer();
        _pair.SignerReponsesFaux = true;
        var rep = await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null);
        Assert.Equal(HttpStatusCode.BadGateway, rep.StatusCode);
        Assert.Contains("signature invalide", await rep.Content.ReadAsStringAsync());
        var echanges = await Lire(db => db.EchangesLiaison.Where(e => e.LiaisonId == _pair.LinkId && e.Resultat == "echec").ToListAsync());
        Assert.Contains(echanges, e => e.Operation == "status" && e.Detail.Contains("signature invalide"));
        Assert.Contains("signature invalide", (await Liaison()).DerniereErreur);
    }

    // ---------- Refus des requêtes signées ----------

    [Fact]
    public async Task Chaque_raison_de_refus_est_repondue_et_journalisee()
    {
        await Appairer();
        async Task Refus(HttpRequestMessage r, string raison, HttpStatusCode code = HttpStatusCode.Unauthorized, bool signee = true)
        {
            var rep = await Envoyer(r);
            Assert.Equal(code, rep.StatusCode);
            Assert.Equal("application/problem+json", rep.Content.Headers.ContentType!.MediaType);
            Assert.Equal(raison, await Raison(rep));
            // Refus signés, sauf unknown-key et link-cut (et une requête sans signature : liaison inconnue).
            if (signee) await _pair.LireReponse(rep);
            else Assert.False(rep.Headers.Contains("Signature"));
        }
        var avis = new AvisNoosphere([new("team", "t1", "upsert", "2026-10-31T12:00:00Z")]);

        // Requête valide d'abord.
        Assert.Equal(HttpStatusCode.Accepted, (await Envoyer(_pair.Requete(HttpMethod.Post, "/link/v1/notices", avis))).StatusCode);

        using var inconnue = CryptoLiaison.NouvelleCleSignature();
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Cle: inconnue)), RefusLiaison.CleInconnue, signee: false);
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(DecalageSecondes: -301)), RefusLiaison.Horloge);
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(DecalageSecondes: 400)), RefusLiaison.Horloge);
        await Refus(_pair.Requete(HttpMethod.Post, "/link/v1/notices", avis, new(DigestFaux: true)), RefusLiaison.Empreinte);
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(SignatureFausse: true)), RefusLiaison.Signature);
        // Chemin signé différent de celui reçu : la signature ne couvre pas cette requête.
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Chemin: "/link/v1/catalog")), RefusLiaison.Signature);
        var sansSignature = new HttpRequestMessage(HttpMethod.Get, "/link/v1/status");
        await Refus(sansSignature, RefusLiaison.Signature, signee: false);
        // Corps illisible : 400 bad-body, signé.
        await Refus(_pair.Requete(HttpMethod.Post, "/link/v1/notices", avis, new(CorpsBrut: "pas.un.jwe")), RefusLiaison.CorpsInvalide, HttpStatusCode.BadRequest);
        // Rejeu : même nonce dans les 10 minutes.
        Assert.Equal(HttpStatusCode.OK, (await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Nonce: "bm9uY2UtcmVqZXU")))).StatusCode);
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Nonce: "bm9uY2UtcmVqZXU")), RefusLiaison.Rejeu);
        // Non confirmée ici : 403.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.Liaisons.Where(l => l.Id == _pair.LinkId).ExecuteUpdateAsync(x => x.SetProperty(l => l.ConfirmeeIci, false));
        }
        await Refus(_pair.Requete(HttpMethod.Post, "/link/v1/notices", avis), RefusLiaison.NonConfirmee, HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status"))).StatusCode);
        // Capacité notices:send absente : 403 capability-missing.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var l = await db.Liaisons.SingleAsync(x => x.Id == _pair.LinkId);
            l.ConfirmeeIci = true;
            l.Capacites = [CapacitesLiaison.LectureCatalogue];
            await db.SaveChangesAsync();
        }
        await Refus(_pair.Requete(HttpMethod.Post, "/link/v1/notices", avis), RefusLiaison.CapaciteManquante, HttpStatusCode.Forbidden);
        // Coupée : link-cut.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.Liaisons.Where(l => l.Id == _pair.LinkId).ExecuteUpdateAsync(x => x.SetProperty(l => l.Etat, EtatLiaison.Coupee));
        }
        await Refus(_pair.Requete(HttpMethod.Get, "/link/v1/status"), RefusLiaison.LiaisonCoupee, signee: false);

        var refus = await Lire(db => db.EchangesLiaison.Where(e => e.Resultat == "refuse").ToListAsync());
        foreach (var raison in RefusLiaison.Toutes) Assert.Contains(refus, e => e.Detail.Contains($"({raison})", StringComparison.Ordinal));
        Assert.Contains(refus, e => e.LiaisonId == null && e.Detail.Contains("unknown-key"));
        // Hors /api : pas d'en-tête anti-CSRF demandé, et une route inconnue de /link/v1 répond 404 en JSON.
        Assert.Equal(HttpStatusCode.NotFound, (await _api.ClientSansEnTete().PostAsync("/link/v1/pair", new StringContent("{}"))).StatusCode);
        var admin = await _api.ClientConnecte("alice");
        var journal = (await admin.GetFromJsonAsync<List<EchangeLiaisonDto>>("/api/admin/liaisons/echanges?refus=true&limite=100", Web))!;
        Assert.All(journal, e => Assert.Equal("refuse", e.Resultat));
        Assert.True(journal.Count >= 9);
    }

    // ---------- Événements de déploiement ----------

    [Fact]
    public async Task Evenements_de_deploiement_rapprochement_anti_rebond_et_nouvel_essai()
    {
        var admin = await Appairer();
        (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/suivi", new { domainesSuivis = new[] { "socle" } })).EnsureSuccessStatusCode();
        var bdd = await Lire(db => db.SystemesCatalogue.AsNoTracking().SingleAsync(s => s.Nom == "BDD"));
        // La liaison fictive de la démonstration n'envoie rien ici (ses envois « demo » fausseraient les comptes).
        await Lire(db => db.Liaisons.Where(l => l.Id != _pair.LinkId).ExecuteUpdateAsync(x => x.SetProperty(l => l.EnvoyerMeps, false)));

        // Rapprochement : toutes les MEP qui touchent BDD ou LDAP (hors modèles, hors MEP terminées depuis plus de 90 jours).
        Assert.True(await Taches.EnvoyerDeploiements(true, CancellationToken.None) > 0);
        var meps = await Lire(db => db.Meps.AsNoTracking().Include(m => m.Systemes).Where(m => !m.EstModele && m.Systemes.Any(s => s.CatalogueId == bdd.Id)).ToListAsync());
        Assert.NotEmpty(_pair.Deploiements);
        foreach (var e in _pair.Deploiements)
        {
            var m = meps.SingleOrDefault(x => x.Id == e.DeploiementId);
            if (m is null) continue;
            Assert.Equal(SynchroLiaison.PhaseContrat(m.Phase), e.Phase);
            Assert.Contains("bdd", e.ApplicationIds);
            Assert.DoesNotContain(e.ApplicationIds, a => a is not ("bdd" or "ldap"));
            Assert.EndsWith($"/meps/{m.Id}", e.Url, StringComparison.Ordinal);
            Assert.Equal(m.Phase == Phase.Cloturee, e.Issue is not null);
            var envs = m.Systemes.Where(s => s.CatalogueId == bdd.Id).SelectMany(s => s.Environnements).Select(x => x.Nom).ToHashSet();
            Assert.All(envs, n => Assert.Contains(e.Environnements, x => x.Nom == n));
        }
        var appels = _pair.AppelsDeploiements;
        // Rien n'a changé : un nouveau rapprochement n'envoie rien.
        Assert.Equal(0, await Taches.EnvoyerDeploiements(true, CancellationToken.None));
        Assert.Equal(appels, _pair.AppelsDeploiements);

        // File vidée des signaux de l'amorçage (MEP de démonstration écrites au démarrage).
        _api.Services.GetRequiredService<SignalLiaisons>().Deploiements.Prendre(DateTimeOffset.MaxValue);
        // Deux modifications d'une MEP : un seul envoi, 30 s après la première.
        var mep = meps.First(m => m.Phase == Phase.Preparation && m.Report == null);
        async Task Annoncer(DateOnly jour, Guid? id = null)
        {
            using var scope = _api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var m = await db.Meps.FirstAsync(x => x.Id == (id ?? mep.Id));
            m.Communication.DateAnnoncee = jour;
            m.Communication.HeureDebutAnnoncee = new TimeOnly(20, 0);
            m.Communication.HeureFinAnnoncee = new TimeOnly(22, 30);
            m.FuseauHoraire = "Europe/Paris";
            m.Version++;
            await db.SaveChangesAsync();
        }
        await Annoncer(new DateOnly(2026, 11, 12));
        _horloge.Decalage = TimeSpan.FromSeconds(10);
        await Annoncer(new DateOnly(2026, 11, 13));
        Assert.Equal(0, await Taches.EnvoyerDeploiements(false, CancellationToken.None));
        Assert.Equal(appels, _pair.AppelsDeploiements);

        // Noosphere indisponible au premier envoi : nouvel essai 30 s plus tard.
        _pair.EchecsDeploiementsAVenir = 1;
        _horloge.Decalage = TimeSpan.FromSeconds(31);
        Assert.Equal(0, await Taches.EnvoyerDeploiements(false, CancellationToken.None));
        Assert.Equal(appels + 1, _pair.AppelsDeploiements);
        _horloge.Decalage = TimeSpan.FromSeconds(50);
        Assert.Equal(0, await Taches.EnvoyerDeploiements(false, CancellationToken.None));
        _horloge.Decalage = TimeSpan.FromSeconds(62);
        Assert.Equal(1, await Taches.EnvoyerDeploiements(false, CancellationToken.None));
        Assert.Equal(appels + 2, _pair.AppelsDeploiements);
        var evenement = _pair.Deploiements.Single(e => e.DeploiementId == mep.Id);
        Assert.Equal("2026-11-13T19:00:00Z", evenement.DebutPrevu);
        Assert.Equal("2026-11-13T21:30:00Z", evenement.FinPrevue);
        Assert.Equal("preparation", evenement.Phase);
        var echecs = await Lire(db => db.EchangesLiaison.Where(e => e.Operation == "deployments" && e.Resultat == "echec").CountAsync());
        Assert.Equal(1, echecs);

        // MEP supprimée : annoncée « cancelled ».
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            db.Meps.Remove(await db.Meps.FirstAsync(x => x.Id == mep.Id));
            await db.SaveChangesAsync();
        }
        _horloge.Decalage = TimeSpan.FromSeconds(100);
        Assert.Equal(1, await Taches.EnvoyerDeploiements(false, CancellationToken.None));
        Assert.Equal("cancelled", _pair.Deploiements.Single(e => e.DeploiementId == mep.Id).Phase);

        // Interrupteur « envoyer les MEP » coupé : plus rien ne part.
        (await admin.PutAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/options", new { envoyerMeps = false })).EnsureSuccessStatusCode();
        await Annoncer(new DateOnly(2026, 11, 20), meps.First(m => m.Id != mep.Id).Id);
        var avant = _pair.AppelsDeploiements;
        Assert.Equal(0, await Taches.EnvoyerDeploiements(true, CancellationToken.None));
        Assert.Equal(avant, _pair.AppelsDeploiements);
    }

    // ---------- Renouvellement des clés et coupure ----------

    [Fact]
    public async Task Domaine_suivi_et_rattachement_choisi_dans_la_meme_saisie_sans_systeme_en_double()
    {
        // Essai d'interopérabilité : suivre le domaine Finance et, dans la même saisie, rattacher Facturation à un système
        // local de nom proche créait d'abord un système « Facturation », laissé ensuite comme système local en double, et le
        // système rattaché gardait son nom local (nom déjà pris).
        var admin = await Appairer();
        var local = (await (await admin.PostAsJsonAsync("/api/catalogue/systemes", new { nom = "Factu", description = "locale", cible = "srv" }))
            .Content.ReadFromJsonAsync<SystemeCatalogueDto>(Web))!;
        (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null)).EnsureSuccessStatusCode();
        var avant = await Lire(db => db.SystemesCatalogue.CountAsync());

        var suivi = await admin.PutAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/suivi", new
        {
            domainesSuivis = new[] { "finance", "socle" },
            applications = new object[]
            {
                new { applicationId = "billing", suivre = true, systemeId = local.Id },
                new { applicationId = "ldap", suivre = false },
            },
        });
        Assert.Equal(HttpStatusCode.OK, suivi.StatusCode);
        var systemes = await Lire(db => db.SystemesCatalogue.AsNoTracking().ToListAsync());
        // Facturation : rattachée au système choisi, qui prend le nom et la description de Noosphere ; aucun autre créé.
        var rattache = systemes.Single(x => x.Id == local.Id);
        Assert.Equal("billing", rattache.Source!.ApplicationId);
        Assert.Equal("Facturation", rattache.Nom);
        Assert.Equal("Factures", rattache.Description);
        Assert.Single(systemes, x => x.Nom.StartsWith("Facturation", StringComparison.OrdinalIgnoreCase));
        // LDAP, écartée une par une : pas suivie, et aucun système créé pour elle par le domaine.
        Assert.DoesNotContain(systemes, x => x.Source?.ApplicationId == "ldap");
        Assert.Equal(avant, systemes.Count);
        Assert.Contains(systemes, x => x.Source?.ApplicationId == "bdd");
    }

    [Fact]
    public async Task Renouvellement_des_cles_dans_les_deux_sens_avec_delai_de_grace()
    {
        var admin = await Appairer();
        var empreinte = (await Liaison()).Empreinte;

        // Noosphere renouvelle : demande signée avec l'ancienne clé, réponse signée, nouvelle empreinte.
        var rep = await Envoyer(_pair.Renouveler());
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        await _pair.LireReponse(rep);
        var l = await Liaison();
        Assert.NotEqual(empreinte, l.Empreinte);
        Assert.Equal(_pair.Empreinte(), l.Empreinte);
        Assert.NotNull(l.ClesRenouveleesLe);
        Assert.True(l.ConfirmeeIci); // pas de nouvelle confirmation
        // Nouvelle clé acceptée ; l'ancienne encore 5 minutes, puis refusée (clé inconnue).
        Assert.Equal(HttpStatusCode.OK, (await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Cle: _pair.AncienneSignature)))).StatusCode);
        // Un renouvellement signé avec l'ancienne clé (encore acceptée) est refusé : il se signe avec la clé en cours.
        var parAncienne = await Envoyer(_pair.Requete(HttpMethod.Post, "/link/v1/rekey", new NouvellesCles(l.ClesPair!.Signature, l.ClesPair.Chiffrement),
            new(Cle: _pair.AncienneSignature)));
        Assert.Equal(HttpStatusCode.Unauthorized, parAncienne.StatusCode);
        Assert.Equal(RefusLiaison.Signature, await Raison(parAncienne));
        _horloge.Decalage = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(5);
        var tard = await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status", null, new(Cle: _pair.AncienneSignature)));
        Assert.Equal(HttpStatusCode.Unauthorized, tard.StatusCode);
        Assert.Equal(RefusLiaison.CleInconnue, await Raison(tard));
        // Le catalogue se lit avec les nouvelles clés de Noosphere.
        (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null)).EnsureSuccessStatusCode();

        // gestion-mep renouvelle à son tour : Noosphere reçoit les nouvelles clés, l'empreinte change encore.
        var avant = (await Liaison()).Empreinte;
        var renouvelee = (await (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/renouveler", null)).Content.ReadFromJsonAsync<LiaisonDto>(Web))!;
        Assert.NotEqual(avant, renouvelee.Empreinte);
        Assert.Equal(_pair.Empreinte(), renouvelee.Empreinte);
        Assert.Contains("POST /link/v1/rekey", _pair.Recus);
        // Les requêtes suivantes partent signées avec la nouvelle clé (Noosphere ne connaît plus que celle-là).
        (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null)).EnsureSuccessStatusCode();
        // Les réponses de gestion-mep aussi.
        await _pair.LireReponse(await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status")));
    }

    [Fact]
    public async Task Coupure_par_Noosphere_puis_par_la_console()
    {
        var admin = await Appairer();
        (await admin.PostAsync($"/api/admin/liaisons/{_pair.LinkId}/synchroniser", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/suivi", new { domainesSuivis = new[] { "socle" } })).EnsureSuccessStatusCode();
        Assert.Equal(2, await Lire(db => db.SystemesCatalogue.CountAsync(s => s.Source != null && s.Nom != "API" && s.Nom != "WEB")));

        var rep = await Envoyer(_pair.Requete(HttpMethod.Post, "/link/v1/unpair"));
        Assert.Equal(HttpStatusCode.NoContent, rep.StatusCode);
        await _pair.LireReponse(rep); // signée avant l'effacement des clés
        var l = await Liaison();
        Assert.Equal(EtatLiaison.Coupee, l.Etat);
        Assert.Null(l.Cles);
        Assert.Null(l.ClesPair);
        Assert.Equal("Noosphere", l.CoupeePar);
        // Les systèmes restent, redevenus locaux.
        var bdd = await Lire(db => db.SystemesCatalogue.AsNoTracking().SingleAsync(s => s.Nom == "BDD"));
        Assert.Null(bdd.Source);
        Assert.True(bdd.Actif);
        // Toute requête suivante : link-cut.
        var apres = await Envoyer(_pair.Requete(HttpMethod.Get, "/link/v1/status"));
        Assert.Equal(RefusLiaison.LiaisonCoupee, await Raison(apres));
        Assert.Contains(await Lire(db => db.JournalAdmin.Select(j => j.Type).ToListAsync()), t => t == "liaison.coupee");

        // Nouvel appairage puis coupure depuis la console : Noosphere est prévenu.
        _pair.Reinitialiser("2M8R-7XKQ-PA4D");
        (await admin.PostAsJsonAsync("/api/admin/liaisons/appairer", new { url = PairNoosphereFactice.Url, code = _pair.Code })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/couper", new { confirmer = false })).StatusCode);
        var coupee = (await (await admin.PostAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/couper", new { confirmer = true })).Content.ReadFromJsonAsync<LiaisonDto>(Web))!;
        Assert.Equal(EtatLiaison.Coupee, coupee.Etat);
        Assert.True(_pair.Coupe);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/liaisons/{_pair.LinkId}/couper", new { confirmer = true })).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<List<LiaisonDto>>("/api/admin/liaisons", Web))!.Count(x => x.Etat == EtatLiaison.Coupee));
    }

    // ---------- Démarrage, démonstration, ligne de commande ----------

    [Fact]
    public async Task Demonstration_liaison_fictive_active_sans_aucun_appel_et_ligne_de_commande()
    {
        _pair.Interdit = true;
        var admin = await _api.ClientConnecte("alice");
        var liste = (await admin.GetFromJsonAsync<List<LiaisonDto>>("/api/admin/liaisons", Web))!;
        var demo = Assert.Single(liste);
        Assert.True(demo.Demo);
        Assert.Equal(EtatLiaison.Active, demo.Etat);
        Assert.True(demo.ConfirmeeParNoosphere);
        Assert.Equal(2, demo.SystemesSuivis);
        var catalogue = (await admin.GetFromJsonAsync<CatalogueLiaisonDto>($"/api/admin/liaisons/{demo.Id}/catalogue", Web))!;
        Assert.Equal(["api-commandes", "portail-web"], catalogue.Applications.Where(a => a.Suivie).Select(a => a.Id).Order());
        var synchro = (await (await admin.PostAsync($"/api/admin/liaisons/{demo.Id}/synchroniser", null)).Content.ReadFromJsonAsync<SynchroLiaisonDto>(Web))!;
        Assert.Equal("aucun changement", synchro.Resume);
        Assert.True(await Taches.EnvoyerDeploiements(true, CancellationToken.None) > 0); // envois notés « demo », rien ne part
        var echanges = (await admin.GetFromJsonAsync<List<EchangeLiaisonDto>>($"/api/admin/liaisons/{demo.Id}/echanges", Web))!;
        Assert.Contains(echanges, e => e.Operation == "catalog" && e.Resultat == "demo");
        Assert.Contains(echanges, e => e.Operation == "deployments" && e.Resultat == "demo" && e.Detail.Contains("non envoyé"));
        Assert.Empty(_pair.Recus);

        // Ligne de commande : liste, confirmation fausse (code 1), coupure sans --oui (code 4), puis avec.
        var json = await LigneDeCommandeConventionsTests.Cli("liaison", "liste", "--json");
        Assert.Equal(0, json.Code);
        Assert.Equal(demo.Id, json.Json[0].GetProperty("id").GetGuid());
        var texte = await LigneDeCommandeConventionsTests.Cli("liaison", "liste");
        Assert.Contains(demo.Empreinte, texte.Sortie);
        var prefixe = demo.Id.ToString()[..8];
        Assert.Equal(1, (await LigneDeCommandeConventionsTests.Cli("liaison", "confirmer", prefixe, "--empreinte", "abbey acorn 0000 zinc")).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "confirmer", prefixe, "--empreinte", demo.Empreinte)).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "synchroniser")).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "options", prefixe, "--suivi-automatique", "non")).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "echanges", "--limite", "3")).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "renouveler", prefixe)).Code);
        Assert.Equal(5, (await LigneDeCommandeConventionsTests.Cli("liaison", "couper", "ffffffff", "--oui")).Code);
        Assert.Equal(4, (await LigneDeCommandeConventionsTests.Cli("liaison", "couper", prefixe, "--non-interactif")).Code);
        Assert.Equal(0, (await LigneDeCommandeConventionsTests.Cli("liaison", "couper", prefixe, "--oui")).Code);
        Assert.Equal(2, (await LigneDeCommandeConventionsTests.Cli("liaison", "appairer", "https://x.test")).Code);
        Assert.Equal(0, await Lire(db => db.SystemesCatalogue.CountAsync(s => s.Source != null)));
        Assert.Empty(_pair.Recus);
    }
}

/// <summary>Appairage au premier démarrage (NOOSPHERE_URL + NOOSPHERE_CODE_LIAISON), chaque test avec sa propre application.</summary>
[Collection("Api")]
public class LiaisonDemarrageTests
{
    [Fact]
    public async Task Appairage_au_demarrage_avec_NOOSPHERE_URL_et_NOOSPHERE_CODE_LIAISON()
    {
        var pair = new PairNoosphereFactice { Code = "BOOT-STRAP-CODE-1234-ABCD" };
        using var api = new ApiFixture(demo: false, hote: b =>
        {
            b.UseSetting("NOOSPHERE_URL", PairNoosphereFactice.Url);
            b.UseSetting("NOOSPHERE_CODE_LIAISON", pair.Code);
            b.ConfigureTestServices(s => s.AddSingleton(new TransportLiaison(pair)));
        });
        var taches = api.Services.GetRequiredService<TachesLiaisons>();
        Assert.True(await taches.Amorcer(CancellationToken.None));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var l = await db.Liaisons.SingleAsync();
        Assert.Equal(pair.LinkId, l.Id);
        Assert.Equal(EtatLiaison.EnAttente, l.Etat); // l'empreinte reste à confirmer
        Assert.Equal(pair.Empreinte(), l.Empreinte);
        Assert.StartsWith("Démarrage", l.AppaireePar, StringComparison.Ordinal);
        // Une seule fois : la liaison existe déjà au démarrage suivant.
        Assert.False(await taches.Amorcer(CancellationToken.None));
    }

    [Fact]
    public async Task Code_de_demarrage_trop_court_ignore()
    {
        var pair = new PairNoosphereFactice { Code = "COURT-123", Interdit = true };
        using var api = new ApiFixture(demo: false, hote: b =>
        {
            b.UseSetting("Liaison:NoosphereUrl", PairNoosphereFactice.Url);
            b.UseSetting("Liaison:CodeAppairage", pair.Code);
            b.ConfigureTestServices(s => s.AddSingleton(new TransportLiaison(pair)));
        });
        Assert.False(await api.Services.GetRequiredService<TachesLiaisons>().Amorcer(CancellationToken.None));
        Assert.Empty(pair.Recus);
    }
}
