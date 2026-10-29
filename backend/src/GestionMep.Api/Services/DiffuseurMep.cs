using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace GestionMep.Api.Services;

/// <summary>
/// Diffuse en mémoire, par MEP, aux connexions SSE ouvertes : la dernière version modifiée, la présence (qui a la MEP
/// ouverte, et sur quel élément chacun travaille) et les verrous d'édition des étapes. Un abonné lent ne reçoit que l'état le
/// plus récent. La présence vit avec la connexion : fermer l'onglet (ou perdre le réseau) ferme le flux, retire la personne et
/// relâche les verrous de cet onglet.
/// (Une seule instance du serveur : pour plusieurs instances, remplacer par LISTEN/NOTIFY PostgreSQL.)
/// </summary>
public sealed partial class DiffuseurMep(IConfiguration config, TimeProvider? temps = null)
{
    /// <summary>Durée d'un verrou d'édition sans signe de vie : le panneau ouvert le renouvelle (toutes les 20 s environ).</summary>
    public static readonly TimeSpan DureeVerrou = TimeSpan.FromMinutes(1);
    private readonly TimeProvider _temps = temps ?? TimeProvider.System;
    /// <summary>Verrous d'édition par MEP, puis par étape.</summary>
    private readonly Dictionary<Guid, Dictionary<Guid, VerrouEtapeDto>> _verrous = [];

    /// <summary>Flux ouverts en même temps par un compte (plusieurs onglets) et pour tout le serveur : au-delà, 429.</summary>
    public const int MaxParCompte = 20;
    /// <summary>Éléments qu'un onglet peut déclarer en même temps.</summary>
    public const int MaxElements = 10;
    /// <summary>Longueur maximale d'une clé d'élément.</summary>
    public const int LongueurMaxElement = 100;
    /// <summary>Réglage <c>Flux:ConnexionsMax</c>, relu à chaque ouverture.</summary>
    private int MaxTotal => Math.Max(1, config.GetValue("Flux:ConnexionsMax", 1000));

    /// <summary>
    /// Pseudo-MEP des abonnés du suivi en direct (#61) : prévenus des modifications qui changent ce que le suivi affiche, sans
    /// présence ni verrous (voir <see cref="AbonnerSuivi"/>).
    /// </summary>
    public static readonly Guid Suivi = Guid.Empty;
    /// <summary>Nombre de modifications diffusées au suivi depuis le démarrage : « version » envoyée à ses abonnés.</summary>
    private int _modifications;

    /// <summary>
    /// Ce qu'une modification change dans le suivi en direct : rien (MEP en préparation non annoncée…), la carte de cette MEP
    /// lancée (le navigateur ne relit qu'elle) ou ses listes (MEP lancée, clôturée, annoncée ou retirée : il relit tout).
    /// </summary>
    public enum PorteeSuivi { Aucune, Carte, Liste }

    /// <summary>MEP dont la carte a changé, au plus ce nombre par envoi : au-delà, le navigateur relit tout le suivi.</summary>
    public const int MaxCartesParEnvoi = 20;

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

    /// <summary>Abonnement au suivi en direct : un événement à chaque modification d'une MEP, quelle qu'elle soit.</summary>
    public Abonnement? AbonnerSuivi(Guid utilisateurId) => Abonner(Suivi, utilisateurId);

    /// <summary>
    /// Nouvelle version d'une MEP : ses flux la reçoivent ; ceux du suivi, selon <paramref name="suivi"/>, l'identifiant de la
    /// MEP dont la carte a changé, ou l'ordre de tout relire (par défaut, pour les écritures qui ne savent pas le dire).
    /// </summary>
    public void Notifier(Guid mepId, int version, PorteeSuivi suivi = PorteeSuivi.Liste)
    {
        Signaler(mepId, version, null, false);
        if (mepId == Suivi || suivi == PorteeSuivi.Aucune) return;
        Signaler(Suivi, Interlocked.Increment(ref _modifications), mepId, suivi == PorteeSuivi.Liste);
    }

