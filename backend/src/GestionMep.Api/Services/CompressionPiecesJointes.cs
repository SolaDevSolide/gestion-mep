using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>Occupation du stockage des pièces jointes (console d'administration, <c>gestion-mep pieces-jointes etat</c>).</summary>
/// <param name="TailleFichiers">Somme des tailles des fichiers déposés (rendus au téléchargement).</param>
/// <param name="TailleStockee">Place occupée sur le serveur, après compression.</param>
/// <param name="NonExaminees">Pièces déposées avant la compression (ou pendant qu'elle était désactivée), jamais examinées.</param>
public record EtatStockageDto(int Pieces, long TailleFichiers, long TailleStockee, int Compressees, int NonExaminees);

/// <summary>
/// Compression (ou décompression) des pièces jointes déjà déposées, par la commande d'exploitation
/// <c>gestion-mep pieces-jointes compresser|decompresser</c>, application en marche ou non.
/// <para>
/// Reprise et idempotence : seules les pièces jamais examinées (compression null) sont traitées, et chacune est marquée dès
/// qu'elle l'est ; une commande interrompue reprend donc là où elle s'était arrêtée. Pendant le traitement d'une pièce, sa
/// ligne est verrouillée : une suppression attend la fin, une pièce supprimée entre-temps est ignorée. Le fichier est
/// remplacé d'un coup (renommage) après vérification de l'empreinte, et le téléchargement reconnaît le format au fichier
/// lui-même : une pièce reste lisible à tout instant, même si la commande s'arrête entre le fichier et la base.
/// </para>
/// </summary>
public static class CompressionPiecesJointes
{
    /// <summary>Avancement et résultat d'un passage.</summary>
    /// <param name="Total">Pièces à examiner.</param>
    /// <param name="Traitees">Pièces compressées (ou décompressées).</param>
    /// <param name="Inchangees">Pièces laissées telles quelles : format déjà compressé ou gain insuffisant.</param>
    /// <param name="Absentes">Pièces dont le fichier manque sur le disque.</param>
    /// <param name="Echecs">Fichier illisible, verrouillé ou disque plein : la pièce sera reprise au passage suivant.</param>
    /// <param name="Avant">Place occupée par les pièces examinées avant le passage.</param>
    /// <param name="Apres">Place qu'elles occupent après (ou occuperaient, à l'essai).</param>
    public record Bilan(int Total, int Examinees, int Traitees, int Inchangees, int Absentes, int Echecs, long Avant, long Apres, bool Interrompu);

    private sealed record Candidate(Guid Id, long Taille, long TailleStockee, string Sha256);

    private enum Issue { Traitee, Inchangee, Echec }

    /// <summary>Ce que le traitement d'une pièce enregistre : taille sur disque et compression, et son issue pour le bilan.</summary>
    private readonly record struct Resultat(long TailleStockee, string? Compression, Issue Issue);

    public static async Task<EtatStockageDto> Etat(MepDbContext db, CancellationToken ct = default)
    {
        var e = await db.PiecesJointes.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Pieces = g.Count(),
            TailleFichiers = g.Sum(p => p.Taille),
            TailleStockee = g.Sum(p => p.TailleStockee),
            Compressees = g.Count(p => p.Compression == CompressionPiece.Gzip),
            NonExaminees = g.Count(p => p.Compression == null),
        }).FirstOrDefaultAsync(ct);
        return e is null ? new(0, 0, 0, 0, 0) : new(e.Pieces, e.TailleFichiers, e.TailleStockee, e.Compressees, e.NonExaminees);
    }

    /// <summary>
    /// Compresse les pièces jamais examinées (<paramref name="tout"/> : aussi celles laissées telles quelles, après une baisse
    /// du gain minimal). Avec <paramref name="essai"/>, rien n'est écrit : le bilan donne le gain attendu.
    /// <paramref name="progression"/> est appelée après chaque pièce. L'annulation arrête le passage entre deux pièces (bilan
    /// <c>Interrompu</c>).
    /// </summary>
    public static async Task<Bilan> Compresser(MepDbContext db, StockagePiecesJointes stockage, bool essai, bool tout,
        Action<Bilan>? progression, CancellationToken ct)
    {
        var candidates = await db.PiecesJointes.AsNoTracking()
            .Where(p => p.Compression == null || (tout && p.Compression == CompressionPiece.Aucune))
            .OrderBy(p => p.DeposeLe).ThenBy(p => p.Id)
            .Select(p => new Candidate(p.Id, p.Taille, p.TailleStockee, p.Sha256)).ToListAsync(ct);
        var bilan = await Parcourir(db, stockage, candidates, essai, progression, async (c, jeton) =>
        {
            var r = await stockage.Compresser(c.Id, c.Taille, c.Sha256, jeton, essai);
            return new Resultat(r.TailleStockee, r.Compression, r.Compression switch
            {
                CompressionPiece.Gzip => Issue.Traitee,
                CompressionPiece.Aucune => Issue.Inchangee,
                _ => Issue.Echec,
            });
        }, ct);
        if (!essai && bilan.Examinees > 0)
            await Journaliser(db, "pieces.compression", $"Compression des pièces jointes existantes : {bilan.Traitees} compressée(s) sur "
                + $"{bilan.Examinees} examinée(s), {Lisible(bilan.Avant)} → {Lisible(bilan.Apres)}{(bilan.Interrompu ? " (interrompue)" : "")}");
        return bilan;
    }

    /// <summary>
    /// Remet dans leur forme d'origine les pièces stockées compressées (avant de revenir à une version de l'application sans
    /// compression, ou pour relire les fichiers sans <c>zcat</c>). Elles sont marquées « jamais examinées » : penser à désactiver
    /// la compression (réglage pieces-compression) pour que les prochains dépôts restent tels quels. S'arrête si le disque n'a
    /// plus la place de décompresser en gardant l'espace libre minimal.
    /// </summary>
    public static async Task<Bilan> Decompresser(MepDbContext db, StockagePiecesJointes stockage, long espaceLibreMin, bool essai,
        Action<Bilan>? progression, CancellationToken ct)
    {
        var candidates = await db.PiecesJointes.AsNoTracking().Where(p => p.Compression == CompressionPiece.Gzip)
            .OrderBy(p => p.DeposeLe).ThenBy(p => p.Id)
            .Select(p => new Candidate(p.Id, p.Taille, p.TailleStockee, p.Sha256)).ToListAsync(ct);
        var bilan = await Parcourir(db, stockage, candidates, essai, progression, async (c, jeton) =>
        {
            if (essai) return new Resultat(c.Taille, null, Issue.Traitee);
            if (stockage.EspaceLibre() is { } libre && libre - c.Taille < espaceLibreMin)
                throw new ErreurHttp(1, $"Plus assez de place sur le disque des pièces jointes pour décompresser ({Lisible(libre)} libres, "
                    + $"{Lisible(espaceLibreMin)} à garder) : libérer de la place puis relancer la commande, qui reprend où elle s'est arrêtée.");
            return await stockage.Decompresser(c.Id, c.Taille, c.Sha256, jeton)
                ? new Resultat(c.Taille, null, Issue.Traitee)
                : new Resultat(c.TailleStockee, CompressionPiece.Gzip, Issue.Echec);
        }, ct);
        if (!essai && bilan.Examinees > 0)
            await Journaliser(db, "pieces.decompression", $"Décompression des pièces jointes : {bilan.Traitees} décompressée(s), "
                + $"{Lisible(bilan.Avant)} → {Lisible(bilan.Apres)}{(bilan.Interrompu ? " (interrompue)" : "")}");
        return bilan;
    }

    /// <summary>
    /// Traite les pièces une à une : ligne verrouillée (ignorée si elle a disparu), fichier traité, ligne mise à jour avec la
    /// taille stockée et la compression que renvoie le traitement. Une pièce en échec garde son état et sera reprise au
    /// passage suivant.
    /// </summary>
    private static async Task<Bilan> Parcourir(MepDbContext db, StockagePiecesJointes stockage, List<Candidate> candidates, bool essai,
        Action<Bilan>? progression, Func<Candidate, CancellationToken, Task<Resultat>> traiter, CancellationToken ct)
    {
        var bilan = new Bilan(candidates.Count, 0, 0, 0, 0, 0, 0, 0, false);
        foreach (var c in candidates)
        {
            if (ct.IsCancellationRequested) return bilan with { Interrompu = true };
            if (stockage.TailleSurDisque(c.Id) is null)
            {
                bilan = bilan with { Examinees = bilan.Examinees + 1, Absentes = bilan.Absentes + 1 };
                progression?.Invoke(bilan);
                continue;
            }
            try
            {
                Resultat r;
                if (essai) r = await traiter(c, ct);
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var existe = (await db.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "PiecesJointes" WHERE "Id" = {c.Id} FOR UPDATE""")
                        .ToListAsync(ct)).Count > 0;
                    if (!existe) continue; // supprimée entre-temps
                    r = await traiter(c, ct);
                    if (r.Issue != Issue.Echec)
                        await db.PiecesJointes.Where(p => p.Id == c.Id).ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.TailleStockee, r.TailleStockee).SetProperty(p => p.Compression, r.Compression), ct);
                    await tx.CommitAsync(ct);
                }
                bilan = bilan with
                {
                    Examinees = bilan.Examinees + 1,
                    Traitees = bilan.Traitees + (r.Issue == Issue.Traitee ? 1 : 0),
                    Inchangees = bilan.Inchangees + (r.Issue == Issue.Inchangee ? 1 : 0),
                    Echecs = bilan.Echecs + (r.Issue == Issue.Echec ? 1 : 0),
                    Avant = bilan.Avant + c.TailleStockee,
                    Apres = bilan.Apres + (r.Issue == Issue.Echec ? c.TailleStockee : r.TailleStockee),
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return bilan with { Interrompu = true };
            }
            progression?.Invoke(bilan);
        }
        return bilan;
    }

    private static async Task Journaliser(MepDbContext db, string type, string detail)
    {
        JournalAdmin.Ecrire(db, null, type, detail);
        await db.SaveChangesAsync();
    }

    /// <summary>« 12,3 Mo » : taille lisible, en français.</summary>
    public static string Lisible(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => Format(octets / 1024d, "Ko"),
        < 1024L * 1024 * 1024 => Format(octets / 1024d / 1024, "Mo"),
        _ => Format(octets / 1024d / 1024 / 1024, "Go"),
    };

    private static string Format(double valeur, string unite) =>
        valeur.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " " + unite;
}
