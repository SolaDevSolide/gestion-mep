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

    /// <summary>
    /// Liste des MEP, les plus récemment créées d'abord (partagée avec <c>gestion-mep meps lister</c>). <paramref name="modele"/> :
    /// true, modèles seulement ; false, MEP seulement ; null, tout. Filtres et tri de la recherche : <see cref="FiltreMeps"/>.
    /// </summary>
    public static async Task<List<MepResumeDto>> Lister(MepDbContext db, bool? modele) => await Resumes(db, modele, await Personnes.Charger(db));

    /// <summary>Échecs d'étape survenus (événements « etape.echec » du journal), par MEP : code de l'étape, une entrée par échec.</summary>
    private static async Task<ILookup<Guid, string>> EchecsSurvenus(MepDbContext db) =>
        (await db.Journal.AsNoTracking().Where(e => e.Type == Regles.EvenementEchecEtape).Select(e => new { e.MepId, e.Detail }).ToListAsync())
        .ToLookup(e => e.MepId, e => Regles.CodeEvenementEtape(e.Detail));

    private static async Task<List<MepResumeDto>> Resumes(MepDbContext db, bool? modele, AnnuairePersonnes annuaire)
    {
        var echecs = await EchecsSurvenus(db);
        var lignes = await db.Meps.AsNoTracking().Where(m => modele == null || m.EstModele == modele).OrderByDescending(m => m.CreeLe)
            .Select(m => new
            {
                m.Id, m.Outil, m.VersionCible, m.Objet, m.DemarrageAutoriseLe, m.ClotureeLe, m.FuseauHoraire, m.Phase, m.Issue, m.EcheanceRepli,
                m.PiloteId, m.SuppleantIds, m.ResponsablesTechniques, m.Approbations,
                Pilote = m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
                // Avancement du déploiement uniquement (le retour arrière a son propre plan).
                NbEtapes = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement),
                NbReussies = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Reussie),
                NbEchecs = m.Etapes.Count(e => e.Plan == TypePlan.Deploiement && e.Etat == EtatEtape.Echec),
                Systemes = m.Systemes.Select(s => new { s.Id, s.Nom, s.CatalogueId, s.Environnements }).ToList(),
                m.EstModele, m.CreeLe, m.RetourArriereActive, m.Type, m.Report, m.Annulation,
                Participants = m.Participants.Select(p => new { p.Id, p.Nom, p.UtilisateurId, p.AjouteDOffice }).ToList(),
                Executants = m.Etapes.Where(e => e.ExecutantId != null).Select(e => e.ExecutantId).Distinct().ToList(),
                // Étapes à rattacher aux échecs survenus (code → systèmes).
                Etapes = m.Etapes.Select(e => new { e.Code, e.SystemeIds }).ToList(),
            })
            .ToListAsync();
        return lignes.Select(m =>
        {
            var comptes = m.Participants.Where(p => p.UtilisateurId != null).Select(p => p.UtilisateurId!.Value).ToList();
            var codes = echecs[m.Id].ToList();
            var systemesDesEtapes = m.Etapes.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().SystemeIds);
            int EchecsDu(Guid systeme) => codes.Count(c => systemesDesEtapes.TryGetValue(c, out var ids) && ids.Contains(systeme));
            // Rôles : la règle du domaine, sur une MEP réduite à ce qu'elle lit.
            var reduite = new Mep
            {
                Type = m.Type, PiloteId = m.PiloteId, SuppleantIds = m.SuppleantIds, ResponsablesTechniques = m.ResponsablesTechniques,
                Participants = [.. m.Participants.Select(p => new Participant { Id = p.Id, Nom = p.Nom, UtilisateurId = p.UtilisateurId, AjouteDOffice = p.AjouteDOffice })],
                Etapes = [.. m.Executants.Select(x => new Etape { ExecutantId = x })],
            };
            var rolesParticipants = Regles.RolesDesParticipants(reduite);
            var intervenants = new Dictionary<Guid, HashSet<RoleMep>>();
            HashSet<RoleMep> De(Guid compte)
            {
                var personne = annuaire.Personne(compte);
                if (!intervenants.TryGetValue(personne, out var r)) intervenants[personne] = r = [];
                return r;
            }
            foreach (var p in m.Participants.Where(p => p.UtilisateurId != null)) De(p.UtilisateurId!.Value).UnionWith(rolesParticipants[p.Id]);
            foreach (var a in m.Approbations) De(a.UtilisateurId).Add(RoleMep.Approbateur);
            var go = m.DemarrageAutoriseLe is { } g ? Fuseaux.Jour(g, m.FuseauHoraire) : (DateOnly?)null;
            return new MepResumeDto(m.Id, m.Outil, m.VersionCible, m.Objet, go, m.Phase,
                m.EcheanceRepli, m.Pilote, m.NbEtapes, m.NbReussies, m.NbEchecs,
                m.Systemes.Any(s => s.Environnements.Any(e => e.Production)), m.EstModele, m.CreeLe, m.Issue,
                m.RetourArriereActive,
                [.. comptes.SelectMany(annuaire.EquipesDeLaPersonne).Distinct().Order()],
                [.. m.Participants.Select(p => p.Nom).Concat(comptes.SelectMany(annuaire.TermesDeRecherche)).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
                m.Type,
                // dateMep : date du Go (la date saisie n'existe plus depuis le jalon 4.2).
                Regles.EtatDe(m.Phase, m.Issue, codes.Count, m.Report is not null), go ?? Fuseaux.Jour(m.CreeLe, m.FuseauHoraire),
                m.DemarrageAutoriseLe, m.ClotureeLe, codes.Count,
                [.. m.Systemes.Select(s => new SystemeResumeDto(s.CatalogueId, s.Nom,
                    [.. s.Environnements.Select(e => new EnvironnementSystemeDto(e.Nom, e.Production))], EchecsDu(s.Id)))],
                [.. intervenants.Select(i => new IntervenantDto(i.Key, annuaire.NomPersonne(i.Key) ?? "", [.. i.Value.Order()]))
                    .OrderBy(i => i.Nom, StringComparer.CurrentCultureIgnoreCase)],
                m.Phase == Phase.Preparation ? m.Report?.DateVisee : null, m.Annulation?.Le);
        }).ToList();
    }

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
        // ?modele=true : modèles seulement ; ?modele=false : MEP seulement ; absent : tout. Autres paramètres : recherche
        // avancée (FiltreMeps : systeme, environnement, equipe, personne, role, etat, phase, type, du, au, echec, recherche).
        api.MapGet("/meps", async (bool? modele, [AsParameters] ParametresFiltreMeps filtres, MepDbContext db) =>
            FiltreMeps.Lire(filtres).Appliquer(await Lister(db, modele)).AsEnumerable());

        api.MapPost("/meps", async (MepCreationSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            var modele = corps.Modele == true;
            var type = corps.Type ?? TypeMep.Complete;
            if (!Enum.IsDefined(type)) throw ErreurHttp.Saisie("Type de MEP inconnu : Complete ou Simplifiee.");
            if (modele && type != TypeMep.Complete) throw ErreurHttp.Saisie("Un modèle est toujours une MEP complète.");
            var mep = new Mep { Outil = outil, Objet = ServiceMep.Texte(corps.Objet), EstModele = modele, Type = type, CreeLe = svc.Maintenant, CreeParUtilisateurId = acteur.Id };
            // Le créateur est participant et responsable technique ; le pilote se choisit ensuite.
            var createur = new Participant { MepId = mep.Id, Nom = acteur.Nom, Role = "Responsable technique", UtilisateurId = acteur.Id };
            mep.Participants.Add(createur);
            mep.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = createur.Id, Disponibilite = DisponibiliteJourJ.Joignable });
            db.Meps.Add(mep);
            var dto = await svc.Enregistrer(mep, acteur, "mep.creee",
                modele ? $"Modèle créé : {outil}" : type == TypeMep.Simplifiee ? $"MEP simplifiée créée : {outil}" : $"MEP créée : {outil}");
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
            // suppleantIds absent (null) : inchangés. MEP simplifiée : suppléants implicites et pas de versions, la saisie est ignorée.
            var simplifiee = Regles.EstSimplifiee(mep);
            var suppleants = simplifiee ? [] : corps.SuppleantIds ?? [.. mep.SuppleantIds];
            foreach (var s in suppleants) ServiceMep.TrouverParticipant(mep, s, "Suppléant");
            var outil = ServiceMep.Texte(corps.Outil);
            if (outil.Length == 0) throw ErreurHttp.Saisie("L'outil est obligatoire.");
            // Fuseau absent ou vide : inchangé ; sinon un identifiant IANA connu (un identifiant Windows est converti).
            var saisieFuseau = ServiceMep.Texte(corps.FuseauHoraire);
            var fuseau = saisieFuseau.Length == 0 ? "" : Fuseaux.Normaliser(saisieFuseau)
                ?? throw ErreurHttp.Saisie($"Fuseau horaire inconnu : « {saisieFuseau} ». Indiquer un identifiant IANA, par exemple {Fuseaux.ParDefaut}.");

            // Champs d'identité : ne sont appliqués qu'en préparation.
            var modifs = new List<(string Nom, Action Appliquer)>();
            void Si<T>(T actuel, T nouveau, Action<T> poser, string nom)
            {
                if (!EqualityComparer<T>.Default.Equals(actuel, nouveau)) modifs.Add((nom, () => poser(nouveau)));
            }
            Si(mep.Outil, outil, v => mep.Outil = v, "outil");
            if (!simplifiee)
            {
                Si(mep.VersionActuelle, ServiceMep.Texte(corps.VersionActuelle), v => mep.VersionActuelle = v, "version actuelle");
                Si(mep.VersionCible, ServiceMep.Texte(corps.VersionCible), v => mep.VersionCible = v, "version cible");
            }
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

        // Type de MEP, en préparation : simplifiée → complète (tout est conservé) ou complète → simplifiée (destructif).
        api.MapPut("/meps/{id:guid}/type", async (Guid id, TypeMepSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            // Réservé au pilote (ou un suppléant, ou un administrateur) ; tant qu'aucun pilote n'est désigné, à tout éditeur.
            if (mep.PiloteId is not null)
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, un suppléant ou un administrateur peut changer le type de la MEP.");
            ServiceMep.ExigerVersion(mep, corps.Version);
            var type = corps.Type ?? throw ErreurHttp.Saisie("Le type (Complete ou Simplifiee) est obligatoire.");
            if (!Enum.IsDefined(type)) throw ErreurHttp.Saisie("Type de MEP inconnu : Complete ou Simplifiee.");
            if (type == mep.Type) return await svc.VersDto(mep, acteur.Id);
            if (type == TypeMep.Complete)
            {
                var suppleants = Regles.PasserEnComplete(mep);
                return await svc.EnregistrerContenu(mep, acteur, "mep.type",
                    $"MEP passée en complète : tout est conservé ; {suppleants} suppléant(s) conservé(s), versions et criticités à renseigner");
            }
            var e = Regles.PasserEnSimplifiee(mep);
            return await svc.EnregistrerContenu(mep, acteur, "mep.type",
                $"MEP passée en simplifiée : effacés {e.Durees} durée(s), {e.Criticites} criticité(s), {e.ResolutionsLiees} résolution(s) liée(s)"
                + (e.Versions ? ", versions actuelle et cible" : "") + $", {e.Suppleants} suppléant(s) choisi(s) (désormais toutes les personnes sauf le pilote)");
        })));

        api.MapPut("/meps/{id:guid}/sauvegarde", async (Guid id, SauvegardeSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            // Ces informations servent de preuve (sauvegarde réalisée, restauration testée) : après la préparation, seuls le
            // pilote, ses suppléants ou un administrateur les complètent, et plus personne une fois la MEP clôturée.
            if (Regles.EstTerminee(mep)) throw new RegleMetierException("La MEP est terminée (clôturée ou annulée) : les informations de sauvegarde ne se modifient plus.");
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

        // Suppression définitive (administrateur) : voir CycleDeVieEndpoints.

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
                        if (envoi.Verrous)
                            await ctx.Response.WriteAsync($"event: verrous\ndata: {JsonSerializer.Serialize(diffuseur.Verrous(id), JsonSerializerOptions.Web)}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                        // Verrous d'édition expirés (panneau fermé sans le dire) : retirés et diffusés au prochain réveil.
                        diffuseur.Verrous(id);
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* déconnexion du client, ou flux renouvelé (durée atteinte) */ }
        });

        // Présence : qui a la MEP ouverte, et sur quels éléments chacun travaille (informatif ; les verrous d'édition des étapes
        // sont à part, voir SectionsEndpoints).
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
