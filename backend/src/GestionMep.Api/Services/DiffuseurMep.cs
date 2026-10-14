using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace GestionMep.Api.Services;

/// <summary>
/// Diffuse en mémoire, par MEP, aux connexions SSE ouvertes : la dernière version modifiée et la présence (qui a la MEP
/// ouverte, et sur quel élément chacun travaille). Un abonné lent ne reçoit que l'état le plus récent.
/// La présence vit avec la connexion : fermer l'onglet (ou perdre le réseau) ferme le flux et retire la personne.
/// (Une seule instance du serveur : pour plusieurs instances, remplacer par LISTEN/NOTIFY PostgreSQL.)
/// </summary>
public sealed partial class DiffuseurMep(IConfiguration config)
{
    /// <summary>Flux ouverts en même temps par un compte (plusieurs onglets) et pour tout le serveur : au-delà, 429.</summary>
    public const int MaxParCompte = 20;
    /// <summary>Éléments qu'un onglet peut déclarer en même temps.</summary>
    public const int MaxElements = 10;
    /// <summary>Longueur maximale d'une clé d'élément.</summary>
    public const int LongueurMaxElement = 100;
    /// <summary>Réglage <c>Flux:ConnexionsMax</c>, relu à chaque ouverture.</summary>
    private int MaxTotal => Math.Max(1, config.GetValue("Flux:ConnexionsMax", 1000));

    private readonly Dictionary<Guid, List<Abonne>> _abonnes = [];
    private readonly Dictionary<Guid, int> _parCompte = [];
    private int _total;
    private long _arrivees;
    private readonly object _verrou = new();

    /// <summary>Clé d'élément acceptée : lettres, chiffres, « : », « - », « _ » et « . » (ex. <c>etape:3f2a…</c>, <c>preparation:intervention</c>).</summary>
    [GeneratedRegex("^[A-Za-z0-9:_.-]+$")]
    private static partial Regex CleElement();

    /// <summary>
    /// Ouvre un abonnement, ou renvoie null si le compte ou le serveur a atteint son nombre maximal de flux.
    /// <paramref name="session"/> identifie l'onglet (choisi par le navigateur) : il sert ensuite à déclarer ses éléments.
    /// </summary>
    public Abonnement? Abonner(Guid mepId, Guid utilisateurId, string nom = "", string? session = null)
    {
        var maxTotal = MaxTotal;
        Abonne abonne;
        lock (_verrou)
        {
            var duCompte = _parCompte.GetValueOrDefault(utilisateurId);
            if (duCompte >= MaxParCompte || _total >= maxTotal) return null;
            _parCompte[utilisateurId] = duCompte + 1;
            _total++;
            abonne = new Abonne(utilisateurId, nom, session ?? Guid.NewGuid().ToString("N"), ++_arrivees);
            if (!_abonnes.TryGetValue(mepId, out var liste)) _abonnes[mepId] = liste = [];
            liste.Add(abonne);
        }
        NotifierPresence(mepId);
        return new Abonnement(abonne, () => Desabonner(mepId, abonne));
    }

    public void Notifier(Guid mepId, int version)
    {
        foreach (var a in Cibles(mepId))
        {
            lock (a) a.Version = Math.Max(a.Version ?? 0, version);
            a.Signal.Writer.TryWrite(true);
        }
    }

    /// <summary>
    /// Déclare les éléments sur lesquels travaille l'onglet <paramref name="session"/> de ce compte (liste vide : aucun).
    /// Renvoie false si aucun flux de cet onglet n'est ouvert sur la MEP. Lève <see cref="ErreurHttp"/> (400) sur une clé invalide.
    /// </summary>
    public bool DefinirElements(Guid mepId, Guid utilisateurId, string session, IReadOnlyList<string>? elements)
    {
        var liste = (elements ?? []).Select(e => (e ?? "").Trim()).Where(e => e.Length > 0).Distinct().ToList();
        if (liste.Count > MaxElements) throw ErreurHttp.Saisie($"Au plus {MaxElements} éléments à la fois.");
        if (liste.Any(e => e.Length > LongueurMaxElement || !CleElement().IsMatch(e)))
            throw ErreurHttp.Saisie("Élément invalide : lettres, chiffres, « : », « - », « _ » et « . » seulement.");

        var change = false;
        var trouve = false;
        foreach (var a in Cibles(mepId).Where(a => a.UtilisateurId == utilisateurId && a.Session == session))
        {
            trouve = true;
            lock (a)
            {
                if (a.Elements.SequenceEqual(liste)) continue;
                a.Elements = liste;
                change = true;
            }
        }
        if (change) NotifierPresence(mepId);
        return trouve;
    }

