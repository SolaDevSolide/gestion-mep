using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Écriture dans le journal d'administration (enregistré avec le prochain <c>SaveChangesAsync</c>).</summary>
public static class JournalAdmin
{
    public const string AuteurLigneDeCommande = "Ligne de commande";

    public static void Ecrire(MepDbContext db, Acteur? acteur, string type, string detail) =>
        db.JournalAdmin.Add(new EvenementAdmin
        {
            UtilisateurId = acteur?.Id,
            Auteur = acteur?.Nom ?? AuteurLigneDeCommande,
            Type = type,
            Detail = detail,
        });

    /// <summary>« Alice (alice) » : nom et login d'un compte, pour le détail des événements.</summary>
    public static string Compte(Utilisateur u) => $"{u.NomAffiche} ({u.Login})";
}
