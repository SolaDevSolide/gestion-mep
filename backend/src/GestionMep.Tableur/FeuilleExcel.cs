using ClosedXML.Excel;

namespace GestionMep.Tableur;

/// <summary>Formule Excel à écrire dans une cellule de tableau (sans « = ») ; <c>{ligne}</c> est remplacé par le numéro de ligne.</summary>
public sealed record FormuleExcel(string Texte);

/// <summary>Texte à plusieurs polices dans une même cellule : morceaux de texte courant et de code (chasse fixe).</summary>
public sealed record TexteRiche(IReadOnlyList<(string Texte, bool Code)> Morceaux)
{
    public string Brut => string.Join("\n", Morceaux.Select(m => m.Texte));
}

/// <summary>Colonne d'un tableau : en-tête, largeur (en caractères) et nature des valeurs.</summary>
public sealed record ColonneTableau(string Titre, double Largeur, TypeColonne Type = TypeColonne.Texte)
{
    /// <summary>Colonne retirée du tableau si aucune ligne n'y a de valeur (évite les colonnes vides).</summary>
    public bool Facultative { get; init; }
    /// <summary>Colonne masquée (identifiants techniques nécessaires au réimport).</summary>
    public bool Masquee { get; init; }
    /// <summary>Aide affichée en note sur l'en-tête.</summary>
    public string? Note { get; init; }
    /// <summary>Tonalité de certaines valeurs (mise en forme conditionnelle : « Réussie » en vert…).</summary>
    public IReadOnlyDictionary<string, Ton>? Tons { get; init; }
    /// <summary>Colonne à remplir par le destinataire : fond jaune pâle, modifiable même si la feuille est protégée.</summary>
    public bool ASaisir { get; init; }
    /// <summary>Texte non renvoyé à la ligne (valeurs courtes).</summary>
    public bool SurUneLigne { get; init; }
}

/// <summary>Emplacement d'un tableau écrit par <see cref="FeuilleExcel.Tableau"/>.</summary>
public sealed class TableauEcrit
{
    internal TableauEcrit(FeuilleExcel feuille, int ligneEnTetes, int derniere, IReadOnlyList<ColonneTableau> colonnes, string? nom)
    {
        Feuille = feuille;
        LigneEnTetes = ligneEnTetes;
        DerniereLigne = derniere;
        Colonnes = colonnes;
        Nom = nom;
    }

    public FeuilleExcel Feuille { get; }
    public int LigneEnTetes { get; }
    public int PremiereLigne => LigneEnTetes + 1;
    /// <summary>Dernière ligne du tableau (égale à <see cref="LigneEnTetes"/> s'il est vide).</summary>
    public int DerniereLigne { get; }
    /// <summary>Colonnes réellement écrites (sans les colonnes facultatives vides).</summary>
    public IReadOnlyList<ColonneTableau> Colonnes { get; }
    /// <summary>Nom du tableau Excel (null si le tableau n'a aucune ligne).</summary>
    public string? Nom { get; }

    /// <summary>Numéro (à partir de 1) de la colonne d'en-tête donnée, ou 0 si elle n'a pas été écrite.</summary>
    public int Colonne(string titre)
    {
        for (var i = 0; i < Colonnes.Count; i++)
            if (string.Equals(Colonnes[i].Titre, titre, StringComparison.Ordinal)) return i + 1;
        return 0;
    }

    /// <summary>Adresse absolue des valeurs d'une colonne, préfixée de l'onglet : <c>'API'!$F$5:$F$12</c>.</summary>
    public string Adresse(string titre)
    {
        var c = Colonne(titre);
        if (c == 0) throw new ArgumentException($"Colonne « {titre} » absente du tableau.", nameof(titre));
        var lettre = XLHelper.GetColumnLetterFromNumber(c);
        return $"'{Feuille.Nom.Replace("'", "''", StringComparison.Ordinal)}'!${lettre}${PremiereLigne}:${lettre}${Math.Max(PremiereLigne, DerniereLigne)}";
    }
}

/// <summary>Feuille d'un <see cref="ClasseurExcel"/> : lignes et colonnes numérotées à partir de 1, comme dans Excel.</summary>
public sealed class FeuilleExcel
{
    /// <summary>Texte maximal d'une cellule Excel.</summary>
    public const int TexteMax = 32_767;

    internal IXLWorksheet Interne { get; }
    private readonly ClasseurExcel _classeur;

    internal FeuilleExcel(IXLWorksheet feuille, ClasseurExcel classeur)
    {
        Interne = feuille;
        _classeur = classeur;
    }

