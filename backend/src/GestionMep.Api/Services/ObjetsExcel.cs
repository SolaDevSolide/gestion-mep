using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Objets impactés d'une MEP en classeur Excel, modèle vide ou export (les deux se réimportent) :
/// <list type="bullet">
/// <item>« À lire » : mode d'emploi, précautions et exemple ;</item>
/// <item>« Objets impactés » : tableau à remplir, une ligne par objet. Système, catégorie et nom se choisissent dans des
/// listes ; type, libellé et précision se remplissent seuls depuis le catalogue, et la colonne « Contrôle » dit tout de
/// suite si l'objet sera reconnu à l'import ;</item>
/// <item>« Catalogue » (lecture seule) : objets connus de chaque système de la MEP ;</item>
/// <item>« Listes » (masqué) : valeurs des listes déroulantes.</item>
/// </list>
/// Le réimport (<see cref="Lire"/>) repère les colonnes par leur en-tête (dans n'importe quel ordre, fichiers de la
/// version précédente compris) et rattache chaque ligne à un objet de la fiche de son système au catalogue ; les autres
/// lignes sont rejetées avec leur raison.
/// </summary>
public static class ObjetsExcel
{
    public const string FeuilleALire = "À lire";
    public const string FeuilleObjets = "Objets impactés";
    public const string FeuilleCatalogue = "Catalogue";
    /// <summary>Lignes vides prêtes à remplir sous les objets (listes déroulantes et formules comprises).</summary>
    public const int LignesASaisir = 100;
    private const int LigneEnTetes = 4;
    /// <summary>Lignes lues au plus.</summary>
    private const int LignesMax = 5000;

    public const string ColSysteme = "Système", ColCategorie = "Catégorie", ColNom = "Nom", ColVersion = "Version ou correction",
        ColType = "Type", ColLibelle = "Libellé", ColPrecision = "Précision", ColControle = "Contrôle", ColPosition = "Ligne au catalogue";

    public const string AuCatalogue = "Au catalogue", AbsentDuCatalogue = "Absent du catalogue", PreciserCategorie = "Préciser la catégorie",
        PreciserSysteme = "Préciser le système";

    /// <summary>Nom de fichier proposé : « objets-impactes-Outil-1.2.xlsx » (« -modele » pour le modèle vide).</summary>
    public static string NomFichier(Mep mep, bool modele) => ExcelCommun.NomFichier(modele ? "objets-impactes-modele" : "objets-impactes", RecetteExcel.NomMep(mep));

    /// <summary>
    /// Classeur des objets impactés : vide (<paramref name="modele"/>) ou avec les objets de la MEP. Les systèmes et objets
    /// proposés viennent de <paramref name="catalogue"/> (fiches des systèmes de la MEP).
    /// </summary>
    public static byte[] Exporter(Mep mep, IReadOnlyDictionary<Guid, SystemeCatalogue> catalogue, bool modele, DateTimeOffset maintenant,
        PaletteExcel? palette = null)
    {
        var nomMep = RecetteExcel.NomMep(mep);
        using var classeur = ExcelCommun.Nouveau(modele ? $"Modèle d'objets impactés · {nomMep}" : $"Objets impactés · {nomMep}", maintenant,
            mep.FuseauHoraire, palette, mep.Objet.Length > 0 ? mep.Objet : null);
        var systemes = mep.Systemes.Where(s => s.CatalogueId is { } c && catalogue.ContainsKey(c)).ToList();
        var objetsCatalogue = systemes.SelectMany(s =>
        {
            var fiche = catalogue[s.CatalogueId!.Value];
            return fiche.Objets.OrderBy(o => fiche.Categories.IndexOf(o.Categorie)).ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase).Select(o => (s, o));
        }).ToList();

        var aLire = classeur.AjouterFeuille(FeuilleALire);
        var f = classeur.AjouterFeuille(FeuilleObjets);
        var cat = classeur.AjouterFeuille(FeuilleCatalogue);

