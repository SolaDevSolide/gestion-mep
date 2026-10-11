using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Écriture dans le journal d'administration (enregistré avec le prochain <c>SaveChangesAsync</c>).</summary>
public static class JournalAdmin
{
    public const string AuteurLigneDeCommande = "Ligne de commande";
    /// <summary>Auteur des événements que l'application constate d'elle-même (alertes).</summary>
    public const string AuteurApplication = "Application";

    public static void Ecrire(MepDbContext db, Acteur? acteur, string type, string detail, string? auteur = null) =>
        db.JournalAdmin.Add(new EvenementAdmin
        {
            UtilisateurId = acteur?.Id,
            Auteur = acteur?.Nom ?? auteur ?? AuteurLigneDeCommande,
            Type = type,
            Detail = detail,
        });

    /// <summary>« Alice (alice) » : nom et login d'un compte, pour le détail des événements.</summary>
    public static string Compte(Utilisateur u) => $"{u.NomAffiche} ({u.Login})";
}
