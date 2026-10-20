using System.Globalization;
using System.Text;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Exportations Excel en lecture des vues de l'application (#58) : liste des MEP, préparation, étapes, retour arrière et
/// journal d'une MEP. Chaque onglet commence par un bloc de titre (vue et MEP, contexte, mention « Généré par ») puis
/// présente un tableau Excel (en-têtes figés, filtres, états colorés) ou une fiche « libellé / valeur ». Ces classeurs ne se réimportent pas : seuls la recette (<see cref="RecetteExcel"/>) et les
/// objets impactés (<see cref="ObjetsExcel"/>) s'échangent dans les deux sens. Les dates et heures sont dans le fuseau de la MEP.
/// </summary>
public static partial class VuesExcel
{
    public const string TypeContenu = ExcelCommun.TypeContenu;

    /// <summary>Vues d'une MEP exportables, avec leur nom dans les adresses et la ligne de commande.</summary>
    public enum Vue { Preparation, Etapes, RetourArriere, Journal }

    // ---------- Libellés (comme dans l'interface, frontend/src/format.ts) ----------

    public static string Libelle(Phase p) => p switch
    {
        Phase.Preparation => "Préparation",
        Phase.Execution => "Exécution",
        Phase.Recette => "Recette",
        Phase.RetourArriere => "Retour arrière",
        Phase.Cloturee => "Clôturée",
        Phase.Annulee => "Annulée",
        _ => p.ToString(),
    };

    /// <summary>Phase, puis issue une fois clôturée (« réussie avec incidents » : une étape a échoué en route, voir <see cref="Regles.EtatDe"/>).</summary>
    private static string LibellePhase(MepDto mep) => mep.Etat switch
    {
        EtatMep.Reussie => "Clôturée : réussie",
        EtatMep.AvecIncidents => "Clôturée : réussie avec incidents",
        EtatMep.Repliee => "Clôturée après retour arrière",
        EtatMep.Echec => "Clôturée : échec",
        EtatMep.Reportee => "Reportée",
        _ => Libelle(mep.Phase),
    };

    private static string Libelle(EtatEtape e) => e switch
    {
        EtatEtape.AFaire => "À faire",
        EtatEtape.EnCours => "En cours",
        EtatEtape.Reussie => "Réussie",
        EtatEtape.Echec => "Échec",
        _ => e.ToString(),
    };

    private static string Libelle(Criticite? c) => c switch
    {
        Criticite.Faible => "Faible",
        Criticite.Moderee => "Modérée",
        Criticite.Elevee => "Élevée",
        Criticite.Bloquante => "Bloquante",
        _ => "",
    };

    private static string Libelle(StatutResolution s) => s switch
    {
        StatutResolution.EnCours => "En cours",
        StatutResolution.Reussie => "Réussie",
        StatutResolution.Echouee => "Échouée",
        _ => s.ToString(),
    };

    private static string Libelle(StatutSauvegarde s) => s switch
    {
        StatutSauvegarde.Requise => "Requise",
        StatutSauvegarde.NonRequise => "Non requise",
        _ => "À renseigner",
    };

    private static string Libelle(ModeSauvegarde m) => m == ModeSauvegarde.PendantMep ? "Pendant la MEP" : "Avant la MEP";

    private static string Libelle(NiveauRisque n) => n switch
    {
        NiveauRisque.Faible => "Faible",
        NiveauRisque.Modere => "Modéré",
        NiveauRisque.Eleve => "Élevé",
        _ => "Non évalué",
    };

    private static string Libelle(TypeMep t) => t == TypeMep.Simplifiee ? "Simplifiée" : "Complète";

    private static string OuiNon(bool b) => b ? "Oui" : "Non";

    /// <summary>Familles d'événements du journal (préfixe du type), comme le filtre de l'onglet Journal.</summary>
    public static readonly IReadOnlyDictionary<string, string> FamillesEvenement = new Dictionary<string, string>
    {
        ["mep"] = "MEP", ["sauvegarde"] = "Sauvegarde", ["participant"] = "Personnes", ["responsables-techniques"] = "Responsables techniques",
        ["systeme"] = "Systèmes", ["livrable"] = "Livrables", ["etape"] = "Étapes", ["procedure"] = "Validation", ["phase"] = "Phase",
        ["repli"] = "Retour arrière", ["resolution"] = "Résolutions", ["approbation"] = "Approbations", ["gonogo"] = "Go / no-go",
        ["recette"] = "Recette", ["piece"] = "Pièces jointes", ["bilan"] = "Bilan", ["communication"] = "Communication",
    };

    /// <summary>Famille d'un type d'événement : « etape.reussie » → « etape » (approbateurs et approbations → approbation).</summary>
    public static string Famille(string type)
    {
        var i = type.IndexOf('.', StringComparison.Ordinal);
        var famille = i < 0 ? type : type[..i];
        return famille switch
        {
            "approbateurs" or "approbations" => "approbation",
            // Passation et pilotage de secours : rangés avec les personnes de la MEP (#93), comme dans l'interface.
            "pilotage" => "participant",
            _ => famille,
        };
    }

    /// <summary>Préfixes de type (avant le point) d'une famille d'événements : l'inverse de <see cref="Famille"/>.</summary>
    public static string[] PrefixesDeFamille(string famille) => famille switch
    {
        "approbation" => ["approbation", "approbateurs", "approbations"],
        "participant" => ["participant", "pilotage"],
        _ => [famille],
    };

