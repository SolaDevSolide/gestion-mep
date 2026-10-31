using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Noosphere en mémoire, pour les tests : il sert <c>/link/v1/pair</c>, <c>/status</c>, <c>/catalog</c> (ETag),
/// <c>/deployments</c>, <c>/rekey</c> et <c>/unpair</c> selon le contrat, vérifie les signatures de gestion-mep et signe ses
/// réponses. Il sait aussi construire les requêtes signées qu'il envoie à gestion-mep (avis, état, renouvellement, coupure).
/// </summary>
public sealed class PairNoosphereFactice : HttpMessageHandler
{
    public const string Url = "https://noosphere.test";
    public string Code { get; set; } = "7K4Q-29XD-M3PA";
    public Guid LinkId { get; private set; } = Guid.NewGuid();
    public ECDsa Signature { get; private set; } = CryptoLiaison.NouvelleCleSignature();
    public ECDiffieHellman Chiffrement { get; private set; } = CryptoLiaison.NouvelleCleChiffrement();
    public ECDsa? AncienneSignature { get; private set; }
    public JwkPublique? SignatureMep { get; private set; }
    public JwkPublique? ChiffrementMep { get; private set; }
    public bool Confirme { get; set; }
    public bool Coupe { get; private set; }
    public List<string> Capacites { get; set; } = [.. CapacitesLiaison.Toutes];
    public CatalogueNoosphere Catalogue { get; set; } = CatalogueParDefaut();
    public string Etag { get; set; } = "\"v1\"";
    public List<EvenementDeploiement> Deploiements { get; } = [];
    public int EchecsDeploiementsAVenir { get; set; }
    public int AppelsDeploiements { get; private set; }
    public List<string> Recus { get; } = [];
    public int EchecsMac { get; private set; }
    public bool CodeUtilise { get; private set; }
    public bool SignerReponsesFaux { get; set; }
    /// <summary>Horloge du pair (partagée avec celle de gestion-mep dans les tests qui avancent le temps).</summary>
    public Func<DateTimeOffset> Horloge { get; set; } = () => DateTimeOffset.UtcNow;
    /// <summary>Tout appel lève une exception (vérifie que la démonstration n'appelle rien).</summary>
    public bool Interdit { get; set; }

    public static CatalogueNoosphere CatalogueParDefaut() => new("2026-10-31T12:00:00Z", "Noosphere Test", Url,
        [new("finance", "Finance"), new("socle", "Socle")],
        [new("equipe-socle", "Équipe Socle", "socle@exemple.invalid"), new("equipe-fin", "Équipe Finance", null)],
        [
            new("bdd", "BDD", "Base de données (Noosphere)", "socle", "equipe-socle", "data", "live", null, $"{Url}/applications/bdd"),
            new("ldap", "LDAP", "Annuaire (Noosphere)", "socle", "equipe-socle", "app", "live", null, $"{Url}/applications/ldap"),
            new("billing", "Facturation", "Factures", "finance", "equipe-fin", "app", "live", null, $"{Url}/applications/billing"),
        ],
        [new("f1", "Billing feed", "billing", "bdd", "file", "live", null, $"{Url}/flows/f1")]);

    public JwkPublique JwkSignature => CryptoLiaison.Jwk(Signature);
    public JwkPublique JwkChiffrement => CryptoLiaison.Jwk(Chiffrement);

    public string Empreinte() => CryptoLiaison.EmpreinteMots([JwkSignature, JwkChiffrement, SignatureMep!, ChiffrementMep!]);

