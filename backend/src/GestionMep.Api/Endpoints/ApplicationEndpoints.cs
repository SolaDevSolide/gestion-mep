using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>Informations sur l'application : version en cours, mise à jour disponible et installation, lien de signalement.</summary>
public static class ApplicationEndpoints
{
    /// <summary>Formulaire de signalement par défaut : les « issues » du dépôt GitHub.</summary>
    public const string LienSignalementParDefaut = "https://github.com/" + VerificationMisesAJour.DepotParDefaut + "/issues/new/choose";

    /// <summary>Phases pendant lesquelles une MEP est en cours : une mise à jour couperait les personnes qui la déroulent.</summary>
    private static readonly Phase[] PhasesEnCours = [Phase.Execution, Phase.Recette, Phase.RetourArriere];

    private static MiseAJourDto Dto(EtatMiseAJour e, EtatInstallateur i) => new(e.Active, e.Depot, VersionApplication.Courante,
        e.DerniereVersion, e.PublieeLe, e.NotesUrl, e.VerifieeLe, e.Erreur,
        VersionApplication.EstPlusRecente(e.DerniereVersion, VersionApplication.Courante),
        new InstallateurDto(i.Present, i.Statut, i.Version, i.Message, i.Le));

    public static void MapApplication(this RouteGroupBuilder api)
    {
        // Tout compte connecté : version et lien de signalement. L'état des mises à jour n'est donné qu'aux administrateurs.
        api.MapGet("/application", (ClaimsPrincipal user, IConfiguration config, VerificationMisesAJour verification, InstallationMiseAJour installation) =>
        {
            var lien = config["Signalement:Lien"];
            return new ApplicationDto(
                VersionApplication.Courante,
                lien is null ? LienSignalementParDefaut : (lien.Trim() is { Length: > 0 } l ? l : null),
                Acteur.Depuis(user).EstAdmin ? Dto(verification.Etat, installation.Lire()) : null);
        });

        var admin = api.MapGroup("/admin/mises-a-jour").RequireAuthorization(AuthentificationLocale.PolitiqueAdmin);

        admin.MapPost("/verifier", async (VerificationMisesAJour verification, InstallationMiseAJour installation, CancellationToken ct) =>
            Dto(await verification.Verifier(ct), installation.Lire()));

        // Installe la dernière version publiée par le service « mise-a-jour ». Refusé pendant une MEP en cours.
        admin.MapPost("/installer", async (InstallationSaisie s, ClaimsPrincipal user, MepDbContext db,
            VerificationMisesAJour verification, InstallationMiseAJour installation) =>
        {
            var etat = verification.Etat;
            var installateur = installation.Lire();
            if (!installateur.Present)
                throw ErreurHttp.Regle(["Le service de mise à jour n'est pas actif : l'équipe informatique doit l'activer (documentation Exploiter)."]);
            if (installateur.Statut == "en-cours")
                throw ErreurHttp.Regle(["Une mise à jour est déjà en cours."]);
            if (s.Version is null || s.Version != etat.DerniereVersion || !VersionApplication.EstPlusRecente(s.Version, VersionApplication.Courante))
                throw ErreurHttp.Regle(["Seule la dernière version publiée, plus récente que la version en cours, peut être installée."]);
            var enCours = await db.Meps.CountAsync(m => !m.EstModele && PhasesEnCours.Contains(m.Phase));
            if (enCours > 0)
                throw ErreurHttp.Regle([$"{enCours} MEP en cours (exécution, recette ou retour arrière) : mettre à jour une fois terminée(s)."]);

            var acteur = Acteur.Depuis(user);
            installation.Demander(s.Version, acteur.Nom);
            JournalAdmin.Ecrire(db, acteur, "mise-a-jour.demandee", $"Mise à jour demandée : {VersionApplication.Courante} → {s.Version}");
            await db.SaveChangesAsync();
            return Dto(etat, installation.Lire() with { Statut = "demandee", Version = s.Version, Message = "Demande transmise au service de mise à jour." });
        });
    }
}