    /// <summary>Nom réel de l'onglet (après assainissement).</summary>
    public string Nom => Interne.Name;

    public StylesExcel Styles => _classeur.Styles;

    // ---------- Blocs ----------

    /// <summary>
    /// Bloc de titre en haut de l'onglet : titre (ligne 1), contexte (ligne 2, par exemple la MEP et sa phase) et mention
    /// « Généré par … le … » (ligne 3), sur <paramref name="colonnes"/> colonnes de large. Renvoie la ligne suivante (4).
    /// </summary>
    public int BlocTitre(string titre, string? contexte, int colonnes, double largeur = 0)
    {
        var n = Math.Max(1, colonnes);
        Ecrire(1, 1, titre, Styles.Titre);
        HauteurLigne(1, 26);
        if (!string.IsNullOrWhiteSpace(contexte))
        {
            Ecrire(2, 1, contexte, Styles.Contexte);
            Fusionner(2, 1, 2, n);
            HauteurLigne(2, StylesExcel.HauteurTexte(contexte, largeur > 0 ? largeur : Largeurs(1, n)));
        }
        Ecrire(3, 1, _classeur.Infos.Mention, Styles.Mention with { RetourALaLigne = false });
        HauteurLigne(3, 20);
        return 4;
    }

    private double Largeurs(int c1, int c2)
    {
        double total = 0;
        for (var c = c1; c <= c2; c++) total += Interne.Column(c).Width;
        return total;
    }

    /// <summary>
    /// Tableau : ligne d'en-têtes à <paramref name="ligne"/>, une ligne par élément, puis <paramref name="lignesVides"/>
    /// lignes prêtes à remplir. C'est un vrai tableau Excel (boutons de filtre, lignes en bandes) ; en-têtes figés et
    /// répétés à l'impression, largeurs et formats fixés par les colonnes, hauteur des lignes ajustée au texte.
    /// Les colonnes facultatives sans aucune valeur sont retirées. Sans ligne, <paramref name="siVide"/> s'affiche sous les
    /// en-têtes. Une valeur peut être un <see cref="FormuleExcel"/> ou un <see cref="TexteRiche"/>.
    /// </summary>
    public TableauEcrit Tableau(int ligne, string nom, IReadOnlyList<ColonneTableau> colonnes, IEnumerable<object?[]> lignes,
        string? siVide = null, int lignesVides = 0, bool figer = true)
    {
        var donnees = lignes.ToList();
        bool Vide(object? v) => v is null || v is string { Length: 0 };
        var gardees = Enumerable.Range(0, colonnes.Count)
            .Where(i => !colonnes[i].Facultative || donnees.Any(l => i < l.Length && !Vide(l[i]))).ToList();
        var cols = gardees.Select(i => colonnes[i]).ToList();

        for (var c = 0; c < cols.Count; c++)
        {
            LargeurColonne(c + 1, cols[c].Largeur);
            if (cols[c].Masquee) Interne.Column(c + 1).Hide();
        }

        // En-têtes.
        double hauteurEnTete = 18;
        for (var c = 0; c < cols.Count; c++)
        {
            Ecrire(ligne, c + 1, cols[c].Titre, Styles.EnTete with { Centre = cols[c].Type is TypeColonne.Court });
            if (cols[c].Note is { Length: > 0 } note) Note(ligne, c + 1, note);
            if (!cols[c].Masquee) hauteurEnTete = Math.Max(hauteurEnTete, StylesExcel.HauteurTexte(cols[c].Titre, cols[c].Largeur - 1, 18));
        }
        HauteurLigne(ligne, hauteurEnTete);

        var l = ligne + 1;
        foreach (var valeurs in donnees)
        {
            double hauteur = 15;
            for (var c = 0; c < cols.Count; c++)
            {
                var i = gardees[c];
                var v = i < valeurs.Length ? valeurs[i] : null;
                hauteur = Math.Max(hauteur, EcrireValeur(l, c + 1, v, cols[c]));
            }
            HauteurLigne(l, hauteur);
            l++;
        }
        for (var k = 0; k < lignesVides; k++, l++)
            for (var c = 0; c < cols.Count; c++) Formater(l, c + 1, l, c + 1, FormatColonne(cols[c]));
        var derniere = l - 1;

        string? nomTable = null;
        if (derniere > ligne)
        {
            nomTable = _classeur.NomTable(nom);
            var table = Interne.Range(ligne, 1, derniere, cols.Count).CreateTable(nomTable);
            table.Theme = XLTableTheme.TableStyleLight1;
            table.ShowRowStripes = true;
            table.ShowAutoFilter = true;
            table.ShowTotalsRow = false;
            // Le thème de tableau ne doit pas écraser l'en-tête aux couleurs de la charte.
            Formater(ligne, 1, ligne, cols.Count, Styles.EnTete);
            for (var c = 0; c < cols.Count; c++)
            {
                if (cols[c].Tons is { Count: > 0 } tons) Tons(ligne + 1, c + 1, derniere, c + 1, tons);
            }
        }
        else if (!string.IsNullOrWhiteSpace(siVide))
        {
            Ecrire(ligne + 1, 1, siVide, Styles.Vide);
            Fusionner(ligne + 1, 1, ligne + 1, Math.Max(1, cols.Count(c => !c.Masquee)));
            Formater(ligne + 1, 1, ligne + 1, Math.Max(1, cols.Count(c => !c.Masquee)), Styles.Vide);
            HauteurLigne(ligne + 1, 20);
        }
        if (figer) FigerLignes(ligne);
        Interne.PageSetup.SetRowsToRepeatAtTop(ligne, ligne);
        return new TableauEcrit(this, ligne, Math.Max(ligne, derniere), cols, nomTable);
    }

