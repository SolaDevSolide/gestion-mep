using GestionMep.Api.Donnees;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Plafonds de stockage des pièces jointes, vérifiés avant d'écrire chaque fichier (0 = sans plafond) :
/// <list type="bullet">
/// <item>par MEP (<c>Stockage:QuotaMepMo</c>, défaut 1024 Mo) ;</item>
/// <item>par compte sur 24 heures glissantes (<c>Stockage:QuotaCompteJourMo</c>, défaut 1024 Mo) ;</item>
/// <item>espace libre minimal laissé sur le disque des pièces jointes (<c>Stockage:EspaceLibreMinMo</c>, défaut 2048 Mo),
/// pour que la base, les sauvegardes et l'application continuent de fonctionner.</item>
/// </list>
/// Les dépôts en cours sont réservés en mémoire : deux dépôts simultanés ne peuvent pas dépasser ensemble un plafond.
/// </summary>
public sealed class QuotasPiecesJointes(IConfiguration config, StockagePiecesJointes stockage) : IDisposable
{
    private const long Mo = 1024L * 1024L;
    public long QuotaMep { get; } = Math.Max(0, config.GetValue("Stockage:QuotaMepMo", 1024L)) * Mo;
    public long QuotaCompteJour { get; } = Math.Max(0, config.GetValue("Stockage:QuotaCompteJourMo", 1024L)) * Mo;
    public long EspaceLibreMin { get; } = Math.Max(0, config.GetValue("Stockage:EspaceLibreMinMo", 2048L)) * Mo;

    private sealed record Reservation(Guid? MepId, Guid CompteId, long Taille);

    private readonly SemaphoreSlim _verrou = new(1, 1);
    private readonly List<Reservation> _reservations = [];

    /// <summary>
    /// Réserve la place d'un fichier ou refuse le dépôt (422 MEP pleine, 429 trop de dépôts du compte, 507 disque presque plein).
    /// La réservation est libérée à la fin du dépôt (Dispose), une fois la pièce enregistrée en base ou abandonnée.
    /// </summary>
    public async Task<IDisposable> Reserver(MepDbContext db, Guid? mepId, Guid compteId, long taille, DateTimeOffset maintenant, CancellationToken ct)
    {
        await _verrou.WaitAsync(ct);
        try
        {
            if (EspaceLibreMin > 0 && EspaceLibre() is { } libre && libre - _reservations.Sum(r => r.Taille) - taille < EspaceLibreMin)
                throw new ErreurHttp(507, "Espace disque presque plein sur le serveur : dépôt refusé. Prévenez l'administrateur.");
            if (QuotaMep > 0 && mepId is { } mep)
            {
                var occupe = await db.PiecesJointes.Where(p => p.MepId == mep).SumAsync(p => p.Taille, ct)
                    + _reservations.Where(r => r.MepId == mep).Sum(r => r.Taille);
                if (occupe + taille > QuotaMep)
                    throw ErreurHttp.Regle([$"Cette MEP a atteint sa limite de pièces jointes ({QuotaMep / Mo} Mo) : supprimez des pièces inutiles ou demandez à l'administrateur de relever la limite."]);
            }
            if (QuotaCompteJour > 0)
            {
                var depuis = maintenant.AddDays(-1);
                var depose = await db.PiecesJointes.Where(p => p.DeposeParId == compteId && p.DeposeLe > depuis).SumAsync(p => p.Taille, ct)
                    + _reservations.Where(r => r.CompteId == compteId).Sum(r => r.Taille);
                if (depose + taille > QuotaCompteJour)
                    throw new ErreurHttp(429, $"Vous avez déposé {QuotaCompteJour / Mo} Mo de pièces jointes en 24 heures, la limite : réessayez plus tard.");
            }
            var reservation = new Reservation(mepId, compteId, taille);
            _reservations.Add(reservation);
            return new Liberation(this, reservation);
        }
        finally
        {
            _verrou.Release();
        }
    }

    public void Dispose() => _verrou.Dispose();

    /// <summary>Octets libres sur le disque du dossier des pièces jointes ; null si on ne peut pas le savoir.</summary>
    private long? EspaceLibre()
    {
        try
        {
            Directory.CreateDirectory(stockage.Dossier);
            return new DriveInfo(Path.GetFullPath(stockage.Dossier)).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void Liberer(Reservation r)
    {
        _verrou.Wait();
        try { _reservations.Remove(r); }
        finally { _verrou.Release(); }
    }

    private sealed class Liberation(QuotasPiecesJointes quotas, Reservation r) : IDisposable
    {
        private int _fait;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _fait, 1) == 0) quotas.Liberer(r);
        }
    }
}
