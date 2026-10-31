using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GestionMep.Domain;

/// <summary>Clé publique P-256 au format JWK (<c>{kty:"EC", crv:"P-256", x, y}</c>, base64url sans remplissage).</summary>
public sealed record JwkPublique(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y)
{
    public JsonObject VersJson() => new() { ["kty"] = Kty, ["crv"] = Crv, ["x"] = X, ["y"] = Y };

    public static JwkPublique? DepuisJson(JsonNode? n) => n is JsonObject o
        && o["kty"]?.GetValue<string>() is { } kty && o["crv"]?.GetValue<string>() is { } crv
        && o["x"]?.GetValue<string>() is { } x && o["y"]?.GetValue<string>() is { } y
        ? new JwkPublique(kty, crv, x, y) : null;
}

/// <summary>Signature HTTP lue dans <c>Signature-Input</c> et <c>Signature</c> (RFC 9421), un seul libellé.</summary>
/// <param name="Parametres">Liste interne telle que reçue (« ("@method" …);created=…;… »), reprise mot pour mot dans la base.</param>
public sealed record SignatureLue(string Libelle, IReadOnlyList<string> Composants, string Parametres, long? Cree, string? Nonce, string? Kid,
    string? Alg, string? Tag, byte[] Valeur);

/// <summary>
/// Cryptographie de la liaison avec Noosphere, uniquement avec <c>System.Security.Cryptography</c> : clés P-256 (JWK et
/// empreinte RFC 7638), empreinte en quatre mots, JSON canonique et MAC d'appairage, signatures HTTP RFC 9421
/// (<c>ecdsa-p256-sha256</c>), <c>Content-Digest</c> RFC 9530 et JWE compact <c>ECDH-ES</c> + <c>A256GCM</c> (Concat KDF RFC 7518
/// §4.6.2). Aucun état : les clés privées sont passées et rendues en PKCS#8, que l'appelant chiffre au repos.
/// </summary>
public static class CryptoLiaison
{
    public const string Courbe = "P-256";
    public const string AlgSignature = "ecdsa-p256-sha256";
    public const string TagRequete = "noosphere-link";
    public const string TagReponse = "noosphere-link-response";
    public const string LibelleSignature = "link";

    // ---------- Encodages ----------