    /// <summary>Personnes qui ont la MEP ouverte, par ordre d'arrivée, avec les éléments de tous leurs onglets.</summary>
    public List<PresenceDto> Presences(Guid mepId) =>
        [.. Cibles(mepId)
            .OrderBy(a => a.Arrivee)
            .GroupBy(a => a.UtilisateurId)
            .Select(g =>
            {
                var elements = g.SelectMany(a => { lock (a) return a.Elements; }).Distinct().ToList();
                return new PresenceDto(g.Key, g.First().Nom, elements);
            })];

    public int NombreAbonnes(Guid mepId)
    {
        lock (_verrou) return _abonnes.TryGetValue(mepId, out var l) ? l.Count : 0;
    }

    private Abonne[] Cibles(Guid mepId)
    {
        lock (_verrou) return _abonnes.TryGetValue(mepId, out var liste) ? [.. liste] : [];
    }

    private void NotifierPresence(Guid mepId)
    {
        foreach (var a in Cibles(mepId))
        {
            lock (a) a.Presence = true;
            a.Signal.Writer.TryWrite(true);
        }
    }

    private void Desabonner(Guid mepId, Abonne abonne)
    {
        lock (_verrou)
        {
            if (!_abonnes.TryGetValue(mepId, out var liste) || !liste.Remove(abonne)) return;
            if (liste.Count == 0) _abonnes.Remove(mepId);
            _total--;
            if (--_parCompte[abonne.UtilisateurId] == 0) _parCompte.Remove(abonne.UtilisateurId);
        }
        abonne.Signal.Writer.TryComplete();
        NotifierPresence(mepId);
    }

    /// <summary>Une connexion SSE : son onglet, ses éléments et ce qu'il reste à lui envoyer.</summary>
    internal sealed class Abonne(Guid utilisateurId, string nom, string session, long arrivee)
    {
        public Guid UtilisateurId { get; } = utilisateurId;
        public string Nom { get; } = nom;
        public string Session { get; } = session;
        public long Arrivee { get; } = arrivee;
        public List<string> Elements { get; set; } = [];
        /// <summary>Dernière version à envoyer (null : rien de nouveau).</summary>
        public int? Version { get; set; }
        /// <summary>La présence a changé depuis le dernier envoi.</summary>
        public bool Presence { get; set; }
        /// <summary>Réveil du flux : capacité 1, les signaux en trop sont ignorés (l'état à envoyer est dans les champs).</summary>
        public Channel<bool> Signal { get; } = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    /// <summary>Ce qu'un flux doit envoyer à son réveil.</summary>
    public readonly record struct Envoi(int? Version, bool Presence);

    public sealed class Abonnement : IDisposable
    {
        private readonly Abonne _abonne;
        private readonly Action _liberer;
        internal Abonnement(Abonne abonne, Action liberer)
        {
            _abonne = abonne;
            _liberer = liberer;
        }

        public string Session => _abonne.Session;

        /// <summary>Attend un changement ; false quand l'abonnement est fermé.</summary>
        public async Task<bool> AttendreAsync(CancellationToken ct)
        {
            if (!await _abonne.Signal.Reader.WaitToReadAsync(ct)) return false;
            _abonne.Signal.Reader.TryRead(out _);
            return true;
        }

        /// <summary>Prend ce qu'il reste à envoyer et le remet à zéro.</summary>
        public Envoi Prendre()
        {
            lock (_abonne)
            {
                var envoi = new Envoi(_abonne.Version, _abonne.Presence);
                _abonne.Version = null;
                _abonne.Presence = false;
                return envoi;
            }
        }

        public void Dispose() => _liberer();
    }
}
