namespace GestionMep.Domain;

// Règles métier : structure du plan (graphe par lignes).
public static partial class Regles
{
    /// <summary>Lignes d'un plan, de haut en bas : étapes regroupées par <see cref="Etape.Ligne"/>, chacune triée par <see cref="Etape.Ordre"/>.</summary>
    public static List<List<Etape>> Lignes(Mep mep, TypePlan plan) =>
        mep.EtapesDu(plan).GroupBy(e => e.Ligne).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

    /// <summary>Recalcule Ligne (0..n-1, lignes vides supprimées) et Ordre (0..k-1) depuis l'ordre des listes.</summary>
    private static void Renumeroter(IEnumerable<List<Etape>> lignes)
    {
        var n = 0;
        foreach (var ligne in lignes.Where(l => l.Count > 0))
        {
            for (var i = 0; i < ligne.Count; i++)
            {
                ligne[i].Ligne = n;
                ligne[i].Ordre = i;
            }
            n++;
        }
    }

    /// <summary>Change les préalables d'une étape (nouvelle liste : la colonne est réécrite).</summary>
    private static void PoserDependances(Etape etape, List<Guid> dependances)
    {
        if (!etape.Dependances.SequenceEqual(dependances)) etape.Dependances = dependances;
    }

    /// <summary>Préalables de chaque étape d'un plan avant une opération sur le graphe.</summary>
    private static Dictionary<Etape, List<Guid>> Instantane(Mep mep, TypePlan plan) =>
        mep.EtapesDu(plan).ToDictionary(e => e, e => e.Dependances.ToList());

    /// <summary>
    /// Une seule fois par opération, la révision des étapes dont les préalables ont changé augmente
    /// (un client qui édite l'une d'elles devra la relire).
    /// </summary>
    private static void IncrementerRevisions(Dictionary<Etape, List<Guid>> avant, Etape? sauf = null)
    {
        foreach (var (e, prealables) in avant)
            if (e != sauf && !e.Dependances.SequenceEqual(prealables)) e.Revision++;
    }

    /// <summary>
    /// Liens par défaut : l'étape attend toutes les étapes de la ligne au-dessus et est attendue par toutes celles de la ligne
    /// en dessous ; ses autres liens sont retirés. Les étapes voisines modifiées voient leur révision augmenter.
    /// </summary>
    public static void RelierParDefaut(Mep mep, Etape etape)
    {
        var avant = Instantane(mep, etape.Plan);
        Relier(mep, etape);
        IncrementerRevisions(avant, sauf: etape);
    }

    private static void Relier(Mep mep, Etape etape)
    {
        var plan = mep.EtapesDu(etape.Plan).Where(e => e != etape).ToList();
        foreach (var e in plan.Where(e => e.Dependances.Contains(etape.Id)))
            PoserDependances(e, e.Dependances.Where(d => d != etape.Id).ToList());
        etape.Dependances = plan.Where(e => e.Ligne == etape.Ligne - 1).Select(e => e.Id).ToList();
        foreach (var e in plan.Where(e => e.Ligne == etape.Ligne + 1))
            PoserDependances(e, [.. e.Dependances, etape.Id]);
    }

    /// <summary>Les étapes données qui n'ont plus aucun préalable (hors première ligne) attendent toute la ligne au-dessus d'elles.</summary>
    private static void RattacherOrphelins(Mep mep, IEnumerable<Etape> etapes)
    {
        foreach (var e in etapes.Where(e => e.Ligne > 0 && e.Dependances.Count == 0 && mep.Etapes.Contains(e)))
            PoserDependances(e, mep.EtapesDu(e.Plan).Where(x => x.Ligne == e.Ligne - 1).Select(x => x.Id).ToList());
    }

    /// <summary>Retire les liens devenus invalides (préalable disparu ou qui ne serait plus sur une ligne au-dessus).</summary>
    private static void NettoyerLiens(Mep mep, TypePlan plan)
    {
        var etapes = mep.EtapesDu(plan).ToList();
        var parId = etapes.ToDictionary(e => e.Id);
        foreach (var e in etapes)
            PoserDependances(e, e.Dependances.Where(d => parId.TryGetValue(d, out var p) && p.Ligne < e.Ligne).ToList());
    }

    /// <summary>
    /// Place une nouvelle étape (pas encore dans <see cref="Mep.Etapes"/> ou ignorée si elle y est) : <paramref name="ligne"/> absente
    /// ou égale au nombre de lignes = nouvelle ligne en bas ; <paramref name="position"/> absente = en fin de ligne (bornée sinon).
    /// </summary>
    public static void PlacerNouvelle(Mep mep, Etape etape, int? ligne, int? position)
    {
        ExigerPreparation(mep);
        var lignes = Lignes(mep, etape.Plan).Select(l => l.Where(e => e != etape).ToList()).Where(l => l.Count > 0).ToList();
        var l = ligne ?? lignes.Count;
        if (l < 0 || l > lignes.Count) throw new RegleMetierException($"Ligne inexistante : choisir une ligne entre 0 et {lignes.Count}.");
        if (l == lignes.Count) lignes.Add([]);
        lignes[l].Insert(Math.Clamp(position ?? lignes[l].Count, 0, lignes[l].Count), etape);
        Renumeroter(lignes);
    }

