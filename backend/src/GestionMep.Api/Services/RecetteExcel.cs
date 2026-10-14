using System.Globalization;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Recette d'une MEP en classeur Excel, pour la faire valider par les métiers sans compte dans l'application :
/// un onglet « Sommaire » (une ligne par fiche, avec lien et compteurs), puis un onglet par système avec toutes ses fiches à
/// la suite, et un onglet masqué « Contrôle » (MEP, version de la recette, emplacement de chaque étape). Seules les cases
/// jaunes (résultat constaté, validation, validé par, date) sont modifiables ; le réimport (<see cref="Lire"/>) retrouve
/// chaque étape grâce à l'onglet de contrôle.
/// </summary>
public static class RecetteExcel
{
    public const string FeuilleSommaire = "Sommaire";
    public const string FeuilleControle = "Contrôle";
    public const string SansSysteme = "Sans système";
    public const string TypeContenu = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    /// <summary>Format de l'onglet de contrôle : change si la disposition du classeur change.</summary>
    public const string Format = "recette-1";

    private const string CleMep = "mep";
    private const string CleVersion = "version recette";
    private const string CleFormat = "format";
    private const string PrefixeEtape = "étape ";

    // Colonnes des onglets système.
    private const int ColEtape = 1, ColDeroule = 2, ColAttendu = 3, ColConstate = 4, ColValidation = 5, ColPar = 6, ColDate = 7;
    private const int LigneEnTetes = 4;
    private static readonly double[] Largeurs = [7, 60, 34, 34, 12, 18, 12];

    /// <summary>Fond clair des systèmes, dans l'ordre des couleurs de l'application (<c>couleursSysteme.ts</c>, thème clair).</summary>
    private static readonly string[] FondsSystemes = ["EFE7F8", "E0F3F1", "FBEEDD", "E6EDFB", "FBE6EF", "EEF4DD", "FBE8E5", "ECEEF2"];
    private static readonly Dictionary<string, int> SystemesConnus = new() { ["BDD"] = 0, ["API"] = 1, ["WEB"] = 2, ["LDAP"] = 3, ["MDM"] = 4, ["BATCH"] = 5 };

    /// <summary>Indice de couleur d'un système, calculé comme dans le front.</summary>
    public static int IndiceCouleur(string nom)
    {
        var cle = nom.Trim().ToUpperInvariant();
        if (SystemesConnus.TryGetValue(cle, out var i)) return i;
        uint h = 0;
        foreach (var c in cle) h = unchecked(h * 31 + c);
        return (int)(h % 8);
    }

    /// <summary>Nom de la MEP dans les fichiers : outil et version cible.</summary>
    public static string NomMep(Mep mep)
    {
        var nom = $"{mep.Outil} {mep.VersionCible}".Trim();
        return nom.Length == 0 ? "MEP" : nom;
    }

    /// <summary>Nom de fichier proposé au téléchargement.</summary>
    public static string NomFichier(Mep mep)
    {
        var nom = new string(NomMep(mep).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray()).Trim('-');
        return $"recette-{(nom.Length == 0 ? "mep" : nom)}.xlsx";
    }

