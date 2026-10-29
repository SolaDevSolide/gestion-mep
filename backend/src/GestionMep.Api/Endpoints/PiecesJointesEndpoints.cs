using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Securite;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Pièces jointes d'une MEP, d'une étape, d'une exécution de résolution ou d'une résolution type.
/// Dépôt en multipart/form-data (champ <c>fichier</c>) ; le contenu est stocké sur disque sous l'Id de la pièce.
/// </summary>
public static class PiecesJointesEndpoints
{
    public const string ChampFichier = "fichier";

    /// <summary>
    /// Les routes de dépôt acceptent un corps de la taille d'une pièce jointe (plus 1 Mo de formulaire) ; toutes les autres
    /// sont limitées à <see cref="LimitesRequetes.CorpsParDefaut"/>.
    /// </summary>
    private static RouteHandlerBuilder AccepterFichier(this RouteHandlerBuilder route, long limite) =>
        route.WithMetadata(new RequestSizeLimitAttribute(limite));

    /// <summary>Enveloppe d'un formulaire de dépôt (délimiteurs, en-têtes de la partie, autres champs) : déduite de la taille annoncée.</summary>
    private const long MargeFormulaire = 64 * 1024;

    /// <summary>
    /// Reçoit le fichier du formulaire en flux, sans le mettre en tampon : la place est réservée d'après la taille annoncée
    /// (plafonds de <see cref="QuotasPiecesJointes"/>, refus immédiat si elle ne tient pas), puis le fichier est écrit sur disque
    /// au fil de la réception (taille et SHA-256 calculés au passage) ; la réception s'arrête dès que le fichier dépasse la
    /// taille maximale (413) ou la place restante (422, 429 ou 507). Le fichier reçu est ensuite compressé s'il y gagne assez.
    /// La réservation est à libérer une fois la pièce enregistrée ou abandonnée.
    /// </summary>
    private static async Task<(PieceJointe Piece, IDisposable Reservation)> Recevoir(HttpRequest req, StockagePiecesJointes stockage,
        QuotasPiecesJointes quotas, MepDbContext db, Guid? mepId, Acteur acteur, DateTimeOffset maintenant)
    {
        var ct = req.HttpContext.RequestAborted;
        var limite = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(req.ContentType, out var type)
            && type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(type.Boundary).Value : null;
        if (string.IsNullOrEmpty(limite)) throw ErreurHttp.Saisie($"Envoyer le fichier en multipart/form-data (champ « {ChampFichier} »).");
        // Taille maximale en cours (réglage modifiable sans redémarrage), appliquée avant de lire le corps.
        if (req.ContentLength > stockage.LimiteDepot) throw new ErreurHttp(413, stockage.MessageTropVolumineux);
        if (req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } maximum)
            maximum.MaxRequestBodySize = stockage.LimiteDepot;

