using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Requête signée reçue de Noosphere, telle que vue par le serveur.</summary>
/// <param name="Chemins">Valeurs possibles de « @path » (avec la base de l'application, puis avec le chemin de l'adresse publique).</param>
public sealed record RequeteLiaison(string Methode, IReadOnlyList<string> Chemins, string Requete, string? SignatureInput, string? Signature,
    string? ContentDigest, byte[] Corps);

/// <summary>
/// Liaison avec Noosphere (#123). Protocole (<c>/link/v1</c>, hors <c>/api</c> : ni cookie ni CSRF, 120 requêtes par minute et
/// par liaison) : <c>GET /status</c>, <c>POST /notices</c>, <c>POST /rekey</c>, <c>POST /unpair</c>, chaque requête signée
/// (RFC 9421) et vérifiée, chaque réponse signée. Console (administrateurs) : <c>/api/admin/liaisons</c>.
/// </summary>
public static class LiaisonEndpoints
{
    public const string PolitiqueDebit = "liaison";
    public const int RequetesParMinute = 120;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Vérification des requêtes reçues ----------

    /// <summary>
    /// Contrôles qui ne demandent ni base ni état : horloge (300 s), Content-Digest, composants couverts, étiquette, algorithme,
    /// nonce présent et signature. Renvoie la raison du refus (<see cref="RefusLiaison"/>), ou null.
    /// </summary>
    public static string? Controler(RequeteLiaison r, SignatureLue sig, JwkPublique cle, DateTimeOffset maintenant)
    {
        if (sig.Cree is not { } cree || Math.Abs(maintenant.ToUnixTimeSeconds() - cree) > (long)ServiceLiaisons.EcartHorloge.TotalSeconds)
            return RefusLiaison.Horloge;
        if (r.Corps.Length > 0 && (!sig.Composants.Contains("content-digest") || !CryptoLiaison.DigestValide(r.ContentDigest, r.Corps)))
            return RefusLiaison.Empreinte;
        if (r.Corps.Length == 0 && r.ContentDigest is { Length: > 0 } && !CryptoLiaison.DigestValide(r.ContentDigest, r.Corps))
            return RefusLiaison.Empreinte;
        string[] attendus = r.Corps.Length > 0 || sig.Composants.Contains("content-digest")
            ? ["@method", "@path", "@query", "content-digest"] : ["@method", "@path", "@query"];
        if (!sig.Composants.SequenceEqual(attendus) || sig.Tag != CryptoLiaison.TagRequete || sig.Alg is not (null or CryptoLiaison.AlgSignature)
            || string.IsNullOrEmpty(sig.Nonce))
            return RefusLiaison.Signature;
        using var verif = CryptoLiaison.CleVerification(cle);
        foreach (var chemin in r.Chemins.Distinct())
        {
            var valeurs = new List<(string, string)> { ("@method", r.Methode.ToUpperInvariant()), ("@path", chemin), ("@query", r.Requete.Length == 0 ? "?" : r.Requete) };
            if (attendus.Length == 4) valeurs.Add(("content-digest", (r.ContentDigest ?? "").Trim()));
            if (CryptoLiaison.VerifierSignature(verif, CryptoLiaison.BaseSignature(valeurs, sig.Parametres), sig.Valeur)) return null;
        }
        return RefusLiaison.Signature;
    }

    private static RequeteLiaison Lire(HttpContext ctx, byte[] corps, IConfiguration config)
    {
        var req = ctx.Request;
        // @path signé par Noosphere : le chemin de l'adresse qu'il appelle, préfixe compris. Accepté : préfixe de l'adresse
        // publique + Path (reverse proxy qui retire le préfixe sans le dire), PathBase + Path, ou Path seul.
        var chemin = req.Path.ToUriComponent();
        var chemins = new List<string>();
        if (Uri.TryCreate(config["Application:UrlPublique"], UriKind.Absolute, out var pub) && pub.AbsolutePath.TrimEnd('/') is { Length: > 0 } prefixe)
            chemins.Add(prefixe + chemin);
        chemins.Add(req.PathBase.ToUriComponent() + chemin);
        chemins.Add(chemin);
        string? Entete(string nom) => req.Headers.TryGetValue(nom, out var v) && v.Count > 0 ? string.Join(", ", v.ToArray()) : null;
        return new RequeteLiaison(req.Method, chemins, req.QueryString.Value ?? "", Entete("Signature-Input"), Entete("Signature"), Entete("Content-Digest"), corps);
    }

