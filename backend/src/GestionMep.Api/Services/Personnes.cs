using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Chargement de l'<see cref="AnnuairePersonnes"/> (comptes reliés et équipes) et comptes vus comme des personnes : nom du
/// compte principal, équipes. Partout où un compte est nommé (MEP, pièces jointes, résolutions types, exports), le nom est
/// celui de la personne.
/// </summary>
public static class Personnes
{
    public static async Task<AnnuairePersonnes> Charger(MepDbContext db, bool avecEquipes = true)
    {
        var comptes = await db.Utilisateurs.AsNoTracking()
            .Select(u => new AnnuairePersonnes.Compte(u.Id, u.ComptePrincipalId, u.NomAffiche, u.Login, u.Actif)).ToListAsync();
        var equipes = avecEquipes ? await db.Equipes.AsNoTracking().ToListAsync() : [];
        return new AnnuairePersonnes(comptes, equipes);
    }

    /// <summary>Nom affiché de chaque compte : celui de sa personne (compte principal).</summary>
    public static async Task<Dictionary<Guid, string>> Noms(MepDbContext db) => (await Charger(db, avecEquipes: false)).NomsAffiches();

    public static UtilisateurDto Dto(Utilisateur u, AnnuairePersonnes annuaire) => MepMapping.VersDto(u,
        annuaire.NomPersonne(u.Id) ?? u.NomAffiche, [.. annuaire.EquipesDuCompte(u.Id).Order()]);

    public static async Task<UtilisateurDto> Dto(MepDbContext db, Utilisateur u) => Dto(u, await Charger(db));

    /// <summary>Tous les comptes (y compris désactivés et reliés), triés par nom affiché.</summary>
    public static async Task<List<UtilisateurDto>> Comptes(MepDbContext db)
    {
        var annuaire = await Charger(db);
        return (await db.Utilisateurs.AsNoTracking().OrderBy(u => u.NomAffiche).ToListAsync()).Select(u => Dto(u, annuaire)).ToList();
    }
}
