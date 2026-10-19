namespace GestionMep.Domain;

// Règles métier : environnements des systèmes et des étapes.
public static partial class Regles
{
    /// <summary>Environnement de production d'après son nom (« Production », « prod », « PROD-2 »…) : valeur proposée par défaut.</summary>
    public static bool NomDeProduction(string nom) => nom.Trim().StartsWith("prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai si l'un des systèmes de la MEP touche un environnement de production.</summary>
    public static bool ToucheProduction(Mep mep) => mep.Systemes.Any(s => s.Environnements.Any(e => e.Production));

    /// <summary>Texte « BDD : Préproduction, Production · WEB : Production » (journal, exports).</summary>
    public static string ResumeEnvironnements(Mep mep) =>
        string.Join(" · ", mep.Systemes.Where(s => s.Environnements.Count > 0)
            .Select(s => $"{s.Nom} : {string.Join(", ", s.Environnements.Select(e => e.Nom))}"));

    /// <summary>
    /// Choisit les environnements touchés par la MEP sur un système (préparation uniquement), dans l'ordre reçu, sans doublon.
    /// Système du catalogue : seuls ses environnements (<paramref name="catalogue"/>) sont admis, avec leur marque
    /// « production » ; un environnement déjà choisi puis retiré du catalogue peut être gardé. Système hors catalogue
    /// (<paramref name="catalogue"/> null) : nom libre, production déduite du nom.
    /// </summary>
    public static void DefinirEnvironnementsSysteme(Mep mep, Systeme systeme, IEnumerable<string> noms, IReadOnlyList<EnvironnementCatalogue>? catalogue)
    {
        ExigerPreparation(mep);
        var choisis = new List<EnvironnementSysteme>();
        foreach (var brut in noms)
        {
            var nom = (brut ?? "").Trim();
            if (nom.Length == 0) continue;
            if (nom.Length > 100) throw new SaisieInvalideException("Nom d'environnement trop long (100 caractères au plus).");
            if (choisis.Any(c => c.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))) continue;
            var deja = systeme.Environnements.FirstOrDefault(e => e.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase));
            if (catalogue is null)
            {
                choisis.Add(new EnvironnementSysteme { Nom = deja?.Nom ?? nom, Production = deja?.Production ?? NomDeProduction(nom) });
                continue;
            }
            var connu = catalogue.FirstOrDefault(e => e.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase));
            if (connu is not null) choisis.Add(new EnvironnementSysteme { Nom = connu.Nom, Production = connu.Production });
            else if (deja is not null) choisis.Add(new EnvironnementSysteme { Nom = deja.Nom, Production = deja.Production });
            else throw new SaisieInvalideException($"L'environnement « {nom} » n'existe pas dans la fiche {systeme.Nom} du catalogue : l'y ajouter d'abord.");
        }
        systeme.Environnements = choisis;
    }

    /// <summary>
    /// Environnements d'une étape, système par système. Chaque système doit être l'un de ceux de l'étape ; chaque
    /// environnement, l'un de ceux choisis pour ce système dans la MEP (un environnement que l'étape avait déjà reste admis,
    /// même retiré du système depuis : il est signalé par la vérification du plan).
    /// </summary>
    public static void DefinirEnvironnementsEtape(Mep mep, Etape etape, IEnumerable<(Guid SystemeId, string Nom)> environnements)
    {
        var avant = etape.Environnements;
        var choisis = new List<EnvironnementEtape>();
        foreach (var (systemeId, brut) in environnements)
        {
            var nom = (brut ?? "").Trim();
            if (nom.Length == 0) continue;
            var systeme = mep.Systemes.FirstOrDefault(s => s.Id == systemeId)
                ?? throw new SaisieInvalideException("Environnement de l'étape : système inconnu dans cette MEP.");
            if (!etape.SystemeIds.Contains(systemeId))
                throw new SaisieInvalideException($"Environnement de l'étape : {systeme.Nom} n'est pas un système de l'étape.");
            if (choisis.Any(c => c.SystemeId == systemeId && c.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))) continue;
            var connu = systeme.Environnements.FirstOrDefault(e => e.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))?.Nom
                ?? avant.FirstOrDefault(e => e.SystemeId == systemeId && e.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))?.Nom
                ?? throw new SaisieInvalideException($"L'environnement « {nom} » n'est pas choisi pour {systeme.Nom} dans cette MEP (Préparation › Systèmes).");
            choisis.Add(new EnvironnementEtape { SystemeId = systemeId, Nom = connu });
        }
        // Ordre stable : systèmes de l'étape, puis environnements dans l'ordre du système.
        etape.Environnements = choisis
            .OrderBy(c => etape.SystemeIds.IndexOf(c.SystemeId))
            .ThenBy(c => IndexEnvironnement(mep, c))
            .ToList();
    }

    private static int IndexEnvironnement(Mep mep, EnvironnementEtape e)
    {
        var liste = mep.Systemes.FirstOrDefault(s => s.Id == e.SystemeId)?.Environnements ?? [];
        var i = liste.FindIndex(x => x.Nom.Equals(e.Nom, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>Retire de l'étape les environnements des systèmes qu'elle ne touche plus.</summary>
    public static void NettoyerEnvironnementsEtape(Etape etape)
    {
        if (etape.Environnements.Any(e => !etape.SystemeIds.Contains(e.SystemeId)))
            etape.Environnements = etape.Environnements.Where(e => etape.SystemeIds.Contains(e.SystemeId)).ToList();
    }

    /// <summary>
    /// Environnements d'étapes qui ne sont plus choisis pour leur système (retirés dans Préparation › Systèmes) : erreur
    /// bloquante, l'étape agirait sur un environnement que la MEP ne déclare plus.
    /// </summary>
    public static List<string> ErreursEnvironnements(Mep mep)
    {
        var erreurs = new List<string>();
        foreach (var e in mep.EtapesTriees().Where(e => mep.RetourArriereActive || e.Plan == TypePlan.Deploiement))
            foreach (var env in e.Environnements)
            {
                var systeme = mep.Systemes.FirstOrDefault(s => s.Id == env.SystemeId);
                if (systeme is null) continue;
                if (systeme.Environnements.All(x => !x.Nom.Equals(env.Nom, StringComparison.OrdinalIgnoreCase)))
                    erreurs.Add($"{e.Code} : l'environnement {env.Nom} n'est plus choisi pour {systeme.Nom} (Préparation › Systèmes) ; le choisir de nouveau ou le retirer de l'étape.");
            }
        return erreurs;
    }
}
