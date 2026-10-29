using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>Chargement d'une MEP, contrôle de version, journal d'audit, enregistrement et notification temps réel.</summary>
public class ServiceMep(MepDbContext db, TimeProvider temps, DiffuseurMep diffuseur)
{
    public const string AuteurSysteme = "Système";

    /// <summary>
    /// Événement de journal ; <c>Systeme</c> l'attribue à « Système » plutôt qu'à l'utilisateur courant ; <c>EtapeId</c> : étape
    /// concernée (événements d'exécution), enregistrée à part pour que rien ne se relise dans le texte du détail.
    /// </summary>
    public record Evenement(string Type, string Detail, bool Systeme = false, Guid? EtapeId = null);

    public DateTimeOffset Maintenant => temps.GetUtcNow();

    /// <summary>
    /// Ce qui a été relevé à la lecture d'une MEP : empreintes de ses blocs de formulaire (révisions, <see cref="BlocsMep"/>)
    /// et de ce qu'en montre le suivi en direct (portée de la notification, <see cref="DiffuseurMep.PorteeSuivi"/>).
    /// </summary>
    private sealed record Releve(BlocsMep.Empreintes Blocs, bool EnCours, string Suivi);
    private readonly Dictionary<Guid, Releve> _releves = [];

    /// <summary>Notifications retenues pendant une écriture (<see cref="Modifier{T}"/>) : envoyées une fois la transaction validée.</summary>
    private List<(Guid Id, int Version, DiffuseurMep.PorteeSuivi Portee)>? _enAttente;

    private static readonly Phase[] PhasesEnCours = [Phase.Execution, Phase.Recette, Phase.RetourArriere];

    /// <summary>MEP lancée (exécution, recette ou retour arrière), hors modèles : une carte du suivi en direct.</summary>
    public static bool EstEnCours(Mep m) => !m.EstModele && PhasesEnCours.Contains(m.Phase);

    /// <summary>Ce que les listes du suivi (MEP annoncées, clôturées) montrent d'une MEP qui n'est pas lancée.</summary>
    private static string EmpreinteSuivi(Mep m) => System.Text.Json.JsonSerializer.Serialize(new
    {
        m.Phase, m.EstModele, m.Outil, m.VersionCible, m.Type, m.Issue, m.ClotureeLe, Echecs = m.EchecsEtapes + m.EchecsRecette,
        Pilote = m.Participants.FirstOrDefault(p => p.Id == m.PiloteId)?.Nom, Reportee = m.Report is not null,
        m.Communication.DateAnnoncee, m.Communication.HeureDebutAnnoncee, m.FuseauHoraire,
    });

    private void Relever(Mep mep) => _releves[mep.Id] = new Releve(BlocsMep.Relever(mep), EstEnCours(mep), EmpreinteSuivi(mep));

    /// <summary>
    /// Avant d'enregistrer : augmente la révision des blocs modifiés depuis la lecture, puis dit ce que le suivi en direct
    /// doit relire (la carte de cette MEP, toutes ses listes, ou rien).
    /// </summary>
    private DiffuseurMep.PorteeSuivi Reviser(Mep mep)
    {
        var avant = _releves.GetValueOrDefault(mep.Id);
        var blocs = BlocsMep.Reviser(mep, avant?.Blocs);
        var apres = new Releve(blocs, EstEnCours(mep), EmpreinteSuivi(mep));
        _releves[mep.Id] = apres;
        if (avant is null) return DiffuseurMep.PorteeSuivi.Liste;
        if (avant.EnCours && apres.EnCours && avant.Suivi == apres.Suivi) return DiffuseurMep.PorteeSuivi.Carte;
        if (avant.EnCours != apres.EnCours || avant.Suivi != apres.Suivi) return DiffuseurMep.PorteeSuivi.Liste;
        return DiffuseurMep.PorteeSuivi.Aucune;
    }

    /// <summary>Notifie les flux de la MEP et du suivi, ou retient la notification jusqu'à la fin de l'écriture en cours.</summary>
    private void Notifier(Guid id, int version, DiffuseurMep.PorteeSuivi portee)
    {
        if (_enAttente is not null) _enAttente.Add((id, version, portee));
        else diffuseur.Notifier(id, version, portee);
    }

