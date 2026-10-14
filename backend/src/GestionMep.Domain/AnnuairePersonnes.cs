namespace GestionMep.Domain;

/// <summary>
/// Personnes de l'application : chaque compte appartient à une personne, représentée par son compte principal. Des comptes
/// reliés (<see cref="Utilisateur.ComptePrincipalId"/>) forment une seule personne : nom du compte principal, équipes de
/// tous ses comptes. Rien n'est fusionné : chaque compte garde ses données et son historique.
/// </summary>
public sealed class AnnuairePersonnes
{
    /// <summary>Ce qu'il faut savoir d'un compte pour le rattacher à sa personne.</summary>
    public sealed record Compte(Guid Id, Guid? ComptePrincipalId, string NomAffiche, string Login, bool Actif);

    private readonly Dictionary<Guid, Compte> _comptes;
    private readonly Dictionary<Guid, List<Compte>> _parPersonne;
    private readonly Dictionary<Guid, HashSet<Guid>> _equipesDuCompte;

    public AnnuairePersonnes(IEnumerable<Compte> comptes, IEnumerable<Equipe> equipes)
    {
        _comptes = comptes.ToDictionary(c => c.Id);
        _parPersonne = _comptes.Values.GroupBy(Personne).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id == g.Key ? 0 : 1).ThenBy(c => c.Login).ToList());
        _equipesDuCompte = [];
        foreach (var e in equipes)
            foreach (var m in e.MembreIds)
            {
                if (!_equipesDuCompte.TryGetValue(m, out var set)) _equipesDuCompte[m] = set = [];
                set.Add(e.Id);
            }
    }

    public static AnnuairePersonnes De(IEnumerable<Utilisateur> comptes, IEnumerable<Equipe> equipes) =>
        new(comptes.Select(u => new Compte(u.Id, u.ComptePrincipalId, u.NomAffiche, u.Login, u.Actif)), equipes);

    public Compte? Trouver(Guid compteId) => _comptes.GetValueOrDefault(compteId);

    private Guid Personne(Compte c) => c.ComptePrincipalId is { } p && _comptes.ContainsKey(p) ? p : c.Id;

    /// <summary>Identifiant de la personne (son compte principal) ; le compte lui-même s'il n'est relié à aucun autre ou inconnu.</summary>
    public Guid Personne(Guid compteId) => _comptes.TryGetValue(compteId, out var c) ? Personne(c) : compteId;

    /// <summary>Nom affiché de la personne : celui du compte principal (null si le compte est inconnu).</summary>
    public string? NomPersonne(Guid compteId) => _comptes.GetValueOrDefault(Personne(compteId))?.NomAffiche;

    /// <summary>Tous les comptes de la personne de ce compte, le principal en premier (vide si le compte est inconnu).</summary>
    public IReadOnlyList<Compte> ComptesDeLaPersonne(Guid compteId) =>
        _parPersonne.TryGetValue(Personne(compteId), out var l) ? l : [];

    /// <summary>Équipes dont le compte lui-même est membre.</summary>
    public IReadOnlyCollection<Guid> EquipesDuCompte(Guid compteId) =>
        _equipesDuCompte.TryGetValue(compteId, out var s) ? s : [];

    /// <summary>Équipes de la personne : celles de tous ses comptes reliés.</summary>
    public IReadOnlySet<Guid> EquipesDeLaPersonne(Guid compteId) =>
        ComptesDeLaPersonne(compteId).SelectMany(c => EquipesDuCompte(c.Id)).ToHashSet();

    /// <summary>Nom à afficher pour chaque compte : celui de sa personne.</summary>
    public Dictionary<Guid, string> NomsAffiches() => _comptes.Keys.ToDictionary(id => id, id => NomPersonne(id)!);

    /// <summary>Noms et logins de tous les comptes de la personne : une recherche sur l'un retrouve la personne entière.</summary>
    public IEnumerable<string> TermesDeRecherche(Guid compteId) =>
        ComptesDeLaPersonne(compteId).SelectMany(c => new[] { c.NomAffiche, c.Login });
}
