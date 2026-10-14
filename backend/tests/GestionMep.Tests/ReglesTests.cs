using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du plan, dont le parcours de test de référence.</summary>
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

    private static void Reussir(Mep m, string code, Guid qui)
    {
        Regles.Demarrer(m, E(m, code), qui, Maintenant);
        Regles.Terminer(m, E(m, code), EtatEtape.Reussie, "OK", true, qui, Maintenant);
    }

    [Fact]
    public void Demo_disponibilites_initiales()
    {
        var m = Demo();
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0001").Statut);
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0003").Statut);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0004").Statut);
        Assert.Equal(StatutDisponibilite.ArretResolution, Dispo(m, "E0005").Statut);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0006").Statut);
        Assert.Equal("Attend E0004", Dispo(m, "E0007").Libelle);
        Assert.Equal("Attend E0008", Dispo(m, "E0009").Libelle);
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
    public void Parcours_E0007_disponible_seulement_apres_toute_la_ligne_au_dessus()
    {
        var m = Demo();
        Go(m);

        Reussir(m, "E0004", Alice);
        Assert.Equal("Attend E0005", Dispo(m, "E0007").Libelle);
        Regles.AutoriserReprise(m, E(m, "E0005"), Alice, Maintenant);
        Reussir(m, "E0005", Bob);
        Assert.Equal("Attend E0006", Dispo(m, "E0007").Libelle);
        Reussir(m, "E0006", Claire);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "E0007").Statut);
    }

    [Fact]
    public void Qui_demarre_devient_executant_et_seul_lui_ou_le_pilote_termine()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Assert.Null(e4.ExecutantId);
        // Plus de responsable prévu : Claire démarre E0004 (prévue autrefois pour Alice).
        Regles.Demarrer(m, e4, Claire, Maintenant);
        Assert.Equal(m.Participants.Single(p => p.UtilisateurId == Claire).Id, e4.ExecutantId);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e4, EtatEtape.Reussie, "OK", true, Guid.NewGuid(), Maintenant));
        Assert.Contains("Prendre la main", ex.Message);
        // Le pilote (ou un suppléant) peut terminer à la place de l'exécutant.
        Regles.Terminer(m, e4, EtatEtape.Reussie, "OK", true, Alice, Maintenant);
        // Un compte qui n'est pas participant ne démarre pas (l'API en fait un participant s'il est administrateur).
        var refus = Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0006"), Guid.NewGuid(), Maintenant));
        Assert.Contains("participant relié à un compte", refus.Message);
        var sansCompte = new Participant { MepId = m.Id, Nom = "Eve" };
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0006"), sansCompte, Maintenant));
    }

    [Fact]
    public void Aucune_execution_en_preparation()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0004"), Alice, Maintenant));
    }

    [Fact]
    public void Resultat_exige_demarrage_confirmation_et_preuve_en_echec()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e4, EtatEtape.Reussie, "ok", true, Alice, Maintenant));
        Regles.Demarrer(m, e4, Alice, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e4, EtatEtape.Reussie, "ok", false, Alice, Maintenant));
        // Un échec doit être décrit.
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e4, EtatEtape.Echec, "  ", false, Alice, Maintenant));
        Regles.Terminer(m, e4, EtatEtape.Echec, "erreur 500", false, Alice, Maintenant);
        Assert.Equal(EtatEtape.Echec, e4.Etat);
        Assert.Equal(Maintenant, e4.FinReelle);
    }

    [Fact]
    public void Reussite_sans_preuve_acceptee()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Regles.Demarrer(m, e4, Alice, Maintenant);
        // Test unitaire : sans preuve, le résultat constaté suffit.
        Regles.Terminer(m, e4, EtatEtape.Reussie, "", true, Alice, Maintenant, resultatConstate: "Migrations appliquées");
        Assert.Equal(EtatEtape.Reussie, e4.Etat);
        Assert.Equal("", e4.Preuve);
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
        Regles.Demarrer(m, E(m, "E0004"), Alice, Maintenant);
        var apres = m.EcheanceRepli!.Value.AddMinutes(1);
        Assert.Equal(StatutDisponibilite.RetourArriere, Regles.Disponibilite(m, E(m, "E0006"), apres).Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, E(m, "E0004"), EtatEtape.Reussie, "ok", true, Alice, apres));
        // Un échec reste déclarable après l'échéance.
        Regles.Terminer(m, E(m, "E0004"), EtatEtape.Echec, "trop tard", false, Alice, apres);
    }

    [Fact]
    public void Passage_en_execution_reserve_au_pilote_et_plan_complet()
    {
        var m = Demo();
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Assert.Throws<RegleMetierException>(() => PasserEnExecution(m, Claire));
        E(m, "E0006").Instructions = "";
        var ex = Assert.Throws<RegleMetierException>(() => PasserEnExecution(m, Alice));
        Assert.Contains("E0006 : compléter Instructions exactes", ex.Message);
        Assert.Equal(Phase.Preparation, m.Phase);
    }

    [Fact]
    public void Retour_en_preparation_refuse_si_action_en_cours()
    {
        var m = Demo();
        Go(m);
        Regles.Demarrer(m, E(m, "E0004"), Alice, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.RevenirEnPreparation(m, Alice));
        Regles.Terminer(m, E(m, "E0004"), EtatEtape.Reussie, "ok", true, Alice, Maintenant);
        Regles.RevenirEnPreparation(m, Alice);
        Assert.Equal(EtatEtape.Reussie, E(m, "E0004").Etat);
    }

    [Fact]
    public void Lien_montant_refuse_et_dependances_inchangees()
    {
        var m = Demo();
        var e1 = E(m, "E0001");
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, e1, [E(m, "E0004").Id]));
        Assert.Equal("Un lien descend toujours : E0004 doit être sur une ligne au-dessus de E0001.", ex.Message);
        Assert.Empty(e1.Dependances);
        Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, e1, [e1.Id]));
    }

    [Fact]
    public void Suppression_rattache_les_suivants_a_la_ligne_au_dessus()
    {
        var m = Demo();
        Regles.RetirerEtape(m, E(m, "E0007"));
        Assert.Equal(["E0004", "E0005", "E0006"], E(m, "E0008").Dependances.Select(d => m.Etapes.Single(x => x.Id == d).Code).ToArray());
        Assert.Equal(2, E(m, "E0008").Ligne);
        Assert.Equal(3, E(m, "E0009").Ligne);
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Regles.RetirerEtape(m, E(m, "E0008")));
    }

    [Fact]
    public void Codes_jamais_reutilises()
    {
        var m = Demo();
        m.Etapes.Remove(E(m, "E0009"));
        Assert.Equal("E0009", Regles.ProchainCode(m.Etapes));
        Assert.Equal("R0004", Regles.ProchainCode(m.Etapes, TypePlan.RetourArriere));
        m.Etapes.Add(new Etape { Code = "E0042" });
        Assert.Equal("E0043", Regles.ProchainCode(m.Etapes));
    }

    [Fact]
    public void Disponibilite_signale_champs_manquants_dans_l_ordre_de_l_excel()
    {
        var m = Demo();
        var e4 = E(m, "E0004");
        e4.Instructions = "";
        Assert.Equal(StatutDisponibilite.InstructionsACompleter, Dispo(m, "E0004").Statut);
        e4.SystemeIds = [];
        Assert.Equal(StatutDisponibilite.FicheACompleter, Dispo(m, "E0004").Statut);
        // L'échéance de retour arrière est facultative : son absence ne bloque pas l'étape.
        m.EcheanceRepli = null;
        Assert.Equal(StatutDisponibilite.FicheACompleter, Dispo(m, "E0004").Statut);
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
        Assert.Equal(StatutDisponibilite.RetourArriere, Dispo(m, "E0004").Statut);
        Assert.Equal(StatutDisponibilite.Terminee, Dispo(m, "E0001").Statut);
        Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0004"), Alice, Maintenant));

        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "R0001").Statut);
        Assert.Equal(StatutDisponibilite.Disponible, Dispo(m, "R0002").Statut);
        Assert.Equal("Attend R0001", Dispo(m, "R0003").Libelle);
        Reussir(m, "R0001", Bob);
        Reussir(m, "R0002", Alice);
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        Reussir(m, "R0003", Claire);
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
    public void Prendre_la_main_sur_une_etape_en_cours_demarree_par_un_autre()
    {
        var m = Demo();
        Go(m);
        var e6 = E(m, "E0006");
        var bob = m.Participants.Single(p => p.UtilisateurId == Bob);
        var claire = m.Participants.Single(p => p.UtilisateurId == Claire);
        // Pas encore démarrée : il suffit de la démarrer.
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, e6, bob, "Absent"));
        Assert.Contains("n'est pas démarrée", ex.Message);
        Regles.Demarrer(m, e6, Claire, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, e6, claire, "Moi")); // déjà exécutante
        var precedent = Regles.PrendreLaMain(m, e6, bob, "Claire est absente");
        Assert.Equal(claire.Id, precedent);
        Assert.Equal(bob.Id, e6.ExecutantId);
        Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e6, EtatEtape.Reussie, "OK", true, Claire, Maintenant));
        Regles.Terminer(m, e6, EtatEtape.Reussie, "WEB démo OK", true, Bob, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, e6, claire, "Absent"));
        // Une étape en échec démarrée par un autre se reprend aussi en main.
        Assert.Equal(EtatEtape.Echec, E(m, "E0005").Etat);
        Regles.PrendreLaMain(m, E(m, "E0005"), claire, "Bob est absent");
        Assert.Equal(claire.Id, E(m, "E0005").ExecutantId);
    }

    [Fact]
    public void Prendre_la_main_refuse_en_preparation()
    {
        var m = Demo();
        var bob = m.Participants.Single(p => p.UtilisateurId == Bob);
        Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, E(m, "E0006"), bob, "Absent"));
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
        foreach (var f in m.FichesRecette)
            foreach (var t in f.Etapes) Regles.ValiderEtapeTest(m, f, t, ResultatControle.Conforme, "OK", Alice, false, Maintenant);
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
