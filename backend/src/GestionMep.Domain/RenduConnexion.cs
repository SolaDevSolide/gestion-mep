using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GestionMep.Domain;

/// <summary>Erreur dans un gabarit (message, adresse, en-tête), à la ligne et à la colonne du gabarit (à partir de 1).</summary>
public record ErreurGabarit(int Ligne, int Colonne, string Message);

/// <summary>
/// Gabarit rempli : le texte qui part, les variables utilisées sans valeur (<see cref="Manquantes"/>, envoyées vides), celles
/// qui n'existent pas (<see cref="Inconnues"/>) et les erreurs qui empêchent l'envoi (JSON invalide, variable hors guillemets).
/// </summary>
public record Rendu(string Texte, IReadOnlyList<string> Manquantes, IReadOnlyList<string> Inconnues, IReadOnlyList<ErreurGabarit> Erreurs)
{
    public bool Valide => Erreurs.Count == 0 && Inconnues.Count == 0;
}

/// <summary>
/// Remplissage des messages des connexions, avec la syntaxe des modèles de texte (<c>{{variable}}</c>). Le même code sert à
/// l'aperçu, à l'essai et à l'envoi : ce qu'on voit est ce qui part. Le front applique les mêmes règles
/// (frontend/src/textes/gabarits.ts), vérifiées des deux côtés par les mêmes cas (frontend/src/vecteurs-gabarits.json).
/// <list type="bullet">
/// <item>JSON : dans une chaîne, la valeur est échappée (guillemets, retours à la ligne…), une liste y est jointe par des
/// virgules ; hors chaîne, seules une variable de liste (tableau JSON) et <c>{{donnees_json}}</c> (objet) sont permises. Une
/// valeur absente donne <c>""</c> (ou <c>[]</c>). Le résultat est vérifié : erreur avec ligne et colonne du gabarit.</item>
/// <item>Texte et formulaire : la valeur telle quelle (liste jointe par des virgules), sans mise en forme.</item>
/// <item>Adresse : chaque valeur est encodée (<c>Uri.EscapeDataString</c>) ; aucune variable dans le domaine.</item>
/// </list>
/// </summary>
public static partial class RenduConnexion
{
    [GeneratedRegex(@"\{\{\s*([a-z0-9_]+)\s*\}\}")]
    private static partial Regex Jeton();

    private static readonly JsonSerializerOptions OptionsJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Clés des variables utilisées par un gabarit, dans l'ordre d'apparition, sans doublon.</summary>
    public static List<string> Variables(string gabarit) =>
        [.. Jeton().Matches(gabarit ?? "").Select(m => m.Groups[1].Value).Distinct()];

    private static string Joindre(ValeurVariable v) => v.Texte ?? string.Join(", ", v.Liste ?? []);

    /// <summary>Ligne et colonne (à partir de 1) d'une position du texte.</summary>
    public static (int Ligne, int Colonne) Position(string texte, int index)
    {
        var ligne = 1;
        var debut = 0;
        for (var i = 0; i < index && i < texte.Length; i++)
            if (texte[i] == '\n')
            {
                ligne++;
                debut = i + 1;
            }
        return (ligne, index - debut + 1);
    }

    /// <summary>Texte ou formulaire : chaque variable remplacée par sa valeur (une liste jointe par des virgules).</summary>
    public static Rendu Texte(string gabarit, IReadOnlyDictionary<string, ValeurVariable> valeurs, Func<string, string>? encoder = null)
    {
        var manquantes = new List<string>();
        var inconnues = new List<string>();
        var texte = Jeton().Replace(gabarit ?? "", m =>
        {
            var cle = m.Groups[1].Value;
            if (VariablesEvenement.Trouver(cle) is null)
            {
                Ajouter(inconnues, cle);
                return "";
            }
            var v = valeurs.GetValueOrDefault(cle) ?? ValeurVariable.Manquante;
            if (v.EstManquante)
            {
                Ajouter(manquantes, cle);
                return "";
            }
            var t = Joindre(v);
            return encoder is null ? t : encoder(t);
        });
        return new Rendu(texte, manquantes, inconnues, []);
    }

    private static void Ajouter(List<string> liste, string cle)
    {
        if (!liste.Contains(cle)) liste.Add(cle);
    }

