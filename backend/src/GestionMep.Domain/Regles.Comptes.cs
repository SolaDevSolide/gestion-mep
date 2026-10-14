namespace GestionMep.Domain;

// Règles métier : équipes et comptes reliés d'une même personne.
public static partial class Regles
{
    public const int LongueurMaxNomEquipe = 80;

    /// <summary>
    /// Relie <paramref name="compte"/> à <paramref name="principal"/> (même personne). Un seul niveau : le compte principal
    /// n'est relié à aucun autre, le compte relié n'a lui-même aucun compte relié. Rien n'est fusionné ni supprimé.
    /// </summary>
    public static void Relier(Utilisateur compte, Utilisateur principal, IEnumerable<Utilisateur> tous)
    {
        if (compte.Id == principal.Id) throw new RegleMetierException("Un compte ne peut pas être relié à lui-même.");
        if (compte.ComptePrincipalId == principal.Id)
            throw new RegleMetierException($"Le compte {compte.Login} est déjà relié à {principal.NomAffiche} ({principal.Login}).");
        if (compte.ComptePrincipalId is not null)
            throw new RegleMetierException($"Le compte {compte.Login} est déjà relié à un autre compte : défaire d'abord cette liaison.");
        if (principal.ComptePrincipalId is not null)
            throw new RegleMetierException($"Le compte {principal.Login} est lui-même relié à un autre compte : choisir ce compte principal.");
        var lies = tous.Where(u => u.ComptePrincipalId == compte.Id && u.Id != compte.Id).Select(u => u.Login).ToList();
        if (lies.Count > 0)
            throw new RegleMetierException($"Le compte {compte.Login} est le compte principal de {string.Join(", ", lies)} : "
                + "le choisir comme compte principal, ou défaire d'abord ces liaisons.");
        compte.ComptePrincipalId = principal.Id;
    }

    /// <summary>Défait la liaison : le compte redevient une personne à part entière, avec son propre nom.</summary>
    public static void Delier(Utilisateur compte)
    {
        if (compte.ComptePrincipalId is null) throw new RegleMetierException($"Le compte {compte.Login} n'est relié à aucun autre compte.");
        compte.ComptePrincipalId = null;
    }

    /// <summary>Nom d'équipe : obligatoire, de longueur raisonnable, distinct de celui des autres équipes (sans tenir compte de la casse).</summary>
    public static string NomEquipe(string? nom, IEnumerable<Equipe> autres)
    {
        var n = (nom ?? "").Trim();
        if (n.Length == 0) throw new RegleMetierException("Le nom de l'équipe est obligatoire.");
        if (n.Length > LongueurMaxNomEquipe) throw new RegleMetierException($"Le nom de l'équipe ne doit pas dépasser {LongueurMaxNomEquipe} caractères.");
        if (autres.Any(e => string.Equals(e.Nom, n, StringComparison.OrdinalIgnoreCase)))
            throw new RegleMetierException($"L'équipe « {n} » existe déjà.");
        return n;
    }

    /// <summary>
    /// Approbateurs : une même personne (comptes reliés) n'approuve qu'une fois, et jamais avec un compte relié à celui du pilote.
    /// </summary>
    public static void ExigerApprobateursDistincts(Mep mep, IReadOnlyList<Guid> approbateurs, AnnuairePersonnes annuaire)
    {
        var doublon = approbateurs.GroupBy(annuaire.Personne).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
            throw new RegleMetierException($"{annuaire.NomPersonne(doublon.Key)} figure plusieurs fois parmi les approbateurs avec des comptes reliés : "
                + "une personne n'approuve qu'une fois.");
        if (CompteDuPilote(mep) is not { } pilote) return;
        var relie = approbateurs.Where(a => a != pilote && annuaire.Personne(a) == annuaire.Personne(pilote)).Select(a => (Guid?)a).FirstOrDefault();
        if (relie is { } r)
            throw new RegleMetierException($"{annuaire.NomPersonne(r)} est le pilote (avec un compte relié) : il ne peut pas approuver sa propre MEP.");
    }
}
