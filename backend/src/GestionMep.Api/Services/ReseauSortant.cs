using System.Net;
using System.Net.Sockets;

namespace GestionMep.Api.Services;

/// <summary>Résolution des noms des connexions (remplaçable en test pour simuler un changement d'adresse DNS).</summary>
public interface IResolveurNoms
{
    Task<IPAddress[]> Resoudre(string hote, CancellationToken ct);
}

public sealed class ResolveurDns : IResolveurNoms
{
    public Task<IPAddress[]> Resoudre(string hote, CancellationToken ct) => Dns.GetHostAddressesAsync(hote, ct);
}

/// <summary>Requête refusée avant de partir : adresse interne non autorisée, ou http hors réseau autorisé.</summary>
public sealed class AdresseRefuseeException(string message) : Exception(message);

/// <summary>
/// Sortie réseau des connexions aux API externes, protégée contre les appels vers le réseau interne (SSRF) : les adresses
/// de bouclage, privées, de lien local (métadonnées des clouds), CGNAT, 0/8, multidiffusion et réservées sont refusées, sauf
/// si elles sont dans le réglage <c>api-reseaux-autorises</c>. Le contrôle porte sur l'adresse réellement connectée (la
/// résolution est faite ici, au moment de la connexion) : un nom qui change d'adresse entre la saisie et l'envoi ne passe
/// pas. Redirections jamais suivies, pas de cookies, certificat toujours vérifié. Proxy sortant facultatif
/// (<c>api-proxy</c>) : les réseaux autorisés passent sans lui ; derrière le proxy, le contrôle se fait sur la résolution
/// faite ici avant l'envoi.
/// </summary>
public sealed class ReseauSortant : IDisposable
{
    public const string CleReseaux = "ConnexionsApi:ReseauxAutorises";
    public const string CleProxy = "ConnexionsApi:Proxy";

    private readonly IConfiguration _config;
    private readonly IResolveurNoms _noms;
    private readonly RecepteurDemo _demo;
    private readonly HttpMessageInvoker _direct;
    private readonly object _verrou = new();
    private (string Adresse, HttpMessageInvoker Client)? _viaProxy;