    private static DateTimeOffset Locale(Mep mep, DateTimeOffset instant)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(mep.FuseauHoraire));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return instant;
        }
    }

    private sealed record Groupe(string Onglet, Systeme? Systeme, List<FicheRecette> Fiches);

    /// <summary>Fiches regroupées par système, dans l'ordre des systèmes de la MEP ; les fiches sans système à la fin.</summary>
    private static List<Groupe> Groupes(Mep mep)
    {
        var fiches = mep.FichesRecette.OrderBy(f => f.Ordre).ToList();
        var groupes = mep.Systemes.Select(s => new Groupe(s.Nom, s, fiches.Where(f => f.SystemeId == s.Id).ToList())).Where(g => g.Fiches.Count > 0).ToList();
        var orphelines = fiches.Where(f => f.SystemeId is null || mep.Systemes.All(s => s.Id != f.SystemeId)).ToList();
        if (orphelines.Count > 0) groupes.Add(new Groupe(SansSysteme, null, orphelines));
        return groupes;
    }

    /// <summary>Classeur de la recette ; les résultats déjà saisis sont pré-remplis.</summary>
    public static byte[] Exporter(Mep mep, IReadOnlyDictionary<Guid, string> noms, DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var nomMep = NomMep(mep);
        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId)?.Nom;
        var exportee = Locale(mep, maintenant).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var sommaire = classeur.AjouterFeuille(FeuilleSommaire);
        sommaire.Ecrire(1, 1, $"Recette · {nomMep}", Charte.Titre);
        sommaire.Ecrire(2, 1, string.Join(" · ", new[] { mep.Objet, $"exportée le {exportee}", pilote is null ? "à renvoyer complétée au pilote" : $"à renvoyer complétée au pilote ({pilote})" }
            .Where(s => !string.IsNullOrWhiteSpace(s))), Charte.SousTitre);
        sommaire.Fusionner(1, 1, 1, 6);
        sommaire.HauteurLigne(1, 24);
        sommaire.Fusionner(2, 1, 2, 6);
        sommaire.Ecrire(4, 1, "Mode d'emploi", new Tableur.Format { Gras = true });
        sommaire.Ecrire(5, 1, "Un onglet par système, ses fiches à la suite. Remplissez uniquement les cases jaunes : résultat constaté, "
            + $"validation ({Regles.LibelleValide} ou {Regles.LibelleRefuse}), votre nom et la date. Ne renommez pas les onglets.");
        sommaire.Fusionner(5, 1, 5, 6);
        sommaire.HauteurLigne(5, 34);
        string[] entetes = ["Système", "Fiche", "Onglet", "Étapes", "Validées", "Refusées"];
        for (var i = 0; i < entetes.Length; i++) sommaire.Ecrire(7, i + 1, entetes[i], Charte.EnTete);
        var ligneSommaire = 8;
        if (mep.FichesRecette.Count == 0)
        {
            sommaire.Ecrire(ligneSommaire, 1, "Aucune fiche de recette.", Charte.SousTitre);
            sommaire.Fusionner(ligneSommaire, 1, ligneSommaire, 6);
        }

        var controle = new List<KeyValuePair<string, string>>
        {
            new(CleMep, mep.Id.ToString()),
            new(CleVersion, Regles.VersionRecette(mep)),
            new(CleFormat, Format),
        };
        var numeroEtape = 0;

        foreach (var g in Groupes(mep))
        {
            var f = classeur.AjouterFeuille(g.Onglet);
            var fond = g.Systeme is null ? FondsSystemes[7] : FondsSystemes[IndiceCouleur(g.Systeme.Nom)];
            f.Ecrire(1, 1, g.Systeme is null ? "Recette · fiches sans système" : $"Recette {g.Systeme.Nom}", Charte.Titre with { Taille = 14 });
            f.Ecrire(2, 1, string.Join(" · ", new[] { nomMep, g.Systeme?.Cible ?? "" }.Where(s => !string.IsNullOrWhiteSpace(s))), Charte.SousTitre);
            f.Fusionner(1, 1, 1, ColDate);
            f.HauteurLigne(1, 22);
            f.Fusionner(2, 1, 2, ColDate);
            string[] colonnes = ["Étape", "Déroulé du test", "Résultat attendu", "Résultat constaté", "Validation", "Validé par", "Date"];
            for (var i = 0; i < colonnes.Length; i++) f.Ecrire(LigneEnTetes, i + 1, colonnes[i], Charte.EnTete);
            for (var i = 0; i < Largeurs.Length; i++) f.LargeurColonne(i + 1, Largeurs[i]);

            var ligne = LigneEnTetes + 1;
            foreach (var fiche in g.Fiches)
            {
                var ligneFiche = ligne;
                var titre = string.IsNullOrWhiteSpace(fiche.Titre) ? $"Fiche {fiche.Ordre}" : fiche.Titre;
                f.Ecrire(ligne, 1, $"Fiche : {titre}", new Tableur.Format { Gras = true, Taille = 12, Fond = fond, Bordure = true });
                f.Formater(ligne, 1, ligne, ColDate, new Tableur.Format { Fond = fond, Bordure = true });
                f.Fusionner(ligne, 1, ligne, ColDate);
                f.HauteurLigne(ligne, 22);
                ligne++;
                var etapes = fiche.Etapes.OrderBy(t => t.Ordre).ToList();
                if (etapes.Count == 0)
                {
                    f.Ecrire(ligne, ColDeroule, "Aucune étape de test.", Charte.SousTitre);
                    ligne++;
                }
                for (var n = 0; n < etapes.Count; n++)
                {
                    var t = etapes[n];
                    var debut = ligne;
                    var blocs = BlocsMarkdown.Decouper(t.Instructions);
                    if (blocs.Count == 0) blocs.Add(new BlocsMarkdown.Bloc(false, "(déroulé à compléter)"));
                    foreach (var b in blocs)
                    {
                        var texte = b.EstCode ? b.Contenu : BlocsMarkdown.TexteSimple(b.Contenu);
                        f.Ecrire(ligne, ColDeroule, texte, b.EstCode ? Charte.Code : Charte.Cellule);
                        f.HauteurLigne(ligne, Charte.HauteurTexte(texte, Largeurs[ColDeroule - 1] * (b.EstCode ? 0.85 : 1)));
                        ligne++;
                    }
                    var fin = ligne - 1;
                    var attendu = BlocsMarkdown.ToutEnTexte(t.ResultatAttendu);
                    // Une étape d'un seul bloc doit laisser la place au résultat attendu.
                    var place = Enumerable.Range(debut, fin - debut + 1).Sum(f.Hauteur);
                    var besoin = Math.Max(32, Charte.HauteurTexte(attendu, Largeurs[ColAttendu - 1]));
                    if (place < besoin) f.HauteurLigne(fin, f.Hauteur(fin) + besoin - place);

                    var fait = t.Resultat is not null;
                    var par = t.ValideePar.Length > 0 ? t.ValideePar : t.ParId is { } u && noms.TryGetValue(u, out var nom) ? nom : "";
                    DateOnly? date = t.ValideeLe ?? (t.Le is { } le ? DateOnly.FromDateTime(Locale(mep, le).DateTime) : null);
                    f.Ecrire(debut, ColEtape, n + 1, Charte.Cellule with { Centre = true });
                    f.Ecrire(debut, ColAttendu, attendu, Charte.Cellule);
                    f.Ecrire(debut, ColConstate, fait ? t.Observation : null, Charte.ASaisir);
                    f.Ecrire(debut, ColValidation, t.Resultat switch
                    {
                        ResultatControle.Conforme => Regles.LibelleValide,
                        ResultatControle.Echec => Regles.LibelleRefuse,
                        _ => null,
                    }, Charte.ASaisir);
                    f.Ecrire(debut, ColPar, fait ? par : null, Charte.ASaisir);
                    f.Ecrire(debut, ColDate, fait ? date : null, Charte.ASaisir with { Date = true });
                    foreach (var col in new[] { ColEtape, ColAttendu })
                        f.Formater(debut, col, fin, col, Charte.Cellule);
                    foreach (var col in new[] { ColConstate, ColValidation, ColPar, ColDate })
                        f.Formater(debut, col, fin, col, Charte.ASaisir with { Date = col == ColDate });
                    foreach (var col in new[] { ColEtape, ColAttendu, ColConstate, ColValidation, ColPar, ColDate })
                        f.Fusionner(debut, col, fin, col);
                    f.ListeDeroulante(debut, ColValidation, debut, ColValidation, [Regles.LibelleValide, Regles.LibelleRefuse]);
                    f.SaisieDate(debut, ColDate, debut, ColDate);

                    numeroEtape++;
                    controle.Add(new($"{PrefixeEtape}{numeroEtape}", string.Join(";", fiche.Id, t.Id, debut, n + 1, f.Nom)));
                }
                var derniere = ligne - 1;

                // Sommaire : une ligne par fiche, lien vers son titre et compteurs calculés par Excel.
                sommaire.Ecrire(ligneSommaire, 1, g.Systeme?.Nom ?? SansSysteme, Charte.Cellule);
                sommaire.Ecrire(ligneSommaire, 2, titre, Charte.Cellule);
                sommaire.LienInterne(ligneSommaire, 3, $"{f.Nom} (ligne {ligneFiche})", f.Nom, ligneFiche);
                sommaire.Formater(ligneSommaire, 3, ligneSommaire, 3, Charte.Cellule);
                sommaire.Ecrire(ligneSommaire, 4, etapes.Count, Charte.Cellule);
                var plage = $"'{f.Nom.Replace("'", "''")}'!E{ligneFiche}:E{derniere}";
                sommaire.Formule(ligneSommaire, 5, $"COUNTIF({plage},\"{Regles.LibelleValide}\")", Charte.Cellule);
                sommaire.Formule(ligneSommaire, 6, $"COUNTIF({plage},\"{Regles.LibelleRefuse}\")", Charte.Cellule);
                ligneSommaire++;
                ligne++;
            }
            f.FigerLignes(LigneEnTetes);
            f.Proteger();
        }

        double[] largeursSommaire = [14, 34, 22, 9, 10, 10];
        for (var i = 0; i < largeursSommaire.Length; i++) sommaire.LargeurColonne(i + 1, largeursSommaire[i]);
        sommaire.Proteger();
        classeur.AjouterFeuilleControle(FeuilleControle, controle).Proteger();
        classeur.VerrouillerStructure();
        return classeur.Enregistrer();
    }

    /// <summary>Contenu d'un fichier réimporté : MEP et version de la recette lues dans l'onglet de contrôle, lignes d'étapes, anomalies de forme.</summary>
    public record Lecture(Guid? MepId, string Version, List<Regles.LigneValidation> Lignes, List<string> Erreurs);

    /// <summary>
    /// Lit un fichier exporté par <see cref="Exporter"/> : onglet de contrôle présent et du bon format, onglets non renommés,
    /// lignes d'étapes à leur place. <see cref="ClasseurIllisibleException"/> si ce n'est pas un classeur .xlsx.
    /// </summary>
    public static Lecture Lire(byte[] contenu, Mep mep)
    {
        using var lecteur = LecteurClasseur.Ouvrir(contenu);
        var controle = lecteur.LireControle(FeuilleControle);
        if (controle is null || !controle.TryGetValue(CleFormat, out var format))
            return new(null, "", [], ["Ce fichier n'est pas un export de recette de l'application (onglet « Contrôle » absent) : exportez la recette puis remplissez ce fichier."]);
        if (format != Format)
            return new(null, "", [], ["Ce fichier vient d'une autre version de l'application : exportez de nouveau la recette."]);
        Guid? mepId = controle.TryGetValue(CleMep, out var m) && Guid.TryParse(m, out var id) ? id : null;
        var version = controle.GetValueOrDefault(CleVersion) ?? "";

        var erreurs = new List<string>();
        var lignes = new List<Regles.LigneValidation>();
        var manquantes = new HashSet<string>();
        foreach (var (cle, valeur) in controle.Where(kv => kv.Key.StartsWith(PrefixeEtape, StringComparison.Ordinal)))
        {
            var p = valeur.Split(';', 5);
            if (p.Length != 5 || !Guid.TryParse(p[0], out var ficheId) || !Guid.TryParse(p[1], out var etapeId)
                || !int.TryParse(p[2], CultureInfo.InvariantCulture, out var ligne) || !int.TryParse(p[3], CultureInfo.InvariantCulture, out var numero))
            {
                erreurs.Add("L'onglet « Contrôle » a été modifié : exportez de nouveau la recette.");
                break;
            }
            var onglet = p[4];
            if (lecteur.Feuille(onglet) is not { } f)
            {
                if (manquantes.Add(onglet))
                    erreurs.Add($"Onglet « {onglet} » introuvable : les onglets du fichier ne doivent être ni renommés ni supprimés.");
                continue;
            }
            var titre = mep.FichesRecette.FirstOrDefault(x => x.Id == ficheId)?.Titre;
            var lieu = $"Onglet {onglet}, ligne {ligne}" + (string.IsNullOrWhiteSpace(titre) ? $" (étape {numero})" : $" (« {titre} », étape {numero})");
            if (f.Texte(ligne, ColEtape) != numero.ToString(CultureInfo.InvariantCulture))
            {
                erreurs.Add($"{lieu} : l'étape n'est plus à sa place (lignes ajoutées ou supprimées) ; repartez d'un nouvel export.");
                continue;
            }
            var (_, dateLisible, date) = f.Date(ligne, ColDate);
            lignes.Add(new Regles.LigneValidation(ficheId, etapeId, lieu, f.Texte(ligne, ColValidation), f.Texte(ligne, ColConstate),
                f.Texte(ligne, ColPar), date, dateLisible));
        }
        return new(mepId, version, lignes, erreurs);
    }
}
