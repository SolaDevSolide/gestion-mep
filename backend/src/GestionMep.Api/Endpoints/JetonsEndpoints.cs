using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Jetons d'accès à l'API (#128). « Mon profil » : chacun gère ses jetons personnels (<c>/api/jetons</c>). Console : les
/// administrateurs créent les jetons de service et révoquent n'importe quel jeton (<c>/api/admin/jetons</c>). Ces routes
/// demandent une session : un jeton ne crée ni ne révoque de jeton (voir <see cref="PorteesRoutes"/>).
/// </summary>
public static class JetonsEndpoints
{
    public static void MapJetons(this RouteGroupBuilder api)
    {
        api.MapGet("/jetons/portees", () => ServiceJetons.Portees());

        api.MapGet("/jetons", (ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var moi = Acteur.Depuis(user).Id;
            return ServiceJetons.Lister(db, j => j.UtilisateurId == moi && j.Type == TypeJeton.Personnel, horloge.GetUtcNow());
        });

        api.MapGet("/jetons/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
            await ServiceJetons.Dto(db, await ServiceJetons.Charger(db, id, Acteur.Depuis(user).Id), horloge.GetUtcNow()));

        api.MapPost("/jetons", async Task<Created<JetonCreeDto>> (JetonSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var acteur = Acteur.Depuis(user);
            var compte = await db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == acteur.Id) ?? throw new ErreurHttp(401, "Session expirée.");
            var cree = await ServiceJetons.CreerPersonnel(db, acteur, compte, c, horloge.GetUtcNow());
            return TypedResults.Created($"/api/jetons/{cree.Jeton.Id}", cree);
        });

        api.MapPost("/jetons/{id:guid}/revoquer", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var acteur = Acteur.Depuis(user);
            return await ServiceJetons.Revoquer(db, acteur, await ServiceJetons.Charger(db, id, acteur.Id), horloge.GetUtcNow());
        });

        var admin = api.MapGroup("/admin/jetons").ExigerAdmin();

        // Tous les jetons (personnels et de service) ; filtres facultatifs : type, compte, actifs seulement.
        admin.MapGet("", (TypeJeton? type, Guid? compte, bool? actifs, MepDbContext db, TimeProvider horloge) =>
        {
            var maintenant = horloge.GetUtcNow();
            var seulementActifs = actifs == true;
            return ServiceJetons.Lister(db, j => (type == null || j.Type == type) && (compte == null || j.UtilisateurId == compte)
                && (!seulementActifs || j.RevoqueLe == null && j.ExpireLe > maintenant), maintenant);
        });

        admin.MapGet("/{id:guid}", async (Guid id, MepDbContext db, TimeProvider horloge) =>
            await ServiceJetons.Dto(db, await ServiceJetons.Charger(db, id), horloge.GetUtcNow()));

        admin.MapPost("", async Task<Created<JetonCreeDto>> (JetonSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var cree = await ServiceJetons.CreerService(db, Acteur.Depuis(user), c, horloge.GetUtcNow());
            return TypedResults.Created($"/api/admin/jetons/{cree.Jeton.Id}", cree);
        });

        admin.MapPost("/{id:guid}/revoquer", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
            await ServiceJetons.Revoquer(db, Acteur.Depuis(user), await ServiceJetons.Charger(db, id), horloge.GetUtcNow()));
    }
}
