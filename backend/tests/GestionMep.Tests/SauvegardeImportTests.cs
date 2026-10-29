using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Sauvegarde puis importation réussie (#98) : l'archive remet la base et les pièces jointes dans l'état sauvegardé ; l'état
/// courant, mis de côté pendant l'opération, est supprimé une fois les contrôles réussis.
/// </summary>
[Collection("Api")]
public class SauvegardeImportTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Une_sauvegarde_importee_remet_la_base_et_les_pieces_jointes_dans_leur_etat()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var corps = new MultipartFormDataContent();
        var fichier = new ByteArrayContent([7, 8, 9]);
        fichier.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        corps.Add(fichier, "fichier", "procedure.txt");
        await AidesApi.Ok(await alice.PostAsync($"/api/meps/{id}/pieces-jointes", corps));
        var piece = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray().Single().GetProperty("id").GetGuid();

        var dossier = Path.Combine(_api.DossierPieces, "sauvegardes");
        var archive = Path.Combine(dossier, "complete.tar.gz");
        var messages = new List<string>();
        Sauvegardes.Manifeste manifeste;
        StockagePiecesJointes stockage;
        Sauvegardes sauvegardes;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            stockage = scope.ServiceProvider.GetRequiredService<StockagePiecesJointes>();
            sauvegardes = new Sauvegardes(dossier, stockage.Dossier, messages.Add, messages.Add);
            manifeste = await sauvegardes.Exporter(db, archive);
        }
        Assert.Contains(manifeste.Pieces, p => p.Nom == piece.ToString("N"));

        // Après la sauvegarde : une équipe créée, la pièce jointe perdue sur le disque.
        await AidesApi.Ok(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "Créée après la sauvegarde", description = "" }));
        stockage.Supprimer(piece);
        Assert.Null(stockage.Ouvrir(piece, 3));

        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            Assert.True(await sauvegardes.Importer(db, archive, "test"), string.Join("\n", messages));
        }

        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            Assert.False(await db.Equipes.AnyAsync(e => e.Nom == "Créée après la sauvegarde"));
            Assert.True(await db.Meps.AnyAsync(m => m.Id == id));
            Assert.True(await db.PiecesJointes.AnyAsync(p => p.Id == piece));
            // Tout le contenu de l'archive est là (le journal d'administration a en plus la ligne de l'importation).
            Assert.Empty(Sauvegardes.Comparer(Sauvegardes.Releve.De(manifeste), await sauvegardes.Relever(db)));
        }
        await using (var flux = stockage.Ouvrir(piece, 3))
        {
            Assert.NotNull(flux);
            using var copie = new MemoryStream();
            await flux!.CopyToAsync(copie);
            Assert.Equal(new byte[] { 7, 8, 9 }, copie.ToArray());
        }
        // Contrôles réussis : la sauvegarde de l'état d'avant l'importation, devenue inutile, est supprimée, et le résultat
        // est inscrit au journal d'administration.
        Assert.Empty(Directory.GetFiles(dossier, "avant-import-*"));
        Assert.Contains(messages, m => m.StartsWith("Importation de complete.tar.gz") && m.Contains("contrôles réussis") && m.Contains("sauvegarde préalable supprimée"));
        var journal = (await AidesApi.Ok(await alice.GetAsync("/api/admin/journal"))).EnumerateArray().ToList();
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "donnees.controle-reussi");

        // L'application relit la base importée.
        var bob = await _api.ClientConnecte("bob");
        Assert.Single((await AidesApi.Ok(await bob.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray());
    }
}
