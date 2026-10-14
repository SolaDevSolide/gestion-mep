using System.Globalization;
using System.Text;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Objets impactés d'une MEP en classeur Excel : un onglet « Objets impactés » (une ligne par objet : système, catégorie,
/// type, nom, libellé, précision, version ou correction), vide pour le modèle ou rempli pour l'export, et un onglet
/// « Catalogue » en lecture seule qui liste les objets connus de chaque système de la MEP. Le réimport (<see cref="Lire"/>)
/// rattache chaque ligne à un objet de la fiche de son système au catalogue ; les autres lignes sont rejetées.
/// </summary>
public static class ObjetsExcel
{
    public const string FeuilleObjets = "Objets impactés";
    public const string FeuilleCatalogue = "Catalogue";
    /// <summary>Lignes prêtes à remplir dans le modèle (listes déroulantes comprises).</summary>
    private const int LignesModele = 500;
    private const int LigneEnTetes = 4;
    /// <summary>Lignes lues au plus, et lignes vides consécutives qui arrêtent la lecture.</summary>
    private const int LignesMax = 5000, VidesMax = 50;

    private static readonly string[] Colonnes = ["Système", "Catégorie", "Type", "Nom", "Libellé", "Précision", "Version ou correction"];
    private static readonly double[] Largeurs = [14, 24, 16, 30, 36, 24, 22];
    private const int ColSysteme = 1, ColNom = 4;

    /// <summary>Nom de fichier proposé : « objets-impactes-Outil-1.2.xlsx » (« -modele » pour le modèle vide).</summary>
    public static string NomFichier(Mep mep, bool modele)
    {
        var nom = new string(RecetteExcel.NomMep(mep).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray()).Trim('-');
        return $"objets-impactes{(modele ? "-modele" : "")}-{(nom.Length == 0 ? "mep" : nom)}.xlsx";
    }

