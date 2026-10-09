using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Paramètres globaux et catalogue des critères go / no-go (lecture : tout connecté ; écriture : admin).</summary>
public static class ParametresEndpoints
{
    private static CritereGoNoGoDto Dto(CritereGoNoGo c) => new(c.Id, c.Libelle, c.Ordre, c.Actif);

    private static void ExigerAdmin(ClaimsPrincipal user)
    {
        if (!Acteur.Depuis(user).EstAdmin) throw ErreurHttp.Interdit("Réservé aux administrateurs.");
    }

    private static string Libelle(string? libelle)
    {
        var l = ServiceMep.Texte(libelle);
        return l.Length == 0 ? throw ErreurHttp.Saisie("Le libellé est obligatoire.") : l;
    }

    public static void MapParametres(this RouteGroupBuilder api)
    {
        // ---------- Paramètres globaux ----------
        api.MapGet("/parametres", async (ServiceMep svc) => new ParametresDto(await svc.ApprobationsMinimum()));

        api.MapPut("/parametres", async (ParametresSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            if (c.ApprobationsMinimum is null or < 0) throw ErreurHttp.Saisie("Le nombre minimum d'approbations doit être un entier positif ou nul.");
            var p = await db.Parametres.FirstOrDefaultAsync(x => x.Id == ParametresGlobaux.IdUnique);
            if (p is null) db.Parametres.Add(p = new ParametresGlobaux());
            p.ApprobationsMinimum = c.ApprobationsMinimum.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new ParametresDto(p.ApprobationsMinimum));
        });

        // ---------- Critères go / no-go ----------
        api.MapGet("/criteres-go-no-go", async (MepDbContext db) =>
            (await db.CriteresGoNoGo.AsNoTracking().OrderBy(c => c.Ordre).ThenBy(c => c.Libelle).ToListAsync()).Select(Dto));

        api.MapPost("/criteres-go-no-go", async (CritereGoNoGoSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var ordre = c.Ordre ?? (await db.CriteresGoNoGo.MaxAsync(x => (int?)x.Ordre) ?? 0) + 1;
            var critere = new CritereGoNoGo { Libelle = Libelle(c.Libelle), Ordre = ordre, Actif = c.Actif ?? true };
            db.CriteresGoNoGo.Add(critere);
            await db.SaveChangesAsync();
            return Results.Created($"/api/criteres-go-no-go/{critere.Id}", Dto(critere));
        });

        api.MapPut("/criteres-go-no-go/{id:guid}", async (Guid id, CritereGoNoGoSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var critere = await db.CriteresGoNoGo.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Critère introuvable.");
            critere.Libelle = Libelle(c.Libelle);
            critere.Ordre = c.Ordre ?? critere.Ordre;
            critere.Actif = c.Actif ?? critere.Actif;
            await db.SaveChangesAsync();
            return Results.Ok(Dto(critere));
        });

        // Les MEP gardent une copie figée des critères cochés : la suppression est toujours possible.
        api.MapDelete("/criteres-go-no-go/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var critere = await db.CriteresGoNoGo.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Critère introuvable.");
            db.CriteresGoNoGo.Remove(critere);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
