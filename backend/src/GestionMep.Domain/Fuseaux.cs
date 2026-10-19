namespace GestionMep.Domain;

/// <summary>
/// Fuseaux horaires des MEP : identifiants IANA (« Europe/Paris »), contrôlés à la saisie. Toutes les heures locales de
/// l'application (jour de la MEP, filtres, suivi, textes de communication, exports Excel) passent par ces conversions.
/// Hôtes Linux et Windows : .NET reconnaît les identifiants IANA sur les deux (Windows Server 2019 ou plus récent).
/// </summary>
public static class Fuseaux
{
    /// <summary>Fuseau d'une nouvelle MEP, et celui des heures sans MEP (exports de listes, historique d'un système).</summary>
    public const string ParDefaut = "Europe/Paris";

    /// <summary>
    /// Identifiant IANA d'une saisie, ou null si le fuseau est inconnu. Un identifiant Windows (« Romance Standard Time »)
    /// est accepté et converti en son équivalent IANA ; la casse doit être celle de l'identifiant.
    /// </summary>
    public static string? Normaliser(string? saisie)
    {
        var id = (saisie ?? "").Trim();
        if (id.Length == 0) return null;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) id = iana;
        if (Chercher(id) is null) return null;
        return id;
    }

    /// <summary>Fuseau de la MEP ; un identifiant inconnu (donnée antérieure au contrôle) retombe sur <see cref="ParDefaut"/>.</summary>
    public static TimeZoneInfo Trouver(string? fuseau) => Chercher(fuseau) ?? Chercher(ParDefaut) ?? TimeZoneInfo.Utc;

    private static TimeZoneInfo? Chercher(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        // Hôte qui ne connaît que les identifiants Windows : on passe par l'identifiant Windows correspondant.
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && TimeZoneInfo.TryFindSystemTimeZoneById(windows, out tz) ? tz : null;
    }

    /// <summary>Heure locale d'un instant dans le fuseau (sans indication de fuseau, pour l'affichage et Excel).</summary>
    public static DateTime Local(DateTimeOffset instant, string? fuseau) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, Trouver(fuseau)).DateTime, DateTimeKind.Unspecified);

    /// <summary>Jour local d'un instant dans le fuseau.</summary>
    public static DateOnly Jour(DateTimeOffset instant, string? fuseau) => DateOnly.FromDateTime(Local(instant, fuseau));

    /// <summary>
    /// Instant d'une date et d'une heure locales (minuit sans heure) dans le fuseau. Changements d'heure : une heure qui
    /// n'existe pas (passage à l'heure d'été) est décalée de la durée du saut (02:30 → 03:30) ; une heure qui existe deux fois
    /// (retour à l'heure d'hiver) désigne la première.
    /// </summary>
    public static DateTimeOffset Instant(DateOnly date, TimeOnly? heure, string? fuseau)
    {
        var tz = Trouver(fuseau);
        var local = date.ToDateTime(heure ?? TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (tz.IsAmbiguousTime(local)) return new DateTimeOffset(local, tz.GetAmbiguousTimeOffsets(local).Max());
        // Heure sautée : le décalage d'avant le saut donne l'instant qui s'affiche après lui.
        var decalage = tz.IsInvalidTime(local) ? tz.GetUtcOffset(local.AddDays(-1)) : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, decalage);
    }
}
