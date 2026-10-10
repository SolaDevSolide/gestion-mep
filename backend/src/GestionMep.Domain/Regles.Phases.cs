namespace GestionMep.Domain;

// Règles métier : phases.
public static partial class Regles
{
    /// <summary>
    /// Décision Go (équivalent de PasserExecution) : réservée au pilote ou à un suppléant, plan sans anomalie
    /// (approbations comprises) et tous les critères go / no-go actifs cochés. Les critères cochés sont recopiés dans <see cref="Mep.GoNoGo"/>.
    /// </summary>
    public static void PasserEnExecution(Mep mep, Guid utilisateurId, IReadOnlyCollection<Guid> criteresCoches,
        IReadOnlyCollection<CritereGoNoGo> criteres, int approbationsMinimum, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut passer le plan en exécution.");
        if (mep.EstModele) throw new RegleMetierException("Un modèle ne passe pas en exécution : le dupliquer pour créer une MEP.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Le plan n'est pas en préparation.");
        var erreurs = ErreursPlan(mep, maintenant, approbationsMinimum);
        if (erreurs.Count > 0) throw new RegleMetierException("Plan incomplet : " + string.Join(" ", erreurs));
        var actifs = criteres.Where(c => c.Actif).OrderBy(c => c.Ordre).ToList();
        var manquants = actifs.Where(c => !criteresCoches.Contains(c.Id)).Select(c => $"« {c.Libelle} »").ToList();
        if (manquants.Count > 0) throw new RegleMetierException("Go impossible : cocher " + string.Join(", ", manquants) + ".");
        mep.Phase = Phase.Execution;
        mep.DemarrageAutoriseParId = utilisateurId;
        mep.DemarrageAutoriseLe = maintenant;
        mep.GoNoGo = actifs.Select(c => new CritereValide { Libelle = c.Libelle, ParId = utilisateurId, Le = maintenant }).ToList();
    }

