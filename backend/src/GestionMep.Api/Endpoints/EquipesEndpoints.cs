using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Équipes (lecture : tout connecté, pour filtrer les personnes et les MEP ; écriture : administrateurs, groupe /api/admin).
/// Les opérations sont partagées avec la commande <c>gestion-mep equipes</c> (acteur null : « Ligne de commande ») et
/// inscrites au journal d'administration.
/// </summary>
public static class EquipesEndpoints
{
    public static EquipeDto Dto(Equipe e) => new(e.Id, e.Nom, e.Description, [.. e.MembreIds], e.CreeeLe);

    public static async Task<List<EquipeDto>> Lister(MepDbContext db) =>
        (await db.Equipes.AsNoTracking().ToListAsync()).OrderBy(e => e.Nom, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), true))
            .Select(Dto).ToList();

    /// <summary>Équipe désignée par son nom (sans tenir compte de la casse) ou son identifiant (404 sinon).</summary>
    public static async Task<Equipe> Trouver(MepDbContext db, string nomOuId)
    {
        var equipes = await db.Equipes.ToListAsync();
        return equipes.FirstOrDefault(e => string.Equals(e.Nom, nomOuId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? (Guid.TryParse(nomOuId, out var id) ? equipes.FirstOrDefault(e => e.Id == id) : null)
            ?? throw ErreurHttp.NonTrouve($"Équipe « {nomOuId} » introuvable.");
    }

    private static async Task<Equipe> Charger(MepDbContext db, Guid id) =>
        await db.Equipes.FirstOrDefaultAsync(e => e.Id == id) ?? throw ErreurHttp.NonTrouve("Équipe introuvable.");

    /// <summary>Comptes demandés, sans doublon, dans l'ordre reçu (400 si l'un est inconnu).</summary>
    private static async Task<List<Utilisateur>> Membres(MepDbContext db, IEnumerable<Guid> ids)
    {
        var liste = ids.Distinct().ToList();
        var comptes = await db.Utilisateurs.AsNoTracking().Where(u => liste.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        if (comptes.Count != liste.Count) throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
        return liste.Select(i => comptes[i]).ToList();
    }

    private static string Comptes(IEnumerable<Utilisateur> comptes) => string.Join(", ", comptes.Select(JournalAdmin.Compte));

    public static async Task<EquipeDto> Creer(MepDbContext db, Acteur? acteur, EquipeSaisie c, DateTimeOffset maintenant)
    {
        var nom = Regles.NomEquipe(c.Nom, await db.Equipes.AsNoTracking().ToListAsync());
        var membres = await Membres(db, c.MembreIds ?? []);
        var e = new Equipe { Nom = nom, Description = ServiceMep.Texte(c.Description), MembreIds = membres.Select(m => m.Id).ToList(), CreeeLe = maintenant };
        db.Equipes.Add(e);
        JournalAdmin.Ecrire(db, acteur, "equipe.creee", $"Équipe « {nom} » créée{(membres.Count > 0 ? $" avec {Comptes(membres)}" : "")}");
        await db.SaveChangesAsync();
        return Dto(e);
    }

    /// <summary>Nom, description et, si <c>MembreIds</c> est donné, membres ; journalise les changements (rien si rien ne change).</summary>
    public static async Task<EquipeDto> Modifier(MepDbContext db, Acteur? acteur, Guid id, EquipeSaisie c)
    {
        var e = await Charger(db, id);
        var nom = Regles.NomEquipe(c.Nom, await db.Equipes.AsNoTracking().Where(x => x.Id != id).ToListAsync());
        var description = ServiceMep.Texte(c.Description);
        var changements = new List<string>();
        if (nom != e.Nom) changements.Add($"renommée « {nom} »");
        if (description != e.Description) changements.Add("description modifiée");
        if (c.MembreIds is { } ids)
        {
            var membres = await Membres(db, ids);
            var ajoutes = membres.Where(m => !e.MembreIds.Contains(m.Id)).ToList();
            var retires = await db.Utilisateurs.AsNoTracking().Where(u => e.MembreIds.Contains(u.Id) && !ids.Contains(u.Id)).ToListAsync();
            if (ajoutes.Count > 0) changements.Add($"{Comptes(ajoutes)} ajouté{(ajoutes.Count > 1 ? "s" : "")}");
            if (retires.Count > 0) changements.Add($"{Comptes(retires)} retiré{(retires.Count > 1 ? "s" : "")}");
            // Comptes supprimés de la base entre-temps : retirés sans bruit.
            if (ajoutes.Count > 0 || retires.Count > 0 || e.MembreIds.Count != membres.Count) e.MembreIds = membres.Select(m => m.Id).ToList();
        }
        if (changements.Count > 0)
            JournalAdmin.Ecrire(db, acteur, "equipe.modifiee", $"Équipe « {e.Nom} » : {string.Join(", ", changements)}");
        e.Nom = nom;
        e.Description = description;
        await db.SaveChangesAsync();
        return Dto(e);
    }

    /// <summary>Supprime l'équipe : les comptes, leurs données et les MEP ne changent pas.</summary>
    public static async Task Supprimer(MepDbContext db, Acteur? acteur, Guid id)
    {
        var e = await Charger(db, id);
        db.Equipes.Remove(e);
        JournalAdmin.Ecrire(db, acteur, "equipe.supprimee", $"Équipe « {e.Nom} » supprimée ({e.MembreIds.Count} membre{(e.MembreIds.Count > 1 ? "s" : "")})");
        await db.SaveChangesAsync();
    }

    public static void MapEquipes(this RouteGroupBuilder api)
    {
        api.MapGet("/equipes", async (MepDbContext db) => await Lister(db));

        var admin = api.MapGroup("/admin/equipes").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapPost("", async (EquipeSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var e = await Creer(db, Acteur.Depuis(user), c, horloge.GetUtcNow());
            return Results.Created($"/api/equipes/{e.Id}", e);
        });

        admin.MapPut("/{id:guid}", async (Guid id, EquipeSaisie c, ClaimsPrincipal user, MepDbContext db) =>
            Results.Ok(await Modifier(db, Acteur.Depuis(user), id, c)));

        admin.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            await Supprimer(db, Acteur.Depuis(user), id);
            return Results.NoContent();
        });
    }
}
