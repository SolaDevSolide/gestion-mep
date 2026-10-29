using System.Net;
using System.Text;

namespace GestionMep.Api.Services;

/// <summary>Message reçu par le récepteur de démonstration (en-têtes secrets masqués).</summary>
public record MessageRecuDto(DateTimeOffset Quand, string Canal, string Methode, string Adresse, List<EnteteAppelDto> Entetes, string Corps);

/// <summary>
/// Récepteur intégré du mode démonstration : les connexions d'exemple visent <c>https://recepteur.demo.invalid/…</c>, que
/// le serveur traite lui-même au lieu de l'envoyer sur le réseau (actif seulement si Demo:Activer). Il garde les
/// <see cref="Capacite"/> derniers messages, lisibles dans la console : on essaie les connexions sans réseau ni outil externe.
/// </summary>
public sealed class RecepteurDemo(IConfiguration config, TimeProvider temps)
{
    public const string Hote = "recepteur.demo.invalid";
    public const int Capacite = 50;
    private readonly LinkedList<MessageRecuDto> _messages = new();
    private readonly object _verrou = new();

    public bool Intercepte(Uri uri) => uri.Host.Equals(Hote, StringComparison.OrdinalIgnoreCase) && config.GetValue("Demo:Activer", false);

    public async Task<HttpResponseMessage> Recevoir(HttpRequestMessage requete, CancellationToken ct)
    {
        var corps = requete.Content is null ? "" : await requete.Content.ReadAsStringAsync(ct);
        var entetes = requete.Headers.Concat(requete.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Select(h => new EnteteAppelDto(h.Key, EstSecret(h.Key) ? Domain.RequeteConnexion.Masque : string.Join(", ", h.Value))).ToList();
        var canal = requete.RequestUri!.AbsolutePath.Trim('/').Split('/')[0];
        lock (_verrou)
        {
            _messages.AddFirst(new MessageRecuDto(temps.GetUtcNow(), canal, requete.Method.Method, requete.RequestUri.GetLeftPart(UriPartial.Path), entetes,
                corps.Length > 20_000 ? corps[..20_000] : corps));
            while (_messages.Count > Capacite) _messages.RemoveLast();
        }
        // Comme Teams Workflows (202, corps vide) ; les autres canaux répondent 200 avec un accusé JSON.
        return canal == "teams"
            ? new HttpResponseMessage(HttpStatusCode.Accepted) { RequestMessage = requete, Content = new StringContent("") }
            : new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = requete, Content = new StringContent("{\"recu\":true}", Encoding.UTF8, "application/json") };
    }

    private static bool EstSecret(string nom) =>
        nom.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || nom.Contains("key", StringComparison.OrdinalIgnoreCase)
        || nom.Contains("token", StringComparison.OrdinalIgnoreCase) || nom.Contains("secret", StringComparison.OrdinalIgnoreCase);

    public List<MessageRecuDto> Messages()
    {
        lock (_verrou) return [.. _messages];
    }

    public void Vider()
    {
        lock (_verrou) _messages.Clear();
    }
}
