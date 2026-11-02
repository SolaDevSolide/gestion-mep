using System.Globalization;
using System.Text;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Fichiers iCalendar (RFC 5545) : relus par un analyseur strict écrit d'après la norme (CRLF, lignes de 75 octets au plus,
/// dépliage, paramètres, textes échappés), indépendant du code qui les écrit.
/// </summary>
public class ICalendarTests
{
    /// <summary>Propriété lue : nom, paramètres et valeur brute (encore échappée).</summary>
    public sealed record Propriete(string Nom, Dictionary<string, string> Parametres, string Valeur);

    /// <summary>Composant lu (VCALENDAR, VEVENT…) : ses propriétés et ses sous-composants.</summary>
    public sealed class Composant(string nom)
    {
        public string Nom { get; } = nom;
        public List<Propriete> Proprietes { get; } = [];
        public List<Composant> Enfants { get; } = [];
        public Propriete? P(string nom) => Proprietes.SingleOrDefault(p => p.Nom == nom);
        public string V(string nom) => P(nom)?.Valeur ?? throw new InvalidOperationException($"{Nom} sans {nom}.");
    }

    /// <summary>
    /// Analyse stricte : toute ligne finit par CRLF (aucun LF seul), fait 75 octets au plus, une suite commence par une espace ou
    /// une tabulation ; chaque BEGIN a son END ; un seul VCALENDAR.
    /// </summary>
    public static Composant Analyser(string texte)
    {
        Assert.EndsWith("\r\n", texte);
        Assert.DoesNotContain("\n", texte.Replace("\r\n", ""));
        Assert.DoesNotContain("\r", texte.Replace("\r\n", ""));
        var physiques = texte[..^2].Split("\r\n");
        foreach (var l in physiques) Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, $"Ligne de plus de 75 octets : {l}");
        var logiques = new List<string>();
        foreach (var l in physiques)
        {
            if (l.Length > 0 && (l[0] == ' ' || l[0] == '\t')) logiques[^1] += l[1..];
            else logiques.Add(l);
        }
        var pile = new Stack<Composant>();
        Composant? racine = null;
        foreach (var ligne in logiques)
        {
            var p = Ligne(ligne);
            if (p.Nom == "BEGIN")
            {
                var c = new Composant(p.Valeur);
                if (pile.Count > 0) pile.Peek().Enfants.Add(c);
                else
                {
                    Assert.Null(racine);
                    racine = c;
                }
                pile.Push(c);
            }
            else if (p.Nom == "END") Assert.Equal(p.Valeur, pile.Pop().Nom);
            else pile.Peek().Proprietes.Add(p);
        }
        Assert.Empty(pile);
        Assert.NotNull(racine);
        Assert.Equal("VCALENDAR", racine.Nom);
        return racine;
    }

    /// <summary>« NOM;PARAM=valeur:valeur » (deux-points dans un paramètre entre guillemets admis).</summary>
    private static Propriete Ligne(string ligne)
    {
        var guillemets = false;
        var fin = -1;
        for (var i = 0; i < ligne.Length && fin < 0; i++)
        {
            if (ligne[i] == '"') guillemets = !guillemets;
            else if (ligne[i] == ':' && !guillemets) fin = i;
        }
        Assert.True(fin > 0, $"Ligne sans valeur : {ligne}");
        var tete = ligne[..fin].Split(';');
        Assert.Matches("^[A-Z][A-Z0-9-]*$", tete[0]);
        var parametres = tete[1..].Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => x[1]);
        return new Propriete(tete[0], parametres, ligne[(fin + 1)..]);
    }

    /// <summary>Valeur TEXT déséchappée ; un antislash seul devant un autre caractère est une erreur.</summary>
    public static string Texte(string valeur)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < valeur.Length; i++)
        {
            var c = valeur[i];
            if (c is ';' or ',') Assert.Fail($"« {c} » non échappé dans : {valeur}");
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            Assert.True(i + 1 < valeur.Length, "Antislash final.");
            sb.Append(valeur[++i] switch
            {
                '\\' => '\\', ';' => ';', ',' => ',', 'n' or 'N' => '\n',
                var x => throw new InvalidOperationException($"Échappement inconnu \\{x}."),
            });
        }
        return sb.ToString();
    }

    public static DateTimeOffset DateUtc(string valeur) =>
        DateTimeOffset.ParseExact(valeur, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Contrôles communs à tout calendrier produit par l'application.</summary>
    public static List<Composant> Verifier(string ics)
    {
        var cal = Analyser(ics);
        Assert.Equal("2.0", cal.V("VERSION"));
        Assert.Equal(ICalendar.Produit, cal.V("PRODID"));
        var evenements = cal.Enfants.Where(c => c.Nom == "VEVENT").ToList();
        Assert.Equal(evenements.Count, cal.Enfants.Count);
        foreach (var e in evenements)
        {
            Assert.NotEmpty(e.V("UID"));
            DateUtc(e.V("DTSTAMP"));
            var debut = e.P("DTSTART")!;
            if (debut.Parametres.GetValueOrDefault("VALUE") == "DATE")
            {
                Assert.Matches("^[0-9]{8}$", debut.Valeur);
                Assert.True(string.CompareOrdinal(e.V("DTEND"), debut.Valeur) > 0);
            }
            else if (e.P("DTEND") is { } dtend) Assert.True(DateUtc(dtend.Valeur) > DateUtc(debut.Valeur));
            Texte(e.V("SUMMARY"));
            if (e.P("DESCRIPTION") is { } d) Texte(d.Valeur);
            Assert.Contains(e.V("STATUS"), new[] { "TENTATIVE", "CONFIRMED", "CANCELLED" });
        }
        Assert.Equal(evenements.Count, evenements.Select(e => e.V("UID")).Distinct().Count());
        return evenements;
    }

    private static readonly DateTimeOffset Horodatage = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Le_calendrier_se_relit_avec_crlf_lignes_repliees_et_textes_echappes()
    {
        var longue = string.Concat(Enumerable.Repeat("Étape « déploiement » ; vérifier, puis noter. ", 6)) + "😀 fin";
        var ics = ICalendar.Calendrier("MEP, équipe ; web", "Europe/Paris",
        [
            new EvenementIcs("mep-1@gestion-mep", "MEP Portail 2.0 ; lot 3, correctif", new DateTimeOffset(2026, 11, 20, 19, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 20, 22, 0, 0, TimeSpan.Zero), Description: longue + "\nDeuxième ligne\\fin", Statut: StatutIcs.Provisoire,
                Categories: ["Préparation", "Production"], Sequence: 7, Url: "https://mep.exemple.invalid/meps/1"),
            new EvenementIcs("mep-2@gestion-mep", "Journée entière", new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero), null, JourneeEntiere: true,
                JourDebut: new DateOnly(2026, 11, 21), JourFin: new DateOnly(2026, 11, 22)),
            new EvenementIcs("mep-3@gestion-mep", "Sans fin connue", new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero), null, Statut: StatutIcs.Annule),
        ], Horodatage);

        var evenements = Verifier(ics);
        Assert.Equal(3, evenements.Count);
        var e1 = evenements[0];
        Assert.Equal("MEP Portail 2.0 ; lot 3, correctif", Texte(e1.V("SUMMARY")));
        Assert.Equal(longue + "\nDeuxième ligne\\fin", Texte(e1.V("DESCRIPTION")));
        Assert.Equal("20261120T190000Z", e1.V("DTSTART"));
        Assert.Equal("20261120T220000Z", e1.V("DTEND"));
        Assert.Equal("20261102T093000Z", e1.V("DTSTAMP"));
        Assert.Equal("7", e1.V("SEQUENCE"));
        Assert.Equal("TENTATIVE", e1.V("STATUS"));
        Assert.Equal("Préparation,Production", e1.V("CATEGORIES"));
        Assert.Equal("20261121", evenements[1].V("DTSTART"));
        Assert.Equal("20261122", evenements[1].V("DTEND"));
        Assert.Null(evenements[2].P("DTEND"));
        Assert.Equal("CANCELLED", evenements[2].V("STATUS"));
        // Une ligne repliée ne coupe jamais un caractère : l'émoji (4 octets) reste entier.
        Assert.Contains("😀", ics.Replace("\r\n ", ""));
        Assert.Contains("\r\n ", ics);
    }

    [Theory]
    [InlineData("a;b,c\\d", @"a\;b\,c\\d")]
    [InlineData("ligne 1\r\nligne 2\rligne 3\nligne 4", @"ligne 1\nligne 2\nligne 3\nligne 4")]
    public void Les_textes_sont_echappes_selon_la_norme(string texte, string attendu) => Assert.Equal(attendu, ICalendar.Echapper(texte));

    [Fact]
    public void Une_ligne_de_caracteres_accentues_se_replie_a_75_octets_et_se_deplie_a_l_identique()
    {
        var sb = new StringBuilder();
        var valeur = "SUMMARY:" + new string('é', 120);
        ICalendar.Ligne(sb, valeur);
        var lignes = sb.ToString()[..^2].Split("\r\n");
        Assert.True(lignes.Length >= 3);
        Assert.All(lignes, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75));
        Assert.Equal(valeur, string.Concat(lignes.Select((l, i) => i == 0 ? l : l[1..])));
    }
}
