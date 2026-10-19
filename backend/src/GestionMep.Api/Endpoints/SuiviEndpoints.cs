using System.Security.Claims;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Suivi des MEP (#61) : tableau de bord « Suivi en direct » (ouvert à tous) et son flux temps réel, rapport de fin d'une MEP
/// clôturée et son export Excel. La vue d'ensemble d'une MEP se calcule dans le navigateur à partir de la MEP elle-même.
/// </summary>
public static class SuiviEndpoints
{
    private static readonly TimeSpan DureeFlux = TimeSpan.FromHours(1);
    private static readonly Phase[] PhasesEnCours = [Phase.Execution, Phase.Recette, Phase.RetourArriere];

    /// <summary>Instant d'une date et d'une heure (minuit sans heure) dans le fuseau de la MEP (UTC si le fuseau est inconnu).</summary>
    private static DateTimeOffset Instant(DateOnly date, TimeOnly? heure, string fuseau)
    {
        var local = date.ToDateTime(heure ?? TimeOnly.MinValue, DateTimeKind.Unspecified);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(fuseau);
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }
    }

    /// <summary>Tableau de bord : MEP lancées (vues par <paramref name="utilisateurId"/>), prévues aujourd'hui et demain, clôturées depuis 24 h.</summary>
    public static async Task<SuiviDto> Suivi(MepDbContext db, ServiceMep svc, Guid utilisateurId)
    {
        var maintenant = svc.Maintenant;
        var ids = await db.Meps.AsNoTracking().Where(m => !m.EstModele && PhasesEnCours.Contains(m.Phase)).OrderBy(m => m.DemarrageAutoriseLe)
            .Select(m => m.Id).ToListAsync();
        var enCours = new List<MepDto>();
        foreach (var id in ids)
        {
            // Charger applique l'échéance de retour arrière, comme à l'ouverture de la MEP.
            db.ChangeTracker.Clear();
            enCours.Add(await svc.VersDto(await svc.Charger(id), utilisateurId));
        }

        var annoncees = await db.Meps.AsNoTracking()
            .Where(m => !m.EstModele && m.Phase == Phase.Preparation && m.Communication.DateAnnoncee != null)
            .Select(m => new
            {
                m.Id, m.Outil, m.VersionCible, m.Type, Date = m.Communication.DateAnnoncee!.Value, Heure = m.Communication.HeureDebutAnnoncee,
                m.FuseauHoraire, Pilote = m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
            }).ToListAsync();
        var prevues = annoncees
            .Select(m => (Jour: DateOnly.FromDateTime(VariablesTexte.Local(maintenant, m.FuseauHoraire)),
                Dto: new MepPrevueDto(m.Id, m.Outil, m.VersionCible, m.Type, m.Date, m.Heure, Instant(m.Date, m.Heure, m.FuseauHoraire), m.FuseauHoraire, m.Pilote)))
            .OrderBy(x => x.Dto.Debut).ThenBy(x => x.Dto.Outil, StringComparer.CurrentCultureIgnoreCase).ToList();
        // Prochaine : la première à venir (sans heure annoncée, tant que son jour n'est pas passé).
        var prochaine = prevues.FirstOrDefault(x => x.Dto.Heure is null ? x.Dto.Date >= x.Jour : x.Dto.Debut >= maintenant).Dto;

        var depuis = maintenant.AddHours(-24);
        var cloturees = (await db.Meps.AsNoTracking()
            .Where(m => !m.EstModele && m.Phase == Phase.Cloturee && m.ClotureeLe >= depuis).OrderByDescending(m => m.ClotureeLe)
            .Select(m => new
            {
                m.Id, m.Outil, m.VersionCible, m.Type, m.Phase, m.Issue, ClotureeLe = m.ClotureeLe!.Value,
                Pilote = m.Participants.Where(p => p.Id == m.PiloteId).Select(p => p.Nom).FirstOrDefault(),
                Echecs = db.Journal.Count(e => e.MepId == m.Id && e.Type == Regles.EvenementEchecEtape),
            })
            .ToListAsync())
            .Select(m => new MepClotureeDto(m.Id, m.Outil, m.VersionCible, m.Type, m.Issue, Regles.EtatDe(m.Phase, m.Issue, m.Echecs), m.ClotureeLe, m.Pilote))
            .ToList();

        return new SuiviDto(enCours, [.. prevues.Where(x => x.Dto.Date >= x.Jour && x.Dto.Date <= x.Jour.AddDays(1)).Select(x => x.Dto)],
            prochaine, cloturees, maintenant);
    }

    /// <summary>Rapport de fin en classeur Excel (route et <c>gestion-mep meps excel &lt;id&gt; rapport</c>).</summary>
    public static async Task<ExportsExcelEndpoints.Fichier> RapportExcel(RapportsFin rapports, Guid id, DateTimeOffset maintenant,
        Tableur.PaletteExcel? palette = null)
    {
        var r = await rapports.Lire(id);
        return new(VuesExcel.Rapport(r, maintenant, palette), VuesExcel.NomFichierRapport(r.Mep));
    }

    public static void MapSuivi(this RouteGroupBuilder api)
    {
        // Réponses non typées, comme GET /meps/{id} : les contrats (SuiviDto, RapportFinDto) sont décrits par DocumentationOpenApi.
        api.MapGet("/suivi", async (ClaimsPrincipal user, MepDbContext db, ServiceMep svc) => Results.Ok(await Suivi(db, svc, Acteur.Depuis(user).Id)));

        // Flux du tableau de bord : « maj » à chaque modification d'une MEP (le navigateur relit alors /suivi).
        api.MapGet("/suivi/flux", async (HttpContext ctx, ClaimsPrincipal user, DiffuseurMep diffuseur) =>
        {
            using var abonnement = diffuseur.AbonnerSuivi(Acteur.Depuis(user).Id)
                ?? throw new ErreurHttp(429, "Trop de flux temps réel ouverts : fermez des onglets, puis réessayez.");
            using var duree = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            duree.CancelAfter(DureeFlux);
            var ct = duree.Token;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
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
                        if (abonnement.Prendre().Version is { } v)
                            await ctx.Response.WriteAsync($"event: maj\ndata: {JsonSerializer.Serialize(new { version = v })}\n\n", ct);
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

        // Rapport de fin : figé à la clôture ; une MEP clôturée avant son existence obtient le sien maintenant. 422 avant la clôture.
        api.MapGet("/meps/{id:guid}/rapport", async (Guid id, RapportsFin rapports) => Results.Ok(await rapports.Lire(id)));

        api.MapGet("/meps/{id:guid}/rapport/excel", async (Guid id, RapportsFin rapports, TimeProvider temps, ServiceCharte charte) =>
        {
            var f = await RapportExcel(rapports, id, temps.GetUtcNow(), charte.Courant.Palette);
            return Results.File(f.Contenu, VuesExcel.TypeContenu, f.Nom);
        }).Produces(StatusCodes.Status200OK, contentType: VuesExcel.TypeContenu);
    }
}