    private Format FormatColonne(ColonneTableau c)
    {
        var f = c.ASaisir ? Styles.ASaisir : c.Type == TypeColonne.Code ? Styles.Cellule with { ChasseFixe = true } : Styles.Cellule;
        return f with
        {
            NombreFormat = StylesExcel.FormatNombre(c.Type),
            Droite = c.Type is TypeColonne.Entier or TypeColonne.Nombre or TypeColonne.Duree,
            Centre = c.Type is TypeColonne.Court or TypeColonne.Date or TypeColonne.DateHeure,
            RetourALaLigne = !c.SurUneLigne && c.Type is TypeColonne.Texte or TypeColonne.Code,
        };
    }

    /// <summary>Écrit une valeur de tableau ; renvoie la hauteur de ligne qu'elle demande.</summary>
    private double EcrireValeur(int ligne, int colonne, object? valeur, ColonneTableau c)
    {
        var format = FormatColonne(c);
        switch (valeur)
        {
            case FormuleExcel f:
                Formule(ligne, colonne, f.Texte.Replace("{ligne}", ligne.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), format);
                return 15;
            case TexteRiche t:
                EcrireRiche(ligne, colonne, t, format);
                return t.Morceaux.Sum(m => StylesExcel.HauteurTexte(m.Texte, c.Largeur, 0, m.Code)) + 2;
            case int or long when c.Type == TypeColonne.Duree:
                Ecrire(ligne, colonne, Convert.ToDouble(valeur, System.Globalization.CultureInfo.InvariantCulture) / 1440d, format);
                return 15;
            case TimeSpan d:
                Ecrire(ligne, colonne, d.TotalDays, format);
                return 15;
            case string s:
                Ecrire(ligne, colonne, s, format);
                return c.Masquee || c.SurUneLigne ? 15 : StylesExcel.HauteurTexte(s, c.Largeur - 1, 15, c.Type == TypeColonne.Code);
            default:
                Ecrire(ligne, colonne, valeur, valeur is DateTime { TimeOfDay.Ticks: > 0 } && c.Type == TypeColonne.Texte
                    ? format with { NombreFormat = StylesExcel.FormatDateHeure }
                    : valeur is DateTime or DateOnly && c.Type == TypeColonne.Texte ? format with { NombreFormat = StylesExcel.FormatDate } : format);
                return 15;
        }
    }

