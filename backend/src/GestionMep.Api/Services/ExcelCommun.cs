using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Ce que partagent tous les classeurs de l'application : création (titre, version de l'application, date de génération
/// dans le fuseau de la MEP, palette de marque), noms de fichier, heures locales et tonalités des états.
/// </summary>
public static class ExcelCommun
{
    public const string TypeContenu = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Nouveau classeur ; <paramref name="palette"/> null : couleurs de l'application.</summary>
    public static ClasseurExcel Nouveau(string titre, DateTimeOffset maintenant, string? fuseau, PaletteExcel? palette, string? sujet = null) =>
        new(new InfosClasseur(titre, Fuseaux.Local(maintenant, fuseau), VersionApplication.Courante) { Palette = palette, Sujet = sujet });

    /// <summary>Instant facultatif dans le fuseau donné (<see cref="Fuseaux.ParDefaut"/> si null).</summary>
    public static DateTime? Quand(DateTimeOffset? instant, string? fuseau) => instant is { } i ? Fuseaux.Local(i, fuseau) : null;

    /// <summary>Nom de la MEP dans les fichiers : outil et version cible (« MEP » si les deux sont vides).</summary>
    public static string NomMep(string outil, string versionCible)
    {
        var nom = $"{outil} {versionCible}".Trim();
        return nom.Length == 0 ? "MEP" : nom;
    }

    /// <summary>« prefixe-Outil-1.2.xlsx » : lettres, chiffres, points et tirets seulement.</summary>
    public static string NomFichier(string prefixe, string nomMep)
    {
        var s = new string(nomMep.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray()).Trim('-');
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-", StringComparison.Ordinal);
        return $"{prefixe}-{(s.Length == 0 ? "mep" : s)}.xlsx";
    }

    // ---------- Tonalités des états (mêmes couleurs que les pastilles de l'interface) ----------

    public static readonly IReadOnlyDictionary<string, Ton> TonsEtatEtape = new Dictionary<string, Ton>
    {
        ["À faire"] = Ton.Attente, ["En cours"] = Ton.EnCours, ["Réussie"] = Ton.Succes, ["Échec"] = Ton.Echec,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsPhase = new Dictionary<string, Ton>
    {
        ["Préparation"] = Ton.Attente, ["Exécution"] = Ton.EnCours, ["Recette"] = Ton.EnCours, ["Retour arrière"] = Ton.Echec,
        ["Clôturée : réussie"] = Ton.Succes, ["Clôturée : réussie avec incidents"] = Ton.Attention, ["Clôturée après retour arrière"] = Ton.Attention,
        ["Clôturée : échec"] = Ton.Echec, ["Annulée"] = Ton.Neutre, ["Reportée"] = Ton.Attente, ["Modèle"] = Ton.Neutre,
    };

    /// <summary>États de la recherche (#59) : phase en cours, puis issue (« Réussie avec incidents » en attention).</summary>
    public static readonly IReadOnlyDictionary<string, Ton> TonsEtatMep = new Dictionary<string, Ton>
    {
        ["Préparation"] = Ton.Attente, ["Exécution"] = Ton.EnCours, ["Recette"] = Ton.EnCours, ["Retour arrière"] = Ton.Echec,
        ["Réussie"] = Ton.Succes, ["Réussie avec incidents"] = Ton.Attention, ["Après retour arrière"] = Ton.Attention,
        ["Reportée"] = Ton.Attente, ["Échec (service dégradé)"] = Ton.Echec, ["Annulée"] = Ton.Neutre,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsResolution = new Dictionary<string, Ton>
    {
        ["En cours"] = Ton.EnCours, ["Réussie"] = Ton.Succes, ["Échouée"] = Ton.Echec,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsResultat = new Dictionary<string, Ton>
    {
        ["Conforme"] = Ton.Succes, ["Non conforme"] = Ton.Echec, ["Non fait"] = Ton.Attente,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsValidation = new Dictionary<string, Ton>
    {
        [Regles.LibelleConforme] = Ton.Succes, [Regles.LibelleNonConforme] = Ton.Echec,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsFait = new Dictionary<string, Ton> { ["Oui"] = Ton.Succes, ["Non"] = Ton.Attention };

    public static readonly IReadOnlyDictionary<string, Ton> TonsAlerte = new Dictionary<string, Ton> { ["Oui"] = Ton.Attention };

    public static readonly IReadOnlyDictionary<string, Ton> TonsCriticite = new Dictionary<string, Ton>
    {
        ["Élevée"] = Ton.Attention, ["Bloquante"] = Ton.Echec,
    };

    public static readonly IReadOnlyDictionary<string, Ton> TonsSauvegarde = new Dictionary<string, Ton>
    {
        ["À renseigner"] = Ton.Attention, ["Requise"] = Ton.EnCours,
    };
}