    private static async Task<byte[]> LireCorps(HttpContext ctx)
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        return ms.ToArray();
    }

    /// <summary>Statut HTTP d'un refus : 401, 403 (link-not-confirmed, capability-missing), 400 (bad-body).</summary>
    public static int StatutRefus(string raison) => raison switch
    {
        RefusLiaison.NonConfirmee or RefusLiaison.CapaciteManquante => 403,
        RefusLiaison.CorpsInvalide => 400,
        _ => 401,
    };

    /// <summary>
    /// Réponse de refus : problem+json <c>https://noosphere.dev/problems/&lt;raison&gt;</c>. Signée quand la liaison est
    /// identifiée et a encore ses clés (tous les refus sauf unknown-key et link-cut).
    /// </summary>
    private static async Task Refuser(HttpContext ctx, ServiceLiaisons svc, Liaison? l, string raison, string? detail = null)
    {
        var statut = StatutRefus(raison);
        var corps = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = RefusLiaison.Type(raison), title = statut == 403 ? "Refusé" : statut == 400 ? "Corps invalide" : "Requête refusée", status = statut,
            detail = detail ?? RefusLiaison.Libelle(raison),
        });
        if (l?.Cles is { } cles && raison is not (RefusLiaison.CleInconnue or RefusLiaison.LiaisonCoupee))
        {
            using var cle = svc.CleSignature(cles);
            await Repondre(ctx, svc, cle, cles.Signature, statut, corps, ReponsesErreur.TypeContenu);
            return;
        }
        ctx.Response.StatusCode = statut;
        ctx.Response.ContentType = ReponsesErreur.TypeContenu;
        await ctx.Response.Body.WriteAsync(corps, ctx.RequestAborted);
    }

    /// <summary>Refus après vérification de la signature (bad-body, capability-missing…) : inscrit au journal, réponse signée.</summary>
    private static async Task Refuser(HttpContext ctx, MepDbContext db, ServiceLiaisons svc, Liaison l, string operation, string raison, string detail)
    {
        svc.Noter(l.Id, "entrant", operation, "refuse", StatutRefus(raison), $"{detail} ({raison})");
        await db.SaveChangesAsync();
        await Refuser(ctx, svc, l, raison, detail);
    }

    /// <summary>
    /// Vérifie une requête reçue : clé connue, liaison non coupée, signature (voir <see cref="Controler"/>), nonce jamais vu
    /// dans les 10 dernières minutes, liaison confirmée ici (sauf <paramref name="avantConfirmation"/> : /status et /unpair).
    /// Un refus est répondu et inscrit au journal ; renvoie la liaison sinon.
    /// </summary>
    private static async Task<Liaison?> Verifier(HttpContext ctx, string operation, byte[] corps, MepDbContext db, ServiceLiaisons svc, IConfiguration config,
        bool avantConfirmation = false)
    {
        var r = Lire(ctx, corps, config);
        var sig = CryptoLiaison.LireSignature(r.SignatureInput, r.Signature, CryptoLiaison.LibelleSignature)
            ?? CryptoLiaison.LireSignature(r.SignatureInput, r.Signature);
        Liaison? l = null;
        string? raison;
        if (sig?.Kid is not { Length: > 0 } kid) raison = sig is null ? RefusLiaison.Signature : RefusLiaison.CleInconnue;
        else
        {
            l = await db.Liaisons.FirstOrDefaultAsync(x => x.KidPair == kid) ?? await db.Liaisons.FirstOrDefaultAsync(x => x.AncienKidPair == kid);
            var maintenant = svc.Maintenant;
            if (l is null) raison = RefusLiaison.CleInconnue;
            else if (l.Etat == EtatLiaison.Coupee) raison = RefusLiaison.LiaisonCoupee;
            else
            {
                var cle = l.KidPair == kid ? l.ClesPair?.Signature
                    : l.AnciennesClesPair is { } a && a.ValablesJusquA > maintenant ? a.Signature : null;
                raison = cle is null ? RefusLiaison.CleInconnue : Controler(r, sig, cle, maintenant);
                if (raison is null)
                {
                    var neuf = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""INSERT INTO "NoncesLiaison" ("Nonce", "VuLe") VALUES ({sig.Nonce}, {maintenant}) ON CONFLICT ("Nonce") DO NOTHING""");
                    if (neuf == 0) raison = RefusLiaison.Rejeu;
                    else if (!l.ConfirmeeIci && !avantConfirmation) raison = RefusLiaison.NonConfirmee;
                }
            }
        }
        if (raison is null) return l;
        svc.Noter(l?.Id, "entrant", operation, "refuse", StatutRefus(raison),
            $"{RefusLiaison.Libelle(raison)} ({raison}){(l is null && sig?.Kid is { } k ? $", clé {k[..Math.Min(12, k.Length)]}…" : "")}");
        await db.SaveChangesAsync();
        await Refuser(ctx, svc, l, raison);
        return null;
    }

    /// <summary>Signe la réponse (<c>("@status" "content-digest")</c>, étiquette noosphere-link-response) puis l'écrit.</summary>
    private static Task Repondre(HttpContext ctx, ServiceLiaisons svc, ECDsa cle, JwkPublique publique, int statut, string? jwe) =>
        Repondre(ctx, svc, cle, publique, statut, jwe is null ? [] : Encoding.ASCII.GetBytes(jwe), "application/jose");

    private static async Task Repondre(HttpContext ctx, ServiceLiaisons svc, ECDsa cle, JwkPublique publique, int statut, byte[] corps, string typeContenu)
    {
        var composants = new List<(string, string)> { ("@status", statut.ToString(CultureInfo.InvariantCulture)) };
        if (corps.Length > 0)
        {
            var digest = CryptoLiaison.Digest(corps);
            ctx.Response.Headers["Content-Digest"] = digest;
            composants.Add(("content-digest", digest));
        }
        var parametres = CryptoLiaison.ParametresSignature(composants.Select(c => c.Item1), svc.Maintenant.ToUnixTimeSeconds(), CryptoLiaison.Aleatoire(),
            CryptoLiaison.Kid(publique), CryptoLiaison.TagReponse);
        ctx.Response.Headers["Signature-Input"] = CryptoLiaison.EnteteSignatureInput(parametres);
        ctx.Response.Headers["Signature"] = CryptoLiaison.EnteteSignature(CryptoLiaison.Signer(cle, CryptoLiaison.BaseSignature(composants, parametres)));
        ctx.Response.StatusCode = statut;
        if (corps.Length > 0)
        {
            ctx.Response.ContentType = typeContenu;
            await ctx.Response.Body.WriteAsync(corps, ctx.RequestAborted);
        }
    }

    private static async Task Repondre(HttpContext ctx, ServiceLiaisons svc, Liaison l, int statut, string? jwe)
    {
        using var cle = svc.CleSignature(l.Cles!);
        await Repondre(ctx, svc, cle, l.Cles!.Signature, statut, jwe);
    }

    public static string Cle(HttpContext ctx)
    {
        var entree = ctx.Request.Headers["Signature-Input"].ToString();
        var i = entree.IndexOf("keyid=\"", StringComparison.Ordinal);
        if (i >= 0)
        {
            var fin = entree.IndexOf('"', i + 7);
            if (fin > i + 7) return "cle:" + entree[(i + 7)..Math.Min(fin, i + 7 + 64)];
        }
        return "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue");
    }

    public static void MapLiaisonProtocole(this WebApplication app)
    {
        var lien = app.MapGroup(ServiceLiaisons.Chemin).AllowAnonymous().RequireRateLimiting(PolitiqueDebit).ExcludeFromDescription();

        lien.MapGet("/status", async (HttpContext ctx, MepDbContext db, ServiceLiaisons svc, IConfiguration config) =>
        {
            var corps = await LireCorps(ctx);
            if (await Verifier(ctx, "status", corps, db, svc, config, avantConfirmation: true) is not { } l) return;
            var statut = new StatutLiaison(l.Id, ServiceLiaisons.Produit, svc.NomInstance, VersionApplication.Courante.Split('+')[0], l.ConfirmeeIci,
                [.. l.Capacites], l.ClesRenouveleesLe is { } d ? SynchroLiaison.DateContrat(d) : null);
            svc.Noter(l.Id, "entrant", "status", "ok", 200, "État demandé par Noosphere.");
            l.DernierEchangeLe = svc.Maintenant;
            await db.SaveChangesAsync();
            // Noosphere lit cet état juste après avoir confirmé l'empreinte de son côté : tant que sa confirmation n'est pas
            // connue ici, relire son état et le catalogue dans la foulée plutôt qu'au prochain passage (15 minutes).
            if (l.ConfirmeeIci && l.Etat == EtatLiaison.Active && l.ConfirmeeParPair != true)
                svc.Signal.DemanderSynchro(l.Id, svc.Maintenant.AddSeconds(2));
            await Repondre(ctx, svc, l, 200, svc.Chiffrer(l, statut));
        });

        lien.MapPost("/notices", async (HttpContext ctx, MepDbContext db, ServiceLiaisons svc, IConfiguration config) =>
        {
            var corps = await LireCorps(ctx);
            if (await Verifier(ctx, "notices", corps, db, svc, config) is not { } l) return;
            if (!l.Capacites.Contains(CapacitesLiaison.EnvoiAvis))
            {
                await Refuser(ctx, db, svc, l, "notices", RefusLiaison.CapaciteManquante, $"La liaison n'a pas la capacité {CapacitesLiaison.EnvoiAvis}.");
                return;
            }
            AvisNoosphere? avis;
            try { avis = JsonSerializer.Deserialize<AvisNoosphere>(svc.Dechiffrer(l, Encoding.ASCII.GetString(corps)), Json); }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { avis = null; }
            if (avis is null)
            {
                await Refuser(ctx, db, svc, l, "notices", RefusLiaison.CorpsInvalide, "Avis illisible : JWE (ECDH-ES, A256GCM) de {changes} attendu.");
                return;
            }
            var changements = avis.Changements ?? [];
            svc.Noter(l.Id, "entrant", "notices", "ok", 202, $"{changements.Count} changement(s) : "
                + string.Join(", ", changements.Take(10).Select(c => $"{c.Type} {c.Id} {c.Operation}")) + (changements.Count > 10 ? "…" : "")
                + " ; catalogue relu dans quelques secondes.");
            l.DernierEchangeLe = svc.Maintenant;
            await db.SaveChangesAsync();
            svc.Signal.DemanderSynchro(l.Id, svc.Maintenant.AddSeconds(2));
            await Repondre(ctx, svc, l, 202, null);
        });

        lien.MapPost("/rekey", async (HttpContext ctx, MepDbContext db, ServiceLiaisons svc, IConfiguration config) =>
        {
            var corps = await LireCorps(ctx);
            if (await Verifier(ctx, "rekey", corps, db, svc, config) is not { } l) return;
            if (CryptoLiaison.LireSignature(ctx.Request.Headers["Signature-Input"].ToString(), ctx.Request.Headers["Signature"].ToString())?.Kid != l.KidPair)
            {
                await Refuser(ctx, db, svc, l, "rekey", RefusLiaison.Signature, "Un renouvellement de clés se signe avec la clé de signature en cours.");
                return;
            }
            NouvellesCles? cles;
            try
            {
                cles = JsonSerializer.Deserialize<NouvellesCles>(svc.Dechiffrer(l, Encoding.ASCII.GetString(corps)), Json);
                if (cles is not null)
                {
                    CryptoLiaison.CleVerification(cles.Signature).Dispose();
                    CryptoLiaison.ClePubliqueChiffrement(cles.Chiffrement).Dispose();
                }
            }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { cles = null; }
            if (cles is null)
            {
                await Refuser(ctx, db, svc, l, "rekey", RefusLiaison.CorpsInvalide, "Nouvelles clés illisibles : JWE de {signingKey, encryptionKey}, deux clés publiques P-256, attendu.");
                return;
            }
            // Réponse signée avant le changement, puis bascule : l'ancienne clé de Noosphere reste acceptée 5 minutes.
            using var cle = svc.CleSignature(l.Cles!);
            var publique = l.Cles!.Signature;
            svc.AppliquerClesPair(l, cles);
            svc.Noter(l.Id, "entrant", "rekey", "ok", 200, $"Clés de Noosphere renouvelées ; nouvelle empreinte {l.Empreinte}.");
            l.DernierEchangeLe = svc.Maintenant;
            await db.SaveChangesAsync();
            await Repondre(ctx, svc, cle, publique, 200, null);
        });

        lien.MapPost("/unpair", async (HttpContext ctx, MepDbContext db, ServiceLiaisons svc, IConfiguration config) =>
        {
            var corps = await LireCorps(ctx);
            if (await Verifier(ctx, "unpair", corps, db, svc, config, avantConfirmation: true) is not { } l) return;
            using var cle = svc.CleSignature(l.Cles!);
            var publique = l.Cles!.Signature;
            await svc.MarquerCoupee(l, "Noosphere");
            svc.Noter(l.Id, "entrant", "unpair", "ok", 204, "Liaison coupée par Noosphere ; clés effacées, systèmes rendus locaux.");
            await db.SaveChangesAsync();
            await Repondre(ctx, svc, cle, publique, 204, null);
        });

        // Le reste de /link/v1 (dont /pair, servi par Noosphere seulement) n'existe pas ici.
        lien.Map("{**reste}", () => Results.Json(new { type = "about:blank", title = "Introuvable", status = 404, detail = "Point d'entrée inconnu." },
            contentType: ReponsesErreur.TypeContenu, statusCode: 404));
    }

    // ---------- Console d'administration ----------

    private const string Auteur = "Console";

    public static void MapLiaisons(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/liaisons").ExigerAdmin();

        admin.MapGet("", (ServiceLiaisons svc) => svc.Lister());

        admin.MapPost("/appairer", async (AppairageLiaisonSaisie s, ClaimsPrincipal user, ServiceLiaisons svc, HttpRequest req, CancellationToken ct) =>
        {
            var l = await svc.Appairer(s.Url, s.Code, Acteur.Depuis(user), Auteur, svc.UrlLocale(req), ct);
            return Results.Created($"/api/admin/liaisons/{l.Id}", await svc.Dto(l));
        });

        // Échanges de toutes les liaisons, y compris les requêtes refusées sans liaison connue.
        admin.MapGet("/echanges", (Guid? liaison, DateTimeOffset? avant, int? limite, bool? refus, ServiceLiaisons svc) =>
            svc.Echanges(liaison, avant, limite, refus == true));

        admin.MapGet("/{id:guid}", async (Guid id, ServiceLiaisons svc) => await svc.Dto(await svc.Charger(id)));

        admin.MapPost("/{id:guid}/confirmer", async (Guid id, ConfirmationLiaisonSaisie s, ClaimsPrincipal user, ServiceLiaisons svc) =>
            await svc.Dto(await svc.Confirmer(id, s.Empreinte, Acteur.Depuis(user), Auteur)));

        // État de la liaison chez Noosphere (GET /link/v1/status), relu à l'ouverture de l'onglet « Liaisons ».
        admin.MapPost("/{id:guid}/statut", async (Guid id, ServiceLiaisons svc, CancellationToken ct) =>
            await svc.Dto(await svc.RelireStatut(await svc.Charger(id), ct)));

        admin.MapGet("/{id:guid}/catalogue", async (Guid id, ServiceLiaisons svc, MepDbContext db) =>
            svc.Catalogue(await svc.Charger(id), await db.SystemesCatalogue.AsNoTracking().ToListAsync()));

        admin.MapPut("/{id:guid}/suivi", async (Guid id, SuiviLiaisonSaisie s, ClaimsPrincipal user, ServiceLiaisons svc, MepDbContext db) =>
        {
            var l = await svc.Charger(id);
            try { await svc.ChoisirSuivi(l, s, Acteur.Depuis(user), Auteur); }
            catch (RegleMetierException e) { throw ErreurHttp.Regle([e.Message]); }
            return svc.Catalogue(l, await db.SystemesCatalogue.AsNoTracking().ToListAsync());
        });

        admin.MapPut("/{id:guid}/options", async (Guid id, OptionsLiaisonSaisie s, ClaimsPrincipal user, ServiceLiaisons svc) =>
        {
            var l = await svc.Charger(id);
            await svc.Options(l, s, Acteur.Depuis(user), Auteur);
            return await svc.Dto(l);
        });

        admin.MapPost("/{id:guid}/synchroniser", async (Guid id, ClaimsPrincipal user, ServiceLiaisons svc, CancellationToken ct) =>
        {
            var l = await svc.Charger(id);
            var (bilan, inchange) = await svc.Synchroniser(l, Acteur.Depuis(user), Auteur, ct);
            svc.Signal.DemanderRapprochement();
            return Synchro(bilan, inchange, await svc.Dto(l));
        });

        admin.MapPost("/{id:guid}/renouveler", async (Guid id, ClaimsPrincipal user, ServiceLiaisons svc, CancellationToken ct) =>
        {
            var l = await svc.Charger(id);
            await svc.Renouveler(l, Acteur.Depuis(user), Auteur, ct);
            return await svc.Dto(l);
        });

        admin.MapPost("/{id:guid}/couper", async (Guid id, CoupureLiaisonSaisie s, ClaimsPrincipal user, ServiceLiaisons svc, CancellationToken ct) =>
        {
            if (s.Confirmer != true) throw ErreurHttp.Saisie("Confirmation attendue : { \"confirmer\": true }.");
            var l = await svc.Charger(id);
            await svc.Couper(l, Acteur.Depuis(user), Auteur, ct);
            return await svc.Dto(l);
        });

        admin.MapGet("/{id:guid}/echanges", async (Guid id, DateTimeOffset? avant, int? limite, ServiceLiaisons svc) =>
        {
            await svc.Charger(id);
            return await svc.Echanges(id, avant, limite);
        });

        // Marque « Envoyée à Noosphere » d'une MEP : lisible par tous ceux qui voient la MEP.
        api.MapGet("/meps/{id:guid}/noosphere", async (Guid id, MepDbContext db, ServiceLiaisons svc) =>
        {
            var mep = await db.Meps.AsNoTracking().Include(m => m.Systemes).AsSplitQuery().FirstOrDefaultAsync(m => m.Id == id)
                ?? throw ErreurHttp.NonTrouve("MEP introuvable.");
            return await svc.EnvoisMep(mep);
        });
    }

    public static SynchroLiaisonDto Synchro(BilanSynchro b, bool inchange, LiaisonDto l) =>
        new(inchange, inchange ? "Catalogue inchangé." : b.Resume(), b.Crees, b.Rattaches, b.MisAJour, b.Desactives, b.Reactives, b.Avertissements, l);
}
