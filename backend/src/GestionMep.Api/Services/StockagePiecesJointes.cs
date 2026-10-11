using System.Security.Cryptography;

namespace GestionMep.Api.Services;

/// <summary>
/// Contenu des pièces jointes sur disque, dans <c>Stockage:Dossier</c> (défaut <c>/data/pieces-jointes</c>).
/// Chaque fichier est stocké sous l'Id de la pièce : le nom d'origine n'est jamais utilisé comme chemin.
/// </summary>
public class StockagePiecesJointes(IConfiguration config)
{
    public const string DossierParDefaut = "/data/pieces-jointes";

    public string Dossier { get; } = string.IsNullOrWhiteSpace(config["Stockage:Dossier"]) ? DossierParDefaut : config["Stockage:Dossier"]!;

    /// <summary>Taille maximale d'un fichier, en Mo (réglage <c>Stockage:TailleMaxMo</c>, défaut 20, relu à chaque dépôt).</summary>
    public int TailleMaxMo => Math.Clamp(config.GetValue("Stockage:TailleMaxMo", 20), 1, Reglages.PlafondPieceMo);

    public long TailleMaxOctets => TailleMaxMo * 1024L * 1024L;

    /// <summary>Corps de requête d'un dépôt : le fichier plus 1 Mo pour l'enveloppe du formulaire.</summary>
    public long LimiteDepot => TailleMaxOctets + 1024 * 1024;

    /// <summary>Limite de corps la plus haute possible (plafond du réglage) ; la limite en cours est appliquée à chaque dépôt.</summary>
    public const long LimiteDepotMaximale = (Reglages.PlafondPieceMo + 1L) * 1024 * 1024;

    private string Chemin(Guid id) => Path.Combine(Dossier, id.ToString("N"));

    /// <summary>Copie le flux sur disque ; renvoie la taille et l'empreinte SHA-256 (hexadécimal minuscule).</summary>
    public async Task<(long Taille, string Sha256)> Ecrire(Guid id, Stream source, CancellationToken ct)
    {
        Directory.CreateDirectory(Dossier);
        var chemin = Chemin(id);
        try
        {
            await using var cible = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var tampon = new byte[81920];
            long taille = 0;
            int lus;
            while ((lus = await source.ReadAsync(tampon, ct)) > 0)
            {
                taille += lus;
                if (taille > TailleMaxOctets) throw new ErreurHttp(413, MessageTropVolumineux);
                sha.AppendData(tampon, 0, lus);
                await cible.WriteAsync(tampon.AsMemory(0, lus), ct);
            }
            return (taille, Convert.ToHexStringLower(sha.GetHashAndReset()));
        }
        catch
        {
            Supprimer(id);
            throw;
        }
    }

    /// <summary>
    /// Met de côté, dans le sous-dossier <c>orphelins</c>, les fichiers qui ne correspondent à aucune pièce jointe en
    /// base : restes d'un arrêt de l'application entre l'écriture du fichier et l'enregistrement en base. Ils sont
    /// déplacés et non supprimés, pour ne rien perdre si l'application a été branchée par erreur sur une autre base.
    /// Ne touche qu'aux fichiers nommés comme une pièce jointe (Id sur 32 caractères hexadécimaux) et plus anciens que
    /// <paramref name="ageMinimum"/>, pour ne jamais déplacer un dépôt en cours. Renvoie le nombre de fichiers déplacés.
    /// </summary>
    public int IsolerOrphelins(IReadOnlySet<Guid> connus, TimeSpan ageMinimum, DateTimeOffset maintenant)
    {
        if (!Directory.Exists(Dossier)) return 0;
        var quarantaine = Path.Combine(Dossier, DossierOrphelins);
        var deplaces = 0;
        foreach (var chemin in Directory.EnumerateFiles(Dossier))
        {
            var nom = Path.GetFileName(chemin);
            if (nom.Length != 32 || !Guid.TryParseExact(nom, "N", out var id) || connus.Contains(id)) continue;
            if (maintenant - File.GetLastWriteTimeUtc(chemin) < ageMinimum) continue;
            try
            {
                Directory.CreateDirectory(quarantaine);
                File.Move(chemin, Path.Combine(quarantaine, nom), overwrite: true);
                deplaces++;
            }
            catch (IOException) { /* verrouillé : on réessaiera au prochain démarrage */ }
        }
        return deplaces;
    }

    /// <summary>Sous-dossier où sont mis de côté les fichiers sans pièce jointe en base.</summary>
    public const string DossierOrphelins = "orphelins";

    public string MessageTropVolumineux => $"Fichier trop volumineux : {TailleMaxMo} Mo maximum.";

    /// <summary>Ouvre le contenu en lecture ; null si le fichier n'existe plus.</summary>
    public Stream? Ouvrir(Guid id)
    {
        var chemin = Chemin(id);
        return File.Exists(chemin) ? new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Supprimer(Guid id)
    {
        try { File.Delete(Chemin(id)); }
        catch (IOException) { /* fichier verrouillé ou déjà absent : la ligne en base fait foi */ }
    }

    /// <summary>
    /// Nom d'origine assaini pour l'affichage et le téléchargement : sans chemin, sans caractères de contrôle,
    /// de mise en forme invisibles (dont l'inversion d'écriture U+202E, qui fait afficher
    /// un « .exe » comme un « .pdf ») ni caractères interdits, 200 caractères au plus.
    /// </summary>
    public static string AssainirNom(string? nom)
    {
        var n = (nom ?? "").Replace('\\', '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        var interdits = new HashSet<char>("<>:\"|?*");
        n = new string(n.Where(c => !char.IsControl(c) && !interdits.Contains(c)
            && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim().Trim('.').Trim();
        if (n.Length > 200)
        {
            var ext = Path.GetExtension(n);
            n = ext.Length is > 0 and <= 20 ? n[..(200 - ext.Length)] + ext : n[..200];
        }
        return n.Length == 0 ? "fichier" : n;
    }
}