    private static string LibelleFamille(string type) => FamillesEvenement.TryGetValue(Famille(type), out var l) ? l : Famille(type);

    // ---------- Filtres (mêmes règles que l'interface) ----------

    /// <summary>Texte sans accents ni casse, comme <c>normaliser</c> du front (recherche.ts).</summary>
    public static string Normaliser(string? s)
    {
        var d = (s ?? "").Replace("œ", "oe").Replace("Œ", "oe").Replace("æ", "ae").Replace("Æ", "ae").Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }

    /// <summary>Filtres de l'onglet Journal : famille d'événement et recherche dans le détail, l'auteur et son compte.</summary>
    public static List<EvenementDto> FiltrerJournal(IEnumerable<EvenementDto> journal, string? famille, string? recherche)
    {
        var q = (recherche ?? "").Trim().ToLowerInvariant();
        var f = string.IsNullOrWhiteSpace(famille) || famille == "tous" ? null : famille;
        return journal.Where(e => (f is null || Famille(e.Type) == f)
            && (q.Length == 0 || e.Detail.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Auteur.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.AuteurLogin ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    // ---------- Noms de fichier ----------

    /// <summary>« etapes-Outil-1.2.xlsx », « preparation-… », « retour-arriere-… », « journal-… ».</summary>
    public static string NomFichier(MepDto mep, Vue vue)
    {
        var prefixe = vue switch
        {
            Vue.Preparation => "preparation",
            Vue.Etapes => "etapes",
            Vue.RetourArriere => "retour-arriere",
            _ => "journal",
        };
        return ExcelCommun.NomFichier(prefixe, NomMep(mep));
    }

    /// <summary>
    /// « meps-2026-10-14.xlsx » (« modeles-… » pour les modèles) ; « meps-BDD-2026-10-14.xlsx » quand la liste est filtrée
    /// sur un seul système.
    /// </summary>
    public static string NomFichierListe(bool modeles, DateTimeOffset maintenant, string? systeme = null) =>
        systeme is { Length: > 0 } && !modeles ? ExcelCommun.NomFichier("meps", $"{systeme} {Fuseaux.Local(maintenant, null):yyyy-MM-dd}")
            : $"{(modeles ? "modeles" : "meps")}-{Fuseaux.Local(maintenant, null):yyyy-MM-dd}.xlsx";

    /// <summary>« historique-BDD-2026-10-14.xlsx ».</summary>
    public static string NomFichierHistorique(string systeme, DateTimeOffset maintenant) =>
        ExcelCommun.NomFichier("historique", $"{systeme} {Fuseaux.Local(maintenant, null):yyyy-MM-dd}");

    // ---------- Briques de mise en page ----------

    private static DateTime? Quand(DateTimeOffset? instant, string? fuseau) => ExcelCommun.Quand(instant, fuseau);

    private static string Heure(TimeOnly? h) => h?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";

    private static string NomMep(MepDto mep) => ExcelCommun.NomMep(mep.Outil, mep.VersionCible);

    private static ClasseurExcel Classeur(string vue, MepDto mep, DateTimeOffset maintenant, PaletteExcel? palette) =>
        ExcelCommun.Nouveau($"{vue} · {NomMep(mep)}", maintenant, mep.FuseauHoraire, palette, mep.Objet.Length > 0 ? mep.Objet : null);

    /// <summary>
    /// Onglet tableau : bloc de titre (titre, contexte, mention de génération), puis tableau Excel à la ligne 4 (en-têtes
    /// figés, filtres, colonnes facultatives vides retirées). Message discret à la place des lignes s'il n'y en a aucune.
    /// </summary>
    private static TableauEcrit Tableau(ClasseurExcel classeur, string onglet, string titre, string? contexte, IReadOnlyList<ColonneTableau> colonnes,
        IEnumerable<object?[]> lignes, string siVide, int colonnesFigees = 0)
    {
        var f = classeur.AjouterFeuille(onglet);
        var t = f.Tableau(4, onglet, colonnes, lignes, siVide);
        // Le contexte s'étend sur les premières colonnes visibles, sans dépasser une largeur d'écran.
        double largeur = 0;
        var n = 0;
        foreach (var c in t.Colonnes)
        {
            if (c.Masquee || largeur >= 140) break;
            largeur += c.Largeur;
            n++;
        }
        f.BlocTitre(titre, contexte, Math.Max(1, n), largeur);
        if (colonnesFigees > 0 && t.Colonnes.Count > colonnesFigees) f.FigerLignes(4, colonnesFigees);
        return t;
    }

    /// <summary>Onglet fiche : bloc de titre, puis une ligne « libellé / valeur » par champ ; une ligne sans valeur (null) sert d'intertitre.</summary>
    private static void Fiche(ClasseurExcel classeur, string onglet, string titre, string? contexte, IEnumerable<(string Libelle, object? Valeur)> champs)
    {
        var f = classeur.AjouterFeuille(onglet);
        f.LargeurColonne(1, 34);
        f.LargeurColonne(2, 90);
        var ligne = f.BlocTitre(titre, contexte, 2, 124);
        f.Fiche(ligne, champs);
    }

    /// <summary>Ligne de contexte d'une MEP : objet et phase (le nom de la MEP est dans le titre).</summary>
    private static string Contexte(MepDto mep) =>
        string.Join(" · ", new[] { mep.Objet, mep.EstModele ? "Modèle" : LibellePhase(mep) }.Where(s => s.Length > 0));

    private static string Participant(MepDto mep, Guid? id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "";

    private static string NomSysteme(MepDto mep, Guid? id) => mep.Systemes.FirstOrDefault(s => s.Id == id)?.Nom ?? "";

    private static string Environnements(SystemeDto s) =>
        string.Join(", ", s.Environnements.Select(e =>
            e.Production && !e.Nom.Contains("prod", StringComparison.OrdinalIgnoreCase) ? $"{e.Nom} (production)" : e.Nom));

    private static string Le(DateTimeOffset instant, string? fuseau) => $"{Fuseaux.Local(instant, fuseau):dd/MM/yyyy à HH:mm}";

    private static int? MinutesEntre(DateTimeOffset? de, DateTimeOffset? a) =>
        de is { } d && a is { } f ? (int)Math.Round((f - d).TotalMinutes) : null;

    private static ColonneTableau Col(string titre, double largeur, TypeColonne type = TypeColonne.Texte) => new(titre, largeur, type);

    /// <summary>Identifiant technique : gardé (masqué) pour les recherches et la ligne de commande, jamais affiché.</summary>
    private static ColonneTableau ColIdentifiant() => new("Identifiant", 38, TypeColonne.Code) { Masquee = true };

    // ---------- Liste des MEP ----------

    /// <summary>
    /// Liste des MEP (ou des modèles) telle qu'affichée, filtres compris : <paramref name="filtres"/> les décrit dans la ligne
    /// de contexte. <paramref name="equipes"/> : noms des équipes, par identifiant. Pour les MEP, un second onglet « Échecs »
    /// donne une ligne par échec d'étape (<paramref name="echecs"/>, limités aux systèmes filtrés) ; une colonne donne les rôles
    /// de la personne filtrée (<paramref name="nomPersonne"/>).
    /// </summary>
    public static byte[] ListeMeps(IReadOnlyList<MepResumeDto> meps, bool modeles, IReadOnlyList<string> filtres,
        IReadOnlyDictionary<Guid, string> equipes, DateTimeOffset maintenant, PaletteExcel? palette = null, FiltreMeps? filtre = null,
        IReadOnlyDictionary<Guid, List<HistoriqueSystemes.EchecDetaille>>? echecs = null, string? nomPersonne = null)
    {
        filtre ??= FiltreMeps.Aucun;
        var titre = modeles ? "Modèles de MEP" : "Mises en production";
        using var classeur = ExcelCommun.Nouveau(titre, maintenant, null, palette);
        var contexte = $"{meps.Count} {(modeles ? (meps.Count > 1 ? "modèles" : "modèle") : "MEP")} · "
            + (filtres.Count == 0 ? "aucun filtre" : "filtres : " + string.Join(" ; ", filtres));
        string Equipes(MepResumeDto m) => string.Join(", ", m.EquipeIds.Select(e => equipes.TryGetValue(e, out var n) ? n : "").Where(n => n.Length > 0).Order());
        if (modeles)
        {
            Tableau(classeur, "Modèles", titre, contexte,
                [Col("Outil", 26), Col("Objet", 48), Col("Étapes de déploiement", 13, TypeColonne.Entier),
                    Col("Retour arrière prévu", 12, TypeColonne.Court), Col("Équipes", 26) with { Facultative = true }, Col("Créé le", 12, TypeColonne.Date),
                    ColIdentifiant()],
                meps.Select(m => new object?[]
                {
                    m.Outil, m.Objet, m.NbEtapes, OuiNon(m.RetourArriereActive), Equipes(m), Fuseaux.Jour(m.CreeLe, null),
                    m.Id.ToString(),
                }),
                "Aucun modèle ne correspond à ces critères.");
            return classeur.Enregistrer();
        }
        var personne = filtre.Personne is not null;
        var colonnes = new List<ColonneTableau>
        {
            Col("Outil", 24), Col("Version cible", 12) with { Facultative = true }, Col("Objet", 44), Col("Type", 11, TypeColonne.Court),
            Col("État", 20, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatMep }, Col("Jour", 12, TypeColonne.Date),
            Col("Go", 16, TypeColonne.DateHeure) with { Facultative = true }, Col("Clôturée le", 16, TypeColonne.DateHeure) with { Facultative = true },
            Col("Systèmes et environnements", 36) with { Facultative = true }, Col("Production", 11, TypeColonne.Court),
            Col("Échecs survenus", 10, TypeColonne.Entier), Col("Pilote", 18),
        };
        if (personne) colonnes.Add(Col($"Rôle de {nomPersonne ?? "la personne"}", 26));
        colonnes.AddRange([Col("Équipes", 24) with { Facultative = true }, Col("Créée le", 12, TypeColonne.Date), Col("Étapes", 8, TypeColonne.Entier),
            Col("Réussies", 9, TypeColonne.Entier), Col("En échec", 9, TypeColonne.Entier), Col("Retour arrière prévu", 12, TypeColonne.Court),
            Col("Échéance de retour arrière", 17, TypeColonne.DateHeure) with { Facultative = true }, ColIdentifiant()]);
        Tableau(classeur, "Mises en production", titre, contexte, colonnes,
            meps.Select(m =>
            {
                var ligne = new List<object?>
                {
                    m.Outil, m.Type == TypeMep.Simplifiee ? "" : m.VersionCible, m.Objet, Libelle(m.Type), FiltreMeps.Libelle(m.Etat), m.Jour,
                    Quand(m.Go, null), Quand(m.ClotureeLe, null), SystemesEtEnvironnements(m.Systemes), OuiNon(m.ToucheProduction),
                    m.EchecsSurvenus, m.Pilote ?? "",
                };
                if (personne) ligne.Add(Roles(filtre.RolesDeLaPersonne(m)));
                ligne.AddRange([Equipes(m), Fuseaux.Jour(m.CreeLe, null), m.NbEtapes, m.NbReussies, m.NbEchecs,
                    OuiNon(m.RetourArriereActive), m.RetourArriereActive ? Quand(m.EcheanceRepli, null) : null, m.Id.ToString()]);
                return ligne.ToArray();
            }),
            "Aucune MEP ne correspond à ces critères.", colonnesFigees: 1);

        var parId = meps.ToDictionary(m => m.Id);
        var lignesEchecs = meps.SelectMany(m => echecs?.GetValueOrDefault(m.Id) ?? [])
            .Where(e => filtre.Systemes.Count == 0 || e.Systemes.Any(s => filtre.Retient(new SystemeResumeDto(s.CatalogueId, s.Nom, [], 0))))
            .ToList();
        Tableau(classeur, "Échecs", "Échecs d'étape", $"{lignesEchecs.Count} échec{(lignesEchecs.Count > 1 ? "s" : "")} sur ces MEP"
                + (filtre.Systemes.Count > 0 ? " (systèmes filtrés)" : "") + " · une étape reprise garde son échec",
            ColonnesEchecs(avecMep: true), lignesEchecs.Select(e => LigneEchec(e, parId[e.MepId])), "Aucun échec d'étape sur ces MEP.", colonnesFigees: 1);
        return classeur.Enregistrer();
    }

    /// <summary>« BDD : Recette, Production · API : Production » ; un système hors catalogue est signalé.</summary>
    private static string SystemesEtEnvironnements(IEnumerable<SystemeResumeDto> systemes) =>
        string.Join(" · ", systemes.Select(s => (s.CatalogueId is null ? $"{s.Nom} (hors catalogue)" : s.Nom)
            + (s.Environnements.Count > 0 ? " : " + string.Join(", ", s.Environnements.Select(e => e.Nom)) : "")));

    /// <summary>Rôles d'une personne dans une MEP : « Pilote, Exécutant », « Participant » sans rôle, vide si elle n'y est pas.</summary>
    private static string Roles(IReadOnlyList<RoleMep>? roles) =>
        roles is null ? "" : roles.Count == 0 ? "Participant" : string.Join(", ", roles.Select(FiltreMeps.Libelle));

    private static List<ColonneTableau> ColonnesEchecs(bool avecMep)
    {
        var c = new List<ColonneTableau>();
        if (avecMep) c.AddRange([Col("MEP", 30), Col("Jour", 12, TypeColonne.Date), Col("État de la MEP", 20, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatMep }]);
        c.AddRange([Col("Systèmes", 18), Col("Plan", 14, TypeColonne.Court), Col("Étape", 8, TypeColonne.Code), Col("Action", 36),
            Col("Échec le", 16, TypeColonne.DateHeure), Col("Par", 18), Col("Motif", 40), Col("Résolution", 30) with { Facultative = true },
            Col("Statut de la résolution", 14, TypeColonne.Court) with { Tons = ExcelCommun.TonsResolution, Facultative = true },
            Col("Reprise réussie", 11, TypeColonne.Court), Col("Arrêt (min)", 10, TypeColonne.Entier) with { Facultative = true }]);
        if (avecMep) c.Add(ColIdentifiant());
        return c;
    }

    private static object?[] LigneEchec(HistoriqueSystemes.EchecDetaille e, MepResumeDto? mep)
    {
        var p = e.Point;
        var l = new List<object?>();
        if (mep is not null) l.AddRange([ExcelCommun.NomMep(mep.Outil, mep.VersionCible), mep.Jour, FiltreMeps.Libelle(mep.Etat)]);
        l.AddRange([string.Join(", ", e.Systemes.Select(s => s.Nom)), p.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", p.Code,
            p.Action, Quand(p.EchecLe, null), p.Par, p.Motif, p.Resolution ?? "", p.StatutResolution is { } st ? Libelle(st) : "", OuiNon(e.Repris),
            p.ArretMinutes]);
        if (mep is not null) l.Add(mep.Id.ToString());
        return [.. l];
    }

    // ---------- Préparation ----------

    /// <summary>Préparation d'une MEP : un onglet par section (Intervention, Personnes, Systèmes…), comme le sommaire de l'onglet.</summary>
    public static byte[] Preparation(MepDto mep, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        using var classeur = Classeur("Préparation", mep, maintenant, palette);
        var contexte = Contexte(mep);
        var nom = NomMep(mep);
        var simplifiee = mep.Type == TypeMep.Simplifiee;
        var f = mep.FuseauHoraire;

        var intervention = new List<(string, object?)>
        {
            ("Outil", mep.Outil), ("Type de MEP", Libelle(mep.Type)),
        };
        if (!simplifiee) intervention.AddRange([("Version actuelle", mep.VersionActuelle), ("Version cible", mep.VersionCible)]);
        intervention.AddRange(
        [
            ("Objet", mep.Objet), ("Impacts attendus", mep.Impacts),
            ("Pilote", Participant(mep, mep.PiloteId)),
            ("Suppléants", string.Join(", ", mep.SuppleantIds.Select(s => Participant(mep, s)).Where(n => n.Length > 0))),
            ("Fuseau horaire", mep.FuseauHoraire),
            ("Indisponibilité prévue", mep.IndisponibiliteMinutes is { } ind ? VariablesTexte.Duree(ind) : ""),
            ("Plan de retour arrière", OuiNon(mep.RetourArriereActive)),
        ]);
        if (mep.RetourArriereActive) intervention.Add(("Échéance de retour arrière", (object?)Quand(mep.EcheanceRepli, f) ?? ""));
        intervention.AddRange([("Phase", mep.EstModele ? "Modèle" : LibellePhase(mep)), ("Démarrée le", mep.DateMep is { } d ? d : "")]);
        Fiche(classeur, "Intervention", $"Intervention · {nom}", contexte, intervention);

        Tableau(classeur, "Personnes", $"Personnes · {nom}", contexte,
            [Col("Nom", 24), Col("Rôle", 24), Col("Contact", 30), Col("Compte", 22) with { Facultative = true }, Col("Rôle dans la MEP", 16, TypeColonne.Court)],
            mep.Participants.Select(p => new object?[]
            {
                p.Nom, p.Role, p.Contact, p.UtilisateurNom ?? "",
                p.Id == mep.PiloteId ? "Pilote" : mep.SuppleantIds.Contains(p.Id) ? "Suppléant" : "",
            }), "Aucune personne.");

        Tableau(classeur, "Systèmes", $"Systèmes · {nom}", contexte,
            [Col("Système", 14), Col("Cible", 24, TypeColonne.Code), Col("Impact", 34), Col("Environnements", 28) with { Facultative = true },
                Col("Sauvegarde", 13, TypeColonne.Court) with { Tons = ExcelCommun.TonsSauvegarde },
                Col("Moment", 15, TypeColonne.Court) with { Facultative = true }, Col("Quoi et comment ?", 34) with { Facultative = true },
                Col("Justification", 30) with { Facultative = true }, Col("Emplacement de la sauvegarde", 28) with { Facultative = true },
                Col("Méthode de récupération", 30) with { Facultative = true }],
            mep.Systemes.Select(s => new object?[]
            {
                s.Nom, s.Cible, s.Impact, Environnements(s), Libelle(s.Sauvegarde.Statut),
                s.Sauvegarde.Statut == StatutSauvegarde.Requise ? Libelle(s.Sauvegarde.Moment) : "",
                BlocsMarkdown.ToutEnTexte(s.Sauvegarde.Contenu), BlocsMarkdown.ToutEnTexte(s.Sauvegarde.Justification),
                BlocsMarkdown.ToutEnTexte(s.Sauvegarde.Emplacement), BlocsMarkdown.ToutEnTexte(s.Sauvegarde.MethodeRecuperation),
            }), "Aucun système.", colonnesFigees: 1);

        Tableau(classeur, "Responsables techniques", $"Responsables techniques · {nom}", contexte,
            [Col("Personne", 24), Col("Systèmes", 22), Col("Périmètre", 40), Col("Jour J", 12, TypeColonne.Court) with
                {
                    Tons = new Dictionary<string, Ton> { ["Absent"] = Ton.Attention, ["Joignable"] = Ton.Succes },
                }, Col("Contact le jour J", 30)],
            mep.ResponsablesTechniques.Select(r => new object?[]
            {
                Participant(mep, r.ParticipantId), string.Join(", ", r.SystemeIds.Select(s => NomSysteme(mep, s)).Where(n => n.Length > 0)),
                r.Perimetre, r.Disponibilite == DisponibiliteJourJ.Absent ? "Absent" : "Joignable", r.ContactJourJ,
            }), "Aucun responsable technique.");

        var ordreSystemes = mep.Systemes.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Tableau(classeur, "Objets impactés", $"Objets impactés · {nom}", contexte,
            [Col("Système", 14), Col("Catégorie", 22), Col("Type", 16), Col("Nom", 30, TypeColonne.Code), Col("Libellé", 36),
                Col("Précision", 24) with { Facultative = true }, Col("Version ou correction", 24)],
            mep.ObjetsImpactes.OrderBy(o => o.SystemeId is { } s && ordreSystemes.TryGetValue(s, out var i) ? i : int.MaxValue)
                .ThenBy(o => o.Categorie, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase)
                .Select(o => new object?[] { NomSysteme(mep, o.SystemeId), o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision, o.Version }),
            "Aucun objet impacté.");

        var sv = mep.Sauvegarde;
        Fiche(classeur, "Sauvegarde", $"Sauvegarde · {nom}", contexte,
        [
            ("Responsable de la sauvegarde", Participant(mep, sv.ResponsableId)),
            ("Réalisée le", (object?)Quand(sv.RealiseeLe, f) ?? ""),
            ("Procédure de sauvegarde", BlocsMarkdown.ToutEnTexte(sv.Procedure)),
            ("Copies (emplacements)", sv.Copies),
            ("Restauration testée", OuiNon(sv.RestaurationTestee)),
            ("Date du test de restauration", sv.DateTestRestauration is { } dt ? dt : ""),
            ("Preuve du test", sv.PreuveTestRestauration),
            ("Sauvegarde par système", null),
            .. mep.Systemes.Select(s => (s.Nom, (object?)(Libelle(s.Sauvegarde.Statut)
                + (s.Sauvegarde.Statut == StatutSauvegarde.Requise ? $" ({Libelle(s.Sauvegarde.Moment).ToLowerInvariant()})" : "")))),
        ]);

        var validation = new List<(string, object?)>
        {
            ("Procédure relue", mep.ProcedureRelue is { } pr ? $"Oui, par {pr.Par} le {Le(pr.Le, f)}" : "Non"),
            ("Approbations demandées au minimum", mep.ApprobationsMinimum.ToString(CultureInfo.InvariantCulture)),
            ("Démarrage autorisé", mep.DemarrageAutorise is { } da ? $"Par {da.Par} le {Le(da.Le, f)}" : "Non"),
            ("Approbations", null),
        };
        validation.AddRange(mep.Approbations.Select(a => (a.Nom, (object?)(a.Decision switch
        {
            DecisionApprobation.Approuvee => "Approuvée",
            DecisionApprobation.Refusee => "Refusée",
            _ => "En attente",
        } + (a.Le is { } le ? $" le {Le(le, f)}" : "") + (a.Commentaire.Length > 0 ? $" : {a.Commentaire}" : "")))));
        if (mep.Approbations.Count == 0) validation.Add(("Approbateurs", "Aucun"));
        if (mep.GoNoGo.Count > 0)
        {
            validation.Add(("Critères validés au Go", null));
            validation.AddRange(mep.GoNoGo.Select(c => (c.Libelle, (object?)$"{(c.Par is null ? "Validé" : $"Validé par {c.Par}")} le {Le(c.Le, f)}")));
        }
        Fiche(classeur, "Validation", $"Validation · {nom}", contexte, validation);

        var c = mep.Communication;
        Fiche(classeur, "Communication", $"Communication · {nom}", contexte,
        [
            ("Référence de la demande", c.ReferenceDemande), ("Niveau de risque", Libelle(c.NiveauRisque)),
            ("Bénéfices et nouveautés", c.Benefices), ("Utilisateurs impactés", c.UtilisateursImpactes),
            ("Arrêt applicatif nécessaire", OuiNon(c.ArretApplicatif)), ("Action demandée aux utilisateurs", c.ActionUtilisateurs),
            ("Heure limite", Heure(c.HeureLimite)), ("Date annoncée", c.DateAnnoncee is { } ann ? ann : ""),
            ("Heure de début annoncée", Heure(c.HeureDebutAnnoncee)), ("Heure de fin annoncée", Heure(c.HeureFinAnnoncee)),
            ("Validée par les utilisateurs", OuiNon(c.ValideeParUtilisateurs)),
            ("Validée par", c.ValideePar), ("Validée le", c.ValideeLe is { } vl ? vl : ""),
        ]);

        var codes = mep.Etapes.ToDictionary(e => e.Id, e => e.Code);
        Tableau(classeur, "Pièces jointes", $"Pièces jointes · {nom}", contexte + " · les fichiers eux-mêmes ne sont pas dans le classeur",
            [Col("Fichier", 36), Col("Étape", 9, TypeColonne.Code) with { Facultative = true }, Col("Type de fichier", 24),
                Col("Taille (Ko)", 11, TypeColonne.Nombre), Col("Déposé par", 22), Col("Déposé le", 16, TypeColonne.DateHeure),
                Col("Empreinte SHA-256", 40, TypeColonne.Code) with { Note = "Empreinte du fichier, pour vérifier qu'il n'a pas été modifié." }],
            mep.PiecesJointes.OrderBy(p => p.DeposeLe).Select(p => new object?[]
            {
                p.NomFichier, p.EtapeId is { } e && codes.TryGetValue(e, out var code) ? code : "", p.TypeContenu,
                Math.Round(p.Taille / 1024d, 1), p.DeposePar ?? "", Quand(p.DeposeLe, f), p.Sha256,
            }), "Aucune pièce jointe.");
        return classeur.Enregistrer();
    }

    // ---------- Étapes et retour arrière ----------

    /// <summary>
    /// Étapes d'un plan (déploiement ou retour arrière), dans l'ordre du graphe (ligne, puis position) : définition, exécution
    /// et résultat de chacune ; les colonnes qu'aucune étape ne renseigne sont omises. Le retour arrière commence par la fiche
    /// du plan ; les résolutions exécutées suivent si besoin. <paramref name="resolutions"/> : titres des résolutions types.
    /// </summary>
    public static byte[] Plan(MepDto mep, TypePlan plan, IReadOnlyDictionary<Guid, string> resolutions, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        var repli = plan == TypePlan.RetourArriere;
        using var classeur = Classeur(repli ? "Retour arrière" : "Étapes de déploiement", mep, maintenant, palette);
        var contexte = Contexte(mep);
        var nom = NomMep(mep);
        var f = mep.FuseauHoraire;
        if (repli)
        {
            var p = mep.PlanRepli;
            Fiche(classeur, "Plan de retour arrière", $"Plan de retour arrière · {nom}", contexte,
            [
                ("Plan de retour arrière prévu", OuiNon(mep.RetourArriereActive)),
                ("Quand déclencher le retour arrière ?", p.QuandDeclencher),
                ("Durée testée", p.DureeTesteeMinutes is { } dt ? VariablesTexte.Duree(dt) : ""),
                ("Version ou copies à restaurer", p.VersionARestaurer),
                ("Données créées depuis la sauvegarde", p.DonneesDepuisSauvegarde),
                ("Si le retour arrière échoue", p.SiRepliEchoue),
                ("Échéance de retour arrière", mep.RetourArriereActive ? (object?)Quand(mep.EcheanceRepli, f) ?? "" : ""),
                ("Point de non-retour franchi", mep.NonRetourFranchi ?? "Non"),
                ("Retour arrière déclenché", mep.RetourArriere is { } ra
                    ? $"Le {Le(ra.Le, f)}{(ra.Par is null ? " (automatiquement)" : $" par {ra.Par}")}{(ra.Motif.Length > 0 ? $" : {ra.Motif}" : "")}"
                    : "Non"),
            ]);
        }

        var etapes = mep.Etapes.Where(e => e.Plan == plan).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).ToList();
        var codes = mep.Etapes.ToDictionary(e => e.Id, e => e.Code);
        string EnvironnementsEtape(EtapeDto e) => string.Join(" ; ", e.Environnements.GroupBy(x => x.SystemeId)
            .Select(g => $"{NomSysteme(mep, g.Key)} : {string.Join(", ", g.Select(x => x.Nom))}"));
        var sections = mep.Sections.ToDictionary(s => s.Id, s => s.Titre);
        var titre = repli ? "Étapes de retour arrière" : "Étapes de déploiement";
        var siVide = repli ? "Aucune étape de retour arrière." : "Aucune étape de déploiement.";
        // Deux onglets pour rester lisibles à l'écran comme à l'impression : le déroulé (qui, quoi, quand, dans quel état),
        // puis le texte des étapes (instructions, résultats, preuves).
        Tableau(classeur, titre, $"{titre} · {nom}", contexte,
            [
                Col("Code", 9, TypeColonne.Code), Col("Ligne", 7, TypeColonne.Entier) with { Note = "Ligne du plan : les étapes d'une même ligne se suivent." },
                Col("Section", 18) with { Facultative = true }, Col("Action", 40), Col("Type", 12, TypeColonne.Court) with { Facultative = true },
                Col("Systèmes", 14) with { Facultative = true }, Col("Environnements", 24) with { Facultative = true },
                Col("Dépend de", 12, TypeColonne.Code) with { Facultative = true },
                Col("Criticité", 11, TypeColonne.Court) with { Facultative = true, Tons = ExcelCommun.TonsCriticite },
                Col("Durée estimée", 10, TypeColonne.Duree) with { Facultative = true },
                Col("Point de non-retour", 11, TypeColonne.Court) with { Facultative = true, Tons = ExcelCommun.TonsAlerte },
                Col("État", 11, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatEtape }, Col("Exécutant", 18) with { Facultative = true },
                Col("Début réel", 16, TypeColonne.DateHeure) with { Facultative = true }, Col("Fin réelle", 16, TypeColonne.DateHeure) with { Facultative = true },
            ],
            etapes.Select(e => new object?[]
            {
                e.Code, e.Ligne + 1, e.SectionId is { } sid && sections.TryGetValue(sid, out var section) ? section : "", e.Action,
                e.Type == TypeEtape.Sauvegarde ? "Sauvegarde" : "",
                string.Join(", ", e.SystemeIds.Select(s => NomSysteme(mep, s)).Where(n => n.Length > 0)), EnvironnementsEtape(e),
                string.Join(", ", e.Dependances.Select(d => codes.TryGetValue(d, out var c) ? c : "").Where(c => c.Length > 0)),
                Libelle(e.Criticite), e.DureeEstimeeMinutes, e.PointDeNonRetour ? "Oui" : "", Libelle(e.Etat),
                Participant(mep, e.ExecutantId), Quand(e.DebutReel, f), Quand(e.FinReelle, f),
            }),
            siVide, colonnesFigees: 1);
        Tableau(classeur, "Instructions et résultats", $"Instructions et résultats · {nom}", contexte,
            [
                Col("Code", 9, TypeColonne.Code), Col("Action", 30), Col("Instructions", 54), Col("Résultat attendu", 34),
                Col("Fin de l'étape", 13, TypeColonne.Court) with { Note = "Test unitaire : le résultat constaté est demandé. Simple vérification : non." },
                Col("Résultat constaté", 34) with { Facultative = true }, Col("Preuve", 26) with { Facultative = true },
                Col("Commentaire", 26) with { Facultative = true }, Col("Résolutions liées", 26) with { Facultative = true },
                Col("Emplacement de la sauvegarde", 24) with { Facultative = true }, Col("Méthode de récupération", 24) with { Facultative = true },
            ],
            etapes.Select(e => new object?[]
            {
                e.Code, e.Action, BlocsMarkdown.ToutEnTexte(e.Instructions), BlocsMarkdown.ToutEnTexte(e.ResultatAttendu),
                e.Fin == FinEtape.Verification ? "Simple vérification" : "Test unitaire", e.ResultatConstate, e.Preuve, e.Commentaire,
                string.Join(" ; ", e.ResolutionIds.Select(r => resolutions.TryGetValue(r, out var t) ? t : "").Where(t => t.Length > 0)),
                BlocsMarkdown.ToutEnTexte(e.SauvegardeEmplacement), BlocsMarkdown.ToutEnTexte(e.SauvegardeMethodeRecuperation),
            }),
            siVide, colonnesFigees: 1);

        var executees = mep.Resolutions.Where(r => etapes.Any(e => e.Id == r.EtapeId)).ToList();
        if (executees.Count > 0)
            Tableau(classeur, "Résolutions exécutées", $"Résolutions exécutées · {nom}", contexte,
                [Col("Étape", 9, TypeColonne.Code), Col("Résolution", 36), Col("Statut", 12, TypeColonne.Court) with { Tons = ExcelCommun.TonsResolution },
                    Col("Démarrée par", 20), Col("Démarrée le", 16, TypeColonne.DateHeure), Col("Terminée le", 16, TypeColonne.DateHeure),
                    Col("Durée", 9, TypeColonne.Duree), Col("Corrections", 50), Col("Vérification", 40)],
                executees.Select(r => new object?[]
                {
                    codes.TryGetValue(r.EtapeId, out var c) ? c : "", r.Titre, Libelle(r.Statut), r.DemarreePar, Quand(r.DemarreeLe, f),
                    Quand(r.TermineeLe, f), MinutesEntre(r.DemarreeLe, r.TermineeLe),
                    string.Join("\n", r.Corrections.OrderBy(x => x.Ordre).Select(x => $"{x.Ordre}. {BlocsMarkdown.ToutEnTexte(x.Instruction)}"
                        + (x.Resultat is { } res ? $" ({(res == ResultatControle.Conforme ? "conforme" : "échec")})" : ""))),
                    BlocsMarkdown.ToutEnTexte(r.VerificationInstruction)
                        + (r.VerificationResultat is { } vr ? $" ({(vr == ResultatControle.Conforme ? "conforme" : "échec")})" : ""),
                }), "");
        return classeur.Enregistrer();
    }

    // ---------- Journal ----------

    private static readonly ColonneTableau[] ColonnesJournal =
    [
        Col("Quand", 16, TypeColonne.DateHeure), Col("Auteur", 22), Col("Compte", 16) with { Facultative = true }, Col("Type", 18, TypeColonne.Court),
        Col("Détail", 90), new("Événement", 24, TypeColonne.Code) { Masquee = true },
    ];

    private static object?[] LigneJournal(EvenementDto e, string? fuseau) =>
        [Quand(e.Quand, fuseau), e.Auteur, e.AuteurLogin ?? "", LibelleFamille(e.Type), e.Detail, e.Type];

    private static ColonneTableau[] ColonnesTests(bool etat) =>
    [
        Col("Code", 9, TypeColonne.Code), Col("Plan", 14, TypeColonne.Court), Col("Action", 36),
        .. etat ? [Col("État", 11, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatEtape }] : Array.Empty<ColonneTableau>(),
        Col("Fait", 8, TypeColonne.Court) with { Tons = ExcelCommun.TonsFait }, Col("Résultat attendu", 40), Col("Résultat constaté", 40),
        Col("Exécutant", 20), Col("Fin", 16, TypeColonne.DateHeure),
    ];

    private static object?[] LigneTest(TestUnitaireDto t, string? fuseau, bool etat) =>
    [
        t.Code, t.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", t.Action, .. etat ? [Libelle(t.Etat)] : Array.Empty<object?>(),
        OuiNon(t.Fait), BlocsMarkdown.ToutEnTexte(t.ResultatAttendu), t.ResultatConstate, t.Executant ?? "", Quand(t.FinReelle, fuseau),
    ];

    private static string ContexteTests(TestsUnitairesDto tests) =>
        $"{tests.Faits} fait(s), {tests.NonFaits} non fait(s) · {tests.Verifications} étape(s) terminée(s) par une simple vérification (non listées)";

    /// <summary>
    /// Journal d'une MEP (du plus récent au plus ancien, comme l'onglet), filtres compris, puis le suivi des tests unitaires.
    /// </summary>
    public static byte[] Journal(MepDto mep, IReadOnlyList<EvenementDto> journal, IReadOnlyList<string> filtres, TestsUnitairesDto tests,
        DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        using var classeur = Classeur("Journal", mep, maintenant, palette);
        var nom = NomMep(mep);
        var contexte = Contexte(mep) + $" · {journal.Count} événement(s)" + (filtres.Count == 0 ? "" : " · filtres : " + string.Join(" ; ", filtres));
        var f = mep.FuseauHoraire;
        Tableau(classeur, "Journal", $"Journal · {nom}", contexte, ColonnesJournal, journal.Select(e => LigneJournal(e, f)),
            "Aucun événement ne correspond à ces critères.");
        Tableau(classeur, "Tests unitaires", $"Tests unitaires · {nom}", ContexteTests(tests), ColonnesTests(etat: true),
            tests.Tests.Select(t => LigneTest(t, f, etat: true)), "Aucun test unitaire.");
        return classeur.Enregistrer();
    }
}
