using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>Échange avec Noosphere en échec (réseau, refus, réponse non signée ou illisible) : 502 pour la console.</summary>
public sealed class ErreurLiaison(string message, int? codeNoosphere = null, string? raison = null) : ErreurHttp(502, message)
{
    public int? CodeNoosphere { get; } = codeNoosphere;
    /// <summary>Raison du contrat renvoyée par Noosphere (fin du <c>type</c> du problème), par exemple <c>link-not-confirmed</c>.</summary>
    public string? Raison { get; } = raison;
}

/// <summary>
/// Sortie HTTP de la liaison : sans redirection ni cookies, délai de 15 s. Remplaçable en test par un pair Noosphere en
/// mémoire. La liaison ne passe pas par la sortie protégée des API externes (<see cref="ReseauSortant"/>) : son adresse est
/// saisie par un administrateur (comme l'autorité de la connexion unique) et vise souvent le réseau interne (Docker).
/// </summary>
public class TransportLiaison(HttpMessageHandler? gestionnaire = null) : IDisposable
{
    public static readonly TimeSpan Delai = TimeSpan.FromSeconds(15);
    private readonly HttpMessageInvoker _client = new(gestionnaire ?? new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    }, disposeHandler: true);

    public async Task<HttpResponseMessage> Envoyer(HttpRequestMessage requete, CancellationToken ct)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(Delai);
        return await _client.SendAsync(requete, delai.Token);
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Réveil et état partagé des tâches de la liaison : synchronisations demandées, file des déploiements, verrous.</summary>
public sealed class SignalLiaisons : IDisposable
{
    private readonly SemaphoreSlim _reveil = new(0, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _synchros = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> _verrous = new();
    public FileDeploiements Deploiements { get; } = new();
    private int _rapprochement;

    /// <summary>Suivi ou catalogue changé : toutes les MEP des systèmes suivis sont à revoir (rapprochement complet).</summary>
    public void DemanderRapprochement()
    {
        Interlocked.Exchange(ref _rapprochement, 1);
        Reveiller();
    }

    public bool PrendreRapprochement() => Interlocked.Exchange(ref _rapprochement, 0) == 1;

    /// <summary>Relire le catalogue de la liaison au plus tôt à <paramref name="quand"/> (avis de changement, confirmation).</summary>
    public void DemanderSynchro(Guid liaison, DateTimeOffset quand)
    {
        _synchros.AddOrUpdate(liaison, quand, (_, d) => d < quand ? d : quand);
        Reveiller();
    }

    public List<Guid> SynchrosDues(DateTimeOffset maintenant)
    {
        var dues = _synchros.Where(p => p.Value <= maintenant).Select(p => p.Key).ToList();
        foreach (var d in dues) _synchros.TryRemove(d, out _);
        return dues;
    }

    public void MepModifiee(Guid mep, DateTimeOffset maintenant)
    {
        Deploiements.Signaler(mep, maintenant);
        Reveiller();
    }

    public void Reveiller()
    {
        try { _reveil.Release(); }
        catch (SemaphoreFullException) { /* déjà réveillé */ }
    }

    public async Task Attendre(TimeSpan delai, CancellationToken ct)
    {
        try { await _reveil.WaitAsync(delai, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Une seule opération réseau à la fois par liaison (synchronisation, envoi, renouvellement, coupure).</summary>
    public SemaphoreSlim Verrou(Guid liaison) => _verrous.GetOrAdd(liaison, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        _reveil.Dispose();
        foreach (var v in _verrous.Values) v.Dispose();
    }
}

/// <summary>Réponse de Noosphere, signature vérifiée et corps déchiffré.</summary>
public sealed record ReponseLiaison(int Code, byte[] Clair, string? Etag);

/// <summary>
/// Liaison avec Noosphere (#123), côté gestion-mep : appairage par code, confirmation de l'empreinte, état du pair,
/// synchronisation du catalogue (ETag), envoi des événements de déploiement, renouvellement des clés et coupure. Chaque appel
/// est signé (RFC 9421) et chaque réponse vérifiée ; les corps de données sont des JWE. Tout échange est inscrit au journal
/// des échanges (<see cref="EchangeLiaison"/>). En démonstration, une liaison vers <c>*.demo.invalid</c> n'appelle rien :
/// l'échange est noté « demo » et le catalogue fictif de la démonstration est utilisé.
/// </summary>
public sealed class ServiceLiaisons(MepDbContext db, IConfiguration config, TimeProvider temps, IDataProtectionProvider protection,
    TransportLiaison transport, SignalLiaisons signal)
{
    public const string UsageCles = "GestionMep.Liaisons.Cle";
    public const string Chemin = "/link/v1";
    public const string Produit = "gestion-mep";
    public const string HoteDemo = "noosphere.demo.invalid";
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan EcartHorloge = TimeSpan.FromSeconds(300);
    public const string CleNomInstance = "Liaison:NomInstance";
    public const string NomInstanceParDefaut = "gestion-mep";
    /// <summary>MEP terminées depuis plus longtemps jamais envoyées : pas reprises par le rapprochement.</summary>
    public static readonly TimeSpan HistoriqueEnvoye = TimeSpan.FromDays(90);
    public const int LotMax = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DateTimeOffset Maintenant => temps.GetUtcNow();
    public SignalLiaisons Signal => signal;

    private IDataProtector Protecteur => protection.CreateProtector(UsageCles);

    public string NomInstance => config[CleNomInstance] is { Length: > 0 } n ? n.Trim() : NomInstanceParDefaut;

    public bool EstDemo(Liaison l) => config.GetValue("Demo:Activer", false)
        && Uri.TryCreate(l.Url, UriKind.Absolute, out var u) && u.Host.Equals(HoteDemo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Adresse publique de gestion-mep (réglage url-publique), sinon celle de la requête, sinon vide.</summary>
    public string UrlLocale(HttpRequest? requete = null) =>
        config["Application:UrlPublique"] is { Length: > 0 } u ? u.TrimEnd('/')
        : requete is null ? "" : $"{requete.Scheme}://{requete.Host}{requete.PathBase}".TrimEnd('/');

    // ---------- Clés ----------

    public ClesLocales NouvellesCles()
    {
        using var s = CryptoLiaison.NouvelleCleSignature();
        using var c = CryptoLiaison.NouvelleCleChiffrement();
        return new ClesLocales
        {
            SignaturePrivee = Protecteur.Protect(Convert.ToBase64String(s.ExportPkcs8PrivateKey())), Signature = CryptoLiaison.Jwk(s),
            ChiffrementPrivee = Protecteur.Protect(Convert.ToBase64String(c.ExportPkcs8PrivateKey())), Chiffrement = CryptoLiaison.Jwk(c),
        };
    }

    public ECDsa CleSignature(ClesLocales c)
    {
        try { return CryptoLiaison.ChargerSignature(Convert.FromBase64String(Protecteur.Unprotect(c.SignaturePrivee))); }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            throw new ErreurLiaison("Clé de signature de la liaison illisible (clés de l'application changées, par exemple après une restauration) : "
                + "coupez la liaison puis appairez de nouveau.");
        }
    }

    public ECDiffieHellman CleChiffrement(ClesLocales c)
    {
        try { return CryptoLiaison.ChargerChiffrement(Convert.FromBase64String(Protecteur.Unprotect(c.ChiffrementPrivee))); }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            throw new ErreurLiaison("Clé de chiffrement de la liaison illisible : coupez la liaison puis appairez de nouveau.");
        }
    }

    public static string CalculerEmpreinte(ClesLocales locales, ClesPair pair) =>
        CryptoLiaison.EmpreinteMots([locales.Signature, locales.Chiffrement, pair.Signature, pair.Chiffrement]);

    /// <summary>Déchiffre un JWE reçu avec la clé de chiffrement qu'il désigne (actuelle, ou ancienne pendant 5 minutes).</summary>
    public byte[] Dechiffrer(Liaison l, string jwe)
    {
        var kid = CryptoLiaison.KidDestinataire(jwe);
        var cles = l.Cles is { } c && CryptoLiaison.Kid(c.Chiffrement) == kid ? c
            : l.AnciennesCles is { } a && CryptoLiaison.Kid(a.Chiffrement) == kid && a.ValablesJusquA > Maintenant ? a
            : l.Cles ?? throw new CryptographicException("Liaison sans clé.");
        using var cle = CleChiffrement(cles);
        return CryptoLiaison.Dechiffrer(jwe, cle);
    }

    public string Chiffrer(Liaison l, object contenu) =>
        CryptoLiaison.Chiffrer(JsonSerializer.SerializeToUtf8Bytes(contenu, Json), (l.ClesPair ?? throw new ErreurLiaison("Liaison sans clé.")).Chiffrement);

    // ---------- Journal des échanges ----------

    public void Noter(Guid? liaison, string sens, string operation, string resultat, int? code, string detail)
    {
        db.EchangesLiaison.Add(new EchangeLiaison
        {
            LiaisonId = liaison, Quand = Maintenant, Sens = sens, Operation = operation, Resultat = resultat, Code = code,
            Detail = detail.Length > 2000 ? detail[..2000] : detail,
        });
    }

    public static LiaisonDto Dto(Liaison l, int suivis, bool demo) => new(l.Id, l.Produit, l.NomInstance, l.Url, l.VersionPair, l.Etat, l.ConfirmeeIci,
        l.ConfirmeeLe, l.ConfirmeePar, l.ConfirmeeParPair, [.. l.Capacites], l.Empreinte, l.AppaireeLe, l.AppaireePar, l.ClesRenouveleesLe,
        l.DernierEchangeLe, l.CoupeeLe, l.CoupeePar, l.EnvoyerMeps, l.SuiviAutomatique, [.. l.DomainesSuivis], l.DerniereSynchro, l.DerniereErreur,
        suivis, demo);

    public async Task<LiaisonDto> Dto(Liaison l)
    {
        var suivis = (await db.SystemesCatalogue.AsNoTracking().Where(s => s.Source != null).Select(s => s.Source).ToListAsync())
            .Count(s => s!.LiaisonId == l.Id);
        return Dto(l, suivis, EstDemo(l));
    }

    public async Task<List<LiaisonDto>> Lister()
    {
        var liaisons = await db.Liaisons.AsNoTracking().OrderBy(l => l.Etat == EtatLiaison.Coupee).ThenByDescending(l => l.AppaireeLe).ToListAsync();
        var sources = (await db.SystemesCatalogue.AsNoTracking().Where(s => s.Source != null).Select(s => s.Source).ToListAsync())
            .GroupBy(s => s!.LiaisonId).ToDictionary(g => g.Key, g => g.Count());
        return liaisons.Select(l => Dto(l, sources.GetValueOrDefault(l.Id), EstDemo(l))).ToList();
    }

    public async Task<Liaison> Charger(Guid id) =>
        await db.Liaisons.FirstOrDefaultAsync(l => l.Id == id) ?? throw ErreurHttp.NonTrouve("Liaison introuvable.");

    /// <summary>Liaison désignée par son identifiant ou ses premiers caractères (ligne de commande), ou la seule liaison en service.</summary>
    public async Task<Liaison> Trouver(string? texte)
    {
        var toutes = await db.Liaisons.ToListAsync();
        if (string.IsNullOrWhiteSpace(texte))
        {
            var actives = toutes.Where(l => l.Etat != EtatLiaison.Coupee).ToList();
            return actives.Count == 1 ? actives[0] : throw ErreurHttp.Saisie(actives.Count == 0
                ? "Aucune liaison en service (voir « gestion-mep liaison liste »)." : "Plusieurs liaisons : donner l'identifiant (voir « gestion-mep liaison liste »).");
        }
        var t = texte.Trim().ToLowerInvariant();
        var trouvees = toutes.Where(l => l.Id.ToString().StartsWith(t, StringComparison.Ordinal)).ToList();
        if (t.Length < 4 || trouvees.Count == 0) throw ErreurHttp.NonTrouve($"Liaison introuvable : « {texte} » (voir « gestion-mep liaison liste »).");
        return trouvees.Count == 1 ? trouvees[0] : throw ErreurHttp.Saisie($"Plusieurs liaisons commencent par « {texte} » : donner l'identifiant complet.");
    }

    // ---------- Appels signés vers Noosphere ----------

    private static string Probleme(byte[] corps)
    {
        try
        {
            var n = JsonNode.Parse(corps);
            var type = n?["type"]?.GetValue<string>() ?? "";
            return type.StartsWith("https://noosphere.dev/problems/", StringComparison.Ordinal) ? type["https://noosphere.dev/problems/".Length..] : "";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return "";
        }
    }

    private static string DetailProbleme(byte[] corps)
    {
        try { return JsonNode.Parse(corps)?["detail"]?.GetValue<string>() ?? ""; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return ""; }
    }

    /// <summary>
    /// Envoie une requête signée à Noosphere ({Url}/link/v1{chemin}) avec la clé de signature actuelle (celle d'avant un
    /// renouvellement, qui n'a pas encore basculé) et vérifie la signature de la réponse. Corps (objet) : chiffré en JWE pour
    /// la clé de chiffrement de Noosphere. <paramref name="siDifferent"/> : ETag du dernier catalogue (If-None-Match). Lève
    /// <see cref="ErreurLiaison"/> en cas d'échec, après l'avoir noté au journal des échanges.
    /// </summary>
    public async Task<ReponseLiaison> Appeler(Liaison l, string operation, HttpMethod methode, string chemin, object? contenu, CancellationToken ct,
        string? siDifferent = null)
    {
        var cles = l.Cles ?? throw new ErreurLiaison("Liaison sans clé : coupée, ou à appairer de nouveau.");
        if (l.ClesPair is null) throw new ErreurLiaison("Clés de Noosphere absentes.");
        var corps = contenu is null ? null : Encoding.ASCII.GetBytes(Chiffrer(l, contenu));
        var uri = new Uri($"{l.Url.TrimEnd('/')}{Chemin}{chemin}");
        using var requete = new HttpRequestMessage(methode, uri);
        var composants = new List<(string, string)>
        {
            ("@method", methode.Method.ToUpperInvariant()), ("@path", uri.AbsolutePath), ("@query", uri.Query.Length == 0 ? "?" : uri.Query),
        };
        if (corps is not null)
        {
            var digest = CryptoLiaison.Digest(corps);
            requete.Content = new ByteArrayContent(corps);
            requete.Content.Headers.ContentType = new MediaTypeHeaderValue("application/jose");
            requete.Content.Headers.TryAddWithoutValidation("Content-Digest", digest);
            composants.Add(("content-digest", digest));
        }
        using (var signature = CleSignature(cles))
        {
            var parametres = CryptoLiaison.ParametresSignature(composants.Select(c => c.Item1), Maintenant.ToUnixTimeSeconds(), CryptoLiaison.Aleatoire(),
                CryptoLiaison.Kid(cles.Signature), CryptoLiaison.TagRequete);
            var valeur = CryptoLiaison.Signer(signature, CryptoLiaison.BaseSignature(composants, parametres));
            requete.Headers.TryAddWithoutValidation("Signature-Input", CryptoLiaison.EnteteSignatureInput(parametres));
            requete.Headers.TryAddWithoutValidation("Signature", CryptoLiaison.EnteteSignature(valeur));
        }
        requete.Headers.Accept.ParseAdd("application/jose");
        requete.Headers.Accept.ParseAdd("application/problem+json");
        requete.Headers.UserAgent.ParseAdd($"gestion-mep/{VersionApplication.Courante.Split('+')[0]}");
        if (siDifferent is { Length: > 0 }) requete.Headers.TryAddWithoutValidation("If-None-Match", siDifferent);

        HttpResponseMessage reponse;
        try
        {
            reponse = await transport.Envoyer(requete, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            var message = e is HttpRequestException h ? $"Noosphere injoignable ({h.Message.TrimEnd('.')})." : "Noosphere ne répond pas (15 s).";
            Noter(l.Id, "sortant", operation, "echec", null, message);
            throw new ErreurLiaison(message);
        }
        using (reponse)
        {
            var octets = await reponse.Content.ReadAsByteArrayAsync(ct);
            var code = (int)reponse.StatusCode;
            if (code is < 200 or >= 300 and not 304)
            {
                var raison = Probleme(octets);
                var detail = DetailProbleme(octets);
                var message = $"Noosphere a refusé ({code}{(raison.Length > 0 ? $" {raison}" : "")}){(detail.Length > 0 ? $" : {detail}" : ".")}";
                Noter(l.Id, "sortant", operation, "echec", code, message);
                throw new ErreurLiaison(message, code, raison.Length > 0 ? raison : null);
            }
            if (VerifierReponse(l, reponse, octets) is { } refus)
            {
                var message = $"Réponse de Noosphere refusée : {refus}.";
                Noter(l.Id, "sortant", operation, "echec", code, message);
                throw new ErreurLiaison(message, code);
            }
            byte[] clair = [];
            if (octets.Length > 0 && reponse.Content.Headers.ContentType?.MediaType == "application/jose")
            {
                try { clair = Dechiffrer(l, Encoding.ASCII.GetString(octets)); }
                catch (CryptographicException)
                {
                    const string message = "Réponse de Noosphere indéchiffrable (JWE).";
                    Noter(l.Id, "sortant", operation, "echec", code, message);
                    throw new ErreurLiaison(message, code);
                }
            }
            else if (octets.Length > 0) clair = octets;
            l.DernierEchangeLe = Maintenant;
            return new ReponseLiaison(code, clair, reponse.Headers.ETag?.ToString()
                ?? (reponse.Headers.TryGetValues("ETag", out var e) ? e.FirstOrDefault() : null));
        }
    }

    /// <summary>
    /// Vérifie la signature d'une réponse : composants « @status » et, s'il y a un corps, « content-digest » (égal au corps),
    /// étiquette <c>noosphere-link-response</c>, clé de signature de Noosphere (ou l'ancienne pendant 5 minutes). Renvoie la
    /// raison du refus, ou null.
    /// </summary>
    public string? VerifierReponse(Liaison l, HttpResponseMessage reponse, byte[] corps)
    {
        string? Entete(string nom) => reponse.Headers.TryGetValues(nom, out var v) ? string.Join(", ", v)
            : reponse.Content.Headers.TryGetValues(nom, out var c) ? string.Join(", ", c) : null;
        var sig = CryptoLiaison.LireSignature(Entete("Signature-Input"), Entete("Signature"), CryptoLiaison.LibelleSignature)
            ?? CryptoLiaison.LireSignature(Entete("Signature-Input"), Entete("Signature"));
        if (sig is null) return "réponse non signée";
        if (sig.Tag != CryptoLiaison.TagReponse || (sig.Alg is not null && sig.Alg != CryptoLiaison.AlgSignature)) return "signature d'un autre usage";
        if (!sig.Composants.Contains("@status")) return "statut non couvert par la signature";
        var digest = Entete("Content-Digest");
        if (corps.Length > 0 && (!sig.Composants.Contains("content-digest") || !CryptoLiaison.DigestValide(digest, corps))) return "Content-Digest absent ou faux";
        var cle = l.ClesPair is { } p && CryptoLiaison.Kid(p.Signature) == sig.Kid ? p.Signature
            : l.AnciennesClesPair is { } a && CryptoLiaison.Kid(a.Signature) == sig.Kid && a.ValablesJusquA > Maintenant ? a.Signature : null;
        if (cle is null) return "clé de signature inconnue";
        var valeurs = new List<(string, string)>();
        foreach (var c in sig.Composants)
        {
            if (c == "@status") valeurs.Add((c, ((int)reponse.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            else if (!c.StartsWith('@') && Entete(c) is { } v) valeurs.Add((c, v.Trim()));
            else return $"composant « {c} » absent";
        }
        using var verif = CryptoLiaison.CleVerification(cle);
        return CryptoLiaison.VerifierSignature(verif, CryptoLiaison.BaseSignature(valeurs, sig.Parametres), sig.Valeur) ? null : "signature invalide";
    }

    // ---------- Appairage et confirmation ----------

    /// <summary>
    /// Appaire gestion-mep avec Noosphere : nouvelles clés, <c>POST /link/v1/pair</c> non signé avec MAC du code, contrôle du
    /// MAC de la réponse. La liaison est créée en attente de confirmation de l'empreinte (des deux côtés).
    /// </summary>
    public async Task<Liaison> Appairer(string? url, string? code, Acteur? acteur, string auteur, string urlLocale, CancellationToken ct)
    {
        var adresse = (url ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(adresse, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
            throw ErreurHttp.Saisie("Adresse de Noosphere attendue, complète : https://noosphere.entreprise.fr.");
        var normalise = CryptoLiaison.NormaliserCode(code ?? "");
        if (normalise.Length < 8) throw ErreurHttp.Saisie("Code d'appairage attendu : le code à usage unique affiché par Noosphere.");
        if (await db.Liaisons.AnyAsync(l => l.Url == adresse && l.Etat != EtatLiaison.Coupee, ct))
            throw ErreurHttp.Conflit("Une liaison avec cette instance de Noosphere existe déjà : coupez-la avant d'appairer de nouveau.");

        var cles = NouvellesCles();
        var nonce = CryptoLiaison.Aleatoire();
        var pair = new JsonObject
        {
            ["product"] = Produit, ["instanceName"] = NomInstance, ["url"] = urlLocale, ["version"] = VersionApplication.Courante.Split('+')[0],
            ["signingKey"] = cles.Signature.VersJson(), ["encryptionKey"] = cles.Chiffrement.VersJson(),
        };
        var couvert = new JsonObject { ["codeId"] = normalise[..4], ["peer"] = pair, ["nonce"] = nonce };
        var mac = CryptoLiaison.Mac(normalise, couvert);
        var corps = (JsonObject)couvert.DeepClone();
        corps["mac"] = mac;
        using var requete = new HttpRequestMessage(HttpMethod.Post, new Uri($"{adresse}{Chemin}/pair"))
        {
            Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        requete.Headers.UserAgent.ParseAdd($"gestion-mep/{VersionApplication.Courante.Split('+')[0]}");
        HttpResponseMessage reponse;
        try { reponse = await transport.Envoyer(requete, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            var message = e is HttpRequestException h ? $"Noosphere injoignable ({h.Message.TrimEnd('.')})." : "Noosphere ne répond pas (15 s).";
            Noter(null, "sortant", "pair", "echec", null, $"{adresse} : {message}");
            await db.SaveChangesAsync(CancellationToken.None);
            throw new ErreurLiaison(message);
        }
        using (reponse)
        {
            var octets = await reponse.Content.ReadAsByteArrayAsync(ct);
            var statut = (int)reponse.StatusCode;
            if (statut != 200)
            {
                var message = statut switch
                {
                    404 => "Code inconnu ou expiré (valable 15 minutes) : créez un nouveau code dans Noosphere.",
                    409 => "Ce code a déjà servi : créez un nouveau code dans Noosphere.",
                    410 => "Code bloqué après 5 essais erronés : créez un nouveau code dans Noosphere.",
                    401 => "Code incorrect : vérifiez sa saisie (après 5 essais erronés, Noosphere le bloque).",
                    _ => DetailProbleme(octets) is { Length: > 0 } d ? $"Noosphere a refusé l'appairage ({statut}) : {d}" : $"Noosphere a refusé l'appairage ({statut}).",
                };
                Noter(null, "sortant", "pair", "echec", statut, $"{adresse} : {message}");
                await db.SaveChangesAsync(CancellationToken.None);
                throw statut is 401 or 404 or 409 or 410 ? new ErreurHttp(statut == 404 ? 404 : statut == 409 ? 409 : 422, message) : new ErreurLiaison(message, statut);
            }
            var liaison = LireReponseAppairage(octets, normalise, nonce, cles, adresse, out var refus);
            if (liaison is null)
            {
                Noter(null, "sortant", "pair", "echec", statut, $"{adresse} : {refus}");
                await db.SaveChangesAsync(CancellationToken.None);
                throw new ErreurLiaison($"Réponse d'appairage refusée : {refus}.");
            }
            if (await db.Liaisons.AnyAsync(l => l.Id == liaison.Id, ct))
                throw ErreurHttp.Conflit("Noosphere a renvoyé l'identifiant d'une liaison déjà connue.");
            liaison.AppaireeLe = Maintenant;
            liaison.AppaireePar = acteur?.Nom ?? auteur;
            liaison.DernierEchangeLe = Maintenant;
            db.Liaisons.Add(liaison);
            Noter(liaison.Id, "sortant", "pair", "ok", statut, $"Appairée avec {liaison.NomInstance} ({adresse}) ; empreinte {liaison.Empreinte}, à confirmer.");
            JournalAdmin.Ecrire(db, acteur, "liaison.appairee", $"Liaison avec {liaison.NomInstance} ({adresse}) appairée, empreinte « {liaison.Empreinte} » à confirmer", auteur);
            await db.SaveChangesAsync(CancellationToken.None);
            return liaison;
        }
    }

    /// <summary>Contrôle la réponse de <c>/pair</c> (MAC, nonce, produit, clés) et prépare la liaison ; null avec la raison sinon.</summary>
    public static Liaison? LireReponseAppairage(byte[] octets, string code, string nonce, ClesLocales cles, string adresse, out string refus)
    {
        refus = "";
        JsonObject? o;
        try { o = JsonNode.Parse(octets) as JsonObject; }
        catch (JsonException) { o = null; }
        if (o is null) { refus = "JSON illisible"; return null; }
        var couvert = new JsonObject
        {
            ["linkId"] = o["linkId"]?.DeepClone(), ["peer"] = o["peer"]?.DeepClone(), ["capabilities"] = o["capabilities"]?.DeepClone(),
            ["nonce"] = o["nonce"]?.DeepClone(),
        };
        string? mac;
        try { mac = o["mac"]?.GetValue<string>(); }
        catch (InvalidOperationException) { mac = null; }
        bool macValide;
        try { macValide = CryptoLiaison.MacValide(code, couvert, mac); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { macValide = false; }
        if (!macValide) { refus = "MAC invalide (réponse modifiée, ou code différent)"; return null; }
        if (o["nonce"]?.GetValue<string>() != nonce) { refus = "nonce différent de celui envoyé"; return null; }
        if (!Guid.TryParse(o["linkId"]?.GetValue<string>(), out var id)) { refus = "linkId absent"; return null; }
        var p = o["peer"] as JsonObject;
        if (p?["product"]?.GetValue<string>() != "noosphere") { refus = "le pair n'est pas Noosphere"; return null; }
        var signature = JwkPublique.DepuisJson(p["signingKey"]);
        var chiffrement = JwkPublique.DepuisJson(p["encryptionKey"]);
        try
        {
            if (signature is null || chiffrement is null) throw new CryptographicException();
            CryptoLiaison.CleVerification(signature).Dispose();
            CryptoLiaison.ClePubliqueChiffrement(chiffrement).Dispose();
        }
        catch (CryptographicException) { refus = "clés de Noosphere invalides (EC P-256 attendu)"; return null; }
        var capacites = (o["capabilities"] as JsonArray)?.Select(c => c?.GetValue<string>()).OfType<string>().Distinct().ToList() ?? [];
        var clesPair = new ClesPair { Signature = signature, Chiffrement = chiffrement };
        return new Liaison
        {
            Id = id, Produit = "noosphere", NomInstance = p["instanceName"]?.GetValue<string>() ?? "Noosphere", Url = adresse,
            VersionPair = p["version"]?.GetValue<string>() ?? "", Etat = EtatLiaison.EnAttente, Capacites = capacites, Cles = cles, ClesPair = clesPair,
            KidPair = CryptoLiaison.Kid(signature), Empreinte = CalculerEmpreinte(cles, clesPair),
        };
    }

    /// <summary>Confirme l'empreinte lue sur l'écran de Noosphere : 422 si elle diffère. La liaison devient active ici.</summary>
    public async Task<Liaison> Confirmer(Guid id, string? empreinte, Acteur? acteur, string auteur)
    {
        var l = await Charger(id);
        if (l.Etat == EtatLiaison.Coupee) throw ErreurHttp.Conflit("Liaison coupée.");
        if (CryptoLiaison.NormaliserEmpreinte(empreinte) != CryptoLiaison.NormaliserEmpreinte(l.Empreinte))
        {
            JournalAdmin.Ecrire(db, acteur, "liaison.empreinte-differente", $"Empreinte saisie différente pour {l.NomInstance} : liaison non confirmée", auteur);
            await db.SaveChangesAsync();
            throw ErreurHttp.Regle(["L'empreinte ne correspond pas à celle de cette liaison : vérifiez les quatre mots sur l'écran de Noosphere, ou "
                + "coupez la liaison si elle ne vient pas de vous."]);
        }
        if (!l.ConfirmeeIci)
        {
            l.ConfirmeeIci = true;
            l.ConfirmeeLe = Maintenant;
            l.ConfirmeePar = acteur?.Nom ?? auteur;
            l.Etat = EtatLiaison.Active;
            JournalAdmin.Ecrire(db, acteur, "liaison.confirmee", $"Empreinte de la liaison avec {l.NomInstance} confirmée", auteur);
            await db.SaveChangesAsync();
        }
        signal.DemanderSynchro(l.Id, Maintenant);
        return l;
    }

    // ---------- État du pair et catalogue ----------

    /// <summary>Lit l'état de la liaison chez Noosphere (<c>GET /status</c>) : confirmation, nom, version, capacités.</summary>
    public async Task LireStatut(Liaison l, CancellationToken ct)
    {
        if (EstDemo(l))
        {
            l.ConfirmeeParPair = true;
            l.DernierEchangeLe = Maintenant;
            Noter(l.Id, "sortant", "status", "demo", null, "Démonstration : aucun appel, Noosphere fictif confirmé.");
            return;
        }
        try
        {
            var r = await Appeler(l, "status", HttpMethod.Get, "/status", null, ct);
            var statut = JsonSerializer.Deserialize<StatutLiaison>(r.Clair, Json) ?? throw new JsonException();
            l.ConfirmeeParPair = statut.Confirmee;
            if (statut.NomInstance is { Length: > 0 } n) l.NomInstance = n;
            if (statut.Version is { Length: > 0 } v) l.VersionPair = v;
            if (statut.Capacites is { } c) l.Capacites = [.. c.Distinct()];
            if (DateTimeOffset.TryParse(statut.ClesRenouveleesLe, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var renouvelees) && (l.ClesRenouveleesLe is null || renouvelees > l.ClesRenouveleesLe))
                l.ClesRenouveleesLe = renouvelees;
            Noter(l.Id, "sortant", "status", "ok", r.Code, statut.Confirmee ? "Confirmée par Noosphere." : "En attente de confirmation dans Noosphere.");
        }
        catch (ErreurLiaison e) when (e.Raison == RefusLiaison.NonConfirmee)
        {
            l.ConfirmeeParPair = false;
        }
        catch (JsonException)
        {
            Noter(l.Id, "sortant", "status", "echec", 200, "État illisible.");
            throw new ErreurLiaison("État renvoyé par Noosphere illisible.");
        }
    }

    /// <summary>
    /// Synchronise la liaison : état du pair puis catalogue (<c>If-None-Match</c> avec le dernier ETag : 304 si rien n'a
    /// changé), appliqué aux systèmes suivis. Exige la confirmation ici ; Noosphere répond 403 tant qu'il n'a pas confirmé.
    /// </summary>
    public async Task<(BilanSynchro Bilan, bool Inchange)> Synchroniser(Liaison l, Acteur? acteur, string auteur, CancellationToken ct)
    {
        if (l.Etat == EtatLiaison.Coupee) throw ErreurHttp.Conflit("Liaison coupée.");
        if (!l.ConfirmeeIci) throw ErreurHttp.Regle(["Confirmez d'abord l'empreinte de la liaison."]);
        var verrou = signal.Verrou(l.Id);
        await verrou.WaitAsync(ct);
        try
        {
            try
            {
                await LireStatut(l, ct);
                var resultat = await LireCatalogue(l, ct);
                l.DerniereErreur = "";
                if (resultat.Bilan.AChange)
                    JournalAdmin.Ecrire(db, acteur, "liaison.synchronisee", $"Catalogue de {l.NomInstance} : {resultat.Bilan.Resume()}", auteur);
                await db.SaveChangesAsync(CancellationToken.None);
                return resultat;
            }
            catch (ErreurLiaison e)
            {
                l.DerniereErreur = e.Message;
                if (e.Raison == RefusLiaison.NonConfirmee) l.ConfirmeeParPair = false;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally { verrou.Release(); }
    }

    private async Task<(BilanSynchro Bilan, bool Inchange)> LireCatalogue(Liaison l, CancellationToken ct)
    {
        CatalogueNoosphere catalogue;
        if (EstDemo(l))
        {
            catalogue = DonneesDemo.CatalogueNoosphere(l.Url);
            Noter(l.Id, "sortant", "catalog", "demo", null, $"Démonstration : catalogue fictif ({catalogue.Applications?.Count ?? 0} applications).");
        }
        else
        {
            if (!l.Capacites.Contains(CapacitesLiaison.LectureCatalogue))
                throw new ErreurLiaison("Noosphere n'accorde pas la lecture du catalogue (capacité catalog:read) à cette liaison.");
            var r = await Appeler(l, "catalog", HttpMethod.Get, "/catalog", null, ct, l.EtagCatalogue.Length > 0 && l.Catalogue is not null ? l.EtagCatalogue : null);
            if (r.Code == 304)
            {
                l.DerniereSynchro = Maintenant;
                Noter(l.Id, "sortant", "catalog", "ok", 304, "Catalogue inchangé.");
                return (new BilanSynchro(), true);
            }
            try { catalogue = JsonSerializer.Deserialize<CatalogueNoosphere>(r.Clair, Json) ?? throw new JsonException(); }
            catch (JsonException)
            {
                Noter(l.Id, "sortant", "catalog", "echec", r.Code, "Catalogue illisible.");
                throw new ErreurLiaison("Catalogue renvoyé par Noosphere illisible.");
            }
            l.EtagCatalogue = r.Etag ?? "";
        }
        var bilan = await AppliquerCatalogue(l, catalogue);
        Noter(l.Id, "sortant", "catalog", EstDemo(l) ? "demo" : "ok", EstDemo(l) ? null : 200,
            $"{catalogue.Applications?.Count ?? 0} application(s), {catalogue.Domaines?.Count ?? 0} domaine(s) ; systèmes : {bilan.Resume()}"
            + (bilan.Avertissements.Count > 0 ? $" ; {string.Join(" ", bilan.Avertissements)}" : ""));
        return (bilan, false);
    }

    private async Task<BilanSynchro> AppliquerCatalogue(Liaison l, CatalogueNoosphere catalogue)
    {
        l.Catalogue = catalogue;
        l.DerniereSynchro = Maintenant;
        if (catalogue.NomInstance is { Length: > 0 } n) l.NomInstance = n;
        var systemes = await db.SystemesCatalogue.ToListAsync();
        var bilan = SynchroLiaison.Appliquer(l, catalogue, systemes, Maintenant);
        db.SystemesCatalogue.AddRange(bilan.Nouveaux);
        if (bilan.AChange) signal.DemanderRapprochement();
        return bilan;
    }

    // ---------- Choix des applications suivies ----------

    public CatalogueLiaisonDto Catalogue(Liaison l, List<SystemeCatalogue> systemes)
    {
        var c = l.Catalogue;
        if (c is null) return new CatalogueLiaisonDto(null, l.NomInstance, [], []);
        var apps = c.Applications ?? [];
        var domaines = (c.Domaines ?? []).Select(d => new DomaineLiaisonDto(d.Id, d.Nom, l.DomainesSuivis.Contains(d.Id), apps.Count(a => a.DomaineId == d.Id)))
            .OrderBy(d => d.Nom, StringComparer.OrdinalIgnoreCase).ToList();
        var parApp = systemes.Where(s => s.Source?.LiaisonId == l.Id).ToDictionary(s => s.Source!.ApplicationId);
        return new CatalogueLiaisonDto(l.DerniereSynchro, c.NomInstance ?? l.NomInstance, domaines, apps.OrderBy(a => a.Nom, StringComparer.OrdinalIgnoreCase).Select(a =>
        {
            var suivi = parApp.GetValueOrDefault(a.Id);
            return new ApplicationLiaisonDto(a.Id, a.Nom, a.Description ?? "", a.DomaineId, c.Domaines?.FirstOrDefault(d => d.Id == a.DomaineId)?.Nom ?? "",
                c.Equipes?.FirstOrDefault(e => e.Id == a.EquipeId)?.Nom ?? "", a.Type ?? "", a.Cycle ?? "", a.Url ?? "", suivi is not null, suivi?.Id, suivi?.Nom,
                suivi is not null ? [] : SynchroLiaison.Suggestions(a, systemes).Take(5)
                    .Select(s => new SuggestionSystemeDto(s.Id, s.Nom, SynchroLiaison.NomNormalise(s.Nom) == SynchroLiaison.NomNormalise(a.Nom))).ToList());
        }).ToList());
    }

    /// <summary>Applique les choix de suivi (domaines, applications une par une) sur le dernier catalogue lu.</summary>
    public async Task<BilanSynchro> ChoisirSuivi(Liaison l, SuiviLiaisonSaisie s, Acteur? acteur, string auteur)
    {
        if (l.Etat == EtatLiaison.Coupee) throw ErreurHttp.Conflit("Liaison coupée.");
        var c = l.Catalogue ?? throw ErreurHttp.Regle(["Catalogue de Noosphere pas encore lu : synchronisez d'abord la liaison."]);
        var apps = (c.Applications ?? []).ToDictionary(a => a.Id);
        var systemes = await db.SystemesCatalogue.ToListAsync();
        var bilan = new BilanSynchro();
        var retires = new List<string>();
        if (s.DomainesSuivis is { } domaines)
        {
            var connus = (c.Domaines ?? []).Select(d => d.Id).ToHashSet();
            var inconnus = domaines.Where(d => !connus.Contains(d)).ToList();
            if (inconnus.Count > 0) throw ErreurHttp.Saisie($"Domaine inconnu de Noosphere : {string.Join(", ", inconnus)}.");
            var ajoutes = domaines.Except(l.DomainesSuivis).ToList();
            var enleves = l.DomainesSuivis.Except(domaines).ToList();
            l.DomainesSuivis = [.. domaines.Distinct()];
            // Une application choisie une par une dans la même saisie suit ce choix (système désigné, ou plus suivie) : le
            // domaine ne lui crée pas d'abord un système qui resterait ensuite dans le catalogue comme système local en double.
            var choisies = (s.Applications ?? []).Select(a => a.ApplicationId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var app in apps.Values.Where(a => a.DomaineId is { } d && ajoutes.Contains(d) && !choisies.Contains(a.Id)
                         && !string.Equals(a.Cycle, "retired", StringComparison.OrdinalIgnoreCase)))
                SynchroLiaison.Suivre(l, c, app, systemes, null, Maintenant, bilan);
            foreach (var sys in systemes.Where(x => x.Source?.LiaisonId == l.Id && apps.TryGetValue(x.Source.ApplicationId, out var a) && a.DomaineId is { } d
                         && enleves.Contains(d)).ToList())
            {
                retires.Add(sys.Nom);
                sys.Source = null;
                sys.Version++;
            }
        }
        foreach (var choix in s.Applications ?? [])
        {
            if (string.IsNullOrWhiteSpace(choix.ApplicationId) || !apps.TryGetValue(choix.ApplicationId, out var app))
                throw ErreurHttp.Saisie($"Application inconnue de Noosphere : {choix.ApplicationId}.");
            if (choix.Suivre == false)
            {
                if (systemes.FirstOrDefault(x => x.Source?.LiaisonId == l.Id && x.Source.ApplicationId == app.Id) is { } sys)
                {
                    retires.Add(sys.Nom);
                    sys.Source = null;
                    sys.Version++;
                }
                continue;
            }
            var existant = systemes.FirstOrDefault(x => x.Source?.LiaisonId == l.Id && x.Source.ApplicationId == app.Id);
            if (existant is not null && choix.SystemeId is { } autre && autre != existant.Id)
            {
                // Rattachement changé : l'ancien système redevient local.
                retires.Add(existant.Nom);
                existant.Source = null;
                existant.Version++;
            }
            try { SynchroLiaison.Suivre(l, c, app, systemes, choix.SystemeId, Maintenant, bilan); }
            catch (SaisieInvalideException e) { throw ErreurHttp.Saisie(e.Message); }
        }
        db.SystemesCatalogue.AddRange(bilan.Nouveaux);
        var details = new List<string>();
        if (bilan.Crees.Count > 0) details.Add($"créés : {string.Join(", ", bilan.Crees)}");
        if (bilan.Rattaches.Count > 0) details.Add($"rattachés : {string.Join(", ", bilan.Rattaches)}");
        if (retires.Count > 0) details.Add($"redevenus locaux : {string.Join(", ", retires)}");
        JournalAdmin.Ecrire(db, acteur, "liaison.suivi", $"Applications suivies de {l.NomInstance}{(details.Count > 0 ? $" ({string.Join(" ; ", details)})" : "")}"
            + (s.DomainesSuivis is not null ? $", domaines suivis : {(l.DomainesSuivis.Count == 0 ? "aucun" : string.Join(", ", l.DomainesSuivis.Select(d => c.Domaines?.FirstOrDefault(x => x.Id == d)?.Nom ?? d)))}" : ""), auteur);
        await db.SaveChangesAsync();
        bilan.MisAJour.AddRange(retires);
        signal.DemanderRapprochement();
        return bilan;
    }

    public async Task Options(Liaison l, OptionsLiaisonSaisie s, Acteur? acteur, string auteur)
    {
        var changes = new List<string>();
        if (s.EnvoyerMeps is { } e && e != l.EnvoyerMeps)
        {
            l.EnvoyerMeps = e;
            changes.Add(e ? "envoi des MEP activé" : "envoi des MEP désactivé");
        }
        if (s.SuiviAutomatique is { } a && a != l.SuiviAutomatique)
        {
            l.SuiviAutomatique = a;
            changes.Add(a ? "suivi automatique des nouvelles applications activé" : "suivi automatique des nouvelles applications désactivé");
        }
        if (changes.Count == 0) return;
        JournalAdmin.Ecrire(db, acteur, "liaison.options", $"Liaison avec {l.NomInstance} : {string.Join(", ", changes)}", auteur);
        await db.SaveChangesAsync();
    }

    // ---------- Événements de déploiement ----------

    /// <summary>
    /// Prépare les événements de déploiement à envoyer à la liaison : MEP désignées (null : rapprochement complet) qui
    /// touchent un système suivi, ou déjà envoyées ; seuls ceux qui ont changé depuis le dernier envoi sont gardés. Une MEP
    /// supprimée après un envoi part « cancelled ».
    /// </summary>
    public async Task<List<EvenementDeploiement>> Preparer(Liaison l, IReadOnlyCollection<Guid>? meps)
    {
        var suivis = (await db.SystemesCatalogue.AsNoTracking().Where(s => s.Source != null).ToListAsync())
            .Where(s => s.Source!.LiaisonId == l.Id && !s.Source.Absente).ToDictionary(s => s.Id, s => s.Source!.ApplicationId);
        var designees = meps?.ToList();
        var qEnvoyes = db.DeploiementsLiaison.AsNoTracking().Where(d => d.LiaisonId == l.Id);
        if (designees is not null) qEnvoyes = qEnvoyes.Where(d => designees.Contains(d.MepId));
        var envoyes = await qEnvoyes.ToDictionaryAsync(d => d.MepId);
        var ids = suivis.Keys.ToList();
        var dejaEnvoyees = envoyes.Keys.ToList();
        var q = db.Meps.AsNoTracking().Include(m => m.Systemes).AsSplitQuery().Where(m => !m.EstModele);
        q = designees is null
            ? q.Where(m => m.Systemes.Any(s => s.CatalogueId != null && ids.Contains(s.CatalogueId.Value)) || dejaEnvoyees.Contains(m.Id))
            : q.Where(m => designees.Contains(m.Id));
        var liste = await q.ToListAsync();
        var urlBase = UrlLocale();
        var maintenant = Maintenant;
        var evenements = new List<EvenementDeploiement>();
        foreach (var m in liste)
        {
            var touche = m.Systemes.Any(s => s.CatalogueId is { } c && suivis.ContainsKey(c));
            var precedent = envoyes.GetValueOrDefault(m.Id);
            if (!touche && precedent is null) continue;
            if (precedent is null && Regles.EstTerminee(m) && (m.ClotureeLe ?? m.Annulation?.Le ?? m.CreeLe) < maintenant - HistoriqueEnvoye) continue;
            var e = SynchroLiaison.Evenement(m, suivis, urlBase, maintenant);
            if (precedent?.Empreinte != SynchroLiaison.Empreinte(e)) evenements.Add(e);
        }
        var presentes = liste.Select(m => m.Id).ToHashSet();
        foreach (var (mepId, envoi) in envoyes)
        {
            if (presentes.Contains(mepId) || envoi.Contenu is null || envoi.Contenu.Phase == "cancelled") continue;
            if (designees is null || designees.Contains(mepId)) evenements.Add(SynchroLiaison.Supprimee(envoi.Contenu, maintenant));
        }
        return evenements;
    }

    /// <summary>Liaison qui reçoit les MEP : confirmée ici, pas refusée par Noosphere, envoi activé, capacité accordée.</summary>
    public static bool EnvoieLesMeps(Liaison l) => l.Etat == EtatLiaison.Active && l.ConfirmeeIci && l.ConfirmeeParPair != false && l.EnvoyerMeps
        && l.Cles is not null && l.Capacites.Contains(CapacitesLiaison.ReceptionDeploiements);

    /// <summary>Envoie les événements (lots de 100) et retient chaque envoi accepté.</summary>
    public async Task<int> Envoyer(Liaison l, List<EvenementDeploiement> evenements, CancellationToken ct)
    {
        if (evenements.Count == 0) return 0;
        var verrou = signal.Verrou(l.Id);
        await verrou.WaitAsync(ct);
        try
        {
            var total = 0;
            foreach (var lot in evenements.Chunk(LotMax))
            {
                string detail;
                if (EstDemo(l))
                {
                    detail = $"Démonstration : {lot.Length} événement(s) non envoyé(s) ({string.Join(", ", lot.Select(e => $"{e.Titre} {e.Phase}"))}).";
                    Noter(l.Id, "sortant", "deployments", "demo", null, detail);
                }
                else
                {
                    var r = await Appeler(l, "deployments", HttpMethod.Post, "/deployments", new LotDeploiements([.. lot]), ct);
                    ReponseDeploiements? rep = null;
                    try { rep = r.Clair.Length > 0 ? JsonSerializer.Deserialize<ReponseDeploiements>(r.Clair, Json) : null; }
                    catch (JsonException) { /* accusé illisible : l'envoi est tout de même accepté (2xx signé) */ }
                    detail = $"{lot.Length} événement(s) ({string.Join(", ", lot.Select(e => $"{e.Titre} {e.Phase}").Take(10))}{(lot.Length > 10 ? "…" : "")})"
                        + (rep is null ? "" : $" ; acceptés : {rep.Acceptes}")
                        + (rep?.ApplicationsIgnorees is { Count: > 0 } ign ? $" ; applications inconnues de Noosphere : {string.Join(", ", ign)}" : "");
                    Noter(l.Id, "sortant", "deployments", "ok", r.Code, detail);
                }
                var ids = lot.Select(e => e.DeploiementId).ToList();
                var lignes = await db.DeploiementsLiaison.Where(d => d.LiaisonId == l.Id && ids.Contains(d.MepId)).ToDictionaryAsync(d => d.MepId, ct);
                foreach (var e in lot)
                {
                    if (!lignes.TryGetValue(e.DeploiementId, out var ligne))
                        db.DeploiementsLiaison.Add(ligne = new DeploiementLiaison { LiaisonId = l.Id, MepId = e.DeploiementId });
                    ligne.Empreinte = SynchroLiaison.Empreinte(e);
                    ligne.Contenu = e;
                    ligne.EnvoyeLe = Maintenant;
                }
                await db.SaveChangesAsync(CancellationToken.None);
                total += lot.Length;
            }
            return total;
        }
        finally { verrou.Release(); }
    }

    // ---------- Renouvellement des clés et coupure ----------

    /// <summary>
    /// Renouvelle les clés de gestion-mep : <c>POST /rekey</c> signé avec l'ancienne clé, nouvelles clés publiques chiffrées
    /// pour Noosphere. Après la réponse, les nouvelles clés servent ; les anciennes restent acceptées 5 minutes.
    /// </summary>
    public async Task Renouveler(Liaison l, Acteur? acteur, string auteur, CancellationToken ct)
    {
        if (l.Etat == EtatLiaison.Coupee || l.Cles is null) throw ErreurHttp.Conflit("Liaison coupée.");
        var verrou = signal.Verrou(l.Id);
        await verrou.WaitAsync(ct);
        try
        {
            var nouvelles = NouvellesCles();
            if (EstDemo(l)) Noter(l.Id, "sortant", "rekey", "demo", null, "Démonstration : nouvelles clés, aucun appel.");
            else
            {
                var r = await Appeler(l, "rekey", HttpMethod.Post, "/rekey", new NouvellesCles(nouvelles.Signature, nouvelles.Chiffrement), ct);
                Noter(l.Id, "sortant", "rekey", "ok", r.Code, "Clés de gestion-mep renouvelées.");
            }
            var anciennes = l.Cles;
            anciennes.ValablesJusquA = Maintenant + Grace;
            l.AnciennesCles = anciennes;
            l.Cles = nouvelles;
            l.ClesRenouveleesLe = Maintenant;
            l.Empreinte = CalculerEmpreinte(nouvelles, l.ClesPair!);
            JournalAdmin.Ecrire(db, acteur, "liaison.cles-renouvelees", $"Clés de la liaison avec {l.NomInstance} renouvelées, nouvelle empreinte « {l.Empreinte} »", auteur);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (ErreurLiaison)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally { verrou.Release(); }
    }

    /// <summary>Clés de Noosphere renouvelées (<c>POST /rekey</c> reçu) : l'ancienne clé de signature reste acceptée 5 minutes.</summary>
    public void AppliquerClesPair(Liaison l, NouvellesCles n)
    {
        var anciennes = l.ClesPair!;
        anciennes.ValablesJusquA = Maintenant + Grace;
        l.AnciennesClesPair = anciennes;
        l.AncienKidPair = l.KidPair;
        l.ClesPair = new ClesPair { Signature = n.Signature, Chiffrement = n.Chiffrement };
        l.KidPair = CryptoLiaison.Kid(n.Signature);
        l.ClesRenouveleesLe = Maintenant;
        l.Empreinte = CalculerEmpreinte(l.Cles!, l.ClesPair);
        JournalAdmin.Ecrire(db, null, "liaison.cles-renouvelees", $"Clés de {l.NomInstance} renouvelées par Noosphere, nouvelle empreinte « {l.Empreinte} »",
            JournalAdmin.AuteurApplication);
    }

    /// <summary>Efface les clés, marque la liaison coupée et rend ses systèmes locaux (les MEP et le journal restent).</summary>
    public async Task MarquerCoupee(Liaison l, string par)
    {
        l.Etat = EtatLiaison.Coupee;
        l.CoupeeLe = Maintenant;
        l.CoupeePar = par;
        l.Cles = null;
        l.AnciennesCles = null;
        l.ClesPair = null;
        l.AnciennesClesPair = null;
        l.AncienKidPair = "";
        l.ConfirmeeIci = false;
        var systemes = await db.SystemesCatalogue.Where(s => s.Source != null).ToListAsync();
        var n = SynchroLiaison.RendreLocaux(l.Id, systemes);
        await db.DeploiementsLiaison.Where(d => d.LiaisonId == l.Id).ExecuteDeleteAsync();
        JournalAdmin.Ecrire(db, null, "liaison.coupee", $"Liaison avec {l.NomInstance} coupée par {par}{(n > 0 ? $" ; {n} système(s) redevenu(s) local(aux)" : "")}",
            JournalAdmin.AuteurApplication);
    }

    /// <summary>Coupe la liaison : prévient Noosphere (au mieux, <c>POST /unpair</c>), puis efface les clés.</summary>
    public async Task<bool> Couper(Liaison l, Acteur? acteur, string auteur, CancellationToken ct)
    {
        if (l.Etat == EtatLiaison.Coupee) throw ErreurHttp.Conflit("Liaison déjà coupée.");
        var prevenu = false;
        if (EstDemo(l)) Noter(l.Id, "sortant", "unpair", "demo", null, "Démonstration : aucun appel.");
        else if (l.Cles is not null)
        {
            try
            {
                var r = await Appeler(l, "unpair", HttpMethod.Post, "/unpair", null, ct);
                Noter(l.Id, "sortant", "unpair", "ok", r.Code, "Noosphere prévenu de la coupure.");
                prevenu = true;
            }
            catch (ErreurLiaison) { /* au mieux : la coupure se fait quand même, l'échec est au journal */ }
        }
        await MarquerCoupee(l, acteur?.Nom ?? auteur);
        await db.SaveChangesAsync(CancellationToken.None);
        return prevenu;
    }

    // ---------- Journal ----------

    public async Task<List<EchangeLiaisonDto>> Echanges(Guid? liaison, DateTimeOffset? avant, int? limite, bool refusSeulement = false)
    {
        var n = Math.Clamp(limite ?? 50, 1, 500);
        var q = db.EchangesLiaison.AsNoTracking();
        if (liaison is { } id) q = q.Where(e => e.LiaisonId == id);
        if (avant is { } a) q = q.Where(e => e.Quand < a);
        if (refusSeulement) q = q.Where(e => e.Resultat == "refuse");
        return await q.OrderByDescending(e => e.Quand).ThenByDescending(e => e.Id).Take(n)
            .Select(e => new EchangeLiaisonDto(e.Id, e.LiaisonId, e.Quand, e.Sens, e.Operation, e.Resultat, e.Code, e.Detail)).ToListAsync();
    }
}
