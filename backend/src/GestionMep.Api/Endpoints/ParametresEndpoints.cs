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

    /// <summary>Nombre d'approbations exigées avant le Go (console et ligne de commande ; <paramref name="acteur"/> null : ligne de commande).</summary>
    public static async Task<int> EnregistrerApprobations(MepDbContext db, int? valeur, Acteur? acteur)
    {
        if (valeur is null or < 0) throw ErreurHttp.Saisie("Le nombre minimum d'approbations doit être un entier positif ou nul.");
        var p = await db.Parametres.FirstOrDefaultAsync(x => x.Id == ParametresGlobaux.IdUnique);
        if (p is null) db.Parametres.Add(p = new ParametresGlobaux());
        if (p.ApprobationsMinimum != valeur.Value)
            JournalAdmin.Ecrire(db, acteur, "parametres.modifies", $"Approbations minimum : {p.ApprobationsMinimum} → {valeur.Value}");
        p.ApprobationsMinimum = valeur.Value;
        await db.SaveChangesAsync();
        return p.ApprobationsMinimum;
    }

    // ---------- Critères go / no-go : opérations partagées avec « gestion-mep criteres » (acteur null : ligne de commande) ----------

    /// <summary>Critères dans l'ordre d'affichage (ordre, puis libellé) : la position dans cette liste est le « n° » de la ligne de commande.</summary>
    public static async Task<List<CritereGoNoGoDto>> ListerCriteres(MepDbContext db) =>
        (await db.CriteresGoNoGo.AsNoTracking().OrderBy(c => c.Ordre).ThenBy(c => c.Libelle).ToListAsync()).Select(Dto).ToList();

    public static async Task<CritereGoNoGoDto> CreerCritere(MepDbContext db, Acteur? acteur, CritereGoNoGoSaisie c)
    {
        var ordre = c.Ordre ?? (await db.CriteresGoNoGo.MaxAsync(x => (int?)x.Ordre) ?? 0) + 1;
        var critere = new CritereGoNoGo { Libelle = Libelle(c.Libelle), Ordre = ordre, Actif = c.Actif ?? true };
        db.CriteresGoNoGo.Add(critere);
        JournalAdmin.Ecrire(db, acteur, "critere.cree", $"Critère go / no-go « {critere.Libelle} » créé");
        await db.SaveChangesAsync();
        return Dto(critere);
    }

    public static async Task<CritereGoNoGoDto> ModifierCritere(MepDbContext db, Acteur? acteur, Guid id, CritereGoNoGoSaisie c)
    {
        var critere = await db.CriteresGoNoGo.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Critère introuvable.");
        critere.Libelle = Libelle(c.Libelle);
        critere.Ordre = c.Ordre ?? critere.Ordre;
        critere.Actif = c.Actif ?? critere.Actif;
        JournalAdmin.Ecrire(db, acteur, "critere.modifie", $"Critère go / no-go « {critere.Libelle} » modifié{(critere.Actif ? "" : " (inactif)")}");
        await db.SaveChangesAsync();
        return Dto(critere);
    }

    /// <summary>Les MEP gardent une copie figée des critères cochés : la suppression est toujours possible.</summary>
    public static async Task<CritereGoNoGoDto> SupprimerCritere(MepDbContext db, Acteur? acteur, Guid id)
    {
        var critere = await db.CriteresGoNoGo.FirstOrDefaultAsync(x => x.Id == id) ?? throw ErreurHttp.NonTrouve("Critère introuvable.");
        db.CriteresGoNoGo.Remove(critere);
        JournalAdmin.Ecrire(db, acteur, "critere.supprime", $"Critère go / no-go « {critere.Libelle} » supprimé");
        await db.SaveChangesAsync();
        return Dto(critere);
    }

    public static void MapParametres(this RouteGroupBuilder api)
    {
        // ---------- Paramètres globaux ----------
        api.MapGet("/parametres", async (ServiceMep svc) => new ParametresDto(await svc.ApprobationsMinimum()));

        api.MapPut("/parametres", async (ParametresSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            return Results.Ok(new ParametresDto(await EnregistrerApprobations(db, c.ApprobationsMinimum, Acteur.Depuis(user))));
        });

        // ---------- Critères go / no-go ----------
        api.MapGet("/criteres-go-no-go", async (MepDbContext db) => (await ListerCriteres(db)).AsEnumerable());

        api.MapPost("/criteres-go-no-go", async (CritereGoNoGoSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            var critere = await CreerCritere(db, Acteur.Depuis(user), c);
            return Results.Created($"/api/criteres-go-no-go/{critere.Id}", critere);
        });

        api.MapPut("/criteres-go-no-go/{id:guid}", async (Guid id, CritereGoNoGoSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            return Results.Ok(await ModifierCritere(db, Acteur.Depuis(user), id, c));
        });

        api.MapDelete("/criteres-go-no-go/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            ExigerAdmin(user);
            await SupprimerCritere(db, Acteur.Depuis(user), id);
            return Results.NoContent();
        });
    }
}
