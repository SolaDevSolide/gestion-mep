using System.Globalization;
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
    /// <summary>Durée d'un flux temps réel avant renouvellement par le navigateur.</summary>
    private static readonly TimeSpan DureeFlux = TimeSpan.FromHours(1);

    /// <summary>Date d'un instant dans le fuseau de la MEP (UTC si le fuseau est inconnu) ; null si l'instant est absent.</summary>
    private static DateOnly? DateLocale(DateTimeOffset? instant, string fuseau)
    {
        if (instant is not { } i) return null;
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(i, TimeZoneInfo.FindSystemTimeZoneById(fuseau)).DateTime); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return DateOnly.FromDateTime(i.UtcDateTime); }
    }

    /// <summary>
    /// Liste des MEP, les plus récentes d'abord (partagée avec <c>gestion-mep meps lister</c>). <paramref name="modele"/> :
    /// true, modèles seulement ; false, MEP seulement ; null, tout. <paramref name="equipe"/> : MEP dont au moins une personne
    /// fait partie de l'équipe (par l'un de ses comptes reliés).
    /// </summary>
    public static async Task<List<MepResumeDto>> Lister(MepDbContext db, bool? modele, Guid? equipe = null)
    {
        var annuaire = await Personnes.Charger(db);
        var meps = await Resumes(db, modele, annuaire);
        return equipe is { } e ? meps.Where(m => m.EquipeIds.Contains(e)).ToList() : meps;
    }

    private static async Task<List<MepResumeDto>> Resumes(MepDbContext db, bool? modele, AnnuairePersonnes annuaire) =>
        (await db.Meps.AsNoTracking().Where(m => modele == null || m.EstModele == modele).OrderByDescending(m => m.CreeLe)
            .Select(m => new
            {
                m.Id, m.Outil, m.VersionCible, m.Objet, m.DemarrageAutoriseLe, m.FuseauHoraire, m.Phase, m.Issue, m.EcheanceRepli,
                Pilote = m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
                // Avancement du déploiement uniquement (le retour arrière a son propre plan).
                NbEtapes = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement),
                NbReussies = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Reussie),
                NbEchecs = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Echec),
                Environnements = m.Systemes.Select(s => s.Environnements).ToList(), m.EstModele, m.CreeLe, m.RetourArriereActive,
                Comptes = m.Participants.Where(p => p.UtilisateurId != null).Select(p => p.UtilisateurId!.Value).ToList(),
                Noms = m.Participants.Select(p => p.Nom).ToList(),
            })
            .ToListAsync())
        // dateMep : date du Go (la date saisie n'existe plus depuis le jalon 4.2).
        .Select(m => new MepResumeDto(m.Id, m.Outil, m.VersionCible, m.Objet, DateLocale(m.DemarrageAutoriseLe, m.FuseauHoraire), m.Phase,
            m.EcheanceRepli, m.Pilote, m.NbEtapes, m.NbReussies, m.NbEchecs,
            m.Environnements.Any(l => l.Any(e => e.Production)), m.EstModele, m.CreeLe, m.Issue,
            m.RetourArriereActive,
            [.. m.Comptes.SelectMany(annuaire.EquipesDeLaPersonne).Distinct().Order()],
            [.. m.Noms.Concat(m.Comptes.SelectMany(annuaire.TermesDeRecherche)).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)])).ToList();

    /// <summary>Journal d'une MEP, du plus récent au plus ancien (partagé avec <c>gestion-mep meps exporter</c>).</summary>
    public static async Task<List<EvenementDto>> Journal(MepDbContext db, Guid id)
    {
        if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
        return await db.Journal.AsNoTracking().Where(e => e.MepId == id).OrderByDescending(e => e.Id)
            .Select(e => new EvenementDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail,
                db.Utilisateurs.Where(u => u.Id == e.UtilisateurId).Select(u => u.Login).FirstOrDefault())).ToListAsync();
    }

    public static void MapMeps(this RouteGroupBuilder api)
    {
        // ?modele=true : modèles seulement ; ?modele=false : MEP seulement ; absent : tout. ?equipe=<id> : MEP de l'équipe.
        api.MapGet("/meps", async (bool? modele, Guid? equipe, MepDbContext db) => (await Lister(db, modele, equipe)).AsEnumerable());

        api.MapPost("/meps", async (MepCreationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            var modele = corps.Modele == true;
            var mep = new Mep { Outil = outil, Objet = ServiceMep.Texte(corps.Objet), EstModele = modele, CreeLe = svc.Maintenant, CreeParUtilisateurId = acteur.Id };
            // Le créateur est participant et responsable technique ; le pilote se choisit ensuite.
            var createur = new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Responsable technique", UtilisateurId = acteur.Id };
            mep.Participants.Add(createur);
            mep.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = createur.Id, Disponibilite = DisponibiliteJourJ.Joignable });
            db.Meps.Add(mep);
            var dto = await svc.Enregistrer(mep, acteur, "mep.creee", modele ? $"Modèle créé : {outil}" : $"MEP créée : {outil}");
            return Results.Created($"/api/meps/{mep.Id}", dto);
        });

        api.MapPost("/meps/{id:guid}/dupliquer", async (Guid id, DuplicationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var source = await svc.Charger(id);
            var copie = Regles.Dupliquer(source, corps.CommeModele, acteur.Id, svc.Maintenant);
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

        api.MapPut("/meps/{id:guid}", async (Guid id, MepIdentiteSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
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
            if (corps.BilanActive is { } bilan) Si(mep.BilanActive, bilan, v => mep.BilanActive = v, "bilan de clôture");
            // Activation ou désactivation du plan de retour arrière : un événement de journal à part, pour qu'elle se repère.
            var retourArriere = corps.RetourArriereActive is { } ra && ra != mep.RetourArriereActive ? ra : (bool?)null;
            if (retourArriere is { } active) modifs.Add(("plan de retour arrière", () => mep.RetourArriereActive = active));
            if (modifs.Count > 0)
            {
                // Pilote et suppléants ont le Go et la clôture : seul le pilote actuel (ou un suppléant) ou un administrateur les change.
                // Sans pilote encore désigné, tout éditeur peut le choisir.
                if (mep.PiloteId is not null && modifs.Any(m => m.Nom is "pilote" or "suppléants"))
                    ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, un suppléant ou un administrateur peut changer le pilote ou les suppléants.");
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
            if (noms.Count == 0) return await svc.VersDto(mep, acteur.Id);
            var evenements = new List<ServiceMep.Evenement>();
            if (retourArriere is { } activation)
            {
                noms.Remove("plan de retour arrière");
                evenements.Add(activation
                    ? new("repli.active", "Plan de retour arrière activé")
                    : new("repli.desactive", "Plan de retour arrière désactivé : ni plan ni échéance réclamés, aucun déclenchement"));
            }
            if (noms.Count > 0) evenements.Add(new("mep.modifiee", "MEP modifiée : " + string.Join(", ", noms)));
            return await svc.EnregistrerContenu(mep, acteur, evenements);
        })));

        api.MapPut("/meps/{id:guid}/sauvegarde", async (Guid id, SauvegardeSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            // Ces informations servent de preuve (sauvegarde réalisée, restauration testée) : après la préparation, seuls le
            // pilote, ses suppléants ou un administrateur les complètent, et plus personne une fois la MEP clôturée.
            if (mep.Phase == Phase.Cloturee) throw new RegleMetierException("La MEP est clôturée : les informations de sauvegarde ne se modifient plus.");
            if (mep.Phase != Phase.Preparation)
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Après la préparation, seuls le pilote, un suppléant ou un administrateur modifient les informations de sauvegarde.");
            ServiceMep.ExigerVersion(mep, corps.Version);
            if (corps.ResponsableId is not null) ServiceMep.TrouverParticipant(mep, corps.ResponsableId, "Responsable de la sauvegarde");
            var s = mep.Sauvegarde;
            // Le mode change la vérification du plan : il ne se modifie qu'en préparation (absent : inchangé).
            if (corps.Mode is { } mode && mode != s.Mode)
            {
                Regles.ExigerPreparation(mep);
                s.Mode = mode;
            }
            // Le journal garde l'ancienne et la nouvelle valeur des preuves, pour que toute modification reste traçable.
            var changements = new List<string>();
            void Preuve<T>(string nom, T avant, T apres, Func<T, string> texte)
            {
                if (!EqualityComparer<T>.Default.Equals(avant, apres)) changements.Add($"{nom} : {texte(avant)} → {texte(apres)}");
            }
            string Date(DateTimeOffset? d) => d?.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "vide";
            string Jour(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "vide";
            string OuiNon(bool b) => b ? "oui" : "non";
            string Texte(string t) => t.Length == 0 ? "vide" : $"« {(t.Length > 80 ? t[..80] + "…" : t)} »";
            var realisee = corps.RealiseeLe?.ToUniversalTime();
            var procedure = ServiceMep.Texte(corps.Procedure);
            var copies = ServiceMep.Texte(corps.Copies);
            var preuve = ServiceMep.Texte(corps.PreuveTestRestauration);
            Preuve("réalisée le", s.RealiseeLe, realisee, Date);
            Preuve("restauration testée", s.RestaurationTestee, corps.RestaurationTestee, OuiNon);
            Preuve("date du test", s.DateTestRestauration, corps.DateTestRestauration, Jour);
            Preuve("preuve du test", s.PreuveTestRestauration, preuve, Texte);
            Preuve("procédure", s.Procedure, procedure, Texte);
            Preuve("copies", s.Copies, copies, Texte);
            s.Structure = corps.Structure;
            s.Donnees = corps.Donnees;
            s.Configuration = corps.Configuration;
            s.ResponsableId = corps.ResponsableId;
            s.RealiseeLe = realisee;
            s.Procedure = procedure;
            s.Copies = copies;
            s.RestaurationTestee = corps.RestaurationTestee;
            s.DateTestRestauration = corps.DateTestRestauration;
            s.PreuveTestRestauration = preuve;
            var detail = "Informations de sauvegarde modifiées" + (changements.Count > 0 ? " : " + string.Join(" ; ", changements) : "");
            return await svc.EnregistrerContenu(mep, acteur, "sauvegarde.modifiee", detail);
        })));

        api.MapDelete("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc, MepDbContext db, StockagePiecesJointes stockage) =>
        {
            var acteur = Acteur.Depuis(user);
            // Les fichiers ne sont effacés qu'une fois la suppression enregistrée (l'action peut être rejouée).
            var pieces = await svc.Modifier(id, async mep =>
            {
                // Le créateur, qui n'est plus pilote d'office, peut supprimer sa MEP tant qu'elle est en préparation.
                if (mep.CreeParUtilisateurId != acteur.Id)
                    ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, le créateur ou un administrateur peut supprimer une MEP.");
                Regles.ExigerPreparation(mep);
                var ids = await db.PiecesJointes.Where(p => p.MepId == id).Select(p => p.Id).ToListAsync();
                db.Meps.Remove(mep);
                await db.SaveChangesAsync(); // les lignes des pièces jointes partent en cascade
                return ids;
            });
            foreach (var p in pieces) stockage.Supprimer(p);
            return Results.NoContent();
        });

        api.MapGet("/meps/{id:guid}/journal", (Guid id, MepDbContext db) => Journal(db, id));

        // Tests unitaires faits, non faits et leurs résultats constatés.
        api.MapGet("/meps/{id:guid}/tests-unitaires", async (Guid id, ServiceMep svc) => MepMapping.TestsUnitaires(await svc.Charger(id)));

        // Flux limités (par compte et pour le serveur) et renouvelés toutes les heures : le navigateur se reconnecte seul.
        // ?session= : identifiant de l'onglet (choisi par le navigateur), pour déclarer ensuite ses éléments (PUT …/presence).
        api.MapGet("/meps/{id:guid}/flux", async (Guid id, string? session, HttpContext ctx, ClaimsPrincipal user, MepDbContext db, DiffuseurMep diffuseur) =>
        {
            var version = await db.Meps.AsNoTracking().Where(m => m.Id == id).Select(m => (int?)m.Version).FirstOrDefaultAsync(ctx.RequestAborted);
            if (version is null) throw ErreurHttp.NonTrouve("MEP introuvable.");
            if (session is { Length: > 64 }) throw ErreurHttp.Saisie("Identifiant de session trop long.");

            // Abonnement avant le premier envoi : aucune modification ne peut se perdre entre les deux.
            var acteur = Acteur.Depuis(user);
            using var abonnement = diffuseur.Abonner(id, acteur.Id, acteur.Nom, string.IsNullOrWhiteSpace(session) ? null : session)
                ?? throw new ErreurHttp(429, "Trop de flux temps réel ouverts : fermez des onglets, puis réessayez.");
            using var duree = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            duree.CancelAfter(DureeFlux);
            var ct = duree.Token;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // pas de tampon côté nginx
            try
            {
                await ctx.Response.WriteAsync(": connecté\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delai.CancelAfter(TimeSpan.FromSeconds(20));
                    try
                    {
                        if (!await abonnement.AttendreAsync(delai.Token)) break;
                        var envoi = abonnement.Prendre();
                        if (envoi.Version is { } v)
                            await ctx.Response.WriteAsync($"event: maj\ndata: {JsonSerializer.Serialize(new { version = v })}\n\n", ct);
                        if (envoi.Presence)
                            await ctx.Response.WriteAsync($"event: presence\ndata: {JsonSerializer.Serialize(diffuseur.Presences(id), JsonSerializerOptions.Web)}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* déconnexion du client, ou flux renouvelé (durée atteinte) */ }
        });

        // Présence : qui a la MEP ouverte, et sur quels éléments chacun travaille (informatif, rien n'est verrouillé).
        api.MapGet("/meps/{id:guid}/presence", async (Guid id, MepDbContext db, DiffuseurMep diffuseur) =>
        {
            if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
            return diffuseur.Presences(id);
        });

        // Déclare les éléments de l'onglet dont le flux est ouvert ; 404 si ce flux n'existe pas (encore, ou plus).
        api.MapPut("/meps/{id:guid}/presence", (Guid id, PresenceSaisie corps, ClaimsPrincipal user, DiffuseurMep diffuseur) =>
        {
            var session = ServiceMep.Texte(corps.Session);
            if (session.Length == 0) throw ErreurHttp.Saisie("La session est obligatoire.");
            if (!diffuseur.DefinirElements(id, Acteur.Depuis(user).Id, session, corps.Elements))
                throw ErreurHttp.NonTrouve("Aucun flux temps réel ouvert pour cet onglet : rouvrez la MEP.");
            return Results.NoContent();
        });
    }
}