        // Catalogue : tableau en lecture seule, avec deux clés masquées pour retrouver un objet depuis l'onglet de saisie.
        var tc = cat.Tableau(LigneEnTetes, "Catalogue",
            [new(ColSysteme, 14), new(ColCategorie, 24), new(ColNom, 32, TypeColonne.Code), new(ColType, 16), new(ColLibelle, 40),
                new(ColPrecision, 26), new("Clé", 30) { Masquee = true }, new("Clé courte", 30) { Masquee = true }],
            objetsCatalogue.Select(x => new object?[]
            {
                x.s.Nom, x.o.Categorie, x.o.Nom, x.o.Type, x.o.Libelle, x.o.Precision, $"{x.s.Nom}|{x.o.Categorie}|{x.o.Nom}", $"{x.s.Nom}|{x.o.Nom}",
            }),
            "Aucun objet au catalogue pour les systèmes de cette MEP.");
        cat.BlocTitre($"Catalogue des objets · {nomMep}",
            "Seuls ces objets peuvent être retenus. Un objet manque ? Un administrateur l'ajoute à la fiche du système (Paramètres › Systèmes).", 6);
        cat.Proteger();

        // Onglet de saisie.
        var plusieursSystemes = systemes.Count > 1;
        var cle3 = objetsCatalogue.Count > 0 ? tc.Adresse("Clé") : null;
        var cle2 = objetsCatalogue.Count > 0 ? tc.Adresse("Clé courte") : null;
        object?[] Ligne(object? systeme, object? categorie, object? nom, object? version)
        {
            // A : système, B : catégorie, C : nom, D : version, I : ligne au catalogue (masquée).
            FormuleExcel? Info(string colonne) => cle3 is null ? null
                : new FormuleExcel($"IF(OR($I{{ligne}}=\"\",$I{{ligne}}=0),\"\",INDEX({tc.Adresse(colonne)},$I{{ligne}})&\"\")");
            var position = cle3 is null ? null
                : new FormuleExcel($"IF($C{{ligne}}=\"\",\"\",IFERROR(MATCH($A{{ligne}}&\"|\"&$B{{ligne}}&\"|\"&$C{{ligne}},{cle3},0),"
                    + $"IFERROR(MATCH($A{{ligne}}&\"|\"&$C{{ligne}},{cle2},0),0)))");
            var controle = cle3 is null ? null
                : new FormuleExcel($"IF($C{{ligne}}=\"\",\"\",IF(AND($A{{ligne}}=\"\",{(plusieursSystemes ? "TRUE" : "FALSE")}),\"{PreciserSysteme}\","
                    + $"IF(AND($B{{ligne}}=\"\",COUNTIF({cle2},$A{{ligne}}&\"|\"&$C{{ligne}})>1),\"{PreciserCategorie}\","
                    + $"IF($I{{ligne}}>0,\"{AuCatalogue}\",\"{AbsentDuCatalogue}\"))))");
            return [systeme, categorie, nom, version, Info(ColType), Info(ColLibelle), Info(ColPrecision), controle, position];
        }
        var lignes = new List<object?[]>();
        if (!modele)
            foreach (var o in Regles.ObjetsTries(mep, catalogue))
                lignes.Add(Ligne(mep.Systemes.FirstOrDefault(s => s.Id == o.SystemeId)?.Nom ?? "", o.Categorie, o.Nom, o.Version));
        for (var i = 0; i < LignesASaisir; i++) lignes.Add(Ligne(null, null, null, null));

