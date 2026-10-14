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

    /// <summary>Événement de journal ; <c>Systeme</c> l'attribue à « Système » plutôt qu'à l'utilisateur courant.</summary>
    public record Evenement(string Type, string Detail, bool Systeme = false);

    public DateTimeOffset Maintenant => temps.GetUtcNow();

    /// <summary>
    /// Charge la MEP après avoir appliqué l'échéance de retour arrière : si elle est dépassée, la MEP bascule
    /// (enregistrement, journal « Système » et notification) avant toute lecture ou écriture.
    /// </summary>
    public async Task<Mep> Charger(Guid id)
    {
        for (var essai = 0; ; essai++)
        {
            var mep = await db.Meps
                .Include(m => m.Participants).Include(m => m.Systemes).Include(m => m.ObjetsImpactes).Include(m => m.Etapes).Include(m => m.Resolutions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id)
                ?? throw ErreurHttp.NonTrouve("MEP introuvable.");
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
    /// Nombre d'essais d'une écriture sur une MEP avant de renvoyer 409 (voir <see cref="Modifier{T}"/>). Chaque échec correspond
    /// au succès d'une autre écriture : N écritures simultanées aboutissent toutes tant que N ≤ ce nombre (une équipe d'exécution).
    /// </summary>
    public const int EssaisEcriture = 5;

    /// <summary>
    /// Exécute une écriture sur une MEP avec réessai automatique. <paramref name="action"/> reçoit la MEP fraîchement chargée
    /// (<see cref="Charger"/>), revérifie toutes ses préconditions (droits, révision de l'étape, version envoyée par le client,
    /// règles du domaine), applique la modification et l'enregistre. Si l'enregistrement échoue uniquement parce qu'une autre
    /// écriture vient de passer sur la même MEP (jeton de concurrence <c>Mep.Version</c>), le suivi est vidé, la MEP relue et
    /// l'action rejouée entièrement, jusqu'à <see cref="EssaisEcriture"/> essais, puis 409 comme avant.
    /// <para>
    /// L'action doit donc pouvoir être rejouée : ce qu'elle ajoute au contexte (journal, entités) est annulé par
    /// <c>ChangeTracker.Clear()</c>, et la notification n'a lieu qu'après un enregistrement réussi. Les effets hors base
    /// (écriture ou suppression de fichier) se font avant ou après l'appel, jamais dans l'action.
    /// </para>
    /// <para>
    /// Un vrai conflit reste refusé : au réessai, une étape modifiée entre-temps ne correspond plus à la révision envoyée par
    /// le client (<see cref="ExigerRevision"/>), une MEP modifiée ne correspond plus à sa version (<see cref="ExigerVersion"/>).
    /// </para>
    /// </summary>
    public async Task<T> Modifier<T>(Guid id, Func<Mep, Task<T>> action)
    {
        for (var essai = 1; ; essai++)
        {
            // Chaque essai repart d'un suivi vide : sinon EF renverrait les entités déjà suivies, avec leurs valeurs périmées.
            db.ChangeTracker.Clear();
            var mep = await Charger(id);
            try
            {
                return await action(mep);
            }
            catch (Exception ex) when (essai < EssaisEcriture && EstEcritureSimultanee(ex))
            {
                // Une autre écriture est passée entre la lecture et l'enregistrement : on relit et on rejoue après un court délai
                // aléatoire, pour que les requêtes en concurrence ne se retrouvent pas de nouveau au même instant.
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(5, 40));
            }
        }
    }

    /// <summary>
    /// Échec dû uniquement à une écriture simultanée : jeton de concurrence (<c>Mep.Version</c>), ou clé unique prise entre-temps
    /// (ex. code d'étape attribué deux fois). Les préconditions sont de toute façon revérifiées sur les données fraîches.
    /// </summary>
    private static bool EstEcritureSimultanee(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    /// <summary>Bascule la MEP en retour arrière si son échéance est atteinte. Renvoie vrai si elle a basculé.</summary>
    public async Task<bool> AppliquerEcheance(Mep mep)
    {
        if (!Regles.AppliquerEcheance(mep, Maintenant)) return false;
        mep.Version++;
        db.Journal.Add(NouvelEvenement(mep, null, AuteurSysteme, "repli.declenche", DetailRetourArriere(mep, null)));
        await db.SaveChangesAsync();
        diffuseur.Notifier(mep.Id, mep.Version);
        return true;
    }

    /// <summary>« Retour arrière déclenché par Alice : motif » ou « … automatiquement : motif ».</summary>
    public static string DetailRetourArriere(Mep mep, string? nomAuteur) =>
        nomAuteur is null
            ? $"Retour arrière déclenché automatiquement : {mep.RetourArriereMotif}"
            : $"Retour arrière déclenché par {nomAuteur} : {mep.RetourArriereMotif}";

    private EvenementJournal NouvelEvenement(Mep mep, Guid? utilisateurId, string auteur, string type, string detail) => new()
    {
        MepId = mep.Id, Quand = Maintenant, UtilisateurId = utilisateurId, Auteur = auteur, Type = type, Detail = detail,
    };

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
    public Task<MepDto> Enregistrer(Mep mep, Acteur acteur, string type, string detail) =>
        Enregistrer(mep, acteur, [new Evenement(type, detail)]);

    /// <summary>
    /// Enregistre une modification du contenu de la MEP : en préparation, les décisions d'approbation sont remises à zéro
    /// (<see cref="Regles.ReinitialiserApprobations"/>) et, s'il y en avait, le journal l'indique.
    /// </summary>
    public Task<MepDto> EnregistrerContenu(Mep mep, Acteur acteur, string type, string detail) =>
        EnregistrerContenu(mep, acteur, [new Evenement(type, detail)]);

    /// <summary>Comme ci-dessus, avec plusieurs événements de journal pour une seule version.</summary>
    public Task<MepDto> EnregistrerContenu(Mep mep, Acteur acteur, List<Evenement> evenements)
    {
        if (Regles.ReinitialiserApprobations(mep))
            evenements.Add(new("approbations.reinitialisees", "Approbations à refaire : la MEP a été modifiée"));
        return Enregistrer(mep, acteur, evenements);
    }

    /// <summary>Comme ci-dessus, avec plusieurs événements de journal pour une seule version (ex. résolution en échec puis retour arrière).</summary>
    public async Task<MepDto> Enregistrer(Mep mep, Acteur acteur, IEnumerable<Evenement> evenements)
    {
        mep.Version++;
        foreach (var ev in evenements)
            db.Journal.Add(ev.Systeme
                ? NouvelEvenement(mep, null, AuteurSysteme, ev.Type, ev.Detail)
                : NouvelEvenement(mep, acteur.Id, acteur.Nom, ev.Type, ev.Detail));
        var nouveaux = Regles.NouveauxElementsBibliotheque(mep);
        await db.SaveChangesAsync();
        // Après l'enregistrement réussi seulement : une écriture rejouée (voir Modifier) ne compte pas deux fois.
        if (nouveaux.Count > 0) await CompterUtilisations(nouveaux);
        diffuseur.Notifier(mep.Id, mep.Version);
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

    public async Task<MepDto> VersDto(Mep mep, Guid? utilisateurId)
    {
        var noms = await Personnes.Noms(db);
        var pieces = await db.PiecesJointes.AsNoTracking().Where(p => p.MepId == mep.Id).ToListAsync();
        var estAdmin = utilisateurId is { } u && await db.Utilisateurs.AsNoTracking().AnyAsync(x => x.Id == u && x.EstAdmin);
        return MepMapping.VersDto(mep, utilisateurId, noms, Maintenant, await ApprobationsMinimum(), pieces, estAdmin);
    }

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