        // Quotas vérifiés avant la réception : un dépôt annoncé plus gros que la place restante est refusé sans être lu.
        var depot = await quotas.ReserverDepot(db, mepId, acteur.Id, req.ContentLength, MargeFormulaire, maintenant, ct);
        try
        {
            var lecteur = new Microsoft.AspNetCore.WebUtilities.MultipartReader(limite, req.Body) { BodyLengthLimit = stockage.LimiteDepot };
            PieceJointe? piece = null;
            for (var section = await LireSection(lecteur, stockage, ct); section is not null; section = await LireSection(lecteur, stockage, ct))
            {
                if (!Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
                    || (Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition.FileName)
                        && Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(disposition.FileNameStar))) continue;
                // Le premier fichier du formulaire (champ « fichier » attendu) ; les suivants sont ignorés.
                if (piece is not null) continue;
                var nomFichier = Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(
                    disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                piece = new PieceJointe
                {
                    NomFichier = StockagePiecesJointes.AssainirNom(nomFichier),
                    TypeContenu = string.IsNullOrWhiteSpace(section.ContentType) || section.ContentType.Length > 255 ? "application/octet-stream" : section.ContentType,
                    DeposeParId = acteur.Id,
                    DeposeLe = maintenant,
                };
                try
                {
                    (piece.Taille, piece.Sha256) = await stockage.Ecrire(piece.Id, section.Body, ct, depot.Plafond, depot.Depassement);
                }
                catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                {
                    throw new ErreurHttp(413, stockage.MessageTropVolumineux);
                }
                if (piece.Taille == 0)
                {
                    stockage.Supprimer(piece.Id);
                    throw ErreurHttp.Saisie("Le fichier est vide.");
                }
            }
            if (piece is null) throw ErreurHttp.Saisie($"Aucun fichier reçu (champ « {ChampFichier} »).");
            // Compression sans perte si le fichier y gagne assez (réglages pieces-compression et pieces-compression-gain-min).
            (piece.TailleStockee, piece.Compression) = stockage.CompressionActive
                ? await stockage.Compresser(piece.Id, piece.Taille, piece.Sha256, ct)
                : new StockagePiecesJointes.Stockage(piece.Taille, null);
            return (piece, depot);
        }
        catch
        {
            depot.Dispose();
            throw;
        }
    }

    /// <summary>Partie suivante du formulaire ; un corps trop gros ou mal formé devient 413 ou 400.</summary>
    private static async Task<Microsoft.AspNetCore.WebUtilities.MultipartSection?> LireSection(Microsoft.AspNetCore.WebUtilities.MultipartReader lecteur,
        StockagePiecesJointes stockage, CancellationToken ct)
    {
        try
        {
            return await lecteur.ReadNextSectionAsync(ct);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new ErreurHttp(413, stockage.MessageTropVolumineux);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw ErreurHttp.Saisie($"Formulaire de dépôt illisible : envoyer le fichier en multipart/form-data (champ « {ChampFichier} »).");
        }
    }

    private static void ExigerNonCloturee(Mep mep)
    {
        if (Regles.EstTerminee(mep)) throw new RegleMetierException("MEP terminée (clôturée ou annulée) : ses pièces jointes ne sont plus modifiables.");
    }

    /// <summary>
    /// Ajoute une pièce déjà écrite sur disque à la MEP (journal et notification), par <see cref="ServiceMep.Modifier{T}"/> :
    /// <paramref name="verifier"/> revérifie la cible sur la MEP fraîche à chaque essai et renvoie son libellé pour le journal.
    /// Le fichier n'est écrit qu'une fois (avant) et n'est effacé que si l'enregistrement échoue définitivement.
    /// </summary>
    private static async Task<MepDto> Ajouter(Guid mepId, PieceJointe piece, Func<Mep, string> verifier, Acteur acteur, ServiceMep svc, MepDbContext db,
        StockagePiecesJointes stockage)
    {
        try
        {
            return await svc.Modifier(mepId, async mep =>
            {
                var cible = verifier(mep);
                piece.MepId = mep.Id;
                db.PiecesJointes.Add(piece);
                return await svc.Enregistrer(mep, acteur, "piece.ajoutee", $"Pièce jointe ajoutée{cible} : {piece.NomFichier}");
            });
        }
        catch
        {
            stockage.Supprimer(piece.Id);
            throw;
        }
    }

    private static string Cible(Mep mep, PieceJointe p) =>
        p.EtapeId is { } e ? $" à {mep.Etapes.FirstOrDefault(x => x.Id == e)?.Code}"
        : p.ExecutionResolutionId is not null ? " à une résolution" : "";

    public static void MapPiecesJointes(this RouteGroupBuilder api)
    {
        const long limiteDepot = StockagePiecesJointes.LimiteDepotMaximale;
        // ---------- MEP, étape, exécution de résolution ----------
        api.MapGet("/meps/{id:guid}/pieces-jointes", async (Guid id, MepDbContext db) =>
        {
            if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
            var noms = await Personnes.Noms(db);
            return (await db.PiecesJointes.AsNoTracking().Where(p => p.MepId == id).OrderBy(p => p.DeposeLe).ToListAsync())
                .Select(p => MepMapping.VersDto(p, noms));
        });

        // Dépôts : la cible est vérifiée avant de lire le fichier, puis de nouveau sur la MEP fraîche à l'enregistrement.
        api.MapPost("/meps/{id:guid}/pieces-jointes", async (Guid id, HttpRequest req, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            StockagePiecesJointes stockage, QuotasPiecesJointes quotas) =>
        {
            var acteur = Acteur.Depuis(user);
            static string Verifier(Mep m)
            {
                ExigerNonCloturee(m);
                return "";
            }
            Verifier(await svc.Charger(id));
            var (piece, reservation) = await Recevoir(req, stockage, quotas, db, id, acteur, svc.Maintenant);
            using (reservation) return Results.Ok(await Ajouter(id, piece, Verifier, acteur, svc, db, stockage));
        }).AccepterFichier(limiteDepot);

        api.MapPost("/meps/{id:guid}/etapes/{eid:guid}/pieces-jointes", async (Guid id, Guid eid, HttpRequest req, ClaimsPrincipal user, ServiceMep svc,
            MepDbContext db, StockagePiecesJointes stockage, QuotasPiecesJointes quotas) =>
        {
            var acteur = Acteur.Depuis(user);
            string Verifier(Mep m)
            {
                var etape = ServiceMep.TrouverEtape(m, eid);
                ExigerNonCloturee(m);
                return $" à {etape.Code}";
            }
            Verifier(await svc.Charger(id));
            var (piece, reservation) = await Recevoir(req, stockage, quotas, db, id, acteur, svc.Maintenant);
            piece.EtapeId = eid;
            using (reservation) return Results.Ok(await Ajouter(id, piece, Verifier, acteur, svc, db, stockage));
        }).AccepterFichier(limiteDepot);

        api.MapPost("/meps/{id:guid}/resolutions/{rid:guid}/pieces-jointes", async (Guid id, Guid rid, HttpRequest req, ClaimsPrincipal user,
            ServiceMep svc, MepDbContext db, StockagePiecesJointes stockage, QuotasPiecesJointes quotas) =>
        {
            var acteur = Acteur.Depuis(user);
            string Verifier(Mep m)
            {
                var execution = m.Resolutions.FirstOrDefault(r => r.Id == rid) ?? throw ErreurHttp.NonTrouve("Résolution introuvable.");
                ExigerNonCloturee(m);
                return $" à la résolution « {execution.Titre} »";
            }
            Verifier(await svc.Charger(id));
            var (piece, reservation) = await Recevoir(req, stockage, quotas, db, id, acteur, svc.Maintenant);
            piece.ExecutionResolutionId = rid;
            using (reservation) return Results.Ok(await Ajouter(id, piece, Verifier, acteur, svc, db, stockage));
        }).AccepterFichier(limiteDepot);

        api.MapDelete("/meps/{id:guid}/pieces-jointes/{pid:guid}", async (Guid id, Guid pid, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            StockagePiecesJointes stockage) =>
        {
            var acteur = Acteur.Depuis(user);
            var dto = await svc.Modifier(id, async mep =>
            {
                var piece = await db.PiecesJointes.FirstOrDefaultAsync(p => p.Id == pid && p.MepId == id) ?? throw ErreurHttp.NonTrouve("Pièce jointe introuvable.");
                if (piece.DeposeParId != acteur.Id && !Regles.EstPilote(mep, acteur.Id) && !acteur.EstAdmin)
                    throw ErreurHttp.Interdit("Seul le déposant, le pilote, un suppléant ou un administrateur peut supprimer cette pièce jointe.");
                ExigerNonCloturee(mep);
                db.PiecesJointes.Remove(piece);
                return await svc.Enregistrer(mep, acteur, "piece.supprimee", $"Pièce jointe supprimée{Cible(mep, piece)} : {piece.NomFichier}");
            });
            // Fichier effacé une seule fois, après l'enregistrement réussi.
            stockage.Supprimer(pid);
            return Results.Ok(dto);
        });

        // ---------- Résolution type ----------
        api.MapGet("/resolutions/{id:guid}/pieces-jointes", async (Guid id, MepDbContext db) =>
        {
            if (!await db.ResolutionsTypes.AnyAsync(r => r.Id == id)) throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            var noms = await Personnes.Noms(db);
            return (await db.PiecesJointes.AsNoTracking().Where(p => p.ResolutionTypeId == id).OrderBy(p => p.DeposeLe).ToListAsync())
                .Select(p => MepMapping.VersDto(p, noms));
        });

        api.MapPost("/resolutions/{id:guid}/pieces-jointes", async (Guid id, HttpRequest req, ClaimsPrincipal user, MepDbContext db,
            StockagePiecesJointes stockage, QuotasPiecesJointes quotas, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!await db.ResolutionsTypes.AnyAsync(r => r.Id == id)) throw ErreurHttp.NonTrouve("Résolution type introuvable.");
            var (piece, reservation) = await Recevoir(req, stockage, quotas, db, null, acteur, temps.GetUtcNow());
            using var _ = reservation;
            piece.ResolutionTypeId = id;
            try
            {
                db.PiecesJointes.Add(piece);
                await db.SaveChangesAsync();
            }
            catch
            {
                stockage.Supprimer(piece.Id);
                throw;
            }
            return Results.Created($"/api/pieces-jointes/{piece.Id}/contenu", MepMapping.VersDto(piece, new Dictionary<Guid, string> { [acteur.Id] = acteur.Nom }));
        }).AccepterFichier(limiteDepot);

        api.MapDelete("/resolutions/{id:guid}/pieces-jointes/{pid:guid}", async (Guid id, Guid pid, ClaimsPrincipal user, MepDbContext db,
            StockagePiecesJointes stockage) =>
        {
            var piece = await db.PiecesJointes.FirstOrDefaultAsync(p => p.Id == pid && p.ResolutionTypeId == id)
                ?? throw ErreurHttp.NonTrouve("Pièce jointe introuvable.");
            db.PiecesJointes.Remove(piece);
            await db.SaveChangesAsync();
            stockage.Supprimer(piece.Id);
            return Results.NoContent();
        }).ExigerAdmin();

        // ---------- Téléchargement ----------
        api.MapGet("/pieces-jointes/{pid:guid}/contenu", async (Guid pid, HttpContext ctx, MepDbContext db, StockagePiecesJointes stockage) =>
        {
            var piece = await db.PiecesJointes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid) ?? throw ErreurHttp.NonTrouve("Pièce jointe introuvable.");
            // Contenu d'origine, décompressé au fil de l'envoi si le fichier est stocké compressé : mêmes octets, nom et type qu'au dépôt.
            var flux = stockage.Ouvrir(piece.Id, piece.Taille) ?? throw ErreurHttp.NonTrouve("Le contenu de cette pièce jointe est introuvable sur le serveur.");
            ctx.Response.ContentLength = piece.Taille;
            // Toujours en téléchargement (Content-Disposition: attachment), jamais interprété par le navigateur.
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(flux, piece.TypeContenu, piece.NomFichier);
        });
    }
}