        var t = f.Tableau(LigneEnTetes, "Objets impactes",
            [
                new(ColSysteme, 14) { ASaisir = true, Note = "À choisir dans la liste : système de la MEP. Facultatif si la MEP n'a qu'un système du catalogue." },
                new(ColCategorie, 24) { ASaisir = true, Note = "À choisir dans la liste. Facultative, sauf si deux objets du système portent le même nom." },
                new(ColNom, 32, TypeColonne.Code) { ASaisir = true, Note = "À choisir dans la liste : nom de l'objet, tel qu'au catalogue (onglet « Catalogue »)." },
                new(ColVersion, 24) { ASaisir = true, Note = "À remplir : version livrée ou référence de la correction (200 caractères au plus)." },
                new(ColType, 16) { Note = "Rempli automatiquement depuis le catalogue." },
                new(ColLibelle, 36) { Note = "Rempli automatiquement depuis le catalogue." },
                new(ColPrecision, 24) { Note = "Rempli automatiquement depuis le catalogue." },
                new(ColControle, 20, TypeColonne.Court)
                {
                    Note = "Indique si la ligne sera reconnue à l'import. Les lignes « Absent du catalogue » seront rejetées.",
                    Tons = new Dictionary<string, Ton>
                    {
                        [AuCatalogue] = Ton.Succes, [AbsentDuCatalogue] = Ton.Echec, [PreciserCategorie] = Ton.Attention, [PreciserSysteme] = Ton.Attention,
                    },
                },
                new(ColPosition, 8, TypeColonne.Entier) { Masquee = true },
            ],
            lignes);
        f.BlocTitre(modele ? $"Modèle d'objets impactés · {nomMep}" : $"Objets impactés · {nomMep}",
            "Une ligne par objet : remplir les cases jaunes (mode d'emploi : onglet « À lire »). L'import ajoute les objets et met à jour les versions ; il ne retire rien.",
            8);
        f.FigerLignes(LigneEnTetes);

        // Listes déroulantes.
        var listeSystemes = classeur.Liste("Systemes", systemes.Select(s => s.Nom));
        var listeCategories = classeur.Liste("Categories", objetsCatalogue.Select(x => x.o.Categorie));
        var listeNoms = classeur.Liste("Objets", objetsCatalogue.Select(x => x.o.Nom).Order(StringComparer.CurrentCultureIgnoreCase));
        int C(string titre) => t.Colonne(titre);
        if (listeSystemes is not null)
            f.ListeDeroulante(t.PremiereLigne, C(ColSysteme), t.DerniereLigne, C(ColSysteme), listeSystemes, stricte: true, ColSysteme,
                "Système de la MEP, dans la liste.");
        if (listeCategories is not null)
            f.ListeDeroulante(t.PremiereLigne, C(ColCategorie), t.DerniereLigne, C(ColCategorie), listeCategories, stricte: true, ColCategorie,
                "Catégorie de l'objet, dans la liste (facultative).");
        if (listeNoms is not null)
            f.ListeDeroulante(t.PremiereLigne, C(ColNom), t.DerniereLigne, C(ColNom), listeNoms, stricte: true, ColNom,
                "Objet du catalogue, dans la liste. Un objet manque ? Le faire ajouter à la fiche du système.");
        f.Aide(t.PremiereLigne, C(ColVersion), t.DerniereLigne, C(ColVersion), ColVersion, "Version livrée ou référence de la correction.");

