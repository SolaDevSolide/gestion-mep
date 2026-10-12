using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Mode démonstration (<c>Demo:Activer</c>, variable <c>DEMO=true</c>, lu au démarrage) : connexion rapide aux comptes de
/// démonstration et réinitialisation de la base. Hors démonstration, rien de tout cela n'est exposé (routes non déclarées,
/// commande refusée). Les opérations sont partagées entre l'API et <c>gestion-mep demo</c> (acteur null : ligne de commande).
/// </summary>
public static class Demonstration
{
    public const string Cle = "Demo:Activer";
    public const string MessageInactif = "Le mode démonstration n'est pas activé (DEMO=true dans le fichier .env).";

    public static bool Active(IConfiguration config) => config.GetValue<bool>(Cle);

    /// <summary>Une seule réinitialisation à la fois sur ce serveur (la seconde est refusée, pas mise en attente).</summary>
    private static readonly SemaphoreSlim EnCours = new(1, 1);

    /// <summary>
    /// Comptes de démonstration proposés par la connexion rapide : ceux de <see cref="DonneesDemo.Comptes"/> présents en base
    /// et actifs, dans l'ordre de la liste. Un compte qui ne porte pas un login de démonstration n'y figure jamais.
    /// </summary>
    public static async Task<List<CompteDemoDto>> Comptes(MepDbContext db)
    {
        var logins = DonneesDemo.Comptes.Select(c => c.Login).ToList();
        var enBase = await db.Utilisateurs.AsNoTracking().Where(u => logins.Contains(u.Login) && u.Actif).ToDictionaryAsync(u => u.Login);
        return DonneesDemo.Comptes.Where(c => enBase.ContainsKey(c.Login))
            .Select(c => new CompteDemoDto(c.Login, enBase[c.Login].NomAffiche, enBase[c.Login].Role, enBase[c.Login].EstAdmin, c.Description))
            .ToList();
    }

    /// <summary>
    /// Remet la base dans l'état d'une base neuve avec <c>DEMO=true</c> : toutes les tables sont vidées (comptes, MEP,
    /// journaux, catalogue, résolutions, critères, paramètres, réglages, connexion unique, pièces jointes) puis
    /// ré-amorcées par <see cref="Amorcage.CreerDemonstration"/>, dans une seule transaction. Les fichiers de pièces jointes
    /// sont ensuite supprimés. Les comptes sont recréés avec de nouveaux identifiants : toutes les sessions ouvertes sont
    /// refusées à la requête suivante. Le journal d'administration repart de la ligne « Démonstration réinitialisée par … ».
    /// </summary>
    /// <returns>Nombre de MEP (hors modèles) et de fichiers de pièces jointes supprimés.</returns>
    public static async Task<(int Meps, int Fichiers)> Reinitialiser(MepDbContext db, IConfiguration config, IPasswordHasher<Utilisateur> hasher,
        StockagePiecesJointes stockage, DateTimeOffset maintenant, Acteur? acteur)
    {
        if (!Active(config)) throw ErreurHttp.NonTrouve(MessageInactif);
        if (!await EnCours.WaitAsync(0)) throw ErreurHttp.Conflit("Une réinitialisation de la démonstration est déjà en cours.");
        try
        {
            var login = acteur is null ? null
                : await db.Utilisateurs.AsNoTracking().Where(u => u.Id == acteur.Id).Select(u => u.Login).FirstOrDefaultAsync();
            db.ChangeTracker.Clear();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var tables = db.Model.GetEntityTypes().Where(t => !t.IsOwned()).Select(t => t.GetTableName()).OfType<string>().Distinct()
                    .Select(t => "\"" + t.Replace("\"", "\"\"") + "\"");
                // Noms de tables issus du modèle EF (aucune saisie).
                var vider = "TRUNCATE TABLE " + string.Join(", ", tables) + " RESTART IDENTITY CASCADE";
                await db.Database.ExecuteSqlRawAsync(vider);
                // Lignes uniques créées par les migrations sur une base neuve.
                db.ConfigurationsSso.Add(new ConfigurationSso { Id = ConfigurationSso.IdUnique });
                await Amorcage.CreerDemonstration(db, hasher, maintenant);
                var auteur = login is null ? null : db.ChangeTracker.Entries<Utilisateur>().Select(e => e.Entity).FirstOrDefault(u => u.Login == login);
                db.JournalAdmin.Add(new EvenementAdmin
                {
                    Quand = maintenant, UtilisateurId = auteur?.Id, Auteur = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, Type = "demo.reinitialisee",
                    Detail = acteur is null ? "Démonstration réinitialisée par la ligne de commande"
                        : $"Démonstration réinitialisée par {acteur.Nom}{(login is null ? "" : $" ({login})")}",
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            db.ChangeTracker.Clear();
            // Plus aucune pièce jointe en base : tous les fichiers du dossier sont à supprimer.
            var fichiers = stockage.SupprimerTout();
            return (await db.Meps.CountAsync(m => !m.EstModele), fichiers);
        }
        finally
        {
            EnCours.Release();
        }
    }
}