    /// <summary>
    /// Adresse : valeurs encodées. Erreur si une variable est placée avant le chemin (schéma, domaine, port), ou si l'adresse
    /// rendue n'est pas une adresse http(s) complète.
    /// </summary>
    public static Rendu Url(string gabarit, IReadOnlyDictionary<string, ValeurVariable> valeurs)
    {
        gabarit ??= "";
        var erreurs = new List<ErreurGabarit>();
        var finOrigine = FinOrigine(gabarit);
        if (Jeton().Match(gabarit) is { Success: true } m && m.Index < finOrigine)
        {
            var (l, c) = Position(gabarit, m.Index);
            erreurs.Add(new(l, c, "Les variables sont permises dans le chemin et les paramètres de l'adresse, pas dans le domaine."));
        }
        var rendu = Texte(gabarit, valeurs, Uri.EscapeDataString);
        if (erreurs.Count == 0 && !Uri.TryCreate(rendu.Texte, UriKind.Absolute, out _))
            erreurs.Add(new(1, 1, "Adresse complète attendue, commençant par https://."));
        return rendu with { Erreurs = erreurs };
    }

    /// <summary>Position de la fin de l'origine (schéma, domaine, port) : le premier « / », « ? » ou « # » après « // ».</summary>
    public static int FinOrigine(string url)
    {
        var debut = url.IndexOf("//", StringComparison.Ordinal);
        debut = debut < 0 ? 0 : debut + 2;
        var fin = url.IndexOfAny(['/', '?', '#'], debut);
        return fin < 0 ? url.Length : fin;
    }

    /// <summary>Segment du rendu JSON, pour retrouver dans le gabarit la position d'une erreur du rendu.</summary>
    private readonly record struct Segment(int Rendu, int Gabarit, bool Jeton);

    /// <summary>
    /// Message JSON : rempli selon les règles d'échappement, puis vérifié. <paramref name="valeurs"/> : valeurs réelles (aperçu,
    /// envoi) ou d'exemple (<see cref="VariablesEvenement.ValeursExemple"/>, vérification avant l'enregistrement).
    /// </summary>
    public static Rendu Json(string gabarit, IReadOnlyDictionary<string, ValeurVariable> valeurs)
    {
        gabarit ??= "";
        var sb = new StringBuilder(gabarit.Length + 64);
        var segments = new List<Segment>();
        var manquantes = new List<string>();
        var inconnues = new List<string>();
        var erreurs = new List<ErreurGabarit>();
        var dansChaine = false;
        var echappe = false;
        var litteral = -1;
        for (var i = 0; i < gabarit.Length;)
        {
            if (gabarit[i] == '{' && i + 1 < gabarit.Length && gabarit[i + 1] == '{' && Jeton().Match(gabarit, i) is { Success: true } m && m.Index == i)
            {
                litteral = -1;
                segments.Add(new Segment(sb.Length, i, true));
                var cle = m.Groups[1].Value;
                var variable = VariablesEvenement.Trouver(cle);
                var v = valeurs.GetValueOrDefault(cle) ?? ValeurVariable.Manquante;
                if (variable is null) Ajouter(inconnues, cle);
                else if (v.EstManquante) Ajouter(manquantes, cle);
                if (dansChaine)
                {
                    if (variable is not null && !v.EstManquante) sb.Append(JsonEncodedText.Encode(Joindre(v), JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value);
                }
                else if (cle == VariablesEvenement.DonneesJson)
                    sb.Append(v.EstManquante ? "{}" : v.Texte);
                else if (variable is { Liste: true })
                    sb.Append(JsonSerializer.Serialize(v.Liste ?? (v.Texte is { } t ? [t] : []), OptionsJson));
                else
                {
                    if (variable is not null)
                    {
                        var (l, c) = Position(gabarit, i);
                        erreurs.Add(new(l, c, $"Mettez {{{{{cle}}}}} entre guillemets : seules les listes et {{{{donnees_json}}}} s'écrivent hors guillemets."));
                    }
                    sb.Append("null");
                }
                i += m.Length;
                continue;
            }
            var ch = gabarit[i];
            if (dansChaine)
            {
                if (echappe) echappe = false;
                else if (ch == '\\') echappe = true;
                else if (ch == '"') dansChaine = false;
            }
            else if (ch == '"') dansChaine = true;
            if (litteral < 0)
            {
                litteral = i;
                segments.Add(new Segment(sb.Length, i, false));
            }
            sb.Append(ch);
            i++;
        }
        var texte = sb.ToString();
        if (erreurs.Count == 0 && ValidateurJson.Valider(texte) is { } e)
        {
            var (l, c) = Position(gabarit, VersGabarit(segments, e.Index));
            erreurs.Add(new(l, c, e.Message));
        }
        return new Rendu(texte, manquantes, inconnues, erreurs);
    }

    /// <summary>Position dans le gabarit d'une position du rendu (le début de la variable si elle tombe dans une valeur).</summary>
    private static int VersGabarit(List<Segment> segments, int index)
    {
        Segment? trouve = null;
        foreach (var s in segments)
        {
            if (s.Rendu > index) break;
            trouve = s;
        }
        if (trouve is not { } seg) return 0;
        return seg.Jeton ? seg.Gabarit : seg.Gabarit + (index - seg.Rendu);
    }

    /// <summary>Formulaire : champs encodés (<c>application/x-www-form-urlencoded</c>).</summary>
    public static Rendu Formulaire(IEnumerable<ChampConnexion> champs, IReadOnlyDictionary<string, ValeurVariable> valeurs)
    {
        var manquantes = new List<string>();
        var inconnues = new List<string>();
        var parties = new List<string>();
        foreach (var c in champs)
        {
            var r = Texte(c.Valeur, valeurs);
            foreach (var x in r.Manquantes) Ajouter(manquantes, x);
            foreach (var x in r.Inconnues) Ajouter(inconnues, x);
            parties.Add($"{Uri.EscapeDataString(c.Nom)}={Uri.EscapeDataString(r.Texte)}");
        }
        return new Rendu(string.Join("&", parties), manquantes, inconnues, []);
    }
}

/// <summary>
/// Vérification d'un texte JSON avec un message en français et la position de l'erreur (où il manque quelque chose plutôt que
/// le caractère suivant). Mêmes messages que le front (frontend/src/textes/gabarits.ts).
/// </summary>
public static class ValidateurJson
{
    public record Erreur(int Index, string Message);

