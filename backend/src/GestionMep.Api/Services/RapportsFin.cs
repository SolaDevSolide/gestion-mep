using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Services;

/// <summary>
/// Rapports de fin (#61) : instantané figé à la clôture (MEP telle qu'elle s'affichait, journal complet, synthèse et points
/// bloquants calculés), enregistré une fois pour toutes. Une MEP clôturée avant l'existence des rapports obtient le sien à la
/// première demande, figé à son tour. Le rapport n'existe pas avant la clôture (pas d'aperçu provisoire).
/// </summary>
public class RapportsFin(MepDbContext db, ServiceMep svc)
{
    /// <summary>
    /// Version du format du contenu enregistré (<see cref="RapportFin.Format"/>). 2 (#94) : la MEP figée porte ses compteurs
    /// d'incidents (échecs d'étape et de recette) et son état en tient compte ; un rapport au format 1 prend les compteurs de la
    /// MEP (repris du journal une fois pour toutes par la migration).
    /// </summary>
    public const int FormatActuel = 2;

    public const string MessageNonCloturee = "Le rapport de fin existe une fois la MEP clôturée.";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Ce qui est figé : tout le rapport sauf ses métadonnées (MEP, date, origine).</summary>
    private sealed record Contenu(SyntheseRapportDto Synthese, List<PointBloquantDto> PointsBloquants, TestsUnitairesDto TestsUnitaires,
        MepDto Mep, List<EvenementDto> Journal);

    /// <summary>Rapport de la MEP : celui figé, sinon (MEP clôturée avant les rapports) calculé et figé maintenant. 422 avant la clôture.</summary>
    public async Task<RapportFinDto> Lire(Guid mepId)
    {
        var rapport = await db.RapportsFin.AsNoTracking().FirstOrDefaultAsync(r => r.MepId == mepId) ?? await Figer(mepId, calculeApres: true);
        // Format 1 : la MEP figée n'avait pas ses compteurs d'incidents ; ceux de la MEP font foi (le rapport est supprimé avec elle).
        var compteurs = rapport.Format >= 2 ? null
            : await db.Meps.AsNoTracking().Where(m => m.Id == mepId).Select(m => new { m.EchecsEtapes, m.EchecsRecette }).FirstOrDefaultAsync();
        return VersDto(rapport, compteurs is null ? null : (compteurs.EchecsEtapes, compteurs.EchecsRecette));
    }

    /// <summary>
    /// Fige le rapport de la MEP clôturée s'il ne l'est pas déjà (sans effet sinon) et le renvoie. <paramref name="calculeApres"/> :
    /// MEP clôturée avant l'existence des rapports, figé à la première demande.
    /// </summary>
    public async Task<RapportFin> Figer(Guid mepId, bool calculeApres)
    {
        if (await db.RapportsFin.AsNoTracking().FirstOrDefaultAsync(r => r.MepId == mepId) is { } existant) return existant;
        var mep = await svc.Charger(mepId);
        if (mep.Phase != Phase.Cloturee) throw new RegleMetierException(MessageNonCloturee);
        var dto = await svc.VersDto(mep, null);
        var journal = await MepsEndpoints.Journal(db, mepId);
        var (synthese, points) = Calculer(mep, journal);
        var contenu = new Contenu(synthese, points, MepMapping.TestsUnitaires(mep), dto, journal);
        var rapport = new RapportFin
        {
            // À la microseconde, comme PostgreSQL le garde : le rapport renvoyé est celui qui sera relu.
            MepId = mepId, FigeLe = new DateTimeOffset(svc.Maintenant.UtcTicks / 10 * 10, TimeSpan.Zero), CalculeApres = calculeApres, Format = FormatActuel,
            Contenu = JsonSerializer.Serialize(contenu, Options),
        };
        db.RapportsFin.Add(rapport);
        try
        {
            await db.SaveChangesAsync();
            return rapport;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Figé entre-temps par une autre requête : c'est celui-là qui fait foi.
            db.Entry(rapport).State = EntityState.Detached;
            return await db.RapportsFin.AsNoTracking().FirstAsync(r => r.MepId == mepId);
        }
    }

