using System.Diagnostics;

namespace GestionMep.Tests;

/// <summary>
/// Latence simulée (Developpement:LatenceMs) : en développement seulement, pour voir l'application comme avec une base
/// lointaine ; inactive par défaut et jamais en production.
/// </summary>
[Collection("Api")]
public class LatenceDeveloppementTests
{
    private static async Task<long> Duree(HttpClient client)
    {
        var chrono = Stopwatch.StartNew();
        (await client.GetAsync("/api/sante")).EnsureSuccessStatusCode();
        return chrono.ElapsedMilliseconds;
    }

    [Fact]
    public async Task La_latence_simulee_ne_ralentit_l_api_qu_en_developpement()
    {
        Environment.SetEnvironmentVariable("Developpement__LatenceMs", "400");
        try
        {
            using (var dev = new ApiFixture())
                Assert.True(await Duree(dev.Client()) >= 400);
            using (var prod = new ApiFixture(environnement: "Production"))
            {
                var client = prod.Client();
                await Duree(client); // premier appel (démarrage)
                Assert.True(await Duree(client) < 400);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("Developpement__LatenceMs", null);
        }
        using var sansReglage = new ApiFixture();
        var c = sansReglage.Client();
        await Duree(c);
        Assert.True(await Duree(c) < 400);
    }
}
