namespace GestionMep.Domain;

// Règles métier : fiche d'un système du catalogue (environnements, catégories et objets) et catégories par défaut.
public static partial class Regles
{
    private const int LongueurNomMax = 200;

    private static string Nettoyer(string? texte, string champ, bool obligatoire, int max = LongueurNomMax)
    {
        var t = (texte ?? "").Trim();
        if (obligatoire && t.Length == 0) throw new SaisieInvalideException($"{champ} est obligatoire.");
        if (t.Length > max) throw new SaisieInvalideException($"{champ} : {max} caractères au plus.");
        return t;
    }

    /// <summary>
    /// Remplace les environnements d'un système du catalogue (ordre reçu). Noms obligatoires et uniques (sans tenir compte
    /// de la casse) ; production non précisée : déduite du nom. Les MEP gardent les environnements déjà choisis.
    /// </summary>
    public static void DefinirEnvironnementsCatalogue(SystemeCatalogue systeme, IEnumerable<(string? Nom, string? Cible, bool? Production)> environnements)
    {
        var liste = new List<EnvironnementCatalogue>();
        foreach (var (nom, cible, production) in environnements)
        {
            var n = Nettoyer(nom, "Le nom de l'environnement", obligatoire: true, max: 100);
            if (liste.Any(e => e.Nom.Equals(n, StringComparison.OrdinalIgnoreCase)))
                throw new SaisieInvalideException($"L'environnement « {n} » est en double.");
            liste.Add(new EnvironnementCatalogue { Nom = n, Cible = Nettoyer(cible, "La cible", obligatoire: false, max: 500), Production = production ?? NomDeProduction(n) });
        }
        systeme.Environnements = liste;
    }

    /// <summary>
    /// Remplace les catégories d'un système du catalogue (ordre reçu). <c>Ancien</c> renomme une catégorie existante (ses
    /// objets la suivent) ; une catégorie absente de la liste est retirée avec ses objets. Renvoie le nombre d'objets retirés.
    /// Les MEP gardent leurs copies des objets.
    /// </summary>
    public static int DefinirCategoriesCatalogue(SystemeCatalogue systeme, IEnumerable<(string? Nom, string? Ancien)> categories)
    {
        var noms = new List<string>();
        var renommages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nom, ancien) in categories)
        {
            var n = Nettoyer(nom, "Le nom de la catégorie", obligatoire: true, max: 100);
            if (noms.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase)))
                throw new SaisieInvalideException($"La catégorie « {n} » est en double.");
            noms.Add(n);
            var a = (ancien ?? "").Trim();
            var source = a.Length > 0 ? a : n;
            var existante = systeme.Categories.FirstOrDefault(c => c.Equals(source, StringComparison.OrdinalIgnoreCase));
            if (existante is not null && !renommages.ContainsKey(existante)) renommages[existante] = n;
        }
        var avant = systeme.Objets.Count;
        systeme.Objets = systeme.Objets
            .Where(o => renommages.ContainsKey(o.Categorie))
            .Select(o => new ObjetCatalogue { Id = o.Id, Categorie = renommages[o.Categorie], Type = o.Type, Nom = o.Nom, Libelle = o.Libelle, Precision = o.Precision })
            .ToList();
        foreach (var g in systeme.Objets.GroupBy(o => (o.Categorie.ToUpperInvariant(), o.Nom.ToUpperInvariant())).Where(g => g.Count() > 1))
            throw new SaisieInvalideException($"Deux objets « {g.First().Nom} » se retrouveraient dans la catégorie {g.First().Categorie}.");
        systeme.Categories = noms;
        return avant - systeme.Objets.Count;
    }

    /// <summary>Ajoute (<paramref name="id"/> null) ou modifie un objet du catalogue, après contrôle.</summary>
    public static ObjetCatalogue EnregistrerObjetCatalogue(SystemeCatalogue systeme, Guid? id, string? categorie, string? type, string? nom,
        string? libelle, string? precision)
    {
        var c = Nettoyer(categorie, "La catégorie", obligatoire: true, max: 100);
        var cat = systeme.Categories.FirstOrDefault(x => x.Equals(c, StringComparison.OrdinalIgnoreCase))
            ?? throw new SaisieInvalideException($"La catégorie « {c} » n'existe pas dans la fiche {systeme.Nom} : l'ajouter d'abord.");
        var n = Nettoyer(nom, "Le nom de l'objet", obligatoire: true);
        if (systeme.Objets.Any(o => o.Id != id && o.Categorie == cat && o.Nom.Equals(n, StringComparison.OrdinalIgnoreCase)))
            throw new SaisieInvalideException($"L'objet « {n} » existe déjà dans la catégorie {cat} de {systeme.Nom}.");
        var objet = new ObjetCatalogue
        {
            Id = id ?? Guid.NewGuid(), Categorie = cat, Type = Nettoyer(type, "Le type", obligatoire: false, max: 100), Nom = n,
            Libelle = Nettoyer(libelle, "Le libellé", obligatoire: false, max: 500), Precision = Nettoyer(precision, "La précision", obligatoire: false, max: 500),
        };
        if (id is { } existant)
        {
            var i = systeme.Objets.FindIndex(o => o.Id == existant);
            if (i < 0) throw new RegleMetierException("Objet introuvable dans la fiche du système.");
            systeme.Objets = [.. systeme.Objets[..i], objet, .. systeme.Objets[(i + 1)..]];
        }
        else systeme.Objets = [.. systeme.Objets, objet];
        return objet;
    }

    /// <summary>Catégories par défaut : noms nettoyés, sans doublon ni vide, dans l'ordre reçu.</summary>
    public static List<string> NormaliserCategoriesDefaut(IEnumerable<string?> categories)
    {
        var liste = new List<string>();
        foreach (var c in categories)
        {
            var n = Nettoyer(c, "Le nom de la catégorie", obligatoire: false, max: 100);
            if (n.Length > 0 && !liste.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase))) liste.Add(n);
        }
        return liste;
    }
}