    /// <summary>Réseaux jamais joints sans autorisation explicite.</summary>
    public static readonly IReadOnlyList<IPNetwork> Internes =
    [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"), IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("224.0.0.0/4"), IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"), IPNetwork.Parse("::1/128"), IPNetwork.Parse("64:ff9b::/96"), IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("fc00::/7"), IPNetwork.Parse("fe80::/10"), IPNetwork.Parse("ff00::/8"),
    ];

    public ReseauSortant(IConfiguration config, IResolveurNoms noms, RecepteurDemo demo)
    {
        _config = config;
        _noms = noms;
        _demo = demo;
        var handler = Handler();
        handler.ConnectCallback = Connecter;
        _direct = new HttpMessageInvoker(handler);
    }

    private static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        UseProxy = false,
    };

    private static IPAddress Normaliser(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public static bool EstInterne(IPAddress ip)
    {
        ip = Normaliser(ip);
        return Internes.Any(n => n.BaseAddress.AddressFamily == ip.AddressFamily && n.Contains(ip));
    }

    /// <summary>Réseaux du réglage <c>api-reseaux-autorises</c> (relu à chaque appel : modifiable sans redémarrage).</summary>
    public IReadOnlyList<IPNetwork> ReseauxAutorises() => Lire(_config[CleReseaux]);

    public static List<IPNetwork> Lire(string? valeur)
    {
        var liste = new List<IPNetwork>();
        foreach (var r in (valeur ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            if (IPNetwork.TryParse(r, out var n)) liste.Add(n);
            else if (IPAddress.TryParse(r, out var ip)) liste.Add(new IPNetwork(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
        return liste;
    }

    private static bool DansAutorises(IPAddress ip, IReadOnlyList<IPNetwork> autorises)
    {
        ip = Normaliser(ip);
        return autorises.Any(n => n.BaseAddress.AddressFamily == ip.AddressFamily && n.Contains(ip));
    }

    public static string MessageRefus(string hote, IPAddress ip) =>
        $"{hote} correspond à {Normaliser(ip)}, une adresse interne qui n'est pas dans les réseaux autorisés. "
        + "Ajoutez son réseau dans Paramètres › API externes › Réseaux internes autorisés.";

    public const string MessageHttp = "HTTP n'est accepté que vers un réseau interne autorisé : utilisez une adresse en https://.";

    private async Task<IPAddress[]> Resoudre(string hote, CancellationToken ct) =>
        IPAddress.TryParse(hote.Trim('[', ']'), out var ip) ? [ip] : await _noms.Resoudre(hote, ct);

    /// <summary>
    /// Choisit l'adresse à joindre parmi celles du nom : la première permise (publique, ou interne mais autorisée ; en http,
    /// seulement une adresse autorisée). Aucune : refus, avec l'adresse en cause.
    /// </summary>
    private IPAddress Choisir(string hote, IPAddress[] adresses, bool https)
    {
        if (adresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
        var autorises = ReseauxAutorises();
        IPAddress? refusee = null;
        var http = false;
        foreach (var a in adresses)
        {
            var autorisee = DansAutorises(a, autorises);
            if (EstInterne(a) && !autorisee)
            {
                refusee ??= a;
                continue;
            }
            if (!https && !autorisee)
            {
                http = true;
                continue;
            }
            return a;
        }
        throw new AdresseRefuseeException(refusee is not null ? MessageRefus(hote, refusee) : http ? MessageHttp : MessageRefus(hote, adresses[0]));
    }

    private async ValueTask<Stream> Connecter(SocketsHttpConnectionContext contexte, CancellationToken ct)
    {
        var hote = contexte.DnsEndPoint.Host;
        var https = contexte.InitialRequestMessage.RequestUri?.Scheme == Uri.UriSchemeHttps;
        var adresse = Choisir(hote, await Resoudre(hote, ct), https);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(adresse, contexte.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Envoie la requête (en-têtes de la réponse lus, corps à lire par l'appelant). Lève <see cref="AdresseRefuseeException"/>
    /// si la destination n'est pas permise.
    /// </summary>
    public async Task<HttpResponseMessage> Envoyer(HttpRequestMessage requete, CancellationToken ct)
    {
        var uri = requete.RequestUri ?? throw new AdresseRefuseeException("Adresse absente.");
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            throw new AdresseRefuseeException("Adresse complète attendue, commençant par https://.");
        if (_demo.Intercepte(uri)) return await _demo.Recevoir(requete, ct);
        var proxy = (_config[CleProxy] ?? "").Trim();
        var client = _direct;
        if (proxy.Length > 0)
        {
            var adresses = await Resoudre(uri.IdnHost, ct);
            var autorises = ReseauxAutorises();
            if (adresses.Length == 0 || !adresses.All(a => DansAutorises(a, autorises)))
            {
                // Hors réseaux autorisés : par le proxy, après le même contrôle sur la résolution faite ici.
                Choisir(uri.IdnHost, adresses, https: true);
                if (uri.Scheme != Uri.UriSchemeHttps) throw new AdresseRefuseeException(MessageHttp);
                client = ViaProxy(proxy);
            }
        }
        return await client.SendAsync(requete, ct);
    }

    private HttpMessageInvoker ViaProxy(string adresse)
    {
        lock (_verrou)
        {
            if (_viaProxy is { } p && p.Adresse == adresse) return p.Client;
            var handler = Handler();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(adresse);
            // Proxy changé : l'ancien client peut encore servir à un envoi en cours, il est laissé au ramasse-miettes.
            _viaProxy = (adresse, new HttpMessageInvoker(handler));
            return _viaProxy.Value.Client;
        }
    }

    public void Dispose()
    {
        _direct.Dispose();
        _viaProxy?.Client.Dispose();
    }
}
