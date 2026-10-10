namespace GestionMep.Domain;

// Règles métier : graphe de dépendances.
public static partial class Regles
{
    /// <summary>Équivalent de ErreurGraphe, pour les étapes d'un même plan.</summary>
    public static string? ErreurGraphe(IReadOnlyList<Etape> etapes)
    {
        var parId = etapes.ToDictionary(e => e.Id);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in etapes)
        {
            if (string.IsNullOrWhiteSpace(e.Code)) return "ID manquant";
            if (!codes.Add(e.Code.Trim())) return $"ID en double : {e.Code}";
        }
        foreach (var e in etapes)
        {
            foreach (var dep in e.Dependances)
            {
                if (dep == e.Id) return $"Auto-dépendance : {e.Code}";
                if (!parId.ContainsKey(dep)) return $"Dépendance introuvable pour {e.Code}";
            }
        }

        // Parcours en profondeur avec trois couleurs.
        var couleur = new Dictionary<Guid, int>();
        bool Visiter(Etape e)
        {
            var c = couleur.GetValueOrDefault(e.Id);
            if (c == 1) return true;
            if (c == 2) return false;
            couleur[e.Id] = 1;
            foreach (var dep in e.Dependances)
                if (Visiter(parId[dep])) return true;
            couleur[e.Id] = 2;
            return false;
        }
        foreach (var e in etapes)
            if (Visiter(e)) return "Cycle de dépendances";
        return null;
    }

    public static string? ErreurGraphe(Mep mep, TypePlan plan) => ErreurGraphe(mep.EtapesDu(plan).ToList());
}
