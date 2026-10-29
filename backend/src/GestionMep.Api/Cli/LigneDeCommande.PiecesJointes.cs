using System.Diagnostics;
using GestionMep.Api.Services;

namespace GestionMep.Api.Cli;

/// <summary>Stockage des pièces jointes : place occupée, compression des fichiers déjà déposés (#112).</summary>
public static partial class LigneDeCommande
{
    private static async Task<int> PiecesJointes(string[] arguments) => arguments switch
    {
        ["etat"] => await PiecesJointesEtat(),
        ["compresser", .. var options] => await PiecesJointesCompresser(options, decompresser: false),
        ["decompresser", .. var options] => await PiecesJointesCompresser(options, decompresser: true),
        _ => throw new ErreurUsage("pieces-jointes etat | compresser [--essai] [--tout] | decompresser [--essai]"),
    };

    private static string Gain(long fichiers, long stockee) =>
        fichiers > stockee ? $" (-{100 * (fichiers - stockee) / fichiers} %)" : "";

    private static async Task<int> PiecesJointesEtat()
    {
        var config = Configuration();
        await using var db = Base(config);
        var etat = await CompressionPiecesJointes.Etat(db);
        if (EnJson)
        {
            EcrireJson(etat);
            return 0;
        }
        var stockage = new StockagePiecesJointes(config);
        Console.WriteLine($"Dossier : {stockage.Dossier}");
        Console.WriteLine($"{etat.Pieces} pièce(s) jointe(s) : {CompressionPiecesJointes.Lisible(etat.TailleFichiers)} de fichiers, "
            + $"{CompressionPiecesJointes.Lisible(etat.TailleStockee)} sur le disque{Gain(etat.TailleFichiers, etat.TailleStockee)}.");
        Console.WriteLine($"{etat.Compressees} compressée(s) ; compression des dépôts : {(stockage.CompressionActive ? $"activée (gain minimal {stockage.GainMinimal} %)" : "désactivée")}.");
        if (etat.NonExaminees > 0)
            Console.WriteLine($"{etat.NonExaminees} pièce(s) jamais examinée(s) (déposées avant la compression) : "
                + "gestion-mep pieces-jointes compresser --essai pour estimer le gain, puis sans --essai pour compresser.");
        return 0;
    }

    /// <summary>
    /// Compresse (ou décompresse) les pièces déjà déposées, une à une, application en marche ou non. Avancement sur la sortie
    /// d'erreur toutes les 2 secondes ; Ctrl+C arrête proprement entre deux pièces, et une nouvelle exécution reprend où la
    /// précédente s'est arrêtée.
    /// </summary>
    private static async Task<int> PiecesJointesCompresser(string[] arguments, bool decompresser)
    {
        var o = decompresser ? Options.Lire(arguments, [], "--essai") : Options.Lire(arguments, [], "--essai", "--tout");
        var essai = o.A("--essai");
        var config = Configuration();
        await using var db = Base(config);
        var stockage = new StockagePiecesJointes(config);

        using var arret = new CancellationTokenSource();
        ConsoleCancelEventHandler interrompre = (_, e) =>
        {
            e.Cancel = true;
            arret.Cancel();
        };
        Console.CancelKeyPress += interrompre;
        var chrono = Stopwatch.StartNew();
        var dernier = TimeSpan.Zero;
        void Avancement(CompressionPiecesJointes.Bilan b)
        {
            if (chrono.Elapsed - dernier < TimeSpan.FromSeconds(2) && b.Examinees < b.Total) return;
            dernier = chrono.Elapsed;
            Console.Error.WriteLine($"  {b.Examinees}/{b.Total} pièce(s) examinée(s) ({100 * b.Examinees / Math.Max(1, b.Total)} %), "
                + $"{b.Traitees} {(decompresser ? "décompressée(s)" : "compressée(s)")} : {CompressionPiecesJointes.Lisible(b.Avant)} → {CompressionPiecesJointes.Lisible(b.Apres)}");
        }
        using var quotas = new QuotasPiecesJointes(config, stockage);
        CompressionPiecesJointes.Bilan bilan;
        try
        {
            bilan = decompresser
                ? await CompressionPiecesJointes.Decompresser(db, stockage, quotas.EspaceLibreMin, essai,
                    EnJson ? null : Avancement, arret.Token)
                : await CompressionPiecesJointes.Compresser(db, stockage, essai, o.A("--tout"), EnJson ? null : Avancement, arret.Token);
        }
        finally
        {
            Console.CancelKeyPress -= interrompre;
        }

        if (EnJson)
        {
            EcrireJson(new { Essai = essai, bilan.Total, bilan.Examinees, bilan.Traitees, bilan.Inchangees, bilan.Absentes, bilan.Echecs,
                bilan.Avant, bilan.Apres, bilan.Interrompu });
            return bilan.Echecs > 0 || bilan.Interrompu ? 1 : 0;
        }
        var verbe = decompresser ? "décompressée(s)" : "compressée(s)";
        if (bilan.Total == 0)
            Console.WriteLine(decompresser ? "Aucune pièce jointe compressée." : "Aucune pièce jointe à examiner : toutes l'ont déjà été (--tout pour réexaminer celles laissées telles quelles).");
        else
        {
            Console.WriteLine($"{(essai ? "Essai, rien n'a été modifié : " : "")}{bilan.Examinees} pièce(s) examinée(s) sur {bilan.Total}, "
                + $"{bilan.Traitees} {(essai ? (decompresser ? "à décompresser" : "à compresser") : verbe)}, {bilan.Inchangees} laissée(s) telle(s) quelle(s).");
            Console.WriteLine($"Place occupée : {CompressionPiecesJointes.Lisible(bilan.Avant)} → {CompressionPiecesJointes.Lisible(bilan.Apres)}"
                + $"{(essai ? " (estimation)" : "")}{Gain(bilan.Avant, bilan.Apres)}.");
            if (bilan.Absentes > 0) Console.WriteLine($"{bilan.Absentes} pièce(s) sans fichier sur le disque (ignorées).");
            if (bilan.Echecs > 0)
                Console.Error.WriteLine($"Erreur : {bilan.Echecs} pièce(s) non traitée(s) (fichier verrouillé ou illisible, disque plein) : relancer la commande pour les reprendre.");
            if (bilan.Interrompu) Console.Error.WriteLine("Interrompu : relancer la commande pour reprendre où elle s'est arrêtée.");
            if (essai && bilan.Traitees > 0)
                Console.WriteLine($"Pour le faire : gestion-mep pieces-jointes {(decompresser ? "decompresser" : "compresser")}{(o.A("--tout") ? " --tout" : "")}");
            if (decompresser && !essai && stockage.CompressionActive)
                Console.WriteLine("La compression des dépôts reste activée : gestion-mep reglages definir pieces-compression non pour garder les prochains fichiers tels quels.");
        }
        return bilan.Echecs > 0 || bilan.Interrompu ? 1 : 0;
    }
}