    private void Signaler(Guid mepId, int version, Guid? carte, bool liste)
    {
        foreach (var a in Cibles(mepId))
        {
            lock (a)
            {
                a.Version = Math.Max(a.Version ?? 0, version);
                if (liste) a.Liste = true;
                else if (carte is { } c) a.Cartes.Add(c);
            }
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
        var verrous = false;
        lock (_verrou)
        {
            if (!_abonnes.TryGetValue(mepId, out var liste) || !liste.Remove(abonne)) return;
            if (liste.Count == 0) _abonnes.Remove(mepId);
            _total--;
            if (--_parCompte[abonne.UtilisateurId] == 0) _parCompte.Remove(abonne.UtilisateurId);
            // Dernier flux de cet onglet fermé : ses verrous d'édition sont relâchés.
            if (!liste.Any(a => a.UtilisateurId == abonne.UtilisateurId && a.Session == abonne.Session)
                && _verrous.TryGetValue(mepId, out var parEtape))
            {
                foreach (var (etape, v) in parEtape.Where(x => x.Value.UtilisateurId == abonne.UtilisateurId && x.Value.Session == abonne.Session).ToList())
                    verrous |= parEtape.Remove(etape);
                if (parEtape.Count == 0) _verrous.Remove(mepId);
            }
        }
        abonne.Signal.Writer.TryComplete();
        NotifierPresence(mepId);
        if (verrous) NotifierVerrous(mepId);
    }

    // ---------- Verrous d'édition des étapes ----------

    /// <summary>
    /// Prend ou renouvelle le verrou d'édition d'une étape pour cet onglet. Libre, expiré ou déjà à cet onglet : pris (ou
    /// renouvelé). Tenu par quelqu'un d'autre : pris seulement avec <paramref name="prendreLaMain"/> (sans confirmation), le verrou
    /// garde alors qui l'avait. Renvoie le verrou de l'étape après l'opération et si cet onglet le tient.
    /// </summary>
    public (bool Pris, VerrouEtapeDto Verrou) PrendreVerrou(Guid mepId, Guid etapeId, Guid utilisateurId, string nom, string session, bool prendreLaMain)
    {
        var maintenant = _temps.GetUtcNow();
        bool change, pris;
        VerrouEtapeDto verrou;
        lock (_verrou)
        {
            change = Purger(mepId, maintenant);
            if (!_verrous.TryGetValue(mepId, out var parEtape)) _verrous[mepId] = parEtape = [];
            parEtape.TryGetValue(etapeId, out var actuel);
            var aMoi = actuel is not null && actuel.UtilisateurId == utilisateurId && actuel.Session == session;
            pris = actuel is null || aMoi || prendreLaMain;
            if (pris)
            {
                verrou = aMoi
                    ? actuel! with { ExpireLe = maintenant + DureeVerrou }
                    : new VerrouEtapeDto(etapeId, utilisateurId, nom, session, maintenant, maintenant + DureeVerrou,
                        actuel?.UtilisateurId, actuel?.Nom, actuel?.Session);
                parEtape[etapeId] = verrou;
                change |= !aMoi;
            }
            else verrou = actuel!;
        }
        if (change) NotifierVerrous(mepId);
        return (pris, verrou);
    }

    /// <summary>Relâche le verrou d'une étape s'il est tenu par cet onglet (fermeture du panneau). Renvoie faux sinon.</summary>
    public bool RelacherVerrou(Guid mepId, Guid etapeId, Guid utilisateurId, string session)
    {
        bool relache;
        lock (_verrou)
        {
            relache = _verrous.TryGetValue(mepId, out var parEtape) && parEtape.TryGetValue(etapeId, out var v)
                && v.UtilisateurId == utilisateurId && v.Session == session && parEtape.Remove(etapeId);
            if (relache && parEtape!.Count == 0) _verrous.Remove(mepId);
        }
        if (relache) NotifierVerrous(mepId);
        return relache;
    }

    /// <summary>Verrous d'édition en cours sur la MEP (les verrous expirés sont retirés et diffusés).</summary>
    public List<VerrouEtapeDto> Verrous(Guid mepId)
    {
        List<VerrouEtapeDto> liste;
        bool change;
        lock (_verrou)
        {
            change = Purger(mepId, _temps.GetUtcNow());
            liste = _verrous.TryGetValue(mepId, out var parEtape) ? [.. parEtape.Values.OrderBy(v => v.PrisLe)] : [];
        }
        if (change) NotifierVerrous(mepId);
        return liste;
    }

    /// <summary>Retire les verrous expirés de la MEP (sous <see cref="_verrou"/>). Renvoie vrai si un verrou a été retiré.</summary>
    private bool Purger(Guid mepId, DateTimeOffset maintenant)
    {
        if (!_verrous.TryGetValue(mepId, out var parEtape)) return false;
        var expires = parEtape.Where(x => x.Value.ExpireLe <= maintenant).Select(x => x.Key).ToList();
        foreach (var e in expires) parEtape.Remove(e);
        if (parEtape.Count == 0) _verrous.Remove(mepId);
        return expires.Count > 0;
    }

    /// <summary>
    /// Un envoi vers une API externe de cette MEP est terminé (journal, « Blocages et échecs ») : ses flux relisent le
    /// journal et les envois, sans nouvelle version de la MEP (rien n'a changé dans la MEP elle-même).
    /// </summary>
    public void NotifierAppels(Guid mepId)
    {
        foreach (var a in Cibles(mepId))
        {
            lock (a) a.Appels = true;
            a.Signal.Writer.TryWrite(true);
        }
    }

    private void NotifierVerrous(Guid mepId)
    {
        foreach (var a in Cibles(mepId))
        {
            lock (a) a.Verrous = true;
            a.Signal.Writer.TryWrite(true);
        }
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
        /// <summary>Suivi : MEP dont la carte a changé depuis le dernier envoi.</summary>
        public HashSet<Guid> Cartes { get; } = [];
        /// <summary>Suivi : les listes ont changé depuis le dernier envoi (tout relire).</summary>
        public bool Liste { get; set; }
        /// <summary>La présence a changé depuis le dernier envoi.</summary>
        public bool Presence { get; set; }
        /// <summary>Les verrous d'édition ont changé depuis le dernier envoi.</summary>
        public bool Verrous { get; set; }
        /// <summary>Un envoi vers une API externe s'est terminé depuis le dernier envoi.</summary>
        public bool Appels { get; set; }
        /// <summary>Réveil du flux : capacité 1, les signaux en trop sont ignorés (l'état à envoyer est dans les champs).</summary>
        public Channel<bool> Signal { get; } = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    /// <summary>
    /// Ce qu'un flux doit envoyer à son réveil. Suivi : <c>Cartes</c>, les MEP dont la carte a changé ; <c>Liste</c>, tout relire
    /// (aussi quand trop de cartes ont changé d'un coup).
    /// </summary>
    public readonly record struct Envoi(int? Version, bool Presence, bool Verrous = false, IReadOnlyList<Guid>? Cartes = null, bool Liste = false,
        bool Appels = false);

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
                var liste = _abonne.Liste || _abonne.Cartes.Count > MaxCartesParEnvoi;
                // Sans carte à relire : null (un envoi de présence ou de verrous reste égal à Envoi(null, true)).
                var envoi = new Envoi(_abonne.Version, _abonne.Presence, _abonne.Verrous,
                    liste || _abonne.Cartes.Count == 0 ? null : [.. _abonne.Cartes], liste, _abonne.Appels);
                _abonne.Version = null;
                _abonne.Cartes.Clear();
                _abonne.Liste = false;
                _abonne.Presence = false;
                _abonne.Verrous = false;
                _abonne.Appels = false;
                return envoi;
            }
        }

        public void Dispose() => _liberer();
    }
}
