using System.Globalization;

namespace GestionMep.Domain;

/// <summary>
/// Variable des modèles de texte : <c>{{Cle}}</c> dans l'objet ou le texte. <paramref name="Liste"/> : la valeur est une liste,
/// rendue en puces quand la variable est seule sur sa ligne (sinon séparée par des virgules).
/// </summary>
public record VariableTexte(string Cle, string Libelle, string Groupe, string Description, bool Liste = false);

/// <summary>Valeur d'une variable pour une MEP : un texte, ou une liste. Les deux null (ou liste vide) : information manquante.</summary>
public record ValeurVariable(string? Texte, IReadOnlyList<string>? Liste)
{
    public static readonly ValeurVariable Manquante = new(null, null);
    public bool EstManquante => string.IsNullOrWhiteSpace(Texte) && (Liste is null || Liste.Count == 0);
}

/// <summary>Modèle de texte fourni par l'application, copié dans chaque équipe (voir <see cref="ModeleTexte.CleFournie"/>).</summary>
public record ModeleTexteFourni(string Cle, string Nom, UsageModeleTexte Usage, string Objet, string Corps);

/// <summary>
/// Variables des modèles de texte et leurs valeurs pour une MEP. La liste est documentée dans docs/Modèles-de-texte.md
/// (un test vérifie que chaque variable y figure) : toute fonctionnalité qui ajoute une information utile dans un courriel
/// ajoute ici sa variable.
/// </summary>
public static class VariablesTexte
{
    public const string GroupeMep = "MEP";
    public const string GroupeDates = "Date et heure";
    public const string GroupePersonnes = "Personnes";
    public const string GroupeSystemes = "Systèmes et objets impactés";
    public const string GroupeCommunication = "Communication";
    public const string GroupeSuivi = "Suivi de l'exécution";
    public const string GroupeFin = "Fin de MEP";
    public const string GroupeEquipe = "Équipe";

