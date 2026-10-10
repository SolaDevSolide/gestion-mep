using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Securite;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionMep.Tests;

/// <summary>Protections de déploiement : CSRF, tentatives de connexion, énumérations, surveillance des échéances.</summary>
[Collection("Api")]
public class SecuriteTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Requete_qui_modifie_sans_en_tete_anti_csrf_refusee()
    {
        var forge = _api.ClientSansEnTete();
        var rep = await forge.PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = "demo" });
        Assert.Equal(HttpStatusCode.Forbidden, rep.StatusCode);
        // Les lectures restent possibles sans l'en-tête (liens, flux SSE, téléchargement de pièces jointes).
        Assert.Equal(HttpStatusCode.OK, (await forge.GetAsync("/api/sante")).StatusCode);

        // Session ouverte puis requête forgée avec le même cookie : refusée aussi.
        var alice = await _api.ClientConnecte("alice");
        alice.DefaultRequestHeaders.Remove(ProtectionsHttp.EnTeteCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync("/api/auth/deconnexion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Login_bloque_apres_cinq_echecs_meme_avec_le_bon_mot_de_passe()
    {
        var client = _api.Client();
        for (var i = 0; i < LimiteurConnexion.EchecsAvantBlocage; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/connexion", new { login = "bob", motDePasse = "faux" })).StatusCode);
        var bloque = await client.PostAsJsonAsync("/api/auth/connexion", new { login = "Bob", motDePasse = "demo" });
        Assert.Equal(HttpStatusCode.TooManyRequests, bloque.StatusCode);
        // Les autres comptes ne sont pas touchés.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/connexion", new { login = "claire", motDePasse = "demo" })).StatusCode);
    }

    [Fact]
    public void Blocage_progressif_puis_levee()
    {
        var temps = new TempsFixe();
        var limiteur = new LimiteurConnexion(temps);
        for (var i = 0; i < LimiteurConnexion.EchecsAvantBlocage - 1; i++) limiteur.Echec("x");
        Assert.Null(limiteur.BloqueJusqua("x"));
        limiteur.Echec("x");
        Assert.Equal(temps.GetUtcNow().AddMinutes(1), limiteur.BloqueJusqua("x"));
        limiteur.Echec("x");
        Assert.Equal(temps.GetUtcNow().AddMinutes(2), limiteur.BloqueJusqua("x"));
        for (var i = 0; i < 10; i++) limiteur.Echec("x");
        Assert.Equal(temps.GetUtcNow() + LimiteurConnexion.BlocageMax, limiteur.BloqueJusqua("x"));
        temps.Advance(LimiteurConnexion.BlocageMax);
        Assert.Null(limiteur.BloqueJusqua("x"));
        limiteur.Reussite("x");
        limiteur.Echec("x");
        Assert.Null(limiteur.BloqueJusqua("x"));
    }

    [Fact]
    public async Task Enumeration_en_entier_refusee()
    {
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(claire);
        var mep = await AidesApi.Ok(await claire.GetAsync($"/api/meps/{id}"));
        var rep = await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = mep.GetProperty("version").GetInt32(), decision = 7, commentaire = "" });
        Assert.Equal(HttpStatusCode.BadRequest, rep.StatusCode);
    }

    [Fact]
    public async Task Surveillance_continue_apres_une_erreur_imprevue_sur_une_MEP()
    {
        var alice = await _api.ClientConnecte("alice");
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
            ids.Add((await AidesApi.Ok(await alice.PostAsJsonAsync("/api/meps", new { outil = "Outil", objet = $"Échéance {i}" }))).GetProperty("id").GetGuid());
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            foreach (var m in await db.Meps.Where(m => ids.Contains(m.Id)).ToListAsync())
            {
                m.Phase = Phase.Execution;
                m.EcheanceRepli = DateTimeOffset.UtcNow.AddMinutes(-5);
            }
            await db.SaveChangesAsync();
        }

        var surveillance = new SurveillanceEcheances(new ScopesDefaillants(_api.Services.GetRequiredService<IServiceScopeFactory>()),
            TimeProvider.System, NullLogger<SurveillanceEcheances>.Instance);
        // La première MEP lève une erreur imprévue : la seconde bascule quand même.
        Assert.Equal(1, await surveillance.Verifier(CancellationToken.None));
        // Passe suivante : la première bascule à son tour.
        Assert.Equal(1, await surveillance.Verifier(CancellationToken.None));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            Assert.All(await db.Meps.Where(m => ids.Contains(m.Id)).ToListAsync(), m => Assert.Equal(Phase.RetourArriere, m.Phase));
        }
    }

    private sealed class TempsFixe : TimeProvider
    {
        private DateTimeOffset _maintenant = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => _maintenant;
        public void Advance(TimeSpan duree) => _maintenant += duree;
    }

    /// <summary>Fabrique de scopes dont le premier <see cref="ServiceMep"/> demandé lève une exception imprévue.</summary>
    private sealed class ScopesDefaillants(IServiceScopeFactory reelle) : IServiceScopeFactory
    {
        private bool _defaillanceFaite;

        public IServiceScope CreateScope() => new Scope(reelle.CreateScope(), this);

        private sealed class Scope(IServiceScope interne, ScopesDefaillants usine) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type type)
            {
                if (type == typeof(ServiceMep) && !usine._defaillanceFaite)
                {
                    usine._defaillanceFaite = true;
                    throw new InvalidOperationException("Panne simulée.");
                }
                return interne.ServiceProvider.GetService(type);
            }
            public void Dispose() => interne.Dispose();
        }
    }
}
