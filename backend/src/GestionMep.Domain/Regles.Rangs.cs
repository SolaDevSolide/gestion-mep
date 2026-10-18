namespace GestionMep.Domain;

// Règles métier : ordre gauche-droite du graphe libre. La hauteur d'une carte (son niveau) vient de ses liens ; l'ordre des
// éléments d'un même niveau (étapes et sections) est un choix des personnes, partagé par tous (Etape.Rang, Section.Rang).
public static partial class Regles
{
    /// <summary>Le plan a-t-il déjà été réordonné à la main (au moins un rang) ? Sinon l'ordre suit Ligne, Ordre, Code.</summary>
    public static bool PlanOrdonne(Mep mep, TypePlan plan, Etape? sauf = null) =>
        mep.EtapesDu(plan).Any(e => e != sauf && e.Rang > 0) || mep.Sections.Any(s => s.Plan == plan && s.Rang > 0);

    private static int RangMax(Mep mep, TypePlan plan, Etape? sauf = null) =>
        Math.Max(mep.EtapesDu(plan).Where(e => e != sauf).Select(e => e.Rang).DefaultIfEmpty(0).Max(),
            mep.Sections.Where(s => s.Plan == plan).Select(s => s.Rang).DefaultIfEmpty(0).Max());

    /// <summary>Rang d'un nouvel élément sans voisin : en bout (plan déjà ordonné), sinon 0.</summary>
    internal static int RangEnFin(Mep mep, TypePlan plan) => PlanOrdonne(mep, plan) ? RangMax(mep, plan) + 1 : 0;

    /// <summary>
    /// Rang d'une étape qu'on pose ou qu'on déplace : juste après son voisin (la carte de référence de sa place), en décalant ce
    /// qui suit ; en bout sans voisin. Rien à faire tant que le plan n'a jamais été réordonné.
    /// </summary>
    internal static void RangApres(Mep mep, Etape etape, Etape? voisin)
    {
        if (!PlanOrdonne(mep, etape.Plan, etape))
        {
            etape.Rang = 0;
            return;
        }
        if (voisin is null || voisin == etape || voisin.Rang == 0)
        {
            etape.Rang = RangMax(mep, etape.Plan, etape) + 1;
            return;
        }
        foreach (var e in mep.EtapesDu(etape.Plan).Where(e => e != etape && e.Rang > voisin.Rang)) e.Rang++;
        foreach (var s in mep.Sections.Where(s => s.Plan == etape.Plan && s.Rang > voisin.Rang)) s.Rang++;
        etape.Rang = voisin.Rang + 1;
    }

    /// <summary>
    /// Nouvel ordre gauche-droite (préparation) : <paramref name="ids"/> (étapes et sections du plan) prennent les rangs 1, 2, 3…
    /// dans cet ordre ; les autres éléments suivent, dans leur ordre actuel. Ne change aucun lien : seulement la disposition.
    /// </summary>
    public static void Ordonner(Mep mep, TypePlan plan, IReadOnlyList<Guid> ids)
    {
        ExigerPreparation(mep);
        if (ids.Count == 0) throw new RegleMetierException("Indiquer l'ordre des éléments.");
        if (ids.Distinct().Count() != ids.Count) throw new RegleMetierException("Un élément apparaît deux fois dans l'ordre.");
        var etapes = mep.EtapesDu(plan).ToDictionary(e => e.Id);
        var sections = mep.Sections.Where(s => s.Plan == plan).ToDictionary(s => s.Id);
        foreach (var id in ids)
            if (!etapes.ContainsKey(id) && !sections.ContainsKey(id))
                throw new RegleMetierException("Élément introuvable dans ce plan : seules ses étapes et ses sections s'ordonnent.");
        var rang = 0;
        foreach (var id in ids)
        {
            rang++;
            if (etapes.TryGetValue(id, out var e)) e.Rang = rang;
            else sections[id].Rang = rang;
        }
        var cites = ids.ToHashSet();
        var reste = etapes.Values.Where(e => !cites.Contains(e.Id))
            .Select(e => (Rang: e.Rang == 0 ? int.MaxValue : e.Rang, e.Ligne, e.Ordre, Cle: e.Code, Affecter: (Action<int>)(r => e.Rang = r)))
            .Concat(sections.Values.Where(s => !cites.Contains(s.Id))
                .Select(s => (Rang: s.Rang == 0 ? int.MaxValue : s.Rang, Ligne: int.MaxValue, Ordre: int.MaxValue, Cle: s.CreeLe.ToString("O"),
                    Affecter: (Action<int>)(r => s.Rang = r))))
            .OrderBy(x => x.Rang).ThenBy(x => x.Ligne).ThenBy(x => x.Ordre).ThenBy(x => x.Cle, StringComparer.Ordinal)
            .ToList();
        foreach (var x in reste) x.Affecter(++rang);
    }
}
