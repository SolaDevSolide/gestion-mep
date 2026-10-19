using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Plan de retour arrière facultatif et échéance facultative (issue #49).</summary>
public class RetourArriereFacultatifTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);

    private static void Go(Mep m)
    {
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
    }

    /// <summary>MEP de démonstration sans plan de retour arrière : ni étapes de retour arrière, ni « quand le déclencher », ni échéance.</summary>
    private static DonneesDemo.Jeu SansRetourArriere()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = j.Mep;
        m.RetourArriereActive = false;
        m.EcheanceRepli = null;
        m.PlanRepli.QuandDeclencher = "";
        // Une étape de retour arrière incomplète ne compte plus : elle est gardée mais ignorée.
        E(m, "R0001").Instructions = "";
        return j;
    }

    [Fact]
    public void Active_par_defaut()
    {
        Assert.True(new Mep().RetourArriereActive);
        Assert.True(DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep.RetourArriereActive);
    }

    [Fact]
    public void Sans_retour_arriere_ni_echeance_le_go_est_possible()
    {
        var m = SansRetourArriere().Mep;
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
        Go(m);
        Assert.Equal(Phase.Execution, m.Phase);
        // Les étapes de retour arrière sont gardées (elles reviennent si on réactive le plan).
        Assert.NotEmpty(m.EtapesDu(TypePlan.RetourArriere));
    }

    [Fact]
    public void Retour_arriere_active_l_echeance_est_facultative_mais_le_plan_reste_exige()
    {
        var m = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        m.EcheanceRepli = null;
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));

        m.EcheanceRepli = Maintenant.AddHours(-1);
        Assert.Contains("L'échéance de retour arrière est passée : la reporter ou la retirer.", Regles.ErreursContenu(m, Maintenant));

        m.EcheanceRepli = null;
        foreach (var r in m.EtapesDu(TypePlan.RetourArriere).ToList()) m.Etapes.Remove(r);
        m.PlanRepli.QuandDeclencher = "";
        var erreurs = Regles.ErreursContenu(m, Maintenant);
        Assert.Contains("Ajouter au moins une étape de retour arrière.", erreurs);
        Assert.Contains("Retour arrière : préciser quand le déclencher.", erreurs);

        // Désactivé : plus rien n'est réclamé, même une échéance passée (elle ne s'applique pas).
        m.RetourArriereActive = false;
        m.EcheanceRepli = Maintenant.AddHours(-1);
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
    }

    [Fact]
    public void Sans_echeance_ni_blocage_ni_declenchement_automatique()
    {
        var m = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        m.EcheanceRepli = null;
        Go(m);
        var bienPlusTard = Maintenant.AddDays(30);
        Assert.False(Regles.AppliquerEcheance(m, bienPlusTard));
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "E0004"), bienPlusTard).Statut);
        Regles.Demarrer(m, E(m, "E0004"), Alice, bienPlusTard);
        Regles.Terminer(m, E(m, "E0004"), EtatEtape.Reussie, "OK", true, Alice, bienPlusTard);
        Regles.AutoriserReprise(m, E(m, "E0005"), Alice, bienPlusTard);
        // Le pilote peut toujours déclencher le retour arrière à la main.
        Regles.DeclencherRetourArriere(m, Alice, "Incident", bienPlusTard);
        Assert.Equal(Phase.RetourArriere, m.Phase);
    }

    [Fact]
    public void Desactive_aucun_declenchement_ni_automatique_ni_manuel()
    {
        var m = SansRetourArriere().Mep;
        m.EcheanceRepli = Maintenant.AddHours(1); // échéance gardée d'avant la désactivation : ignorée
        Go(m);
        var apres = Maintenant.AddHours(2);
        Assert.False(Regles.AppliquerEcheance(m, apres));
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "E0004"), apres).Statut);
        var ex = Assert.ThrowsAny<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, Alice, "Incident", apres));
        Assert.Equal("Cette MEP n'a pas de plan de retour arrière : tout problème se corrige en avant.", ex.Message);
        Assert.ThrowsAny<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, null, Regles.MotifEcheance, apres));
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.False(Regles.RetourArrierePossible(m));
    }

    [Fact]
    public void Desactive_une_resolution_en_echec_laisse_l_etape_en_echec()
    {
        var j = SansRetourArriere();
        var m = j.Mep;
        Go(m);
        var exec = Regles.DemarrerResolution(m, E(m, "E0005"), j.Resolutions[0], Bob, Maintenant);
        Regles.ValiderCorrection(m, exec, 1, ResultatControle.Echec, "toujours aucune réponse", Bob, Maintenant);
        Assert.Equal(StatutResolution.Echouee, exec.Statut);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(EtatEtape.Echec, E(m, "E0005").Etat);
        Assert.Null(m.RetourArriereDeclencheLe);
        Regles.AutoriserReprise(m, E(m, "E0005"), Alice, Maintenant);
        Assert.Equal(EtatEtape.AFaire, E(m, "E0005").Etat);
    }

    [Fact]
    public void La_duplication_garde_le_choix_du_modele()
    {
        var source = SansRetourArriere().Mep;
        var copie = Regles.Dupliquer(source, commeModele: false, Dave, Maintenant);
        Assert.False(copie.RetourArriereActive);
        Assert.Equal(source.EtapesDu(TypePlan.RetourArriere).Count(), copie.EtapesDu(TypePlan.RetourArriere).Count());
        Assert.True(Regles.Dupliquer(DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep, commeModele: true, Dave, Maintenant).RetourArriereActive);
    }
}
