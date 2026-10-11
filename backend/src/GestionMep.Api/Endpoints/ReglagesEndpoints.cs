using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;

namespace GestionMep.Api.Endpoints;

/// <summary>Réglages d'exploitation de la console (mêmes règles que <c>gestion-mep reglages</c>), réservés aux administrateurs.</summary>
public static class ReglagesEndpoints
{
    public static void MapReglages(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/reglages").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapGet("", (MepDbContext db, IConfiguration config) => Reglages.Lister(db, config));

        // Après l'enregistrement, la configuration est relue tout de suite : la nouvelle valeur vaut dès la requête suivante.
        admin.MapPut("/{nom}", async (string nom, ReglageSaisie s, ClaimsPrincipal user, MepDbContext db, IConfiguration config,
            FournisseurReglagesBase fournisseur, TimeProvider temps) =>
        {
            var reglage = await Reglages.Definir(db, config, Reglages.Trouver(nom), s.Valeur, Acteur.Depuis(user), temps.GetUtcNow());
            fournisseur.Recharger();
            return reglage;
        });

        admin.MapDelete("/{nom}", async (string nom, ClaimsPrincipal user, MepDbContext db, IConfiguration config, FournisseurReglagesBase fournisseur) =>
        {
            var reglage = await Reglages.Retablir(db, config, Reglages.Trouver(nom), Acteur.Depuis(user));
            fournisseur.Recharger();
            return reglage;
        });
    }
}
