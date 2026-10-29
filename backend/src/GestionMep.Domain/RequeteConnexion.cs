using System.Text.RegularExpressions;

namespace GestionMep.Domain;

/// <summary>Erreur d'un gabarit de la connexion : <paramref name="Champ"/> vaut « adresse », « corps » ou « entete:Nom ».</summary>
public record ErreurChamp(string Champ, int Ligne, int Colonne, string Message);

/// <summary>
/// Requête d'une connexion remplie pour un événement, sans aucun secret : adresse (réduite au domaine si elle est secrète),
/// type de contenu, corps et en-têtes ordinaires. Les secrets (adresse complète, authentification, en-têtes secrets,
/// signature) sont ajoutés au moment de l'envoi seulement.
/// </summary>
public record RequeteRendue(MethodeHttp Methode, string Url, bool UrlSecrete, string TypeContenu, string Corps, List<EnteteAppel> Entetes,
    List<string> Manquantes, List<string> Inconnues, List<ErreurChamp> Erreurs)
{
    public bool Valide => Erreurs.Count == 0 && Inconnues.Count == 0;
}

/// <summary>Construction de la requête d'une connexion (aperçu, essai, envoi : le même code) et règles des en-têtes.</summary>
public static partial class RequeteConnexion
{
    public const string EnteteAppel = "X-Gestion-Mep-Appel";
    public const string EnteteEvenement = "X-Gestion-Mep-Evenement";
    public const string EnteteSignature = "X-Gestion-Mep-Signature";
    /// <summary>Ce qui remplace un secret partout où la requête s'affiche.</summary>
    public const string Masque = "••••";

    /// <summary>En-têtes réservés : posés par le serveur HTTP ou par l'application (contenu, cookies, connexion).</summary>
    public static readonly IReadOnlyList<string> EntetesReserves = ["Host", "Content-Length", "Transfer-Encoding", "Connection", "Cookie", "Content-Type", "User-Agent"];

    [GeneratedRegex("^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,100}$")]
    public static partial Regex NomEntete();