    private sealed class Echec(int index, string message) : Exception(message)
    {
        public int Index { get; } = index;
    }

    public static Erreur? Valider(string texte)
    {
        var p = new Analyse(texte);
        try
        {
            p.Blancs();
            if (p.Fin) return new Erreur(0, "Le message est vide : un JSON est attendu.");
            p.Valeur("du message");
            var fin = p.I;
            p.Blancs();
            if (!p.Fin) return new Erreur(fin, "Contenu en trop après la fin du JSON : un seul objet ou une seule liste est attendu.");
            return null;
        }
        catch (Echec e)
        {
            return new Erreur(e.Index, e.Message);
        }
    }

    private sealed class Analyse(string t)
    {
        public int I;
        public bool Fin => I >= t.Length;
        private char C => t[I];

        public void Blancs()
        {
            while (!Fin && C is ' ' or '\t' or '\r' or '\n') I++;
        }

        private static string NomListe(string nom) => nom == "du message" ? "du message" : $"de la liste « {nom} »";
        private static string NomObjet(string nom) => nom == "du message" ? "du message" : $"de l'objet « {nom} »";

        /// <summary>Valeur ; <paramref name="nom"/> : clé qui la porte, pour nommer la liste ou l'objet dans les messages.</summary>
        public void Valeur(string nom)
        {
            Blancs();
            if (Fin) throw new Echec(I, "Le JSON s'arrête trop tôt : une valeur est attendue.");
            switch (C)
            {
                case '{': Objet(nom); break;
                case '[': Liste(nom); break;
                case '"': Chaine(); break;
                case 't': Mot("true"); break;
                case 'f': Mot("false"); break;
                case 'n': Mot("null"); break;
                default:
                    if (C == '-' || char.IsAsciiDigit(C)) Nombre();
                    else throw new Echec(I, "Une valeur est attendue : texte entre guillemets, nombre, objet, liste, true, false ou null.");
                    break;
            }
        }

