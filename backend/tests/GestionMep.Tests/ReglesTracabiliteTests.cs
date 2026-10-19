using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Approbation et traçabilité des décisions (#94) : séparation des tâches, relecture effacée, résolutions figées, échéance
/// tracée, pas de retour arrière automatique en recette ni sur échec de résolution, incidents de recette.
/// </summary>
public class ReglesTracabiliteTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid(), DaveBis = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static DonneesDemo.Jeu Jeu() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static List<Guid> Coches => Criteres.Select(c => c.Id).ToList();

    /// <summary>Dave et son second compte, relié au premier.</summary>
    private static AnnuairePersonnes Annuaire() => new(
    [
        new(Alice, null, "Alice", "alice", true), new(Bob, null, "Bob", "bob", true), new(Claire, null, "Claire", "claire", true),
        new(Dave, null, "Dave", "dave", true), new(DaveBis, Dave, "Dave", "dave.admin", true),
    ], []);

    private static Participant AjouterDave(Mep m)
    {
        var dave = new Participant { MepId = m.Id, Nom = "Dave", UtilisateurId = Dave };
        m.Participants.Add(dave);
        return dave;
    }

    private static void Go(Mep m, ContexteVerification contexte, Guid? pilote = null) =>
        Regles.PasserEnExecution(m, pilote ?? Alice, Coches, Criteres, contexte, Maintenant);

    // ---------- Séparation des tâches ----------

    [Theory]
    [InlineData("pilote")]
    [InlineData("suppléant du pilote")]
    [InlineData("responsable technique")]
    public void Ni_pilote_ni_suppleant_ni_responsable_technique_n_approuvent(string role)
    {
        var m = Jeu().Mep;
        var dave = AjouterDave(m);
        switch (role)
        {
            case "pilote": m.PiloteId = dave.Id; m.SuppleantIds.Clear(); break;
            case "suppléant du pilote": m.SuppleantIds.Add(dave.Id); break;
            default: m.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = dave.Id }); break;
        }
        Assert.Equal(role, Regles.EmpechementApprobation(m, Dave));

        // Désignation refusée.
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirApprobateurs(m, [(Claire, "Claire"), (Dave, "Dave")]));
        Assert.StartsWith($"Dave ne peut pas approuver cette MEP ({role}).", ex.Message);
        Assert.Equal([Claire], m.Approbations.Select(a => a.UtilisateurId));

        // Approbation existante (désigné avant de recevoir ce rôle) : gardée, mais elle ne compte pas et bloque le Go.
        m.Approbations.Add(new Approbation { UtilisateurId = Dave, Nom = "Dave", Decision = DecisionApprobation.Approuvee, Le = Maintenant });
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        var refus = Assert.Throws<DroitInsuffisantException>(() => Regles.Decider(m, Dave, DecisionApprobation.Approuvee, "", Maintenant));
        Assert.Equal($"Vous ne pouvez pas approuver cette MEP ({role}) : demander au pilote de désigner un autre approbateur.", refus.Message);
        var anomalie = $"Dave ne peut pas approuver cette MEP ({role}). Le retirer des approbateurs.";
        Assert.Contains(anomalie, Regles.ErreursPlan(m, Maintenant, 1));
        Assert.Equal(DecisionApprobation.Approuvee, m.Approbations.Single(a => a.UtilisateurId == Dave).Decision);
        var pilote = Regles.CompteDuPilote(m)!.Value;
        var go = Assert.Throws<RegleMetierException>(() => Go(m, new ContexteVerification(1), pilote));
        Assert.Contains(anomalie, go.Message);

        // Seule, l'approbation de Dave ne suffit pas non plus.
        m.Approbations.RemoveAll(a => a.UtilisateurId == Claire);
        Assert.Contains("Il faut 1 approbation(s).", Regles.ErreursPlan(m, Maintenant, 1));

        // Retiré des approbateurs : le Go passe avec Claire.
        Regles.DefinirApprobateurs(m, [(Claire, "Claire")]);
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Go(m, new ContexteVerification(1), pilote);
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void Un_compte_relie_au_pilote_a_un_suppleant_ou_a_un_responsable_technique_n_approuve_pas()
    {
        var m = Jeu().Mep;
        var dave = AjouterDave(m);
        m.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = dave.Id });
        var annuaire = Annuaire();
        const string role = "responsable technique, par un compte relié";

        // Sans annuaire, le second compte de Dave est un autre compte ; avec, c'est la même personne.
        Assert.Null(Regles.EmpechementApprobation(m, DaveBis));
        Assert.Equal(role, Regles.EmpechementApprobation(m, DaveBis, annuaire));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirApprobateurs(m, [(DaveBis, "Dave")], annuaire));
        Assert.StartsWith($"Dave ne peut pas approuver cette MEP ({role}).", ex.Message);

        m.Approbations.Add(new Approbation { UtilisateurId = DaveBis, Nom = "Dave" });
        Assert.Throws<DroitInsuffisantException>(() => Regles.Decider(m, DaveBis, DecisionApprobation.Approuvee, "", Maintenant, annuaire));
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, annuaire);
        var contexte = new ContexteVerification(1, annuaire);
        Assert.Contains($"Dave ne peut pas approuver cette MEP ({role}). Le retirer des approbateurs.", Regles.ErreursPlan(m, Maintenant, contexte));
        Assert.Throws<RegleMetierException>(() => Go(m, contexte));

        // Même chose pour un compte relié au pilote (Alice) ou à un suppléant (Bob).
        var annuaire2 = new AnnuairePersonnes(
        [
            new(Alice, null, "Alice", "alice", true), new(Bob, null, "Bob", "bob", true), new(Dave, Alice, "Alice", "alice.adm", true),
            new(DaveBis, Bob, "Bob", "bob.adm", true),
        ], []);
        var n = Jeu().Mep;
        Assert.Equal("pilote, par un compte relié", Regles.EmpechementApprobation(n, Dave, annuaire2));
        Assert.Equal("suppléant du pilote, par un compte relié", Regles.EmpechementApprobation(n, DaveBis, annuaire2));
        Assert.Null(Regles.EmpechementApprobation(n, Claire, annuaire2));
    }

    [Fact]
    public void Dans_une_mep_simplifiee_toute_personne_de_la_mep_est_suppleante_et_n_approuve_pas()
    {
        var m = Jeu().Mep;
        Regles.PasserEnSimplifiee(m);
        Assert.Equal("suppléant du pilote", Regles.EmpechementApprobation(m, Claire));
        Assert.Contains("Claire ne peut pas approuver cette MEP (suppléant du pilote). Le retirer des approbateurs.", Regles.ErreursApprobation(m, 1));
        Assert.Throws<DroitInsuffisantException>(() => Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant));
        // Un compte hors de la MEP approuve.
        Assert.Null(Regles.EmpechementApprobation(m, Dave));
        Regles.DefinirApprobateurs(m, [(Dave, "Dave")]);
        Regles.Decider(m, Dave, DecisionApprobation.Approuvee, "", Maintenant);
        Assert.Empty(Regles.ErreursApprobation(m, 1));
    }

    // ---------- Relecture ----------

    [Fact]
    public void Modifier_le_plan_efface_la_relecture_en_preparation_seulement()
    {
        var m = Jeu().Mep;
        m.ProcedureRelueParId = Bob;
        m.ProcedureRelueLe = Maintenant;
        Assert.True(Regles.ReinitialiserRelecture(m));
        Assert.Null(m.ProcedureRelueParId);
        Assert.Null(m.ProcedureRelueLe);
        Assert.False(Regles.ReinitialiserRelecture(m)); // rien à effacer : rien à journaliser
        Assert.Contains("La procédure doit être relue par une personne de la MEP.", Regles.ErreursPlan(m, Maintenant, 0));

        m.ProcedureRelueParId = Bob;
        m.Phase = Phase.Execution;
        Assert.False(Regles.ReinitialiserRelecture(m));
        Assert.Equal(Bob, m.ProcedureRelueParId);
    }

    // ---------- Résolutions figées ----------

    [Fact]
    public void L_approbation_fige_les_resolutions_et_le_deroule_applique_la_copie()
    {
        var j = Jeu();
        var m = j.Mep;
        var fix = j.Resolutions.Single(r => r.Id == E(m, "E0005").ResolutionIds.Single());
        var (titre, version, corrections) = (fix.Titre, fix.Version, fix.Corrections.Count);
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, resolutions: j.Resolutions);
        Assert.Equal(Maintenant, m.ResolutionsFigeesLe);
        Assert.Equal(Regles.ResolutionsLiees(m).Order(), m.ResolutionsFigees.Select(f => f.ResolutionTypeId).Order());
        Assert.Contains($"« {titre} » v{version}", Regles.DetailResolutionsFigees(m));
        // Déjà figées : une seconde approbation ne les refige pas.
        Assert.False(Regles.FigerResolutions(m, j.Resolutions, Maintenant.AddHours(1)));

        // La bibliothèque change après l'approbation : la MEP garde la version approuvée.
        fix.Titre = "Titre changé après l'approbation";
        fix.Version++;
        fix.Corrections.Add(new Correction { Ordre = 99, Instruction = "Ajoutée après", ResultatAttendu = "x" });
        Go(m, new ContexteVerification(1, Resolutions: j.Resolutions));
        var exec = Regles.DemarrerResolution(m, E(m, "E0005"), fix, Bob, Maintenant);
        Assert.Equal(titre, exec.Titre);
        Assert.Equal(corrections, exec.Corrections.Count);
        Assert.DoesNotContain(exec.Corrections, c => c.Instruction == "Ajoutée après");
    }

    [Fact]
    public void Modifier_la_mep_efface_les_resolutions_figees_et_le_go_les_fige_a_defaut()
    {
        var j = Jeu();
        var m = j.Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, resolutions: j.Resolutions);
        Assert.NotEmpty(m.ResolutionsFigees);
        Assert.True(Regles.ReinitialiserApprobations(m));
        Assert.Empty(m.ResolutionsFigees);
        Assert.Null(m.ResolutionsFigeesLe);

        // Sans approbation exigée, le Go fige les résolutions.
        Go(m, new ContexteVerification(0, Resolutions: j.Resolutions));
        Assert.Equal(Maintenant, m.ResolutionsFigeesLe);
        Assert.NotEmpty(m.ResolutionsFigees);
    }

    [Fact]
    public void Une_resolution_liee_desactivee_supprimee_ou_incomplete_bloque_l_approbation_et_le_go()
    {
        var j = Jeu();
        var m = j.Mep;
        var fix = j.Resolutions.Single(r => r.Id == E(m, "E0005").ResolutionIds.Single());
        var contexte = new ContexteVerification(1, Resolutions: j.Resolutions);

        fix.Actif = false;
        var desactivee = $"E0005 : la résolution « {fix.Titre} » est désactivée, la délier ou la faire réactiver.";
        Assert.Contains(desactivee, Regles.ErreursResolutionsLiees(m, j.Resolutions));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, resolutions: j.Resolutions));
        Assert.Contains(desactivee, ex.Message);
        // Approuvée avant la désactivation : le Go est refusé quand même.
        fix.Actif = true;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, resolutions: j.Resolutions);
        fix.Actif = false;
        Assert.Contains(desactivee, Assert.Throws<RegleMetierException>(() => Go(m, contexte)).Message);
        fix.Actif = true;

        // Incomplète dans la bibliothèque : la copie figée (complète) reste valable ; sans copie, le plan est bloqué.
        fix.VerificationInstruction = "";
        Assert.Empty(Regles.ErreursResolutionsLiees(m, j.Resolutions));
        m.ResolutionsFigees = [];
        m.ResolutionsFigeesLe = null;
        Assert.Contains(Regles.ErreursResolutionsLiees(m, j.Resolutions), e => e.StartsWith($"E0005 : la résolution « {fix.Titre} » est incomplète ("));

        // Supprimée de la bibliothèque.
        var sansFix = j.Resolutions.Where(r => r.Id != fix.Id).ToList();
        Assert.Contains("E0005 : une résolution type liée n'existe plus dans la bibliothèque, la délier.", Regles.ErreursResolutionsLiees(m, sansFix));
        Assert.Throws<RegleMetierException>(() => Go(m, new ContexteVerification(1, Resolutions: sansFix)));
    }

    [Fact]
    public void Une_mep_simplifiee_ne_fige_rien_et_ne_verifie_pas_de_resolutions_liees()
    {
        var j = Jeu();
        var m = j.Mep;
        Regles.PasserEnSimplifiee(m);
        foreach (var r in j.Resolutions) r.Actif = false;
        Assert.Empty(Regles.ErreursResolutionsLiees(m, j.Resolutions));
        Assert.True(Regles.FigerResolutions(m, j.Resolutions, Maintenant));
        Assert.Empty(m.ResolutionsFigees);
    }

    // ---------- Échéance ----------

    [Fact]
    public void L_echeance_se_modifie_avec_un_motif_sauf_a_la_premiere_saisie_en_preparation()
    {
        var m = Jeu().Mep;
        m.EcheanceRepli = null;
        var premiere = Maintenant.AddDays(1);
        Assert.Null(Regles.ModifierEcheance(m, premiere, "", Maintenant));
        Assert.Equal(premiere, m.EcheanceRepli);

        var ex = Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, premiere.AddHours(1), "  ", Maintenant));
        Assert.Contains("motif", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant.AddMinutes(-1), "Trop tard", Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant, "Maintenant", Maintenant));
        Assert.Equal(premiere, Regles.ModifierEcheance(m, premiere.AddHours(1), "Créneau décalé", Maintenant));
        Assert.Equal(premiere.AddHours(1), m.EcheanceRepli);
        // Inchangée : rien à faire, pas de motif réclamé.
        Assert.Equal(premiere.AddHours(1), Regles.ModifierEcheance(m, premiere.AddHours(1), "", Maintenant));
        // La retirer demande aussi un motif.
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, null, "", Maintenant));
        Assert.Equal(premiere.AddHours(1), Regles.ModifierEcheance(m, null, "Plus d'échéance", Maintenant));
        Assert.Null(m.EcheanceRepli);
    }

    [Theory]
    [InlineData(Phase.Cloturee)]
    [InlineData(Phase.Annulee)]
    [InlineData(Phase.RetourArriere)]
    public void L_echeance_ne_se_modifie_plus_une_fois_la_mep_terminee_ou_repliee(Phase phase)
    {
        var m = Jeu().Mep;
        m.Phase = phase;
        var avant = m.EcheanceRepli;
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant.AddDays(2), "Report", Maintenant));
        Assert.Equal(avant, m.EcheanceRepli);
    }

    [Fact]
    public void Ni_modele_ni_mep_sans_retour_arriere_n_ont_d_echeance()
    {
        var m = Jeu().Mep;
        m.RetourArriereActive = false;
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant.AddDays(2), "Report", Maintenant));
        m.RetourArriereActive = true;
        m.EstModele = true;
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant.AddDays(2), "Report", Maintenant));
    }

    [Fact]
    public void Le_journal_de_l_echeance_donne_l_ancienne_et_la_nouvelle_heure_dans_le_fuseau_de_la_mep()
    {
        // 23/10 : heure d'été (UTC+2) ; 26/10 : heure d'hiver (UTC+1).
        var ancienne = new DateTimeOffset(2026, 10, 23, 21, 0, 0, TimeSpan.Zero);
        var nouvelle = new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal("Échéance de retour arrière : 23/10/2026 23:00 → 26/10/2026 02:00 (Europe/Paris) : Migration plus longue",
            Regles.DetailEcheance(ancienne, nouvelle, "Europe/Paris", " Migration plus longue "));
        // Fuseau inconnu (donnée antérieure au contrôle) : fuseau par défaut ; identifiant Windows : son équivalent IANA.
        Assert.Equal("Échéance de retour arrière : aucune → 23/10/2026 23:00 (Europe/Paris)", Regles.DetailEcheance(null, ancienne, "Zone/Inconnue", ""));
        Assert.Equal("Échéance de retour arrière : aucune → 23/10/2026 23:00 (Europe/Paris)", Regles.DetailEcheance(null, ancienne, "Romance Standard Time", ""));
        Assert.Equal("Échéance de retour arrière : aucune → 23/10/2026 17:00 (America/New_York)", Regles.DetailEcheance(null, ancienne, "America/New_York", ""));
        Assert.Equal("Échéance de retour arrière : 23/10/2026 23:00 → aucune (Europe/Paris) : retirée", Regles.DetailEcheance(ancienne, null, "Europe/Paris", "retirée"));
    }

    [Fact]
    public void En_recette_l_echeance_alerte_seulement_et_bloque_le_retour_en_execution_jusqu_au_report()
    {
        var j = Jeu();
        var m = j.Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Go(m, new ContexteVerification(1));
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        E(m, "E0007").PointDeNonRetour = false;
        Regles.PasserEnRecette(m, Alice, Maintenant);
        var echeance = m.EcheanceRepli!.Value;

        Assert.False(Regles.AppliquerEcheance(m, echeance.AddHours(1)));
        Assert.Equal(Phase.Recette, m.Phase);
        Assert.True(Regles.EcheanceDepassee(m, echeance));
        Assert.True(Regles.SignalerEcheance(m, echeance.AddMinutes(1)));
        Assert.False(Regles.SignalerEcheance(m, echeance.AddMinutes(2)));
        var ex = Assert.Throws<RegleMetierException>(() => Regles.RevenirEnExecution(m, Alice, "Corriger", echeance.AddMinutes(3)));
        Assert.Contains("chéance", ex.Message);

        // Le pilote reporte l'échéance : un nouveau signalement redevient possible, le retour en exécution aussi.
        Regles.ModifierEcheance(m, echeance.AddHours(2), "Recette prolongée, accord du métier", echeance.AddMinutes(3));
        Assert.Null(m.EcheanceSignaleeLe);
        Regles.RevenirEnExecution(m, Alice, "Corriger", echeance.AddMinutes(4));
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void En_execution_l_echeance_declenche_toujours_le_retour_arriere()
    {
        var m = Jeu().Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Go(m, new ContexteVerification(1));
        Assert.False(Regles.SignalerEcheance(m, m.EcheanceRepli!.Value));
        Assert.True(Regles.AppliquerEcheance(m, m.EcheanceRepli!.Value));
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Throws<RegleMetierException>(() => Regles.ModifierEcheance(m, Maintenant.AddDays(3), "Trop tard", Maintenant));
    }

    // ---------- Échec de résolution ----------

    [Fact]
    public void Une_verification_finale_en_echec_laisse_aussi_l_etape_en_echec()
    {
        var j = Jeu();
        var m = j.Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant, resolutions: j.Resolutions);
        Go(m, new ContexteVerification(1, Resolutions: j.Resolutions));
        var e5 = E(m, "E0005");
        var fix = j.Resolutions.Single(r => r.Id == e5.ResolutionIds.Single());
        var exec = Regles.DemarrerResolution(m, e5, fix, Bob, Maintenant);
        foreach (var c in exec.Corrections) Regles.ValiderCorrection(m, exec, c.Ordre, ResultatControle.Conforme, "fait", Bob, Maintenant);
        Regles.ValiderVerification(m, exec, ResultatControle.Echec, "toujours en erreur", Bob, Maintenant);
        Assert.Equal(StatutResolution.Echouee, exec.Statut);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal(EtatEtape.Echec, e5.Etat);
        Assert.Equal($"Résolution « {exec.Titre} » en échec sur E0005 : l'étape reste en échec, le pilote décide (retour arrière ou nouvelle tentative)",
            Regles.DetailResolutionEchouee(m, exec, e5));
        m.RetourArriereActive = false;
        Assert.EndsWith("(nouvelle tentative ou correction en avant)", Regles.DetailResolutionEchouee(m, exec, e5));
        m.RetourArriereActive = true;
        // Le pilote décide : retour arrière à la main.
        Regles.DeclencherRetourArriere(m, Alice, "Résolution inefficace", Maintenant);
        Assert.Equal(Alice, m.RetourArriereDeclenchePar);
    }

    // ---------- Incidents : étapes et recette ----------

    private static Mep EnRecette(DonneesDemo.Jeu j)
    {
        var m = j.Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Go(m, new ContexteVerification(1));
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        Regles.PasserEnRecette(m, Alice, Maintenant);
        return m;
    }

    private static void ToutConforme(Mep m)
    {
        foreach (var f in m.FichesRecette)
            foreach (var t in f.Etapes.Where(t => t.Resultat != ResultatControle.Conforme))
                Regles.ValiderEtapeTest(m, f, t, ResultatControle.Conforme, "OK", Claire, false, Maintenant);
    }

    private static readonly Bilan BilanOk = new() { CeQuiAMarche = "Tout", Incidents = "Rien à signaler" };

    [Fact]
    public void Une_recette_sans_echec_donne_une_mep_reussie()
    {
        var m = EnRecette(Jeu());
        ToutConforme(m);
        Regles.Cloturer(m, Alice, Maintenant, BilanOk);
        Assert.Equal((0, 0), (m.EchecsEtapes, m.EchecsRecette));
        Assert.Equal(EtatMep.Reussie, Regles.EtatDeLaMep(m));
        Assert.Equal("réussie", Regles.IssueEnClair(m));
    }

    [Fact]
    public void Un_test_de_recette_en_echec_puis_refait_donne_une_mep_reussie_avec_incidents()
    {
        var m = EnRecette(Jeu());
        var f = m.FichesRecette[0];
        Regles.ValiderEtapeTest(m, f, f.Etapes[0], ResultatControle.Echec, "Erreur 500", Claire, false, Maintenant);
        Assert.Equal(1, m.EchecsRecette);
        Regles.RefaireRecette(m, f, f.Etapes[0], Alice);
        ToutConforme(m);
        Assert.Equal(1, m.EchecsRecette); // un test refait garde son échec
        Regles.Cloturer(m, Alice, Maintenant, BilanOk);
        Assert.Equal(EtatMep.AvecIncidents, Regles.EtatDeLaMep(m));
        Assert.Equal("réussie avec incidents", Regles.IssueEnClair(m));
    }

    [Fact]
    public void Un_test_refuse_dans_le_classeur_importe_compte_comme_incident()
    {
        var m = EnRecette(Jeu());
        var lignes = m.FichesRecette.SelectMany(f => f.Etapes.Select(t => new Regles.LigneValidation(f.Id, t.Id, $"{f.Titre} {t.Ordre}", "", "", "", null))).ToList();
        lignes[0] = lignes[0] with { Validation = "Refusé", Constate = "Erreur 500", ValidePar = "Paul" };
        lignes[1] = lignes[1] with { Validation = "Validé", Constate = "OK", ValidePar = "Paul" };
        var analyse = Regles.AnalyserValidation(m, m.Id, Regles.VersionRecette(m), lignes);
        Assert.Empty(analyse.Erreurs);
        Regles.AppliquerValidation(m, analyse, Alice, false, Maintenant);
        Assert.Equal(1, m.EchecsRecette);
    }

    [Fact]
    public void Un_echec_d_etape_repris_compte_comme_incident()
    {
        var m = Jeu().Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Go(m, new ContexteVerification(1));
        // E0005 est en échec dans la démonstration ; une nouvelle étape en échec s'ajoute au compteur.
        var avant = m.EchecsEtapes;
        Regles.Demarrer(m, E(m, "E0004"), Alice, Maintenant);
        Regles.Terminer(m, E(m, "E0004"), EtatEtape.Echec, "KO", false, Alice, Maintenant);
        Assert.Equal(avant + 1, m.EchecsEtapes);
        Regles.AutoriserReprise(m, E(m, "E0004"), Alice, Maintenant);
        Assert.Equal(avant + 1, m.EchecsEtapes);
        Assert.Equal(EtatMep.AvecIncidents, Regles.EtatDe(Phase.Cloturee, IssueMep.Reussie, Regles.Incidents(m)));
        Assert.Equal(EtatMep.Reussie, Regles.EtatDe(Phase.Cloturee, IssueMep.Reussie, 0));
    }
}
