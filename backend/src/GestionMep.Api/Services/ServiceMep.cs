using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

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
                .Include(m => m.Participants).Include(m => m.Systemes).Include(m => m.Livrables).Include(m => m.Etapes).Include(m => m.Resolutions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id)
                ?? throw ErreurHttp.NonTrouve("MEP introuvable.");
            try
            {
                await AppliquerEcheance(mep);
                return mep;
            }
            catch (DbUpdateConcurrencyException) when (essai < 2)
            {
                // Quelqu'un a modifié la MEP (ou appliqué l'échéance) entre-temps : on relit.
                db.ChangeTracker.Clear();
            }
        }
    }

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
    public Task<MepDto> EnregistrerContenu(Mep mep, Acteur acteur, string type, string detail)
    {
        var evenements = new List<Evenement> { new(type, detail) };
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
        await db.SaveChangesAsync();
        diffuseur.Notifier(mep.Id, mep.Version);
        return await VersDto(mep, acteur.Id);
    }

    public async Task<MepDto> VersDto(Mep mep, Guid? utilisateurId)
    {
        var noms = await db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NomAffiche);
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
