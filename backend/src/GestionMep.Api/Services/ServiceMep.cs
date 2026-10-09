using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>Chargement d'une MEP, contrôle de version, journal d'audit, enregistrement et notification temps réel.</summary>
public class ServiceMep(MepDbContext db, TimeProvider temps, DiffuseurMep diffuseur)
{
    public DateTimeOffset Maintenant => temps.GetUtcNow();

    public async Task<Mep> Charger(Guid id)
    {
        var mep = await db.Meps
            .Include(m => m.Participants).Include(m => m.Systemes).Include(m => m.Livrables).Include(m => m.Etapes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id);
        return mep ?? throw ErreurHttp.NonTrouve("MEP introuvable.");
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
    public async Task<MepDto> Enregistrer(Mep mep, Acteur acteur, string type, string detail)
    {
        mep.Version++;
        db.Journal.Add(new EvenementJournal
        {
            MepId = mep.Id, Quand = Maintenant, UtilisateurId = acteur.Id, Auteur = acteur.Nom, Type = type, Detail = detail,
        });
        await db.SaveChangesAsync();
        diffuseur.Notifier(mep.Id, mep.Version);
        return await VersDto(mep, acteur.Id);
    }

    public async Task<MepDto> VersDto(Mep mep, Guid? utilisateurId)
    {
        var noms = await db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NomAffiche);
        return MepMapping.VersDto(mep, utilisateurId, noms, Maintenant);
    }

    // ---------- Contrôles de saisie ----------

    public static string Texte(string? s) => (s ?? "").Trim();

    public static Participant TrouverParticipant(Mep mep, Guid? id, string champ) =>
        mep.Participants.FirstOrDefault(p => p.Id == id) ?? throw ErreurHttp.Saisie($"{champ} : participant inconnu dans cette MEP.");

    public static void ExigerSystemeExistant(Mep mep, Guid? id)
    {
        if (id is not null && mep.Systemes.All(s => s.Id != id)) throw ErreurHttp.Saisie("Système inconnu dans cette MEP.");
    }

    public static void ExigerPiloteOuAdmin(Mep mep, Acteur acteur, string message)
    {
        if (!acteur.EstAdmin && !Regles.EstPilote(mep, acteur.Id)) throw ErreurHttp.Interdit(message);
    }

    public async Task ExigerUtilisateurExistant(Guid? utilisateurId)
    {
        if (utilisateurId is not null && !await db.Utilisateurs.AnyAsync(u => u.Id == utilisateurId))
            throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
    }
}
