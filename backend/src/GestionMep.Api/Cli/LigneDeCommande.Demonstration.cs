using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;

namespace GestionMep.Api.Cli;

/// <summary>Mode démonstration : réinitialisation de la base (même opération que le bandeau de l'application).</summary>
public static partial class LigneDeCommande
{
    /// <summary>
    /// « gestion-mep demo reinitialiser --oui » : refusée hors démonstration (DEMO=true), demande confirmation (--oui). Possible application
    /// en marche : tout se fait dans une transaction, et les sessions ouvertes sont refusées à la requête suivante.
    /// </summary>
    private static async Task<int> DemoReinitialiser(string[] arguments)
    {
        var o = Options.Lire(arguments, [], "--oui");
        var config = Configuration();
        if (!Demonstration.Active(config)) throw new ErreurHttp(1, $"{Demonstration.MessageInactif} Réinitialisation refusée.");
        Confirmer(o.A("--oui"),
        [
            "La réinitialisation remet la base dans l'état initial de la démonstration : comptes, MEP, catalogue, résolutions,",
            "critères, paramètres, réglages et journaux sont recréés ; les pièces jointes sont supprimées ; les sessions sont fermées.",
        ], "gestion-mep demo reinitialiser --oui");
        await using var db = Base(config);
        var (meps, fichiers) = await Demonstration.Reinitialiser(db, config, new PasswordHasher<Utilisateur>(), new StockagePiecesJointes(config),
            DateTimeOffset.UtcNow, null);
        if (EnJson) EcrireJson(new ReinitialisationDemoDto(meps, fichiers));
        else
        {
            Informer($"Démonstration réinitialisée : {meps} MEP, comptes {string.Join(", ", DonneesDemo.Comptes.Select(c => c.Login))} "
                + $"(mot de passe {DonneesDemo.MotDePasse}), {fichiers} fichier(s) de pièce jointe supprimé(s).");
            Informer("Les réglages modifiés depuis la console sont revenus aux valeurs du fichier .env (sous 10 secondes pour l'application en marche).");
        }
        return 0;
    }
}