        // À lire.
        var aide = new FeuilleAide(aLire, modele ? $"Modèle d'objets impactés · {nomMep}" : $"Objets impactés · {nomMep}",
            mep.Objet.Length > 0 ? mep.Objet : null);
        aide.Section("Comment remplir ce classeur");
        aide.Point("1", "Dans l'onglet « Objets impactés », une ligne par objet : choisissez le système, la catégorie et le nom dans les listes.");
        aide.Point("2", "Indiquez la version livrée ou la référence de la correction.");
        aide.Point("3", "Type, libellé et précision se remplissent seuls ; la colonne « Contrôle » indique si l'objet sera reconnu.");
        aide.Point("4", "Dans l'application : Préparation › Objets impactés › « Importer ». L'import ajoute les objets et met à jour les versions ; "
            + "il ne retire rien. Les lignes non reconnues sont listées avec leur raison.");
        aide.Section("À savoir");
        aide.Point("Catalogue", "Seuls les objets de l'onglet « Catalogue » peuvent être retenus. Un objet manque ? Un administrateur l'ajoute "
            + "à la fiche du système (Paramètres › Systèmes).");
        aide.Point("Souplesse", "Les lignes vides sont ignorées ; majuscules, accents et espaces autour des valeurs ne comptent pas. "
            + "Ajouter des lignes au tableau ou réordonner les colonnes ne gêne pas l'import.");
        aide.Point("Ancien fichier", "Un fichier produit par la version précédente de l'application s'importe aussi.");
        aide.Section("Légende");
        aide.Legende();
        aide.Point(AuCatalogue, "L'objet sera retenu à l'import.", aLire.Styles.Etat(Ton.Succes));
        aide.Point(AbsentDuCatalogue, "L'objet n'existe pas dans la fiche du système : la ligne sera rejetée.", aLire.Styles.Etat(Ton.Echec));
        aide.Section("Exemple de ligne remplie (à ne pas recopier)");
        var exemple = objetsCatalogue.FirstOrDefault();
        aide.Exemple(ColSysteme, exemple.s?.Nom ?? "API");
        aide.Exemple(ColCategorie, exemple.o?.Categorie ?? "Services");
        aide.Exemple(ColNom, exemple.o?.Nom ?? "commandes");
        aide.Exemple(ColVersion, "2.4.1");
        aide.Terminer();

