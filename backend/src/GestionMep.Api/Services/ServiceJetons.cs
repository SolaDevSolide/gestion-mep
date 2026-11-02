using System.Globalization;
using System.Linq.Expressions;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Jetons d'accès à l'API (#128) : création, liste et révocation, partagées par « Mon profil » (<c>/api/jetons</c>), la
/// console (<c>/api/admin/jetons</c>) et <c>gestion-mep jetons</c> (acteur null : « Ligne de commande »). Tout est inscrit
/// au journal d'administration ; le secret n'est jamais conservé ni journalisé.
/// </summary>
public static class ServiceJetons
{
    /// <summary>Rôle affiché du compte technique d'un jeton de service.</summary>
    public const string RoleCompteTechnique = "Compte technique (jeton de service)";

    private sealed record Ligne(JetonApi Jeton, string Login, string Nom);

    private static JetonDto Dto(Ligne l, DateTimeOffset maintenant)
    {
        var j = l.Jeton;
        return new JetonDto(j.Id, j.Nom, j.Type, j.Etat(maintenant), j.Prefixe, [.. j.Portees], j.CreeLe, j.CreePar, j.ExpireLe, j.RevoqueLe, j.RevoquePar,
            j.DerniereUtilisationLe, j.DerniereAdresseIp, j.UtilisateurId, l.Login, l.Nom);
    }

    /// <summary>Jetons choisis, du plus récent au plus ancien, avec le compte au nom duquel chacun agit.</summary>
    public static async Task<List<JetonDto>> Lister(MepDbContext db, Expression<Func<JetonApi, bool>> filtre, DateTimeOffset maintenant)
    {
        var lignes = await db.JetonsApi.AsNoTracking().Where(filtre)
            .Join(db.Utilisateurs, j => j.UtilisateurId, u => u.Id, (j, u) => new Ligne(j, u.Login, u.NomAffiche))
            .ToListAsync();
        return lignes.OrderByDescending(l => l.Jeton.CreeLe).ThenBy(l => l.Jeton.Nom, StringComparer.Ordinal).Select(l => Dto(l, maintenant)).ToList();
    }

    public static async Task<JetonDto> Dto(MepDbContext db, JetonApi j, DateTimeOffset maintenant) =>
        (await Lister(db, x => x.Id == j.Id, maintenant)).Single();

    /// <summary>Jeton par identifiant (404 sinon), éventuellement limité à un compte (« Mon profil » : ses propres jetons).</summary>
    public static async Task<JetonApi> Charger(MepDbContext db, Guid id, Guid? compte = null) =>
        await db.JetonsApi.FirstOrDefaultAsync(j => j.Id == id && (compte == null || j.UtilisateurId == compte && j.Type == TypeJeton.Personnel))
        ?? throw ErreurHttp.NonTrouve("Jeton introuvable.");

    private static string Date(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Libelle(JetonApi j) => $"{(j.Type == TypeJeton.Service ? "Jeton de service" : "Jeton personnel")} « {j.Nom} » ({j.Prefixe})";

    private static (JetonApi Jeton, string Secret) Nouveau(TypeJeton type, JetonSaisie c, Guid compte, Acteur? acteur, DateTimeOffset maintenant)
    {
        var (nom, portees, expireLe) = Regles.ValiderJeton(type, c.Nom, c.Portees, c.DureeJours, maintenant);
        var (secret, prefixe, empreinte) = AuthentificationJeton.Generer();
        return (new JetonApi
        {
            Nom = nom, Type = type, UtilisateurId = compte, Prefixe = prefixe, Empreinte = empreinte, Portees = portees,
            CreeLe = maintenant, CreeParId = acteur?.Compte, CreePar = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, ExpireLe = expireLe,
        }, secret);
    }

    /// <summary>
    /// Jeton personnel du compte : jamais la portée admin (le rôle administrateur ne passe pas par un jeton personnel),
    /// <see cref="Regles.JetonsActifsMaxParCompte"/> jetons actifs au plus (422 au-delà).
    /// </summary>
    public static async Task<JetonCreeDto> CreerPersonnel(MepDbContext db, Acteur? acteur, Utilisateur compte, JetonSaisie c, DateTimeOffset maintenant)
    {
        if (compte.EstTechnique) throw ErreurHttp.Saisie("Un compte technique n'a pas de jeton personnel : ses jetons sont des jetons de service.");
        if (!compte.Actif) throw new ErreurHttp(422, "Ce compte est désactivé : il ne peut pas recevoir de jeton.");
        var (jeton, secret) = Nouveau(TypeJeton.Personnel, c, compte.Id, acteur, maintenant);
        var actifs = await db.JetonsApi.CountAsync(j => j.UtilisateurId == compte.Id && j.RevoqueLe == null && j.ExpireLe > maintenant);
        if (actifs >= Regles.JetonsActifsMaxParCompte)
            throw new ErreurHttp(422, $"{Regles.JetonsActifsMaxParCompte} jetons actifs au plus par compte : révoquer un jeton inutilisé avant d'en créer un autre.");
        db.JetonsApi.Add(jeton);
        JournalAdmin.Ecrire(db, acteur, "jeton.cree",
            $"{Libelle(jeton)} créé pour {JournalAdmin.Compte(compte)} : portées {string.Join(", ", jeton.Portees)}, expire le {Date(jeton.ExpireLe)}");
        await db.SaveChangesAsync();
        return new JetonCreeDto(await Dto(db, jeton, maintenant), secret);
    }

    /// <summary>
    /// Jeton de service : un compte technique est créé à son nom (sans mot de passe, impossible à connecter), pour qu'il
    /// signe le journal et qu'on puisse l'ajouter à une MEP. Le nom ne peut pas imiter un autre compte (400).
    /// </summary>
    public static async Task<JetonCreeDto> CreerService(MepDbContext db, Acteur? acteur, JetonSaisie c, DateTimeOffset maintenant)
    {
        var compte = new Utilisateur { EstTechnique = true, MotDePasseHash = "", Role = RoleCompteTechnique };
        var (jeton, secret) = Nouveau(TypeJeton.Service, c, compte.Id, acteur, maintenant);
        await AuthentificationLocale.ExigerNomDisponible(db, jeton.Nom, null);
        compte.NomAffiche = jeton.Nom;
        compte.Login = $"service-{compte.Id.ToString("N")[..12]}";
        db.Utilisateurs.Add(compte);
        db.JetonsApi.Add(jeton);
        JournalAdmin.Ecrire(db, acteur, "jeton.cree",
            $"{Libelle(jeton)} créé : portées {string.Join(", ", jeton.Portees)}, expire le {Date(jeton.ExpireLe)}, compte technique {compte.Login}");
        await db.SaveChangesAsync();
        return new JetonCreeDto(await Dto(db, jeton, maintenant), secret);
    }

    /// <summary>
    /// Révocation définitive (422 si déjà révoqué). Le compte technique d'un jeton de service est aussi désactivé : il garde
    /// son nom dans l'historique des MEP.
    /// </summary>
    public static async Task<JetonDto> Revoquer(MepDbContext db, Acteur? acteur, JetonApi jeton, DateTimeOffset maintenant)
    {
        Regles.RevoquerJeton(jeton, acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, maintenant);
        var compte = await db.Utilisateurs.FirstAsync(u => u.Id == jeton.UtilisateurId);
        if (jeton.Type == TypeJeton.Service && compte.EstTechnique && compte.Actif)
        {
            compte.Actif = false;
            compte.TamponSecurite = Guid.NewGuid();
        }
        JournalAdmin.Ecrire(db, acteur, "jeton.revoque",
            jeton.Type == TypeJeton.Service ? $"{Libelle(jeton)} révoqué" : $"{Libelle(jeton)} de {JournalAdmin.Compte(compte)} révoqué");
        await db.SaveChangesAsync();
        return await Dto(db, jeton, maintenant);
    }

    public static List<PorteeJetonDto> Portees() => PorteesJeton.Toutes
        .Select(p => new PorteeJetonDto(p, PorteesJeton.Descriptions[p], p == PorteesJeton.Admin ? [TypeJeton.Service] : [TypeJeton.Personnel, TypeJeton.Service]))
        .ToList();
}
