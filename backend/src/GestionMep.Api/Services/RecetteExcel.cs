using System.Globalization;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Recette d'une MEP en classeur Excel, pour la faire valider par les métiers sans compte dans l'application : un onglet
/// « À lire » (mode d'emploi, légende, exemple), un « Sommaire » (avancement de chaque fiche, calculé par Excel), puis un
/// onglet par système : un tableau d'une ligne par étape de test, filtrable, dont seules les cases jaunes (résultat
/// constaté, validation, validé par, date) sont modifiables. Une colonne masquée « Réf. » relie chaque ligne à son étape :
/// trier ou filtrer ne gêne pas le réimport (<see cref="Lire"/>). Un onglet masqué « Contrôle » porte la MEP et la version
/// de la recette ; les listes déroulantes sont alimentées par l'onglet masqué « Listes ».
/// Les fichiers de la version précédente (format « recette-1 », étapes repérées par leur ligne) se réimportent toujours.
/// </summary>
public static class RecetteExcel
{
    public const string FeuilleALire = "À lire";
    public const string FeuilleSommaire = "Sommaire";
    public const string FeuilleControle = "Contrôle";
    public const string SansSysteme = "Sans système";
    public const string TypeContenu = ExcelCommun.TypeContenu;
    /// <summary>Format de l'onglet de contrôle : change si la disposition du classeur change.</summary>
    public const string Format = "recette-2";
    /// <summary>Format des fichiers exportés par la version précédente, toujours relus.</summary>
    public const string FormatPrecedent = "recette-1";

    private const string CleMep = "mep";
    private const string CleVersion = "version recette";
    private const string CleFormat = "format";
    private const string PrefixeOnglet = "onglet ";
    private const string PrefixeEtape = "étape ";

    // En-têtes des onglets système (format recette-2).
    public const string ColFiche = "Fiche", ColNumero = "N°", ColDeroule = "Déroulé du test", ColAttendu = "Résultat attendu",
        ColConstate = "Résultat constaté", ColValidation = "Validation", ColPar = "Validé par", ColDate = "Date", ColRef = "Réf.";
    private const int LigneEnTetes = 4;

    /// <summary>Couleur pleine des systèmes, dans l'ordre de l'application (<c>couleursSysteme.ts</c>, thème clair) : couleur d'onglet.</summary>
    private static readonly string[] CouleursSystemes = ["6B3FA0", "0F7D78", "A85D00", "2B59C3", "B0306B", "5B7A12", "A1342A", "4A5568"];
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
    public static string NomMep(Mep mep) => ExcelCommun.NomMep(mep.Outil, mep.VersionCible);

    /// <summary>Nom de fichier proposé au téléchargement : « recette-Outil-1.2.xlsx ».</summary>
    public static string NomFichier(Mep mep) => ExcelCommun.NomFichier("recette", NomMep(mep));

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

    private static string TitreFiche(FicheRecette f) => string.IsNullOrWhiteSpace(f.Titre) ? $"Fiche {f.Ordre}" : f.Titre;

    /// <summary>Texte d'un critère NB.SI.ENS : les jokers d'Excel (« * », « ? », « ~ ») sont neutralisés, les guillemets doublés.</summary>
    private static string Critere(string texte) =>
        texte.Replace("~", "~~", StringComparison.Ordinal).Replace("*", "~*", StringComparison.Ordinal).Replace("?", "~?", StringComparison.Ordinal)
            .Replace("\"", "\"\"", StringComparison.Ordinal);

    private static readonly ColonneTableau[] Colonnes =
    [
        new(ColFiche, 22) { Note = "Fiche de recette : ses étapes se suivent. Les filtres permettent de n'en afficher qu'une." },
        new(ColNumero, 5, TypeColonne.Entier),
        new(ColDeroule, 58),
        new(ColAttendu, 34),
        new(ColConstate, 34) { ASaisir = true, Note = "À remplir : ce que vous avez observé. Obligatoire pour un refus." },
        new(ColValidation, 12, TypeColonne.Court) { ASaisir = true, Tons = ExcelCommun.TonsValidation,
            Note = $"À remplir : {Regles.LibelleConforme} ou {Regles.LibelleNonConforme}, dans la liste. Laisser vide si l'étape n'a pas été testée." },
        new(ColPar, 18) { ASaisir = true, Note = "À remplir : nom de la personne qui a testé." },
        new(ColDate, 12, TypeColonne.Date) { ASaisir = true, Note = "À remplir : date du test (jour/mois/année)." },
        new(ColRef, 20, TypeColonne.Code) { Masquee = true },
    ];