        classeur.VerrouillerStructure();
        return classeur.Enregistrer();
    }

    /// <summary>Ligne retenue (rattachée à un objet du catalogue) ou rejetée, et anomalie de forme qui refuse le fichier entier.</summary>
    public record Lecture(List<Regles.LigneObjet> Retenues, List<LigneRejeteeDto> Rejetees, string? Erreur);

    /// <summary>En-têtes reconnus (forme comparable, voir <see cref="LecteurClasseur.Cle"/>) et colonne qu'ils désignent.</summary>
    private static readonly Dictionary<string, string> EnTetes = new()
    {
        ["systeme"] = "systeme", ["application"] = "systeme",
        ["categorie"] = "categorie",
        ["type"] = "type", ["type d objet"] = "type",
        ["nom"] = "nom", ["objet"] = "nom", ["nom de l objet"] = "nom", ["nom objet"] = "nom",
        ["version ou correction"] = "version", ["version correction"] = "version", ["version"] = "version", ["correction"] = "version",
        ["version livree"] = "version",
    };

    private static readonly string[] OngletsIgnores = [FeuilleALire, FeuilleCatalogue, ClasseurExcel.FeuilleListes];

    /// <summary>
    /// Lit un classeur rempli (modèle, export, fichier de la version précédente ou construit à la main avec les mêmes
    /// en-têtes) : onglet « Objets impactés » (ou, à défaut, le premier onglet qui a une colonne « Nom ») ; colonnes repérées
    /// par leur en-tête, dans n'importe quel ordre ; lignes vides ignorées ; valeurs comparées sans casse ni espaces autour.
    /// Chaque ligne est rattachée à un objet de la fiche de son système (même nom, même catégorie si elle est donnée, même
    /// type s'il départage). <see cref="ClasseurIllisibleException"/> si ce n'est pas un classeur .xlsx.
    /// </summary>
    public static Lecture Lire(byte[] contenu, Mep mep, IReadOnlyDictionary<Guid, SystemeCatalogue> catalogue)
    {
        using var lecteur = LecteurClasseur.Ouvrir(contenu);
        if (!lecteur.Feuilles.Any()) return new([], [], "Classeur vide.");
        var f = lecteur.FeuilleApprochee(FeuilleObjets) is { } o && o.EnTetes(EnTetes, "nom") is not null ? o
            : lecteur.Toutes.FirstOrDefault(x => !OngletsIgnores.Any(n => LecteurClasseur.Cle(n) == LecteurClasseur.Cle(x.Nom)) && x.EnTetes(EnTetes, "nom") is not null);
        if (f?.EnTetes(EnTetes, "nom") is not { } entetes)
            return new([], [], $"Fichier non reconnu : aucun onglet n'a de ligne d'en-têtes avec une colonne « {ColNom} » "
                + $"(attendu : {ColSysteme}, {ColCategorie}, {ColNom}, {ColVersion}). Partez du modèle Excel de la page.");
        var (ligneEnTetes, colonnes) = entetes;

        var candidats = mep.Systemes.Where(s => s.CatalogueId is { } c && catalogue.ContainsKey(c)).ToList();
        string Valeur(int ligne, string colonne) => colonnes.TryGetValue(colonne, out var c) ? f.Texte(ligne, c) : "";
        var retenues = new List<Regles.LigneObjet>();
        var rejetees = new List<LigneRejeteeDto>();
        var derniere = Math.Min(f.DerniereLigne, ligneEnTetes + LignesMax);
        for (var r = ligneEnTetes + 1; r <= derniere; r++)
        {
            var (systeme, categorie, type, nom, version) = (Valeur(r, "systeme"), Valeur(r, "categorie"), Valeur(r, "type"), Valeur(r, "nom"), Valeur(r, "version"));
            if (systeme.Length + categorie.Length + nom.Length + version.Length == 0) continue;
            LigneRejeteeDto Rejet(string raison, Guid? catalogueId = null) => new(r, systeme, categorie, nom, raison, catalogueId);

            Systeme? s;
            if (systeme.Length == 0)
            {
                // Système non précisé : admis seulement si la MEP n'a qu'un système du catalogue.
                if (candidats.Count != 1)
                {
                    rejetees.Add(Rejet($"Colonne « {ColSysteme} » : système non précisé ({string.Join(", ", candidats.Select(c => c.Nom))}).".Replace(" ()", "", StringComparison.Ordinal)));
                    continue;
                }
                s = candidats[0];
            }
            else s = mep.Systemes.FirstOrDefault(x => LecteurClasseur.Cle(x.Nom) == LecteurClasseur.Cle(systeme));
            if (s is null)
            {
                rejetees.Add(Rejet($"Colonne « {ColSysteme} » : le système {systeme} n'est pas dans la MEP"
                    + (mep.Systemes.Count > 0 ? $" ({string.Join(", ", mep.Systemes.Select(x => x.Nom))})." : ".")));
                continue;
            }
            if (s.CatalogueId is not { } cid || !catalogue.TryGetValue(cid, out var fiche))
            {
                rejetees.Add(Rejet($"{s.Nom} n'est pas issu du catalogue : ses objets ne se choisissent pas."));
                continue;
            }
            if (nom.Length == 0) { rejetees.Add(Rejet($"Colonne « {ColNom} » : nom de l'objet manquant.", cid)); continue; }
            static bool Egal(string a, string b) => LecteurClasseur.Cle(a) == LecteurClasseur.Cle(b);
            var memes = fiche.Objets.Where(o => o.Nom.Trim().Equals(nom, StringComparison.OrdinalIgnoreCase)
                && (categorie.Length == 0 || Egal(o.Categorie, categorie))).ToList();
            if (memes.Count > 1 && type.Length > 0) memes = memes.Where(o => Egal(o.Type, type)).ToList();
            if (memes.Count == 0)
            {
                rejetees.Add(Rejet(categorie.Length == 0
                    ? $"Colonne « {ColNom} » : objet absent de la fiche {s.Nom} du catalogue."
                    : $"Colonnes « {ColCategorie} » et « {ColNom} » : objet absent de la catégorie {categorie} de la fiche {s.Nom} du catalogue.", cid));
                continue;
            }
            if (memes.Count > 1)
            {
                rejetees.Add(Rejet($"Colonne « {ColCategorie} » : plusieurs objets « {nom} » dans la fiche {s.Nom}, préciser la catégorie.", cid));
                continue;
            }
            if (version.Length > 200) { rejetees.Add(Rejet($"Colonne « {ColVersion} » : 200 caractères au plus.", cid)); continue; }
            retenues.Add(new Regles.LigneObjet(s, memes[0], version));
        }
        return new(retenues, rejetees, null);
    }
}