    /// <summary>Toutes les variables, groupe par groupe, dans l'ordre où l'éditeur les propose.</summary>
    public static readonly IReadOnlyList<VariableTexte> Toutes =
    [
        new("application", "Application", GroupeMep, "Outil concerné (Intervention)."),
        new("objet", "Objet de la MEP", GroupeMep, "Objet de l'intervention."),
        new("type_mep", "Type de MEP", GroupeMep, "« MEP complète » ou « MEP simplifiée »."),
        new("version_actuelle", "Version actuelle", GroupeMep, "Version en production avant la MEP."),
        new("version_cible", "Version cible", GroupeMep, "Version installée par la MEP."),
        new("impacts", "Impacts attendus", GroupeMep, "Impacts saisis dans Intervention."),
        new("lien_mep", "Lien vers la MEP", GroupeMep, "Adresse de la MEP dans l'application (adresse publique du serveur)."),
        new("sections", "Sections du plan", GroupeMep, "Titres des sections du plan de déploiement, dans l'ordre de création.", Liste: true),

        new("date", "Date de la MEP", GroupeDates, "Date annoncée (Communication), sinon date du Go. Ex. « mardi 20 octobre 2026 »."),
        new("heure_debut", "Heure de début", GroupeDates, "Heure de début annoncée, sinon heure du Go. Ex. « 20h00 »."),
        new("heure_fin", "Heure de fin", GroupeDates, "Heure de fin annoncée, sinon heure de la clôture."),
        new("indisponibilite", "Indisponibilité prévue", GroupeDates, "Durée d'indisponibilité saisie dans Intervention. Ex. « 2 h 30 »."),

        new("pilote", "Pilote", GroupePersonnes, "Nom du pilote de la MEP."),
        new("pilote_contact", "Contact du pilote", GroupePersonnes, "Contact du pilote (fiche Personnes)."),
        new("responsables_techniques", "Responsables techniques", GroupePersonnes, "Responsables techniques et leur périmètre.", Liste: true),

        new("systemes", "Systèmes", GroupeSystemes, "Systèmes de la MEP et leur impact.", Liste: true),
        new("environnements", "Environnements", GroupeSystemes, "Environnements touchés, système par système. Ex. « BDD : Préproduction, Production ».", Liste: true),
        new("touche_production", "Touche la production", GroupeSystemes, "« oui (BDD, WEB) » : systèmes dont un environnement de production est touché, sinon « non »."),
        new("objets_impactes", "Objets impactés", GroupeSystemes, "Objets impactés et leur version, système par système. Ex. « BDD : clients (Tables) 2.0 ».", Liste: true),

        new("reference_demande", "Référence de la demande", GroupeCommunication, "Numéro du ticket ou de la demande de changement."),
        new("niveau_risque", "Niveau de risque", GroupeCommunication, "Niveau retenu par l'analyse d'impact : faible, modéré ou élevé."),
        new("benefices", "Bénéfices et nouveautés", GroupeCommunication, "Une nouveauté par ligne.", Liste: true),
        new("utilisateurs_impactes", "Utilisateurs impactés", GroupeCommunication, "Utilisateurs concernés par la MEP."),
        new("arret_applicatif", "Arrêt applicatif", GroupeCommunication, "« oui, pendant 2 h 30 » (durée de l'indisponibilité prévue) ou « non »."),
        new("action_utilisateurs", "Action demandée", GroupeCommunication, "Action demandée aux utilisateurs avant la MEP."),
        new("heure_limite", "Heure limite", GroupeCommunication, "Heure limite de l'action demandée."),
        new("validation_utilisateurs", "Validation par les utilisateurs", GroupeCommunication, "« validée par … le … (preuve : fichier) »."),

        new("avancement", "Avancement", GroupeSuivi, "Étapes réussies du plan suivi (le retour arrière une fois déclenché). Ex. « 4/14 étapes réussies (29 %) »."),
        new("fin_estimee", "Fin estimée", GroupeSuivi,
            "Heure de fin estimée de l'exécution, comme la vue d'ensemble (plus long chemin des durées restantes), avec le retard. "
            + "Ex. « 16h33 (prévue à 15h18, +1 h 15) ». Absente avant le Go et pour une MEP sans durées estimées."),

        new("issue", "Issue", GroupeFin,
            "« réussie », « réussie avec incidents » (une étape a échoué en route) ou « clôturée après retour arrière », une fois la MEP clôturée."),
        new("demarree_le", "Démarrée le", GroupeFin, "Date et heure du Go."),
        new("terminee_le", "Clôturée le", GroupeFin, "Date et heure de la clôture."),
        new("bilan", "Bilan de clôture", GroupeFin, "Ce qui a marché, incidents et actions du bilan.", Liste: true),
        new("lien_rapport", "Lien vers le rapport de fin", GroupeFin, "Adresse du rapport de fin, une fois la MEP clôturée."),

        new("equipe", "Nom de l'équipe", GroupeEquipe, "Équipe dont le modèle est utilisé."),
        new("contact_support", "Contact support", GroupeEquipe, "Contact support de l'équipe (Paramètres › Modèles de texte)."),
        new("signature", "Signature", GroupeEquipe, "Signature de l'équipe (à défaut « L'équipe {nom} »)."),
    ];