    /// <summary>Décision No-go : la MEP reste en préparation ; seul le motif est tracé (journal).</summary>
    public static void NoGo(Mep mep, Guid utilisateurId, string motif)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut décider d'un no-go.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Le no-go se décide en préparation.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du no-go.");
    }

    /// <summary>Équivalent de RevenirPreparation : refusé tant qu'une action est en cours ; les états sont conservés.</summary>
    public static void RevenirEnPreparation(Mep mep, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut revenir en préparation.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Retour en préparation possible seulement depuis l'exécution.");
        if (EtapeNonRetourFranchie(mep) is { } franchie)
            throw new RegleMetierException($"Le point de non-retour {franchie.Code} est franchi : on ne peut plus revenir en préparation, tout problème se corrige en avant.");
        var enCours = mep.Etapes.FirstOrDefault(e => e.Etat == EtatEtape.EnCours);
        if (enCours is not null) throw new RegleMetierException($"{enCours.Code} est encore en cours. Attendre sa fin.");
        if (mep.Resolutions.Any(r => r.Statut == StatutResolution.EnCours))
            throw new RegleMetierException("Une résolution est en cours. Attendre sa fin.");
        mep.Phase = Phase.Preparation;
    }

    /// <summary>
    /// Bascule sur le plan de retour arrière, pendant l'exécution ou la recette. Par le pilote (utilisateurId renseigné)
    /// ou automatiquement (échéance atteinte, résolution en échec). Impossible une fois le point de non-retour franchi.
    /// </summary>
    public static void DeclencherRetourArriere(Mep mep, Guid? utilisateurId, string motif, DateTimeOffset maintenant)
    {
        if (utilisateurId is { } u && !EstPilote(mep, u))
            throw new RegleMetierException("Seul le pilote ou un suppléant peut déclencher le retour arrière.");
        if (mep.Phase is not (Phase.Execution or Phase.Recette))
            throw new RegleMetierException("Le retour arrière se déclenche pendant l'exécution ou la recette.");
        if (EtapeNonRetourFranchie(mep) is { } franchie)
            throw new RegleMetierException($"Point de non-retour franchi ({franchie.Code}) : corriger en avant.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du retour arrière.");
        mep.Phase = Phase.RetourArriere;
        mep.RetourArriereDeclencheLe = maintenant;
        mep.RetourArriereDeclenchePar = utilisateurId;
        mep.RetourArriereMotif = motif;
    }

    /// <summary>
    /// Déclenche automatiquement le retour arrière si l'échéance est atteinte avant la clôture (exécution ou recette),
    /// sauf si le point de non-retour est franchi.
    /// </summary>
    public static bool AppliquerEcheance(Mep mep, DateTimeOffset maintenant)
    {
        if (mep.Phase is not (Phase.Execution or Phase.Recette) || mep.EcheanceRepli is null || maintenant < mep.EcheanceRepli) return false;
        if (NonRetourFranchi(mep)) return false;
        DeclencherRetourArriere(mep, null, MotifEcheance, maintenant);
        return true;
    }

    /// <summary>Fin du déploiement : le pilote passe la MEP en recette quand tout le déploiement est réussi.</summary>
    public static void PasserEnRecette(Mep mep, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut passer la MEP en recette.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le passage en recette se fait depuis l'exécution.");
        var restant = mep.EtapesDu(TypePlan.Deploiement).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
        if (restant is not null) throw new RegleMetierException($"{restant.Code} n'est pas réussie : recette impossible.");
        mep.Phase = Phase.Recette;
    }

    /// <summary>
    /// Clôture par le pilote : MEP réussie depuis la recette quand toutes les étapes de test sont conformes,
    /// ou service rétabli quand tout le retour arrière est réussi. Si le bilan est activé, il est obligatoire.
    /// </summary>
    public static void Cloturer(Mep mep, Guid utilisateurId, DateTimeOffset maintenant, Bilan? bilan = null)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut clôturer la MEP.");
        IssueMep issue;
        switch (mep.Phase)
        {
            case Phase.Execution:
                throw new RegleMetierException("Passer d'abord en recette : la clôture réussie se fait après les tests de recette.");
            case Phase.Recette:
                foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
                {
                    var test = f.Etapes.OrderBy(t => t.Ordre).FirstOrDefault(t => t.Resultat != ResultatControle.Conforme);
                    if (test is not null)
                        throw new RegleMetierException($"{NomRecette(f)}, étape {test.Ordre} non conforme : clôture impossible.");
                }
                issue = IssueMep.Reussie;
                break;
            case Phase.RetourArriere:
                var repli = mep.EtapesDu(TypePlan.RetourArriere).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
                if (repli is not null)
                    throw new RegleMetierException($"{repli.Code} n'est pas réussie : le service n'est pas rétabli. Suivre « Si le repli échoue ».");
                issue = IssueMep.Repliee;
                break;
            default:
                throw new RegleMetierException("Seule une MEP en recette ou en retour arrière peut être clôturée.");
        }
        var ceQuiAMarche = (bilan?.CeQuiAMarche ?? "").Trim();
        var incidents = (bilan?.Incidents ?? "").Trim();
        if (mep.BilanActive && (ceQuiAMarche.Length == 0 || incidents.Length == 0))
            throw new RegleMetierException("Bilan : décrire ce qui a marché et les incidents (« Rien à signaler » accepté).");
        if (bilan is not null && (ceQuiAMarche.Length > 0 || incidents.Length > 0 || !string.IsNullOrWhiteSpace(bilan.Actions)))
            mep.Bilan = new Bilan
            {
                CeQuiAMarche = ceQuiAMarche, Incidents = incidents, Actions = (bilan.Actions ?? "").Trim(),
                RedigeParId = utilisateurId, Le = maintenant,
            };
        mep.Issue = issue;
        mep.Phase = Phase.Cloturee;
        mep.ClotureeLe = maintenant;
        mep.ClotureeParId = utilisateurId;
    }
}
