using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du plan, dont le « parcours de test » de l'onglet Préparation de l'Excel (lignes 54 à 57).</summary>
public class ReglesTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Disponibilite Dispo(Mep m, string code) => Regles.Disponibilite(m, E(m, code), Maintenant);

    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();
    private static List<Guid> TousCoches => Criteres.Select(c => c.Id).ToList();

    private static void PasserEnExecution(Mep m, Guid utilisateur, IReadOnlyCollection<Guid>? coches = null) =>
        Regles.PasserEnExecution(m, utilisateur, coches ?? TousCoches, Criteres, DonneesDemo.ApprobationsMinimum, Maintenant);

    /// <summary>Claire approuve, puis Alice décide le Go avec tous les critères cochés.</summary>
    private static void Go(Mep m)
    {
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        PasserEnExecution(m, Alice);
    }

    private static Bilan BilanOk => new() { CeQuiAMarche = "Tout", Incidents = "Rien à signaler" };

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
    public void Demo_plan_valide_en_attente_de_l_approbation_de_claire()
    {
        var m = Demo();
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
        Assert.Equal(["Il faut 1 approbation(s)."], Regles.ErreursPlan(m, Maintenant, 1));
        Assert.Empty(Regles.ErreursPlan(m, Maintenant, 0));
    }

    [Fact]
    public void Parcours_E0004_disponible_seulement_apres_E0002_et_E0003()
    {
        var m = Demo();
        Go(m);

        Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant);
        Regles.Terminer(m, E(m, "E0002"), EtatEtape.Reussie, "API démo OK", true, Bob, Maintenant);
        Assert.Equal("Attend E0003", Dispo(m, "E0004").Libelle);

        Regles.Demarrer(m, E(m, "E0003"), Claire, Maintenant);
        Regles.Terminer(m, E(m, "E0003"), EtatEtape.Reussie, "WEB démo OK", true, Claire, Maintenant);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0004").Statut);
    }

    [Fact]
    public void Seul_l_executant_execute_son_action()
    {
        var m = Demo();
        Go(m);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0002"), Claire, Maintenant));
        Assert.Contains("Prendre la main", ex.Message);
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
        Go(m);
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
        Go(m);
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
        Go(m);
        Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant);
        var apres = m.EcheanceRepli!.Value.AddMinutes(1);
        Assert.Equal(StatutDisponibilite.RetourArriere, Regles.Disponibilite(m, E(m, "E0003"), apres).Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, E(m, "E0002"), EtatEtape.Reussie, "ok", true, Bob, apres));
        // Un échec reste déclarable après l'échéance.
        Regles.Terminer(m, E(m, "E0002"), EtatEtape.Echec, "trop tard", false, Bob, apres);
    }

    [Fact]
    public void Passage_en_execution_reserve_au_pilote_et_plan_complet()
    {
        var m = Demo();
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Assert.Throws<RegleMetierException>(() => PasserEnExecution(m, Claire));
        E(m, "E0003").Instructions = "";
        var ex = Assert.Throws<RegleMetierException>(() => PasserEnExecution(m, Alice));
        Assert.Contains("E0003 : compléter Instructions exactes", ex.Message);
        Assert.Equal(Phase.Preparation, m.Phase);
    }

    [Fact]
    public void Retour_en_preparation_refuse_si_action_en_cours()
    {
        var m = Demo();
        Go(m);
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
        Assert.Equal("R0003", Regles.ProchainCode(m.Etapes, TypePlan.RetourArriere));
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

    // ---------- Jalon 2 ----------

    private static ResolutionType Fix(DonneesDemo.Jeu j) => j.Resolutions[0];

    [Fact]
    public void Plan_de_retour_arriere_obligatoire()
    {
        var m = Demo();
        foreach (var r in m.EtapesDu(TypePlan.RetourArriere).ToList()) m.Etapes.Remove(r);
        m.PlanRepli.QuandDeclencher = "";
        var erreurs = Regles.ErreursPlan(m, Maintenant, 1);
        Assert.Contains("Ajouter au moins une étape de retour arrière.", erreurs);
        Assert.Contains("Retour arrière : préciser quand le déclencher.", erreurs);
    }

    [Fact]
    public void Etapes_de_repli_en_attente_tant_que_non_declenche()
    {
        var m = Demo();
        Go(m);
        Assert.Equal(StatutDisponibilite.SiRetourArriere, Dispo(m, "R0001").Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "R0001"), Bob, Maintenant));
    }

    [Fact]
    public void Retour_arriere_declenche_par_le_pilote_arrete_le_deploiement()
    {
        var m = Demo();
        Go(m);
        Assert.Throws<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, Claire, "test", Maintenant));
        Regles.DeclencherRetourArriere(m, Alice, "Incident bloquant", Maintenant);
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Equal(StatutDisponibilite.RetourArriere, Dispo(m, "E0002").Statut);
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0001").Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0002"), Bob, Maintenant));

        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "R0001").Statut);
        Assert.Equal("Attend R0001", Dispo(m, "R0002").Libelle);
        Regles.Demarrer(m, E(m, "R0001"), Bob, Maintenant);
        Regles.Terminer(m, E(m, "R0001"), EtatEtape.Reussie, "rétablie", true, Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        Regles.Demarrer(m, E(m, "R0002"), Claire, Maintenant);
        Regles.Terminer(m, E(m, "R0002"), EtatEtape.Reussie, "Service 1.0 OK", true, Claire, Maintenant);
        Regles.Cloturer(m, Alice, Maintenant, BilanOk);
        Assert.Equal(Phase.Cloturee, m.Phase);
        Assert.Equal(IssueMep.Repliee, m.Issue);
    }

    [Fact]
    public void Echeance_declenche_automatiquement_le_retour_arriere()
    {
        var m = Demo();
        Go(m);
        Assert.False(Regles.AppliquerEcheance(m, Maintenant));
        var apres = m.EcheanceRepli!.Value;
        Assert.True(Regles.AppliquerEcheance(m, apres));
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Null(m.RetourArriereDeclenchePar);
        Assert.Equal(Regles.MotifEcheance, m.RetourArriereMotif);
        // Le repli n'est pas soumis à l'échéance.
        Regles.Demarrer(m, E(m, "R0001"), Bob, apres.AddHours(1));
    }

    [Fact]
    public void Prendre_la_main_permet_d_executer_a_la_place_du_responsable()
    {
        var m = Demo();
        Go(m);
        var e3 = E(m, "E0003");
        var bob = m.Participants.Single(p => p.UtilisateurId == Bob);
        var precedent = Regles.PrendreLaMain(m, e3, bob, "Absent");
        Assert.Equal(e3.ResponsableId, precedent);
        Assert.Equal(bob.Id, e3.ExecutantId);
        Assert.NotEqual(bob.Id, e3.ResponsableId);
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, e3, Claire, Maintenant));
        Regles.Demarrer(m, e3, Bob, Maintenant);
        Regles.Terminer(m, e3, EtatEtape.Reussie, "WEB démo OK", true, Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, e3, bob, "Absent"));
    }

    [Fact]
    public void Prendre_la_main_refuse_en_preparation()
    {
        var m = Demo();
        var bob = m.Participants.Single(p => p.UtilisateurId == Bob);
        Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, E(m, "E0003"), bob, "Absent"));
    }

    [Fact]
    public void Resolution_conforme_permet_de_reprendre_l_etape()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = j.Mep;
        Go(m);
        var e5 = E(m, "E0005");
        var exec = Regles.DemarrerResolution(m, e5, Fix(j), Bob, Maintenant);
        Assert.Equal(StatutDisponibilite.ResolutionEnCours, Dispo(m, "E0005").Statut);
        Assert.Throws<RegleMetierException>(() => Regles.ValiderVerification(m, exec, ResultatControle.Conforme, "ok", Bob, Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.ValiderCorrection(m, exec, 2, ResultatControle.Conforme, "", Bob, Maintenant));
        Regles.ValiderCorrection(m, exec, 1, ResultatControle.Conforme, "", Bob, Maintenant);
        Regles.ValiderVerification(m, exec, ResultatControle.Conforme, "réponse OK", Bob, Maintenant);
        Assert.Equal(StatutResolution.Reussie, exec.Statut);
        Assert.Equal(EtatEtape.AFaire, e5.Etat);
        Regles.Demarrer(m, e5, Bob, Maintenant);
    }

    [Fact]
    public void Resolution_en_echec_declenche_le_retour_arriere()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = j.Mep;
        Go(m);
        var exec = Regles.DemarrerResolution(m, E(m, "E0005"), Fix(j), Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.ValiderCorrection(m, exec, 1, ResultatControle.Echec, " ", Bob, Maintenant));
        Regles.ValiderCorrection(m, exec, 1, ResultatControle.Echec, "toujours aucune réponse", Bob, Maintenant);
        Assert.Equal(StatutResolution.Echouee, exec.Statut);
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Contains("E0005", m.RetourArriereMotif);
    }

    [Fact]
    public void Resolution_non_prevue_pour_l_etape_refusee()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = j.Mep;
        Go(m);
        Assert.Throws<RegleMetierException>(() => Regles.DemarrerResolution(m, E(m, "E0005"), j.Resolutions[1], Bob, Maintenant));
    }

    [Fact]
    public void Cloture_reussie_apres_la_recette_quand_tous_les_tests_sont_conformes()
    {
        var m = Demo();
        Go(m);
        Assert.Throws<RegleMetierException>(() => Regles.PasserEnRecette(m, Alice, Maintenant));
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        Assert.Contains("Passer d'abord en recette", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.PasserEnRecette(m, Claire, Maintenant));
        Regles.PasserEnRecette(m, Alice, Maintenant);
        Assert.Equal(Phase.Recette, m.Phase);
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0004").Statut);

        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        foreach (var t in m.TestsRecette) Regles.ValiderTestRecette(m, t, ResultatControle.Conforme, "OK", Alice, Maintenant);
        Regles.Cloturer(m, Alice, Maintenant, BilanOk);
        Assert.Equal(IssueMep.Reussie, m.Issue);
        Assert.Equal(Phase.Cloturee, m.Phase);
        Assert.Equal("Rien à signaler", m.Bilan!.Incidents);
        Assert.Equal(Alice, m.Bilan.RedigeParId);
        Assert.False(Regles.AppliquerEcheance(m, m.EcheanceRepli!.Value.AddDays(1)));
    }

    [Fact]
    public void Dependance_vers_un_autre_plan_refusee()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, E(m, "R0001"), [E(m, "E0001").Id]));
    }

    [Fact]
    public void Resolution_type_incomplete_signalee()
    {
        var r = new ResolutionType { Titre = "x", Symptomes = "y", Corrections = [new() { Ordre = 1, Instruction = "a" }] };
        var erreurs = Regles.ErreursResolutionType(r);
        Assert.Contains("Correction 1 : préciser l'instruction et le résultat attendu.", erreurs);
        Assert.Contains("Préciser la vérification finale et son résultat attendu.", erreurs);
    }
}
