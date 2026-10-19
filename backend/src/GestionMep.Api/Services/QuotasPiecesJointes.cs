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
/// Les dépôts en cours sont réservés en mémoire : deux dépôts simultanés ne peuvent pas dépasser ensemble un plafond. Un dépôt
/// est vérifié avant d'en lire le corps (taille annoncée), puis pendant sa réception : il s'arrête dès qu'un plafond est dépassé.
/// </summary>
public sealed class QuotasPiecesJointes(IConfiguration config, StockagePiecesJointes stockage) : IDisposable
{
    private const long Mo = 1024L * 1024L;
    // Réglages modifiables sans redémarrage : relus à chaque dépôt.
    public long QuotaMep => Math.Max(0, config.GetValue("Stockage:QuotaMepMo", 1024L)) * Mo;
    public long QuotaCompteJour => Math.Max(0, config.GetValue("Stockage:QuotaCompteJourMo", 1024L)) * Mo;
    public long EspaceLibreMin => Math.Max(0, config.GetValue("Stockage:EspaceLibreMinMo", 2048L)) * Mo;

    private sealed record Reservation(Guid? MepId, Guid CompteId, long Taille);

    private readonly SemaphoreSlim _verrou = new(1, 1);
    private readonly List<Reservation> _reservations = [];

    /// <summary>
    /// Réserve la place d'un fichier de taille connue ou refuse le dépôt (422 MEP pleine, 429 trop de dépôts du compte, 507
    /// disque presque plein). La réservation est libérée à la fin du dépôt (Dispose), une fois la pièce enregistrée en base ou
    /// abandonnée.
    /// </summary>
    public async Task<IDisposable> Reserver(MepDbContext db, Guid? mepId, Guid compteId, long taille, DateTimeOffset maintenant, CancellationToken ct) =>
        await ReserverDepot(db, mepId, compteId, taille, 0, maintenant, ct);

    /// <summary>
    /// Place restante pour un dépôt et refus à opposer si le fichier la dépasse. <c>Plafond</c> : taille au-delà de laquelle
    /// la réception doit s'arrêter (<see cref="long.MaxValue"/> sans plafond) ; <c>Depassement</c> : l'erreur du plafond le plus
    /// proche. La réservation est libérée par Dispose.
    /// </summary>
    public sealed class Depot(IDisposable liberation, long plafond, Func<ErreurHttp> depassement) : IDisposable
    {
        public long Plafond { get; } = plafond;
        public ErreurHttp Depassement() => depassement();
        public void Dispose() => liberation.Dispose();
    }

    /// <summary>
    /// Avant de lire le corps d'un dépôt : réserve la place de ce qui est annoncé (<paramref name="annonce"/>, l'en-tête
    /// <c>Content-Length</c> moins <paramref name="marge"/> pour l'enveloppe du formulaire ; null si la taille n'est pas annoncée)
    /// et refuse aussitôt un dépôt qui dépasserait un plafond, sans attendre de l'avoir reçu. Le fichier reçu ne peut ensuite pas
    /// dépasser <see cref="Depot.Plafond"/> : la réception s'arrête dès qu'il le dépasse (voir
    /// <see cref="StockagePiecesJointes.Ecrire"/>).
    /// </summary>
    public async Task<Depot> ReserverDepot(MepDbContext db, Guid? mepId, Guid compteId, long? annonce, long marge, DateTimeOffset maintenant,
        CancellationToken ct)
    {
        await _verrou.WaitAsync(ct);
        try
        {
            long quotaMep = QuotaMep, quotaCompteJour = QuotaCompteJour, espaceLibreMin = EspaceLibreMin;
            // Place restante sous chaque plafond actif, avec l'erreur qu'il oppose.
            var restes = new List<(long Reste, Func<ErreurHttp> Erreur)>();
            if (espaceLibreMin > 0 && EspaceLibre() is { } libre)
                restes.Add((libre - _reservations.Sum(r => r.Taille) - espaceLibreMin,
                    () => new ErreurHttp(507, "Espace disque presque plein sur le serveur : dépôt refusé. Prévenez l'administrateur.")));
            if (quotaMep > 0 && mepId is { } mep)
            {
                var occupe = await db.PiecesJointes.Where(p => p.MepId == mep).SumAsync(p => p.Taille, ct)
                    + _reservations.Where(r => r.MepId == mep).Sum(r => r.Taille);
                restes.Add((quotaMep - occupe,
                    () => ErreurHttp.Regle([$"Cette MEP a atteint sa limite de pièces jointes ({quotaMep / Mo} Mo) : supprimez des pièces inutiles ou demandez à l'administrateur de relever la limite."])));
            }
            if (quotaCompteJour > 0)
            {
                var depuis = maintenant.AddDays(-1);
                var depose = await db.PiecesJointes.Where(p => p.DeposeParId == compteId && p.DeposeLe > depuis).SumAsync(p => p.Taille, ct)
                    + _reservations.Where(r => r.CompteId == compteId).Sum(r => r.Taille);
                restes.Add((quotaCompteJour - depose,
                    () => new ErreurHttp(429, $"Vous avez déposé {quotaCompteJour / Mo} Mo de pièces jointes en 24 heures, la limite : réessayez plus tard.")));
            }
            // Taille minimale du fichier d'après l'en-tête (le corps compte aussi l'enveloppe du formulaire).
            var auMoins = annonce is { } a ? Math.Max(1, a - marge) : 1;
            foreach (var (reste, erreur) in restes)
                if (auMoins > reste) throw erreur();
            var plus = restes.Count == 0 ? (Reste: long.MaxValue, Erreur: (Func<ErreurHttp>)(() => ErreurHttp.Saisie("Dépôt refusé."))) : restes.MinBy(r => r.Reste);
            // Réservé : ce qui est annoncé (le fichier ne peut pas être plus gros que le corps), au plus la place restante.
            var reservation = new Reservation(mepId, compteId, Math.Min(annonce ?? plus.Reste, plus.Reste));
            _reservations.Add(reservation);
            return new Depot(new Liberation(this, reservation), plus.Reste, plus.Erreur);
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
