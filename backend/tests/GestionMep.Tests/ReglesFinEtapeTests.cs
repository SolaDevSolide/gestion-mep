using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Fin d'étape typée (test unitaire ou simple vérification) et sauvegardes complètes (emplacement, méthode de récupération).</summary>
public class ReglesFinEtapeTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Systeme S(Mep m, string nom) => m.Systemes.Single(s => s.Nom == nom);

    private static void Go(Mep m)
    {
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        var criteres = DonneesDemo.CriteresGoNoGo();
        Regles.PasserEnExecution(m, Alice, criteres.Select(c => c.Id).ToList(), criteres, DonneesDemo.ApprobationsMinimum, Maintenant);
    }

    // ---------- Fin d'étape ----------
    [Fact]
    public void Une_etape_se_termine_par_defaut_par_un_test_unitaire()
    {
        Assert.Equal(FinEtape.TestUnitaire, new Etape().Fin);
        var m = Demo();
        Assert.Equal(FinEtape.Verification, E(m, "E0003").Fin);
        Regles.DefinirFin(E(m, "E0004"), FinEtape.Verification);
        Assert.Equal(FinEtape.Verification, E(m, "E0004").Fin);
        Assert.Throws<RegleMetierException>(() => Regles.DefinirFin(E(m, "E0004"), (FinEtape)7));
    }

    [Fact]
    public void Test_unitaire_reussi_exige_un_resultat_constate_a_defaut_la_preuve()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Regles.Demarrer(m, e4, Alice, Maintenant);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Terminer(m, e4, EtatEtape.Reussie, " ", true, Alice, Maintenant, resultatConstate: " "));
        Assert.Equal("Test unitaire : saisir le résultat constaté.", ex.Message);
        Regles.Terminer(m, e4, EtatEtape.Reussie, "Sortie de psql jointe", true, Alice, Maintenant, resultatConstate: " 12 migrations appliquées ");
        Assert.Equal(("12 migrations appliquées", "Sortie de psql jointe"), (e4.ResultatConstate, e4.Preuve));

        // Sans résultat constaté, la preuve (ce qui a été constaté) en tient lieu.
        var e6 = E(m, "E0006");
        Regles.Demarrer(m, e6, Claire, Maintenant);
        Regles.Terminer(m, e6, EtatEtape.Reussie, "Portail publié", true, Claire, Maintenant);
        Assert.Equal("Portail publié", e6.ResultatConstate);
    }

    [Fact]
    public void Simple_verification_sans_resultat_constate()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Regles.DefinirFin(e4, FinEtape.Verification);
        Regles.Demarrer(m, e4, Alice, Maintenant);
        Regles.Terminer(m, e4, EtatEtape.Reussie, "", true, Alice, Maintenant, resultatConstate: "ignoré");
        Assert.Equal((EtatEtape.Reussie, ""), (e4.Etat, e4.ResultatConstate));
    }

    [Fact]
    public void Test_unitaire_en_echec_garde_la_preuve_comme_resultat_puis_une_nouvelle_tentative_l_efface()
    {
        var m = Demo();
        Go(m);
        var e4 = E(m, "E0004");
        Regles.Demarrer(m, e4, Alice, Maintenant);
        Regles.Terminer(m, e4, EtatEtape.Echec, "Erreur 42P07", false, Alice, Maintenant);
        Assert.Equal("Erreur 42P07", e4.ResultatConstate);
        Regles.AutoriserReprise(m, e4, Alice, Maintenant);
        Regles.Demarrer(m, e4, Alice, Maintenant);
        Assert.Equal("", e4.ResultatConstate);
    }

    [Fact]
    public void Suivi_des_tests_unitaires_faits_et_non_faits_retour_arriere_compris_une_fois_declenche()
    {
        var m = Demo();
        // Déploiement : E0001 et E0002 réussies, E0005 en échec ; E0003 est une simple vérification.
        var tests = Regles.TestsUnitaires(m);
        Assert.Equal(["E0001", "E0002", "E0004", "E0005", "E0006", "E0007", "E0008", "E0009"], tests.Select(e => e.Code).ToArray());
        Assert.Equal(["E0001", "E0002", "E0005"], tests.Where(Regles.TestFait).Select(e => e.Code).ToArray());
        Assert.Equal(1, Regles.EtapesSuivies(m).Count(e => e.Fin == FinEtape.Verification));

        Go(m);
        Regles.DeclencherRetourArriere(m, Alice, "Essai", Maintenant);
        Assert.Contains(Regles.TestsUnitaires(m), e => e.Plan == TypePlan.RetourArriere);
    }

    [Fact]
    public void La_duplication_garde_la_fin_et_la_sauvegarde_mais_pas_le_resultat_constate()
    {
        var m = Demo();
        E(m, "E0001").ResultatConstate = "clients.sql";
        var copie = Regles.Dupliquer(m, commeModele: false, Alice, Maintenant);
        Assert.Equal(FinEtape.Verification, E(copie, "E0003").Fin);
        Assert.Equal("", E(copie, "E0001").ResultatConstate);
        Assert.Equal(E(m, "E0001").SauvegardeEmplacement, E(copie, "E0001").SauvegardeEmplacement);
        Assert.NotEqual("", E(copie, "E0001").SauvegardeMethodeRecuperation);
        Assert.Equal(S(m, "BDD").Sauvegarde.Emplacement, S(copie, "BDD").Sauvegarde.Emplacement);
        Assert.NotEqual("", S(copie, "BDD").Sauvegarde.MethodeRecuperation);
    }

    // ---------- Sauvegardes complètes ----------
    [Fact]
    public void Sauvegarde_requise_sans_emplacement_ni_methode_de_recuperation_bloque_le_go()
    {
        var m = Demo();
        // Approuvée tant que le plan était complet (l'API remettrait l'approbation à zéro à la modification suivante).
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        var bdd = S(m, "BDD");
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.Requise, ModeSauvegarde.AvantMep, "pg_dump", "", " ", "");
        Assert.Equal(["Sauvegarde de BDD : compléter Emplacement, Méthode de récupération."], Regles.ErreursSauvegarde(m));
        // Absents (null) : inchangés.
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.Requise, ModeSauvegarde.AvantMep, "pg_dump", "", " /sauvegardes/clients.sql ");
        Assert.Equal(["Sauvegarde de BDD : compléter Méthode de récupération."], Regles.ErreursSauvegarde(m));
        Assert.Equal("/sauvegardes/clients.sql", bdd.Sauvegarde.Emplacement);

        var criteres = DonneesDemo.CriteresGoNoGo();
        var ex = Assert.Throws<RegleMetierException>(() =>
            Regles.PasserEnExecution(m, Alice, criteres.Select(c => c.Id).ToList(), criteres, DonneesDemo.ApprobationsMinimum, Maintenant));
        Assert.Contains("Sauvegarde de BDD : compléter Méthode de récupération.", ex.Message);

        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.Requise, ModeSauvegarde.AvantMep, "pg_dump", "", null, "psql < clients.sql");
        Assert.Empty(Regles.ErreursSauvegarde(m));
        // Non requise : ni emplacement ni méthode exigés.
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep, "", "Lecture seule", "", "");
        Assert.Empty(Regles.ErreursSauvegarde(m));
    }

    [Fact]
    public void Etape_de_sauvegarde_sans_emplacement_ni_methode_de_recuperation_est_incomplete()
    {
        var m = Demo();
        var e1 = E(m, "E0001");
        e1.SauvegardeEmplacement = " ";
        e1.SauvegardeMethodeRecuperation = "";
        Assert.Contains("E0001 : compléter Emplacement de la sauvegarde, Méthode de récupération.", Regles.ErreursContenu(m, Maintenant));
        e1.SauvegardeEmplacement = "/sauvegardes/clients.sql";
        e1.SauvegardeMethodeRecuperation = "psql < clients.sql";
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));

        // Une étape standard ne garde ni emplacement ni méthode, et ne les réclame pas.
        var e4 = E(m, "E0004");
        e4.SauvegardeEmplacement = "ailleurs";
        Regles.DefinirType(e4, TypeEtape.Standard, false);
        Assert.Equal("", e4.SauvegardeEmplacement);
        Regles.DefinirType(e1, TypeEtape.Standard, false);
        Assert.Equal(("", ""), (e1.SauvegardeEmplacement, e1.SauvegardeMethodeRecuperation));
    }
}
