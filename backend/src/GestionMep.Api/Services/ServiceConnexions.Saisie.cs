using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Usage d'une saisie de connexion : enregistrement (tout est vérifié), essai (la requête doit pouvoir partir), aperçu (rien
/// n'est refusé) ou importation (comme l'enregistrement, mais un secret absent est seulement relevé dans
/// <see cref="ServiceConnexions.SecretsManquants"/> : il se saisit ensuite par « connexions secret »).
/// </summary>
public enum UsageSaisie { Enregistrement, Essai, Apercu, Importation }

public sealed partial class ServiceConnexions
{
    public const int LongueurMaxNom = 200;
    public const int LongueurMaxDescription = 1000;
    public const int LongueurMaxAdresse = 2000;
    public const int LongueurMaxCorps = 100_000;
    public const int MaxEntetes = 30;
    public const int MaxChamps = 50;

    private static string T(string? s) => (s ?? "").Trim();

    /// <summary>Secrets absents relevés par la dernière <see cref="Appliquer"/> en importation (« adresse », « authentification », « hmac », « entete:Nom »).</summary>
    public List<string> SecretsManquants { get; } = [];

    /// <summary>
    /// Saisie équivalente à une connexion enregistrée, sans aucun secret (laissés vides, ils sont gardés) : base des
    /// modifications de la ligne de commande et de l'export.
    /// </summary>
    public static ConnexionApiSaisie Saisie(ConnexionApi c) => new(c.Version, c.Nom, c.Description, c.Modele, c.Methode, c.Url, c.UrlSecrete is not null,
        null, c.Authentification, c.Utilisateur, c.NomEntete, c.TokenUrl, c.ClientId, c.Scope, null,
        [.. c.Entetes.Select(e => new EnteteConnexionSaisie(e.Nom, e.Secret ? null : e.Valeur, e.Secret, null))], c.SecretSignature is not null, null,
        c.Format, c.Corps, [.. c.Champs.Select(x => new ChampConnexion { Nom = x.Nom, Valeur = x.Valeur })], c.DelaiSecondes, c.Tentatives,
        [.. c.Declencheurs], c.Filtres ?? new FiltresConnexion(), c.EnvoiManuel, c.LibelleEnvoiManuel, c.Active);

    /// <summary>Article et nom du secret de l'authentification : « le jeton », « le mot de passe »…</summary>
    public static (string Article, string Nom) NomSecret(AuthentificationApi a) => a switch
    {
        AuthentificationApi.Jeton => ("le ", "jeton"),
        AuthentificationApi.Basic => ("le ", "mot de passe"),
        AuthentificationApi.CleEntete => ("la ", "clé"),
        AuthentificationApi.OAuth2 => ("le ", "secret client"),
        _ => ("", ""),
    };

    private static bool AUnSecret(ConnexionApi c) =>
        c.UrlSecrete is not null || c.Secret is not null || c.SecretSignature is not null || c.Entetes.Any(e => e.ValeurSecrete is not null);

    /// <summary>Adresse sans variable (adresse secrète, point de jeton) : complète, en https (ou http), sans identifiant.</summary>
    private static string? RefusAdresseFixe(string adresse, string libelle)
    {
        if (adresse.Length > LongueurMaxAdresse) return $"{libelle} : {LongueurMaxAdresse} caractères au plus.";
        if (adresse.Contains("{{", StringComparison.Ordinal)) return $"{libelle} : pas de variable dans une adresse secrète ni dans le point de jeton.";
        if (!Uri.TryCreate(adresse, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp))
            return $"{libelle} : adresse complète attendue, commençant par https://.";
        if (u.UserInfo.Length > 0) return $"{libelle} : pas d'identifiant dans l'adresse ; choisissez « Identifiant et mot de passe ».";
        return null;
    }