    private static RapportFinDto VersDto(RapportFin r, (int Etapes, int Recette)? compteurs)
    {
        var c = JsonSerializer.Deserialize<Contenu>(r.Contenu, Options)
            ?? throw new InvalidOperationException($"Rapport de fin illisible pour la MEP {r.MepId}.");
        var mep = c.Mep;
        // Rapport figé avant les compteurs d'incidents (format 1) : état (réussie, avec incidents, repliée) d'après les compteurs
        // de la MEP, échecs de recette compris ; aucun texte du journal n'est relu.
        if (compteurs is { } n)
            mep = mep with { Etat = Regles.EtatDe(mep.Phase, mep.Issue, n.Etapes + n.Recette), EchecsSurvenus = n.Etapes, EchecsRecette = n.Recette };
        // Rapport figé avant la clôture en échec (#93) : champs de clôture absents du JSON.
        if (mep.Cloture is { } cl && (cl.EtatService is null || cl.ReferenceIncident is null))
            mep = mep with { Cloture = cl with { EtatService = cl.EtatService ?? "", ReferenceIncident = cl.ReferenceIncident ?? "" } };
        return new RapportFinDto(r.MepId, r.FigeLe, r.CalculeApres, c.Synthese, c.PointsBloquants, c.TestsUnitaires, mep, c.Journal);
    }

    /// <summary>
    /// Synthèse et points bloquants d'une MEP, d'après son état et son journal (du plus récent au plus ancien, comme l'API le
    /// donne). Les échecs sont les événements « etape.echec » du journal, rattachés à leur étape par <see cref="EvenementDto.EtapeId"/> :
    /// une étape refaite garde ainsi son premier échec, et un changement de libellé ne change rien.
    /// </summary>
    public static (SyntheseRapportDto Synthese, List<PointBloquantDto> PointsBloquants) Calculer(Mep mep, IReadOnlyList<EvenementDto> journal)
    {
        var chrono = journal.OrderBy(e => e.Quand).ThenBy(e => e.Id).ToList();
        var parId = mep.Etapes.ToDictionary(e => e.Id);
        int Minutes(DateTimeOffset de, DateTimeOffset a) => (int)Math.Round((a - de).TotalMinutes);

        var points = new List<PointBloquantDto>();
        foreach (var echec in chrono.Where(e => e.Type == Regles.EvenementEchecEtape))
        {
            if (echec.EtapeId is not { } etapeId || !parId.TryGetValue(etapeId, out var etape)) continue;
            var code = etape.Code;
            var apres = chrono.Where(e => e.Quand >= echec.Quand && e.Id != echec.Id && e.EtapeId == etapeId).ToList();
            var reprise = apres.FirstOrDefault(e => e.Type == "etape.demarree")?.Quand;
            var reussite = reprise is { } r ? apres.FirstOrDefault(e => e.Type == "etape.reussie" && e.Quand >= r)?.Quand : null;
            var resolution = mep.Resolutions
                .Where(x => x.EtapeId == etape.Id && x.DemarreeLe >= echec.Quand && (reprise is null || x.DemarreeLe <= reprise))
                .OrderBy(x => x.DemarreeLe).LastOrDefault();
            // Motif : le détail sans le rappel de l'étape (le détail entier si son libellé a changé).
            var marque = $"{code} en échec : ";
            var motif = echec.Detail.StartsWith(marque, StringComparison.Ordinal) ? echec.Detail[marque.Length..] : echec.Detail;
            points.Add(new PointBloquantDto(etape.Plan, code, etape.Action, echec.Quand, echec.Auteur, motif, resolution?.Titre, resolution?.Statut,
                resolution is { TermineeLe: { } fin } ? Minutes(resolution.DemarreeLe, fin) : null, reprise, reussite,
                reprise is { } rr ? Minutes(echec.Quand, rr) : null));
        }

        var deploiement = mep.EtapesDu(TypePlan.Deploiement).ToList();
        var go = mep.DemarrageAutoriseLe;
        var fins = deploiement.Select(e => e.FinReelle).OfType<DateTimeOffset>().ToList();
        DateTimeOffset? finDeploiement = fins.Count > 0 ? fins.Max() : null;
        var tests = mep.FichesRecette.SelectMany(f => f.Etapes).ToList();
        var synthese = new SyntheseRapportDto(
            deploiement.Count, deploiement.Count(e => e.Etat == EtatEtape.Reussie), go, finDeploiement,
            go is { } g && finDeploiement is { } f ? Minutes(g, f) : null,
            (int)Math.Round(Regles.CheminCritique(deploiement, e => e.DureeEstimeeMinutes ?? 0)),
            points.Count, points.Count(p => p.RepriseReussieLe is not null), mep.Resolutions.Count,
            tests.Count, tests.Count(t => t.Resultat == ResultatControle.Conforme), tests.Count(t => t.Resultat == ResultatControle.Echec),
            mep.RetourArriereDeclencheLe is not null,
            mep.PremierGoLe is { } premier && premier != go ? premier : null,
            chrono.Count(e => e.Type == "phase.preparation"),
            deploiement.Count(e => e.AjouteeApresGo));
        return (synthese, points);
    }
}
