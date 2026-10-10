using System.Security.Claims;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

public static class MepsEndpoints
{
    /// <summary>Date d'un instant dans le fuseau de la MEP (UTC si le fuseau est inconnu) ; null si l'instant est absent.</summary>
    private static DateOnly? DateLocale(DateTimeOffset? instant, string fuseau)
    {
        if (instant is not { } i) return null;
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(i, TimeZoneInfo.FindSystemTimeZoneById(fuseau)).DateTime); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return DateOnly.FromDateTime(i.UtcDateTime); }
    }

    public static void MapMeps(this RouteGroupBuilder api)
    {
        // ?modele=true : modèles seulement ; ?modele=false : MEP seulement ; absent : tout.
        api.MapGet("/meps", async (bool? modele, MepDbContext db) =>
            (await db.Meps.AsNoTracking().Where(m => modele == null || m.EstModele == modele).OrderByDescending(m => m.CreeLe)
                .Select(m => new
                {
                    m.Id, m.Outil, m.VersionCible, m.Objet, m.DemarrageAutoriseLe, m.FuseauHoraire, m.Phase, m.EcheanceRepli,
                    Pilote = m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
                    // Avancement du déploiement uniquement (le retour arrière a son propre plan).
                    NbEtapes = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement),
                    NbReussies = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Reussie),
                    NbEchecs = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Echec),
                    m.Environnement, m.EstModele, m.CreeLe,
                })
                .ToListAsync())
            // dateMep : date du Go (la date saisie n'existe plus depuis le jalon 4.2).
            .Select(m => new MepResumeDto(m.Id, m.Outil, m.VersionCible, m.Objet, DateLocale(m.DemarrageAutoriseLe, m.FuseauHoraire), m.Phase,
                m.EcheanceRepli, m.Pilote, m.NbEtapes, m.NbReussies, m.NbEchecs, m.Environnement, m.EstModele, m.CreeLe)));

        api.MapPost("/meps", async (MepCreationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            var mep = new Mep { Outil = outil, Objet = ServiceMep.Texte(corps.Objet), CreeLe = svc.Maintenant, CreeParUtilisateurId = acteur.Id };
            // Le créateur est participant et responsable technique ; le pilote se choisit ensuite.
            var createur = new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Responsable technique", UtilisateurId = acteur.Id };
            mep.Participants.Add(createur);
            mep.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = createur.Id, Disponibilite = DisponibiliteJourJ.Joignable });
            db.Meps.Add(mep);
            var dto = await svc.Enregistrer(mep, acteur, "mep.creee", $"MEP créée : {outil}");
            return Results.Created($"/api/meps/{mep.Id}", dto);
        });

        api.MapPost("/meps/{id:guid}/dupliquer", async (Guid id, DuplicationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var source = await svc.Charger(id);
            var copie = Regles.Dupliquer(source, corps.Environnement, corps.CommeModele, acteur.Id, svc.Maintenant);
            // Celui qui duplique doit pouvoir éditer sa copie : il en devient participant et responsable technique s'il n'y figure pas.
            if (!Regles.PeutEditerContenu(copie, acteur.Id, acteur.EstAdmin))
            {
                var duplicateur = new Participant { MepId = copie.Id, Nom = acteur.Nom, Role = "Responsable technique", UtilisateurId = acteur.Id };
                copie.Participants.Add(duplicateur);
                copie.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = duplicateur.Id });
            }
            db.Meps.Add(copie);
            var detail = source.EstModele && !corps.CommeModele
                ? $"MEP créée depuis le modèle « {source.Outil} »"
                : $"MEP créée par duplication de « {source.Outil} »";
            var dto = await svc.Enregistrer(copie, acteur, "mep.creee", detail);
            return Results.Created($"/api/meps/{copie.Id}", dto);
        });

        api.MapGet("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc) =>
            Results.Ok(await svc.VersDto(await svc.Charger(id), Acteur.Depuis(user).Id)));

        api.MapPut("/meps/{id:guid}", async (Guid id, MepIdentiteSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerVersion(mep, corps.Version);

            if (corps.IndisponibiliteMinutes is < 0) throw ErreurHttp.Saisie("L'indisponibilité ne peut pas être négative.");
            if (corps.PiloteId is not null) ServiceMep.TrouverParticipant(mep, corps.PiloteId, "Pilote");
            // suppleantIds absent (null) : inchangés.
            var suppleants = corps.SuppleantIds ?? [.. mep.SuppleantIds];
            foreach (var s in suppleants) ServiceMep.TrouverParticipant(mep, s, "Suppléant");
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            var fuseau = ServiceMep.Texte(corps.FuseauHoraire);

            // Champs d'identité : ne sont appliqués qu'en préparation.
            var modifs = new List<(string Nom, Action Appliquer)>();
            void Si<T>(T actuel, T nouveau, Action<T> poser, string nom)
            {
                if (!EqualityComparer<T>.Default.Equals(actuel, nouveau)) modifs.Add((nom, () => poser(nouveau)));
            }
            Si(mep.Outil, outil, v => mep.Outil = v, "outil");
            Si(mep.VersionActuelle, ServiceMep.Texte(corps.VersionActuelle), v => mep.VersionActuelle = v, "version actuelle");
            Si(mep.VersionCible, ServiceMep.Texte(corps.VersionCible), v => mep.VersionCible = v, "version cible");
            Si(mep.Objet, ServiceMep.Texte(corps.Objet), v => mep.Objet = v, "objet");
            Si(mep.Impacts, ServiceMep.Texte(corps.Impacts), v => mep.Impacts = v, "impacts");
            // dateMep, heureDebut, heureFin : ignorés depuis le jalon 4.2 (début = Go, fin = clôture).
            if (fuseau.Length > 0) Si(mep.FuseauHoraire, fuseau, v => mep.FuseauHoraire = v, "fuseau horaire");
            Si(mep.IndisponibiliteMinutes, corps.IndisponibiliteMinutes, v => mep.IndisponibiliteMinutes = v, "indisponibilité");
            Si(mep.PiloteId, corps.PiloteId, v => mep.PiloteId = v, "pilote");
            if (!mep.SuppleantIds.SequenceEqual(suppleants)) modifs.Add(("suppléants", () => mep.SuppleantIds = [.. suppleants]));
            // Clés facultatives (absentes : inchangées).
            if (corps.Environnement is not null) Si(mep.Environnement, ServiceMep.Texte(corps.Environnement), v => mep.Environnement = v, "environnement");
            if (corps.BilanActive is { } bilan) Si(mep.BilanActive, bilan, v => mep.BilanActive = v, "bilan de clôture");
            if (modifs.Count > 0)
            {
                Regles.ExigerPreparation(mep);
                Regles.VerifierSuppleants(mep, corps.PiloteId, suppleants);
                var comptePilote = mep.Participants.FirstOrDefault(p => p.Id == corps.PiloteId)?.UtilisateurId;
                var approbateur = mep.Approbations.FirstOrDefault(a => a.UtilisateurId == comptePilote);
                if (corps.PiloteId != mep.PiloteId && comptePilote is not null && approbateur is not null)
                    throw new RegleMetierException($"{approbateur.Nom} est approbateur : le retirer des approbateurs avant d'en faire le pilote.");
                foreach (var m in modifs) m.Appliquer();
            }

            var echeance = corps.EcheanceRepli?.ToUniversalTime();
            var noms = modifs.Select(m => m.Nom).ToList();
            if (mep.EcheanceRepli != echeance)
            {
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote ou son suppléant peut modifier l'échéance de retour arrière.");
                mep.EcheanceRepli = echeance;
                noms.Add("échéance de retour arrière");
            }
            if (noms.Count == 0) return Results.Ok(await svc.VersDto(mep, acteur.Id));
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "mep.modifiee", "MEP modifiée : " + string.Join(", ", noms)));
        });

        api.MapPut("/meps/{id:guid}/sauvegarde", async (Guid id, SauvegardeSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerVersion(mep, corps.Version);
            if (corps.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, corps.ResponsableId, "Responsable de la sauvegarde");
            var s = mep.Sauvegarde;
            // Le mode change la vérification du plan : il ne se modifie qu'en préparation (absent : inchangé).
            if (corps.Mode is { } mode && mode != s.Mode)
            {
                Regles.ExigerPreparation(mep);
                s.Mode = mode;
            }
            s.Structure = corps.Structure;
            s.Donnees = corps.Donnees;
            s.Configuration = corps.Configuration;
            s.ResponsableId = corps.ResponsableId;
            s.RealiseeLe = corps.RealiseeLe?.ToUniversalTime();
            s.Procedure = ServiceMep.Texte(corps.Procedure);
            s.Copies = ServiceMep.Texte(corps.Copies);
            s.RestaurationTestee = corps.RestaurationTestee;
            s.DateTestRestauration = corps.DateTestRestauration;
            s.PreuveTestRestauration = ServiceMep.Texte(corps.PreuveTestRestauration);
            return Results.Ok(await svc.EnregistrerContenu(mep, acteur, "sauvegarde.modifiee", "Informations de sauvegarde modifiées"));
        });

        api.MapDelete("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc, MepDbContext db, StockagePiecesJointes stockage) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            // Le créateur, qui n'est plus pilote d'office, peut supprimer sa MEP tant qu'elle est en préparation.
            if (mep.CreeParUtilisateurId != acteur.Id)
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, le créateur ou un administrateur peut supprimer une MEP.");
            Regles.ExigerPreparation(mep);
            var pieces = await db.PiecesJointes.Where(p => p.MepId == id).Select(p => p.Id).ToListAsync();
            db.Meps.Remove(mep);
            await db.SaveChangesAsync(); // les lignes des pièces jointes partent en cascade
            foreach (var p in pieces) stockage.Supprimer(p);
            return Results.NoContent();
        });

        api.MapGet("/meps/{id:guid}/journal", async (Guid id, MepDbContext db) =>
        {
            if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
            return await db.Journal.AsNoTracking().Where(e => e.MepId == id).OrderByDescending(e => e.Id)
                .Select(e => new EvenementDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail)).ToListAsync();
        });

        api.MapGet("/meps/{id:guid}/flux", async (Guid id, HttpContext ctx, MepDbContext db, DiffuseurMep diffuseur) =>
        {
            var ct = ctx.RequestAborted;
            var version = await db.Meps.AsNoTracking().Where(m => m.Id == id).Select(m => (int?)m.Version).FirstOrDefaultAsync(ct);
            if (version is null) throw ErreurHttp.NonTrouve("MEP introuvable.");

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // pas de tampon côté nginx
            // Abonnement avant le premier envoi : aucune modification ne peut se perdre entre les deux.
            using var abonnement = diffuseur.Abonner(id);
            await ctx.Response.WriteAsync(": connecté\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delai.CancelAfter(TimeSpan.FromSeconds(20));
                    try
                    {
                        if (!await abonnement.Lecteur.WaitToReadAsync(delai.Token)) break;
                        if (abonnement.Lecteur.TryRead(out var v))
                            await ctx.Response.WriteAsync($"event: maj\ndata: {JsonSerializer.Serialize(new { version = v })}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* déconnexion du client */ }
        });
    }
}
