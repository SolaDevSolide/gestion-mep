using GestionMep.Api.Services;

namespace GestionMep.Api.Cli;

/// <summary>
/// <c>gestion-mep charte</c> : charte visuelle (couleurs de l'interface et des exports Excel). Mêmes règles que l'onglet
/// « Charte visuelle » de la console : la charte enregistrée ici remplace le fichier charte.json, « retablir » y revient.
/// </summary>
public static partial class LigneDeCommande
{
    private static readonly string[] RolesDefinissables = [.. CharteVisuelle.Roles, .. CharteVisuelle.EtatsConnus];

    private static string Origine(EtatCharte e) => e.Origine switch
    {
        "console" => $"console : modifiée le {e.ModifieLe:yyyy-MM-dd HH:mm} par {e.ModifiePar}"
            + (e.Fichier.Charte is not null ? $" ; remplace le fichier {e.Fichier.Chemin}" : ""),
        "fichier" => $"fichier {e.Fichier.Chemin}",
        _ => "charte par défaut",
    };

    private static void AfficherProblemes(string titre, List<ProblemeCharte> problemes)
    {
        if (problemes.Count == 0) return;
        Ecrire(titre);
        foreach (var p in problemes) Ecrire($"  - {p.Message}");
    }

    private static async Task<int> CharteAfficher()
    {
        var config = Configuration();
        await using var db = Base(config);
        var etat = await ServiceCharte.Charger(db, config);
        if (EnJson)
        {
            EcrireJson(ServiceCharte.Dto(etat));
            return 0;
        }
        var c = etat.Charte;
        Ecrire($"Charte visuelle « {c.Nom} » [{Origine(etat)}]");
        foreach (var role in CharteVisuelle.Roles)
        {
            var clair = role == "tertiaire" && c.TertiaireLiee ? "suit la primaire" : CharteVisuelle.CouleurSaisie(c, role).ToUpperInvariant();
            var sombre = CharteVisuelle.CouleurRole(c, role, ModeCharte.Sombre).ToUpperInvariant();
            var retouche = (role == "tertiaire" && c.TertiaireLiee ? c.Sombre.ContainsKey("primaire") : c.Sombre.ContainsKey(role)) ? "retouchée" : "calculée";
            Ecrire($"  {role,-13}{clair,-18}thème sombre {sombre} ({retouche})");
        }
        Ecrire($"  {"menu latéral",-13}{(c.MenuLateral == "secondaire" ? "secondaire" : "comme les pages")}");
        Ecrire($"  {"gris",-13}{c.Gris}");
        Ecrire($"  {"états",-13}{(c.Etats.Count == 0 ? "standard" : string.Join(", ", c.Etats.Select(e => $"{e.Key} {e.Value.ToUpperInvariant()}")))}");
        Ecrire(etat.Fichier switch
        {
            { Present: false } f => $"Fichier : {f.Chemin} absent (facultatif).",
            { Erreur: { } erreur } f => $"Fichier : {f.Chemin} invalide, ignoré : {erreur}",
            var f => $"Fichier : {f.Chemin} valide.",
        });
        AfficherProblemes("Fichier, avertissements :", etat.Fichier.Avertissements);
        var avertissements = CharteVisuelle.Avertissements(c);
        if (avertissements.Count == 0) Ecrire("Lisibilité : tout est lisible (WCAG AA, thèmes clair et sombre).");
        else AfficherProblemes($"Lisibilité : {avertissements.Count} point(s) à revoir (avertissements, rien n'est bloqué) :", avertissements);
        return 0;
    }

    private static async Task<int> CharteExporter(string? fichier)
    {
        var config = Configuration();
        await using var db = Base(config);
        var etat = await ServiceCharte.Charger(db, config);
        var texte = CharteVisuelle.Serialiser(etat.Charte, CharteVisuelle.CheminSchema(config["Application:UrlPublique"]));
        if (fichier is null or "-")
        {
            S.DonneesEcrites = true; // le fichier tel quel, même avec --json
            Console.WriteLine(texte);
            return 0;
        }
        if (Path.GetDirectoryName(Path.GetFullPath(fichier)) is { } dossier) Directory.CreateDirectory(dossier);
        await File.WriteAllTextAsync(fichier, texte + "\n");
        Informer($"Charte « {etat.Charte.Nom} » ({Origine(etat)}) exportée dans {fichier}.");
        return 0;
    }