    /// <summary>Raison du refus d'un nom d'en-tête saisi, ou null s'il est accepté.</summary>
    public static string? RefusNomEntete(string nom, AuthentificationApi authentification)
    {
        if (!NomEntete().IsMatch(nom)) return $"« {nom} » n'est pas un nom d'en-tête valide (lettres, chiffres et - seulement).";
        if (EntetesReserves.Any(r => r.Equals(nom, StringComparison.OrdinalIgnoreCase)) || nom.StartsWith("X-Gestion-Mep-", StringComparison.OrdinalIgnoreCase))
            return $"L'en-tête « {nom} » est posé par l'application : il ne se règle pas.";
        if (authentification != AuthentificationApi.Aucune && nom.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            return "L'en-tête « Authorization » est posé par l'authentification choisie : choisissez « Aucune » pour le régler vous-même.";
        return null;
    }

    public static string TypeContenu(FormatCorps format) => format switch
    {
        FormatCorps.Json => "application/json; charset=utf-8",
        FormatCorps.Formulaire => "application/x-www-form-urlencoded",
        FormatCorps.Texte => "text/plain; charset=utf-8",
        _ => "",
    };

    /// <summary>« https://hote:port » d'une adresse (minuscules, port par défaut omis) ; vide si elle n'est pas lisible.</summary>
    public static string Origine(string url)
    {
        var fin = RenduConnexion.FinOrigine(url ?? "");
        return Uri.TryCreate((url ?? "")[..fin], UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : "";
    }

    /// <summary>
    /// Origine à laquelle les secrets d'une connexion sont liés : celle de l'adresse, et celle du point de jeton en OAuth2 (le
    /// secret client y part). <paramref name="adresse"/> : adresse complète (secrète) ou gabarit.
    /// </summary>
    public static string OrigineSecrets(string adresse, AuthentificationApi authentification, string tokenUrl) =>
        authentification == AuthentificationApi.OAuth2 ? $"{Origine(adresse)} {Origine(tokenUrl)}" : Origine(adresse);

    /// <summary>Adresse telle qu'on peut la montrer : réduite au domaine suivi de « /••• » si elle est secrète.</summary>
    public static string UrlAffichee(string url, bool secrete) => secrete ? $"{Origine(url)}/•••" : url;

    /// <summary>Valeur d'en-tête sans retour à la ligne (une variable ne peut pas ajouter d'en-tête).</summary>
    private static string SansRetour(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Remplit l'adresse, le corps et les en-têtes ordinaires d'une connexion avec <paramref name="valeurs"/>.</summary>
    public static RequeteRendue Rendre(ConnexionApi c, IReadOnlyDictionary<string, ValeurVariable> valeurs)
    {
        var manquantes = new List<string>();
        var inconnues = new List<string>();
        var erreurs = new List<ErreurChamp>();
        void Prendre(Rendu r, string champ)
        {
            foreach (var x in r.Manquantes.Where(x => !manquantes.Contains(x))) manquantes.Add(x);
            foreach (var x in r.Inconnues.Where(x => !inconnues.Contains(x))) inconnues.Add(x);
            erreurs.AddRange(r.Erreurs.Select(e => new ErreurChamp(champ, e.Ligne, e.Colonne, e.Message)));
        }

        string url;
        var secrete = c.UrlSecrete is not null;
        if (secrete) url = c.Url;
        else
        {
            var r = RenduConnexion.Url(c.Url, valeurs);
            Prendre(r, "adresse");
            url = r.Texte;
        }

        var corps = "";
        if (c.Format == FormatCorps.Json)
        {
            var r = RenduConnexion.Json(c.Corps, valeurs);
            Prendre(r, "corps");
            corps = r.Texte;
        }
        else if (c.Format == FormatCorps.Texte)
        {
            var r = RenduConnexion.Texte(c.Corps, valeurs);
            Prendre(r, "corps");
            corps = r.Texte;
        }
        else if (c.Format == FormatCorps.Formulaire)
        {
            var r = RenduConnexion.Formulaire(c.Champs, valeurs);
            Prendre(r, "corps");
            corps = r.Texte;
        }

        var entetes = new List<EnteteAppel>();
        foreach (var e in c.Entetes.Where(e => !e.Secret))
        {
            var r = RenduConnexion.Texte(e.Valeur, valeurs, SansRetour);
            Prendre(r, $"entete:{e.Nom}");
            entetes.Add(new EnteteAppel { Nom = e.Nom, Valeur = SansRetour(r.Texte).Trim() });
        }
        return new RequeteRendue(c.Methode, url, secrete, TypeContenu(c.Format), corps, entetes, manquantes, inconnues, erreurs);
    }

    /// <summary>
    /// En-têtes tels qu'ils partent, secrets masqués : ceux de l'application, l'authentification, la signature, puis ceux de la
    /// connexion (<paramref name="entetes"/> : valeurs rendues des en-têtes ordinaires).
    /// </summary>
    public static List<EnteteAppel> EntetesAffiches(ConnexionApi c, string typeContenu, IEnumerable<EnteteAppel> entetes, Guid appelId,
        string typeEvenement, string version)
    {
        var liste = new List<EnteteAppel>();
        void Ajouter(string nom, string valeur) => liste.Add(new EnteteAppel { Nom = nom, Valeur = valeur });
        if (typeContenu.Length > 0) Ajouter("Content-Type", typeContenu);
        Ajouter("User-Agent", $"GestionMep/{version}");
        Ajouter(EnteteAppel, appelId.ToString());
        Ajouter(EnteteEvenement, typeEvenement);
        switch (c.Authentification)
        {
            case AuthentificationApi.Jeton or AuthentificationApi.OAuth2: Ajouter("Authorization", $"Bearer {Masque}"); break;
            case AuthentificationApi.Basic: Ajouter("Authorization", $"Basic {Masque}"); break;
            case AuthentificationApi.CleEntete when c.NomEntete.Length > 0: Ajouter(c.NomEntete, Masque); break;
        }
        if (c.SecretSignature is not null) Ajouter(EnteteSignature, $"sha256={Masque}");
        var rendus = entetes.ToDictionary(e => e.Nom, e => e.Valeur, StringComparer.OrdinalIgnoreCase);
        foreach (var e in c.Entetes)
            Ajouter(e.Nom, e.Secret ? Masque : rendus.GetValueOrDefault(e.Nom) ?? "");
        return liste;
    }

    /// <summary>
    /// Version « lisible » d'un corps JSON : chaque valeur texte sous sa clé, une par ligne (« title : Pilote »), pour relire le
    /// message comme dans l'aperçu des modèles de texte. Texte et formulaire : tels quels (formulaire décodé).
    /// </summary>
    public static string Lisible(FormatCorps format, string corps)
    {
        if (format == FormatCorps.Formulaire)
            return string.Join("\n", corps.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p =>
            {
                var i = p.IndexOf('=');
                return i < 0 ? Uri.UnescapeDataString(p) : $"{Uri.UnescapeDataString(p[..i])} : {Uri.UnescapeDataString(p[(i + 1)..])}";
            }));
        if (format != FormatCorps.Json) return corps;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(corps);
            var lignes = new List<string>();
            Parcourir(doc.RootElement, null, lignes);
            return string.Join("\n", lignes);
        }
        catch (System.Text.Json.JsonException)
        {
            return corps;
        }
    }

    private static void Parcourir(System.Text.Json.JsonElement e, string? cle, List<string> lignes)
    {
        switch (e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                // Fait d'une carte (« title » et « value ») ou bouton (« title » et « url ») : sur une ligne.
                string? Texte(string nom) => e.TryGetProperty(nom, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                if (Texte("title") is { } titre && (Texte("value") ?? Texte("url")) is { } valeur)
                {
                    lignes.Add(Texte("value") is not null ? $"{titre} : {valeur}" : $"[{titre}] {valeur}");
                    break;
                }
                foreach (var p in e.EnumerateObject()) Parcourir(p.Value, p.Name, lignes);
                break;
            case System.Text.Json.JsonValueKind.Array:
                if (e.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.String))
                {
                    var valeurs = e.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (valeurs.Count > 0) lignes.Add(cle is null ? string.Join(", ", valeurs) : $"{cle} : {string.Join(", ", valeurs)}");
                }
                else foreach (var x in e.EnumerateArray()) Parcourir(x, cle, lignes);
                break;
            case System.Text.Json.JsonValueKind.String:
                var s = e.GetString();
                // Valeurs techniques d'un message (type de carte, schéma) : sans intérêt pour la relecture.
                if (string.IsNullOrWhiteSpace(s) || cle is "type" or "$schema" or "version" or "contentType" or "weight" or "size") break;
                lignes.Add(cle is null or "text" ? s : $"{cle} : {s}");
                break;
            case System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False:
                if (cle is not ("version" or "wrap" or "isSubtle" or "separator")) lignes.Add($"{cle} : {e.GetRawText()}");
                break;
        }
    }
}
