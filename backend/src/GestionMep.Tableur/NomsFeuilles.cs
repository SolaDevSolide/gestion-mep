namespace GestionMep.Tableur;

/// <summary>Noms d'onglets valides pour Excel : 31 caractères au plus, sans <c>[ ] : * ? / \</c>, uniques sans tenir compte de la casse.</summary>
public static class NomsFeuilles
{
    public const int LongueurMax = 31;
    private static readonly char[] Interdits = ['[', ']', ':', '*', '?', '/', '\\'];

    /// <summary>Remplace les caractères interdits, retire les apostrophes en bord et tronque ; « Feuille » si rien ne reste.</summary>
    public static string Assainir(string nom)
    {
        var s = new string((nom ?? "").Select(c => Interdits.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim().Trim('\'').Trim();
        if (s.Length > LongueurMax) s = s[..LongueurMax].TrimEnd();
        return s.Length == 0 ? "Feuille" : s;
    }

    /// <summary>Nom non encore pris parmi <paramref name="pris"/> : « WEB », puis « WEB (2) », « WEB (3) »…</summary>
    public static string Unique(string nom, IReadOnlySet<string> pris)
    {
        if (!pris.Contains(nom)) return nom;
        for (var i = 2; ; i++)
        {
            var suffixe = $" ({i})";
            var candidat = (nom.Length + suffixe.Length > LongueurMax ? nom[..(LongueurMax - suffixe.Length)] : nom) + suffixe;
            if (!pris.Contains(candidat)) return candidat;
        }
    }
}