    public static string Base64Url(ReadOnlySpan<byte> octets) => Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DepuisBase64Url(string texte)
    {
        var s = texte.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException("base64url invalide") };
        return Convert.FromBase64String(s);
    }

    public static string Aleatoire(int octets = 16) => Base64Url(RandomNumberGenerator.GetBytes(octets));

    // ---------- Clés ----------

    public static ECDsa NouvelleCleSignature() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static ECDiffieHellman NouvelleCleChiffrement() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static JwkPublique Jwk(ECParameters p) =>
        new("EC", Courbe, Base64Url(p.Q.X!), Base64Url(p.Q.Y!));

    public static JwkPublique Jwk(ECDsa cle) => Jwk(cle.ExportParameters(false));

    public static JwkPublique Jwk(ECDiffieHellman cle) => Jwk(cle.ExportParameters(false));

    /// <summary>Paramètres d'une clé publique reçue ; refuse tout autre type, courbe ou taille de coordonnée.</summary>
    public static ECParameters Parametres(JwkPublique jwk)
    {
        if (jwk.Kty != "EC" || jwk.Crv != Courbe) throw new CryptographicException("Clé attendue : EC P-256.");
        var x = DepuisBase64Url(jwk.X);
        var y = DepuisBase64Url(jwk.Y);
        if (x.Length != 32 || y.Length != 32) throw new CryptographicException("Coordonnées P-256 de 32 octets attendues.");
        return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };
    }

    /// <summary>Clé publique de vérification (lève si le point n'est pas sur la courbe).</summary>
    public static ECDsa CleVerification(JwkPublique jwk)
    {
        var c = ECDsa.Create();
        c.ImportParameters(Parametres(jwk));
        return c;
    }

    public static ECDiffieHellman ClePubliqueChiffrement(JwkPublique jwk)
    {
        var c = ECDiffieHellman.Create();
        c.ImportParameters(Parametres(jwk));
        return c;
    }

    public static ECDsa ChargerSignature(byte[] pkcs8)
    {
        var c = ECDsa.Create();
        c.ImportPkcs8PrivateKey(pkcs8, out _);
        return c;
    }

    public static ECDiffieHellman ChargerChiffrement(byte[] pkcs8)
    {
        var c = ECDiffieHellman.Create();
        c.ImportPkcs8PrivateKey(pkcs8, out _);
        return c;
    }

    /// <summary>Empreinte RFC 7638 (SHA-256 de <c>{"crv","kty","x","y"}</c> dans cet ordre, sans espace), 32 octets.</summary>
    public static byte[] Empreinte7638(JwkPublique jwk) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":{Chaine(jwk.Crv)},\"kty\":{Chaine(jwk.Kty)},\"x\":{Chaine(jwk.X)},\"y\":{Chaine(jwk.Y)}}}"));

    /// <summary>Identifiant de clé : empreinte RFC 7638 en base64url.</summary>
    public static string Kid(JwkPublique jwk) => Base64Url(Empreinte7638(jwk));

    // ---------- Empreinte en quatre mots ----------

    /// <summary>
    /// « orbit cedar 0712 lumen » : empreintes RFC 7638 brutes des quatre clés publiques (deux par côté), triées par ordre
    /// lexicographique des octets, concaténées, SHA-256 → H ; mot(H[0]) mot(H[1]) ((H[2]&lt;&lt;8 | H[3]) % 10000 sur 4 chiffres) mot(H[4]).
    /// </summary>
    public static string EmpreinteMots(IEnumerable<JwkPublique> cles)
    {
        var empreintes = cles.Select(Empreinte7638).ToList();
        if (empreintes.Count != 4) throw new ArgumentException("Quatre clés publiques attendues.", nameof(cles));
        empreintes.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
        var h = SHA256.HashData(empreintes.SelectMany(e => e).ToArray());
        var nombre = ((h[2] << 8) | h[3]) % 10000;
        return $"{Mots[h[0]]} {Mots[h[1]]} {nombre.ToString("D4", CultureInfo.InvariantCulture)} {Mots[h[4]]}";
    }

    /// <summary>Empreinte saisie comparable : minuscules, espaces réduits à un seul.</summary>
    public static string NormaliserEmpreinte(string? saisie) =>
        string.Join(' ', (saisie ?? "").Trim().ToLowerInvariant().Split([' ', '\t', '-', '·'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Liste de 256 mots du contrat, identique des deux côtés (index 0 à 255).</summary>
    public static readonly IReadOnlyList<string> Mots = ("abbey acorn adobe agate alder alloy amber anchor anvil apron arbor arch arrow aspen atlas attic "
        + "badge bagel baker bamboo banjo barley basil beacon bear beech bell berry birch bison blaze bloom "
        + "board bonsai boot brass bread brick brook broom bucket bugle cabin cable cactus camel candle canoe "
        + "canyon cargo carpet castle cedar cello chalk charm cherry chess cider cinder citrus clay cliff clock "
        + "clover cobalt cocoa comet copper coral cotton crane crater crayon creek crown crystal cumin dahlia "
        + "daisy delta desert dingo dolphin dome dove dragon drum dune eagle easel echo eclipse elbow ember "
        + "emerald engine fable falcon fennel fern ferry fiddle fig flame flask flint flute forest fossil "
        + "fox frost gable garnet geyser ginger glacier globe goose granite grape gravel guitar gull hammer "
        + "harbor harp hazel heron hickory hill honey horizon husky igloo indigo iris island ivory jade jasmine "
        + "jelly jet juniper kayak kelp kettle kiwi koala lagoon lamp lantern larch lark lava lemon lilac "
        + "lily linen lion llama lotus lumen lynx magnet mango maple marble meadow melon mesa meteor mint "
        + "mirror mocha moose moss muffin nectar needle nest nickel nimbus nova nutmeg oak oasis ocean olive "
        + "onyx opal orbit orchid otter oyster paddle palm panda paper parrot pebble pecan pepper pine pixel "
        + "plum polar poppy prairie prism pumpkin quartz quill rabbit radar raven reef ribbon river robin "
        + "rocket rose ruby saddle saffron sage salmon sand satin sequoia shell sierra silver sparrow spruce "
        + "summit swan tango thistle thunder tiger timber topaz torch tulip tundra velvet violet walnut willow "
        + "wren yarrow yew zebra zenith zinc").Split(' ');

    // ---------- JSON canonique et MAC d'appairage ----------

    /// <summary>
    /// JSON canonique (sous-ensemble de RFC 8785) : clés triées par unités UTF-16, aucun espace, chaînes échappées comme
    /// JSON.stringify (<c>\"</c>, <c>\\</c>, <c>\b \f \n \r \t</c>, autres caractères de contrôle en <c>\u00xx</c>, le reste tel
    /// quel). Valeurs admises : chaînes, objets, tableaux (ordre gardé), booléens et null.
    /// </summary>
    public static string JsonCanonique(JsonNode? n)
    {
        var sb = new StringBuilder();
        Ecrire(sb, n);
        return sb.ToString();
    }

    private static void Ecrire(StringBuilder sb, JsonNode? n)
    {
        switch (n)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject o:
                sb.Append('{');
                var premier = true;
                foreach (var (cle, valeur) in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!premier) sb.Append(',');
                    premier = false;
                    sb.Append(Chaine(cle)).Append(':');
                    Ecrire(sb, valeur);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                for (var i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Ecrire(sb, a[i]);
                }
                sb.Append(']');
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                sb.Append(Chaine(v.GetValue<string>()));
                break;
            case JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                sb.Append(v.GetValue<bool>() ? "true" : "false");
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Null:
                sb.Append("null");
                break;
            default:
                throw new ArgumentException("JSON canonique : seules les chaînes, objets, tableaux, booléens et null sont admis.");
        }
    }

    private static string Chaine(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Code d'appairage normalisé : majuscules, sans tirets ni espaces.</summary>
    public static string NormaliserCode(string code) =>
        new([.. code.ToUpperInvariant().Where(c => c != '-' && !char.IsWhiteSpace(c))]);

    /// <summary>HMAC-SHA256 (base64url) du JSON canonique, avec pour clé l'UTF-8 du code normalisé.</summary>
    public static string Mac(string code, JsonNode objet) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(NormaliserCode(code)), Encoding.UTF8.GetBytes(JsonCanonique(objet))));

    public static bool MacValide(string code, JsonNode objet, string? mac)
    {
        if (mac is null) return false;
        byte[] recu;
        try { recu = DepuisBase64Url(mac); }
        catch (FormatException) { return false; }
        var attendu = HMACSHA256.HashData(Encoding.UTF8.GetBytes(NormaliserCode(code)), Encoding.UTF8.GetBytes(JsonCanonique(objet)));
        return CryptographicOperations.FixedTimeEquals(recu, attendu);
    }

    // ---------- Content-Digest (RFC 9530) ----------

    public static string Digest(ReadOnlySpan<byte> corps) => $"sha-256=:{Convert.ToBase64String(SHA256.HashData(corps))}:";

    /// <summary>Vrai si l'en-tête contient une valeur sha-256 égale à celle du corps (les autres algorithmes sont ignorés).</summary>
    public static bool DigestValide(string? entete, ReadOnlySpan<byte> corps)
    {
        if (string.IsNullOrWhiteSpace(entete)) return false;
        var attendu = SHA256.HashData(corps);
        foreach (var membre in entete.Split(','))
        {
            var m = membre.Trim();
            if (!m.StartsWith("sha-256=:", StringComparison.OrdinalIgnoreCase) || !m.EndsWith(':')) continue;
            try
            {
                if (CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(m[9..^1]), attendu)) return true;
            }
            catch (FormatException) { return false; }
        }
        return false;
    }

    // ---------- Signatures HTTP (RFC 9421) ----------

    /// <summary>
    /// Paramètres de signature (liste interne) : <c>("c1" "c2");created=…;nonce="…";keyid="…";alg="ecdsa-p256-sha256";tag="…"</c>,
    /// dans cet ordre.
    /// </summary>
    public static string ParametresSignature(IEnumerable<string> composants, long cree, string nonce, string kid, string tag) =>
        $"({string.Join(' ', composants.Select(c => $"\"{c}\""))});created={cree.ToString(CultureInfo.InvariantCulture)};nonce=\"{nonce}\";keyid=\"{kid}\";alg=\"{AlgSignature}\";tag=\"{tag}\"";

    /// <summary>
    /// Base de signature (RFC 9421 §2.5) : une ligne <c>"nom": valeur</c> par composant, dans l'ordre, puis la ligne
    /// <c>"@signature-params"</c> ; lignes séparées par LF, sans LF final.
    /// </summary>
    public static string BaseSignature(IEnumerable<(string Nom, string Valeur)> composants, string parametres)
    {
        var sb = new StringBuilder();
        foreach (var (nom, valeur) in composants) sb.Append('"').Append(nom).Append("\": ").Append(valeur).Append('\n');
        return sb.Append("\"@signature-params\": ").Append(parametres).ToString();
    }

    /// <summary>Signature ECDSA P-256 SHA-256 de la base, 64 octets r||s.</summary>
    public static byte[] Signer(ECDsa cle, string baseSignature) =>
        cle.SignData(Encoding.ASCII.GetBytes(baseSignature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool VerifierSignature(ECDsa cle, string baseSignature, byte[] signature) =>
        signature.Length == 64 && cle.VerifyData(Encoding.ASCII.GetBytes(baseSignature), signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>Valeur de l'en-tête <c>Signature-Input</c> pour le libellé <see cref="LibelleSignature"/>.</summary>
    public static string EnteteSignatureInput(string parametres, string libelle = LibelleSignature) => $"{libelle}={parametres}";

    public static string EnteteSignature(byte[] signature, string libelle = LibelleSignature) => $"{libelle}=:{Convert.ToBase64String(signature)}:";

    /// <summary>
    /// Lit <c>Signature-Input</c> et <c>Signature</c> (dictionnaires de champs structurés, RFC 8941) pour un libellé (par
    /// défaut le premier). Null si les en-têtes sont absents ou mal formés.
    /// </summary>
    public static SignatureLue? LireSignature(string? signatureInput, string? signature, string? libelle = null)
    {
        if (string.IsNullOrWhiteSpace(signatureInput) || string.IsNullOrWhiteSpace(signature)) return null;
        var entrees = MembresDictionnaire(signatureInput);
        var valeurs = MembresDictionnaire(signature);
        var (lib, parametres) = libelle is null ? entrees.FirstOrDefault() : entrees.FirstOrDefault(e => e.Cle == libelle);
        if (lib is null) return null;
        var sig = valeurs.FirstOrDefault(v => v.Cle == lib).Valeur;
        if (sig is null || sig.Length < 2 || sig[0] != ':' || sig[^1] != ':') return null;
        byte[] octets;
        try { octets = Convert.FromBase64String(sig[1..^1]); }
        catch (FormatException) { return null; }
        if (parametres.Length == 0 || parametres[0] != '(') return null;
        var fin = parametres.IndexOf(')', StringComparison.Ordinal);
        if (fin < 0) return null;
        var composants = new List<string>();
        foreach (var c in parametres[1..fin].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (c.Length < 2 || c[0] != '"' || c[^1] != '"') return null;
            composants.Add(c[1..^1]);
        }
        var p = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var morceau in Decouper(parametres[(fin + 1)..], ';'))
        {
            var t = morceau.Trim();
            if (t.Length == 0) continue;
            var egal = t.IndexOf('=', StringComparison.Ordinal);
            if (egal < 0) { p[t] = "?1"; continue; }
            var v = t[(egal + 1)..];
            p[t[..egal]] = v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;
        }
        long? cree = p.TryGetValue("created", out var cr) && long.TryParse(cr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        return new SignatureLue(lib, composants, parametres, cree, p.GetValueOrDefault("nonce"), p.GetValueOrDefault("keyid"),
            p.GetValueOrDefault("alg"), p.GetValueOrDefault("tag"), octets);
    }

    /// <summary>Membres « cle=valeur » d'un dictionnaire de champs structurés (virgules hors guillemets et parenthèses).</summary>
    private static List<(string? Cle, string Valeur)> MembresDictionnaire(string entete) =>
        Decouper(entete, ',').Select(m => m.Trim()).Where(m => m.Length > 0)
            .Select(m => m.IndexOf('=', StringComparison.Ordinal) is var i and > 0 ? ((string?)m[..i].Trim(), m[(i + 1)..].Trim()) : ((string?)null, m))
            .Where(x => x.Item1 is not null).ToList();

    private static IEnumerable<string> Decouper(string texte, char separateur)
    {
        var sb = new StringBuilder();
        bool guillemets = false;
        var profondeur = 0;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (guillemets)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < texte.Length) sb.Append(texte[++i]);
                else if (c == '"') guillemets = false;
                continue;
            }
            if (c == '"') guillemets = true;
            else if (c == '(') profondeur++;
            else if (c == ')') profondeur--;
            else if (c == separateur && profondeur == 0)
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        yield return sb.ToString();
    }

    // ---------- JWE compact ECDH-ES + A256GCM ----------

    /// <summary>
    /// Concat KDF (RFC 7518 §4.6.2, NIST SP 800-56A), SHA-256 : <c>keydatalen</c> bits dérivés du secret partagé Z, avec
    /// AlgorithmID = <paramref name="algorithme"/>, PartyUInfo, PartyVInfo (vides par défaut) et SuppPubInfo = keydatalen.
    /// </summary>
    public static byte[] ConcatKdf(byte[] z, string algorithme, int bits, byte[]? apu = null, byte[]? apv = null)
    {
        var autres = new List<byte>();
        void AvecLongueur(byte[] d)
        {
            Span<byte> l = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(l, d.Length);
            autres.AddRange(l.ToArray());
            autres.AddRange(d);
        }
        AvecLongueur(Encoding.ASCII.GetBytes(algorithme));
        AvecLongueur(apu ?? []);
        AvecLongueur(apv ?? []);
        var supp = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(supp, bits);
        autres.AddRange(supp);
        var octets = bits / 8;
        var resultat = new byte[octets];
        var fait = 0;
        for (var compteur = 1; fait < octets; compteur++)
        {
            var entree = new byte[4 + z.Length + autres.Count];
            BinaryPrimitives.WriteInt32BigEndian(entree, compteur);
            z.CopyTo(entree, 4);
            autres.CopyTo(entree, 4 + z.Length);
            var bloc = SHA256.HashData(entree);
            var n = Math.Min(bloc.Length, octets - fait);
            Array.Copy(bloc, 0, resultat, fait, n);
            fait += n;
        }
        return resultat;
    }

    /// <summary>Secret partagé Z (abscisse du point ECDH, 32 octets).</summary>
    public static byte[] SecretPartage(ECDiffieHellman privee, ECDiffieHellman publique) =>
        privee.DeriveRawSecretAgreement(publique.PublicKey);

    /// <summary>Chiffre <paramref name="contenu"/> (JSON UTF-8) pour la clé de chiffrement du destinataire : JWE compact.</summary>
    public static string Chiffrer(byte[] contenu, JwkPublique destinataire)
    {
        using var pub = ClePubliqueChiffrement(destinataire);
        using var ephemere = NouvelleCleChiffrement();
        var entete = new JsonObject
        {
            ["alg"] = "ECDH-ES", ["enc"] = "A256GCM", ["kid"] = Kid(destinataire), ["epk"] = Jwk(ephemere).VersJson(), ["cty"] = "application/json",
        };
        var enteteB64 = Base64Url(Encoding.UTF8.GetBytes(entete.ToJsonString()));
        var cle = ConcatKdf(SecretPartage(ephemere, pub), "A256GCM", 256);
        var iv = RandomNumberGenerator.GetBytes(12);
        var chiffre = new byte[contenu.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cle, 16)) gcm.Encrypt(iv, contenu, chiffre, tag, Encoding.ASCII.GetBytes(enteteB64));
        CryptographicOperations.ZeroMemory(cle);
        return $"{enteteB64}..{Base64Url(iv)}.{Base64Url(chiffre)}.{Base64Url(tag)}";
    }

    /// <summary>Identifiant (<c>kid</c>) de la clé destinataire d'un JWE compact, ou null s'il est illisible.</summary>
    public static string? KidDestinataire(string jwe)
    {
        try
        {
            var parties = jwe.Trim().Split('.');
            return parties.Length == 5 ? JsonNode.Parse(DepuisBase64Url(parties[0]))?["kid"]?.GetValue<string>() : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Déchiffre un JWE compact ECDH-ES / A256GCM avec la clé privée de chiffrement ; lève CryptographicException sinon.</summary>
    public static byte[] Dechiffrer(string jwe, ECDiffieHellman privee)
    {
        var parties = jwe.Trim().Split('.');
        if (parties.Length != 5 || parties[1].Length != 0) throw new CryptographicException("JWE compact à clé directe attendu.");
        JsonNode? entete;
        try { entete = JsonNode.Parse(DepuisBase64Url(parties[0])); }
        catch (Exception e) when (e is FormatException or JsonException) { throw new CryptographicException("En-tête JWE illisible.", e); }
        if (entete?["alg"]?.GetValue<string>() != "ECDH-ES" || entete["enc"]?.GetValue<string>() != "A256GCM")
            throw new CryptographicException("JWE ECDH-ES / A256GCM attendu.");
        var epk = JwkPublique.DepuisJson(entete["epk"]) ?? throw new CryptographicException("Clé éphémère absente.");
        byte[] iv, chiffre, tag;
        try
        {
            iv = DepuisBase64Url(parties[2]);
            chiffre = DepuisBase64Url(parties[3]);
            tag = DepuisBase64Url(parties[4]);
        }
        catch (FormatException e) { throw new CryptographicException("JWE mal encodé.", e); }
        if (iv.Length != 12 || tag.Length != 16) throw new CryptographicException("IV ou étiquette GCM de taille inattendue.");
        using var pub = ClePubliqueChiffrement(epk);
        var cle = ConcatKdf(SecretPartage(privee, pub), "A256GCM", 256);
        var clair = new byte[chiffre.Length];
        try
        {
            using var gcm = new AesGcm(cle, 16);
            gcm.Decrypt(iv, chiffre, tag, clair, Encoding.ASCII.GetBytes(parties[0]));
        }
        finally { CryptographicOperations.ZeroMemory(cle); }
        return clair;
    }
}
