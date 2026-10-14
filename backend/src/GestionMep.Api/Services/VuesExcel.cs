using System.Globalization;
using System.Text;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>
/// Exportations Excel en lecture des vues de l'application (#58) : liste des MEP, préparation, étapes, retour arrière et
/// journal d'une MEP. Chaque onglet est un tableau (titre, ligne de contexte, en-têtes figés et filtre automatique) ou une
/// fiche « libellé / valeur ». Ces classeurs ne se réimportent pas : seuls la recette (<see cref="RecetteExcel"/>) et les
/// objets impactés (<see cref="ObjetsExcel"/>) s'échangent dans les deux sens. Les dates et heures sont dans le fuseau de la MEP.
/// </summary>
public static class VuesExcel
{
    public const string TypeContenu = RecetteExcel.TypeContenu;
    private const int LigneEnTetes = 4;

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
        _ => p.ToString(),
    };

    private static string Libelle(Phase p, IssueMep? issue) => issue switch
    {
        IssueMep.Reussie => "Clôturée : réussie",
        IssueMep.Repliee => "Clôturée : repliée",
        _ => Libelle(p),
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
        return famille is "approbateurs" or "approbations" ? "approbation" : famille;
    }

    private static string LibelleFamille(string type) => FamillesEvenement.TryGetValue(Famille(type), out var l) ? l : Famille(type);

    // ---------- Filtres (mêmes règles que l'interface) ----------

    /// <summary>Texte sans accents ni casse, comme <c>normaliser</c> du front (recherche.ts).</summary>
    public static string Normaliser(string? s)
    {
        var d = (s ?? "").Replace("œ", "oe").Replace("Œ", "oe").Replace("æ", "ae").Replace("Æ", "ae").Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Filtres de la liste des MEP : phase, type, et recherche dans l'outil, la version, l'objet, le pilote et les personnes
    /// (avec tous leurs comptes reliés), comme la page d'accueil. L'équipe est filtrée par <c>MepsEndpoints.Lister</c>.
    /// </summary>
    public static List<MepResumeDto> FiltrerMeps(IEnumerable<MepResumeDto> meps, Phase? phase, TypeMep? type, string? recherche)
    {
        var q = Normaliser((recherche ?? "").Trim());
        return meps.Where(m => (phase is null || m.Phase == phase) && (type is null || m.Type == type)
            && (q.Length == 0 || new[] { m.Outil, m.VersionCible, m.Objet, m.Pilote ?? "" }.Concat(m.Personnes).Any(t => Normaliser(t).Contains(q, StringComparison.Ordinal))))
            .ToList();
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

    private static string Assainir(string nom)
    {
        var s = new string(nom.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray()).Trim('-');
        return s.Length == 0 ? "mep" : s;
    }

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
        return $"{prefixe}-{Assainir($"{mep.Outil} {mep.VersionCible}".Trim())}.xlsx";
    }

    /// <summary>« meps-2026-10-14.xlsx » (« modeles-… » pour les modèles).</summary>
    public static string NomFichierListe(bool modeles, DateTimeOffset maintenant) =>
        $"{(modeles ? "modeles" : "meps")}-{Locale(maintenant, null):yyyy-MM-dd}.xlsx";

    // ---------- Briques de mise en page ----------

    /// <summary>Instant dans le fuseau donné (Europe/Paris par défaut, celui des MEP), sans indication de fuseau pour Excel.</summary>
    private static DateTime Locale(DateTimeOffset instant, string? fuseau)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(fuseau ?? "Europe/Paris")).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return instant.UtcDateTime;
        }
    }

    private static DateTime? Quand(DateTimeOffset? instant, string? fuseau) => instant is { } i ? DateTime.SpecifyKind(Locale(i, fuseau), DateTimeKind.Unspecified) : null;

    private static string Heure(TimeOnly? h) => h?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";

    private static string NomMep(MepDto mep)
    {
        var nom = $"{mep.Outil} {mep.VersionCible}".Trim();
        return nom.Length == 0 ? "MEP" : nom;
    }

    private static Format FormatValeur(object? v) => v is DateTime { TimeOfDay.Ticks: > 0 } ? Charte.Cellule with { DateHeure = true }
        : v is DateTime or DateOnly ? Charte.Cellule with { Date = true } : Charte.Cellule;

    /// <summary>Colonne d'un tableau : en-tête, largeur, et police à chasse fixe (codes, commandes).</summary>
    private sealed record Colonne(string Titre, double Largeur, bool Code = false);

    /// <summary>
    /// Onglet tableau : titre (ligne 1), contexte (ligne 2), en-têtes (ligne 4, figées, avec filtre automatique), puis une
    /// ligne par élément. Message discret à la place des lignes s'il n'y en a aucune.
    /// </summary>
    private static void Tableau(ClasseurExcel classeur, string onglet, string titre, string contexte, IReadOnlyList<Colonne> colonnes,
        IEnumerable<object?[]> lignes, string siVide)
    {
        var f = classeur.AjouterFeuille(onglet);
        f.Ecrire(1, 1, titre, Charte.Titre);
        f.Fusionner(1, 1, 1, Math.Max(1, Math.Min(colonnes.Count, 8)));
        f.HauteurLigne(1, 24);
        f.Ecrire(2, 1, contexte, Charte.SousTitre);
        f.Fusionner(2, 1, 2, Math.Max(1, Math.Min(colonnes.Count, 8)));
        f.HauteurLigne(2, Charte.HauteurTexte(contexte, colonnes.Take(8).Sum(c => c.Largeur)));
        for (var i = 0; i < colonnes.Count; i++)
        {
            f.Ecrire(LigneEnTetes, i + 1, colonnes[i].Titre, Charte.EnTete);
            f.LargeurColonne(i + 1, colonnes[i].Largeur);
        }
        var ligne = LigneEnTetes + 1;
        foreach (var valeurs in lignes)
        {
            for (var i = 0; i < colonnes.Count && i < valeurs.Length; i++)
            {
                var format = FormatValeur(valeurs[i]);
                f.Ecrire(ligne, i + 1, valeurs[i], colonnes[i].Code ? format with { ChasseFixe = true } : format);
            }
            ligne++;
        }
        if (ligne == LigneEnTetes + 1) f.Ecrire(ligne, 1, siVide, Charte.SousTitre);
        else f.FiltreAuto(LigneEnTetes, 1, ligne - 1, colonnes.Count);
        f.FigerLignes(LigneEnTetes);
    }

    /// <summary>Onglet fiche : une ligne « libellé / valeur » par champ ; une ligne sans valeur (null) sert d'intertitre.</summary>
    private static void Fiche(ClasseurExcel classeur, string onglet, string titre, string contexte, IEnumerable<(string Libelle, object? Valeur)> champs)
    {
        var f = classeur.AjouterFeuille(onglet);
        f.LargeurColonne(1, 34);
        f.LargeurColonne(2, 80);
        f.Ecrire(1, 1, titre, Charte.Titre);
        f.Fusionner(1, 1, 1, 2);
        f.HauteurLigne(1, 24);
        f.Ecrire(2, 1, contexte, Charte.SousTitre);
        f.Fusionner(2, 1, 2, 2);
        var ligne = 4;
        foreach (var (libelle, valeur) in champs)
        {
            if (valeur is null)
            {
                if (ligne > 4) ligne++;
                f.Ecrire(ligne, 1, libelle, Charte.EnTete);
                f.Fusionner(ligne, 1, ligne, 2);
                f.Formater(ligne, 1, ligne, 2, Charte.EnTete);
            }
            else
            {
                f.Ecrire(ligne, 1, libelle, Charte.Cellule with { Gras = true });
                var texte = valeur as string;
                f.Ecrire(ligne, 2, texte is { Length: 0 } ? "Non renseigné" : valeur,
                    texte is { Length: 0 } ? Charte.Cellule with { Couleur = Charte.TexteDiscret } : FormatValeur(valeur));
                if (texte is { Length: > 0 }) f.HauteurLigne(ligne, Charte.HauteurTexte(texte, 80));
            }
            ligne++;
        }
    }

    private static string Contexte(MepDto mep, DateTimeOffset maintenant) =>
        $"{NomMep(mep)}{(mep.Objet.Length > 0 ? " · " + mep.Objet : "")} · {(mep.EstModele ? "Modèle" : Libelle(mep.Phase, mep.Issue))} · "
        + $"exporté le {Locale(maintenant, mep.FuseauHoraire):dd/MM/yyyy à HH:mm}";

    private static string Participant(MepDto mep, Guid? id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "";

    private static string NomSysteme(MepDto mep, Guid? id) => mep.Systemes.FirstOrDefault(s => s.Id == id)?.Nom ?? "";

    private static string Environnements(SystemeDto s) =>
        string.Join(", ", s.Environnements.Select(e => e.Production ? $"{e.Nom} (production)" : e.Nom));

    // ---------- Liste des MEP ----------

    /// <summary>
    /// Liste des MEP (ou des modèles) telle qu'affichée, filtres compris : <paramref name="filtres"/> les décrit dans la ligne
    /// de contexte. <paramref name="equipes"/> : noms des équipes, par identifiant.
    /// </summary>
    public static byte[] ListeMeps(IReadOnlyList<MepResumeDto> meps, bool modeles, IReadOnlyList<string> filtres,
        IReadOnlyDictionary<Guid, string> equipes, DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var contexte = $"{meps.Count} {(modeles ? "modèle(s)" : "MEP")} · "
            + (filtres.Count == 0 ? "aucun filtre" : "filtres : " + string.Join(" ; ", filtres))
            + $" · exporté le {Locale(maintenant, null):dd/MM/yyyy à HH:mm}";
        string Equipes(MepResumeDto m) => string.Join(", ", m.EquipeIds.Select(e => equipes.TryGetValue(e, out var n) ? n : "").Where(n => n.Length > 0).Order());
        if (modeles)
        {
            Tableau(classeur, "Modèles", "Modèles de MEP", contexte,
                [new("Outil", 26), new("Objet", 44), new("Étapes de déploiement", 12), new("Retour arrière prévu", 12), new("Équipes", 24),
                    new("Créé le", 12), new("Identifiant", 38, true)],
                meps.Select(m => new object?[] { m.Outil, m.Objet, m.NbEtapes, OuiNon(m.RetourArriereActive), Equipes(m), DateOnly.FromDateTime(Locale(m.CreeLe, null)), m.Id.ToString() }),
                "Aucun modèle ne correspond à ces critères.");
        }
        else
        {
            Tableau(classeur, "MEP", "Mises en production", contexte,
                [new("Outil", 24), new("Version cible", 14, true), new("Objet", 40), new("Type", 11), new("Phase", 18), new("Production", 11),
                    new("Pilote", 20), new("Équipes", 22), new("Démarrée le", 12), new("Créée le", 12), new("Étapes", 8), new("Réussies", 9),
                    new("En échec", 9), new("Retour arrière prévu", 12), new("Échéance de retour arrière", 18), new("Identifiant", 38, true)],
                meps.Select(m => new object?[]
                {
                    m.Outil, m.Type == TypeMep.Simplifiee ? "" : m.VersionCible, m.Objet, Libelle(m.Type), Libelle(m.Phase, m.Issue),
                    m.ToucheProduction ? "Oui" : "", m.Pilote ?? "", Equipes(m), m.DateMep, DateOnly.FromDateTime(Locale(m.CreeLe, null)),
                    m.NbEtapes, m.NbReussies, m.NbEchecs, OuiNon(m.RetourArriereActive),
                    m.RetourArriereActive ? Quand(m.EcheanceRepli, null) : null, m.Id.ToString(),
                }),
                "Aucune MEP ne correspond à ces critères.");
        }
        return classeur.Enregistrer();
    }

    // ---------- Préparation ----------

    /// <summary>Préparation d'une MEP : un onglet par section (Intervention, Personnes, Systèmes…), comme le sommaire de l'onglet.</summary>
    public static byte[] Preparation(MepDto mep, DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var contexte = Contexte(mep, maintenant);
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
            ("Indisponibilité prévue (minutes)", mep.IndisponibiliteMinutes?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ("Plan de retour arrière", OuiNon(mep.RetourArriereActive)),
        ]);
        if (mep.RetourArriereActive) intervention.Add(("Échéance de retour arrière", (object?)Quand(mep.EcheanceRepli, f) ?? ""));
        intervention.AddRange([("Phase", mep.EstModele ? "Modèle" : Libelle(mep.Phase, mep.Issue)), ("Démarrée le", mep.DateMep is { } d ? d : "")]);
        Fiche(classeur, "Intervention", "1. Intervention", contexte, intervention);

        Tableau(classeur, "Personnes", "2. Personnes", contexte,
            [new("Nom", 24), new("Rôle", 22), new("Contact", 28), new("Compte", 22), new("Rôle dans la MEP", 16)],
            mep.Participants.Select(p => new object?[]
            {
                p.Nom, p.Role, p.Contact, p.UtilisateurNom ?? "",
                p.Id == mep.PiloteId ? "Pilote" : mep.SuppleantIds.Contains(p.Id) ? "Suppléant" : "",
            }), "Aucune personne.");

        Tableau(classeur, "Systèmes", "3. Systèmes", contexte,
            [new("Système", 14), new("Cible", 24, true), new("Impact", 34), new("Environnements", 30), new("Sauvegarde", 14),
                new("Moment", 14), new("Quoi et comment ?", 34), new("Justification", 30), new("Emplacement de la sauvegarde", 28),
                new("Méthode de récupération", 30)],
            mep.Systemes.Select(s => new object?[]
            {
                s.Nom, s.Cible, s.Impact, Environnements(s), Libelle(s.Sauvegarde.Statut),
                s.Sauvegarde.Statut == StatutSauvegarde.Requise ? Libelle(s.Sauvegarde.Moment) : "",
                s.Sauvegarde.Contenu, s.Sauvegarde.Justification, s.Sauvegarde.Emplacement, s.Sauvegarde.MethodeRecuperation,
            }), "Aucun système.");

        Tableau(classeur, "Responsables techniques", "4. Responsables techniques", contexte,
            [new("Personne", 24), new("Systèmes", 22), new("Périmètre", 36), new("Jour J", 12), new("Contact le jour J", 28)],
            mep.ResponsablesTechniques.Select(r => new object?[]
            {
                Participant(mep, r.ParticipantId), string.Join(", ", r.SystemeIds.Select(s => NomSysteme(mep, s)).Where(n => n.Length > 0)),
                r.Perimetre, r.Disponibilite == DisponibiliteJourJ.Absent ? "Absent" : "Joignable", r.ContactJourJ,
            }), "Aucun responsable technique.");

        var ordreSystemes = mep.Systemes.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Tableau(classeur, "Objets impactés", "5. Objets impactés", contexte,
            [new("Système", 14), new("Catégorie", 22), new("Type", 16), new("Nom", 30, true), new("Libellé", 34), new("Précision", 24),
                new("Version ou correction", 22)],
            mep.ObjetsImpactes.OrderBy(o => o.SystemeId is { } s && ordreSystemes.TryGetValue(s, out var i) ? i : int.MaxValue)
                .ThenBy(o => o.Categorie, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase)
                .Select(o => new object?[] { NomSysteme(mep, o.SystemeId), o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision, o.Version }),
            "Aucun objet impacté.");

        var sv = mep.Sauvegarde;
        Fiche(classeur, "Sauvegarde", "6. Sauvegarde", contexte,
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
            ("Procédure relue", mep.ProcedureRelue is { } pr ? $"Oui, par {pr.Par} le {Locale(pr.Le, f):dd/MM/yyyy à HH:mm}" : "Non"),
            ("Approbations demandées au minimum", mep.ApprobationsMinimum.ToString(CultureInfo.InvariantCulture)),
            ("Démarrage autorisé", mep.DemarrageAutorise is { } da ? $"Par {da.Par} le {Locale(da.Le, f):dd/MM/yyyy à HH:mm}" : "Non"),
            ("Approbations", null),
        };
        validation.AddRange(mep.Approbations.Select(a => (a.Nom, (object?)(a.Decision switch
        {
            DecisionApprobation.Approuvee => "Approuvée",
            DecisionApprobation.Refusee => "Refusée",
            _ => "En attente",
        } + (a.Le is { } le ? $" le {Locale(le, f):dd/MM/yyyy à HH:mm}" : "") + (a.Commentaire.Length > 0 ? $" : {a.Commentaire}" : "")))));
        if (mep.Approbations.Count == 0) validation.Add(("Approbateurs", "Aucun"));
        if (mep.GoNoGo.Count > 0)
        {
            validation.Add(("Critères validés au Go", null));
            validation.AddRange(mep.GoNoGo.Select(c => (c.Libelle, (object?)$"{(c.Par is null ? "Validé" : $"Validé par {c.Par}")} le {Locale(c.Le, f):dd/MM/yyyy à HH:mm}")));
        }
        Fiche(classeur, "Validation", "7. Validation", contexte, validation);

        var c = mep.Communication;
        Fiche(classeur, "Communication", "8. Communication", contexte,
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
        Tableau(classeur, "Pièces jointes", "9. Pièces jointes", contexte,
            [new("Fichier", 36), new("Étape", 10, true), new("Type", 26), new("Taille (Ko)", 11), new("Déposé par", 22), new("Déposé le", 16),
                new("Empreinte SHA-256", 40, true)],
            mep.PiecesJointes.OrderBy(p => p.DeposeLe).Select(p => new object?[]
            {
                p.NomFichier, p.EtapeId is { } e && codes.TryGetValue(e, out var code) ? code : "", p.TypeContenu,
                Math.Round(p.Taille / 1024d, 1), p.DeposePar ?? "", Quand(p.DeposeLe, f), p.Sha256,
            }), "Aucune pièce jointe (les fichiers eux-mêmes ne sont pas dans le classeur).");
        return classeur.Enregistrer();
    }

    // ---------- Étapes et retour arrière ----------

    /// <summary>
    /// Étapes d'un plan (déploiement ou retour arrière), dans l'ordre du graphe (ligne, puis position) : définition, exécution
    /// et résultat de chacune. Le retour arrière commence par la fiche du plan ; les résolutions exécutées suivent si besoin.
    /// <paramref name="resolutions"/> : titres des résolutions types, par identifiant.
    /// </summary>
    public static byte[] Plan(MepDto mep, TypePlan plan, IReadOnlyDictionary<Guid, string> resolutions, DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var contexte = Contexte(mep, maintenant);
        var f = mep.FuseauHoraire;
        var simplifiee = mep.Type == TypeMep.Simplifiee;
        var repli = plan == TypePlan.RetourArriere;
        if (repli)
        {
            var p = mep.PlanRepli;
            Fiche(classeur, "Fiche du plan", "Fiche du plan de retour arrière", contexte,
            [
                ("Plan de retour arrière prévu", OuiNon(mep.RetourArriereActive)),
                ("Quand déclencher le retour arrière ?", p.QuandDeclencher),
                ("Durée testée (minutes)", p.DureeTesteeMinutes?.ToString(CultureInfo.InvariantCulture) ?? ""),
                ("Version ou copies à restaurer", p.VersionARestaurer),
                ("Données créées depuis la sauvegarde", p.DonneesDepuisSauvegarde),
                ("Si le retour arrière échoue", p.SiRepliEchoue),
                ("Échéance de retour arrière", mep.RetourArriereActive ? (object?)Quand(mep.EcheanceRepli, f) ?? "" : ""),
                ("Point de non-retour franchi", mep.NonRetourFranchi ?? "Non"),
                ("Retour arrière déclenché", mep.RetourArriere is { } ra
                    ? $"Le {Locale(ra.Le, f):dd/MM/yyyy à HH:mm}{(ra.Par is null ? " (automatiquement)" : $" par {ra.Par}")}{(ra.Motif.Length > 0 ? $" : {ra.Motif}" : "")}"
                    : "Non"),
            ]);
        }

        var etapes = mep.Etapes.Where(e => e.Plan == plan).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).ToList();
        var codes = mep.Etapes.ToDictionary(e => e.Id, e => e.Code);
        string EnvironnementsEtape(EtapeDto e) => string.Join(" ; ", e.Environnements.GroupBy(x => x.SystemeId)
            .Select(g => $"{NomSysteme(mep, g.Key)} : {string.Join(", ", g.Select(x => x.Nom))}"));
        var colonnes = new List<Colonne> { new("Code", 9, true), new("Ligne", 7), new("Action", 40), new("Type", 12), new("Systèmes", 16),
            new("Environnements", 28), new("Dépend de", 14, true) };
        if (!simplifiee) colonnes.AddRange([new("Criticité", 11), new("Durée estimée (min)", 11)]);
        colonnes.AddRange([new("Point de non-retour", 12), new("Fin de l'étape", 16), new("Instructions", 60), new("Résultat attendu", 36),
            new("État", 11), new("Exécutant", 20), new("Début réel", 16), new("Fin réelle", 16), new("Résultat constaté", 36), new("Preuve", 30),
            new("Commentaire", 30)]);
        if (!simplifiee) colonnes.Add(new("Résolutions liées", 30));
        colonnes.AddRange([new("Emplacement de la sauvegarde", 26), new("Méthode de récupération", 26)]);
        Tableau(classeur, repli ? "Étapes de retour arrière" : "Étapes", repli ? "Étapes de retour arrière" : "Étapes de déploiement", contexte, colonnes,
            etapes.Select(e =>
            {
                var v = new List<object?>
                {
                    e.Code, e.Ligne + 1, e.Action, e.Type == TypeEtape.Sauvegarde ? "Sauvegarde" : "Normale",
                    string.Join(", ", e.SystemeIds.Select(s => NomSysteme(mep, s)).Where(n => n.Length > 0)), EnvironnementsEtape(e),
                    string.Join(", ", e.Dependances.Select(d => codes.TryGetValue(d, out var c) ? c : "").Where(c => c.Length > 0)),
                };
                if (!simplifiee) v.AddRange([Libelle(e.Criticite), e.DureeEstimeeMinutes]);
                v.AddRange([e.PointDeNonRetour ? "Oui" : "", e.Fin == FinEtape.Verification ? "Simple vérification" : "Test unitaire",
                    BlocsMarkdown.ToutEnTexte(e.Instructions), BlocsMarkdown.ToutEnTexte(e.ResultatAttendu), Libelle(e.Etat),
                    Participant(mep, e.ExecutantId), Quand(e.DebutReel, f), Quand(e.FinReelle, f), e.ResultatConstate, e.Preuve, e.Commentaire]);
                if (!simplifiee) v.Add(string.Join(" ; ", e.ResolutionIds.Select(r => resolutions.TryGetValue(r, out var t) ? t : "").Where(t => t.Length > 0)));
                v.AddRange([e.SauvegardeEmplacement, e.SauvegardeMethodeRecuperation]);
                return v.ToArray();
            }),
            repli ? "Aucune étape de retour arrière." : "Aucune étape de déploiement.");

        var executees = mep.Resolutions.Where(r => etapes.Any(e => e.Id == r.EtapeId)).ToList();
        if (executees.Count > 0)
            Tableau(classeur, "Résolutions exécutées", "Résolutions exécutées", contexte,
                [new("Étape", 9, true), new("Résolution", 36), new("Statut", 12), new("Démarrée par", 20), new("Démarrée le", 16),
                    new("Terminée le", 16), new("Corrections", 50), new("Vérification", 40)],
                executees.Select(r => new object?[]
                {
                    codes.TryGetValue(r.EtapeId, out var c) ? c : "", r.Titre, Libelle(r.Statut), r.DemarreePar, Quand(r.DemarreeLe, f),
                    Quand(r.TermineeLe, f),
                    string.Join("\n", r.Corrections.OrderBy(x => x.Ordre).Select(x => $"{x.Ordre}. {BlocsMarkdown.ToutEnTexte(x.Instruction)}"
                        + (x.Resultat is { } res ? $" ({(res == ResultatControle.Conforme ? "conforme" : "échec")})" : ""))),
                    BlocsMarkdown.ToutEnTexte(r.VerificationInstruction)
                        + (r.VerificationResultat is { } vr ? $" ({(vr == ResultatControle.Conforme ? "conforme" : "échec")})" : ""),
                }), "");
        return classeur.Enregistrer();
    }

    // ---------- Journal ----------

    /// <summary>
    /// Journal d'une MEP (du plus récent au plus ancien, comme l'onglet), filtres compris, puis le suivi des tests unitaires.
    /// </summary>
    public static byte[] Journal(MepDto mep, IReadOnlyList<EvenementDto> journal, IReadOnlyList<string> filtres, TestsUnitairesDto tests,
        DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var contexte = Contexte(mep, maintenant) + $" · {journal.Count} événement(s)"
            + (filtres.Count == 0 ? "" : " · filtres : " + string.Join(" ; ", filtres));
        var f = mep.FuseauHoraire;
        Tableau(classeur, "Journal", "Journal", contexte,
            [new("Quand", 16), new("Auteur", 22), new("Compte", 18), new("Type", 18), new("Détail", 80), new("Événement", 24, true)],
            journal.Select(e => new object?[] { Quand(e.Quand, f), e.Auteur, e.AuteurLogin ?? "", LibelleFamille(e.Type), e.Detail, e.Type }),
            "Aucun événement ne correspond à ces critères.");
        Tableau(classeur, "Tests unitaires", "Tests unitaires",
            $"{tests.Faits} fait(s), {tests.NonFaits} non fait(s) · {tests.Verifications} étape(s) terminée(s) par une simple vérification (non listées)",
            [new("Code", 9, true), new("Plan", 14), new("Action", 36), new("État", 11), new("Fait", 8), new("Résultat attendu", 40),
                new("Résultat constaté", 40), new("Exécutant", 20), new("Fin", 16)],
            tests.Tests.Select(t => new object?[]
            {
                t.Code, t.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", t.Action, Libelle(t.Etat), OuiNon(t.Fait),
                BlocsMarkdown.ToutEnTexte(t.ResultatAttendu), t.ResultatConstate, t.Executant ?? "", Quand(t.FinReelle, f),
            }), "Aucun test unitaire.");
        return classeur.Enregistrer();
    }
}
