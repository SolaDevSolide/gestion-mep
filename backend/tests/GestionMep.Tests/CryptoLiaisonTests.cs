using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Cryptographie de la liaison avec Noosphere : vecteurs des RFC, JWE, empreinte, JSON canonique et MAC d'appairage.</summary>
public class CryptoLiaisonTests
{
    /// <summary>RFC 9421, annexe B.2.4 : réponse signée en ecdsa-p256-sha256 avec test-key-ecc-p256 (vérification seule).</summary>
    [Fact]
    public void Rfc9421_B24_verification()
    {
        // Clé publique de test de l'annexe B.1.3 (JWK), assemblée à l'exécution.
        var jwk = new JwkPublique("EC", "P-256", "qIVYZVLCrPZHGHjP17CTW0_-" + "D9Lfw0EkjqF7xB4FivA", "Mc4nN9LTDOBhfoUeg8Ye9Wed" + "FRhnZXZJA12Qp0zZ6F0");
        var signatureInput = "sig-b24=(\"@status\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-ecc-p256\"";
        var signature = "sig-b24=:wNmSUAhwb5LxtOtOpNa6W5xj067m5hFrj0XQ4fvpaCLx0NK" + "ocgPquLgyahnzDnDAUy5eCdlYUEkLIj+32oiasw==:";
        var lue = CryptoLiaison.LireSignature(signatureInput, signature);
        Assert.NotNull(lue);
        Assert.Equal("sig-b24", lue.Libelle);
        Assert.Equal(["@status", "content-type", "content-digest", "content-length"], lue.Composants);
        Assert.Equal(1618884473, lue.Cree);
        Assert.Equal("test-key-ecc-p256", lue.Kid);
        var valeurs = new Dictionary<string, string>
        {
            ["@status"] = "200",
            ["content-type"] = "application/json",
            ["content-digest"] = "sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:",
            ["content-length"] = "23",
        };
        var baseSignature = CryptoLiaison.BaseSignature(lue.Composants.Select(c => (c, valeurs[c])), lue.Parametres);
        Assert.Equal("\"@status\": 200\n\"content-type\": application/json\n"
            + "\"content-digest\": sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:\n"
            + "\"content-length\": 23\n"
            + "\"@signature-params\": (\"@status\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-ecc-p256\"",
            baseSignature);
        using var cle = CryptoLiaison.CleVerification(jwk);
        Assert.True(CryptoLiaison.VerifierSignature(cle, baseSignature, lue.Valeur));
        // Un octet changé dans la base : refusée.
        Assert.False(CryptoLiaison.VerifierSignature(cle, baseSignature.Replace("200", "201", StringComparison.Ordinal), lue.Valeur));
    }

