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
            j.DerniereUtilisationLe, j.DerniereAdresseIp, j.UtilisateurId, l.Login, l.Nom, j.RequetesParMinute);
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
    /// Jeton de service. Sans <see cref="JetonServiceSaisie.CompteTechniqueId"/>, un compte technique est créé au nom du jeton
    /// (login <c>svc-…</c>, sans mot de passe, impossible à connecter) pour qu'il signe le journal et qu'on puisse l'ajouter à
    /// une MEP ; le nom ne peut alors pas imiter un autre compte (400). Avec, le jeton agit sous ce compte technique existant
    /// (« Recréer » un jeton qui arrive à expiration garde ainsi le même compte, ses équipes et ses MEP) : 422 si ce n'est
    /// pas un compte technique ; un compte technique désactivé (ses jetons révoqués) est réactivé.
    /// </summary>
    public static async Task<JetonCreeDto> CreerService(MepDbContext db, Acteur? acteur, JetonServiceSaisie c, DateTimeOffset maintenant)
    {
        var debit = Regles.ValiderDebitJeton(TypeJeton.Service, c.RequetesParMinute);
        var saisie = new JetonSaisie(c.Nom, c.Portees, c.DureeJours);
        Utilisateur compte;
        var nouveauCompte = c.CompteTechniqueId is null;
        if (c.CompteTechniqueId is { } id)
        {
            compte = await db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == id) ?? throw ErreurHttp.NonTrouve("Compte technique introuvable.");
            if (!compte.EstTechnique)
                throw ErreurHttp.Regle([$"{compte.NomAffiche} n'est pas un compte technique : un jeton de service n'agit jamais au nom d'une personne."]);
        }
        else compte = new Utilisateur { EstTechnique = true, MotDePasseHash = "", Role = RoleCompteTechnique };
        var (jeton, secret) = Nouveau(TypeJeton.Service, saisie, compte.Id, acteur, maintenant);
        jeton.RequetesParMinute = debit;
        if (nouveauCompte)
        {
            await AuthentificationLocale.ExigerNomDisponible(db, jeton.Nom, null);
            compte.NomAffiche = jeton.Nom;
            compte.Login = await LoginLibre(db, Regles.LoginCompteTechnique(jeton.Nom));
            db.Utilisateurs.Add(compte);
        }
        else if (!compte.Actif) compte.Actif = true;
        db.JetonsApi.Add(jeton);
        JournalAdmin.Ecrire(db, acteur, "jeton.cree",
            $"{Libelle(jeton)} créé : portées {string.Join(", ", jeton.Portees)}, expire le {Date(jeton.ExpireLe)}, "
            + $"compte technique {compte.Login}{(nouveauCompte ? "" : " (existant)")}{(debit is { } d ? $", {d} requêtes par minute" : "")}");
        await db.SaveChangesAsync();
        return new JetonCreeDto(await Dto(db, jeton, maintenant), secret);
    }

    /// <summary>Login proposé, ou suivi de -2, -3… s'il est déjà pris.</summary>
    private static async Task<string> LoginLibre(MepDbContext db, string login)
    {
        var pris = await db.Utilisateurs.AsNoTracking().Where(u => u.Login.StartsWith(login)).Select(u => u.Login).ToListAsync();
        if (!pris.Contains(login)) return login;
        for (var i = 2; ; i++)
            if (!pris.Contains($"{login}-{i}")) return $"{login}-{i}";
    }

    /// <summary>
    /// Révocation définitive (422 si déjà révoqué). Le compte technique d'un jeton de service est aussi désactivé quand il n'a
    /// plus d'autre jeton actif : il garde son nom dans l'historique des MEP.
    /// </summary>
    public static async Task<JetonDto> Revoquer(MepDbContext db, Acteur? acteur, JetonApi jeton, DateTimeOffset maintenant)
    {
        Regles.RevoquerJeton(jeton, acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, maintenant);
        var compte = await db.Utilisateurs.FirstAsync(u => u.Id == jeton.UtilisateurId);
        if (jeton.Type == TypeJeton.Service && compte.EstTechnique && compte.Actif
            && !await db.JetonsApi.AnyAsync(j => j.UtilisateurId == compte.Id && j.Id != jeton.Id && j.RevoqueLe == null && j.ExpireLe > maintenant))
        {
            compte.Actif = false;
            compte.TamponSecurite = Guid.NewGuid();
        }
        JournalAdmin.Ecrire(db, acteur, "jeton.revoque",
            jeton.Type == TypeJeton.Service ? $"{Libelle(jeton)} révoqué" : $"{Libelle(jeton)} de {JournalAdmin.Compte(compte)} révoqué");
        await db.SaveChangesAsync();
        return await Dto(db, jeton, maintenant);
    }

    /// <summary>
    /// Compte désactivé : ses jetons personnels encore actifs sont révoqués aussitôt (réactiver le compte ne les fait pas
    /// revenir), chacun inscrit au journal. L'appelant enregistre. Renvoie le nombre de jetons révoqués.
    /// </summary>
    public static async Task<int> RevoquerPersonnels(MepDbContext db, Acteur? acteur, Utilisateur compte, DateTimeOffset maintenant)
    {
        var actifs = await db.JetonsApi.Where(j => j.UtilisateurId == compte.Id && j.Type == TypeJeton.Personnel && j.RevoqueLe == null && j.ExpireLe > maintenant)
            .ToListAsync();
        foreach (var j in actifs)
        {
            Regles.RevoquerJeton(j, acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, maintenant);
            JournalAdmin.Ecrire(db, acteur, "jeton.revoque", $"{Libelle(j)} de {JournalAdmin.Compte(compte)} révoqué : compte désactivé");
        }
        return actifs.Count;
    }

    public static List<PorteeJetonDto> Portees() => PorteesJeton.Toutes
        .Select(p => new PorteeJetonDto(p, PorteesJeton.Descriptions[p], p == PorteesJeton.Admin ? [TypeJeton.Service] : [TypeJeton.Personnel, TypeJeton.Service]))
        .ToList();
}
