using System.Globalization;
using System.Text;

namespace GestionMep.Domain;

/// <summary>Statut d'un événement iCalendar (propriété STATUS d'un VEVENT).</summary>
public enum StatutIcs { Provisoire, Confirme, Annule }

/// <summary>
/// Événement d'un fichier iCalendar. <paramref name="Uid"/> : identifiant stable (le même à chaque export, pour que l'agenda
/// mette l'événement à jour au lieu de le dupliquer). <paramref name="JourneeEntiere"/> : <paramref name="JourDebut"/> et
/// <paramref name="JourFin"/> (exclu) en dates locales ; sinon <paramref name="Debut"/> et <paramref name="Fin"/> en UTC.
/// <paramref name="Sequence"/> : augmente à chaque modification de l'objet.
/// </summary>
public sealed record EvenementIcs(string Uid, string Resume, DateTimeOffset Debut, DateTimeOffset? Fin, bool JourneeEntiere = false,
    DateOnly? JourDebut = null, DateOnly? JourFin = null, string Description = "", string? Url = null, StatutIcs Statut = StatutIcs.Confirme,
    IReadOnlyList<string>? Categories = null, int Sequence = 0, bool Transparent = false);

/// <summary>
/// Écriture d'un calendrier iCalendar (RFC 5545) : lignes terminées par CRLF, repliées à 75 octets (UTF-8, sans couper un
/// caractère), textes échappés (\\, \;, \, et \n), heures en UTC (suffixe Z, aucun VTIMEZONE nécessaire), journées entières en
/// VALUE=DATE, DTSTAMP sur chaque événement.
/// </summary>
public static class ICalendar
{
    public const string TypeContenu = "text/calendar; charset=utf-8";
    public const string Produit = "-//SolaDevSolide//gestion-mep//FR";

    /// <summary>Texte d'une propriété TEXT échappé (RFC 5545, 3.3.11).</summary>
    public static string Echapper(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte.Replace("\r\n", "\n").Replace('\r', '\n'))
            sb.Append(c switch
            {
                '\\' => @"\\",
                ';' => @"\;",
                ',' => @"\,",
                '\n' => @"\n",
                _ => char.IsControl(c) && c != '\t' ? "" : c.ToString(),
            });
        return sb.ToString();
    }

    /// <summary>« 20261128T230000Z ».</summary>
    public static string DateUtc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static string Date(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ligne de contenu repliée (RFC 5545, 3.1) : au plus 75 octets par ligne, chaque suite commençant par une espace ; un
    /// caractère (y compris une paire de substitution) n'est jamais coupé.
    /// </summary>
    public static void Ligne(StringBuilder sortie, string ligne)
    {
        const int limite = 75;
        var octets = 0;
        var e = StringInfo.GetTextElementEnumerator(ligne);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var taille = Encoding.UTF8.GetByteCount(element);
            if (octets + taille > limite)
            {
                sortie.Append("\r\n ");
                octets = 1;
            }
            sortie.Append(element);
            octets += taille;
        }
        sortie.Append("\r\n");
    }

    /// <summary>
    /// Calendrier complet. <paramref name="nom"/> : nom affiché par l'agenda (X-WR-CALNAME) ; <paramref name="fuseau"/> : fuseau
    /// d'affichage suggéré (X-WR-TIMEZONE, celui de l'instance), sans effet sur les heures, toutes en UTC ;
    /// <paramref name="horodatage"/> : DTSTAMP de chaque événement (moment de la génération).
    /// </summary>
    public static string Calendrier(string nom, string fuseau, IEnumerable<EvenementIcs> evenements, DateTimeOffset horodatage)
    {
        var sb = new StringBuilder();
        void L(string ligne) => Ligne(sb, ligne);
        L("BEGIN:VCALENDAR");
        L("VERSION:2.0");
        L("PRODID:" + Produit);
        L("CALSCALE:GREGORIAN");
        L("METHOD:PUBLISH");
        L("X-WR-CALNAME:" + Echapper(nom));
        L("NAME:" + Echapper(nom));
        L("X-WR-TIMEZONE:" + fuseau);
        // Abonnement : relu toutes les heures par les agendas qui suivent ces indications (RFC 7986 et usage courant).
        L("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        L("X-PUBLISHED-TTL:PT1H");
        var stamp = DateUtc(horodatage);
        foreach (var ev in evenements)
        {
            L("BEGIN:VEVENT");
            L("UID:" + ev.Uid);
            L("DTSTAMP:" + stamp);
            if (ev.JourneeEntiere && ev.JourDebut is { } jd)
            {
                L("DTSTART;VALUE=DATE:" + Date(jd));
                L("DTEND;VALUE=DATE:" + Date(ev.JourFin is { } jf && jf > jd ? jf : jd.AddDays(1)));
            }
            else
            {
                L("DTSTART:" + DateUtc(ev.Debut));
                if (ev.Fin is { } f && f > ev.Debut) L("DTEND:" + DateUtc(f));
            }
            L("SUMMARY:" + Echapper(ev.Resume));
            if (ev.Description.Length > 0) L("DESCRIPTION:" + Echapper(ev.Description));
            if (ev.Url is { Length: > 0 } url) L("URL:" + url);
            if (ev.Categories is { Count: > 0 } cats) L("CATEGORIES:" + string.Join(",", cats.Select(Echapper)));
            L("STATUS:" + ev.Statut switch { StatutIcs.Provisoire => "TENTATIVE", StatutIcs.Annule => "CANCELLED", _ => "CONFIRMED" });
            L("SEQUENCE:" + ev.Sequence.ToString(CultureInfo.InvariantCulture));
            L("TRANSP:" + (ev.Transparent ? "TRANSPARENT" : "OPAQUE"));
            L("END:VEVENT");
        }
        L("END:VCALENDAR");
        return sb.ToString();
    }
}