    /// <summary>Classeur de la recette ; les résultats déjà saisis sont pré-remplis. <paramref name="palette"/> null : couleurs de l'application.</summary>
    public static byte[] Exporter(Mep mep, IReadOnlyDictionary<Guid, string> noms, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        var nomMep = NomMep(mep);
        using var classeur = ExcelCommun.Nouveau($"Recette · {nomMep}", maintenant, mep.FuseauHoraire, palette, mep.Objet.Length > 0 ? mep.Objet : null);
        var styles = classeur.Styles;
        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId)?.Nom;
        var aLire = classeur.AjouterFeuille(FeuilleALire);
        var sommaire = classeur.AjouterFeuille(FeuilleSommaire);

        var controle = new List<KeyValuePair<string, string>>
        {
            new(CleMep, mep.Id.ToString()),
            new(CleVersion, Regles.VersionRecette(mep)),
            new(CleFormat, Format),
        };
        var lignesSommaire = new List<object?[]>();
        var liens = new List<(string Onglet, int Ligne)>();
        var tableaux = new List<TableauEcrit>();
        var nomsSaisis = new List<string>();

        foreach (var g in Groupes(mep))
        {
            var f = classeur.AjouterFeuille(g.Onglet);
            f.CouleurOnglet(CouleursSystemes[g.Systeme is null ? 7 : IndiceCouleur(g.Systeme.Nom)]);
            controle.Add(new($"{PrefixeOnglet}{tableaux.Count + 1}", f.Nom));

            var lignes = new List<object?[]>();
            var plages = new List<(FicheRecette Fiche, int Premiere, int Derniere)>();
            foreach (var fiche in g.Fiches)
            {
                var etapes = fiche.Etapes.OrderBy(t => t.Ordre).ToList();
                var premiere = LigneEnTetes + 1 + lignes.Count;
                for (var n = 0; n < etapes.Count; n++)
                {
                    var t = etapes[n];
                    var blocs = BlocsMarkdown.Decouper(t.Instructions);
                    object deroule = blocs.Count == 0 ? "(déroulé à compléter)"
                        : new TexteRiche(blocs.Select(b => (b.EstCode ? b.Contenu : BlocsMarkdown.TexteSimple(b.Contenu), b.EstCode)).ToList());
                    var fait = t.Resultat is not null;
                    var par = t.ValideePar.Length > 0 ? t.ValideePar : t.ParId is { } u && noms.TryGetValue(u, out var nom) ? nom : "";
                    if (fait && par.Length > 0) nomsSaisis.Add(par);
                    DateOnly? date = t.ValideeLe ?? (t.Le is { } le ? Fuseaux.Jour(le, mep.FuseauHoraire) : null);
                    lignes.Add(
                    [
                        TitreFiche(fiche), n + 1, deroule, BlocsMarkdown.ToutEnTexte(t.ResultatAttendu),
                        fait ? t.Observation : null,
                        t.Resultat switch
                        {
                            ResultatControle.Conforme => Regles.LibelleConforme,
                            ResultatControle.Echec => Regles.LibelleNonConforme,
                            _ => null,
                        },
                        fait ? par : null, fait ? date : null, $"{fiche.Id};{t.Id}",
                    ]);
                }
                plages.Add((fiche, premiere, LigneEnTetes + lignes.Count));
            }

            var tableau = f.Tableau(LigneEnTetes, $"Recette {g.Onglet}", Colonnes, lignes, "Aucune étape de test.");
            tableaux.Add(tableau);
            // Filet appuyé entre deux fiches.
            foreach (var (_, premiere, derniere) in plages.Skip(1).Where(p => p.Derniere >= p.Premiere)) f.Separateur(premiere, 1, Colonnes.Length - 1);
            var cible = g.Systeme?.Cible ?? "";
            f.BlocTitre(g.Systeme is null ? $"Recette · fiches sans système · {nomMep}" : $"Recette {g.Systeme.Nom} · {nomMep}",
                string.Join(" · ", new[] { cible, "Remplir les cases jaunes, une ligne par étape de test (mode d'emploi : onglet « À lire »)." }
                    .Where(s => s.Length > 0)), 6);
            f.FigerLignes(LigneEnTetes, 2);
            f.Proteger();

            foreach (var (fiche, premiere, derniere) in plages)
            {
                var n = derniere - premiere + 1;
                string Compte(string valeur) => n == 0 ? "0"
                    : $"COUNTIFS({tableau.Adresse(ColFiche)},\"{Critere(TitreFiche(fiche))}\",{tableau.Adresse(ColValidation)},\"{valeur}\")";
                var validees = Compte(Regles.LibelleConforme);
                var refusees = Compte(Regles.LibelleNonConforme);
                var cibleLien = n == 0 ? LigneEnTetes : premiere;
                lignesSommaire.Add(
                [
                    g.Systeme?.Nom ?? SansSysteme, TitreFiche(fiche), n, new FormuleExcel(validees), new FormuleExcel(refusees),
                    new FormuleExcel($"{n}-{validees}-{refusees}"), $"{f.Nom}, ligne {cibleLien}",
                ]);
                liens.Add((f.Nom, cibleLien));
            }
        }

