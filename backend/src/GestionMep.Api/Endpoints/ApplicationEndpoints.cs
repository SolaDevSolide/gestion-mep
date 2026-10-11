using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Services;

namespace GestionMep.Api.Endpoints;

/// <summary>Informations sur l'application : version en cours, mise à jour disponible, lien de signalement.</summary>
public static class ApplicationEndpoints
{
    /// <summary>Formulaire de signalement par défaut : les « issues » du dépôt GitHub.</summary>
    public const string LienSignalementParDefaut = "https://github.com/" + VerificationMisesAJour.DepotParDefaut + "/issues/new/choose";

    private static MiseAJourDto Dto(EtatMiseAJour e) => new(e.Active, e.Depot, VersionApplication.Courante, e.DerniereVersion,
        e.PublieeLe, e.NotesUrl, e.VerifieeLe, e.Erreur, VersionApplication.EstPlusRecente(e.DerniereVersion, VersionApplication.Courante));

    public static void MapApplication(this RouteGroupBuilder api)
    {
        // Tout compte connecté : version et lien de signalement. L'état des mises à jour n'est donné qu'aux administrateurs.
        api.MapGet("/application", (ClaimsPrincipal user, IConfiguration config, VerificationMisesAJour verification) =>
        {
            var lien = config["Signalement:Lien"];
            return new ApplicationDto(
                VersionApplication.Courante,
                lien is null ? LienSignalementParDefaut : (lien.Trim() is { Length: > 0 } l ? l : null),
                Acteur.Depuis(user).EstAdmin ? Dto(verification.Etat) : null);
        });

        api.MapPost("/admin/mises-a-jour/verifier", async (VerificationMisesAJour verification, CancellationToken ct) =>
            Dto(await verification.Verifier(ct)))
            .RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);
    }
}
