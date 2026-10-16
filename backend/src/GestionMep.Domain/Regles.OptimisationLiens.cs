namespace GestionMep.Domain;

// Règles métier : optimisation des liens d'un plan (réduction transitive du graphe, cercles des sections compris).
public static partial class Regles
{
    /// <summary>Lien du graphe d'un plan : <paramref name="Vers"/> attend <paramref name="De"/> (étapes ou cercles de section).</summary>
    public sealed record LienPlan(Guid De, Guid Vers);

    /// <summary>
    /// Tous les liens saisis d'un plan, cercles compris : préalables directs entre étapes, sources des cercles d'entrée, étapes
    /// qui attendent un cercle d'entrée, étapes attendues par un cercle de sortie, cercles de sortie attendus par une étape. Les
    /// préalables qui viennent des cercles (dérivés) ne sont pas des liens : ils passent par les cercles.
    /// </summary>
    public static List<LienPlan> LiensDuPlan(Mep mep, TypePlan plan)
    {
        var etapes = mep.EtapesDu(plan).ToList();
        var ids = etapes.Select(e => e.Id).ToHashSet();
        var liens = new List<LienPlan>();
        foreach (var b in etapes)
        {
            liens.AddRange(Directes(b).Where(ids.Contains).Select(a => new LienPlan(a, b.Id)));
            liens.AddRange(b.PortsAttendus.Select(q => new LienPlan(q, b.Id)));
        }
        foreach (var s in mep.SectionsDu(plan))
            foreach (var p in s.Ports.OrderBy(p => p.Ordre))
                if (p.Sens == SensPort.Entree)
                {
                    liens.AddRange(p.Sources.Select(src => new LienPlan(src, p.Id)));
                    liens.AddRange(p.Etapes.Select(e => new LienPlan(p.Id, e)));
                }
                else liens.AddRange(p.Etapes.Select(e => new LienPlan(e, p.Id)));
        return liens.Distinct().ToList();
    }

    /// <summary>
    /// Liens redondants d'un plan : « B attend A » est inutile quand B attend déjà A par un autre chemin (par d'autres étapes ou
    /// par des cercles de section). Les retirer tous ensemble ne change ni ce que chaque étape attend, directement ou non, ni donc
    /// l'ordre d'exécution (réduction transitive d'un graphe sans cycle).
    /// <paramref name="sectionId"/> : seulement les liens entre éléments de cette section (ses étapes et ses cercles), l'autre
    /// chemin passant lui aussi par la section ; le reste du plan n'est pas concerné. Un lien redondant dans la section l'est
    /// aussi dans le plan : les retirer garde l'ordre d'exécution.
    /// </summary>
    public static List<LienPlan> LiensRedondants(Mep mep, TypePlan plan, Guid? sectionId = null)
    {
        var liens = LiensDuPlan(mep, plan);
        if (sectionId is { } sid)
        {
            var section = ExigerSection(mep, plan, sid);
            var dedans = mep.EtapesDu(plan).Where(e => e.SectionId == sid).Select(e => e.Id)
                .Concat(section.Ports.Select(p => p.Id)).ToHashSet();
            liens = liens.Where(l => dedans.Contains(l.De) && dedans.Contains(l.Vers)).ToList();
        }
        var suivants = liens.GroupBy(l => l.De).ToDictionary(g => g.Key, g => g.Select(l => l.Vers).ToList());
        bool Atteint(Guid depart, Guid cible, Guid exclu)
        {
            // Chemin depart → … → cible qui ne commence pas par le lien direct depart → cible.
            var vus = new HashSet<Guid>();
            var pile = new Stack<Guid>((suivants.GetValueOrDefault(depart) ?? []).Where(x => x != exclu));
            while (pile.Count > 0)
            {
                var n = pile.Pop();
                if (n == cible) return true;
                if (!vus.Add(n)) continue;
                foreach (var s in suivants.GetValueOrDefault(n) ?? []) pile.Push(s);
            }
            return false;
        }
        return liens.Where(l => Atteint(l.De, l.Vers, l.Vers)).ToList();
    }

    /// <summary>Pour chaque étape du plan, toutes les étapes qu'elle attend, directement ou non (préalables effectifs).</summary>
    private static Dictionary<Guid, HashSet<Guid>> Fermeture(Mep mep, TypePlan plan)
    {
        var parId = mep.EtapesDu(plan).ToDictionary(e => e.Id);
        var res = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var e in parId.Values)
        {
            var vus = new HashSet<Guid>();
            var pile = new Stack<Guid>(e.Dependances);
            while (pile.Count > 0)
            {
                var id = pile.Pop();
                if (!parId.TryGetValue(id, out var x) || !vus.Add(id)) continue;
                foreach (var d in x.Dependances) pile.Push(d);
            }
            res[e.Id] = vus;
        }
        return res;
    }

    /// <summary>
    /// Retire en une fois les liens redondants du plan, ou d'une de ses sections (<see cref="LiensRedondants"/>). <paramref name="attendus"/> est la liste
    /// montrée à la personne : si le plan a changé depuis (autre liste), rien n'est fait et l'opération est refusée. Par sécurité,
    /// l'opération est annulée si ce qu'attend une étape, directement ou non, changeait.
    /// </summary>
    /// <param name="sectionId">Seulement dans cette section (voir <see cref="LiensRedondants"/>).</param>
    /// <returns>Liens retirés, avec leurs extrémités (pour le journal).</returns>
    public static List<(Noeud De, Noeud Vers)> OptimiserLiens(Mep mep, TypePlan plan, IReadOnlyCollection<LienPlan> attendus,
        Guid? sectionId = null)
    {
        var redondants = LiensRedondants(mep, plan, sectionId);
        if (!redondants.ToHashSet().SetEquals(attendus))
            throw new RegleMetierException("Le plan a changé depuis le calcul : relancez « Optimiser les liens ».");
        var retires = new List<(Noeud De, Noeud Vers)>();
        if (redondants.Count == 0) return retires;
        var avant = Fermeture(mep, plan);
        Operer(mep, plan, () =>
        {
            foreach (var l in redondants)
            {
                var de = ExigerNoeud(mep, plan, l.De);
                var vers = ExigerNoeud(mep, plan, l.Vers);
                Delier(de, vers);
                retires.Add((de, vers));
            }
            // Les cercles sont recompilés par Operer : on vérifie ensuite que l'ordre d'exécution n'a pas bougé.
            CompilerSections(mep, plan);
            var apres = Fermeture(mep, plan);
            if (avant.Any(x => !apres.TryGetValue(x.Key, out var a) || !a.SetEquals(x.Value)))
                throw new RegleMetierException("Optimisation refusée : elle changerait l'ordre d'exécution du plan.");
        });
        return retires;
    }
}