    /// <summary>
    /// Classeur des objets impactés : vide (<paramref name="modele"/>) ou avec les objets de la MEP. Les systèmes et objets
    /// proposés viennent de <paramref name="catalogue"/> (fiches des systèmes de la MEP).
    /// </summary>
    public static byte[] Exporter(Mep mep, IReadOnlyDictionary<Guid, SystemeCatalogue> catalogue, bool modele)
    {
        using var classeur = new ClasseurExcel();
        var systemes = mep.Systemes.Where(s => s.CatalogueId is { } c && catalogue.ContainsKey(c)).ToList();

        var f = classeur.AjouterFeuille(FeuilleObjets);
        f.Ecrire(1, 1, $"Objets impactés · {RecetteExcel.NomMep(mep)}", Charte.Titre);
        f.Ecrire(2, 1, "Une ligne par objet. Système, catégorie et nom doivent correspondre à un objet de la fiche du système au catalogue "
            + "(onglet « Catalogue ») ; les autres lignes sont rejetées à l'import. Type, libellé et précision sont repris du catalogue. "
            + "L'import ajoute les objets et met à jour les versions ; il ne retire rien.", Charte.SousTitre);
        f.Fusionner(1, 1, 1, Colonnes.Length);
        f.HauteurLigne(1, 24);
        f.Fusionner(2, 1, 2, Colonnes.Length);
        f.HauteurLigne(2, 46);
        for (var i = 0; i < Colonnes.Length; i++)
        {
            f.Ecrire(LigneEnTetes, i + 1, Colonnes[i], Charte.EnTete);
            f.LargeurColonne(i + 1, Largeurs[i]);
        }
        var ligne = LigneEnTetes + 1;
        if (!modele)
            foreach (var o in Regles.ObjetsTries(mep, catalogue))
            {
                var systeme = mep.Systemes.FirstOrDefault(s => s.Id == o.SystemeId)?.Nom ?? "";
                object?[] valeurs = [systeme, o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision, o.Version];
                for (var i = 0; i < valeurs.Length; i++)
                    f.Ecrire(ligne, i + 1, valeurs[i], i + 1 == ColNom ? Charte.Cellule with { ChasseFixe = true } : Charte.Cellule);
                ligne++;
            }
        var derniere = Math.Max(ligne - 1, LigneEnTetes + LignesModele);
        f.Formater(ligne, 1, derniere, Colonnes.Length, Charte.ASaisir);
        var noms = systemes.Select(s => s.Nom).ToList();
        if (noms.Count > 0 && string.Join(",", noms).Length < 250) f.ListeDeroulante(LigneEnTetes + 1, ColSysteme, derniere, ColSysteme, noms);
        f.FigerLignes(LigneEnTetes);

        var cat = classeur.AjouterFeuille(FeuilleCatalogue);
        cat.Ecrire(1, 1, "Objets connus des systèmes de la MEP", Charte.Titre with { Taille = 14 });
        cat.Ecrire(2, 1, "Seuls ces objets peuvent être retenus. Un objet manque ? Un administrateur l'ajoute à la fiche du système (Paramètres › Systèmes).",
            Charte.SousTitre);
        cat.Fusionner(1, 1, 1, 6);
        cat.Fusionner(2, 1, 2, 6);
        string[] entetes = ["Système", "Catégorie", "Type", "Nom", "Libellé", "Précision"];
        for (var i = 0; i < entetes.Length; i++)
        {
            cat.Ecrire(LigneEnTetes, i + 1, entetes[i], Charte.EnTete);
            cat.LargeurColonne(i + 1, Largeurs[i]);
        }
        var l = LigneEnTetes + 1;
        foreach (var s in systemes)
        {
            var fiche = catalogue[s.CatalogueId!.Value];
            foreach (var o in fiche.Objets.OrderBy(o => fiche.Categories.IndexOf(o.Categorie)).ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase))
            {
                object?[] valeurs = [s.Nom, o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision];
                for (var i = 0; i < valeurs.Length; i++)
                    cat.Ecrire(l, i + 1, valeurs[i], i + 1 == ColNom ? Charte.Cellule with { ChasseFixe = true } : Charte.Cellule);
                l++;
            }
        }
        if (l == LigneEnTetes + 1) cat.Ecrire(l, 1, "Aucun objet au catalogue pour les systèmes de cette MEP.", Charte.SousTitre);
        cat.FigerLignes(LigneEnTetes);
        cat.Proteger();
        return classeur.Enregistrer();
    }

    /// <summary>Ligne retenue (rattachée à un objet du catalogue) ou rejetée, et anomalie de forme qui refuse le fichier entier.</summary>
    public record Lecture(List<Regles.LigneObjet> Retenues, List<LigneRejeteeDto> Rejetees, string? Erreur);

    private static string Cle(string texte)
    {
        var d = texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    /// <summary>
    /// Lit un classeur rempli (modèle, export ou fichier construit à la main avec les mêmes en-têtes) : onglet « Objets
    /// impactés » ou, à défaut, le premier ; ligne d'en-têtes repérée par la colonne « Nom ». Chaque ligne est rattachée à
    /// un objet de la fiche de son système (même nom, même catégorie si elle est donnée, même type s'il départage).
    /// <see cref="ClasseurIllisibleException"/> si ce n'est pas un classeur .xlsx.
    /// </summary>
    public static Lecture Lire(byte[] contenu, Mep mep, IReadOnlyDictionary<Guid, SystemeCatalogue> catalogue)
    {
        using var lecteur = LecteurClasseur.Ouvrir(contenu);
        var f = lecteur.Feuille(FeuilleObjets) ?? (lecteur.Feuilles.Count > 0 ? lecteur.Feuille(lecteur.Feuilles[0]) : null);
        if (f is null) return new([], [], "Classeur vide.");

        // En-têtes : une ligne qui contient « Nom », dans les 15 premières.
        var colonnes = new Dictionary<string, int>();
        var enTetes = 0;
        for (var r = 1; r <= 15 && enTetes == 0; r++)
        {
            var trouvees = new Dictionary<string, int>();
            for (var c = 1; c <= 20; c++)
            {
                var t = Cle(f.Texte(r, c));
                var nom = t switch
                {
                    "systeme" => "systeme",
                    "categorie" => "categorie",
                    "type" => "type",
                    "nom" or "objet" => "nom",
                    "version ou correction" or "version" or "correction" => "version",
                    _ => null,
                };
                if (nom is not null && !trouvees.ContainsKey(nom)) trouvees[nom] = c;
            }
            if (trouvees.ContainsKey("nom"))
            {
                enTetes = r;
                colonnes = trouvees;
            }
        }
        if (enTetes == 0)
            return new([], [], "Fichier non reconnu : la ligne d'en-têtes (Système, Catégorie, Type, Nom, Version ou correction) est introuvable. "
                + "Partez du modèle Excel de la page.");

        var candidats = mep.Systemes.Where(s => s.CatalogueId is { } c && catalogue.ContainsKey(c)).ToList();
        string Valeur(int ligne, string colonne) => colonnes.TryGetValue(colonne, out var c) ? f.Texte(ligne, c) : "";
        var retenues = new List<Regles.LigneObjet>();
        var rejetees = new List<LigneRejeteeDto>();
        var vides = 0;
        for (var r = enTetes + 1; r <= enTetes + LignesMax && vides < VidesMax; r++)
        {
            var (systeme, categorie, type, nom, version) = (Valeur(r, "systeme"), Valeur(r, "categorie"), Valeur(r, "type"), Valeur(r, "nom"), Valeur(r, "version"));
            if (systeme.Length + categorie.Length + nom.Length + version.Length == 0)
            {
                vides++;
                continue;
            }
            vides = 0;
            LigneRejeteeDto Rejet(string raison, Guid? catalogueId = null) => new(r, systeme, categorie, nom, raison, catalogueId);

            Systeme? s;
            if (systeme.Length == 0)
            {
                // Système non précisé : admis seulement si la MEP n'a qu'un système du catalogue.
                if (candidats.Count != 1) { rejetees.Add(Rejet("Système non précisé.")); continue; }
                s = candidats[0];
            }
            else s = mep.Systemes.FirstOrDefault(x => x.Nom.Equals(systeme, StringComparison.OrdinalIgnoreCase));
            if (s is null) { rejetees.Add(Rejet($"Le système {systeme} n'est pas dans la MEP.")); continue; }
            if (s.CatalogueId is not { } cid || !catalogue.TryGetValue(cid, out var fiche))
            {
                rejetees.Add(Rejet($"{s.Nom} n'est pas issu du catalogue : ses objets ne se choisissent pas."));
                continue;
            }
            if (nom.Length == 0) { rejetees.Add(Rejet("Nom de l'objet manquant.", cid)); continue; }
            var memes = fiche.Objets.Where(o => o.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase)
                && (categorie.Length == 0 || o.Categorie.Equals(categorie, StringComparison.OrdinalIgnoreCase))).ToList();
            if (memes.Count > 1 && type.Length > 0) memes = memes.Where(o => o.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
            if (memes.Count == 0)
            {
                rejetees.Add(Rejet(categorie.Length == 0
                    ? $"Objet absent de la fiche {s.Nom} du catalogue."
                    : $"Objet absent de la catégorie {categorie} de la fiche {s.Nom} du catalogue.", cid));
                continue;
            }
            if (memes.Count > 1) { rejetees.Add(Rejet($"Plusieurs objets « {nom} » dans la fiche {s.Nom} : préciser la catégorie.", cid)); continue; }
            if (version.Length > 200) { rejetees.Add(Rejet("Version ou correction trop longue (200 caractères au plus).", cid)); continue; }
            retenues.Add(new Regles.LigneObjet(s, memes[0], version));
        }
        return new(retenues, rejetees, null);
    }
}
