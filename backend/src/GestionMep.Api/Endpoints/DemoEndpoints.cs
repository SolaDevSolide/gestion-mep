using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Securite;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Mode démonstration : comptes proposés par la connexion rapide, connexion rapide, simulation de collègues et réinitialisation
/// de la base.
/// Les routes ne sont déclarées que si la démonstration est active (<see cref="Demonstration.Active"/>, lu au démarrage) :
/// sinon, /api/demo… répond comme toute route inconnue (404) et rien n'est exposé.
/// </summary>
public static class DemoEndpoints
{
    public const string MessageReserve = "La connexion rapide est réservée aux comptes de démonstration.";
    public const string MessageMotDePasseChange =
        "Ce compte de démonstration n'utilise plus le mot de passe « demo » : connectez-vous avec le formulaire, ou réinitialisez la démonstration.";

    public static void MapDemo(this RouteGroupBuilder api, IConfiguration config)
    {
        if (!Demonstration.Active(config))
        {
            // Hors démonstration : /api/demo… répond comme une route inconnue, connecté ou non (le reste de /api exige une session).
            api.Map("/demo/{**reste}", () => Results.Json(new { message = "Route introuvable." }, statusCode: StatusCodes.Status404NotFound))
                .AllowAnonymous().ExcludeFromDescription();
            return;
        }
        var demo = api.MapGroup("/demo");

        // Sans authentification : l'écran de connexion et le bandeau « Mode démonstration » en ont besoin.
        demo.MapGet("", async (MepDbContext db) => new DemoDto(true, await Demonstration.Comptes(db))).AllowAnonymous();

        // Connexion rapide : uniquement un login de démonstration, et seulement s'il a encore le mot de passe de démonstration
        // (vérifié comme une connexion normale : limite par adresse, blocage progressif, journal des échecs répétés).
        demo.MapPost("/connexion", async (ConnexionDemoSaisie corps, HttpContext ctx, MepDbContext db, IPasswordHasher<Utilisateur> hasher,
            LimiteurConnexion limiteur, ILoggerFactory journaux) =>
        {
            var journal = journaux.CreateLogger("Demonstration");
            var login = AuthentificationLocale.NormaliserLogin(corps.Login);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue";
            if (!DonneesDemo.EstCompteDemo(login))
            {
                journal.LogWarning("Connexion rapide refusée depuis {Ip} : « {Login} » n'est pas un compte de démonstration.",
                    ip, login.Length > 64 ? login[..64] : login);
                return Results.Json(new { message = MessageReserve }, statusCode: StatusCodes.Status403Forbidden);
            }
            var resultat = await AuthEndpoints.Connecter(ctx, db, hasher, limiteur, login, DonneesDemo.MotDePasse, MessageMotDePasseChange);
            if (resultat is Ok<UtilisateurDto>) journal.LogInformation("Connexion rapide de démonstration : {Login} depuis {Ip}.", login, ip);
            return resultat;
        }).AllowAnonymous().RequireRateLimiting(ProtectionsHttp.PolitiqueConnexion);

        // Simulation de collègues : Bob et Claire travaillent en vrai sur « Facturation électronique » (toute personne connectée).
        demo.MapGet("/simulation", (SimulationDemo simulation) => simulation.Etat());
        demo.MapPost("/simulation", async (SimulationSaisie corps, ClaimsPrincipal user, SimulationDemo simulation) =>
            corps.Actif ? await simulation.Demarrer(Acteur.Depuis(user)) : await simulation.Arreter());

        // Réinitialisation : administrateurs seulement. La session de l'auteur est fermée (son compte est recréé).
        demo.MapPost("/reinitialiser", async (ClaimsPrincipal user, HttpContext ctx, MepDbContext db, IConfiguration configuration,
            IPasswordHasher<Utilisateur> hasher, StockagePiecesJointes stockage, TimeProvider temps, FournisseurReglagesBase reglages, ILoggerFactory journaux,
            SimulationDemo simulation) =>
        {
            var acteur = Acteur.Depuis(user);
            // Les collègues simulés quittent la MEP avant qu'elle soit recréée.
            await simulation.Arreter();
            var (meps, fichiers) = await Demonstration.Reinitialiser(db, configuration, hasher, stockage, temps.GetUtcNow(), acteur);
            reglages.Recharger();
            await AuthentificationLocale.Fermer(ctx);
            journaux.CreateLogger("Demonstration").LogWarning("Démonstration réinitialisée par {Nom} : {Meps} MEP recréées, {Fichiers} fichier(s) supprimé(s).",
                acteur.Nom, meps, fichiers);
            return new ReinitialisationDemoDto(meps, fichiers);
        }).RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);
    }
}
