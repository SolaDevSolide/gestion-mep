using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Plafonds des pièces jointes vérifiés pendant la réception (#96) : un dépôt qui annonce une taille au-delà d'un plafond est
/// refusé sans être lu ; un dépôt sans taille annoncée (envoi par morceaux) est arrêté dès qu'il dépasse le plafond, et rien
/// n'en reste (ni fichier, ni place réservée).
/// </summary>
[Collection("Api")]
public class DepotEnFluxTests : IDisposable
{
    private readonly ApiFixture _api;
    public void Dispose() => _api.Dispose();

    private const int Ko = 1024;

    /// <summary>Octets du corps de la dernière requête lus par l'application (le client de test, lui, envoie toujours tout).</summary>
    private sealed class Compteur
    {
        public long Lus;
        public long? Annonce;
    }

    private readonly Compteur _compteur = new();

    public DepotEnFluxTests()
    {
        var compteur = _compteur;
        _api = new ApiFixture(hote: b => b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new FiltreCompteur(compteur))));
    }

    private sealed class FiltreCompteur(Compteur compteur) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> suite) => app =>
        {
            app.Use((ctx, next) =>
            {
                if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.Value!.EndsWith("/pieces-jointes"))
                {
                    compteur.Lus = 0;
                    compteur.Annonce = ctx.Request.ContentLength;
                    ctx.Request.Body = new CorpsCompte(ctx.Request.Body, compteur);
                }
                return next();
            });
            suite(app);
        };
    }

    /// <summary>Corps de requête qui compte ce que l'application en lit.</summary>
    private sealed class CorpsCompte(Stream source, Compteur compteur) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Compter(source.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Compter(await source.ReadAsync(buffer, ct));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private int Compter(int n)
        {
            Interlocked.Add(ref compteur.Lus, n);
            return n;
        }
    }

    /// <summary>Contenu non positionnable : le formulaire part alors sans taille annoncée (envoi par morceaux).</summary>
    private sealed class FluxSansTaille(byte[] contenu) : MemoryStream(contenu)
    {
        public override bool CanSeek => false;
    }

    /// <summary>
    /// Client connecté qui ne suit pas les redirections : celui qui les suit garde le corps en mémoire pour pouvoir le renvoyer,
    /// et annonce donc toujours sa taille.
    /// </summary>
    private async Task<HttpClient> SansTampon()
    {
        var client = _api.ClientSansRedirection();
        (await client.PostAsJsonAsync("/api/auth/connexion", new { login = "alice", motDePasse = "demo" })).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<(HttpResponseMessage Reponse, long Lus)> Deposer(HttpClient c, Guid id, int taille, bool tailleConnue)
    {
        // Contenu incompressible : la place occupée reste celle du fichier, comme avant la compression des pièces jointes.
        var contenu = new byte[taille];
        Random.Shared.NextBytes(contenu);
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(tailleConnue ? new MemoryStream(contenu) : new FluxSansTaille(contenu)), "fichier", "gros.bin" },
        };
        var rep = await c.PostAsync($"/api/meps/{id}/pieces-jointes", form);
        Assert.Equal(tailleConnue, _compteur.Annonce is not null);
        return (rep, Interlocked.Read(ref _compteur.Lus));
    }

    private static async Task<int> NombrePieces(HttpClient c, Guid id) =>
        (await AidesApi.Ok(await c.GetAsync($"/api/meps/{id}/pieces-jointes"))).GetArrayLength();

    [Fact]
    public async Task Le_quota_d_une_mep_refuse_un_depot_annonce_trop_gros_sans_le_lire_et_coupe_un_depot_par_morceaux()
    {
        var alice = await SansTampon();
        var id = await AidesApi.IdDemo(alice);
        await AidesApi.Ok(await alice.PutAsJsonAsync("/api/admin/reglages/pieces-quota-mep-mo", new { valeur = "1" }));
        var depart = await NombrePieces(alice, id);
        // Ce que la MEP de démonstration contient déjà compte dans le quota : on la remplit jusqu'à 600 Ko.
        var occupe = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray().Sum(p => p.GetProperty("tailleStockee").GetInt64());
        var (premier, _) = await Deposer(alice, id, (int)(600 * Ko - occupe), tailleConnue: true);
        await AidesApi.Ok(premier);

        // Taille annoncée : 600 Ko de plus ne tiennent pas, refus avant lecture.
        var (annonce, lusAnnonce) = await Deposer(alice, id, 600 * Ko, tailleConnue: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, annonce.StatusCode);
        Assert.Contains("limite de pièces jointes", (await annonce.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal(0, lusAnnonce);

        // Sans taille annoncée : la réception s'arrête peu après les 424 Ko restants.
        var (morceaux, lusMorceaux) = await Deposer(alice, id, 900 * Ko, tailleConnue: false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, morceaux.StatusCode);
        Assert.True(lusMorceaux < 700 * Ko, $"{lusMorceaux} octets lus sur 900 Ko");
        Assert.Equal(depart + 1, await NombrePieces(alice, id));

        // Rien n'est resté réservé : ce qui tient passe, annoncé ou non.
        await AidesApi.Ok((await Deposer(alice, id, 200 * Ko, tailleConnue: false)).Reponse);
        await AidesApi.Ok((await Deposer(alice, id, 200 * Ko, tailleConnue: true)).Reponse);
        Assert.Equal(depart + 3, await NombrePieces(alice, id));
    }

    [Fact]
    public async Task Un_depot_par_morceaux_plus_gros_que_la_taille_maximale_est_coupe_en_413()
    {
        var alice = await SansTampon();
        var id = await AidesApi.IdDemo(alice);
        var depart = await NombrePieces(alice, id);
        // 1 Mo par fichier dans la configuration des tests.
        var (rep, lus) = await Deposer(alice, id, 3 * 1024 * Ko, tailleConnue: false);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rep.StatusCode);
        Assert.True(lus < 2 * 1024 * Ko, $"{lus} octets lus sur 3 Mo");
        Assert.Equal(depart, await NombrePieces(alice, id));
    }
}