    /// <summary>
    /// Fiche « libellé / valeur » à partir de <paramref name="ligne"/> (colonnes A et B) : une ligne par champ ; un champ
    /// sans valeur (null) est un intertitre en bandeau ; une valeur vide s'affiche « Non renseigné ». Renvoie la ligne suivante.
    /// </summary>
    public int Fiche(int ligne, IEnumerable<(string Libelle, object? Valeur)> champs, double largeurLibelle = 34, double largeurValeur = 90)
    {
        LargeurColonne(1, largeurLibelle);
        LargeurColonne(2, largeurValeur);
        var premiere = true;
        foreach (var (libelle, valeur) in champs)
        {
            if (valeur is null)
            {
                if (!premiere) ligne++;
                Ecrire(ligne, 1, libelle, Styles.Section);
                Fusionner(ligne, 1, ligne, 2);
                Formater(ligne, 1, ligne, 2, Styles.Section);
                HauteurLigne(ligne, 20);
            }
            else
            {
                Ecrire(ligne, 1, libelle, Styles.Libelle);
                var texte = valeur as string;
                if (texte is { Length: 0 }) Ecrire(ligne, 2, "Non renseigné", Styles.Vide);
                else
                    Ecrire(ligne, 2, valeur, valeur switch
                    {
                        DateTime { TimeOfDay.Ticks: > 0 } => Styles.Cellule with { NombreFormat = StylesExcel.FormatDateHeure, Droite = false },
                        DateTime or DateOnly => Styles.Cellule with { NombreFormat = StylesExcel.FormatDate },
                        _ => Styles.Cellule,
                    });
                HauteurLigne(ligne, Math.Max(StylesExcel.HauteurTexte(texte ?? "", largeurValeur - 1, 18), StylesExcel.HauteurTexte(libelle, largeurLibelle - 1, 18)));
            }
            premiere = false;
            ligne++;
        }
        Interne.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        return ligne;
    }

    // ---------- Cellules ----------

    /// <summary>
    /// Écrit une valeur (texte, nombre, date, booléen ou null) avec sa mise en forme. Un texte trop long pour Excel est
    /// tronqué ; un texte qui commence par « = » est écrit tel quel, jamais comme une formule.
    /// </summary>
    public void Ecrire(int ligne, int colonne, object? valeur, Format? format = null)
    {
        var c = Interne.Cell(ligne, colonne);
        switch (valeur)
        {
            case null: c.Clear(XLClearOptions.Contents); break;
            case string s: c.SetValue(s.Length > TexteMax ? s[..(TexteMax - 1)] + "…" : s); break;
            case int i: c.SetValue(i); break;
            case long l: c.SetValue(l); break;
            case double d: c.SetValue(d); break;
            case decimal m: c.SetValue(m); break;
            case bool b: c.SetValue(b); break;
            case DateOnly j: c.SetValue(j.ToDateTime(TimeOnly.MinValue)); break;
            case DateTime dt: c.SetValue(dt); break;
            default: c.SetValue(valeur.ToString()); break;
        }
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
    }

    /// <summary>Texte à plusieurs polices (texte courant et code) dans une cellule.</summary>
    public void EcrireRiche(int ligne, int colonne, TexteRiche texte, Format? format = null)
    {
        var c = Interne.Cell(ligne, colonne);
        c.Clear(XLClearOptions.Contents);
        var riche = c.CreateRichText();
        for (var i = 0; i < texte.Morceaux.Count; i++)
        {
            var (t, code) = texte.Morceaux[i];
            var morceau = riche.AddText(i < texte.Morceaux.Count - 1 ? t + "\n" : t);
            morceau.SetFontName(code ? StylesExcel.PoliceCode : StylesExcel.Police).SetFontSize(StylesExcel.TailleTexte);
        }
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
    }

    /// <summary>Formule Excel (sans le « = » initial), par exemple <c>COUNTIF('WEB'!E5:E9,"Validé")</c>.</summary>
    public void Formule(int ligne, int colonne, string formule, Format? format = null)
    {
        Interne.Cell(ligne, colonne).FormulaA1 = formule;
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
    }

    /// <summary>Applique une mise en forme à une plage (bordure, fond, saisie autorisée…).</summary>
    public void Formater(int ligne1, int colonne1, int ligne2, int colonne2, Format format) =>
        Appliquer(Interne.Range(ligne1, colonne1, ligne2, colonne2), format);

    /// <summary>Fusionne une plage (la valeur est celle de la cellule en haut à gauche).</summary>
    public void Fusionner(int ligne1, int colonne1, int ligne2, int colonne2)
    {
        if (ligne1 != ligne2 || colonne1 != colonne2) Interne.Range(ligne1, colonne1, ligne2, colonne2).Merge();
    }

    /// <summary>Note (commentaire) sur une cellule : aide sur un en-tête, par exemple.</summary>
    public void Note(int ligne, int colonne, string texte)
    {
        var note = Interne.Cell(ligne, colonne).CreateComment();
        note.Author = _classeur.Infos.Application;
        note.AddText(texte);
        note.Style.Size.SetWidth(Math.Clamp(texte.Length / 4.0, 18, 40));
        note.Style.Size.SetHeight(Math.Clamp(texte.Length / 6.0, 6, 30));
    }

