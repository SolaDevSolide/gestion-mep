using System.Threading.Channels;

namespace GestionMep.Api.Services;

/// <summary>
/// Diffuse en mémoire, par MEP, la dernière version modifiée aux connexions SSE ouvertes.
/// Canal de capacité 1 qui garde la valeur la plus récente : un abonné lent ne voit que la dernière version.
/// (Une seule instance du serveur : pour plusieurs instances, remplacer par LISTEN/NOTIFY PostgreSQL.)
/// </summary>
public sealed class DiffuseurMep
{
    private readonly Dictionary<Guid, List<Channel<int>>> _abonnes = [];
    private readonly object _verrou = new();

    public Abonnement Abonner(Guid mepId)
    {
        var canal = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_verrou)
        {
            if (!_abonnes.TryGetValue(mepId, out var liste)) _abonnes[mepId] = liste = [];
            liste.Add(canal);
        }
        return new Abonnement(canal.Reader, () => Desabonner(mepId, canal));
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

    private void Desabonner(Guid mepId, Channel<int> canal)
    {
        lock (_verrou)
        {
            if (!_abonnes.TryGetValue(mepId, out var liste)) return;
            liste.Remove(canal);
            if (liste.Count == 0) _abonnes.Remove(mepId);
        }
        canal.Writer.TryComplete();
    }

    public sealed class Abonnement(ChannelReader<int> lecteur, Action liberer) : IDisposable
    {
        public ChannelReader<int> Lecteur { get; } = lecteur;
        public void Dispose() => liberer();
    }
}