        // Listes déroulantes (onglet masqué « Listes »), posées une fois tous les onglets créés.
        var listeValidation = classeur.Liste("Validation", [Regles.LibelleConforme, Regles.LibelleNonConforme]);
        var listePersonnes = classeur.Liste("Personnes", mep.Participants.Select(p => p.Nom).Concat(nomsSaisis).Order(StringComparer.CurrentCulture));
        foreach (var t in tableaux.Where(t => t.Nom is not null))
        {
            var f = t.Feuille;
            var (v, p, d, c) = (t.Colonne(ColValidation), t.Colonne(ColPar), t.Colonne(ColDate), t.Colonne(ColConstate));
            if (listeValidation is not null)
                f.ListeDeroulante(t.PremiereLigne, v, t.DerniereLigne, v, listeValidation, stricte: true, "Validation",
                    $"{Regles.LibelleConforme} ou {Regles.LibelleNonConforme}. Laisser vide si l'étape n'a pas été testée.");
            if (listePersonnes is not null)
                f.ListeDeroulante(t.PremiereLigne, p, t.DerniereLigne, p, listePersonnes, stricte: false, "Validé par",
                    "Votre nom : le choisir dans la liste ou le saisir.");
            else f.Aide(t.PremiereLigne, p, t.DerniereLigne, p, "Validé par", "Votre nom.");
            f.SaisieDate(t.PremiereLigne, d, t.DerniereLigne, d, "Date du test, par exemple 18/10/2026.");
            f.Aide(t.PremiereLigne, c, t.DerniereLigne, c, "Résultat constaté", "Ce que vous avez observé. Obligatoire pour un refus.");
        }

        // Sommaire : une ligne par fiche, compteurs calculés par Excel, lien vers la fiche.
        var ts = sommaire.Tableau(LigneEnTetes, "Sommaire",
            [new("Système", 14), new("Fiche", 36), new("Étapes", 9, TypeColonne.Entier), new("Conformes", 10, TypeColonne.Entier),
                new("Non conformes", 12, TypeColonne.Entier), new("Restantes", 10, TypeColonne.Entier) { Note = "Étapes sans validation." },
                new("Aller à", 20) { SurUneLigne = true }],
            lignesSommaire, "Aucune fiche de recette.");
        for (var i = 0; i < liens.Count; i++)
            sommaire.LienInterne(ts.PremiereLigne + i, ts.Colonne("Aller à"), $"{liens[i].Onglet}, ligne {liens[i].Ligne}", liens[i].Onglet, liens[i].Ligne,
                format: styles.Cellule with { RetourALaLigne = false });
        if (liens.Count > 0)
        {
            var (refus, restantes, etapes) = (ts.Colonne("Non conformes"), ts.Colonne("Restantes"), ts.Colonne("Étapes"));
            sommaire.Signaler(ts.PremiereLigne, refus, ts.DerniereLigne, refus, $"{Cellule(ts.PremiereLigne, refus)}>0", Ton.Echec);
            sommaire.Signaler(ts.PremiereLigne, restantes, ts.DerniereLigne, restantes,
                $"AND({Cellule(ts.PremiereLigne, restantes)}=0,{Cellule(ts.PremiereLigne, etapes)}>0)", Ton.Succes);
        }
        sommaire.BlocTitre($"Sommaire de la recette · {nomMep}", "Avancement de chaque fiche, mis à jour par Excel au fil de la saisie.", 6);
        sommaire.Proteger();