    private static readonly string[] Jours = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];
    private static readonly string[] Mois =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    /// <summary>« mardi 20 octobre 2026 » (sans dépendre de la culture du serveur).</summary>
    public static string Jour(DateOnly d) => $"{Jours[(int)d.DayOfWeek]} {d.Day} {Mois[d.Month - 1]} {d.Year}";

    /// <summary>« 20h00 ».</summary>
    public static string Heure(TimeOnly t) => $"{t.Hour}h{t.Minute:00}";

    /// <summary>« 45 min », « 2 h », « 2 h 30 ».</summary>
    public static string Duree(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";

    private static string? Texte(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ValeurVariable V(string? texte) => new(Texte(texte), null);

    /// <summary>« BDD : clients (Tables) 2.0 », dans l'ordre des systèmes de la MEP, puis par catégorie et par nom.</summary>
    private static IEnumerable<string> ObjetsImpactes(Mep mep)
    {
        var rang = mep.Systemes.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var noms = mep.Systemes.ToDictionary(s => s.Id, s => s.Nom);
        return mep.ObjetsImpactes
            .OrderBy(o => o.SystemeId is { } sid && rang.TryGetValue(sid, out var r) ? r : int.MaxValue)
            .ThenBy(o => o.Categorie, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.Nom, StringComparer.CurrentCultureIgnoreCase)
            .Select(o =>
            {
                var texte = string.IsNullOrWhiteSpace(o.Categorie) ? o.Nom.Trim() : $"{o.Nom.Trim()} ({o.Categorie.Trim()})";
                if (!string.IsNullOrWhiteSpace(o.Version)) texte += $" {o.Version.Trim()}";
                return o.SystemeId is { } s && noms.TryGetValue(s, out var nom) ? $"{nom} : {texte}" : texte;
            });
    }

    private static ValeurVariable L(IEnumerable<string> elements)
    {
        var liste = elements.Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        return liste.Count == 0 ? ValeurVariable.Manquante : new(null, liste);
    }

    /// <summary>Lignes d'un texte libre (Markdown simple) mises bout à bout, puces retirées.</summary>
    private static string UneLigne(string texte) =>
        string.Join(" ", texte.Split('\n').Select(l => l.Trim().TrimStart('-', '*').Trim()).Where(l => l.Length > 0));

    /// <summary>
    /// Valeur de chaque variable de <see cref="Toutes"/> pour la MEP et l'équipe (null : aucune équipe, les variables d'équipe
    /// manquent). <paramref name="lienMep"/> : adresse complète de la MEP, ou null si l'adresse du serveur est inconnue.
    /// <paramref name="nomsPieces"/> : noms des pièces jointes de la MEP (preuve de validation). <paramref name="maintenant"/> :
    /// heure de la fin estimée (absente sans elle). <paramref name="echecsSurvenus"/> : incidents survenus, échecs d'étape et
    /// de recette (issue « réussie avec incidents », voir <see cref="Regles.Incidents"/> et <see cref="Regles.EtatDe"/>).
    /// </summary>
    public static Dictionary<string, ValeurVariable> Valeurs(Mep mep, Equipe? equipe, string? lienMep, IReadOnlyDictionary<Guid, string> nomsPieces,
        DateTimeOffset? maintenant = null, int echecsSurvenus = 0)
    {
        var c = mep.Communication;
        var go = mep.DemarrageAutoriseLe is { } g ? Fuseaux.Local(g, mep.FuseauHoraire) : (DateTime?)null;
        var fin = mep.ClotureeLe is { } f ? Fuseaux.Local(f, mep.FuseauHoraire) : (DateTime?)null;
        string Instant(DateTime d) => $"{Jour(DateOnly.FromDateTime(d))} à {Heure(TimeOnly.FromDateTime(d))}";
        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId);
        var indispo = mep.IndisponibiliteMinutes is { } m and > 0 ? Duree(m) : null;

        string? Validation()
        {
            if (!c.ValideeParUtilisateurs) return null;
            var texte = "validée";
            if (Texte(c.ValideePar) is { } par) texte += $" par {par}";
            if (c.ValideeLe is { } le) texte += $" le {le.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            if (c.PreuveValidationId is { } preuve && nomsPieces.TryGetValue(preuve, out var nom)) texte += $" (preuve : {nom})";
            return texte;
        }

        var bilan = mep.Bilan is { } b
            ? L(new[] { ("Ce qui a marché", b.CeQuiAMarche), ("Incidents", b.Incidents), ("Actions", b.Actions) }
                .Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => $"{x.Item1} : {UneLigne(x.Item2)}"))
            : ValeurVariable.Manquante;

        string? FinEstimee()
        {
            if (maintenant is not { } m || mep.Phase is not (Phase.Execution or Phase.RetourArriere or Phase.Recette)) return null;
            var f = Regles.EstimerFin(mep, m);
            if (f.FinEstimee is not { } fin || f.FinPrevue is not { } prevue) return null;
            var retard = f.RetardMinutes > 0 ? $", +{Duree(f.RetardMinutes)}" : "";
            return $"{Heure(TimeOnly.FromDateTime(Fuseaux.Local(fin, mep.FuseauHoraire)))} (prévue à {Heure(TimeOnly.FromDateTime(Fuseaux.Local(prevue, mep.FuseauHoraire)))}{retard})";
        }

        var valeurs = new Dictionary<string, ValeurVariable>
        {
            ["application"] = V(mep.Outil),
            ["objet"] = V(mep.Objet),
            ["type_mep"] = V(mep.Type == TypeMep.Simplifiee ? "MEP simplifiée" : "MEP complète"),
            ["version_actuelle"] = V(mep.VersionActuelle),
            ["version_cible"] = V(mep.VersionCible),
            ["impacts"] = V(UneLigne(mep.Impacts)),
            ["lien_mep"] = V(lienMep),

            ["date"] = V(c.DateAnnoncee is { } da ? Jour(da) : go is { } dg ? Jour(DateOnly.FromDateTime(dg)) : null),
            ["heure_debut"] = V(c.HeureDebutAnnoncee is { } hd ? Heure(hd) : go is { } hg ? Heure(TimeOnly.FromDateTime(hg)) : null),
            ["heure_fin"] = V(c.HeureFinAnnoncee is { } hf ? Heure(hf) : fin is { } hc ? Heure(TimeOnly.FromDateTime(hc)) : null),
            ["indisponibilite"] = V(indispo),

            ["pilote"] = V(pilote?.Nom),
            ["pilote_contact"] = V(pilote?.Contact),
            ["responsables_techniques"] = L(mep.ResponsablesTechniques
                .Select(r => (r, p: mep.Participants.FirstOrDefault(p => p.Id == r.ParticipantId)))
                .Where(x => x.p is not null)
                .Select(x => string.IsNullOrWhiteSpace(x.r.Perimetre) ? x.p!.Nom : $"{x.p!.Nom} : {x.r.Perimetre.Trim()}")),

            ["systemes"] = L(mep.Systemes.Select(s => string.IsNullOrWhiteSpace(s.Impact) ? s.Nom : $"{s.Nom} : {UneLigne(s.Impact)}")),
            ["environnements"] = L(mep.Systemes.Where(s => s.Environnements.Count > 0)
                .Select(s => $"{s.Nom} : {string.Join(", ", s.Environnements.Select(e => e.Nom))}")),
            ["touche_production"] = V(mep.Systemes.Where(s => s.Environnements.Any(e => e.Production)).Select(s => s.Nom).ToList() is { Count: > 0 } prod
                ? $"oui ({string.Join(", ", prod)})"
                : mep.Systemes.Any(s => s.Environnements.Count > 0) ? "non" : null),
            ["objets_impactes"] = L(ObjetsImpactes(mep)),
            ["sections"] = L(mep.SectionsDu(TypePlan.Deploiement).Select(s => s.Titre)),

            ["reference_demande"] = V(c.ReferenceDemande),
            ["niveau_risque"] = V(c.NiveauRisque switch
            {
                NiveauRisque.Faible => "faible",
                NiveauRisque.Modere => "modéré",
                NiveauRisque.Eleve => "élevé",
                _ => null,
            }),
            ["benefices"] = L(c.Benefices.Split('\n').Select(l => l.Trim().TrimStart('-', '*').Trim())),
            ["utilisateurs_impactes"] = V(c.UtilisateursImpactes),
            ["arret_applicatif"] = V(c.ArretApplicatif ? indispo is null ? "oui" : $"oui, pendant {indispo}" : "non"),
            ["action_utilisateurs"] = V(c.ActionUtilisateurs),
            ["heure_limite"] = V(c.HeureLimite is { } hl ? Heure(hl) : null),
            ["validation_utilisateurs"] = V(Validation()),

            ["issue"] = V(mep.Issue is null ? null : Regles.EtatDe(Phase.Cloturee, mep.Issue, echecsSurvenus) switch
            {
                EtatMep.Reussie => "réussie",
                EtatMep.AvecIncidents => "réussie avec incidents",
                EtatMep.Repliee => "clôturée après retour arrière",
                EtatMep.Echec => "échec (service dégradé)",
                _ => null,
            }),
            ["avancement"] = V(mep.Phase is Phase.Preparation or Phase.Annulee ? null : Regles.Avancement(mep)),
            ["fin_estimee"] = V(FinEstimee()),

            ["demarree_le"] = V(go is { } d1 ? Instant(d1) : null),
            ["terminee_le"] = V(fin is { } d2 ? Instant(d2) : null),
            ["bilan"] = bilan,
            ["lien_rapport"] = V(mep.Phase == Phase.Cloturee && lienMep is not null ? $"{lienMep}/rapport" : null),

            ["equipe"] = V(equipe?.Nom),
            ["contact_support"] = V(equipe?.ContactSupport),
            ["signature"] = V(equipe is null ? null : string.IsNullOrWhiteSpace(equipe.Signature) ? $"L'équipe {equipe.Nom}" : equipe.Signature),
        };
        return valeurs;
    }
}

