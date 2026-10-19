namespace GestionMep.Domain;

/// <summary>Ce que le passage d'une MEP complète en simplifiée a effacé (pour le journal et la réponse de l'API).</summary>
public record EffacementsSimplification(int Durees, int Criticites, int ResolutionsLiees, bool Versions, int Suppleants);

// Règles métier : MEP complète ou simplifiée.
public static partial class Regles
{
    public static bool EstSimplifiee(Mep mep) => mep.Type == TypeMep.Simplifiee;

    /// <summary>
    /// Suppléants du pilote, qui ont les mêmes droits que lui. MEP complète : ceux qui ont été choisis. MEP simplifiée : toutes
    /// les personnes de la MEP sauf le pilote, dans l'ordre de la liste des personnes (aucune tant que le pilote n'est pas désigné),
    /// hormis les administrateurs ajoutés d'office en démarrant une étape ou en prenant la main (<see cref="Participant.AjouteDOffice"/>) :
    /// ils ne sont qu'exécutants.
    /// </summary>
    public static List<Guid> Suppleants(Mep mep) => EstSimplifiee(mep)
        ? mep.PiloteId is null ? [] : mep.Participants.Where(p => p.Id != mep.PiloteId && !p.AjouteDOffice).Select(p => p.Id).ToList()
        : [.. mep.SuppleantIds];

    /// <summary>
    /// Une étape de MEP simplifiée n'a ni durée, ni criticité, ni résolutions liées : ces champs sont vidés, quelle que soit
    /// la saisie. Sans effet sur une MEP complète.
    /// </summary>
    public static void AppliquerTypeAEtape(Mep mep, Etape etape)
    {
        if (!EstSimplifiee(mep)) return;
        etape.DureeEstimeeMinutes = null;
        etape.Criticite = null;
        etape.ResolutionIds = [];
    }

    private static void ExigerChangementDeType(Mep mep)
    {
        if (mep.EstModele) throw new RegleMetierException("Un modèle est toujours une MEP complète : son type ne change pas.");
        if (mep.Phase != Phase.Preparation)
            throw new RegleMetierException("Le type de MEP ne change que pendant la préparation.");
    }

    /// <summary>
    /// Passe une MEP simplifiée en complète, en préparation : tout est conservé ; les suppléants implicites deviennent des
    /// suppléants choisis (le pilote peut ensuite en retirer). Les étapes n'ont pas encore de criticité : « Vérifier le plan »
    /// la réclamera. Renvoie le nombre de suppléants conservés.
    /// </summary>
    public static int PasserEnComplete(Mep mep)
    {
        ExigerChangementDeType(mep);
        if (!EstSimplifiee(mep)) throw new RegleMetierException("La MEP est déjà complète.");
        var suppleants = Suppleants(mep);
        mep.SuppleantIds = suppleants;
        mep.Type = TypeMep.Complete;
        return suppleants.Count;
    }

    /// <summary>
    /// Passe une MEP complète en simplifiée, en préparation. Destructif : durées, criticités et résolutions liées des étapes,
    /// versions actuelle et cible sont effacées ; les suppléants choisis disparaissent au profit des suppléants implicites
    /// (toutes les personnes sauf le pilote). Renvoie ce qui a été effacé.
    /// </summary>
    public static EffacementsSimplification PasserEnSimplifiee(Mep mep)
    {
        ExigerChangementDeType(mep);
        if (EstSimplifiee(mep)) throw new RegleMetierException("La MEP est déjà simplifiée.");
        var effacements = EffacementsPourSimplifier(mep);
        mep.Type = TypeMep.Simplifiee;
        mep.VersionActuelle = "";
        mep.VersionCible = "";
        mep.SuppleantIds = [];
        foreach (var e in mep.Etapes) AppliquerTypeAEtape(mep, e);
        return effacements;
    }

    /// <summary>Ce que <see cref="PasserEnSimplifiee"/> effacerait (pour prévenir avant de le faire).</summary>
    public static EffacementsSimplification EffacementsPourSimplifier(Mep mep) => new(
        mep.Etapes.Count(e => e.DureeEstimeeMinutes is not null),
        mep.Etapes.Count(e => e.Criticite is not null),
        mep.Etapes.Sum(e => e.ResolutionIds.Count),
        !string.IsNullOrWhiteSpace(mep.VersionActuelle) || !string.IsNullOrWhiteSpace(mep.VersionCible),
        mep.SuppleantIds.Count);

    /// <summary>
    /// Résolutions types proposées pour une étape en échec. MEP complète : celles liées à l'étape. MEP simplifiée : toutes les
    /// résolutions actives des systèmes de l'étape (systèmes du catalogue), plus celles qui ne visent aucun système.
    /// </summary>
    public static bool EstResolutionProposee(Mep mep, Etape etape, ResolutionType resolution)
    {
        if (!EstSimplifiee(mep)) return etape.ResolutionIds.Contains(resolution.Id);
        if (!resolution.Actif) return false;
        if (resolution.SystemeCatalogueIds.Count == 0) return true;
        var catalogue = mep.Systemes.Where(s => etape.SystemeIds.Contains(s.Id) && s.CatalogueId is not null).Select(s => s.CatalogueId!.Value);
        return resolution.SystemeCatalogueIds.Intersect(catalogue).Any();
    }
}