        private void Mot(string mot)
        {
            if (string.CompareOrdinal(t, I, mot, 0, mot.Length) != 0)
                throw new Echec(I, "Valeur inconnue : un texte s'écrit entre guillemets (true, false et null s'écrivent sans).");
            I += mot.Length;
        }

        private void Nombre()
        {
            var debut = I;
            if (C == '-') I++;
            if (Fin || !char.IsAsciiDigit(C)) throw new Echec(debut, "Nombre invalide.");
            if (C == '0') I++;
            else while (!Fin && char.IsAsciiDigit(C)) I++;
            if (!Fin && C == '.')
            {
                I++;
                if (Fin || !char.IsAsciiDigit(C)) throw new Echec(debut, "Nombre invalide.");
                while (!Fin && char.IsAsciiDigit(C)) I++;
            }
            if (!Fin && C is 'e' or 'E')
            {
                I++;
                if (!Fin && C is '+' or '-') I++;
                if (Fin || !char.IsAsciiDigit(C)) throw new Echec(debut, "Nombre invalide.");
                while (!Fin && char.IsAsciiDigit(C)) I++;
            }
        }

        public string Chaine()
        {
            var debut = I;
            I++;
            var sb = new StringBuilder();
            while (true)
            {
                if (Fin) throw new Echec(debut, "Texte non fermé : un guillemet manque.");
                var c = C;
                if (c == '"')
                {
                    I++;
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    I++;
                    if (Fin) throw new Echec(debut, "Texte non fermé : un guillemet manque.");
                    switch (C)
                    {
                        case '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't': I++; break;
                        case 'u':
                            if (I + 4 >= t.Length || !t.Substring(I + 1, 4).All(char.IsAsciiHexDigit))
                                throw new Echec(I - 1, "Échappement invalide dans un texte : \\u doit être suivi de 4 chiffres hexadécimaux.");
                            I += 5;
                            break;
                        default: throw new Echec(I - 1, "Échappement invalide dans un texte : écrivez \\\\ pour une barre oblique inverse.");
                    }
                    continue;
                }
                if (c < ' ')
                    throw new Echec(I, c == '\n' ? "Retour à la ligne dans un texte : fermez le guillemet, ou écrivez \\n." : "Caractère de contrôle interdit dans un texte.");
                sb.Append(c);
                I++;
            }
        }

        private void Objet(string nom)
        {
            I++;
            Blancs();
            if (!Fin && C == '}')
            {
                I++;
                return;
            }
            while (true)
            {
                Blancs();
                if (Fin) throw new Echec(I, $"L'objet {NomObjet(nom)} n'est pas fermé : « }} » manque.");
                if (C == '}') throw new Echec(I, $"Virgule en trop avant « }} » dans l'objet {NomObjet(nom)}.");
                if (C != '"') throw new Echec(I, "Un nom de propriété entre guillemets est attendu.");
                var cle = Chaine();
                Blancs();
                if (Fin || C != ':') throw new Echec(I, $"« : » est attendu après le nom « {cle} ».");
                I++;
                Valeur(cle);
                var finValeur = I;
                Blancs();
                if (Fin) throw new Echec(finValeur, $"L'objet {NomObjet(nom)} n'est pas fermé : « }} » manque.");
                if (C == ',')
                {
                    I++;
                    continue;
                }
                if (C == '}')
                {
                    I++;
                    return;
                }
                throw new Echec(finValeur, $"Une virgule est attendue avant la propriété suivante {NomObjet(nom)}.");
            }
        }

        private void Liste(string nom)
        {
            I++;
            Blancs();
            if (!Fin && C == ']')
            {
                I++;
                return;
            }
            while (true)
            {
                Blancs();
                if (Fin) throw new Echec(I, $"La liste {NomListe(nom)} n'est pas fermée : « ] » manque.");
                if (C == ']') throw new Echec(I, $"Virgule en trop avant « ] » dans la liste {NomListe(nom)}.");
                Valeur(nom);
                var finValeur = I;
                Blancs();
                if (Fin) throw new Echec(finValeur, $"La liste {NomListe(nom)} n'est pas fermée : « ] » manque.");
                if (C == ',')
                {
                    I++;
                    continue;
                }
                if (C == ']')
                {
                    I++;
                    return;
                }
                throw new Echec(finValeur, $"Une virgule est attendue avant l'élément suivant {NomListe(nom)}.");
            }
        }
    }
}
