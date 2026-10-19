namespace GestionMep.Domain;

// Règles métier : sorties du cycle de vie (#93) — annuler, reporter, corriger en avant, passer le pilotage, supprimer.
public static partial class Regles
{
    /// <summary>
    /// Annulation d'une MEP, en préparation seulement (après un Go, on revient d'abord en préparation) : par le pilote, un
    /// suppléant ou un administrateur. Motif obligatoire ; si la MEP a déjà eu un Go, l'état laissé en production l'est aussi.
    /// La MEP passe en lecture seule (<see cref="Phase.Annulee"/>) et reste duplicable ; son report éventuel est levé.
    /// </summary>
    public static void Annuler(Mep mep, Guid utilisateurId, bool estAdmin, string motif, string? etatProduction, DateTimeOffset maintenant)
    {
        if (!estAdmin && !EstPilote(mep, utilisateurId))
            throw new RegleMetierException("Seul le pilote, un suppléant ou un administrateur peut annuler la MEP.");
        if (mep.EstModele) throw new RegleMetierException("Un modèle ne s'annule pas : un administrateur peut le supprimer.");
        switch (mep.Phase)
        {
            case Phase.Annulee: throw new RegleMetierException("La MEP est déjà annulée.");
            case Phase.Cloturee: throw new RegleMetierException("La MEP est clôturée : elle ne s'annule plus.");
            case Phase.Preparation: break;
            default: throw new RegleMetierException("Revenir d'abord en préparation : une MEP s'annule depuis la préparation.");
        }
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif de l'annulation.");
        var aEuUnGo = ADejaEuUnGo(mep);
        if (aEuUnGo && string.IsNullOrWhiteSpace(etatProduction))
            throw new RegleMetierException("La MEP a déjà eu un Go : décrire l'état laissé en production.");
        mep.Annulation = new Annulation
        {
            Motif = motif.Trim(), EtatProduction = (etatProduction ?? "").Trim(), ParId = utilisateurId, Le = maintenant,
        };
        mep.Report = null;
        mep.Phase = Phase.Annulee;
    }

    /// <summary>Détail du journal d'une annulation : « MEP annulée : motif. État laissé en production : … ».</summary>
    public static string DetailAnnulation(Annulation a) =>
        $"MEP annulée : {a.Motif}" + (a.EtatProduction.Length > 0 ? $". État laissé en production : {a.EtatProduction}" : "");

    /// <summary>Détail du journal d'un report : « MEP reportée au 05/11/2026 : motif » ou « MEP reportée sans date : motif ».</summary>
    public static string DetailReport(Report r) =>
        (r.DateVisee is { } d ? $"MEP reportée au {d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}" : "MEP reportée sans date")
        + $" : {r.Motif}";

    /// <summary>La MEP a déjà eu un Go (même suivi d'un retour en préparation).</summary>
    public static bool ADejaEuUnGo(Mep mep) => mep.PremierGoLe is not null || mep.DemarrageAutoriseLe is not null;

    /// <summary>Étapes de déploiement exécutées en production (terminées ou démarrées), dans l'ordre du plan.</summary>
    public static List<Etape> EtapesExecutees(Mep mep) =>
        [.. mep.EtapesDu(TypePlan.Deploiement).Where(e => e.Etat != EtatEtape.AFaire || e.DebutReel is not null)];

    /// <summary>
    /// Report d'une MEP en préparation, par le pilote ou un suppléant : motif obligatoire, date visée facultative. La MEP reste
    /// en préparation (plan modifiable, approbations gardées) ; un nouveau report remplace le précédent.
    /// </summary>
    public static void Reporter(Mep mep, Guid utilisateurId, string motif, DateOnly? dateVisee, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut reporter la MEP.");
        if (mep.EstModele) throw new RegleMetierException("Un modèle ne se reporte pas.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Une MEP se reporte en préparation.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du report.");
        mep.Report = new Report { Motif = motif.Trim(), DateVisee = dateVisee, ParId = utilisateurId, Le = maintenant };
    }

    /// <summary>« Reprendre la préparation » : le pilote ou un suppléant lève le report (le Go le lève aussi).</summary>
    public static void ReprendrePreparation(Mep mep, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut reprendre la préparation.");
        if (mep.Phase != Phase.Preparation || mep.Report is null) throw new RegleMetierException("La MEP n'est pas reportée.");
        mep.Report = null;
    }