/// <summary>Les quatre modèles de texte fournis, copiés dans chaque équipe à sa création (et pour les équipes existantes).</summary>
public static class ModelesTexteFournis
{
    public static readonly IReadOnlyList<ModeleTexteFourni> Tous =
    [
        new("annonce", "Annonce aux utilisateurs", UsageModeleTexte.Annonce,
            "[{{application}}] Mise en production le {{date}} : {{objet}}",
            """
            Bonjour,

            Une mise en production de **{{application}}** (version {{version_cible}}) aura lieu le **{{date}} de {{heure_debut}} à {{heure_fin}}**.

            **Ce qui change**
            {{benefices}}

            **Personnes concernées** : {{utilisateurs_impactes}}
            **Interruption de service** : {{arret_applicatif}}

            **Action attendue de votre part** : {{action_utilisateurs}} avant {{heure_limite}}.

            Pour toute question : {{contact_support}}.

            {{signature}}
            """),
        new("rappel", "Rappel le jour J", UsageModeleTexte.Rappel,
            "Rappel : {{application}} indisponible le {{date}} à partir de {{heure_debut}}",
            """
            Bonjour,

            Pour rappel, **{{application}}** sera indisponible le {{date}} à partir de **{{heure_debut}}** (retour prévu à {{heure_fin}}).

            **Merci de {{action_utilisateurs}} avant {{heure_limite}}.**

            Pour toute question : {{contact_support}}.

            {{signature}}
            """),
        new("demande-mep", "Demande de MEP au responsable", UsageModeleTexte.DemandeMep,
            "Demande de MEP {{reference_demande}} : {{application}} {{version_cible}}",
            """
            Bonjour,

            Nous sollicitons votre accord pour la mise en production suivante.

            **Demande** : {{reference_demande}}
            **Application** : {{application}}, de la version {{version_actuelle}} à la version {{version_cible}}
            **Date** : {{date}}, de {{heure_debut}} à {{heure_fin}} (indisponibilité prévue : {{indisponibilite}})
            **Pilote** : {{pilote}}

            **Systèmes concernés**
            {{systemes}}

            **Environnements**
            {{environnements}}

            **Production touchée** : {{touche_production}}

            **Objets impactés**
            {{objets_impactes}}

            **Niveau de risque (analyse d'impact)** : {{niveau_risque}}
            **Validation par les utilisateurs** : {{validation_utilisateurs}}

            Le détail du plan est disponible ici : {{lien_mep}}

            {{signature}}
            """),
        new("compte-rendu", "Compte rendu de fin", UsageModeleTexte.CompteRendu,
            "[{{application}}] Mise en production du {{date}} terminée",
            """
            Bonjour,

            La mise en production de **{{application}}** (version {{version_cible}}) est terminée : **{{issue}}**, le {{terminee_le}}.

            **Ce qui change pour vous**
            {{benefices}}

            **Bilan**
            {{bilan}}

            En cas d'anomalie, contactez {{contact_support}}.

            {{signature}}
            """),
    ];

