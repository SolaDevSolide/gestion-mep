using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du jalon 3 : suppléants, sauvegarde, approbation, go / no-go, point de non-retour, recette, bilan, modèles.</summary>
public class ReglesJalon3Tests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Participant P(Mep m, string nom) => m.Participants.Single(p => p.Nom == nom);
    private static Bilan BilanOk => new() { CeQuiAMarche = "Tout", Incidents = "Rien à signaler" };

    private static void Approuver(Mep m) => Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);

    private static void Go(Mep m)
    {
        Approuver(m);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
    }

    private static void Reussir(Mep m, string code, Guid qui)
    {
        Regles.Demarrer(m, E(m, code), qui, Maintenant);
        Regles.Terminer(m, E(m, code), EtatEtape.Reussie, "OK", true, qui, Maintenant);
    }

    /// <summary>Go, puis E0004, E0005 (après reprise) et E0006 réussies : E0007, point de non-retour, est disponible.</summary>
    private static Mep JusquAuNonRetour(DonneesDemo.Jeu? jeu = null)
    {
        var m = (jeu ?? DonneesDemo.Creer(Alice, Bob, Claire, Maintenant)).Mep;
        Go(m);
        Reussir(m, "E0004", Alice);
        Regles.AutoriserReprise(m, E(m, "E0005"), Alice, Maintenant);
        Reussir(m, "E0005", Bob);
        Reussir(m, "E0006", Claire);
        return m;
    }

    /// <summary>Passage de E0007 autorisé puis E0007 démarrée par Bob : point de non-retour franchi.</summary>
    private static Mep NonRetourFranchi(DonneesDemo.Jeu? jeu = null)
    {
        var m = JusquAuNonRetour(jeu);
        Regles.AutoriserNonRetour(m, E(m, "E0007"), Alice, Maintenant);
        Regles.Demarrer(m, E(m, "E0007"), Bob, Maintenant);
        return m;
    }

    /// <summary>Tout le déploiement réussi puis passage en recette.</summary>
    private static Mep EnRecette()
    {
        var m = Demo();
        Go(m);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        Regles.PasserEnRecette(m, Alice, Maintenant);
        return m;
    }

    // ---------- 1. Pilote et suppléants ----------
    [Fact]
    public void Tous_les_suppleants_ont_les_droits_du_pilote()
    {
        var m = Demo();
        var dave = new Participant { MepId = m.Id, Nom = "Dave", UtilisateurId = Dave };
        m.Participants.Add(dave);
        Assert.False(Regles.EstPilote(m, Dave));
        m.SuppleantIds.Add(dave.Id);
        Assert.True(Regles.EstPilote(m, Bob));
        Assert.True(Regles.EstPilote(m, Dave));
        Assert.False(Regles.EstPilote(m, Claire));
        Regles.NoGo(m, Dave, "Supervision absente");
    }

    [Fact]
    public void Le_pilote_ne_peut_pas_etre_suppleant_et_pas_de_doublon()
    {
        var m = Demo();
        var alice = P(m, "Alice").Id;
        var bob = P(m, "Bob").Id;
        var ex = Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppleants(m, alice, [bob, alice]));
        Assert.Equal("Alice est le pilote : il ne peut pas être aussi suppléant.", ex.Message);
        ex = Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppleants(m, alice, [bob, bob]));
        Assert.Equal("Bob est désigné deux fois comme suppléant.", ex.Message);
        Regles.VerifierSuppleants(m, alice, [bob, P(m, "Claire").Id]);
    }

    // ---------- 2. Type d'étape (la sauvegarde par système est testée dans ReglesJalon4Tests) ----------
    [Fact]
    public void Type_sauvegarde_et_point_de_non_retour_refuses_sur_le_plan_de_retour_arriere()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.DefinirType(E(m, "R0001"), TypeEtape.Sauvegarde, false));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirType(E(m, "R0001"), TypeEtape.Standard, true));
        Assert.Contains("retour arrière", ex.Message);
        Regles.DefinirType(E(m, "R0001"), TypeEtape.Standard, false);
    }

    // ---------- 3. Circuit d'approbation ----------
    [Fact]
    public void Approbation_par_l_approbateur_designe_seulement()
    {
        var m = Demo();
        Assert.Equal(["Il faut 1 approbation(s)."], Regles.ErreursApprobation(m, 1));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Decider(m, Bob, DecisionApprobation.Approuvee, "", Maintenant));
        Assert.Contains("approbateur désigné", ex.Message);
        var a = Regles.Decider(m, Claire, DecisionApprobation.Approuvee, " RAS ", Maintenant);
        Assert.Equal(DecisionApprobation.Approuvee, a.Decision);
        Assert.Equal("RAS", a.Commentaire);
        Assert.Equal(Maintenant, a.Le);
        Assert.Empty(Regles.ErreursPlan(m, Maintenant, 1));
        Assert.Equal(["Il faut 2 approbation(s)."], Regles.ErreursPlan(m, Maintenant, 2));
    }

    [Fact]
    public void Refus_exige_un_commentaire_et_bloque_le_plan()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.Decider(m, Claire, DecisionApprobation.Refusee, " ", Maintenant));
        Regles.Decider(m, Claire, DecisionApprobation.Refusee, "Pas de test de charge", Maintenant);
        Assert.Contains("Claire a refusé : Pas de test de charge", Regles.ErreursPlan(m, Maintenant, 0));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 0, Maintenant));
        Assert.Contains("Claire a refusé", ex.Message);
    }

    [Fact]
    public void Decision_impossible_tant_que_le_contenu_est_incomplet_ou_hors_preparation()
    {
        var m = Demo();
        E(m, "E0004").Instructions = "";
        var ex = Assert.Throws<RegleMetierException>(() => Approuver(m));
        Assert.Contains("E0004 : compléter Instructions exactes", ex.Message);
        E(m, "E0004").Instructions = "Lire la fiche";
        Go(m);
        Assert.Throws<RegleMetierException>(() => Approuver(m));
    }

    [Fact]
    public void Le_pilote_ne_peut_pas_etre_approbateur()
    {
        var m = Demo();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirApprobateurs(m, [(Claire, "Claire"), (Alice, "Alice")]));
        Assert.Contains("Alice est le pilote", ex.Message);
        // Si le pilote devient approbateur autrement (compte relié après coup), la décision est refusée et le plan le signale.
        m.Approbations.Add(new Approbation { UtilisateurId = Alice, Nom = "Alice" });
        Assert.Throws<RegleMetierException>(() => Regles.Decider(m, Alice, DecisionApprobation.Approuvee, "", Maintenant));
        Assert.Contains("Alice ne peut pas approuver : c'est le pilote.", Regles.ErreursApprobation(m, 1));
    }

    [Fact]
    public void Definir_les_approbateurs_conserve_les_existants()
    {
        var m = Demo();
        var claire = m.Approbations.Single();
        Regles.DefinirApprobateurs(m, [(Claire, "Claire"), (Dave, "Dave"), (Dave, "Dave")]);
        Assert.Equal([Claire, Dave], m.Approbations.Select(a => a.UtilisateurId).ToArray());
        Assert.Same(claire, m.Approbations[0]);
    }

    [Fact]
    public void Toute_modification_en_preparation_remet_les_approbations_a_zero()
    {
        var m = Demo();
        Assert.False(Regles.ReinitialiserApprobations(m));
        Regles.Decider(m, Claire, DecisionApprobation.Refusee, "Non", Maintenant);
        Assert.True(Regles.ReinitialiserApprobations(m));
        var a = m.Approbations.Single();
        Assert.Null(a.Decision);
        Assert.Equal("", a.Commentaire);
        Assert.Null(a.Le);
        Assert.False(Regles.ReinitialiserApprobations(m));

        // Hors préparation, les décisions restent comme trace.
        Go(m);
        Assert.False(Regles.ReinitialiserApprobations(m));
        Assert.Equal(DecisionApprobation.Approuvee, m.Approbations.Single().Decision);
    }

    // ---------- 4. Go / no-go ----------
    [Fact]
    public void Go_exige_tous_les_criteres_actifs_coches_et_les_fige()
    {
        var m = Demo();
        Approuver(m);
        var criteres = DonneesDemo.CriteresGoNoGo();
        criteres.Add(new CritereGoNoGo { Ordre = 5, Libelle = "Critère retiré", Actif = false });
        var coches = criteres.Where(c => c.Actif).Select(c => c.Id).ToList();

        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, coches.Skip(1).ToList(), criteres, 1, Maintenant));
        Assert.Equal("Go impossible : cocher « Équipe présente et joignable ».", ex.Message);
        Assert.Equal(Phase.Preparation, m.Phase);

        Regles.PasserEnExecution(m, Bob, coches, criteres, 1, Maintenant);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(Criteres.Select(c => c.Libelle), m.GoNoGo.Select(c => c.Libelle));
        Assert.All(m.GoNoGo, c => { Assert.Equal(Bob, c.ParId); Assert.Equal(Maintenant, c.Le); });
        // Copie figée : renommer le critère du catalogue ne change pas la MEP.
        criteres[0].Libelle = "Autre";
        Assert.Equal("Équipe présente et joignable", m.GoNoGo[0].Libelle);
    }

    [Fact]
    public void Go_refuse_sans_approbation()
    {
        var m = Demo();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant));
        Assert.Contains("Il faut 1 approbation(s).", ex.Message);
    }

    [Fact]
    public void No_go_par_le_pilote_avec_motif_en_preparation()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.NoGo(m, Claire, "Supervision absente"));
        Assert.Throws<RegleMetierException>(() => Regles.NoGo(m, Alice, " "));
        Regles.NoGo(m, Alice, "Supervision absente");
        Assert.Equal(Phase.Preparation, m.Phase);
        Go(m);
        Assert.Throws<RegleMetierException>(() => Regles.NoGo(m, Alice, "Trop tard"));
    }

    // ---------- 5. Point de non-retour ----------
    [Fact]
    public void Point_de_non_retour_exige_l_accord_du_pilote()
    {
        var m = Demo();
        Assert.True(E(m, "E0007").PointDeNonRetour);
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserNonRetour(m, E(m, "E0007"), Alice, Maintenant)); // préparation
        m = JusquAuNonRetour();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Demarrer(m, E(m, "E0007"), Bob, Maintenant));
        Assert.Equal("Point de non-retour : l'accord du pilote est requis.", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserNonRetour(m, E(m, "E0007"), Claire, Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserNonRetour(m, E(m, "E0008"), Alice, Maintenant)); // pas un point de non-retour
        Assert.False(Regles.NonRetourFranchi(m));

        Regles.AutoriserNonRetour(m, E(m, "E0007"), Bob, Maintenant); // suppléant
        Assert.Equal(Bob, E(m, "E0007").NonRetourAutoriseParId);
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserNonRetour(m, E(m, "E0007"), Alice, Maintenant));
        Assert.False(Regles.NonRetourFranchi(m));
        Regles.Demarrer(m, E(m, "E0007"), Bob, Maintenant);
        Assert.True(Regles.NonRetourFranchi(m));
        Assert.Equal("E0007", Regles.EtapeNonRetourFranchie(m)!.Code);
    }

    [Fact]
    public void Apres_le_point_de_non_retour_l_echeance_ne_s_applique_plus_au_deploiement()
    {
        var m = NonRetourFranchi();
        var apres = m.EcheanceRepli!.Value.AddHours(1);
        Assert.False(Regles.AppliquerEcheance(m, apres));
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(StatutDisponibilite.EnCours, Regles.Disponibilite(m, E(m, "E0007"), apres).Statut);
        // Échec puis reprise d'une étape malgré l'échéance dépassée.
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Echec, "Erreur", false, Bob, apres);
        Assert.Equal(StatutDisponibilite.ArretResolution, Regles.Disponibilite(m, E(m, "E0007"), apres).Statut);
        Regles.AutoriserReprise(m, E(m, "E0007"), Alice, apres);
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "E0007"), apres).Statut);
        Regles.Demarrer(m, E(m, "E0007"), Bob, apres);
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Reussie, "Données basculées", true, Bob, apres);
    }

    [Fact]
    public void Apres_le_point_de_non_retour_aucun_retour_arriere()
    {
        var m = NonRetourFranchi();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, Alice, "Incident", Maintenant));
        Assert.Equal("Point de non-retour franchi (E0007) : corriger en avant.", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, null, "Automatique", Maintenant));
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void Apres_le_point_de_non_retour_une_resolution_en_echec_laisse_l_etape_en_echec()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        E(j.Mep, "E0007").ResolutionIds = [j.Resolutions[0].Id];
        var m = NonRetourFranchi(j);
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Echec, "Erreur", false, Bob, Maintenant);

        var apres = m.EcheanceRepli!.Value.AddHours(1); // l'échéance n'empêche plus de lancer une résolution
        var exec = Regles.DemarrerResolution(m, E(m, "E0007"), j.Resolutions[0], Bob, apres);
        Regles.ValiderCorrection(m, exec, 1, ResultatControle.Echec, "toujours aucune réponse", Bob, apres);
        Assert.Equal(StatutResolution.Echouee, exec.Statut);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(EtatEtape.Echec, E(m, "E0007").Etat);
        Assert.Null(m.RetourArriereDeclencheLe);
        // Le pilote peut autoriser une reprise.
        Regles.AutoriserReprise(m, E(m, "E0007"), Alice, apres);
        Assert.Equal(EtatEtape.AFaire, E(m, "E0007").Etat);
    }

    [Fact]
    public void Point_de_non_retour_reste_franchi_apres_une_reprise_de_l_etape()
    {
        var m = NonRetourFranchi();
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Echec, "Erreur", false, Bob, Maintenant);
        Regles.AutoriserReprise(m, E(m, "E0007"), Alice, Maintenant);
        Assert.Equal(EtatEtape.AFaire, E(m, "E0007").Etat);
        Assert.True(Regles.NonRetourFranchi(m));
    }

    // ---------- 6. Recette en production (fiches : voir ReglesJalon4Tests) ----------
    [Fact]
    public void Recette_seulement_quand_tout_le_deploiement_est_reussi()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.PasserEnRecette(m, Alice, Maintenant)); // préparation
        Go(m);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnRecette(m, Alice, Maintenant));
        Assert.Equal("E0004 n'est pas réussie : recette impossible.", ex.Message);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        Regles.PasserEnRecette(m, Bob, Maintenant);
        Assert.Equal(Phase.Recette, m.Phase);
        // Le déploiement est terminé : plus d'action sur ses étapes.
        Assert.Throws<RegleMetierException>(() => Regles.AutoriserReprise(m, E(m, "E0005"), Alice, Maintenant));
    }

    [Fact]
    public void En_recette_l_echeance_et_le_pilote_peuvent_declencher_le_retour_arriere()
    {
        // Sans point de non-retour dans le plan (celui de la démo est franchi une fois le déploiement terminé).
        var m = EnRecette();
        E(m, "E0007").PointDeNonRetour = false;
        Assert.False(Regles.AppliquerEcheance(m, Maintenant));
        Assert.True(Regles.AppliquerEcheance(m, m.EcheanceRepli!.Value));
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Equal(Regles.MotifEcheance, m.RetourArriereMotif);

        m = EnRecette();
        E(m, "E0007").PointDeNonRetour = false;
        Assert.Throws<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, Claire, "Recette KO", Maintenant));
        Regles.DeclencherRetourArriere(m, Alice, "Recette KO", Maintenant);
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "R0001"), Maintenant).Statut);
    }

    [Fact]
    public void En_recette_pas_de_retour_arriere_automatique_si_le_point_de_non_retour_est_franchi()
    {
        var m = NonRetourFranchi();
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Reussie, "OK", true, Bob, Maintenant);
        Reussir(m, "E0008", Claire);
        Reussir(m, "E0009", Alice);
        Regles.PasserEnRecette(m, Alice, Maintenant);
        Assert.False(Regles.AppliquerEcheance(m, m.EcheanceRepli!.Value.AddDays(1)));
        Assert.Equal(Phase.Recette, m.Phase);
        Assert.Throws<RegleMetierException>(() => Regles.DeclencherRetourArriere(m, Alice, "Recette KO", Maintenant));
    }

    [Fact]
    public void Cloture_depuis_l_execution_refusee()
    {
        var m = Demo();
        Go(m);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        Assert.StartsWith("Passer d'abord en recette", ex.Message);
    }

    // ---------- 7. Bilan de clôture ----------
    [Fact]
    public void Bilan_obligatoire_quand_il_est_active()
    {
        var m = EnRecette();
        foreach (var f in m.FichesRecette)
            foreach (var t in f.Etapes) Regles.ValiderEtapeTest(m, f, t, ResultatControle.Conforme, "OK", Alice, false, Maintenant);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant));
        Assert.Contains("Rien à signaler", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, new Bilan { CeQuiAMarche = "Tout", Incidents = " " }));
        Assert.Equal(Phase.Recette, m.Phase);
        Regles.Cloturer(m, Bob, Maintenant, new Bilan { CeQuiAMarche = " Tout ", Incidents = "Rien à signaler", Actions = "Aucune" });
        Assert.Equal("Tout", m.Bilan!.CeQuiAMarche);
        Assert.Equal("Aucune", m.Bilan.Actions);
        Assert.Equal(Bob, m.Bilan.RedigeParId);
        Assert.Equal(Maintenant, m.Bilan.Le);
    }

    [Fact]
    public void Bilan_facultatif_quand_il_est_desactive()
    {
        var m = Demo();
        m.BilanActive = false;
        Go(m);
        Regles.DeclencherRetourArriere(m, Alice, "Incident", Maintenant);
        foreach (var r in m.EtapesDu(TypePlan.RetourArriere)) r.Etat = EtatEtape.Reussie;
        Regles.Cloturer(m, Alice, Maintenant);
        Assert.Equal(IssueMep.Repliee, m.Issue);
        Assert.Null(m.Bilan);
    }

    // ---------- 8. Modèles et duplication ----------
    [Fact]
    public void Duplication_copie_le_contenu_avec_de_nouveaux_identifiants()
    {
        var source = Demo();
        Go(source);
        Reussir(source, "E0004", Alice);
        var copie = Regles.Dupliquer(source, commeModele: false, Dave, Maintenant.AddDays(1));

        Assert.NotEqual(source.Id, copie.Id);
        Assert.Equal(Phase.Preparation, copie.Phase);
        Assert.False(copie.EstModele);
        Assert.Equal(Dave, copie.CreeParUtilisateurId);
        Assert.Equal(source.Outil + " (copie)", copie.Outil);
        Assert.True(copie.BilanActive);
        Assert.Equal(source.PlanRepli.QuandDeclencher, copie.PlanRepli.QuandDeclencher);
        Assert.NotSame(source.PlanRepli, copie.PlanRepli);

        // Non copiés.
        Assert.Null(copie.EcheanceRepli);
        Assert.Null(copie.DateMep);
        Assert.Null(copie.ProcedureRelueParId);
        Assert.Null(copie.DemarrageAutoriseParId);
        Assert.Empty(copie.GoNoGo);
        Assert.Null(copie.Bilan);
        Assert.Empty(copie.Resolutions);

        // Participants et références remappés.
        var ids = copie.Participants.Select(p => p.Id).ToHashSet();
        Assert.Equal(source.Participants.Select(p => p.Nom), copie.Participants.Select(p => p.Nom));
        Assert.Equal(source.Participants.Select(p => p.UtilisateurId), copie.Participants.Select(p => p.UtilisateurId));
        Assert.DoesNotContain(copie.Participants, p => source.Participants.Any(s => s.Id == p.Id));
        Assert.All(copie.Participants, p => Assert.Equal(copie.Id, p.MepId));
        Assert.Equal("Alice", copie.Participants.Single(p => p.Id == copie.PiloteId).Nom);
        Assert.Equal(["Bob"], copie.SuppleantIds.Select(id => copie.Participants.Single(p => p.Id == id).Nom).ToArray());
        Assert.True(Regles.EstPilote(copie, Bob));

        var systemes = copie.Systemes.Select(s => s.Id).ToHashSet();
        Assert.Equal(source.Systemes.Select(s => s.CatalogueId), copie.Systemes.Select(s => s.CatalogueId));
        Assert.All(copie.ObjetsImpactes, l => Assert.Contains(l.SystemeId!.Value, systemes));

        Assert.Equal(source.Etapes.Select(e => e.Code).Order(), copie.Etapes.Select(e => e.Code).Order());
        foreach (var e in copie.Etapes)
        {
            var s = source.Etapes.Single(x => x.Code == e.Code);
            Assert.NotEqual(s.Id, e.Id);
            Assert.Equal(EtatEtape.AFaire, e.Etat);
            Assert.Equal("", e.Preuve);
            Assert.Null(e.DebutReel);
            Assert.Null(e.FinReelle);
            Assert.Null(e.ExecutantId);
            Assert.Null(e.NonRetourAutoriseParId);
            Assert.Equal(0, e.Revision);
            Assert.Equal(s.Plan, e.Plan);
            Assert.Equal(s.Type, e.Type);
            Assert.Equal(s.PointDeNonRetour, e.PointDeNonRetour);
            Assert.Equal(s.ResolutionIds, e.ResolutionIds);
            Assert.Equal(s.SystemeIds.Count, e.SystemeIds.Count);
            Assert.All(e.SystemeIds, id => Assert.Contains(id, systemes));
            Assert.Equal((s.Ligne, s.Ordre, s.DureeEstimeeMinutes), (e.Ligne, e.Ordre, e.DureeEstimeeMinutes));
            Assert.Null(e.ResponsableId); // obsolète depuis le jalon 4.2
            Assert.Null(e.ExecutantId);
            Assert.Equal(s.Dependances.Select(d => source.Etapes.Single(x => x.Id == d).Code), e.Dependances.Select(d => copie.Etapes.Single(x => x.Id == d).Code));
        }
        Assert.Null(Regles.ErreurGraphe(copie, TypePlan.Deploiement));

        Assert.Equal(source.Sauvegarde.Mode, copie.Sauvegarde.Mode);
        Assert.Null(copie.Sauvegarde.RealiseeLe);
        Assert.False(copie.Sauvegarde.RestaurationTestee);
        Assert.Equal("", copie.Sauvegarde.PreuveTestRestauration);
        Assert.Equal(source.Systemes.Select(s => (s.Sauvegarde.Statut, s.Sauvegarde.Moment, s.Sauvegarde.Contenu, s.Sauvegarde.Justification)),
            copie.Systemes.Select(s => (s.Sauvegarde.Statut, s.Sauvegarde.Moment, s.Sauvegarde.Contenu, s.Sauvegarde.Justification)));
        Assert.NotSame(source.Systemes[0].Sauvegarde, copie.Systemes[0].Sauvegarde);

        Assert.Equal(source.FichesRecette.Select(f => f.Titre), copie.FichesRecette.Select(f => f.Titre));
        Assert.All(copie.FichesRecette, f => Assert.Contains(f.SystemeId!.Value, systemes));
        Assert.DoesNotContain(copie.FichesRecette, f => source.FichesRecette.Any(s => s.Id == f.Id));

        Assert.Equal([Claire], copie.Approbations.Select(a => a.UtilisateurId).ToArray());
        Assert.Null(copie.Approbations[0].Decision);
        Assert.Equal(DecisionApprobation.Approuvee, source.Approbations[0].Decision);
    }

    [Fact]
    public void Un_modele_ne_passe_pas_en_execution()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var modele = j.Modele;
        Assert.True(modele.EstModele);
        Assert.Equal("Modèle : déploiement API standard", modele.Outil);
        Assert.Equal(12, modele.Etapes.Count);

        var m = Regles.Dupliquer(j.Mep, commeModele: true, Alice, Maintenant);
        m.EcheanceRepli = j.Mep.EcheanceRepli;
        m.ProcedureRelueParId = Bob;
        Approuver(m);
        var ex = Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant));
        Assert.StartsWith("Un modèle ne passe pas en exécution", ex.Message);

        // Dupliquer le modèle donne une MEP exécutable une fois complétée.
        var mep = Regles.Dupliquer(m, commeModele: false, Alice, Maintenant);
        Assert.False(mep.EstModele);
        Assert.True(Regles.ToucheProduction(mep));
        mep.EcheanceRepli = j.Mep.EcheanceRepli;
        mep.ProcedureRelueParId = Bob;
        Go(mep);
        Assert.Equal(Phase.Execution, mep.Phase);
    }
}