    /// <summary>Lit un fichier de charte : affiche erreurs et avertissements ; null s'il est invalide.</summary>
    private static async Task<Charte?> LireCharte(string fichier)
    {
        var lecture = CharteVisuelle.Lire(await LireFichierOuEntree(fichier));
        AfficherProblemes("Erreurs (fichier refusé) :", lecture.Erreurs);
        AfficherProblemes("Avertissements :", lecture.Avertissements);
        if (lecture.Charte is { } c) AfficherProblemes("Lisibilité, à revoir (avertissements) :", CharteVisuelle.Avertissements(c));
        return lecture.Charte;
    }

    private static async Task<int> CharteImporter(string fichier, string[] arguments)
    {
        var o = Options.Lire(arguments, [], "--essai");
        var charte = await LireCharte(fichier);
        if (charte is null) return 1;
        if (o.A("--essai"))
        {
            Informer($"Essai, rien n'a été enregistré : charte « {charte.Nom} » valide.");
            return 0;
        }
        var config = Configuration();
        await using var db = Base(config);
        var etat = await ServiceCharte.Enregistrer(db, config, charte, null, DateTimeOffset.UtcNow);
        Informer($"Charte « {etat.Charte.Nom} » enregistrée : elle remplace le fichier et s'applique sous 10 secondes, sans redémarrage.");
        return 0;
    }

    private static async Task<int> CharteVerifier(string? fichier)
    {
        if (fichier is not null)
        {
            var charte = await LireCharte(fichier);
            Ecrire(charte is null ? "Fichier invalide." : $"Fichier valide : charte « {charte.Nom} ».");
            return charte is null ? 1 : 0;
        }
        var config = Configuration();
        await using var db = Base(config);
        var etat = await ServiceCharte.Charger(db, config);
        Ecrire($"Charte en vigueur « {etat.Charte.Nom} » [{Origine(etat)}].");
        AfficherProblemes("Lisibilité, à revoir (avertissements) :", CharteVisuelle.Avertissements(etat.Charte));
        if (etat.Fichier.Erreur is { } erreur)
        {
            Ecrire($"Fichier {etat.Fichier.Chemin} invalide, ignoré : {erreur}");
            return 1;
        }
        AfficherProblemes($"Fichier {etat.Fichier.Chemin}, avertissements :", etat.Fichier.Avertissements);
        Ecrire("Charte valide.");
        return 0;
    }

    private static async Task<int> CharteDefinir(string role, string valeur)
    {
        role = role.ToLowerInvariant();
        if (!RolesDefinissables.Contains(role))
            throw new ErreurUsage($"rôle inconnu « {role} » : {string.Join(", ", RolesDefinissables)}");
        var config = Configuration();
        await using var db = Base(config);
        var c = (await ServiceCharte.Charger(db, config)).Charte;
        var lien = role == "tertiaire" && string.Equals(valeur.Trim(), CharteVisuelle.SuitPrimaire, StringComparison.OrdinalIgnoreCase);
        var hex = lien ? CharteVisuelle.SuitPrimaire : CouleursCharte.NormaliserHex(valeur)
            ?? throw ErreurHttp.Saisie($"« {valeur} » n'est pas une couleur (attendu #RRGGBB ou #RGB, par exemple 0F766E ; dans un shell, « # » entre guillemets)."
                + (role == "tertiaire" ? " « primaire » : la tertiaire suit la primaire." : ""));
        var nouvelle = role switch
        {
            "primaire" => c with { Primaire = hex },
            "secondaire" => c with { Secondaire = hex },
            "tertiaire" => c with { Tertiaire = hex },
            _ => c with { Etats = new Dictionary<string, string>(c.Etats) { [role] = hex } },
        };
        var etat = await ServiceCharte.Enregistrer(db, config, nouvelle, null, DateTimeOffset.UtcNow);
        Informer($"Charte enregistrée : {CharteVisuelle.LibelleRole(role).ToLowerInvariant()} {(lien ? "suit la primaire" : hex.ToUpperInvariant())}. "
            + "Appliquée sous 10 secondes, sans redémarrage.");
        AfficherProblemes("Lisibilité, à revoir (avertissements) :", CharteVisuelle.Avertissements(etat.Charte));
        return 0;
    }

    private static async Task<int> CharteRetablir()
    {
        var config = Configuration();
        await using var db = Base(config);
        var etat = await ServiceCharte.Retablir(db, config, null);
        Informer(etat.Origine == "fichier"
            ? $"Charte de la console effacée : retour au fichier {etat.Fichier.Chemin} (« {etat.Charte.Nom} »)."
            : "Charte de la console effacée : retour à la charte par défaut.");
        if (etat.Fichier.Erreur is { } erreur) Avertir($"Attention : fichier {etat.Fichier.Chemin} invalide, ignoré : {erreur}");
        return 0;
    }
}