    public static ModeleTexteFourni? Trouver(string? cle) => Tous.FirstOrDefault(m => m.Cle == cle);

    /// <summary>Copie d'un modèle fourni pour une équipe.</summary>
    public static ModeleTexte Copie(ModeleTexteFourni f, Guid equipeId, DateTimeOffset maintenant) => new()
    {
        EquipeId = equipeId, Nom = f.Nom, Usage = f.Usage, Objet = f.Objet, Corps = f.Corps, CleFournie = f.Cle,
        Version = 1, CreeLe = maintenant, ModifieLe = maintenant,
    };

    /// <summary>Copies manquantes pour l'équipe (aucune si elle a déjà les quatre).</summary>
    public static List<ModeleTexte> Manquants(Guid equipeId, IEnumerable<string?> clesPresentes, DateTimeOffset maintenant)
    {
        var presentes = clesPresentes.OfType<string>().ToHashSet();
        return Tous.Where(f => !presentes.Contains(f.Cle)).Select(f => Copie(f, equipeId, maintenant)).ToList();
    }

    /// <summary>Le modèle diffère-t-il du texte fourni (nom, usage, objet ou texte) ? Toujours faux pour un modèle de l'équipe.</summary>
    public static bool EstModifie(ModeleTexte m) =>
        Trouver(m.CleFournie) is { } f && (m.Nom != f.Nom || m.Usage != f.Usage || m.Objet != f.Objet || m.Corps != f.Corps);

    /// <summary>Rétablit le texte fourni (nom, usage, objet, texte).</summary>
    public static void Retablir(ModeleTexte m)
    {
        var f = Trouver(m.CleFournie) ?? throw new RegleMetierException("Ce modèle a été créé par l'équipe : il n'a pas de texte fourni à rétablir.");
        m.Nom = f.Nom;
        m.Usage = f.Usage;
        m.Objet = f.Objet;
        m.Corps = f.Corps;
    }
}
