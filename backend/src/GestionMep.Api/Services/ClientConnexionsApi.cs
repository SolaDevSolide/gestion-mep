using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Résultat d'une tentative : réussie (2xx), à refaire (erreur passagère, avec l'attente demandée par l'outil), tronquée.</summary>
public sealed record ResultatTentative(TentativeAppel Tentative, bool Reussi, bool Reessayable, TimeSpan? Attente, bool Tronque);

/// <summary>
/// Envoi d'une tentative d'appel : relit et déchiffre les secrets de la connexion (jamais copiés dans l'appel), ajoute
/// l'authentification, la signature et les en-têtes de l'application, envoie par <see cref="ReseauSortant"/> et lit au plus
/// <c>limite</c> octets de la réponse. Chaque erreur est traduite en un message qui dit quoi faire.
/// </summary>
public sealed class ClientConnexionsApi(ReseauSortant reseau, SecretsConnexion secrets, TimeProvider temps)
{
    /// <summary>Réponse lue au plus : 4 Ko pour un essai (affichée), 2 Ko pour l'historique.</summary>
    public const int LimiteEssai = 4096;
    public const int LimiteHistorique = 2048;
    /// <summary>Attente maximale demandée par un Retry-After qui soit respectée.</summary>
    public static readonly TimeSpan AttenteMax = TimeSpan.FromMinutes(10);
    private static readonly string[] EntetesGardes = ["Content-Type", "Retry-After", "Location"];

    /// <summary>Jetons OAuth2 gardés en mémoire jusqu'à 60 s avant leur expiration (jamais en base).</summary>
    private readonly ConcurrentDictionary<string, (string Jeton, DateTimeOffset Expire)> _jetons = new();

    private sealed class Refus(string type, string message) : Exception(message)
    {
        public string Type { get; } = type;
    }

    private const string MessageIllisible =
        "Un secret de la connexion est illisible (clés de l'application changées, par exemple après une restauration) : saisissez-le de nouveau.";

    private string Secret(SecretConnexion? s) =>
        s is null
            ? throw new Refus("secret", "Un secret de la connexion n'a pas été saisi : saisissez-le dans la console, ou par « gestion-mep connexions secret ».")
            : secrets.Dechiffrer(s) ?? throw new Refus("secret", MessageIllisible);

