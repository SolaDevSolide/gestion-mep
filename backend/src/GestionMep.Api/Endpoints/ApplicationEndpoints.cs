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

    public static MiseAJourDto Dto(EtatMiseAJour e, EtatInstallateur i) => new(e.Active, e.Depot, VersionApplication.Courante,
        e.DerniereVersion, e.PublieeLe, e.NotesUrl, e.VerifieeLe, e.Erreur,
        VersionApplication.EstPlusRecente(e.DerniereVersion, VersionApplication.Courante),
        new InstallateurDto(i.Present, i.Statut, i.Version, i.Message, i.Le));

    /// <summary>
    /// Demande au service « mise-a-jour » d'installer <paramref name="version"/> (console et <c>gestion-mep mettre-a-jour</c> ;
    /// acteur null : ligne de commande). Refusée (422) si le service est absent ou occupé, si la version n'est pas un numéro
    /// publié (1.2.3) plus récent que la version en cours, s'il dépasse la dernière version publiée connue, ou pendant une
    /// MEP en cours. Sans vérification sur GitHub (registre interne, serveur sans Internet), toute version plus récente peut
    /// être demandée : le service vérifie qu'elle existe dans son registre (MEP_DEPOT_IMAGE) avant de rien changer.
    /// </summary>
    public static async Task<MiseAJourDto> DemanderInstallation(MepDbContext db, Acteur? acteur, string? version, EtatMiseAJour etat,
        InstallationMiseAJour installation)
    {
        var installateur = installation.Lire();
        // Sans Docker, pas de service de mise à jour : le script d'installation s'en charge, sur le serveur.
        if (!installateur.Present && !Installation.EstDocker)
            throw ErreurHttp.Regle([$"Installation sans Docker : la mise à jour se fait sur le serveur, avec « {Installation.CommandeMiseAJour(version ?? etat.DerniereVersion ?? "x.y.z")} »."]);
        if (!installateur.Present)
            throw ErreurHttp.Regle(["Le service de mise à jour n'est pas actif : l'équipe informatique doit l'activer (documentation Exploiter)."]);
        if (installateur.Statut == "en-cours")
            throw ErreurHttp.Regle(["Une mise à jour est déjà en cours."]);
        if (version is null || version.Split('.') is not [{ Length: > 0 } a, { Length: > 0 } b, { Length: > 0 } c]
            || !$"{a}{b}{c}".All(char.IsAsciiDigit) || !VersionApplication.EstPlusRecente(version, VersionApplication.Courante))
            throw ErreurHttp.Regle([$"Numéro de version attendu (1.2.3), plus récent que la version en cours ({VersionApplication.Courante})."]);
        if (etat.DerniereVersion is { } derniere && VersionApplication.EstPlusRecente(version, derniere))
            throw ErreurHttp.Regle([$"La version {version} n'est pas publiée : la dernière version publiée est {derniere}."]);
        var enCours = await db.Meps.CountAsync(m => !m.EstModele && PhasesEnCours.Contains(m.Phase));
        if (enCours > 0)
            throw ErreurHttp.Regle([$"{enCours} MEP en cours (exécution, recette ou retour arrière) : mettre à jour une fois terminée(s)."]);

        installation.Demander(version, acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande);
        JournalAdmin.Ecrire(db, acteur, "mise-a-jour.demandee", $"Mise à jour demandée : {VersionApplication.Courante} → {version}");
        await db.SaveChangesAsync();
        return Dto(etat, installation.Lire() with { Statut = "demandee", Version = version, Message = "Demande transmise au service de mise à jour." });
    }

    public static void MapApplication(this RouteGroupBuilder api)
    {
        // Tout compte connecté : version et lien de signalement. L'état des mises à jour n'est donné qu'aux administrateurs.
        api.MapGet("/application", (ClaimsPrincipal user, IConfiguration config, VerificationMisesAJour verification, InstallationMiseAJour installation) =>
        {
            var lien = config["Signalement:Lien"];
            return new ApplicationDto(
                VersionApplication.Courante,
                lien is null ? LienSignalementParDefaut : (lien.Trim() is { Length: > 0 } l ? l : null),
                Acteur.Depuis(user).EstAdmin ? Dto(verification.Etat, installation.Lire()) : null,
                Installation.Mode);
        });

        var admin = api.MapGroup("/admin/mises-a-jour").ExigerAdmin();

        admin.MapPost("/verifier", async (VerificationMisesAJour verification, InstallationMiseAJour installation, CancellationToken ct) =>
            Dto(await verification.Verifier(ct), installation.Lire()));

        // Installe la dernière version publiée par le service « mise-a-jour ». Refusé pendant une MEP en cours.
        admin.MapPost("/installer", (InstallationSaisie s, ClaimsPrincipal user, MepDbContext db,
            VerificationMisesAJour verification, InstallationMiseAJour installation) =>
            DemanderInstallation(db, Acteur.Depuis(user), s.Version, verification.Etat, installation));
    }
}