        EcrireALire(aLire, mep, nomMep, pilote);

        classeur.AjouterFeuilleControle(FeuilleControle, controle);
        classeur.VerrouillerStructure();
        return classeur.Enregistrer();
    }

    private static string Cellule(int ligne, int colonne) => $"{ClosedXML.Excel.XLHelper.GetColumnLetterFromNumber(colonne)}{ligne}";

    /// <summary>Onglet « À lire » : mode d'emploi en quelques lignes, précautions, légende et exemple de ligne remplie.</summary>
    private static void EcrireALire(FeuilleExcel f, Mep mep, string nomMep, string? pilote)
    {
        var renvoi = pilote is null ? "au pilote de la MEP" : $"au pilote de la MEP ({pilote})";
        var aide = new FeuilleAide(f, $"Recette · {nomMep}",
            string.Join(" · ", new[] { mep.Objet, $"à renvoyer complétée {renvoi}" }.Where(s => s.Length > 0)));

        aide.Section("Comment remplir ce classeur");
        aide.Point("1", "Ouvrez l'onglet de chaque système : une ligne par étape de test, les fiches à la suite. L'onglet « Sommaire » montre l'avancement.");
        aide.Point("2", "Pour chaque étape, suivez le déroulé du test et comparez avec le résultat attendu.");
        aide.Point("3", $"Remplissez les cases jaunes : résultat constaté, validation ({Regles.LibelleConforme} ou {Regles.LibelleNonConforme}, dans la liste), "
            + "votre nom et la date du test.");
        aide.Point("4", "Un refus doit décrire ce qui a été constaté. Une étape laissée sans validation n'est pas prise en compte.");
        aide.Point("5", $"Enregistrez le fichier (format .xlsx) et renvoyez-le {renvoi}, qui l'importe dans l'application.");

        aide.Section("À savoir");
        aide.Point("Filtres", "Les boutons des en-têtes filtrent les lignes (une fiche, les étapes restantes…) : filtrer ne gêne pas l'import.");
        aide.Point("Protection", "Seules les cases jaunes sont modifiables (protection sans mot de passe, contre les fausses manipulations). "
            + "Les onglets ne se renomment pas et ne se suppriment pas.");
        aide.Point("Colonne masquée", "La colonne masquée « Réf. » relie chaque ligne à l'application : ne pas la modifier.");
        aide.Point("Recette modifiée", "Si la recette change dans l'application après cet export, le pilote vous enverra un nouveau fichier.");

        aide.Section("Légende");
        aide.Legende();
        aide.Point(Regles.LibelleConforme, "Étape conforme au résultat attendu.", f.Styles.Etat(Ton.Succes));
        aide.Point(Regles.LibelleNonConforme, "Étape non conforme : décrire le résultat constaté.", f.Styles.Etat(Ton.Echec));

        aide.Section("Exemple de ligne remplie (à ne pas recopier)");
        aide.Exemple(ColConstate, "Connexion réussie, tableau de bord affiché en 2 secondes.");
        aide.Exemple(ColValidation, Regles.LibelleConforme);
        aide.Exemple(ColPar, "Marie Martin");
        aide.Exemple(ColDate, new DateOnly(2026, 10, 18));
        aide.Terminer();
    }

    /// <summary>Contenu d'un fichier réimporté : MEP et version de la recette lues dans l'onglet de contrôle, lignes d'étapes, anomalies de forme.</summary>
    public record Lecture(Guid? MepId, string Version, List<Regles.LigneValidation> Lignes, List<string> Erreurs);

    /// <summary>En-têtes reconnus (forme comparable, voir <see cref="LecteurClasseur.Cle"/>) et colonne qu'ils désignent.</summary>
    private static readonly Dictionary<string, string> EnTetes = new()
    {
        ["ref"] = ColRef, ["reference"] = ColRef,
        ["fiche"] = ColFiche,
        ["n"] = ColNumero, ["no"] = ColNumero, ["numero"] = ColNumero, ["etape"] = ColNumero,
        ["resultat constate"] = ColConstate, ["constate"] = ColConstate, ["observation"] = ColConstate,
        ["validation"] = ColValidation, ["decision"] = ColValidation,
        ["valide par"] = ColPar, ["validee par"] = ColPar, ["par"] = ColPar, ["testeur"] = ColPar,
        ["date"] = ColDate, ["le"] = ColDate, ["date du test"] = ColDate,
    };

    /// <summary>
    /// Lit un fichier exporté par <see cref="Exporter"/> : onglet de contrôle présent et d'un format connu, puis chaque ligne
    /// d'étape. Format actuel : lignes retrouvées par leur référence (colonne masquée « Réf. »), quel que soit leur ordre, les
    /// colonnes par leur en-tête ; les lignes sans référence sont ignorées. Format précédent : étapes à la ligne notée dans
    /// le contrôle. <see cref="ClasseurIllisibleException"/> si ce n'est pas un classeur .xlsx.
    /// </summary>
    public static Lecture Lire(byte[] contenu, Mep mep)
    {
        using var lecteur = LecteurClasseur.Ouvrir(contenu);
        var controle = lecteur.LireControle(FeuilleControle);
        if (controle is null || !controle.TryGetValue(CleFormat, out var format))
            return new(null, "", [], ["Ce fichier n'est pas un export de recette de l'application (onglet « Contrôle » absent) : exportez la recette puis remplissez ce fichier."]);
        if (format != Format && format != FormatPrecedent)
            return new(null, "", [], ["Ce fichier vient d'une autre version de l'application : exportez de nouveau la recette."]);
        Guid? mepId = controle.TryGetValue(CleMep, out var m) && Guid.TryParse(m, out var id) ? id : null;
        var version = controle.GetValueOrDefault(CleVersion) ?? "";
        return format == Format ? LireFormatActuel(lecteur, mep, mepId, version) : LireFormatPrecedent(lecteur, controle, mep, mepId, version);
    }

    private static Lecture LireFormatActuel(LecteurClasseur lecteur, Mep mep, Guid? mepId, string version)
    {
        var erreurs = new List<string>();
        var lignes = new List<Regles.LigneValidation>();
        var reconnus = 0;
        foreach (var f in lecteur.Toutes)
        {
            if (f.Nom is FeuilleALire or FeuilleSommaire or FeuilleControle or ClasseurExcel.FeuilleListes) continue;
            if (f.EnTetes(EnTetes, ColRef) is not { } entetes) continue;
            reconnus++;
            var (ligneEnTetes, colonnes) = entetes;
            var manquantes = new[] { ColConstate, ColValidation, ColPar, ColDate }.Where(c => !colonnes.ContainsKey(c)).ToList();
            if (manquantes.Count > 0)
            {
                erreurs.Add($"Onglet {f.Nom}, ligne {ligneEnTetes} : colonne{(manquantes.Count > 1 ? "s" : "")} "
                    + $"{string.Join(", ", manquantes.Select(c => $"« {c} »"))} introuvable{(manquantes.Count > 1 ? "s" : "")} ; repartez d'un nouvel export.");
                continue;
            }
            string Valeur(int ligne, string colonne) => colonnes.TryGetValue(colonne, out var c) ? f.Texte(ligne, c) : "";
            for (var r = ligneEnTetes + 1; r <= f.DerniereLigne; r++)
            {
                var reference = Valeur(r, ColRef);
                if (reference.Length == 0) continue;
                var p = reference.Split(';');
                if (p.Length != 2 || !Guid.TryParse(p[0], out var ficheId) || !Guid.TryParse(p[1], out var etapeId))
                {
                    erreurs.Add($"Onglet {f.Nom}, ligne {r}, colonne « {ColRef} » : référence modifiée ; repartez d'un nouvel export.");
                    continue;
                }
                var fiche = mep.FichesRecette.FirstOrDefault(x => x.Id == ficheId);
                var numero = fiche?.Etapes.FirstOrDefault(t => t.Id == etapeId)?.Ordre;
                var titre = fiche is null ? Valeur(r, ColFiche) : TitreFiche(fiche);
                var lieu = $"Onglet {f.Nom}, ligne {r}" + (titre.Length == 0 ? "" : numero is { } n ? $" (« {titre} », étape {n})" : $" (« {titre} »)");
                var (_, dateLisible, date) = f.Date(r, colonnes[ColDate]);
                lignes.Add(new Regles.LigneValidation(ficheId, etapeId, lieu, Valeur(r, ColValidation), Valeur(r, ColConstate), Valeur(r, ColPar),
                    date, dateLisible));
            }
        }
        if (reconnus == 0)
            erreurs.Add("Aucun onglet d'étapes de test reconnu (colonne « Réf. » introuvable) : repartez d'un nouvel export de la recette.");
        return new(mepId, version, lignes, erreurs);
    }

    // Disposition des onglets système du format précédent (recette-1).
    private const int AncienneColEtape = 1, AncienneColConstate = 4, AncienneColValidation = 5, AncienneColPar = 6, AncienneColDate = 7;

    /// <summary>Format précédent (recette-1) : chaque étape à la ligne et dans l'onglet notés dans le contrôle.</summary>
    private static Lecture LireFormatPrecedent(LecteurClasseur lecteur, IReadOnlyDictionary<string, string> controle, Mep mep, Guid? mepId, string version)
    {
        var erreurs = new List<string>();
        var lignes = new List<Regles.LigneValidation>();
        var manquantes = new HashSet<string>();
        foreach (var (_, valeur) in controle.Where(kv => kv.Key.StartsWith(PrefixeEtape, StringComparison.Ordinal)))
        {
            var p = valeur.Split(';', 5);
            if (p.Length != 5 || !Guid.TryParse(p[0], out var ficheId) || !Guid.TryParse(p[1], out var etapeId)
                || !int.TryParse(p[2], CultureInfo.InvariantCulture, out var ligne) || !int.TryParse(p[3], CultureInfo.InvariantCulture, out var numero))
            {
                erreurs.Add("L'onglet « Contrôle » a été modifié : exportez de nouveau la recette.");
                break;
            }
            var onglet = p[4];
            if (lecteur.FeuilleApprochee(onglet) is not { } f)
            {
                if (manquantes.Add(onglet))
                    erreurs.Add($"Onglet « {onglet} » introuvable : les onglets du fichier ne doivent être ni renommés ni supprimés.");
                continue;
            }
            var titre = mep.FichesRecette.FirstOrDefault(x => x.Id == ficheId)?.Titre;
            var lieu = $"Onglet {f.Nom}, ligne {ligne}" + (string.IsNullOrWhiteSpace(titre) ? $" (étape {numero})" : $" (« {titre} », étape {numero})");
            if (f.Texte(ligne, AncienneColEtape) != numero.ToString(CultureInfo.InvariantCulture))
            {
                erreurs.Add($"{lieu} : l'étape n'est plus à sa place (lignes ajoutées ou supprimées) ; repartez d'un nouvel export.");
                continue;
            }
            var (_, dateLisible, date) = f.Date(ligne, AncienneColDate);
            lignes.Add(new Regles.LigneValidation(ficheId, etapeId, lieu, f.Texte(ligne, AncienneColValidation), f.Texte(ligne, AncienneColConstate),
                f.Texte(ligne, AncienneColPar), date, dateLisible));
        }
        return new(mepId, version, lignes, erreurs);
    }
}