    public async Task<ResultatTentative> Envoyer(ConnexionApi c, AppelApi a, int limite, CancellationToken ct)
    {
        var t = new TentativeAppel { Debut = temps.GetUtcNow() };
        var chrono = Stopwatch.StartNew();
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(a.DelaiSecondes, 1, 30)));
        try
        {
            var url = a.UrlSecrete ? Secret(c.UrlSecrete) : a.Url;
            // Les secrets ne partent que vers l'origine à laquelle ils sont liés : un appel créé avant un changement d'adresse
            // de la connexion échoue plutôt que d'envoyer les nouveaux secrets à l'ancienne adresse (ou l'inverse).
            if (a.UrlSecrete && RequeteConnexion.Origine(url) != RequeteConnexion.Origine(a.Url)
                || AUnSecret(c) && c.OrigineSecrets.Split(' ')[0] != RequeteConnexion.Origine(url))
                throw new Refus("adresse", "L'adresse de la connexion a changé depuis cet appel : renvoyez le message pour qu'il parte vers la nouvelle adresse.");
            var (reponse, tronque, attente) = await Tenter(c, a, url, limite, false, delai.Token);
            if (reponse.Code == 401 && c.Authentification == AuthentificationApi.OAuth2)
                (reponse, tronque, attente) = await Tenter(c, a, url, limite, true, delai.Token);
            reponse.Debut = t.Debut;
            reponse.DureeMs = (int)chrono.ElapsedMilliseconds;
            var code = reponse.Code ?? 0;
            var (erreur, reessayable) = Classer(c, a, code, reponse);
            reponse.Erreur = erreur;
            return new ResultatTentative(reponse, code is >= 200 and < 300, reessayable, attente, tronque);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            t.DureeMs = (int)chrono.ElapsedMilliseconds;
            var hote = Uri.TryCreate(a.Url, UriKind.Absolute, out var u) ? u.Host : a.Url;
            (t.TypeErreur, t.Erreur) = ex switch
            {
                OperationCanceledException => ("delai", $"Pas de réponse en {a.DelaiSecondes} s : l'outil n'a pas répondu dans le délai d'attente."),
                Refus r => (r.Type, r.Message),
                AdresseRefuseeException r => ("adresse", r.Message),
                _ when Interne<AdresseRefuseeException>(ex) is { } r => ("adresse", r.Message),
                _ when Interne<AuthenticationException>(ex) is not null => ("certificat",
                    $"Le certificat de {hote} est refusé (autorité inconnue du serveur, nom ou date invalide). Ajoutez l'autorité interne au magasin de certificats du serveur (voir Exploiter)."),
                _ when Interne<SocketException>(ex) is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain } =>
                    ("reseau", $"Le nom {hote} est introuvable (DNS) : vérifiez l'adresse."),
                _ when Interne<SocketException>(ex) is { SocketErrorCode: SocketError.ConnectionRefused } =>
                    ("reseau", $"{hote} refuse la connexion : le service est arrêté, ou le port est fermé."),
                _ when Interne<SocketException>(ex) is not null || ex is HttpRequestException or IOException =>
                    ("reseau", $"Impossible de joindre {hote} : {Racine(ex).Message}"),
                _ => ("requete", $"La requête n'a pas pu partir : {Racine(ex).Message}"),
            };
            var reessayable = t.TypeErreur is "delai" or "reseau";
            return new ResultatTentative(t, false, reessayable, null, false);
        }
    }

    private static bool AUnSecret(ConnexionApi c) =>
        c.UrlSecrete is not null || c.Secret is not null || c.SecretSignature is not null || c.Entetes.Any(e => e.Secret);

    private static T? Interne<T>(Exception ex) where T : Exception
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is T t) return t;
        return null;
    }

    private static Exception Racine(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }

    private async Task<(TentativeAppel Tentative, bool Tronque, TimeSpan? Attente)> Tenter(ConnexionApi c, AppelApi a, string url, int limite,
        bool nouveauJeton, CancellationToken ct)
    {
        using var requete = new HttpRequestMessage(new HttpMethod(a.Methode.ToString().ToUpperInvariant()), url);
        var corps = Encoding.UTF8.GetBytes(a.Corps);
        if (a.TypeContenu.Length > 0)
        {
            requete.Content = new ByteArrayContent(corps);
            requete.Content.Headers.TryAddWithoutValidation("Content-Type", a.TypeContenu);
        }
        requete.Headers.TryAddWithoutValidation("User-Agent", $"GestionMep/{VersionApplication.Courante}");
        requete.Headers.TryAddWithoutValidation(RequeteConnexion.EnteteAppel, (a.AppelOrigineId ?? a.Id).ToString());
        requete.Headers.TryAddWithoutValidation(RequeteConnexion.EnteteEvenement, a.TypeEvenement);
        foreach (var e in a.Entetes) requete.Headers.TryAddWithoutValidation(e.Nom, e.Valeur);
        foreach (var e in c.Entetes.Where(e => e.Secret)) requete.Headers.TryAddWithoutValidation(e.Nom, Secret(e.ValeurSecrete));
        switch (c.Authentification)
        {
            case AuthentificationApi.Jeton:
                requete.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Secret(c.Secret)}");
                break;
            case AuthentificationApi.Basic:
                requete.Headers.TryAddWithoutValidation("Authorization",
                    $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Utilisateur}:{Secret(c.Secret)}"))}");
                break;
            case AuthentificationApi.CleEntete:
                requete.Headers.TryAddWithoutValidation(c.NomEntete, Secret(c.Secret));
                break;
            case AuthentificationApi.OAuth2:
                requete.Headers.TryAddWithoutValidation("Authorization", $"Bearer {await Jeton(c, nouveauJeton, ct)}");
                break;
        }
        if (c.SecretSignature is not null)
            requete.Headers.TryAddWithoutValidation(RequeteConnexion.EnteteSignature,
                "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret(c.SecretSignature)), corps)));

        using var reponse = await reseau.Envoyer(requete, ct);
        var t = new TentativeAppel
        {
            Code = (int)reponse.StatusCode,
            Libelle = reponse.ReasonPhrase is { Length: > 0 } r ? r : reponse.StatusCode.ToString(),
            EntetesReponse = string.Join("\n", EntetesGardes.SelectMany(nom =>
                (reponse.Headers.TryGetValues(nom, out var v) || reponse.Content.Headers.TryGetValues(nom, out v) ? v : []).Select(x => $"{nom}: {x}"))),
        };
        var (extrait, tronque) = await Lire(reponse, limite, ct);
        t.ExtraitReponse = extrait;
        TimeSpan? attente = null;
        if (reponse.Headers.RetryAfter is { } ra)
        {
            var d = ra.Delta ?? (ra.Date is { } date ? date - temps.GetUtcNow() : null);
            if (d is { } x && x > TimeSpan.Zero) attente = x < AttenteMax ? x : AttenteMax;
        }
        return (t, tronque, attente);
    }

    /// <summary>Lit au plus <paramref name="limite"/> octets du corps (le reste est abandonné avec la connexion).</summary>
    private static async Task<(string Texte, bool Tronque)> Lire(HttpResponseMessage reponse, int limite, CancellationToken ct)
    {
        await using var flux = await reponse.Content.ReadAsStreamAsync(ct);
        var tampon = new byte[limite + 1];
        var lu = 0;
        while (lu < tampon.Length)
        {
            var n = await flux.ReadAsync(tampon.AsMemory(lu), ct);
            if (n == 0) break;
            lu += n;
        }
        var tronque = lu > limite;
        var texte = Encoding.UTF8.GetString(tampon, 0, Math.Min(lu, limite));
        return (tronque ? texte.TrimEnd('�') : texte, tronque);
    }

    private async Task<string> Jeton(ConnexionApi c, bool nouveau, CancellationToken ct)
    {
        var secret = Secret(c.Secret);
        var cle = $"{c.Id}|{c.TokenUrl}|{c.ClientId}|{c.Scope}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
        if (!nouveau && _jetons.TryGetValue(cle, out var j) && j.Expire > temps.GetUtcNow()) return j.Jeton;
        var champs = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"), new("client_id", c.ClientId), new("client_secret", secret),
        };
        if (c.Scope.Length > 0) champs.Add(new("scope", c.Scope));
        using var requete = new HttpRequestMessage(HttpMethod.Post, c.TokenUrl) { Content = new FormUrlEncodedContent(champs) };
        requete.Headers.TryAddWithoutValidation("User-Agent", $"GestionMep/{VersionApplication.Courante}");
        using var reponse = await reseau.Envoyer(requete, ct);
        var (texte, _) = await Lire(reponse, 64 * 1024, ct);
        if (!reponse.IsSuccessStatusCode)
            throw new Refus("authentification",
                $"Le point de jeton OAuth2 a répondu {(int)reponse.StatusCode} : vérifiez l'ID client, le secret client et la portée.");
        try
        {
            using var doc = JsonDocument.Parse(texte);
            var jeton = doc.RootElement.GetProperty("access_token").GetString() ?? throw new KeyNotFoundException();
            var duree = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600;
            _jetons[cle] = (jeton, temps.GetUtcNow().AddSeconds(Math.Max(0, duree - 60)));
            return jeton;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new Refus("authentification", "Le point de jeton OAuth2 n'a pas renvoyé de jeton (access_token) : vérifiez son adresse.");
        }
    }

    /// <summary>Message d'une réponse (vide si elle est réussie) et possibilité de réessayer.</summary>
    private static (string Message, bool Reessayable) Classer(ConnexionApi c, AppelApi a, int code, TentativeAppel t)
    {
        if (code is >= 200 and < 300) return ("", false);
        if (code is >= 300 and < 400)
        {
            t.TypeErreur = "redirection";
            var location = t.EntetesReponse.Split('\n').FirstOrDefault(l => l.StartsWith("Location: ", StringComparison.Ordinal))?["Location: ".Length..];
            return (location is null
                ? $"Redirection non suivie ({code}) : vérifiez l'adresse de la connexion."
                : $"Redirection non suivie ({code}) : l'outil renvoie vers {location}. Si c'est la bonne adresse, recopiez-la dans la connexion.", false);
        }
        if (code is 408 or 425 or 429 or >= 500)
            return (code == 429
                ? "L'outil limite le nombre d'appels (429) : nouvelle tentative plus tard."
                : $"L'outil est indisponible ({code} {t.Libelle}) : erreur passagère, l'envoi automatique est réessayé.", true);
        return (code switch
        {
            401 => c.Authentification switch
            {
                AuthentificationApi.Aucune => "L'outil demande une identification (401) : choisissez une authentification, ou vérifiez l'adresse (clé incluse).",
                AuthentificationApi.Jeton => "L'outil refuse l'identification (401) : vérifiez le jeton.",
                AuthentificationApi.Basic => "L'outil refuse l'identification (401) : vérifiez l'identifiant et le mot de passe du compte de service.",
                AuthentificationApi.CleEntete => "L'outil refuse l'identification (401) : vérifiez la clé et le nom de son en-tête.",
                _ => "L'outil refuse le jeton OAuth2 (401) : vérifiez la portée et les droits accordés à l'application.",
            },
            403 => "L'outil refuse l'accès (403) : le compte ou la clé n'a pas les droits nécessaires.",
            404 => "Adresse introuvable chez l'outil (404) : vérifiez l'adresse de la connexion.",
            405 => $"L'outil n'accepte pas la méthode {a.Methode.ToString().ToUpperInvariant()} à cette adresse (405) : vérifiez la méthode.",
            413 => "Message trop volumineux pour l'outil (413) : allégez le message.",
            _ => $"L'outil refuse le message ({code} {t.Libelle}) : vérifiez le message et les champs qu'il attend (voir sa réponse).",
        }, false);
    }
}
