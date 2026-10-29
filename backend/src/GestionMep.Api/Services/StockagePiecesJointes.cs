using System.IO.Compression;
using System.Security.Cryptography;
using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>
/// Contenu des pièces jointes sur disque, dans <c>Stockage:Dossier</c> (défaut <c>/data/pieces-jointes</c>).
/// Chaque fichier est stocké sous l'Id de la pièce : le nom d'origine n'est jamais utilisé comme chemin.
/// <para>
/// Compression sans perte (#112) : après le dépôt, un fichier qui y gagne au moins <see cref="GainMinimal"/> % est remplacé sur
/// disque par sa version gzip (journaux, textes, CSV, JSON, images non compressées…). Les formats déjà compressés (zip, dont
/// les documents Office, archives, vidéos, sons) restent tels quels. Le téléchargement rend toujours les octets d'origine
/// (<see cref="Ouvrir"/>) ; un fichier compressé se relit aussi hors de l'application avec <c>zcat</c> ou <c>gzip -d</c>.
/// </para>
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

    /// <summary>Fichier de travail d'une compression ou d'une décompression, renommé sur le fichier de la pièce une fois vérifié.</summary>
    private string CheminTravail(Guid id) => Chemin(id) + SuffixeTravail;

    /// <summary>Suffixe des fichiers de travail (leur nom ne ressemble jamais à celui d'une pièce).</summary>
    public const string SuffixeTravail = ".compression";

    /// <summary>Compression des fichiers déposés (réglage <c>Stockage:Compression</c>, défaut oui, relu à chaque dépôt).</summary>
    public bool CompressionActive => config.GetValue("Stockage:Compression", true);

    /// <summary>
    /// Gain minimal, en pour cent de la taille d'origine, pour garder un fichier compressé (réglage
    /// <c>Stockage:CompressionGainMin</c>, défaut 10) : en dessous, le fichier reste tel quel.
    /// </summary>
    public int GainMinimal => Math.Clamp(config.GetValue("Stockage:CompressionGainMin", 10), 1, 90);

    /// <summary>
    /// Copie le flux sur disque ; renvoie la taille et l'empreinte SHA-256 (hexadécimal minuscule). La copie s'arrête dès que le
    /// fichier dépasse la taille maximale (413) ou <paramref name="plafond"/> (place restante sous les quotas : l'erreur de
    /// <paramref name="depassement"/>), sans lire la suite ; le fichier partiel est effacé.
    /// </summary>
    public async Task<(long Taille, string Sha256)> Ecrire(Guid id, Stream source, CancellationToken ct, long plafond = long.MaxValue,
        Func<Exception>? depassement = null)
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
                if (taille > plafond) throw depassement?.Invoke() ?? new ErreurHttp(413, MessageTropVolumineux);
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
    /// Les fichiers de travail d'une compression interrompue (<see cref="SuffixeTravail"/>), sans valeur, sont supprimés.
    /// </summary>
    public int IsolerOrphelins(IReadOnlySet<Guid> connus, TimeSpan ageMinimum, DateTimeOffset maintenant)
    {
        if (!Directory.Exists(Dossier)) return 0;
        var quarantaine = Path.Combine(Dossier, DossierOrphelins);
        var deplaces = 0;
        foreach (var chemin in Directory.EnumerateFiles(Dossier))
        {
            var nom = Path.GetFileName(chemin);
            // Fichier de travail d'une compression interrompue (arrêt pendant le dépôt ou la commande) : inutile.
            if (nom.EndsWith(SuffixeTravail, StringComparison.Ordinal) && nom.Length == 32 + SuffixeTravail.Length
                && maintenant - File.GetLastWriteTimeUtc(chemin) >= ageMinimum)
            {
                SupprimerTravail(chemin);
                continue;
            }
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

    /// <summary>
    /// Supprime tous les fichiers de pièces jointes du dossier (nommés d'après un Id), y compris ceux mis de côté dans
    /// <see cref="DossierOrphelins"/>. Réservé à la réinitialisation de la démonstration, une fois la base vidée.
    /// Renvoie le nombre de fichiers supprimés.
    /// </summary>
    public int SupprimerTout()
    {
        var supprimes = 0;
        foreach (var dossier in new[] { Dossier, Path.Combine(Dossier, DossierOrphelins) }.Where(Directory.Exists))
            foreach (var chemin in Directory.EnumerateFiles(dossier))
            {
                var nom = Path.GetFileName(chemin);
                if (nom.Length != 32 || !Guid.TryParseExact(nom, "N", out _)) continue;
                try
                {
                    File.Delete(chemin);
                    supprimes++;
                }
                catch (IOException) { /* verrouillé : isolé au prochain démarrage (aucune ligne en base) */ }
            }
        return supprimes;
    }

    /// <summary>Sous-dossier où sont mis de côté les fichiers sans pièce jointe en base.</summary>
    public const string DossierOrphelins = "orphelins";

    public string MessageTropVolumineux => $"Fichier trop volumineux : {TailleMaxMo} Mo maximum.";

    /// <summary>
    /// Ouvre le contenu d'origine en lecture (décompressé au fil de la lecture si le fichier est stocké compressé) ; null si
    /// le fichier n'existe plus. Le format se déduit du fichier lui-même et non de la ligne en base : un fichier de la taille
    /// d'origine est rendu tel quel, un fichier d'une autre taille commençant par l'en-tête gzip est décompressé. La lecture
    /// reste donc juste pendant qu'une commande d'exploitation compresse ou décompresse le fichier.
    /// </summary>
    public Stream? Ouvrir(Guid id, long taille)
    {
        var chemin = Chemin(id);
        if (!File.Exists(chemin)) return null;
        var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
        try
        {
            if (flux.Length == taille || !CommencePar(flux, EnteteGzip)) return flux;
            return new GZipStream(flux, CompressionMode.Decompress);
        }
        catch
        {
            flux.Dispose();
            throw;
        }
    }

    // ---------- Compression ----------

    private static readonly byte[] EnteteGzip = [0x1f, 0x8b, 0x08];
    private const CompressionLevel Niveau = CompressionLevel.Optimal;
    /// <summary>Début du fichier compressé à l'essai : s'il n'y gagne pas assez, le reste n'est pas compressé.</summary>
    private const int Echantillon = 1024 * 1024;

    /// <summary>Lit les premiers octets du flux, puis le remet au début.</summary>
    private static bool CommencePar(Stream flux, ReadOnlySpan<byte> entete)
    {
        Span<byte> debut = stackalloc byte[entete.Length];
        var lus = flux.ReadAtLeast(debut, debut.Length, throwOnEndOfStream: false);
        flux.Position = 0;
        return lus == entete.Length && debut.SequenceEqual(entete);
    }

    /// <summary>
    /// Formats déjà compressés, reconnus à leurs premiers octets (et non au nom ou au type annoncés) : zip (dont docx, xlsx,
    /// pptx, odt, jar), gzip, bzip2, xz, zstd, 7z, rar, vidéos MP4/MOV et Matroska/WebM, sons MP3, Ogg et FLAC, images
    /// WebP, AVIF et HEIC. Ils ne sont pas compressés à l'essai.
    /// </summary>
    public static bool FormatDejaCompresse(ReadOnlySpan<byte> debut)
    {
        static bool Debut(ReadOnlySpan<byte> d, ReadOnlySpan<byte> e) => d.StartsWith(e);
        return Debut(debut, [0x50, 0x4b, 0x03, 0x04]) || Debut(debut, [0x50, 0x4b, 0x05, 0x06]) // zip (vide)
            || Debut(debut, [0x1f, 0x8b]) || Debut(debut, "BZh"u8)
            || Debut(debut, [0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00]) || Debut(debut, [0x28, 0xb5, 0x2f, 0xfd]) // xz, zstd
            || Debut(debut, [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]) || Debut(debut, [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07]) // 7z, rar
            || (debut.Length >= 12 && debut[4..8].SequenceEqual("ftyp"u8)) // MP4, MOV, M4A, AVIF, HEIC
            || Debut(debut, [0x1a, 0x45, 0xdf, 0xa3]) // Matroska, WebM
            || Debut(debut, "ID3"u8) || (debut.Length >= 2 && debut[0] == 0xff && (debut[1] & 0xe0) == 0xe0 && debut[1] != 0xff) // MP3
            || Debut(debut, "OggS"u8) || Debut(debut, "fLaC"u8)
            || (Debut(debut, "RIFF"u8) && debut.Length >= 12 && debut[8..12].SequenceEqual("WEBP"u8));
    }

    /// <summary>Vrai si <paramref name="compresse"/> octets économisent au moins <paramref name="gainMinimal"/> % de <paramref name="original"/>.</summary>
    public static bool VautLaPeine(long original, long compresse, int gainMinimal) =>
        original > 0 && compresse < original && (original - compresse) * 100 >= original * (long)gainMinimal;

    /// <summary>Résultat d'un examen : taille sur disque et compression (<see cref="CompressionPiece"/> ; null : à réexaminer).</summary>
    public readonly record struct Stockage(long TailleStockee, string? Compression);

    /// <summary>
    /// Compresse le fichier de la pièce s'il y gagne au moins <see cref="GainMinimal"/> % : la version gzip est écrite à côté,
    /// relue et comparée à l'empreinte d'origine, puis renommée sur le fichier (remplacement atomique). Un format déjà compressé
    /// ou un premier Mo qui ne gagne pas assez arrête l'examen sans compresser le reste. Avec <paramref name="essai"/>, rien
    /// n'est écrit : renvoie la taille qu'aurait le fichier. Un incident (fichier verrouillé, disque plein) laisse le fichier tel
    /// quel et renvoie une compression null, pour réessayer plus tard ; seule l'annulation est propagée.
    /// </summary>
    public async Task<Stockage> Compresser(Guid id, long taille, string sha256, CancellationToken ct, bool essai = false)
    {
        var travail = CheminTravail(id);
        try
        {
            long compresse;
            await using (var source = new FileStream(Chemin(id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                if (source.Length != taille) return new(source.Length, source.Length < taille && CommencePar(source, EnteteGzip) ? CompressionPiece.Gzip : null);
                var debut = new byte[16];
                var lus = await source.ReadAtLeastAsync(debut, debut.Length, throwOnEndOfStream: false, ct);
                if (FormatDejaCompresse(debut.AsSpan(0, lus))) return new(taille, CompressionPiece.Aucune);
                source.Position = 0;

                // Essai sur le premier Mo (tout le fichier s'il est plus petit).
                using var tampon = new MemoryStream();
                await using (var gz = new GZipStream(tampon, Niveau, leaveOpen: true))
                    await Copier(source, gz, Echantillon, ct);
                var echantillon = Math.Min(taille, Echantillon);
                if (!VautLaPeine(echantillon, tampon.Length, GainMinimal)) return new(taille, CompressionPiece.Aucune);

                if (taille <= Echantillon)
                {
                    compresse = tampon.Length;
                    if (!essai && VautLaPeine(taille, compresse, GainMinimal))
                        await File.WriteAllBytesAsync(travail, tampon.ToArray(), ct);
                }
                else
                {
                    source.Position = 0;
                    await using var cible = essai ? (Stream)new Compteur() : new FileStream(travail, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await using (var gz = new GZipStream(cible, Niveau, leaveOpen: true))
                        await source.CopyToAsync(gz, 81920, ct);
                    compresse = cible.Length;
                }
            }
            if (!VautLaPeine(taille, compresse, GainMinimal))
            {
                SupprimerTravail(travail);
                return new(taille, CompressionPiece.Aucune);
            }
            if (essai) return new(compresse, CompressionPiece.Gzip);

            // Vérification avant remplacement : la version compressée redonne exactement le fichier déposé.
            await using (var relu = new GZipStream(new FileStream(travail, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true), CompressionMode.Decompress))
                if (await Empreinte(relu, ct) != sha256) throw new InvalidDataException("La version compressée ne redonne pas le fichier d'origine.");
            File.Move(travail, Chemin(id), overwrite: true);
            return new(compresse, CompressionPiece.Gzip);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SupprimerTravail(travail);
            return new(taille, null);
        }
        catch
        {
            SupprimerTravail(travail);
            throw;
        }
    }

    /// <summary>
    /// Remet le fichier compressé d'une pièce dans sa forme d'origine (avant un retour à une version sans compression, par
    /// exemple) : la version décompressée est vérifiée contre l'empreinte puis renommée sur le fichier. Renvoie faux si le
    /// fichier n'était pas compressé ou n'a pas pu l'être.
    /// </summary>
    public async Task<bool> Decompresser(Guid id, long taille, string sha256, CancellationToken ct)
    {
        var travail = CheminTravail(id);
        try
        {
            await using (var source = Ouvrir(id, taille))
            {
                if (source is not GZipStream) return false;
                await using var cible = new FileStream(travail, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(cible, 81920, ct);
            }
            await using (var relu = new FileStream(travail, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                if (relu.Length != taille || await Empreinte(relu, ct) != sha256)
                    throw new InvalidDataException("Le fichier décompressé ne correspond pas au fichier déposé.");
            File.Move(travail, Chemin(id), overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SupprimerTravail(travail);
            return false;
        }
        catch
        {
            SupprimerTravail(travail);
            throw;
        }
    }

    /// <summary>Octets libres sur le disque du dossier des pièces jointes ; null si on ne peut pas le savoir.</summary>
    public long? EspaceLibre()
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            return new DriveInfo(Path.GetFullPath(Dossier)).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Taille du fichier sur disque ; null s'il n'existe pas.</summary>
    public long? TailleSurDisque(Guid id)
    {
        var f = new FileInfo(Chemin(id));
        return f.Exists ? f.Length : null;
    }

    private static async Task<string> Empreinte(Stream flux, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var tampon = new byte[81920];
        int lus;
        while ((lus = await flux.ReadAsync(tampon, ct)) > 0) sha.AppendData(tampon, 0, lus);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task Copier(Stream source, Stream cible, long maximum, CancellationToken ct)
    {
        var tampon = new byte[81920];
        int lus;
        while (maximum > 0 && (lus = await source.ReadAsync(tampon.AsMemory(0, (int)Math.Min(tampon.Length, maximum)), ct)) > 0)
        {
            await cible.WriteAsync(tampon.AsMemory(0, lus), ct);
            maximum -= lus;
        }
    }

    private static void SupprimerTravail(string chemin)
    {
        try { File.Delete(chemin); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* repris par IsolerOrphelins */ }
    }

    /// <summary>Flux d'écriture qui ne garde rien : compte les octets (compression à l'essai).</summary>
    private sealed class Compteur : Stream
    {
        private long _longueur;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _longueur;
        public override long Position { get => _longueur; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _longueur += count;
        public override void Write(ReadOnlySpan<byte> buffer) => _longueur += buffer.Length;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            _longueur += buffer.Length;
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            _longueur += count;
            return Task.CompletedTask;
        }
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
