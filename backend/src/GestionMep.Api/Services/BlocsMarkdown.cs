using System.Text.RegularExpressions;

namespace GestionMep.Api.Services;

/// <summary>
/// Instructions en blocs Texte / Code, comme dans l'éditeur du front (<c>frontend/src/blocs.ts</c>) : un bloc Code est un
/// bloc markdown délimité (``` … ```), le texte entre deux blocs forme un bloc Texte. Sert aux exportations (Excel…).
/// </summary>
public static partial class BlocsMarkdown
{
    public record Bloc(bool EstCode, string Contenu);

    /// <summary>Découpe une chaîne markdown en blocs Texte et Code ; les blocs vides sont ignorés.</summary>
    public static List<Bloc> Decouper(string? source)
    {
        var lignes = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocs = new List<Bloc>();
        var texte = new List<string>();
        void PousserTexte()
        {
            var t = string.Join("\n", texte).Trim('\n').TrimEnd();
            if (t.Trim().Length > 0) blocs.Add(new Bloc(false, t));
            texte.Clear();
        }
        for (var i = 0; i < lignes.Length; i++)
        {
            var m = Ouverture().Match(lignes[i]);
            if (!m.Success)
            {
                texte.Add(lignes[i]);
                continue;
            }
            var cloture = m.Groups[1].Value;
            var fin = new Regex($"^ {{0,3}}{(cloture[0] == '`' ? "`" : "~")}{{{cloture.Length},}}[ \\t]*$");
            var code = new List<string>();
            var j = i + 1;
            while (j < lignes.Length && !fin.IsMatch(lignes[j])) code.Add(lignes[j++]);
            PousserTexte();
            var contenu = string.Join("\n", code).TrimEnd('\n');
            if (contenu.Trim().Length > 0) blocs.Add(new Bloc(true, contenu));
            // Bloc non fermé : il court jusqu'à la fin (comme en CommonMark).
            i = j;
        }
        PousserTexte();
        return blocs;
    }

    /// <summary>
    /// Texte lisible hors de l'application : gras, italique et code en ligne sans leurs marques, liens « texte (adresse) »,
    /// cases à cocher « ☐ ». Les listes et les retours à la ligne sont gardés.
    /// </summary>
    public static string TexteSimple(string markdown)
    {
        var s = Lien().Replace(markdown, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[1].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        s = Case().Replace(s, m => m.Groups[1].Value + (m.Groups[2].Value == " " ? "☐ " : "☑ "));
        s = Gras().Replace(s, "$2");
        s = CodeEnLigne().Replace(s, "$1");
        s = Titre().Replace(s, "");
        return s;
    }

    /// <summary>Tout le contenu en texte simple, blocs de code compris (sans leurs délimiteurs).</summary>
    public static string ToutEnTexte(string? markdown) =>
        string.Join("\n", Decouper(markdown).Select(b => b.EstCode ? b.Contenu : TexteSimple(b.Contenu)));

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})[ \t]*([^`\s]*)[^`]*$")]
    private static partial Regex Ouverture();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Lien();

    [GeneratedRegex(@"^(\s*)[-*] \[([ xX])\] ", RegexOptions.Multiline)]
    private static partial Regex Case();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex Gras();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeEnLigne();

    [GeneratedRegex(@"^#{1,6} ", RegexOptions.Multiline)]
    private static partial Regex Titre();
}