    private IQueryable<Mep> MepsCompletes() => db.Meps
        .Include(m => m.Participants).Include(m => m.Systemes).Include(m => m.ObjetsImpactes).Include(m => m.Etapes).Include(m => m.Resolutions)
        .AsSplitQuery();

    /// <summary>
    /// Charge la MEP après avoir appliqué l'échéance de retour arrière : si elle est dépassée, la MEP bascule
    /// (enregistrement, journal « Système » et notification) avant toute lecture ou écriture.
    /// </summary>
    public async Task<Mep> Charger(Guid id)
    {
        for (var essai = 0; ; essai++)
        {
            var mep = await MepsCompletes().FirstOrDefaultAsync(m => m.Id == id) ?? throw ErreurHttp.NonTrouve("MEP introuvable.");
            Relever(mep);
            try
            {
                await AppliquerEcheance(mep);
                return mep;
            }
            catch (DbUpdateConcurrencyException) when (essai < EssaisEcriture - 1)
            {
                // Quelqu'un a modifié la MEP (ou appliqué l'échéance) entre-temps : on relit (même nombre d'essais que Modifier).
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Charge plusieurs MEP complètes en une seule série de requêtes (suivi en direct), dans l'ordre des identifiants, échéances
    /// appliquées. Les MEP disparues entre-temps sont omises.
    /// </summary>
    public async Task<List<Mep>> ChargerPlusieurs(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0) return [];
        db.ChangeTracker.Clear();
        var parId = (await MepsCompletes().Where(m => ids.Contains(m.Id)).ToListAsync()).ToDictionary(m => m.Id);
        var meps = ids.Where(parId.ContainsKey).Select(i => parId[i]).ToList();
        foreach (var m in meps) Relever(m);
        try
        {
            // Rien n'est écrit tant qu'aucune échéance n'est atteinte (SurveillanceEcheances les applique déjà en continu).
            foreach (var m in meps) await AppliquerEcheance(m);
            return meps;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Une écriture est passée entre-temps : relecture une par une, avec réessai.
            db.ChangeTracker.Clear();
            var relues = new List<Mep>();
            foreach (var i in ids)
            {
                try { relues.Add(await Charger(i)); }
                catch (ErreurHttp e) when (e.Statut == StatusCodes.Status404NotFound) { /* supprimée entre-temps */ }
            }
            return relues;
        }
    }

    /// <summary>
    /// Nombre d'essais d'une écriture sur une MEP avant de renvoyer 409 (voir <see cref="Modifier{T}"/>). Les écritures sur une
    /// même MEP passent l'une après l'autre (verrou de la ligne) : un nouvel essai ne sert plus qu'aux écritures faites hors de
    /// <see cref="Modifier{T}"/> (échéance appliquée à la lecture) ou à une impasse signalée par la base.
    /// </summary>
    public const int EssaisEcriture = 8;

    /// <summary>Attente maximale du verrou d'une MEP avant de répondre 409 (une écriture normale le garde quelques millisecondes).</summary>
    private const string AttenteVerrou = "SET LOCAL lock_timeout = '15s'";

    /// <summary>
    /// Exécute une écriture sur une MEP. Les écritures sur une même MEP passent l'une après l'autre : chacune ouvre une
    /// transaction, verrouille la ligne de la MEP (<c>SELECT … FOR UPDATE</c>), puis <paramref name="action"/> reçoit la MEP
    /// fraîchement chargée (<see cref="Charger"/>), revérifie toutes ses préconditions (droits, révision de l'étape ou du bloc,
    /// règles du domaine), applique la modification et l'enregistre. Dix personnes qui écrivent en même temps sur des étapes
    /// différentes aboutissent donc toutes, sans conflit ni réessai ; deux personnes qui modifient la même étape (ou le même
    /// bloc) avec la même révision : la seconde reçoit 409.
    /// <para>
    /// Les notifications temps réel partent une fois la transaction validée. Si l'enregistrement échoue seulement à cause
    /// d'une écriture faite hors de ce verrou (jeton de concurrence <c>Mep.Version</c>, impasse), le suivi est vidé, la MEP
    /// relue et l'action rejouée, jusqu'à <see cref="EssaisEcriture"/> essais. L'action doit donc pouvoir être rejouée : ce
    /// qu'elle ajoute au contexte est annulé par <c>ChangeTracker.Clear()</c> et la transaction ; les effets hors base (écriture
    /// ou suppression de fichier) se font avant ou après l'appel, jamais dans l'action.
    /// </para>
    /// </summary>
    public async Task<T> Modifier<T>(Guid id, Func<Mep, Task<T>> action)
    {
        for (var essai = 1; ; essai++)
        {
            // Chaque essai repart d'un suivi vide : sinon EF renverrait les entités déjà suivies, avec leurs valeurs périmées.
            db.ChangeTracker.Clear();
            var notifications = new List<(Guid Id, int Version, DiffuseurMep.PorteeSuivi Portee)>();
            try
            {
                T resultat;
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    _enAttente = notifications;
                    await Verrouiller(id);
                    var mep = await Charger(id);
                    resultat = await action(mep);
                    await transaction.CommitAsync();
                }
                _enAttente = null;
                foreach (var n in notifications) diffuseur.Notifier(n.Id, n.Version, n.Portee);
                return resultat;
            }
            catch (Exception ex) when (essai < EssaisEcriture && EstEcritureSimultanee(ex))
            {
                // Écriture passée hors verrou entre la lecture et l'enregistrement : on relit et on rejoue après une attente
                // aléatoire qui double à chaque essai, pour que les requêtes en concurrence ne se retrouvent pas au même instant.
                _enAttente = null;
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(5, 20) << Math.Min(essai - 1, 5));
            }
            catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }
                || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } })
            {
                _enAttente = null;
                db.ChangeTracker.Clear();
                throw ErreurHttp.Conflit("La MEP est en cours de modification par quelqu'un d'autre depuis trop longtemps : réessayez dans un instant.");
            }
            finally
            {
                _enAttente = null;
            }
        }
    }

    /// <summary>Verrouille la ligne de la MEP jusqu'à la fin de la transaction (rien si elle n'existe pas : <see cref="Charger"/> répond 404).</summary>
    private async Task Verrouiller(Guid id)
    {
        await db.Database.ExecuteSqlRawAsync(AttenteVerrou);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Meps\" WHERE \"Id\" = {id} FOR UPDATE");
    }

    /// <summary>
    /// Échec dû uniquement à une écriture simultanée : jeton de concurrence (<c>Mep.Version</c>), clé unique prise entre-temps
    /// (ex. code d'étape attribué deux fois), impasse ou sérialisation refusée par la base. Les préconditions sont de toute
    /// façon revérifiées sur les données fraîches.
    /// </summary>
    private static bool EstEcritureSimultanee(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } }
        || ex is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure };

    /// <summary>
    /// Échéance atteinte : pendant l'exécution, la MEP bascule en retour arrière ; pendant la recette, l'échéance est seulement
    /// signalée (journal « Système », une fois) et le pilote décide. Renvoie vrai si la MEP a basculé en retour arrière.
    /// </summary>
    public async Task<bool> AppliquerEcheance(Mep mep)
    {
        EvenementJournal evenement;
        var bascule = Regles.AppliquerEcheance(mep, Maintenant);
        if (bascule) evenement = NouvelEvenement(mep, null, AuteurSysteme, "repli.declenche", DetailRetourArriere(mep, null));
        else if (Regles.SignalerEcheance(mep, Maintenant)) evenement = NouvelEvenement(mep, null, AuteurSysteme, "echeance.depassee", Regles.DetailEcheanceSignalee);
        else return false;
        mep.Version++;
        var portee = Reviser(mep);
        db.Journal.Add(evenement);
        await db.SaveChangesAsync();
        Notifier(mep.Id, mep.Version, portee);
        return bascule;
    }

    /// <summary>« Retour arrière déclenché par Alice : motif » ou « … automatiquement : motif ».</summary>
    public static string DetailRetourArriere(Mep mep, string? nomAuteur) =>
        nomAuteur is null
            ? $"Retour arrière déclenché automatiquement : {mep.RetourArriereMotif}"
            : $"Retour arrière déclenché par {nomAuteur} : {mep.RetourArriereMotif}";

    private EvenementJournal NouvelEvenement(Mep mep, Guid? utilisateurId, string auteur, string type, string detail, Guid? etapeId = null) => new()
    {
        MepId = mep.Id, Quand = Maintenant, UtilisateurId = utilisateurId, Auteur = auteur, Type = type, Detail = detail, EtapeId = etapeId,
    };

    /// <summary>
    /// Contrôle d'un formulaire de section : <paramref name="revisionClient"/>, la révision du bloc lue (une écriture sur un
    /// autre bloc, une étape ou une section ne la périme pas) ; à défaut, <paramref name="versionClient"/>, la version de toute
    /// la MEP (clients existants). 400 sans l'une ni l'autre, 409 si elle est périmée.
    /// </summary>
    public static void ExigerRevision(Mep mep, BlocMep bloc, int? revisionClient, int? versionClient)
    {
        if (revisionClient is null)
        {
            if (versionClient is null) throw ErreurHttp.Saisie("La révision de la section (revision) est obligatoire.");
            ExigerVersion(mep, versionClient);
            return;
        }
        if (revisionClient != BlocsMep.Revision(mep, bloc))
            throw ErreurHttp.Conflit($"« {BlocsMep.Libelle(bloc)} » a été modifié par quelqu'un d'autre depuis votre dernière lecture : "
                + "rechargez la MEP, vérifiez ce qui a changé, puis recommencez.");
    }

    /// <summary>Comme ci-dessus, pour la sauvegarde d'un système de la MEP (<see cref="Systeme.RevisionSauvegarde"/>).</summary>
    public static void ExigerRevision(Mep mep, Systeme systeme, int? revisionClient, int? versionClient)
    {
        if (revisionClient is null)
        {
            if (versionClient is null) throw ErreurHttp.Saisie("La révision de la sauvegarde du système (revision) est obligatoire.");
            ExigerVersion(mep, versionClient);
            return;
        }
        if (revisionClient != systeme.RevisionSauvegarde)
            throw ErreurHttp.Conflit($"La sauvegarde de {systeme.Nom} a été modifiée par quelqu'un d'autre depuis votre dernière lecture : "
                + "rechargez la MEP, vérifiez ce qui a changé, puis recommencez.");
    }

    public static void ExigerVersion(Mep mep, int? versionClient)
    {
        if (versionClient is null) throw ErreurHttp.Saisie("La version de la MEP est obligatoire.");
        if (versionClient != mep.Version)
            throw ErreurHttp.Conflit("La MEP a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
    }

    public static Etape TrouverEtape(Mep mep, Guid etapeId) =>
        mep.Etapes.FirstOrDefault(e => e.Id == etapeId) ?? throw ErreurHttp.NonTrouve("Étape introuvable.");

    public static void ExigerRevision(Etape etape, int? revisionClient)
    {
        if (revisionClient is null) throw ErreurHttp.Saisie("La révision de l'étape est obligatoire.");
        if (revisionClient != etape.Revision)
            throw ErreurHttp.Conflit($"{etape.Code} a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
    }

    /// <summary>Incrémente la version, ajoute l'événement au journal, sauvegarde puis notifie les abonnés du flux.</summary>
    public Task<MepDto> Enregistrer(Mep mep, Acteur acteur, string type, string detail, Guid? etapeId = null) =>
        Enregistrer(mep, acteur, [new Evenement(type, detail, EtapeId: etapeId)]);

    /// <summary>
    /// Enregistre une modification du contenu de la MEP : en préparation, les décisions d'approbation sont remises à zéro
    /// (<see cref="Regles.ReinitialiserApprobations"/>), ainsi que la relecture de la procédure
    /// (<see cref="Regles.ReinitialiserRelecture"/>), et, s'il y en avait, le journal l'indique.
    /// </summary>
    public Task<MepDto> EnregistrerContenu(Mep mep, Acteur acteur, string type, string detail) =>
        EnregistrerContenu(mep, acteur, [new Evenement(type, detail)]);

    /// <summary>
    /// Comme ci-dessus, avec plusieurs événements de journal pour une seule version. <paramref name="relecture"/> : false quand
    /// le plan lui-même ne change pas (liste des approbateurs, échéance) : la relecture de la procédure reste acquise.
    /// </summary>
    public Task<MepDto> EnregistrerContenu(Mep mep, Acteur acteur, List<Evenement> evenements, bool relecture = true)
    {
        if (Regles.ReinitialiserApprobations(mep))
            evenements.Add(new("approbations.reinitialisees", "Approbations à refaire : la MEP a été modifiée"));
        if (relecture && Regles.ReinitialiserRelecture(mep))
            evenements.Add(new("procedure.reinitialisee", "Relecture de la procédure à refaire : la MEP a été modifiée"));
        return Enregistrer(mep, acteur, evenements);
    }

    /// <summary>Comme ci-dessus, avec plusieurs événements de journal pour une seule version (ex. résolution en échec puis retour arrière).</summary>
    public async Task<MepDto> Enregistrer(Mep mep, Acteur acteur, IEnumerable<Evenement> evenements)
    {
        mep.Version++;
        foreach (var ev in evenements)
            db.Journal.Add(ev.Systeme
                ? NouvelEvenement(mep, null, AuteurSysteme, ev.Type, ev.Detail, ev.EtapeId)
                : NouvelEvenement(mep, acteur.Compte, acteur.Nom, ev.Type, ev.Detail, ev.EtapeId));
        var nouveaux = Regles.NouveauxElementsBibliotheque(mep);
        var portee = Reviser(mep);
        await db.SaveChangesAsync();
        // Après l'enregistrement réussi seulement : une écriture rejouée (voir Modifier) ne compte pas deux fois.
        if (nouveaux.Count > 0) await CompterUtilisations(nouveaux);
        Notifier(mep.Id, mep.Version, portee);
        return await VersDto(mep, acteur.Id);
    }

    /// <summary>
    /// Ajoute une utilisation aux éléments des bibliothèques donnés (étapes modèles, modèles de recette, résolutions types),
    /// par une mise à jour atomique en base : deux MEP qui utilisent le même élément au même moment comptent toutes les deux.
    /// </summary>
    private async Task CompterUtilisations(List<Guid> ids)
    {
        await db.ModelesEtape.Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.Utilisations, m => m.Utilisations + 1));
        await db.ModelesRecette.Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.Utilisations, m => m.Utilisations + 1));
        await db.ResolutionsTypes.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(s => s.SetProperty(r => r.Utilisations, r => r.Utilisations + 1));
    }

    public async Task<MepDto> VersDto(Mep mep, Guid? utilisateurId) => (await VersDtos([mep], utilisateurId))[0];

    /// <summary>
    /// MEP complètes vues par <paramref name="utilisateurId"/>, avec ce que la vérification du plan lit hors de la MEP : une
    /// seule série de requêtes, quel que soit le nombre de MEP (suivi en direct).
    /// </summary>
    public async Task<List<MepDto>> VersDtos(IReadOnlyList<Mep> meps, Guid? utilisateurId)
    {
        var liees = meps.ToDictionary(m => m.Id, m => Regles.ResolutionsLiees(m).ToHashSet());
        var ids = liees.Values.SelectMany(x => x).Distinct().ToList();
        var resolutions = ids.Count == 0 ? [] : await db.ResolutionsTypes.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        var minimum = await ApprobationsMinimum();
        var annuaire = await Personnes.Charger(db, avecEquipes: false);
        var noms = annuaire.NomsAffiches();
        var mepIds = meps.Select(m => (Guid?)m.Id).ToList();
        var pieces = (await db.PiecesJointes.AsNoTracking().Where(p => mepIds.Contains(p.MepId)).ToListAsync()).ToLookup(p => p.MepId);
        var estAdmin = utilisateurId is { } u && await db.Utilisateurs.AsNoTracking().AnyAsync(x => x.Id == u && x.EstAdmin);
        return [.. meps.Select(m => MepMapping.VersDto(m, utilisateurId, noms, Maintenant,
            new ContexteVerification(minimum, annuaire, [.. resolutions.Where(r => liees[m.Id].Contains(r.Id))]), [.. pieces[m.Id]], estAdmin))];
    }

    /// <summary>
    /// Ce que la vérification du plan lit hors de la MEP : seuil d'approbations, annuaire des personnes (comptes reliés) et
    /// résolutions types liées aux étapes, telles qu'elles sont dans la bibliothèque.
    /// </summary>
    public async Task<ContexteVerification> Contexte(Mep mep)
    {
        var ids = Regles.ResolutionsLiees(mep);
        var resolutions = ids.Count == 0 ? [] : await db.ResolutionsTypes.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        return new ContexteVerification(await ApprobationsMinimum(), await Personnes.Charger(db, avecEquipes: false), resolutions);
    }

    /// <summary>
    /// Événement « resolutions.figees » à journaliser si l'action vient de figer les résolutions liées (voir
    /// <see cref="Regles.FigerResolutions"/>) ; <paramref name="avant"/> : <see cref="Mep.ResolutionsFigeesLe"/> avant l'action.
    /// </summary>
    public static IEnumerable<Evenement> EvenementsFigees(Mep mep, DateTimeOffset? avant) =>
        avant is null && mep.ResolutionsFigeesLe is not null && Regles.DetailResolutionsFigees(mep) is { } detail
            ? [new Evenement("resolutions.figees", detail)]
            : [];

    /// <summary>Paramètre global <see cref="ParametresGlobaux.ApprobationsMinimum"/> (1 si la ligne manque).</summary>
    public async Task<int> ApprobationsMinimum() =>
        await db.Parametres.AsNoTracking().Where(p => p.Id == ParametresGlobaux.IdUnique).Select(p => (int?)p.ApprobationsMinimum).FirstOrDefaultAsync() ?? 1;

    public async Task<List<CritereGoNoGo>> CriteresGoNoGo() => await db.CriteresGoNoGo.AsNoTracking().OrderBy(c => c.Ordre).ToListAsync();

    // ---------- Contrôles de saisie ----------

    public static string Texte(string? s) => (s ?? "").Trim();

    public static Participant TrouverParticipant(Mep mep, Guid? id, string champ) =>
        mep.Participants.FirstOrDefault(p => p.Id == id) ?? throw ErreurHttp.Saisie($"{champ} : participant inconnu dans cette MEP.");

    public static void ExigerSystemeExistant(Mep mep, Guid? id)
    {
        if (id is not null && mep.Systemes.All(s => s.Id != id)) throw ErreurHttp.Saisie("Système inconnu dans cette MEP.");
    }

    /// <summary>Systèmes de la MEP (400 sinon), sans doublon, dans l'ordre reçu.</summary>
    public static List<Guid> ValiderSystemeIds(Mep mep, IEnumerable<Guid>? ids)
    {
        var liste = (ids ?? []).Distinct().ToList();
        foreach (var id in liste) ExigerSystemeExistant(mep, id);
        return liste;
    }

    /// <summary>Vérifie que les résolutions types existent et sont actives (400 sinon) ; celles déjà liées à l'étape restent admises.</summary>
    public async Task<List<Guid>> ValiderResolutionIds(IEnumerable<Guid>? ids, IEnumerable<Guid> dejaLiees)
    {
        var demandees = (ids ?? []).Distinct().ToList();
        if (demandees.Count == 0) return demandees;
        var actives = await db.ResolutionsTypes.AsNoTracking().Where(r => demandees.Contains(r.Id)).Select(r => new { r.Id, r.Actif }).ToListAsync();
        var conservees = dejaLiees.ToHashSet();
        foreach (var id in demandees)
            if (!actives.Any(r => r.Id == id && (r.Actif || conservees.Contains(id))))
                throw ErreurHttp.Saisie("Résolution type inconnue ou désactivée.");
        return demandees;
    }

    public static void ExigerPiloteOuAdmin(Mep mep, Acteur acteur, string message)
    {
        if (!acteur.EstAdmin && !Regles.EstPilote(mep, acteur.Id)) throw ErreurHttp.Interdit(message);
    }

    /// <summary>403 si l'acteur n'est ni responsable technique, ni pilote (ou suppléant), ni administrateur.</summary>
    public static void ExigerEditeur(Mep mep, Acteur acteur)
    {
        if (!Regles.PeutEditerContenu(mep, acteur.Id, acteur.EstAdmin)) throw ErreurHttp.Interdit(Regles.MessageEditionInterdite);
    }

    public async Task ExigerUtilisateurExistant(Guid? utilisateurId)
    {
        if (utilisateurId is not null && !await db.Utilisateurs.AnyAsync(u => u.Id == utilisateurId))
            throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
    }
}