    /// <summary>
    /// Applique une saisie à une connexion (nouvelle ou enregistrée) et renvoie ce qui a changé, pour le journal
    /// d'administration (« mot de passe remplacé », jamais une valeur). Secrets : une valeur saisie remplace l'ancienne,
    /// rien garde l'ancienne, sauf si l'adresse change de domaine (ou de port) : ils sont alors effacés et à ressaisir.
    /// </summary>
    public async Task<List<string>> Appliquer(ConnexionApi c, ConnexionApiSaisie s, string auteur, UsageSaisie usage = UsageSaisie.Enregistrement)
    {
        var enregistrement = usage is UsageSaisie.Enregistrement or UsageSaisie.Importation;
        var apercu = usage == UsageSaisie.Apercu;
        SecretsManquants.Clear();
        void Refuser(string message)
        {
            if (!apercu) throw ErreurHttp.Saisie(message);
        }
        void Manque(string champ, string message)
        {
            if (usage == UsageSaisie.Importation) SecretsManquants.Add(champ);
            else Refuser(message);
        }
        var changes = new List<string>();
        void Si<TV>(TV avant, TV apres, string libelle)
        {
            if (!EqualityComparer<TV>.Default.Equals(avant, apres)) changes.Add(libelle);
        }
        var maintenant = Maintenant;
        var exemple = VariablesEvenement.ValeursExemple();

        // 1. Connexion
        var nom = T(s.Nom);
        if (enregistrement)
        {
            if (nom.Length == 0) Refuser("Le nom de la connexion est obligatoire.");
            if (nom.Length > LongueurMaxNom) Refuser($"Nom : {LongueurMaxNom} caractères au plus.");
            if (await db.ConnexionsApi.AnyAsync(x => x.Nom == nom && x.Id != c.Id))
                throw ErreurHttp.Saisie($"Une connexion s'appelle déjà « {nom} » : choisissez un autre nom.");
        }
        var description = T(s.Description);
        if (enregistrement && description.Length > LongueurMaxDescription) Refuser($"Description : {LongueurMaxDescription} caractères au plus.");

        // 2. Requête
        var methode = s.Methode ?? MethodeHttp.Post;
        var format = s.Format ?? FormatCorps.Json;
        if (methode == MethodeHttp.Get && format != FormatCorps.Aucun)
            Refuser("Une requête GET n'envoie pas de message : choisissez le format « Aucun », ou une autre méthode.");
        var authentification = s.Authentification ?? AuthentificationApi.Aucune;
        var delai = s.DelaiSecondes ?? 10;
        if (delai is < 1 or > 30) Refuser("Délai d'attente : de 1 à 30 secondes.");
        var tentatives = s.Tentatives ?? 3;
        if (tentatives is < 1 or > 5) Refuser("Tentatives : de 1 à 5.");

        // Adresse : gabarit, ou adresse complète secrète (seule l'origine reste lisible).
        string url;
        string adresse;
        string? nouvelleAdresseSecrete = null;
        if (s.UrlSecrete)
        {
            var saisie = T(s.AdresseSecrete);
            if (saisie.Length > 0)
            {
                if (RefusAdresseFixe(saisie, "Adresse") is { } refus) Refuser(refus);
                nouvelleAdresseSecrete = saisie;
                adresse = saisie;
            }
            else if (c.UrlSecrete is not null && secrets.Dechiffrer(c.UrlSecrete) is { } ancienne) adresse = ancienne;
            else
            {
                Manque("adresse", c.UrlSecrete is not null
                    ? "L'adresse secrète enregistrée est illisible (clés de l'application changées, par exemple après une restauration) : saisissez-la de nouveau."
                    : "L'adresse est obligatoire.");
                // Importation : l'export ne donne que l'origine de l'adresse secrète, l'adresse complète se saisit ensuite.
                adresse = usage == UsageSaisie.Importation ? RequeteConnexion.Origine(T(s.Url)) : "";
            }
            url = RequeteConnexion.Origine(adresse);
        }
        else
        {
            url = T(s.Url);
            if (url.Length == 0) Refuser("L'adresse est obligatoire.");
            else if (url.Length > LongueurMaxAdresse) Refuser($"Adresse : {LongueurMaxAdresse} caractères au plus.");
            else
            {
                var r = RenduConnexion.Url(url, exemple);
                if (r.Erreurs is [var e, ..]) Refuser($"Adresse : {e.Message}");
                else if (r.Inconnues is [var x, ..]) Refuser($"Adresse : variable inconnue {{{{{x}}}}}.");
                else if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    Refuser("Adresse : adresse complète attendue, commençant par https://.");
            }
            adresse = url;
        }

        var utilisateur = authentification == AuthentificationApi.Basic ? T(s.Utilisateur) : "";
        if (authentification == AuthentificationApi.Basic && (utilisateur.Length == 0 || utilisateur.Contains(':')))
            Refuser("L'identifiant est obligatoire (sans « : ») avec « Identifiant et mot de passe ».");
        var nomEntete = authentification == AuthentificationApi.CleEntete ? T(s.NomEntete) : "";
        if (authentification == AuthentificationApi.CleEntete)
        {
            if (nomEntete.Length == 0) Refuser("Le nom de l'en-tête qui porte la clé est obligatoire (par exemple X-Api-Key).");
            else if (RequeteConnexion.RefusNomEntete(nomEntete, AuthentificationApi.Aucune) is { } refus) Refuser(refus);
        }
        var oauth = authentification == AuthentificationApi.OAuth2;
        var tokenUrl = oauth ? T(s.TokenUrl) : "";
        var clientId = oauth ? T(s.ClientId) : "";
        var scope = oauth ? T(s.Scope) : "";
        if (oauth)
        {
            if (tokenUrl.Length == 0) Refuser("Le point de jeton OAuth2 est obligatoire.");
            else if (RefusAdresseFixe(tokenUrl, "Point de jeton") is { } refus) Refuser(refus);
            if (clientId.Length == 0) Refuser("L'ID client OAuth2 est obligatoire.");
        }

        // Secrets liés à l'origine : un changement de domaine (ou de port) efface ceux qui sont enregistrés.
        var origine = RequeteConnexion.OrigineSecrets(adresse, authentification, tokenUrl);
        var ancienneOrigine = c.OrigineSecrets;
        var domaineChange = ancienneOrigine.Length > 0 && ancienneOrigine != origine && AUnSecret(c);
        var anciensEntetes = c.Entetes.ToDictionary(e => e.Nom, StringComparer.OrdinalIgnoreCase);
        if (domaineChange)
        {
            c.UrlSecrete = null;
            c.Secret = null;
            c.SecretSignature = null;
            foreach (var e in anciensEntetes.Values) e.ValeurSecrete = null;
            changes.Add("secrets effacés (nouvelle adresse)");
        }
        var ancienHote = Uri.TryCreate(ancienneOrigine.Split(' ')[0], UriKind.Absolute, out var ah) ? ah.Host : ancienneOrigine;
        var (article, nomSecret) = NomSecret(authentification);
        string Ressaisir(string quoi) => domaineChange
            ? $"L'adresse ne mène plus à {ancienHote} : saisissez {quoi} de la nouvelle adresse, l'ancien secret est effacé."
            : $"Saisissez {quoi}.";

        // Adresse secrète
        if (s.UrlSecrete)
        {
            if (nouvelleAdresseSecrete is not null)
            {
                c.UrlSecrete = secrets.Chiffrer(nouvelleAdresseSecrete, auteur, maintenant);
                changes.Add("adresse secrète remplacée");
            }
            else if (c.UrlSecrete is null && !apercu && !SecretsManquants.Contains("adresse")) Manque("adresse", Ressaisir("l'adresse"));
        }
        else if (c.UrlSecrete is not null)
        {
            c.UrlSecrete = null;
            changes.Add("adresse secrète retirée");
        }

        // Secret de l'authentification
        if (authentification == AuthentificationApi.Aucune) c.Secret = null;
        else
        {
            var saisi = s.Secret ?? "";
            if (saisi.Trim().Length > 0)
            {
                c.Secret = secrets.Chiffrer(saisi.Trim(), auteur, maintenant);
                changes.Add($"{nomSecret} remplacé{(authentification == AuthentificationApi.CleEntete ? "e" : "")}");
            }
            else if (c.Secret is not null && c.Authentification != authentification)
            {
                // Un mot de passe ne devient pas un jeton : changer de mode exige le nouveau secret.
                c.Secret = null;
                Manque("authentification", $"Saisissez {article}{nomSecret} : le mode d'identification a changé.");
            }
            else if (c.Secret is null) Manque("authentification", Ressaisir($"{article}{nomSecret}"));
            else if (secrets.Dechiffrer(c.Secret) is null)
                Refuser($"{char.ToUpperInvariant(article[0])}{article[1..]}{nomSecret} enregistré{(authentification == AuthentificationApi.CleEntete ? "e" : "")} est illisible (clés de l'application changées, par exemple après une restauration) : saisissez-le de nouveau.");
        }

        // Signature HMAC du corps
        if (!s.Signature)
        {
            if (c.SecretSignature is not null) changes.Add("signature retirée");
            c.SecretSignature = null;
        }
        else if (T(s.SecretSignature) is { Length: > 0 } secretSignature)
        {
            c.SecretSignature = secrets.Chiffrer(secretSignature, auteur, maintenant);
            changes.Add("secret de signature remplacé");
        }
        else if (c.SecretSignature is null) Manque("hmac", Ressaisir("le secret de la signature"));

        // En-têtes
        var entetes = new List<EnteteConnexion>();
        foreach (var e in s.Entetes ?? [])
        {
            var nomE = T(e.Nom);
            if (nomE.Length == 0 && T(e.Valeur).Length == 0 && T(e.ValeurSecrete).Length == 0) continue;
            if (RequeteConnexion.RefusNomEntete(nomE, authentification) is { } refus)
            {
                Refuser(nomE.Length == 0 ? "Chaque en-tête a un nom." : refus);
                continue;
            }
            if (entetes.Any(x => x.Nom.Equals(nomE, StringComparison.OrdinalIgnoreCase))
                || nomE.Equals(nomEntete, StringComparison.OrdinalIgnoreCase))
            {
                Refuser($"L'en-tête « {nomE} » est donné deux fois.");
                continue;
            }
            if (e.Secret)
            {
                var ancien = anciensEntetes.GetValueOrDefault(nomE);
                SecretConnexion? valeur = null;
                if (T(e.ValeurSecrete) is { Length: > 0 } nouvelle)
                {
                    valeur = secrets.Chiffrer(nouvelle, auteur, maintenant);
                    changes.Add($"en-tête {nomE} remplacé");
                }
                else if (ancien is { Secret: true, ValeurSecrete: not null }) valeur = ancien.ValeurSecrete;
                else Manque($"entete:{nomE}", Ressaisir($"la valeur de l'en-tête secret « {nomE} »"));
                entetes.Add(new EnteteConnexion { Nom = nomE, Secret = true, ValeurSecrete = valeur });
            }
            else
            {
                var valeur = T(e.Valeur);
                if (valeur.Length > LongueurMaxAdresse) Refuser($"En-tête « {nomE} » : {LongueurMaxAdresse} caractères au plus.");
                if (RenduConnexion.Texte(valeur, exemple).Inconnues is [var x, ..]) Refuser($"En-tête « {nomE} » : variable inconnue {{{{{x}}}}}.");
                entetes.Add(new EnteteConnexion { Nom = nomE, Valeur = valeur });
            }
        }
        if (entetes.Count > MaxEntetes) Refuser($"{MaxEntetes} en-têtes au plus.");

        // 3. Message
        var corps = format is FormatCorps.Json or FormatCorps.Texte ? (s.Corps ?? "").ReplaceLineEndings("\n") : "";
        if (corps.Length > LongueurMaxCorps) Refuser($"Message : {LongueurMaxCorps} caractères au plus.");
        var champs = new List<ChampConnexion>();
        if (format == FormatCorps.Formulaire)
            foreach (var ch in s.Champs ?? [])
            {
                var nomC = T(ch.Nom);
                if (nomC.Length == 0 && T(ch.Valeur).Length == 0) continue;
                if (nomC.Length == 0) Refuser("Chaque champ du formulaire a un nom.");
                if (RenduConnexion.Texte(ch.Valeur ?? "", exemple).Inconnues is [var x, ..])
                    Refuser($"Champ « {nomC} » : variable inconnue {{{{{x}}}}}.");
                champs.Add(new ChampConnexion { Nom = nomC, Valeur = (ch.Valeur ?? "").Trim() });
            }
        if (champs.Count > MaxChamps) Refuser($"{MaxChamps} champs au plus.");
        if (enregistrement)
        {
            var rendu = format switch
            {
                FormatCorps.Json => RenduConnexion.Json(corps, exemple),
                FormatCorps.Texte => RenduConnexion.Texte(corps, exemple),
                _ => null,
            };
            if (rendu?.Erreurs is [var e, ..]) Refuser($"Message : JSON invalide, ligne {e.Ligne}, colonne {e.Colonne} : {e.Message}");
            if (rendu?.Inconnues is [var x, ..]) Refuser($"Message : variable inconnue {{{{{x}}}}}.");
        }

        // 4. Quand appeler
        var declencheurs = (s.Declencheurs ?? []).Select(T).Where(d => d.Length > 0).Distinct().ToList();
        foreach (var d in declencheurs.Where(d => DeclencheursConnexion.Trouver(d) is null).ToList())
        {
            if (enregistrement) Refuser($"Déclencheur inconnu : « {d} ».");
            declencheurs.Remove(d);
        }
        declencheurs = [.. DeclencheursConnexion.Tous.Select(d => d.Cle).Where(declencheurs.Contains)];
        var f = s.Filtres ?? new FiltresConnexion();
        var filtres = new FiltresConnexion
        {
            EquipeIds = [.. f.EquipeIds.Distinct()],
            SystemeCatalogueIds = [.. f.SystemeCatalogueIds.Distinct()],
            Environnements = [.. f.Environnements.Select(T).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            NiveauxRisque = [.. f.NiveauxRisque.Distinct().Order()],
            CriticitesEtape = [.. f.CriticitesEtape.Distinct().Order()],
            TypesMep = [.. f.TypesMep.Distinct().Order()],
        };
        if (enregistrement)
        {
            if (filtres.EquipeIds.Count > 0 && await db.Equipes.CountAsync(e => filtres.EquipeIds.Contains(e.Id)) != filtres.EquipeIds.Count)
                Refuser("Filtre : équipe inconnue (supprimée entre-temps ?). Rechargez la page.");
            if (filtres.SystemeCatalogueIds.Count > 0
                && await db.SystemesCatalogue.CountAsync(x => filtres.SystemeCatalogueIds.Contains(x.Id)) != filtres.SystemeCatalogueIds.Count)
                Refuser("Filtre : système du catalogue inconnu (supprimé entre-temps ?). Rechargez la page.");
        }
        var libelleEnvoi = T(s.LibelleEnvoiManuel);
        if (enregistrement && s.EnvoiManuel && libelleEnvoi.Length == 0)
            Refuser("Le libellé dans la MEP est obligatoire pour l'envoi depuis une MEP (par exemple « Prévenir le canal Exploitation »).");
        if (enregistrement && libelleEnvoi.Length > 80) Refuser("Libellé dans la MEP : 80 caractères au plus.");

        // Changements (journal d'administration), puis écriture.
        Si(c.Nom, nom, "nom");
        Si(c.Description, description, "description");
        Si(c.Methode, methode, "méthode");
        Si(c.Url, url, "adresse");
        Si(c.Authentification, authentification, "authentification");
        Si((c.Utilisateur, c.NomEntete, c.TokenUrl, c.ClientId, c.Scope), (utilisateur, nomEntete, tokenUrl, clientId, scope), "paramètres d'identification");
        Si(string.Join("\n", c.Entetes.Select(e => $"{e.Nom}:{e.Secret}:{e.Valeur}")), string.Join("\n", entetes.Select(e => $"{e.Nom}:{e.Secret}:{e.Valeur}")), "en-têtes");
        Si((c.Format, c.Corps, string.Join("&", c.Champs.Select(x => $"{x.Nom}={x.Valeur}"))), (format, corps, string.Join("&", champs.Select(x => $"{x.Nom}={x.Valeur}"))), "message");
        Si((c.DelaiSecondes, c.Tentatives), (delai, tentatives), "délai et tentatives");
        var ajoutes = declencheurs.Except(c.Declencheurs).Select(DeclencheursConnexion.Libelle).ToList();
        var retires = c.Declencheurs.Except(declencheurs).Select(DeclencheursConnexion.Libelle).ToList();
        if (ajoutes.Count + retires.Count > 0)
            changes.Add($"déclencheurs ({string.Join(", ", ajoutes.Select(a => "+ " + a).Concat(retires.Select(r => "− " + r)))})");
        Si(System.Text.Json.JsonSerializer.Serialize(c.Filtres ?? new FiltresConnexion()), System.Text.Json.JsonSerializer.Serialize(filtres), "filtres");
        Si((c.EnvoiManuel, c.LibelleEnvoiManuel), (s.EnvoiManuel, libelleEnvoi), "envoi depuis une MEP");

        c.Nom = nom;
        c.Description = description;
        c.Modele = s.Modele ?? c.Modele;
        c.Methode = methode;
        c.Url = url;
        c.Authentification = authentification;
        c.Utilisateur = utilisateur;
        c.NomEntete = nomEntete;
        c.TokenUrl = tokenUrl;
        c.ClientId = clientId;
        c.Scope = scope;
        c.Entetes = entetes;
        c.Format = format;
        c.Corps = corps;
        c.Champs = champs;
        c.DelaiSecondes = Math.Clamp(delai, 1, 30);
        c.Tentatives = Math.Clamp(tentatives, 1, 5);
        c.Declencheurs = declencheurs;
        c.Filtres = filtres;
        c.EnvoiManuel = s.EnvoiManuel;
        c.LibelleEnvoiManuel = libelleEnvoi;
        c.OrigineSecrets = origine;
        if (s.Active is { } active && active != c.Active)
        {
            changes.Add(active ? "activée" : "désactivée");
            c.Active = active;
        }
        if (enregistrement && changes.Count > 0)
        {
            c.ModifieeLe = maintenant;
            c.ModifieePar = auteur;
        }
        return changes;
    }

    /// <summary>
    /// Brouillon de l'éditeur (aperçu, essai) : la saisie appliquée à une copie de la connexion enregistrée
    /// (<paramref name="enregistree"/>, dont les secrets laissés vides sont repris si le domaine n'a pas changé), sans rien
    /// enregistrer.
    /// </summary>
    public async Task<ConnexionApi> Brouillon(ConnexionApiSaisie s, ConnexionApi? enregistree, string auteur, UsageSaisie usage)
    {
        var copie = enregistree is null ? new ConnexionApi { Id = Guid.Empty } : Copier(enregistree);
        copie.Active = true;
        await Appliquer(copie, s with { Active = null }, auteur, usage);
        return copie;
    }

    /// <summary>Copie d'une connexion, secrets compris (brouillon de l'éditeur, jamais enregistré).</summary>
    private static ConnexionApi Copier(ConnexionApi c) =>
        System.Text.Json.JsonSerializer.Deserialize<ConnexionApi>(System.Text.Json.JsonSerializer.Serialize(c))!;
}