    /// <summary>
    /// Déplace une étape (préparation). Même ligne : simple réordonnancement, liens inchangés. Autre ligne (<paramref name="ligne"/> égale
    /// au nombre de lignes = nouvelle ligne en bas) : liens par défaut si demandé, et ses anciens suivants restés sans préalable attendent
    /// la ligne au-dessus d'eux ; sinon seuls les liens devenus invalides sont retirés. Les lignes vides disparaissent.
    /// </summary>
    public static void Placer(Mep mep, Etape etape, int ligne, int position, bool liensParDefaut)
    {
        ExigerPreparation(mep);
        var lignes = Lignes(mep, etape.Plan);
        if (ligne < 0 || ligne > lignes.Count) throw new RegleMetierException($"Ligne inexistante : choisir une ligne entre 0 et {lignes.Count}.");
        var depart = lignes.FindIndex(l => l.Contains(etape));
        var memeLigne = ligne == depart;
        var anciensSuivants = mep.EtapesDu(etape.Plan).Where(e => e.Dependances.Contains(etape.Id)).ToList();
        var avant = Instantane(mep, etape.Plan);

        lignes[depart].Remove(etape);
        if (ligne == lignes.Count) lignes.Add([]);
        lignes[ligne].Insert(Math.Clamp(position, 0, lignes[ligne].Count), etape);
        Renumeroter(lignes);
        if (memeLigne) return;

        if (liensParDefaut)
        {
            Relier(mep, etape);
            RattacherOrphelins(mep, anciensSuivants);
        }
        NettoyerLiens(mep, etape.Plan);
        IncrementerRevisions(avant);
    }

    /// <summary>
    /// Supprime une étape (préparation) : elle est retirée des préalables des autres, ses anciens suivants restés sans préalable
    /// attendent la ligne au-dessus d'eux, et une ligne devenue vide disparaît.
    /// </summary>
    public static void RetirerEtape(Mep mep, Etape etape)
    {
        ExigerPreparation(mep);
        var avant = Instantane(mep, etape.Plan);
        var suivants = mep.Etapes.Where(e => e.Dependances.Contains(etape.Id)).ToList();
        foreach (var s in suivants) PoserDependances(s, s.Dependances.Where(d => d != etape.Id).ToList());
        mep.Etapes.Remove(etape);
        Renumeroter(Lignes(mep, etape.Plan));
        RattacherOrphelins(mep, suivants);
        IncrementerRevisions(avant, sauf: etape);
    }

    /// <summary>
    /// Lignes des étapes d'un plan créées avant le jalon 4 : profondeur dans le graphe des dépendances (0 sans préalable,
    /// sinon 1 + la plus grande ligne des préalables) ; dans une ligne, l'ancien ordre (puis le code) donne la position.
    /// </summary>
    public static void CalculerLignes(IReadOnlyList<Etape> etapesDuPlan)
    {
        var parId = etapesDuPlan.ToDictionary(e => e.Id);
        var profondeur = new Dictionary<Guid, int>();
        var enCours = new HashSet<Guid>();
        int Profondeur(Etape e)
        {
            if (profondeur.TryGetValue(e.Id, out var p)) return p;
            if (!enCours.Add(e.Id)) return 0; // cycle (données invalides) : on coupe
            var prealables = e.Dependances.Where(d => d != e.Id && parId.ContainsKey(d)).Select(d => Profondeur(parId[d])).ToList();
            enCours.Remove(e.Id);
            return profondeur[e.Id] = prealables.Count == 0 ? 0 : prealables.Max() + 1;
        }
        foreach (var e in etapesDuPlan) Profondeur(e);
        Renumeroter(etapesDuPlan.GroupBy(e => profondeur[e.Id]).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(e => e.Ordre).ThenBy(e => e.Code, StringComparer.Ordinal).ToList()));
    }

    /// <summary>Équivalent de InitialiserAction : prochain code E0001… (ou R0001… pour le retour arrière) sans jamais réutiliser un numéro.</summary>
    public static string ProchainCode(IEnumerable<Etape> etapes, TypePlan plan = TypePlan.Deploiement)
    {
        var prefixe = plan == TypePlan.Deploiement ? 'E' : 'R';
        var max = 0;
        foreach (var e in etapes)
            if (e.Code.Length > 1 && e.Code[0] == prefixe && int.TryParse(e.Code[1..], out var n) && n > max) max = n;
        return $"{prefixe}{max + 1:0000}";
    }

    /// <summary>
    /// Applique de nouvelles dépendances après avoir vérifié qu'elles gardent un graphe valide : même plan,
    /// et chaque préalable sur une ligne strictement au-dessus (un lien descend toujours).
    /// </summary>
    public static void DefinirDependances(Mep mep, Etape etape, IEnumerable<Guid> dependances)
    {
        ExigerPreparation(mep);
        var nouvelles = dependances.Distinct().ToList();
        var autrePlan = mep.Etapes.FirstOrDefault(e => nouvelles.Contains(e.Id) && e.Plan != etape.Plan);
        if (autrePlan is not null) throw new RegleMetierException($"{autrePlan.Code} appartient à un autre plan.");
        var montant = mep.Etapes.FirstOrDefault(e => e != etape && nouvelles.Contains(e.Id) && e.Ligne >= etape.Ligne);
        if (montant is not null)
            throw new RegleMetierException($"Un lien descend toujours : {montant.Code} doit être sur une ligne au-dessus de {etape.Code}.");
        var anciennes = etape.Dependances;
        etape.Dependances = nouvelles;
        var erreur = ErreurGraphe(mep, etape.Plan);
        if (erreur is not null)
        {
            etape.Dependances = anciennes;
            throw new RegleMetierException(erreur);
        }
    }
}
