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
    public static async Task<List<MepResumeDto>> Lister(MepDbContext db, bool? modele) =>
        await Resumes(db, Vue(db, modele), await Personnes.Charger(db));

    private static IQueryable<Mep> Vue(MepDbContext db, bool? modele) => db.Meps.AsNoTracking().Where(m => modele == null || m.EstModele == modele);

    /// <summary>
    /// MEP qui correspondent au filtre, triées comme <see cref="FiltreMeps.Appliquer"/>. Les critères simples (type, phase, état,
    /// systèmes du catalogue, équipe, période, échec) sont d'abord traduits en SQL (<see cref="FiltreMeps.Prefiltrer"/>) :
    /// seules les MEP candidates sont lues ; la règle exacte s'applique ensuite, la même que dans le navigateur.
    /// </summary>
    public static async Task<List<MepResumeDto>> Rechercher(MepDbContext db, bool? modele, FiltreMeps filtre)
    {
        var annuaire = await Personnes.Charger(db);
        return filtre.Appliquer(await Resumes(db, filtre.Prefiltrer(Vue(db, modele), annuaire), annuaire));
    }

    /// <summary>Taille d'une page de la liste par défaut, et au plus.</summary>
    public const int LimiteParDefaut = 30, LimiteMax = 200;

    /// <summary>
    /// Page de la liste filtrée : au plus <paramref name="limite"/> MEP après le curseur <paramref name="apres"/> (celui que
    /// renvoie la page précédente), le nombre total de résultats et le nombre de MEP (ou de modèles) sans filtre.
    /// </summary>
    public static async Task<PageMepsDto> Page(MepDbContext db, bool? modele, FiltreMeps filtre, int? limite, string? apres)
    {
        var n = limite ?? LimiteParDefaut;
        if (n is < 1 or > LimiteMax) throw ErreurHttp.Saisie($"« limite » doit être comprise entre 1 et {LimiteMax}.");
        var curseur = apres is null ? null : CurseurMeps.Lire(apres);
        var resultats = (await Rechercher(db, modele, filtre)).OrderByDescending(m => m.Jour).ThenByDescending(m => m.CreeLe).ThenByDescending(m => m.Id).ToList();
        var suite = curseur is null ? resultats : [.. resultats.SkipWhile(m => !curseur.Precede(m))];
        var page = suite.Take(n).ToList();
        var ensemble = await Vue(db, modele).CountAsync();
        return new PageMepsDto(page, resultats.Count, ensemble, suite.Count > n ? CurseurMeps.De(page[^1]) : null);
    }

    /// <summary>
    /// Étapes des échecs survenus (événements « etape.echec » du journal, par leur étape enregistrée), par MEP : une entrée par
    /// échec, pour rattacher les échecs aux systèmes. Le nombre d'échecs de la MEP est son compteur (<see cref="Mep.EchecsEtapes"/>).
    /// </summary>
    private static async Task<ILookup<Guid, Guid>> EtapesEnEchec(MepDbContext db, IQueryable<Mep> source) =>
        (await db.Journal.AsNoTracking().Where(e => e.Type == Regles.EvenementEchecEtape && e.EtapeId != null && source.Any(m => m.Id == e.MepId))
            .Select(e => new { e.MepId, e.EtapeId }).ToListAsync())
        .ToLookup(e => e.MepId, e => e.EtapeId!.Value);

    private static async Task<List<MepResumeDto>> Resumes(MepDbContext db, IQueryable<Mep> source, AnnuairePersonnes annuaire)
    {
        var echecs = await EtapesEnEchec(db, source);
        var lignes = await source.OrderByDescending(m => m.CreeLe)
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
                m.EstModele, m.CreeLe, m.RetourArriereActive, m.Type, m.Report, m.Annulation, m.EchecsEtapes, m.EchecsRecette,
                Participants = m.Participants.Select(p => new { p.Id, p.Nom, p.UtilisateurId, p.AjouteDOffice }).ToList(),
                Executants = m.Etapes.Where(e => e.ExecutantId != null).Select(e => e.ExecutantId).Distinct().ToList(),
                // Étapes à rattacher aux échecs survenus (étape → systèmes).
                Etapes = m.Etapes.Select(e => new { e.Id, e.SystemeIds }).ToList(),
            })
            .ToListAsync();
        return lignes.Select(m =>
        {
            var comptes = m.Participants.Where(p => p.UtilisateurId != null).Select(p => p.UtilisateurId!.Value).ToList();
            var etapesEnEchec = echecs[m.Id].ToList();
            var systemesDesEtapes = m.Etapes.ToDictionary(e => e.Id, e => e.SystemeIds);
            int EchecsDu(Guid systeme) => etapesEnEchec.Count(e => systemesDesEtapes.TryGetValue(e, out var ids) && ids.Contains(systeme));
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
                Regles.EtatDe(m.Phase, m.Issue, m.EchecsEtapes + m.EchecsRecette, m.Report is not null), go ?? Fuseaux.Jour(m.CreeLe, m.FuseauHoraire),
                m.DemarrageAutoriseLe, m.ClotureeLe, m.EchecsEtapes,
                [.. m.Systemes.Select(s => new SystemeResumeDto(s.CatalogueId, s.Nom,
                    [.. s.Environnements.Select(e => new EnvironnementSystemeDto(e.Nom, e.Production))], EchecsDu(s.Id)))],
                [.. intervenants.Select(i => new IntervenantDto(i.Key, annuaire.NomPersonne(i.Key) ?? "", [.. i.Value.Order()]))
                    .OrderBy(i => i.Nom, StringComparer.CurrentCultureIgnoreCase)],
                m.Phase == Phase.Preparation ? m.Report?.DateVisee : null, m.Annulation?.Le, m.EchecsRecette);
        }).ToList();
    }

    /// <summary>Journal d'une MEP, du plus récent au plus ancien (partagé avec <c>gestion-mep meps exporter</c>).</summary>
    public static Task<List<EvenementDto>> Journal(MepDbContext db, Guid id) => Journal(db, id, new FiltreJournal());

    /// <summary>
    /// Événements du journal d'une MEP, du plus récent au plus ancien, filtrés (<see cref="FiltreJournal"/>) : par famille de
    /// type et texte, puis par position (<c>apres</c> : seulement les événements plus récents que celui-ci, pour suivre la MEP
    /// sans relire tout le journal ; <c>avant</c> : les plus anciens, pour la page suivante), au plus <c>limite</c>.
    /// Le compte de l'auteur est lu par jointure, en une seule requête.
    /// </summary>
    /// <summary>Texte cherché tel quel dans un motif LIKE (caractère d'échappement par défaut : la barre oblique inverse).</summary>
    private static string Echapper(string texte) => texte.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Journaux complets de plusieurs MEP, en une seule requête (du plus récent au plus ancien pour chacune).</summary>
    public static async Task<ILookup<Guid, EvenementDto>> Journaux(MepDbContext db, IReadOnlyCollection<Guid> ids) =>
        (await (from e in db.Journal.AsNoTracking().Where(e => ids.Contains(e.MepId)).OrderByDescending(e => e.Id)
                join u in db.Utilisateurs.AsNoTracking() on e.UtilisateurId equals (Guid?)u.Id into comptes
                from u in comptes.DefaultIfEmpty()
                select new { e.MepId, Dto = new EvenementDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail, u == null ? null : u.Login, e.EtapeId) })
            .ToListAsync()).ToLookup(x => x.MepId, x => x.Dto);

    public static async Task<List<EvenementDto>> Journal(MepDbContext db, Guid id, FiltreJournal f)
    {
        if (f.Limite is < 1 or > FiltreJournal.LimiteMax) throw ErreurHttp.Saisie($"« limite » doit être comprise entre 1 et {FiltreJournal.LimiteMax}.");
        if (!await db.Meps.AnyAsync(m => m.Id == id)) throw ErreurHttp.NonTrouve("MEP introuvable.");
        var q = db.Journal.AsNoTracking().Where(e => e.MepId == id);
        if (f.Apres is { } apres) q = q.Where(e => e.Id > apres);
        if (f.Avant is { } avant) q = q.Where(e => e.Id < avant);
        var famille = (f.Type ?? "").Trim();
        if (famille.Length > 0 && famille != "tous")
        {
            // Famille de l'onglet Journal (VuesExcel.Famille) : le type lui-même ou « famille.… » pour chacun de ses préfixes.
            var motifs = VuesExcel.PrefixesDeFamille(famille).Select(Echapper).SelectMany(x => new[] { x, x + ".%" }).ToArray();
            q = q.Where(e => motifs.Any(x => EF.Functions.Like(e.Type, x)));
        }
        var texte = (f.Recherche ?? "").Trim();
        if (texte.Length > 0)
        {
            // Sans casse (comme l'onglet Journal) ; LIKE échappé : « % » et « _ » se cherchent tels quels.
            var motif = "%" + Echapper(texte) + "%";
            q = q.Where(e => EF.Functions.ILike(e.Detail, motif) || EF.Functions.ILike(e.Auteur, motif)
                || db.Utilisateurs.Any(u => u.Id == e.UtilisateurId && EF.Functions.ILike(u.Login, motif)));
        }
        var lignes = from e in q.OrderByDescending(e => e.Id)
                     join u in db.Utilisateurs.AsNoTracking() on e.UtilisateurId equals (Guid?)u.Id into comptes
                     from u in comptes.DefaultIfEmpty()
                     select new EvenementDto(e.Id, e.Quand, e.Auteur, e.Type, e.Detail, u == null ? null : u.Login, e.EtapeId);
        return await (f.Limite is { } n ? lignes.Take(n) : lignes).ToListAsync();
    }

    /// <summary>
    /// Copie d'une MEP ou d'un modèle (<see cref="Regles.Dupliquer"/>) confiée à <paramref name="proprietaire"/>, qui doit pouvoir
    /// l'éditer : il en devient participant et responsable technique s'il n'y figure pas. <paramref name="auteur"/> signe le
    /// journal (la ligne de commande crée une copie pour un compte). Partagé avec « gestion-mep meps dupliquer / importer ».
    /// </summary>
    public static async Task<MepDto> Dupliquer(ServiceMep svc, MepDbContext db, Mep source, bool commeModele, Acteur auteur, Acteur proprietaire,
        string detail, string? outil = null)
    {
        var copie = Regles.Dupliquer(source, commeModele, proprietaire.Id, svc.Maintenant);
        if (outil is not null) copie.Outil = outil;
        if (!Regles.PeutEditerContenu(copie, proprietaire.Id, proprietaire.EstAdmin))
        {
            var duplicateur = new Participant { MepId = copie.Id, Nom = proprietaire.Nom, Role = "Responsable technique", UtilisateurId = proprietaire.Id };
            copie.Participants.Add(duplicateur);
            copie.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = duplicateur.Id });
        }
        db.Meps.Add(copie);
        return await svc.Enregistrer(copie, auteur, "mep.creee", detail);
    }

    public static void MapMeps(this RouteGroupBuilder api)
    {
        // ?modele=true : modèles seulement ; ?modele=false : MEP seulement ; absent : tout. Autres paramètres : recherche
        // avancée (FiltreMeps : systeme, environnement, equipe, personne, role, etat, phase, type, du, au, echec, recherche).
        api.MapGet("/meps", async (bool? modele, [AsParameters] ParametresFiltreMeps filtres, MepDbContext db) =>
            (await Rechercher(db, modele, FiltreMeps.Lire(filtres))).AsEnumerable());

        // Liste paginée (#96) : mêmes filtres, ?limite= (30 par défaut, 200 au plus) et ?apres= (curseur « suivant » de la page
        // précédente) ; le navigateur charge les pages suivantes au fil de la lecture (« Afficher plus »).
        api.MapGet("/meps/page", async (bool? modele, int? limite, string? apres, [AsParameters] ParametresFiltreMeps filtres, MepDbContext db) =>
            await Page(db, modele, FiltreMeps.Lire(filtres), limite, apres));

        // Comptes de la barre de filtres (#96) : pour chaque valeur d'un critère, le nombre de MEP qu'on obtiendrait en la
        // choisissant, avec les autres critères en cours ; et, pour chaque critère actif, le nombre sans lui.
        api.MapGet("/meps/facettes", async (bool? modele, [AsParameters] ParametresFiltreMeps filtres, MepDbContext db) =>
            FiltreMeps.Lire(filtres).Facettes(await Lister(db, modele)));

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
            var detail = source.EstModele && !corps.CommeModele
                ? $"MEP créée depuis le modèle « {source.Outil} »"
                : $"MEP créée par duplication de « {source.Outil} »";
            var dto = await Dupliquer(svc, db, source, corps.CommeModele, acteur, acteur, detail);
            return Results.Created($"/api/meps/{dto.Id}", dto);
        });

        api.MapGet("/meps/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceMep svc) =>
            Results.Ok(await svc.VersDto(await svc.Charger(id), Acteur.Depuis(user).Id)));

        api.MapPut("/meps/{id:guid}", async (Guid id, MepIdentiteSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, Acteur.Depuis(user));
            ServiceMep.ExigerRevision(mep, BlocMep.Identite, corps.Revision, corps.Version);

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
                if (corps.PiloteId != mep.PiloteId && comptePilote is { } cp && mep.Approbations.Count > 0)
                {
                    // Comptes reliés compris : la même personne ne pilote pas une MEP qu'elle approuve.
                    var annuaire = await Personnes.Charger(db, avecEquipes: false);
                    if (mep.Approbations.FirstOrDefault(a => annuaire.Personne(a.UtilisateurId) == annuaire.Personne(cp)) is { } approbateur)
                        throw new RegleMetierException($"{approbateur.Nom} est approbateur : le retirer des approbateurs avant d'en faire le pilote.");
                }
                foreach (var m in modifs) m.Appliquer();
            }

            // L'échéance de retour arrière se modifie par PUT /meps/{id}/echeance (motif, ancienne valeur au journal).
            var noms = modifs.Select(m => m.Nom).ToList();
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

        // Échéance de retour arrière (#94) : en préparation tout éditeur, ensuite pilote, suppléant ou administrateur ; motif obligatoire (sauf première saisie en
        // préparation), ancienne et nouvelle valeur au journal, dans le fuseau de la MEP ; refusée une fois la MEP terminée.
        api.MapPut("/meps/{id:guid}/echeance", async (Guid id, EcheanceSaisie corps, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            // En préparation, l'échéance fait partie du plan (tout éditeur) ; après le Go, elle est au pilote.
            if (mep.Phase == Phase.Preparation) ServiceMep.ExigerEditeur(mep, acteur);
            else ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seul le pilote, un suppléant ou un administrateur peut modifier l'échéance de retour arrière.");
            ServiceMep.ExigerRevision(mep, BlocMep.Echeance, corps.Revision, corps.Version);
            var motif = ServiceMep.Texte(corps.Motif);
            var nouvelle = corps.EcheanceRepli?.ToUniversalTime();
            var ancienne = Regles.ModifierEcheance(mep, nouvelle, motif, svc.Maintenant);
            if (ancienne == nouvelle) return await svc.VersDto(mep, acteur.Id);
            var evenements = new List<ServiceMep.Evenement> { new("echeance.modifiee", Regles.DetailEcheance(ancienne, nouvelle, mep.FuseauHoraire, motif)) };
            // En préparation, les approbations sont à refaire ; la procédure, elle, n'a pas changé : sa relecture reste acquise.
            return await svc.EnregistrerContenu(mep, acteur, evenements, relecture: false);
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
            ServiceMep.ExigerRevision(mep, BlocMep.Sauvegarde, corps.Revision, corps.Version);
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

        // ?limite= (500 au plus), ?apres=<id> (nouveaux événements seulement), ?avant=<id> (page plus ancienne), ?type=, ?recherche=.
        api.MapGet("/meps/{id:guid}/journal", (Guid id, [AsParameters] FiltreJournal filtre, MepDbContext db) => Journal(db, id, filtre));

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
                        if (envoi.Appels)
                            await ctx.Response.WriteAsync("event: appels\ndata: {}\n\n", ct);
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
