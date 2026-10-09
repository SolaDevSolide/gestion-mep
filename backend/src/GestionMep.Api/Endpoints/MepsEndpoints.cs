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
    public static void MapMeps(this RouteGroupBuilder api)
    {
        api.MapGet("/meps", async (MepDbContext db) =>
            await db.Meps.AsNoTracking().OrderByDescending(m => m.CreeLe)
                .Select(m => new MepResumeDto(m.Id, m.Outil, m.VersionCible, m.Objet, m.DateMep, m.Phase, m.EcheanceRepli,
                    m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
                    // Avancement du déploiement uniquement (le retour arrière a son propre plan).
                    m.Etapes.Count(e => e.Plan == TypePlan.Deploiement),
                    m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Reussie),
                    m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Echec)))
                .ToListAsync());

        api.MapPost("/meps", async (MepCreationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            var mep = new Mep { Outil = outil, Objet = ServiceMep.Texte(corps.Objet), CreeLe = svc.Maintenant, CreeParUtilisateurId = acteur.Id };
            var pilote = new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Pilote", UtilisateurId = acteur.Id };
            mep.Participants.Add(pilote);
            mep.PiloteId = pilote.Id;
            db.Meps.Add(mep);
            var dto = await svc.Enregistrer(mep, acteur, "mep.creee", $"MEP créée : {outil}");
            return Results.Created($"/api/meps/{mep.Id}", dto);
        });

        api.MapGet("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc) =>
            Results.Ok(await svc.VersDto(await svc.Charger(id), Acteur.Depuis(user).Id)));

        api.MapPut("/meps/{id:guid}", async (Guid id, MepIdentiteSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerVersion(mep, corps.Version);

            if (corps.IndisponibiliteMinutes is < 0) throw ErreurHttp.Saisie("L'indisponibilité ne peut pas être négative.");
            if (corps.PiloteId is not null) ServiceMep.TrouverParticipant(mep, corps.PiloteId, "Pilote");
            if (corps.SuppleantId is not null) ServiceMep.TrouverParticipant(mep, corps.SuppleantId, "Suppléant");
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
            Si(mep.DateMep, corps.DateMep, v => mep.DateMep = v, "date");
            Si(mep.HeureDebut, corps.HeureDebut, v => mep.HeureDebut = v, "heure de début");
            Si(mep.HeureFin, corps.HeureFin, v => mep.HeureFin = v, "heure de fin");
            if (fuseau.Length > 0) Si(mep.FuseauHoraire, fuseau, v => mep.FuseauHoraire = v, "fuseau horaire");
            Si(mep.IndisponibiliteMinutes, corps.IndisponibiliteMinutes, v => mep.IndisponibiliteMinutes = v, "indisponibilité");
            Si(mep.PiloteId, corps.PiloteId, v => mep.PiloteId = v, "pilote");
            Si(mep.SuppleantId, corps.SuppleantId, v => mep.SuppleantId = v, "suppléant");
            if (modifs.Count > 0)
            {
                Regles.ExigerPreparation(mep);
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
            return Results.Ok(await svc.Enregistrer(mep, acteur, "mep.modifiee", "MEP modifiée : " + string.Join(", ", noms)));
        });

        api.MapPut("/meps/{id:guid}/sauvegarde", async (Guid id, SauvegardeSaisie corps, ClaimsPrincipal user, ServiceMep svc) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerVersion(mep, corps.Version);
            if (corps.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, corps.ResponsableId, "Responsable de la sauvegarde");
            var s = mep.Sauvegarde;
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
            return Results.Ok(await svc.Enregistrer(mep, acteur, "sauvegarde.modifiee", "Informations de sauvegarde modifiées"));
        });

        api.MapDelete("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote ou un administrateur peut supprimer une MEP.");
            Regles.ExigerPreparation(mep);
            db.Meps.Remove(mep);
            await db.SaveChangesAsync();
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
