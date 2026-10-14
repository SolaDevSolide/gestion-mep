using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using GestionMep.Tableur;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Bibliothèque de recettes (modèles de fiches rattachés à un système du catalogue, insérés par copie dans les MEP) et
/// recette en Excel : export pour les métiers, puis réimport de leur validation (aperçu, puis report).
/// </summary>
public static class RecetteEndpoints
{
    /// <summary>Taille maximale d'un fichier de validation réimporté.</summary>
    public const long TailleFichierMax = 5L * 1024 * 1024;
    private const string MessageRefus = "Fichier refusé, rien n'a été modifié : ";

    private static ModeleRecetteDto Dto(ModeleRecette m, Dictionary<Guid, string> noms) => new(m.Id, m.Titre, m.SystemeCatalogueId,
        m.Etapes.OrderBy(t => t.Ordre).Select(t => new EtapeModeleRecetteDto(t.Ordre, t.Instructions, t.ResultatAttendu)).ToList(),
        m.Version, m.ModifieLe, m.ModifieParId, m.ModifieParId is { } u && noms.TryGetValue(u, out var n) ? n : null, m.Utilisations, m.CreeLe);

    private static Task<Dictionary<Guid, string>> Noms(MepDbContext db) => db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NomAffiche);

    private static async Task<ModeleRecette> TrouverModele(MepDbContext db, Guid id) =>
        await db.ModelesRecette.FirstOrDefaultAsync(m => m.Id == id) ?? throw ErreurHttp.NonTrouve("Modèle de recette introuvable.");

    private static async Task ExigerSystemeCatalogue(MepDbContext db, Guid? id)
    {
        if (id is null) throw ErreurHttp.Saisie("Choisir le système du catalogue testé par le modèle.");
        if (!await db.SystemesCatalogue.AnyAsync(s => s.Id == id)) throw ErreurHttp.Saisie("Système du catalogue inconnu.");
    }

    /// <summary>Contrôle le modèle (422 avec la liste des anomalies), puis note la date et l'auteur.</summary>
    private static void Valider(ModeleRecette m, Acteur acteur, DateTimeOffset maintenant)
    {
        var erreurs = Regles.ErreursModeleRecette(m);
        if (erreurs.Count > 0) throw ErreurHttp.Regle(erreurs);
        m.ModifieLe = maintenant;
        m.ModifieParId = acteur.Id;
    }

    private static List<EtapeModeleRecette> Etapes(List<EtapeModeleRecetteSaisie>? etapes) =>
        (etapes ?? []).Select((t, i) => new EtapeModeleRecette
        {
            Ordre = i + 1, Instructions = ServiceMep.Texte(t.Instructions), ResultatAttendu = ServiceMep.Texte(t.ResultatAttendu),
        }).ToList();

    /// <summary>Contenu du fichier envoyé en multipart/form-data (champ « fichier »), 5 Mo au plus.</summary>
    internal static async Task<byte[]> LireFichier(HttpRequest req)
    {
        if (!req.HasFormContentType) throw ErreurHttp.Saisie($"Envoyer le fichier en multipart/form-data (champ « {PiecesJointesEndpoints.ChampFichier} »).");
        if (req.ContentLength > TailleFichierMax + 64 * 1024) throw new ErreurHttp(413, "Fichier trop volumineux (5 Mo au plus).");
        IFormCollection form;
        try
        {
            form = await req.ReadFormAsync(req.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            throw new ErreurHttp(413, "Fichier trop volumineux (5 Mo au plus).");
        }
        var fichier = form.Files.GetFile(PiecesJointesEndpoints.ChampFichier) ?? (form.Files.Count > 0 ? form.Files[0] : null)
            ?? throw ErreurHttp.Saisie($"Aucun fichier reçu (champ « {PiecesJointesEndpoints.ChampFichier} »).");
        if (fichier.Length == 0) throw ErreurHttp.Saisie("Le fichier est vide.");
        if (fichier.Length > TailleFichierMax) throw new ErreurHttp(413, "Fichier trop volumineux (5 Mo au plus).");
        using var flux = new MemoryStream();
        await fichier.CopyToAsync(flux, req.HttpContext.RequestAborted);
        return flux.ToArray();
    }

    /// <summary>
    /// Analyse un fichier de validation pour la MEP : droits et phase de saisie d'une recette, puis contrôle complet du
    /// fichier. La moindre anomalie le fait refuser en entier (422, liste des raisons dans <c>erreurs</c>).
    /// </summary>
    private static Regles.AnalyseValidation Analyser(Mep mep, Acteur acteur, byte[] contenu)
    {
        Regles.ExigerSaisieRecette(mep, acteur.Id, acteur.EstAdmin);
        RecetteExcel.Lecture lecture;
        try
        {
            lecture = RecetteExcel.Lire(contenu, mep);
        }
        catch (ClasseurIllisibleException ex)
        {
            throw ErreurHttp.Regle([ex.Message], MessageRefus);
        }
        if (lecture.MepId is null && lecture.Erreurs.Count > 0) throw ErreurHttp.Regle(lecture.Erreurs, MessageRefus);
        var analyse = Regles.AnalyserValidation(mep, lecture.MepId, lecture.Version, lecture.Lignes);
        var erreurs = analyse.Erreurs.Count == 1 && lecture.MepId != mep.Id ? analyse.Erreurs : [.. lecture.Erreurs, .. analyse.Erreurs];
        if (erreurs.Count > 0) throw ErreurHttp.Regle(erreurs, MessageRefus);
        return analyse;
    }

    private static ApercuValidationDto Apercu(Mep mep, Regles.AnalyseValidation a) => new(a.Changements.Select(c => new ChangementValidationDto(
        c.Fiche.Id, c.Etape.Id, mep.Systemes.FirstOrDefault(s => s.Id == c.Fiche.SystemeId)?.Nom ?? RecetteExcel.SansSysteme,
        c.Fiche.Titre, c.Etape.Ordre, c.Resultat, c.Observation, c.ValidePar, c.Date)).ToList(), a.Inchangees, a.SansValidation);

    public static void MapRecette(this RouteGroupBuilder api)
    {
        // ---------- Bibliothèque de recettes ----------
        api.MapGet("/modeles-recette", async (Guid? systemeCatalogueId, MepDbContext db) =>
        {
            var q = db.ModelesRecette.AsNoTracking();
            if (systemeCatalogueId is { } s) q = q.Where(m => m.SystemeCatalogueId == s);
            var noms = await Noms(db);
            // Tri des bibliothèques : les plus utilisés d'abord, puis par ordre de création.
            return (await q.OrderByDescending(m => m.Utilisations).ThenBy(m => m.CreeLe).ThenBy(m => m.Titre).ToListAsync()).Select(m => Dto(m, noms));
        });

        api.MapGet("/modeles-recette/{id:guid}", async (Guid id, MepDbContext db) => Dto(await TrouverModele(db, id), await Noms(db)));

        api.MapPost("/modeles-recette", async (ModeleRecetteSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            await ExigerSystemeCatalogue(db, c.SystemeCatalogueId);
            var m = new ModeleRecette
            {
                Titre = ServiceMep.Texte(c.Titre), SystemeCatalogueId = c.SystemeCatalogueId!.Value, Etapes = Etapes(c.Etapes), Version = 1,
                CreeLe = temps.GetUtcNow(),
            };
            Valider(m, acteur, temps.GetUtcNow());
            db.ModelesRecette.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/modeles-recette/{m.Id}", Dto(m, await Noms(db)));
        });

        // « Enregistrer comme modèle » depuis une fiche de MEP (copie, sans les résultats).
        api.MapPost("/modeles-recette/depuis-fiche", async (ModeleDepuisFicheSaisie c, ClaimsPrincipal user, MepDbContext db, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            if (c.MepId is null || c.FicheId is null) throw ErreurHttp.Saisie("La MEP et la fiche de recette sont obligatoires.");
            var mep = await svc.Charger(c.MepId.Value);
            var fiche = mep.FichesRecette.FirstOrDefault(f => f.Id == c.FicheId) ?? throw ErreurHttp.NonTrouve("Fiche de recette introuvable.");
            await ExigerSystemeCatalogue(db, c.SystemeCatalogueId);
            var titre = ServiceMep.Texte(c.Titre);
            var m = new ModeleRecette
            {
                Titre = titre.Length > 0 ? titre : fiche.Titre, SystemeCatalogueId = c.SystemeCatalogueId!.Value,
                Etapes = Regles.EtapesPourModele(fiche), Version = 1, CreeLe = svc.Maintenant,
            };
            Valider(m, acteur, svc.Maintenant);
            db.ModelesRecette.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/modeles-recette/{m.Id}", Dto(m, await Noms(db)));
        });

        api.MapPut("/modeles-recette/{id:guid}", async (Guid id, ModeleRecetteSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider temps) =>
        {
            var acteur = Acteur.Depuis(user);
            var m = await TrouverModele(db, id);
            if (c.Version is null) throw ErreurHttp.Saisie("La version du modèle est obligatoire.");
            if (c.Version != m.Version)
                throw ErreurHttp.Conflit("Ce modèle a été modifié par quelqu'un d'autre depuis votre dernière lecture. Rechargez-le puis recommencez.");
            await ExigerSystemeCatalogue(db, c.SystemeCatalogueId);
            m.Titre = ServiceMep.Texte(c.Titre);
            m.SystemeCatalogueId = c.SystemeCatalogueId!.Value;
            m.Etapes = Etapes(c.Etapes);
            Valider(m, acteur, temps.GetUtcNow());
            m.Version++;
            await db.SaveChangesAsync();
            return Results.Ok(Dto(m, await Noms(db)));
        });

        // Les fiches déjà insérées dans des MEP sont des copies : elles ne changent pas.
        api.MapDelete("/modeles-recette/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
            var m = await TrouverModele(db, id);
            db.ModelesRecette.Remove(m);
            JournalAdmin.Ecrire(db, acteur, "modele-recette.supprime", $"Modèle de recette « {m.Titre} » supprimé de la bibliothèque");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/meps/{id:guid}/fiches-recette/depuis-modele", async (Guid id, InsertionModeleRecetteSaisie c, ClaimsPrincipal user, ServiceMep svc,
            MepDbContext db) =>
        {
            if (c.ModeleId is null) throw ErreurHttp.Saisie("Choisir un modèle de la bibliothèque.");
            if (c.SystemeId is null) throw ErreurHttp.Saisie("Choisir le système de la fiche de recette.");
            var modele = await db.ModelesRecette.AsNoTracking().FirstOrDefaultAsync(m => m.Id == c.ModeleId)
                ?? throw ErreurHttp.NonTrouve("Modèle de recette introuvable.");
            return Results.Ok(await svc.Modifier(id, async mep =>
            {
                var acteur = Acteur.Depuis(user);
                ServiceMep.ExigerEditeur(mep, acteur);
                Regles.ExigerPreparation(mep);
                ServiceMep.ExigerSystemeExistant(mep, c.SystemeId);
                var f = Regles.InsererModeleRecette(mep, c.SystemeId.Value, modele);
                return await svc.EnregistrerContenu(mep, acteur, "recette.fiche-ajoutee", $"{Regles.NomRecette(f)} ajoutée depuis la bibliothèque");
            }));
        });

        // ---------- Recette en Excel ----------
        api.MapGet("/meps/{id:guid}/recette/excel", async (Guid id, ServiceMep svc, MepDbContext db) =>
        {
            var mep = await svc.Charger(id);
            var noms = await Noms(db);
            return Results.File(RecetteExcel.Exporter(mep, noms, svc.Maintenant), RecetteExcel.TypeContenu, RecetteExcel.NomFichier(mep));
        }).Produces(StatusCodes.Status200OK, contentType: RecetteExcel.TypeContenu);

        // Aperçu : rien n'est modifié.
        api.MapPost("/meps/{id:guid}/recette/excel/apercu", async (Guid id, HttpRequest req, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var contenu = await LireFichier(req);
            var mep = await svc.Charger(id);
            return Results.Ok(Apercu(mep, Analyser(mep, Acteur.Depuis(user), contenu)));
        }).WithMetadata(new RequestSizeLimitAttribute(TailleFichierMax + 64 * 1024));

        // Report : le fichier est de nouveau analysé sur la MEP fraîche, puis tout est appliqué d'un coup (ou rien).
        api.MapPost("/meps/{id:guid}/recette/excel/import", async (Guid id, HttpRequest req, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var contenu = await LireFichier(req);
            var acteur = Acteur.Depuis(user);
            return Results.Ok(await svc.Modifier(id, async mep =>
            {
                var analyse = Analyser(mep, acteur, contenu);
                if (analyse.Changements.Count == 0)
                    throw new RegleMetierException("Aucune nouvelle validation dans ce fichier : la recette est déjà à jour.");
                Regles.AppliquerValidation(mep, analyse, acteur.Id, acteur.EstAdmin, svc.Maintenant);
                var validees = analyse.Changements.Count(c => c.Resultat == ResultatControle.Conforme);
                var refusees = analyse.Changements.Count - validees;
                return await svc.Enregistrer(mep, acteur, "recette.import",
                    $"Validation de la recette importée d'Excel : {validees} étape(s) validée(s), {refusees} refusée(s)");
            }));
        }).WithMetadata(new RequestSizeLimitAttribute(TailleFichierMax + 64 * 1024));
    }
}
