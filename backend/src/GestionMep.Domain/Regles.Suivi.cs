namespace GestionMep.Domain;

/// <summary>
/// Suivi d'une MEP lancée (#61) : avancement, fin estimée et retard. Mêmes calculs que la vue
/// d'ensemble et le tableau de bord du navigateur (<c>frontend/src/mep/ensemble/calculs.ts</c>) ; ici pour les variables des
/// modèles de texte et le rapport de fin.
/// </summary>
public static partial class Regles
{
    /// <summary>Temps ajouté pour une étape en échec : sa résolution, avant de la refaire (durée estimée).</summary>
    public const int SurcoutEchecMinutes = 10;

    /// <summary>Temps restant compté pour une étape en cours qui dépasse déjà sa durée estimée.</summary>
    public const int SurcoutDepassementMinutes = 5;

    /// <summary>
    /// Fin prévue et fin estimée du plan suivi (déploiement, ou retour arrière une fois déclenché). <c>Depart</c> : Go, ou
    /// déclenchement du retour arrière ; null avant le Go (ni fin prévue ni fin estimée). <c>SansDuree</c> : aucune durée estimée
    /// (MEP simplifiée), donc ni fin estimée ni retard. <c>Terminee</c> : toutes les étapes du plan réussies, la fin estimée est la
    /// fin réelle.
    /// </summary>
    public sealed record EstimationFin(TypePlan Plan, DateTimeOffset? Depart, int PrevuMinutes, DateTimeOffset? FinPrevue,
        DateTimeOffset? FinEstimee, int RetardMinutes, bool Terminee, bool SansDuree);

    /// <summary>Plan suivi par la vue d'ensemble : le retour arrière dès qu'il est déclenché, sinon le déploiement.</summary>
    public static TypePlan PlanSuivi(Mep mep) => RetourArriereEffectue(mep) ? TypePlan.RetourArriere : TypePlan.Deploiement;

    /// <summary>
    /// Le retour arrière a été déclenché et la MEP n'en est pas sortie : en cours, clôturée repliée, ou clôturée en échec après
    /// un retour arrière.
    /// </summary>
    public static bool RetourArriereEffectue(Mep mep) =>
        mep.Phase == Phase.RetourArriere
        || (mep.Phase == Phase.Cloturee && (mep.Issue == IssueMep.Repliee || (mep.Issue == IssueMep.Echec && mep.RetourArriereDeclencheLe is not null)));

    /// <summary>
    /// Plus long chemin, en minutes, à travers les préalables (<see cref="Etape.Dependances"/>) : ce qui fixe la fin d'un plan dont
    /// les étapes indépendantes avancent en parallèle. Une boucle (impossible dans un plan valide) ne fait pas boucler le calcul.
    /// </summary>
    public static double CheminCritique(IReadOnlyCollection<Etape> etapes, Func<Etape, double> duree)
    {
        var parId = etapes.ToDictionary(e => e.Id);
        var fins = new Dictionary<Guid, double>();
        double Fin(Etape e)
        {
            if (fins.TryGetValue(e.Id, out var f)) return f;
            fins[e.Id] = 0;
            var avant = e.Dependances.Select(d => parId.GetValueOrDefault(d)).OfType<Etape>().Select(Fin).DefaultIfEmpty(0).Max();
            return fins[e.Id] = avant + duree(e);
        }
        return etapes.Select(Fin).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Temps qu'il reste à une étape, en minutes : rien si elle est réussie ; en cours, le reste de sa durée estimée, ou
    /// <see cref="SurcoutDepassementMinutes"/> si elle la dépasse déjà ; en échec, sa durée plus <see cref="SurcoutEchecMinutes"/> ;
    /// à faire, sa durée estimée.
    /// </summary>
    public static double TempsRestant(Etape e, DateTimeOffset maintenant)
    {
        var duree = e.DureeEstimeeMinutes ?? 0;
        return e.Etat switch
        {
            EtatEtape.Reussie => 0,
            EtatEtape.EnCours => (maintenant - (e.DebutReel ?? maintenant)).TotalMinutes is var ecoule && ecoule > duree
                ? SurcoutDepassementMinutes
                : duree - ecoule,
            EtatEtape.Echec => duree + SurcoutEchecMinutes,
            _ => duree,
        };
    }

    public static EstimationFin EstimerFin(Mep mep, DateTimeOffset maintenant)
    {
        var plan = PlanSuivi(mep);
        var etapes = mep.EtapesDu(plan).ToList();
        var depart = plan == TypePlan.RetourArriere ? mep.RetourArriereDeclencheLe : mep.DemarrageAutoriseLe;
        var prevu = (int)Math.Round(CheminCritique(etapes, e => e.DureeEstimeeMinutes ?? 0));
        var terminee = etapes.Count > 0 && etapes.All(e => e.Etat == EtatEtape.Reussie);
        var sansDuree = prevu == 0;
        if (depart is not { } d) return new(plan, null, prevu, null, null, 0, terminee, sansDuree);
        var finPrevue = d.AddMinutes(prevu);
        var finEstimee = terminee
            ? etapes.Max(e => e.FinReelle ?? d)
            : maintenant.AddMinutes(CheminCritique(etapes, e => TempsRestant(e, maintenant)));
        var retard = sansDuree ? 0 : (int)Math.Round((finEstimee - finPrevue).TotalMinutes);
        return new(plan, d, prevu, sansDuree ? null : finPrevue, sansDuree ? null : finEstimee, retard, terminee, sansDuree);
    }

    /// <summary>« 4/14 étapes faites (29 %) » : avancement du plan suivi (étapes réussies).</summary>
    public static string Avancement(Mep mep)
    {
        var etapes = mep.EtapesDu(PlanSuivi(mep)).ToList();
        var faites = etapes.Count(e => e.Etat == EtatEtape.Reussie);
        var pourcent = etapes.Count == 0 ? 0 : (int)Math.Round(100.0 * faites / etapes.Count, MidpointRounding.AwayFromZero);
        var s = etapes.Count > 1 ? "s" : "";
        return $"{faites}/{etapes.Count} étape{s} réussie{s} ({pourcent} %)";
    }
}
