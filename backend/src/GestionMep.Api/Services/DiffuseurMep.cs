using System.Threading.Channels;

namespace GestionMep.Api.Services;

/// <summary>
/// Diffuse en mémoire, par MEP, la dernière version modifiée aux connexions SSE ouvertes.
/// Canal de capacité 1 qui garde la valeur la plus récente : un abonné lent ne voit que la dernière version.
/// (Une seule instance du serveur : pour plusieurs instances, remplacer par LISTEN/NOTIFY PostgreSQL.)
/// </summary>
public sealed class DiffuseurMep(IConfiguration config)
{
    /// <summary>Flux ouverts en même temps par un compte (plusieurs onglets) et pour tout le serveur : au-delà, 429.</summary>
    public const int MaxParCompte = 20;
    private readonly int _maxTotal = Math.Max(1, config.GetValue("Flux:ConnexionsMax", 1000));

    private readonly Dictionary<Guid, List<Channel<int>>> _abonnes = [];
    private readonly Dictionary<Guid, int> _parCompte = [];
    private int _total;
    private readonly object _verrou = new();

    /// <summary>Ouvre un abonnement, ou renvoie null si le compte ou le serveur a atteint son nombre maximal de flux.</summary>
    public Abonnement? Abonner(Guid mepId, Guid utilisateurId)
    {
        var canal = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_verrou)
        {
            var duCompte = _parCompte.GetValueOrDefault(utilisateurId);
            if (duCompte >= MaxParCompte || _total >= _maxTotal) return null;
            _parCompte[utilisateurId] = duCompte + 1;
            _total++;
            if (!_abonnes.TryGetValue(mepId, out var liste)) _abonnes[mepId] = liste = [];
            liste.Add(canal);
        }
        return new Abonnement(canal.Reader, () => Desabonner(mepId, utilisateurId, canal));
    }

    public void Notifier(Guid mepId, int version)
    {
        Channel<int>[] cibles;
        lock (_verrou)
            cibles = _abonnes.TryGetValue(mepId, out var liste) ? [.. liste] : [];
        foreach (var c in cibles) c.Writer.TryWrite(version);
    }

    public int NombreAbonnes(Guid mepId)
    {
        lock (_verrou) return _abonnes.TryGetValue(mepId, out var l) ? l.Count : 0;
    }

    private void Desabonner(Guid mepId, Guid utilisateurId, Channel<int> canal)
    {
        lock (_verrou)
        {
            if (!_abonnes.TryGetValue(mepId, out var liste) || !liste.Remove(canal)) return;
            if (liste.Count == 0) _abonnes.Remove(mepId);
            _total--;
            if (--_parCompte[utilisateurId] == 0) _parCompte.Remove(utilisateurId);
        }
        canal.Writer.TryComplete();
    }

    public sealed class Abonnement(ChannelReader<int> lecteur, Action liberer) : IDisposable
    {
        public ChannelReader<int> Lecteur { get; } = lecteur;
        public void Dispose() => liberer();
    }
}