    // ---------- Côté serveur (gestion-mep appelle Noosphere) ----------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
    {
        if (Interdit) throw new InvalidOperationException($"Appel inattendu : {requete.Method} {requete.RequestUri}");
        var chemin = requete.RequestUri!.AbsolutePath;
        Recus.Add($"{requete.Method} {chemin}");
        var corps = requete.Content is null ? [] : await requete.Content.ReadAsByteArrayAsync(ct);
        if (chemin == "/link/v1/pair") return Appairer(corps);
        if (Coupe) return Probleme(401, RefusLiaison.LiaisonCoupee);
        if (VerifierRequete(requete, corps) is { } refus) return Probleme(refus == RefusLiaison.NonConfirmee ? 403 : 401, refus);
        switch (requete.Method.Method, chemin)
        {
            case ("GET", "/link/v1/status"):
                return Signer(200, Chiffrer(new StatutLiaison(LinkId, "noosphere", "Noosphere Test", "1.5.0", Confirme, Capacites, null)));
            case ("GET", "/link/v1/catalog"):
                if (!Confirme) return Probleme(403, RefusLiaison.NonConfirmee);
                if (requete.Headers.TryGetValues("If-None-Match", out var v) && v.Contains(Etag)) return Signer(304, null, Etag);
                return Signer(200, Chiffrer(Catalogue), Etag);
            case ("POST", "/link/v1/deployments"):
                AppelsDeploiements++;
                if (EchecsDeploiementsAVenir > 0)
                {
                    EchecsDeploiementsAVenir--;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new ByteArrayContent([]) };
                }
                var lot = JsonSerializer.Deserialize<LotDeploiements>(Dechiffrer(corps))!;
                var connues = (Catalogue.Applications ?? []).Select(a => a.Id).ToHashSet();
                foreach (var e in lot.Evenements)
                {
                    Deploiements.RemoveAll(d => d.DeploiementId == e.DeploiementId);
                    Deploiements.Add(e);
                }
                return Signer(200, Chiffrer(new ReponseDeploiements(lot.Evenements.Count,
                    [.. lot.Evenements.SelectMany(e => e.ApplicationIds).Where(a => !connues.Contains(a)).Distinct()])));
            case ("POST", "/link/v1/rekey"):
                var cles = JsonSerializer.Deserialize<NouvellesCles>(Dechiffrer(corps))!;
                var reponse = Signer(200, null);
                SignatureMep = cles.Signature;
                ChiffrementMep = cles.Chiffrement;
                return reponse;
            case ("POST", "/link/v1/unpair"):
                var fin = Signer(204, null);
                Coupe = true;
                return fin;
        }
        return Probleme(404, "not-found");
    }

    private HttpResponseMessage Appairer(byte[] corps)
    {
        var o = JsonNode.Parse(corps)!.AsObject();
        var couvert = new JsonObject { ["codeId"] = o["codeId"]!.DeepClone(), ["peer"] = o["peer"]!.DeepClone(), ["nonce"] = o["nonce"]!.DeepClone() };
        var normalise = CryptoLiaison.NormaliserCode(Code);
        if (o["codeId"]!.GetValue<string>() != normalise[..4]) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (EchecsMac >= 5) return new HttpResponseMessage(HttpStatusCode.Gone);
        if (CodeUtilise) return new HttpResponseMessage(HttpStatusCode.Conflict);
        if (!CryptoLiaison.MacValide(Code, couvert, o["mac"]!.GetValue<string>()))
        {
            EchecsMac++;
            return new HttpResponseMessage(EchecsMac >= 5 ? HttpStatusCode.Gone : HttpStatusCode.NotFound);
        }
        CodeUtilise = true;
        var pair = o["peer"]!.AsObject();
        if (pair["product"]!.GetValue<string>() != "gestion-mep") throw new InvalidOperationException("produit inattendu");
        SignatureMep = JwkPublique.DepuisJson(pair["signingKey"]);
        ChiffrementMep = JwkPublique.DepuisJson(pair["encryptionKey"]);
        var reponse = new JsonObject
        {
            ["linkId"] = LinkId.ToString(),
            ["peer"] = new JsonObject
            {
                ["product"] = "noosphere", ["instanceName"] = "Noosphere Test", ["url"] = Url, ["version"] = "1.5.0",
                ["signingKey"] = JwkSignature.VersJson(), ["encryptionKey"] = JwkChiffrement.VersJson(),
            },
            ["capabilities"] = new JsonArray([.. Capacites.Select(c => JsonValue.Create(c))]),
            ["nonce"] = o["nonce"]!.GetValue<string>(),
        };
        reponse["mac"] = CryptoLiaison.Mac(Code, reponse);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reponse.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    /// <summary>Vérification côté Noosphere d'une requête de gestion-mep (assemblage indépendant de la base de signature).</summary>
    private string? VerifierRequete(HttpRequestMessage r, byte[] corps)
    {
        var entree = r.Headers.GetValues("Signature-Input").Single();
        var sig = CryptoLiaison.LireSignature(entree, r.Headers.GetValues("Signature").Single(), "link");
        if (sig is null || SignatureMep is null) return RefusLiaison.Signature;
        if (sig.Kid != CryptoLiaison.Kid(SignatureMep)) return RefusLiaison.CleInconnue;
        if (Math.Abs(Horloge().ToUnixTimeSeconds() - sig.Cree!.Value) > 300) return RefusLiaison.Horloge;
        var digest = r.Content?.Headers.TryGetValues("Content-Digest", out var d) == true ? d.Single() : null;
        if (corps.Length > 0 && !CryptoLiaison.DigestValide(digest, corps)) return RefusLiaison.Empreinte;
        var lignes = new StringBuilder()
            .Append("\"@method\": ").Append(r.Method.Method).Append('\n')
            .Append("\"@path\": ").Append(r.RequestUri!.AbsolutePath).Append('\n')
            .Append("\"@query\": ").Append(r.RequestUri.Query.Length == 0 ? "?" : r.RequestUri.Query).Append('\n');
        if (corps.Length > 0) lignes.Append("\"content-digest\": ").Append(digest).Append('\n');
        var attendu = corps.Length > 0 ? "(\"@method\" \"@path\" \"@query\" \"content-digest\")" : "(\"@method\" \"@path\" \"@query\")";
        if (!entree.StartsWith($"link={attendu};created=", StringComparison.Ordinal) || !entree.Contains(";tag=\"noosphere-link\"", StringComparison.Ordinal))
            return RefusLiaison.Signature;
        lignes.Append("\"@signature-params\": ").Append(entree["link=".Length..]);
        using var cle = CryptoLiaison.CleVerification(SignatureMep);
        return cle.VerifyData(Encoding.ASCII.GetBytes(lignes.ToString()), sig.Valeur, HashAlgorithmName.SHA256) ? null : RefusLiaison.Signature;
    }

    private static HttpResponseMessage Probleme(int statut, string raison) => new((HttpStatusCode)statut)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { type = RefusLiaison.Type(raison), title = raison, status = statut, detail = raison }),
            Encoding.UTF8, "application/problem+json"),
    };

    private string Chiffrer(object contenu) => CryptoLiaison.Chiffrer(JsonSerializer.SerializeToUtf8Bytes(contenu), ChiffrementMep!);

    private byte[] Dechiffrer(byte[] corps) => CryptoLiaison.Dechiffrer(Encoding.ASCII.GetString(corps), Chiffrement);

    private HttpResponseMessage Signer(int statut, string? jwe, string? etag = null)
    {
        var rep = new HttpResponseMessage((HttpStatusCode)statut);
        var corps = jwe is null ? [] : Encoding.ASCII.GetBytes(jwe);
        rep.Content = new ByteArrayContent(corps);
        var composants = new List<(string, string)> { ("@status", statut.ToString(CultureInfo.InvariantCulture)) };
        if (corps.Length > 0)
        {
            rep.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose");
            var digest = CryptoLiaison.Digest(corps);
            rep.Headers.TryAddWithoutValidation("Content-Digest", digest);
            composants.Add(("content-digest", digest));
        }
        if (etag is not null) rep.Headers.TryAddWithoutValidation("ETag", etag);
        var p = CryptoLiaison.ParametresSignature(composants.Select(c => c.Item1), Horloge().ToUnixTimeSeconds(), CryptoLiaison.Aleatoire(),
            CryptoLiaison.Kid(JwkSignature), CryptoLiaison.TagReponse);
        var valeur = CryptoLiaison.Signer(Signature, CryptoLiaison.BaseSignature(composants, p));
        if (SignerReponsesFaux) valeur[0] ^= 0xFF;
        rep.Headers.TryAddWithoutValidation("Signature-Input", CryptoLiaison.EnteteSignatureInput(p));
        rep.Headers.TryAddWithoutValidation("Signature", CryptoLiaison.EnteteSignature(valeur));
        return rep;
    }

    // ---------- Côté client (Noosphere appelle gestion-mep) ----------

    /// <summary>Options d'une requête signée envoyée à gestion-mep (pour provoquer chaque refus).</summary>
    public sealed record Alteration(long DecalageSecondes = 0, string? Nonce = null, bool DigestFaux = false, bool SignatureFausse = false,
        ECDsa? Cle = null, string? Chemin = null, string? CorpsBrut = null);

    public HttpRequestMessage Requete(HttpMethod methode, string chemin, object? contenu = null, Alteration? a = null)
    {
        a ??= new Alteration();
        var r = new HttpRequestMessage(methode, chemin);
        var composants = new List<(string, string)>
        {
            ("@method", methode.Method), ("@path", a.Chemin ?? chemin.Split('?')[0]), ("@query", chemin.Contains('?') ? chemin[chemin.IndexOf('?')..] : "?"),
        };
        if (contenu is not null)
        {
            var corps = Encoding.ASCII.GetBytes(a.CorpsBrut ?? CryptoLiaison.Chiffrer(JsonSerializer.SerializeToUtf8Bytes(contenu), ChiffrementMep!));
            r.Content = new ByteArrayContent(corps);
            r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose");
            var digest = a.DigestFaux ? CryptoLiaison.Digest(Encoding.ASCII.GetBytes("autre")) : CryptoLiaison.Digest(corps);
            r.Content.Headers.TryAddWithoutValidation("Content-Digest", digest);
            composants.Add(("content-digest", digest));
        }
        var cle = a.Cle ?? Signature;
        var p = CryptoLiaison.ParametresSignature(composants.Select(c => c.Item1), Horloge().ToUnixTimeSeconds() + a.DecalageSecondes,
            a.Nonce ?? CryptoLiaison.Aleatoire(), CryptoLiaison.Kid(CryptoLiaison.Jwk(cle)), CryptoLiaison.TagRequete);
        var valeur = CryptoLiaison.Signer(cle, CryptoLiaison.BaseSignature(composants, p));
        if (a.SignatureFausse) valeur[10] ^= 0x55;
        r.Headers.TryAddWithoutValidation("Signature-Input", CryptoLiaison.EnteteSignatureInput(p));
        r.Headers.TryAddWithoutValidation("Signature", CryptoLiaison.EnteteSignature(valeur));
        return r;
    }

    /// <summary>Vérifie la signature d'une réponse de gestion-mep et renvoie le corps déchiffré (vide sans corps).</summary>
    public async Task<byte[]> LireReponse(HttpResponseMessage rep)
    {
        var corps = await rep.Content.ReadAsByteArrayAsync();
        var sig = CryptoLiaison.LireSignature(rep.Headers.GetValues("Signature-Input").Single(), rep.Headers.GetValues("Signature").Single())
            ?? throw new InvalidOperationException("réponse non signée");
        if (sig.Tag != CryptoLiaison.TagReponse) throw new InvalidOperationException("étiquette");
        var composants = new List<(string, string)> { ("@status", ((int)rep.StatusCode).ToString(CultureInfo.InvariantCulture)) };
        if (corps.Length > 0)
        {
            var digest = rep.Headers.GetValues("Content-Digest").Single();
            if (!CryptoLiaison.DigestValide(digest, corps)) throw new InvalidOperationException("digest");
            composants.Add(("content-digest", digest));
        }
        if (!sig.Composants.SequenceEqual(composants.Select(c => c.Item1))) throw new InvalidOperationException("composants");
        using var cle = CryptoLiaison.CleVerification(SignatureMep!);
        if (!CryptoLiaison.VerifierSignature(cle, CryptoLiaison.BaseSignature(composants, sig.Parametres), sig.Valeur))
            throw new InvalidOperationException("signature de la réponse invalide");
        return corps.Length == 0 || rep.Content.Headers.ContentType?.MediaType != "application/jose" ? corps : Dechiffrer(corps);
    }

    /// <summary>Renouvelle les clés de Noosphere : renvoie la requête (signée avec l'ancienne clé) et bascule ensuite.</summary>
    public HttpRequestMessage Renouveler()
    {
        var nouvelleS = CryptoLiaison.NouvelleCleSignature();
        var nouvelleC = CryptoLiaison.NouvelleCleChiffrement();
        var r = Requete(HttpMethod.Post, "/link/v1/rekey", new NouvellesCles(CryptoLiaison.Jwk(nouvelleS), CryptoLiaison.Jwk(nouvelleC)));
        AncienneSignature = Signature;
        Signature = nouvelleS;
        Chiffrement = nouvelleC;
        return r;
    }

    /// <summary>Nouveau Noosphere à la même adresse (nouvelle liaison, nouvelles clés, nouveau code).</summary>
    public void Reinitialiser(string code)
    {
        Code = code;
        LinkId = Guid.NewGuid();
        Signature = CryptoLiaison.NouvelleCleSignature();
        Chiffrement = CryptoLiaison.NouvelleCleChiffrement();
        CodeUtilise = false;
        Coupe = false;
        Confirme = false;
        EchecsMac = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Signature.Dispose();
            Chiffrement.Dispose();
            AncienneSignature?.Dispose();
        }
        base.Dispose(disposing);
    }
}
