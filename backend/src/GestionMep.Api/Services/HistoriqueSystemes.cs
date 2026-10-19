using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Historique des MEP par système du catalogue (#59) : toutes les MEP qui l'ont touché, avec leur date, leur issue et les
/// échecs survenus sur ses étapes ; activité de chaque système pour la colonne « Dernière MEP » du catalogue. Partagé par
/// l'API (<c>/api/catalogue/systemes/{id}/historique</c>), l'export Excel et <c>gestion-mep systemes historique</c>.
/// Les systèmes saisis à la main (hors catalogue) n'ont pas d'historique.
/// </summary>
public static class HistoriqueSystemes
{
    /// <summary>Échec d'étape d'une MEP (point bloquant du rapport de fin) et les systèmes de son étape.</summary>
    public sealed record EchecDetaille(Guid MepId, PointBloquantDto Point, IReadOnlyList<(Guid? CatalogueId, string Nom)> Systemes)
    {
        public bool Repris => Point.RepriseReussieLe is not null;
    }

    /// <summary>
    /// Échecs d'étape détaillés des MEP données, par MEP, du plus ancien au plus récent : mêmes règles que le rapport de fin
    /// (<see cref="RapportsFin.Calculer"/>). Seules les MEP qui ont des échecs sont lues en détail.
    /// </summary>
    public static async Task<Dictionary<Guid, List<EchecDetaille>>> Echecs(MepDbContext db, IEnumerable<MepResumeDto> meps)
    {
        var ids = meps.Where(m => m.EchecsSurvenus > 0).Select(m => m.Id).ToList();
        var resultat = new Dictionary<Guid, List<EchecDetaille>>();
        if (ids.Count == 0) return resultat;
        var entites = await db.Meps.AsNoTracking().Include(m => m.Etapes).Include(m => m.Resolutions).Include(m => m.Systemes)
            .AsSplitQuery().Where(m => ids.Contains(m.Id)).ToListAsync();
        // Journaux de toutes ces MEP en une seule requête (et non une par MEP).
        var journaux = await MepsEndpoints.Journaux(db, ids);
        foreach (var mep in entites)
        {
            var (_, points) = RapportsFin.Calculer(mep, [.. journaux[mep.Id]]);
            var systemes = mep.Systemes.ToDictionary(s => s.Id, s => (s.CatalogueId, s.Nom));
            var etapes = mep.Etapes.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First());
            resultat[mep.Id] = [.. points.Select(p => new EchecDetaille(mep.Id, p,
                etapes.TryGetValue(p.Code, out var e) ? [.. e.SystemeIds.Where(systemes.ContainsKey).Select(s => systemes[s])] : []))];
        }
        return resultat;
    }

    private static bool Touche(EchecDetaille e, Guid catalogueId) => e.Systemes.Any(s => s.CatalogueId == catalogueId);

    /// <summary>Dernière MEP lancée (Go le plus récent) ; à défaut, la plus récente.</summary>
    public static MepResumeDto? Derniere(IEnumerable<MepResumeDto> meps)
    {
        var liste = meps.ToList();
        return liste.Where(m => m.Go is not null).MaxBy(m => m.Go) ?? liste.MaxBy(m => (m.Jour, m.CreeLe));
    }

    public static async Task<SystemeCatalogue> Systeme(MepDbContext db, Guid id) =>
        await db.SystemesCatalogue.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id) ?? throw ErreurHttp.NonTrouve("Système introuvable.");

    /// <summary>
    /// Historique d'un système du catalogue : MEP (hors modèles) qui le touchent, la plus récente d'abord, et leurs chiffres.
    /// <paramref name="aujourdhui"/> borne les « 12 derniers mois ».
    /// </summary>
    public static async Task<HistoriqueSystemeDto> Historique(MepDbContext db, Guid systemeId, DateOnly aujourdhui)
    {
        var systeme = await Systeme(db, systemeId);
        var meps = await MepsEndpoints.Rechercher(db, false, new FiltreMeps { Systemes = [systemeId.ToString()] });
        var echecs = await Echecs(db, meps.Where(m => m.Systemes.Any(s => s.CatalogueId == systemeId && s.Echecs > 0)));
        var lignes = meps.Select(m =>
        {
            var ici = m.Systemes.First(s => s.CatalogueId == systemeId);
            var siens = echecs.GetValueOrDefault(m.Id, []).Where(e => Touche(e, systemeId)).Select(e => new EchecSystemeDto(e.Point.Plan,
                e.Point.Code, e.Point.Action, e.Point.EchecLe, e.Point.Motif, e.Repris, e.Point.Resolution, e.Point.StatutResolution)).ToList();
            return new MepHistoriqueDto(m.Id, m.Outil, m.VersionCible, m.Objet, m.Type, m.Etat, m.Jour, m.Go, m.ClotureeLe, m.Pilote,
                ici.Environnements, [.. m.Systemes.Where(s => s != ici)], siens, m.Phase == Phase.Cloturee);
        }).ToList();
        var tous = lignes.SelectMany(l => l.Echecs).ToList();
        var resume = new ResumeHistoriqueDto(lignes.Count, lignes.Count(l => l.Jour > aujourdhui.AddMonths(-12)),
            lignes.Count(l => !Terminee(l.Etat)),
            lignes.Count(l => l.Etat is EtatMep.Reussie or EtatMep.AvecIncidents or EtatMep.Repliee or EtatMep.Echec),
            lignes.Count(l => l.Etat == EtatMep.Reussie), lignes.Count(l => l.Etat == EtatMep.AvecIncidents), lignes.Count(l => l.Etat == EtatMep.Repliee),
            tous.Count, tous.Count(e => e.Repris), Derniere(meps)?.Id,
            lignes.Count(l => l.Etat == EtatMep.Echec), lignes.Count(l => l.Etat == EtatMep.Annulee));
        return new HistoriqueSystemeDto(systeme.Id, systeme.Nom, resume, lignes);
    }

    /// <summary>MEP clôturée (quelle que soit son issue) ou annulée : elle n'est plus en cours.</summary>
    private static bool Terminee(EtatMep e) => e is EtatMep.Reussie or EtatMep.AvecIncidents or EtatMep.Repliee or EtatMep.Echec or EtatMep.Annulee;

    /// <summary>Activité de chaque système du catalogue (même ceux sans MEP) : nombre de MEP et dernière MEP lancée.</summary>
    public static async Task<List<ActiviteSystemeDto>> Activite(MepDbContext db)
    {
        var ids = await db.SystemesCatalogue.AsNoTracking().Select(s => s.Id).ToListAsync();
        var meps = await MepsEndpoints.Lister(db, false);
        return [.. ids.Select(id =>
        {
            var siennes = meps.Where(m => m.Systemes.Any(s => s.CatalogueId == id)).ToList();
            var d = Derniere(siennes);
            return new ActiviteSystemeDto(id, siennes.Count, d?.Id, d?.Outil, d?.VersionCible, d?.Jour, d?.Etat);
        })];
    }
}