    /// <summary>Filet appuyé, à la couleur primaire, au-dessus d'une plage : sépare deux groupes de lignes (fiches…).</summary>
    public void Separateur(int ligne, int colonne1, int colonne2)
    {
        var b = Interne.Range(ligne, colonne1, ligne, colonne2).Style.Border;
        b.TopBorder = XLBorderStyleValues.Medium;
        b.TopBorderColor = XLColor.FromHtml("#" + Styles.Palette.Primaire);
    }

    /// <summary>Couleur de l'onglet (« RRGGBB »).</summary>
    public void CouleurOnglet(string couleur) => Interne.SetTabColor(XLColor.FromHtml("#" + couleur));

    // ---------- Saisie ----------

    /// <summary>
    /// Liste déroulante sur une plage, alimentée par une liste du classeur (<see cref="ClasseurExcel.Liste"/>) ou par des
    /// valeurs écrites dans la règle. <paramref name="stricte"/> : seules ces valeurs (ou une case vide) sont acceptées ;
    /// sinon une autre valeur est permise après avertissement.
    /// </summary>
    public void ListeDeroulante(int ligne1, int colonne1, int ligne2, int colonne2, IXLRange source, bool stricte = true,
        string? titre = null, string? aide = null)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.IgnoreBlanks = true;
        v.InCellDropdown = true;
        v.List(source, true);
        Messages(v, stricte, titre, aide, stricte ? "Choisir une valeur dans la liste." : "Cette valeur n'est pas dans la liste proposée.");
    }

    /// <summary>Liste déroulante de quelques valeurs écrites dans la règle elle-même.</summary>
    public void ListeDeroulante(int ligne1, int colonne1, int ligne2, int colonne2, IReadOnlyList<string> valeurs, string? titre = null, string? aide = null)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.IgnoreBlanks = true;
        v.InCellDropdown = true;
        v.List("\"" + string.Join(",", valeurs) + "\"", true);
        Messages(v, true, titre, aide, "Choisir dans la liste : " + string.Join(" ou ", valeurs) + ".");
    }

    private static void Messages(IXLDataValidation v, bool stricte, string? titre, string? aide, string erreur)
    {
        v.ErrorStyle = stricte ? XLErrorStyle.Stop : XLErrorStyle.Warning;
        v.ErrorTitle = "Valeur non prévue";
        v.ErrorMessage = erreur;
        if (aide is { Length: > 0 })
        {
            v.ShowInputMessage = true;
            v.InputTitle = Tronquer(titre ?? "", 32);
            v.InputMessage = Tronquer(aide, 255);
        }
    }

    private static string Tronquer(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Date seulement (jour/mois/année) acceptée dans la plage, ou case vide.</summary>
    public void SaisieDate(int ligne1, int colonne1, int ligne2, int colonne2, string? aide = null)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.IgnoreBlanks = true;
        v.Date.GreaterThan(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        v.ErrorStyle = XLErrorStyle.Stop;
        v.ErrorTitle = "Date attendue";
        v.ErrorMessage = "Saisir une date, par exemple 18/10/2026.";
        if (aide is { Length: > 0 })
        {
            v.ShowInputMessage = true;
            v.InputTitle = "Date";
            v.InputMessage = Tronquer(aide, 255);
        }
    }

    /// <summary>Message d'aide affiché quand une case de la plage est sélectionnée (sans contrôle de la valeur).</summary>
    public void Aide(int ligne1, int colonne1, int ligne2, int colonne2, string titre, string aide)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.AllowedValues = XLAllowedValues.AnyValue;
        v.ShowInputMessage = true;
        v.InputTitle = Tronquer(titre, 32);
        v.InputMessage = Tronquer(aide, 255);
    }

    /// <summary>Mise en forme conditionnelle : chaque valeur citée prend les couleurs de sa tonalité.</summary>
    public void Tons(int ligne1, int colonne1, int ligne2, int colonne2, IReadOnlyDictionary<string, Ton> tons)
    {
        var plage = Interne.Range(ligne1, colonne1, ligne2, colonne2);
        var haut = Interne.Cell(ligne1, colonne1).Address.ToStringRelative();
        foreach (var (valeur, ton) in tons)
        {
            var (fond, texte) = Styles.Palette.Couleurs(ton);
            plage.AddConditionalFormat().WhenIsTrue($"{haut}=\"{valeur.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")
                .Fill.SetBackgroundColor(XLColor.FromHtml("#" + fond)).Font.SetFontColor(XLColor.FromHtml("#" + texte));
        }
    }

    /// <summary>Mise en forme conditionnelle par formule (relative à la cellule en haut à gauche de la plage).</summary>
    public void Signaler(int ligne1, int colonne1, int ligne2, int colonne2, string formule, Ton ton)
    {
        var (fond, texte) = Styles.Palette.Couleurs(ton);
        Interne.Range(ligne1, colonne1, ligne2, colonne2).AddConditionalFormat().WhenIsTrue(formule)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#" + fond)).Font.SetFontColor(XLColor.FromHtml("#" + texte));
    }

    /// <summary>Lien vers une cellule d'une feuille du même classeur.</summary>
    public void LienInterne(int ligne, int colonne, string texte, string feuille, int ligneCible, int colonneCible = 1, Format? format = null)
    {
        var c = Interne.Cell(ligne, colonne);
        c.SetValue(texte);
        var adresse = Interne.Workbook.Worksheet(feuille).Cell(ligneCible, colonneCible).Address.ToStringRelative();
        c.SetHyperlink(new XLHyperlink($"'{feuille.Replace("'", "''", StringComparison.Ordinal)}'!{adresse}"));
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
        c.Style.Font.FontColor = XLColor.FromHtml("#" + Styles.Palette.TexteLien);
        c.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    // ---------- Disposition ----------

    public void LargeurColonne(int colonne, double largeur) => Interne.Column(colonne).Width = largeur;

    public void HauteurLigne(int ligne, double hauteur) => Interne.Row(ligne).Height = hauteur;

    /// <summary>Hauteur actuelle d'une ligne, en points.</summary>
    public double Hauteur(int ligne) => Interne.Row(ligne).Height;

    /// <summary>Masque une colonne.</summary>
    public void MasquerColonne(int colonne) => Interne.Column(colonne).Hide();

    /// <summary>Fige les lignes jusqu'à <paramref name="lignes"/> comprise (en-têtes toujours visibles), et les premières colonnes.</summary>
    public void FigerLignes(int lignes, int colonnes = 0)
    {
        if (colonnes > 0) Interne.SheetView.Freeze(lignes, colonnes);
        else Interne.SheetView.FreezeRows(lignes);
    }

    /// <summary>Impression en portrait (fiches, consignes).</summary>
    public void Portrait() => Interne.PageSetup.PageOrientation = XLPageOrientation.Portrait;

    /// <summary>
    /// Protège la feuille (sans mot de passe) : seules les cellules à saisir restent modifiables. Largeurs, hauteurs et
    /// filtres restent utilisables.
    /// </summary>
    public void Proteger() =>
        Interne.Protect(XLProtectionAlgorithm.Algorithm.SimpleHash, XLSheetProtectionElements.FormatColumns | XLSheetProtectionElements.FormatRows
            | XLSheetProtectionElements.SelectEverything | XLSheetProtectionElements.AutoFilter);

    private void Appliquer(IXLRange r, Format f)
    {
        var s = r.Style;
        s.Alignment.WrapText = f.RetourALaLigne;
        s.Alignment.Vertical = f.CentreVerticalement ? XLAlignmentVerticalValues.Center : XLAlignmentVerticalValues.Top;
        if (f.Centre) s.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        else if (f.Droite) s.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        else if (f.Gauche) s.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        if (f.Gras) s.Font.Bold = true;
        if (f.Italique) s.Font.Italic = true;
        if (f.Taille is { } t) s.Font.FontSize = t;
        if (f.Couleur is { } c) s.Font.FontColor = XLColor.FromHtml("#" + c);
        if (f.ChasseFixe)
        {
            s.Font.FontName = StylesExcel.PoliceCode;
            s.Font.FontSize = StylesExcel.TailleTexte - 0.5;
        }
        var fond = f.Fond ?? (f.ASaisir ? Styles.Palette.FondASaisir : null);
        if (fond is not null) s.Fill.SetBackgroundColor(XLColor.FromHtml("#" + fond));
        if (f.Bordure)
        {
            var filet = XLColor.FromHtml("#" + Styles.Palette.Filet);
            s.Border.OutsideBorder = XLBorderStyleValues.Thin;
            s.Border.OutsideBorderColor = filet;
            s.Border.InsideBorder = XLBorderStyleValues.Thin;
            s.Border.InsideBorderColor = filet;
        }
        if (f.ASaisir) s.Protection.Locked = false;
        var nombre = f.NombreFormat ?? (f.DateHeure ? StylesExcel.FormatDateHeure : f.Date ? StylesExcel.FormatDate : null);
        if (nombre is not null) s.NumberFormat.Format = nombre;
    }
}
