using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du plan, dont le « parcours de test » de l'onglet Préparation de l'Excel (lignes 54 à 57).</summary>
public class ReglesTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Disponibilite Dispo(Mep m, string code) => Regles.Disponibilite(E(m, code), m.Etapes, m.EcheanceRepli, Maintenant);

    [Fact]
    public void Demo_disponibilites_initiales_identiques_a_l_excel()
    {
        var m = Demo();
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0001").Statut);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0002").Statut);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0003").Statut);
        Assert.Equal("Attend E0002", Dispo(m, "E0004").Libelle);
        Assert.Equal(StatutDisponibilite.ArretResolution, Dispo(m, "E0005").Statut);
    }

    [Fact]
    public void Demo_plan_valide()
    {
        Assert.Empty(Regles.ErreursPlan(Demo(), Maintenant));
    }

    [Fact]
    public void Parcours_E0004_disponible_seulement_apres_E0002_et_E0003()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);

        Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant);
        Regles.Terminer(m, E(m, "E0002"), EtatEtape.Reussie, "API démo OK", true, Bob, Maintenant);
        Assert.Equal("Attend E0003", Dispo(m, "E0004").Libelle);

        Regles.Demarrer(m, E(m, "E0003"), Claire, Maintenant);
        Regles.Terminer(m, E(m, "E0003"), EtatEtape.Reussie, "WEB démo OK", true, Claire, Maintenant);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0004").Statut);
    }

    [Fact]
    public void Seul_le_responsable_execute_son_action()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0002"), Claire, Maintenant));
        Assert.Contains("responsable", ex.Message);
    }

    [Fact]
    public void Aucune_execution_en_preparation()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant));
    }

    [Fact]
    public void Resultat_exige_demarrage_preuve_et_confirmation()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);
        var e2 = E(m, "E0002");
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e2, EtatEtape.Reussie, "ok", true, Bob, Maintenant));
        Regles.Demarrer(m, e2, Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e2, EtatEtape.Reussie, "  ", true, Bob, Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e2, EtatEtape.Reussie, "ok", false, Bob, Maintenant));
        Regles.Terminer(m, e2, EtatEtape.Echec, "erreur 500", false, Bob, Maintenant);
        Assert.Equal(EtatEtape.Echec, e2.Etat);
        Assert.Equal(Maintenant, e2.FinReelle);
    }

    [Fact]
    public void Reprise_apres_echec_par_le_pilote_uniquement()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);
        var e5 = E(m, "E0005");
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, e5, Bob, Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserReprise(m, e5, Claire, Maintenant));
        Regles.AutoriserReprise(m, e5, Alice, Maintenant);
        Regles.Demarrer(m, e5, Bob, Maintenant);
        Assert.Equal(EtatEtape.EnCours, e5.Etat);
    }

    [Fact]
    public void Echeance_depassee_impose_le_retour_arriere()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);
        Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant);
        var apres = m.EcheanceRepli!.Value.AddMinutes(1);
        Assert.Equal(StatutDisponibilite.RetourArriere,
            Regles.Disponibilite(E(m, "E0003"), m.Etapes, m.EcheanceRepli, apres).Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, E(m, "E0002"), EtatEtape.Reussie, "ok", true, Bob, apres));
        // Un échec reste déclarable après l'échéance.
        Regles.Terminer(m, E(m, "E0002"), EtatEtape.Echec, "trop tard", false, Bob, apres);
    }

    [Fact]
    public void Passage_en_execution_reserve_au_pilote_et_plan_complet()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Claire, Maintenant));
        E(m, "E0003").Instructions = "";
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, Maintenant));
        Assert.Contains("E0003 : compléter Instructions exactes", ex.Message);
        Assert.Equal(Phase.Preparation, m.Phase);
    }

    [Fact]
    public void Retour_en_preparation_refuse_si_action_en_cours()
    {
        var m = Demo();
        Regles.PasserEnExecution(m, Alice, Maintenant);
        Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.RevenirEnPreparation(m, Alice));
        Regles.Terminer(m, E(m, "E0002"), EtatEtape.Reussie, "ok", true, Bob, Maintenant);
        Regles.RevenirEnPreparation(m, Alice);
        Assert.Equal(EtatEtape.Reussie, E(m, "E0002").Etat);
    }

    [Fact]
    public void Cycle_refuse_et_dependances_inchangees()
    {
        var m = Demo();
        var e1 = E(m, "E0001");
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, e1, [E(m, "E0004").Id]));
        Assert.Equal("Cycle de dépendances", ex.Message);
        Assert.Empty(e1.Dependances);
        Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, e1, [e1.Id]));
    }

    [Fact]
    public void Suppression_refusee_si_une_etape_en_depend()
    {
        var m = Demo();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppression(m, E(m, "E0002")));
        Assert.Contains("E0004 dépend de E0002", ex.Message);
        Regles.VerifierSuppression(m, E(m, "E0004"));
    }

    [Fact]
    public void Codes_jamais_reutilises()
    {
        var m = Demo();
        m.Etapes.Remove(E(m, "E0005"));
        Assert.Equal("E0005", Regles.ProchainCode(m.Etapes));
        m.Etapes.Add(new Etape { Code = "E0042" });
        Assert.Equal("E0043", Regles.ProchainCode(m.Etapes));
    }

    [Fact]
    public void Disponibilite_signale_champs_manquants_dans_l_ordre_de_l_excel()
    {
        var m = Demo();
        var e2 = E(m, "E0002");
        e2.Instructions = "";
        Assert.Equal(StatutDisponibilite.InstructionsACompleter, Dispo(m, "E0002").Statut);
        e2.ResponsableId = null;
        Assert.Equal(StatutDisponibilite.FicheACompleter, Dispo(m, "E0002").Statut);
        m.EcheanceRepli = null;
        Assert.Equal(StatutDisponibilite.EcheanceARenseigner, Dispo(m, "E0002").Statut);
    }
}