    /// <summary>
    /// Raison pour laquelle le retour arrière est impossible (pas de plan de retour arrière, point de non-retour franchi), ou
    /// null s'il est encore possible.
    /// </summary>
    public static string? RetourArriereImpossible(Mep mep) =>
        !mep.RetourArriereActive ? "Cette MEP n'a pas de plan de retour arrière."
        : EtapeNonRetourFranchie(mep) is { } franchie ? $"Point de non-retour franchi ({franchie.Code} {franchie.Action})."
        : null;

    /// <summary>
    /// « Corriger en avant » : retour de la recette à l'exécution, par le pilote ou un suppléant, motif obligatoire. Les
    /// résultats de test en échec redeviennent « à faire » (l'historique reste au journal). Renvoie les tests remis à faire
    /// (« Fiche, étape n »).
    /// </summary>
    public static List<string> RevenirEnExecution(Mep mep, Guid utilisateurId, string motif)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut revenir en exécution.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Le retour en exécution se fait depuis la recette.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du retour en exécution.");
        var remis = new List<string>();
        foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
            foreach (var t in f.Etapes.OrderBy(t => t.Ordre).Where(t => t.Resultat == ResultatControle.Echec))
            {
                remis.Add($"{NomRecette(f)}, étape {t.Ordre}");
                t.Resultat = null;
                t.ParId = null;
                t.Le = null;
            }
        // Les fiches sont stockées en JSON : nouvelle liste pour que la modification soit vue.
        mep.FichesRecette = [.. mep.FichesRecette];
        mep.Phase = Phase.Execution;
        return remis;
    }

    /// <summary>
    /// Étape corrective ajoutée pendant l'exécution, après le Go, par le pilote ou un suppléant : déploiement seulement, à la suite
    /// du plan (elle attend les dernières étapes), code suivant, marquée <see cref="Etape.AjouteeApresGo"/>, sur les environnements
    /// de production de ses systèmes (à défaut, tous ceux de la MEP). Sa fiche doit être complète : elle ne se modifie plus ensuite.
    /// </summary>
    public static void AjouterEtapeCorrective(Mep mep, Etape etape, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut ajouter une étape corrective.");
        if (mep.Phase != Phase.Execution)
            throw new RegleMetierException("Une étape corrective s'ajoute pendant l'exécution (depuis la recette : revenir d'abord en exécution).");
        if (string.IsNullOrWhiteSpace(etape.Action) || etape.SystemeIds.Count == 0)
            throw new RegleMetierException("Étape corrective : indiquer l'action et au moins un système.");
        if (string.IsNullOrWhiteSpace(etape.Instructions) || string.IsNullOrWhiteSpace(etape.ResultatAttendu))
            throw new RegleMetierException("Étape corrective : indiquer les instructions et le résultat attendu.");
        etape.Plan = TypePlan.Deploiement;
        etape.AjouteeApresGo = true;
        // Après le Go, on corrige là où la MEP agit : les environnements de production de chaque système (à défaut, tous ceux
        // de la MEP sur ce système).
        if (etape.Environnements.Count == 0)
            etape.Environnements = [.. etape.SystemeIds.SelectMany(id => mep.Systemes.Where(s => s.Id == id)).SelectMany(s =>
            {
                var envs = s.Environnements.Where(e => e.Production).ToList();
                return (envs.Count > 0 ? envs : s.Environnements).Select(e => new EnvironnementEtape { SystemeId = s.Id, Nom = e.Nom });
            })];
        etape.PointDeNonRetour = false;
        etape.Code = ProchainCode(mep.Etapes, TypePlan.Deploiement);
        var deploiement = mep.EtapesDu(TypePlan.Deploiement).Where(e => !e.APlacer).ToList();
        // Dernières étapes du plan : celles qu'aucune autre n'attend.
        var dernieres = deploiement.Where(e => deploiement.All(x => !x.Dependances.Contains(e.Id))).Select(e => e.Id).ToList();
        Operer(mep, TypePlan.Deploiement, () =>
        {
            etape.SectionId = null;
            etape.Dependances = dernieres;
            etape.DependancesDerivees = [];
            etape.PortsAttendus = [];
            mep.Etapes.Add(etape);
        }, sauf: etape, apresGo: true);
    }

    /// <summary>Phases où le pilotage se passe (et où l'on ajoute une personne) hors préparation : de l'exécution au retour arrière.</summary>
    public static bool EstApresGoEnCours(Mep mep) => mep.Phase is Phase.Execution or Phase.Recette or Phase.RetourArriere;

    /// <summary>
    /// Changement de pilote après le Go (de l'exécution au retour arrière), motif obligatoire. Passation : par le pilote ou un
    /// suppléant. Secours : par un administrateur qui n'est ni pilote ni suppléant (<paramref name="secours"/> vrai) ; il est
    /// tracé (<see cref="Mep.PilotageSecours"/>) et affiché à tous jusqu'à la clôture. L'ancien pilote devient suppléant si
    /// <paramref name="ancienPiloteSuppleant"/> ; <paramref name="suppleantsGardes"/> (null : tous) dit quels suppléants restent.
    /// Une MEP simplifiée n'a que des suppléants implicites : seul le pilote change.
    /// </summary>
    public static void ChangerPilote(Mep mep, Guid utilisateurId, bool estAdmin, Participant nouveau, bool ancienPiloteSuppleant,
        IReadOnlyCollection<Guid>? suppleantsGardes, string motif, DateTimeOffset maintenant, out bool secours)
    {
        var pilote = EstPilote(mep, utilisateurId);
        if (!pilote && !estAdmin)
            throw new RegleMetierException("Seul le pilote, un suppléant ou un administrateur (pilotage de secours) peut changer le pilote.");
        if (mep.Phase == Phase.Preparation)
            throw new RegleMetierException("En préparation, le pilote se change dans la section Personnes.");
        if (!EstApresGoEnCours(mep)) throw new RegleMetierException("La MEP est terminée : son pilote ne change plus.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du changement de pilote.");
        if (nouveau.UtilisateurId is null) throw new RegleMetierException($"{nouveau.Nom} n'est relié à aucun compte : il ne pourrait pas piloter.");
        if (nouveau.Id == mep.PiloteId) throw new RegleMetierException($"{nouveau.Nom} est déjà le pilote.");
        secours = !pilote;
        var ancien = mep.PiloteId;
        if (!EstSimplifiee(mep))
        {
            var gardes = mep.SuppleantIds.Where(s => s != nouveau.Id && (suppleantsGardes is null || suppleantsGardes.Contains(s))).ToList();
            if (ancienPiloteSuppleant && ancien is { } a && !gardes.Contains(a)) gardes.Insert(0, a);
            mep.SuppleantIds = gardes;
        }
        mep.PiloteId = nouveau.Id;
        mep.PilotageSecours = secours
            ? new PilotageSecours { ParId = utilisateurId, Le = maintenant, Motif = motif.Trim(), AncienPiloteId = ancien, NouveauPiloteId = nouveau.Id }
            : null;
    }

    /// <summary>Nom complet d'une MEP, à saisir pour confirmer sa suppression : outil, puis version cible s'il y en a une.</summary>
    public static string NomComplet(Mep mep) =>
        string.IsNullOrWhiteSpace(mep.VersionCible) ? mep.Outil.Trim() : $"{mep.Outil.Trim()} {mep.VersionCible.Trim()}";

    /// <summary>
    /// Suppression définitive (administrateur seulement, contrôlé par l'API) : réservée aux modèles et aux MEP jamais lancées
    /// (aucun Go, même suivi d'un retour en préparation) ; motif obligatoire et nom de la MEP saisi pour confirmer.
    /// </summary>
    public static void VerifierSuppression(Mep mep, string motif, string nomSaisi)
    {
        if (ADejaEuUnGo(mep))
            throw new RegleMetierException("Cette MEP a déjà eu un Go : elle ne se supprime pas. L'annuler pour garder sa trace.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif de la suppression.");
        if (!string.Equals(nomSaisi.Trim(), NomComplet(mep), StringComparison.Ordinal))
            throw new RegleMetierException($"Pour confirmer, saisir exactement le nom de la MEP : {NomComplet(mep)}.");
    }
}
