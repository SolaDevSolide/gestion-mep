namespace GestionMep.Tableur;

/// <summary>
/// Onglet de consignes (« À lire ») d'un classeur à remplir : bloc de titre, puis des intertitres et des points courts sur
/// deux colonnes (repère, texte), une légende et des exemples de saisie bien distincts des vraies lignes.
/// </summary>
public sealed class FeuilleAide
{
    private readonly FeuilleExcel _f;
    private readonly double _largeurTexte;
    private int _ligne;

    public FeuilleAide(FeuilleExcel feuille, string titre, string? contexte, double largeurRepere = 22, double largeurTexte = 92)
    {
        _f = feuille;
        _largeurTexte = largeurTexte;
        _f.LargeurColonne(1, largeurRepere);
        _f.LargeurColonne(2, largeurTexte);
        _ligne = _f.BlocTitre(titre, contexte, 2, largeurRepere + largeurTexte);
    }

    /// <summary>Intertitre en bandeau.</summary>
    public void Section(string titre)
    {
        _ligne++;
        _f.Ecrire(_ligne, 1, titre, _f.Styles.Section);
        _f.Fusionner(_ligne, 1, _ligne, 2);
        _f.Formater(_ligne, 1, _ligne, 2, _f.Styles.Section);
        _f.HauteurLigne(_ligne, 20);
        _ligne++;
    }

    /// <summary>Un point : repère (numéro, mot-clé) à gauche, texte à droite.</summary>
    public void Point(string repere, string texte, Format? formatRepere = null)
    {
        _f.Ecrire(_ligne, 1, repere, formatRepere ?? _f.Styles.Libelle with { Centre = true });
        _f.Ecrire(_ligne, 2, texte, _f.Styles.Cellule);
        _f.HauteurLigne(_ligne, StylesExcel.HauteurTexte(texte, _largeurTexte - 2, 18));
        _ligne++;
    }

    /// <summary>Légende des cases à remplir (fond jaune).</summary>
    public void Legende() => Point("", "Case à remplir.", _f.Styles.ASaisir with { ASaisir = false });

    /// <summary>Ligne d'exemple : libellé de colonne à gauche, valeur d'exemple (italique grise) à droite.</summary>
    public void Exemple(string colonne, object valeur)
    {
        _f.Ecrire(_ligne, 1, colonne, _f.Styles.Libelle);
        _f.Ecrire(_ligne, 2, valeur, _f.Styles.Exemple with
        {
            NombreFormat = valeur is DateOnly or DateTime ? StylesExcel.FormatDate : null,
            Gauche = true,
        });
        _f.HauteurLigne(_ligne, valeur is string s ? StylesExcel.HauteurTexte(s, _largeurTexte - 2, 18) : 18);
        _ligne++;
    }

    /// <summary>Termine l'onglet : impression en portrait, protection (rien n'y est à saisir).</summary>
    public void Terminer()
    {
        _f.Portrait();
        _f.Proteger();
    }
}