    /// <summary>RFC 7518, annexe C : accord de clé ECDH-ES (A128GCM, 128 bits, apu « Alice », apv « Bob »).</summary>
    [Fact]
    public void Rfc7518_annexe_C_accord_de_cle()
    {
        // Clés de l'exemple de la RFC, assemblées à l'exécution.
        var alice = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = CryptoLiaison.DepuisBase64Url("gI0GAILBdu7T53akrFmMyGcsF3n5dO7M" + "mwNBHKW5SV0"),
                Y = CryptoLiaison.DepuisBase64Url("SLW_xSffzlPWrHEVI30DHM_4egVwt3NQ" + "qeUD7nMFpps"),
            },
            D = CryptoLiaison.DepuisBase64Url("0_NxaRPUMQoAJt50Gz8YiTr8gRTw" + "yEaCumd-MToTmIo"),
        };
        var bob = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = CryptoLiaison.DepuisBase64Url("weNJy2HscCSM6AEDTDg04biOvhFhyyWv" + "OHQfeF_PxMQ"),
                Y = CryptoLiaison.DepuisBase64Url("e8lnCO-AlStT-NJVX-crhB7QRYhiix03" + "illJOVAOyck"),
            },
            D = CryptoLiaison.DepuisBase64Url("VEmDZpDXXK8p8N0Cndsxs924q6nS" + "1RXFASRl6BfUqdw"),
        };
        using var a = ECDiffieHellman.Create(alice);
        using var b = ECDiffieHellman.Create(bob);
        using var bPublique = CryptoLiaison.ClePubliqueChiffrement(CryptoLiaison.Jwk(bob));
        using var aPublique = CryptoLiaison.ClePubliqueChiffrement(CryptoLiaison.Jwk(alice));
        var z = CryptoLiaison.SecretPartage(a, bPublique);
        Assert.Equal(z, CryptoLiaison.SecretPartage(b, aPublique));
        var cle = CryptoLiaison.ConcatKdf(z, "A128GCM", 128, Encoding.ASCII.GetBytes("Alice"), Encoding.ASCII.GetBytes("Bob"));
        Assert.Equal("VqqN6vgjbSBcIijNcacQGg", CryptoLiaison.Base64Url(cle));
    }

    [Fact]
    public void Jwe_aller_retour()
    {
        using var destinataire = CryptoLiaison.NouvelleCleChiffrement();
        var jwk = CryptoLiaison.Jwk(destinataire);
        var clair = Encoding.UTF8.GetBytes("{\"changes\":[{\"kind\":\"application\",\"id\":\"billing\",\"operation\":\"upsert\",\"at\":\"2026-11-12T20:00:00Z\"}],\"é\":\"ü\"}");
        var jwe = CryptoLiaison.Chiffrer(clair, jwk);
        var parties = jwe.Split('.');
        Assert.Equal(5, parties.Length);
        Assert.Empty(parties[1]);
        var entete = JsonNode.Parse(CryptoLiaison.DepuisBase64Url(parties[0]))!;
        Assert.Equal("ECDH-ES", entete["alg"]!.GetValue<string>());
        Assert.Equal("A256GCM", entete["enc"]!.GetValue<string>());
        Assert.Equal("application/json", entete["cty"]!.GetValue<string>());
        Assert.Equal(CryptoLiaison.Kid(jwk), entete["kid"]!.GetValue<string>());
        Assert.Equal(CryptoLiaison.Kid(jwk), CryptoLiaison.KidDestinataire(jwe));
        Assert.Equal(clair, CryptoLiaison.Dechiffrer(jwe, destinataire));

        // Autre clé, en-tête modifié (AAD) ou chiffré altéré : refusé.
        using var autre = CryptoLiaison.NouvelleCleChiffrement();
        Assert.ThrowsAny<CryptographicException>(() => CryptoLiaison.Dechiffrer(jwe, autre));
        var enteteModifie = CryptoLiaison.Base64Url(Encoding.UTF8.GetBytes(entete.ToJsonString().Replace("application/json", "text/plain", StringComparison.Ordinal)));
        Assert.ThrowsAny<CryptographicException>(() => CryptoLiaison.Dechiffrer(enteteModifie + jwe[parties[0].Length..], destinataire));
        var altere = parties[3].Length > 0 ? string.Join('.', parties[0], "", parties[2], (parties[3][0] == 'A' ? "B" : "A") + parties[3][1..], parties[4]) : jwe;
        Assert.ThrowsAny<CryptographicException>(() => CryptoLiaison.Dechiffrer(altere, destinataire));
    }

    [Fact]
    public void Empreinte_deterministe_et_independante_de_l_ordre()
    {
        using var s1 = CryptoLiaison.NouvelleCleSignature();
        using var c1 = CryptoLiaison.NouvelleCleChiffrement();
        using var s2 = CryptoLiaison.NouvelleCleSignature();
        using var c2 = CryptoLiaison.NouvelleCleChiffrement();
        JwkPublique[] cles = [CryptoLiaison.Jwk(s1), CryptoLiaison.Jwk(c1), CryptoLiaison.Jwk(s2), CryptoLiaison.Jwk(c2)];
        var e1 = CryptoLiaison.EmpreinteMots(cles);
        var e2 = CryptoLiaison.EmpreinteMots(cles.Reverse());
        Assert.Equal(e1, e2);
        Assert.Matches("^[a-z]+ [a-z]+ [0-9]{4} [a-z]+$", e1);
        Assert.Equal(256, CryptoLiaison.Mots.Count);
        Assert.Equal(256, CryptoLiaison.Mots.Distinct().Count());
        Assert.Equal("abbey", CryptoLiaison.Mots[0]);
        Assert.Equal("zinc", CryptoLiaison.Mots[255]);

        // Valeur figée : clés fixes, empreinte calculée à la main d'après le contrat.
        JwkPublique Fixe(byte graine) => new("EC", "P-256", CryptoLiaison.Base64Url(Enumerable.Repeat(graine, 32).ToArray()),
            CryptoLiaison.Base64Url(Enumerable.Repeat((byte)(graine + 1), 32).ToArray()));
        JwkPublique[] fixes = [Fixe(1), Fixe(3), Fixe(5), Fixe(7)];
        var empreintes = fixes.Select(CryptoLiaison.Empreinte7638).ToList();
        empreintes.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
        var h = SHA256.HashData(empreintes.SelectMany(x => x).ToArray());
        var attendu = $"{CryptoLiaison.Mots[h[0]]} {CryptoLiaison.Mots[h[1]]} {((h[2] << 8 | h[3]) % 10000):D4} {CryptoLiaison.Mots[h[4]]}";
        Assert.Equal(attendu, CryptoLiaison.EmpreinteMots(fixes));
        Assert.Equal(attendu, CryptoLiaison.EmpreinteMots([fixes[2], fixes[0], fixes[3], fixes[1]]));

        Assert.Equal("orbit cedar 0712 lumen", CryptoLiaison.NormaliserEmpreinte("  Orbit  CEDAR 0712\tlumen "));
    }

    [Fact]
    public void Empreinte_du_vecteur_commun_avec_Noosphere()
    {
        // Vecteur partagé avec Noosphere : clés publiques de RFC 9421 (test-key-ecc-p256), de RFC 7518 annexe C (Alice, Bob)
        // et de RFC 7515 annexe A.3. Les deux implémentations doivent trouver les mêmes kid et la même empreinte.
        JwkPublique[] cles =
        [
            new("EC", "P-256", "qIVYZVLCrPZHGHjP17CTW0_-D9Lfw0EkjqF7xB4FivA", "Mc4nN9LTDOBhfoUeg8Ye9WedFRhnZXZJA12Qp0zZ6F0"),
            new("EC", "P-256", "gI0GAILBdu7T53akrFmMyGcsF3n5dO7MmwNBHKW5SV0", "SLW_xSffzlPWrHEVI30DHM_4egVwt3NQqeUD7nMFpps"),
            new("EC", "P-256", "weNJy2HscCSM6AEDTDg04biOvhFhyyWvOHQfeF_PxMQ", "e8lnCO-AlStT-NJVX-crhB7QRYhiix03illJOVAOyck"),
            new("EC", "P-256", "f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU", "x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"),
        ];
        Assert.Equal(["ydQXMtvbsOsZyFir-Y7A8t7fKEM1gbKPvyFkdpu4fvI", "_GK0r6GCoJt9zcssg9lay4obIxgCq05ntiRymRHADSU",
            "Vy57XrArUrW0NbpI12tEzDHABxMwrTh6HHXRenSpnCo", "oKIywvGUpTVTyxMQ3bwIIeQUudfr_CkLMjCE19ECD-U"], cles.Select(CryptoLiaison.Kid));
        Assert.Equal("cedar anchor 8451 arbor", CryptoLiaison.EmpreinteMots(cles));
        Assert.Equal("cedar anchor 8451 arbor", CryptoLiaison.EmpreinteMots(cles.Reverse()));
    }

    [Fact]
    public void Empreinte_7638_du_rfc()
    {
        // RFC 7638 §3.1 décrit le calcul ; ici, le membre « kid » et l'ordre d'écriture n'ont pas d'effet.
        var jwk = new JwkPublique("EC", "P-256", "f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU", "x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0");
        var attendu = CryptoLiaison.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(
            "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU\",\"y\":\"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0\"}")));
        Assert.Equal(attendu, CryptoLiaison.Kid(jwk));
        Assert.Equal(43, CryptoLiaison.Kid(jwk).Length);
    }

    [Fact]
    public void Json_canonique_et_mac_d_appairage()
    {
        var objet = new JsonObject
        {
            ["peer"] = new JsonObject { ["url"] = "https://mep.example", ["product"] = "gestion-mep", ["instanceName"] = "MEP \"Alizé\"\n" },
            ["nonce"] = "abc",
            ["codeId"] = "7K4Q",
            ["capabilities"] = new JsonArray("catalog:read", "notices:send"),
        };
        Assert.Equal("{\"capabilities\":[\"catalog:read\",\"notices:send\"],\"codeId\":\"7K4Q\",\"nonce\":\"abc\","
            + "\"peer\":{\"instanceName\":\"MEP \\\"Alizé\\\"\\n\",\"product\":\"gestion-mep\",\"url\":\"https://mep.example\"}}", CryptoLiaison.JsonCanonique(objet));
        Assert.Equal("{\"a\":\"\\u0001\\t/\"}", CryptoLiaison.JsonCanonique(new JsonObject { ["a"] = "\u0001\t/" }));

        Assert.Equal("7K4Q29XDM3PA", CryptoLiaison.NormaliserCode("7k4q-29xd m3pa"));
        var mac = CryptoLiaison.Mac("7k4q-29xd-m3pa", objet);
        var attendu = CryptoLiaison.Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes("7K4Q29XDM3PA"),
            Encoding.UTF8.GetBytes(CryptoLiaison.JsonCanonique(objet))));
        Assert.Equal(attendu, mac);
        Assert.True(CryptoLiaison.MacValide("7K4Q 29XD M3PA", objet, mac));
        Assert.False(CryptoLiaison.MacValide("7K4Q-29XD-M3PB", objet, mac));
        Assert.False(CryptoLiaison.MacValide("7K4Q-29XD-M3PA", objet, "pas-un-mac!"));
        // L'ordre des clés ne change pas le MAC.
        var reordonne = new JsonObject
        {
            ["codeId"] = "7K4Q", ["capabilities"] = new JsonArray("catalog:read", "notices:send"), ["nonce"] = "abc",
            ["peer"] = new JsonObject { ["instanceName"] = "MEP \"Alizé\"\n", ["product"] = "gestion-mep", ["url"] = "https://mep.example" },
        };
        Assert.True(CryptoLiaison.MacValide("7K4Q29XDM3PA", reordonne, mac));
    }

    [Fact]
    public void Signature_requete_aller_retour_et_digest()
    {
        using var cle = CryptoLiaison.NouvelleCleSignature();
        var corps = Encoding.ASCII.GetBytes("eyJh..x.y.z");
        var digest = CryptoLiaison.Digest(corps);
        Assert.StartsWith("sha-256=:", digest, StringComparison.Ordinal);
        Assert.True(CryptoLiaison.DigestValide(digest, corps));
        Assert.True(CryptoLiaison.DigestValide("sha-512=:AAAA:, " + digest, corps));
        Assert.False(CryptoLiaison.DigestValide(digest, Encoding.ASCII.GetBytes("autre")));
        Assert.False(CryptoLiaison.DigestValide(null, corps));

        var kid = CryptoLiaison.Kid(CryptoLiaison.Jwk(cle));
        var parametres = CryptoLiaison.ParametresSignature(["@method", "@path", "@query", "content-digest"], 1_793_000_000, "bm9uY2U", kid, CryptoLiaison.TagRequete);
        Assert.Equal($"(\"@method\" \"@path\" \"@query\" \"content-digest\");created=1793000000;nonce=\"bm9uY2U\";keyid=\"{kid}\";alg=\"ecdsa-p256-sha256\";tag=\"noosphere-link\"", parametres);
        var baseSig = CryptoLiaison.BaseSignature([("@method", "POST"), ("@path", "/link/v1/notices"), ("@query", "?"), ("content-digest", digest)], parametres);
        var sig = CryptoLiaison.Signer(cle, baseSig);
        Assert.Equal(64, sig.Length);
        var lue = CryptoLiaison.LireSignature(CryptoLiaison.EnteteSignatureInput(parametres), CryptoLiaison.EnteteSignature(sig))!;
        Assert.Equal("link", lue.Libelle);
        Assert.Equal(parametres, lue.Parametres);
        Assert.Equal("bm9uY2U", lue.Nonce);
        Assert.Equal(kid, lue.Kid);
        Assert.Equal(CryptoLiaison.AlgSignature, lue.Alg);
        Assert.Equal(CryptoLiaison.TagRequete, lue.Tag);
        using var verif = CryptoLiaison.CleVerification(CryptoLiaison.Jwk(cle));
        Assert.True(CryptoLiaison.VerifierSignature(verif, baseSig, lue.Valeur));
        Assert.Null(CryptoLiaison.LireSignature("link=(\"@method\";created=1", "link=:AA==:"));
        Assert.Null(CryptoLiaison.LireSignature(null, null));
    }

    [Fact]
    public void Cle_publique_hors_courbe_refusee()
    {
        Assert.ThrowsAny<CryptographicException>(() => CryptoLiaison.CleVerification(new JwkPublique("RSA", "P-256", "AA", "AA")));
        Assert.ThrowsAny<CryptographicException>(() => CryptoLiaison.CleVerification(
            new JwkPublique("EC", "P-256", CryptoLiaison.Base64Url(new byte[32]), CryptoLiaison.Base64Url(new byte[32]))));
    }
}
