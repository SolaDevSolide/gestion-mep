using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Sorties du cycle de vie (#93) : annuler, reporter, revenir en préparation avec motif, corriger en avant depuis la recette,
/// clôturer en échec, passer le pilotage ou le reprendre en secours, supprimer une MEP jamais lancée.
/// </summary>
public class ReglesCycleDeVieTests
{
    // Portail de démonstration : Alice pilote, Bob suppléant, Claire approbatrice ; Dave n'est pas de la MEP.
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Participant P(Mep m, Guid compte) => m.Participants.Single(p => p.UtilisateurId == compte);

    private static Mep EnExecution(DateTimeOffset? go = null)
    {
        var m = Demo();
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, go ?? Maintenant);
        return m;
    }

    /// <summary>Déploiement réussi (états posés directement), passage en recette ; le point de non-retour est franchi si demandé.</summary>
    private static Mep EnRecette(bool nonRetourFranchi)
    {
        var m = Demo();
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.PointDeNonRetour = false;
        if (nonRetourFranchi) E(m, "E0007").PointDeNonRetour = true;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement))
        {
            e.Etat = EtatEtape.Reussie;
            e.DebutReel ??= Maintenant;
        }
        Regles.PasserEnRecette(m, Alice, Maintenant.AddHours(1));
        return m;
    }

    private static Mep Cloturee()
    {
        var m = EnRecette(nonRetourFranchi: false);
        foreach (var t in m.FichesRecette.SelectMany(f => f.Etapes)) t.Resultat = ResultatControle.Conforme;
        Regles.Cloturer(m, Alice, Maintenant.AddHours(2), new Bilan { CeQuiAMarche = "Tout", Incidents = "Rien" });
        return m;
    }

    private static Bilan BilanComplet() => new() { CeQuiAMarche = "Sauvegardes.", Incidents = "Restauration impossible.", Actions = "" };

    // ---------- Annuler ----------

    [Fact]
    public void Annuler_en_preparation_passe_la_mep_en_lecture_seule_et_leve_le_report()
    {
        var m = Demo();
        Regles.Reporter(m, Bob, "Gel des changements", new DateOnly(2026, 11, 5), Maintenant);
        Regles.Annuler(m, Alice, estAdmin: false, "  Projet abandonné  ", null, Maintenant);
        Assert.Equal(Phase.Annulee, m.Phase);
        Assert.Equal("Projet abandonné", m.Annulation!.Motif);
        Assert.Equal("", m.Annulation.EtatProduction);
        Assert.Equal((Alice, Maintenant), (m.Annulation.ParId, m.Annulation.Le));
        Assert.Null(m.Report);
        Assert.Equal(EtatMep.Annulee, Regles.EtatDeLaMep(m, 0));
        Assert.True(Regles.EstTerminee(m));
        Assert.Throws<RegleMetierException>(() => Regles.ExigerPreparation(m));
        Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Alice, false, "Encore", null, Maintenant));
        // Elle reste duplicable : la copie repart en préparation, sans annulation.
        var copie = Regles.Dupliquer(m, commeModele: false, Alice, Maintenant);
        Assert.Equal(Phase.Preparation, copie.Phase);
        Assert.Null(copie.Annulation);
    }

    [Fact]
    public void Annuler_exige_un_motif_et_les_droits_du_pilote_ou_d_un_administrateur()
    {
        var m = Demo();
        Assert.Contains("motif", Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Alice, false, " ", null, Maintenant)).Message);
        Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Claire, estAdmin: false, "Motif", null, Maintenant));
        // Un administrateur qui ne pilote pas la MEP peut l'annuler.
        Regles.Annuler(m, Dave, estAdmin: true, "Doublon de la MEP 4.0", null, Maintenant);
        Assert.Equal(Dave, m.Annulation!.ParId);
    }

    [Fact]
    public void Apres_un_go_on_revient_d_abord_en_preparation_et_l_etat_laisse_en_production_est_obligatoire()
    {
        var go = Maintenant.AddHours(-2);
        var m = EnExecution(go);
        var refus = Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Alice, false, "Abandon", "Rien", Maintenant));
        Assert.Contains("Revenir d'abord en préparation", refus.Message);

        Regles.RevenirEnPreparation(m, Alice, "Prestataire injoignable");
        Assert.Equal(go, m.PremierGoLe);
        Assert.Contains("état laissé en production", Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Alice, false, "Abandon", " ", Maintenant)).Message);
        Regles.Annuler(m, Alice, false, "Abandon", "Sauvegardes faites, rien de déployé.", Maintenant);
        Assert.Equal("Sauvegardes faites, rien de déployé.", m.Annulation!.EtatProduction);
        Assert.Equal("MEP annulée : Abandon. État laissé en production : Sauvegardes faites, rien de déployé.", Regles.DetailAnnulation(m.Annulation));
    }

    [Fact]
    public void Une_mep_cloturee_ou_un_modele_ne_s_annulent_pas()
    {
        var m = Cloturee();
        Assert.Contains("clôturée", Assert.Throws<RegleMetierException>(() => Regles.Annuler(m, Alice, true, "x", "y", Maintenant)).Message);
        var modele = Demo();
        modele.EstModele = true;
        Assert.Throws<RegleMetierException>(() => Regles.Annuler(modele, Alice, true, "x", null, Maintenant));
    }

    // ---------- Reporter, reprendre ----------

    [Fact]
    public void Reporter_marque_la_preparation_sans_la_fermer_et_le_go_leve_le_report()
    {
        var m = Demo();
        Regles.Reporter(m, Bob, "Gel des changements", null, Maintenant);
        Assert.Equal(Phase.Preparation, m.Phase);
        Assert.Equal(EtatMep.Reportee, Regles.EtatDeLaMep(m, 0));
        Assert.Equal("MEP reportée sans date : Gel des changements", Regles.DetailReport(m.Report!));
        // Un nouveau report remplace le précédent ; la préparation reste modifiable.
        Regles.Reporter(m, Alice, "Fenêtre refusée", new DateOnly(2026, 11, 5), Maintenant.AddHours(1));
        Assert.Equal("MEP reportée au 05/11/2026 : Fenêtre refusée", Regles.DetailReport(m.Report!));
        Regles.ExigerPreparation(m);

        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        Assert.Null(m.Report);
        Assert.Equal(EtatMep.Execution, Regles.EtatDeLaMep(m, 0));
    }

    [Fact]
    public void Reprendre_la_preparation_leve_le_report_et_les_refus_sont_explicites()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.Reporter(m, Claire, "x", null, Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.Reporter(m, Alice, "", null, Maintenant));
        Assert.Contains("pas reportée", Assert.Throws<RegleMetierException>(() => Regles.ReprendrePreparation(m, Alice)).Message);
        Regles.Reporter(m, Alice, "Gel", null, Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.ReprendrePreparation(m, Claire));
        Regles.ReprendrePreparation(m, Bob);
        Assert.Null(m.Report);
        Assert.Equal(EtatMep.Preparation, Regles.EtatDeLaMep(m, 0));

        var x = EnExecution();
        Assert.Contains("en préparation", Assert.Throws<RegleMetierException>(() => Regles.Reporter(x, Alice, "x", null, Maintenant)).Message);
    }

    [Fact]
    public void Revenir_en_preparation_exige_un_motif_et_garde_le_premier_go()
    {
        var premier = Maintenant.AddHours(-3);
        var m = EnExecution(premier);
        Assert.Contains("motif", Assert.Throws<RegleMetierException>(() => Regles.RevenirEnPreparation(m, Alice, "  ")).Message);
        Regles.RevenirEnPreparation(m, Alice, "Étape mal écrite");
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        Assert.Equal(premier, m.PremierGoLe);
        Assert.Equal(Maintenant, m.DemarrageAutoriseLe);
    }

    // ---------- Corriger en avant ----------

    [Fact]
    public void Revenir_en_execution_depuis_la_recette_remet_les_tests_en_echec_a_faire()
    {
        var m = EnRecette(nonRetourFranchi: true);
        var fiche = m.FichesRecette.OrderBy(f => f.Ordre).First();
        var test = fiche.Etapes.OrderBy(t => t.Ordre).First();
        test.Resultat = ResultatControle.Echec;
        test.ParId = Bob;
        test.Le = Maintenant;
        var conforme = m.FichesRecette.OrderBy(f => f.Ordre).Last().Etapes.First();
        conforme.Resultat = ResultatControle.Conforme;

        Assert.Throws<RegleMetierException>(() => Regles.RevenirEnExecution(m, Alice, ""));
        Assert.Throws<RegleMetierException>(() => Regles.RevenirEnExecution(m, Claire, "Correctif"));
        var remis = Regles.RevenirEnExecution(m, Bob, "Index manquant");
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal([$"{Regles.NomRecette(fiche)}, étape {test.Ordre}"], remis);
        var relu = m.FichesRecette.Single(f => f.Ordre == fiche.Ordre).Etapes.Single(t => t.Ordre == test.Ordre);
        Assert.Null(relu.Resultat);
        Assert.Null(relu.ParId);
        Assert.Equal(ResultatControle.Conforme, m.FichesRecette.OrderBy(f => f.Ordre).Last().Etapes.First().Resultat);
        Assert.Throws<RegleMetierException>(() => Regles.RevenirEnExecution(m, Alice, "Encore"));
    }

    [Fact]
    public void Une_etape_corrective_s_ajoute_en_execution_a_la_suite_du_plan()
    {
        var m = EnExecution();
        var api = m.Systemes.Single(s => s.Nom == "API").Id;
        Etape Corrective() => new()
        {
            MepId = m.Id, Action = "Créer l'index manquant", SystemeIds = [api], Instructions = "psql -f index.sql", ResultatAttendu = "Index présent.",
        };
        var dernieres = m.EtapesDu(TypePlan.Deploiement).Where(e => m.EtapesDu(TypePlan.Deploiement).All(x => !x.Dependances.Contains(e.Id))).Select(e => e.Id).ToList();
        var e = Corrective();
        Regles.AjouterEtapeCorrective(m, e, Bob);
        Assert.Equal("E0010", e.Code);
        Assert.True(e.AjouteeApresGo);
        Assert.False(e.PointDeNonRetour);
        Assert.Equal(dernieres, e.Dependances);
        Assert.Contains(e, m.Etapes);
        // Environnements : ceux de production du système dans la MEP (à défaut, tous).
        var envApi = m.Systemes.Single(s => s.Id == api).Environnements;
        var attendus = envApi.Any(x => x.Production) ? envApi.Where(x => x.Production) : envApi;
        Assert.Equal(attendus.Select(x => x.Nom), e.Environnements.Select(x => x.Nom));
        Assert.All(e.Environnements, x => Assert.Equal(api, x.SystemeId));
        Assert.NotEmpty(e.Environnements);

        Assert.Throws<RegleMetierException>(() => Regles.AjouterEtapeCorrective(m, Corrective(), Claire));
        var incomplete = Corrective();
        incomplete.ResultatAttendu = "";
        Assert.Contains("résultat attendu", Assert.Throws<RegleMetierException>(() => Regles.AjouterEtapeCorrective(m, incomplete, Alice)).Message);
        var preparation = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.AjouterEtapeCorrective(preparation, Corrective(), Alice));
        var recette = EnRecette(nonRetourFranchi: false);
        Assert.Contains("revenir d'abord en exécution",
            Assert.Throws<RegleMetierException>(() => Regles.AjouterEtapeCorrective(recette, Corrective(), Alice)).Message);
    }

    // ---------- Clôturer en échec ----------

    [Fact]
    public void Echec_depuis_la_recette_refuse_tant_que_le_retour_arriere_est_possible()
    {
        var m = EnRecette(nonRetourFranchi: false);
        Assert.Null(Regles.RetourArriereImpossible(m));
        var refus = Assert.Throws<RegleMetierException>(() =>
            Regles.Cloturer(m, Alice, Maintenant, BilanComplet(), echec: true, "Service dégradé"));
        Assert.Contains("retour arrière est encore possible", refus.Message);
        Assert.Equal(Phase.Recette, m.Phase);
    }

    [Fact]
    public void Echec_depuis_la_recette_apres_le_point_de_non_retour_exige_etat_du_service_et_bilan()
    {
        var m = EnRecette(nonRetourFranchi: true);
        m.BilanActive = false;
        Assert.StartsWith("Point de non-retour franchi (E0007", Regles.RetourArriereImpossible(m));
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanComplet(), echec: true, " "));
        // Bilan obligatoire même s'il est désactivé pour cette MEP.
        Assert.Contains("Bilan obligatoire", Assert.Throws<RegleMetierException>(() =>
            Regles.Cloturer(m, Alice, Maintenant, null, echec: true, "Service dégradé")).Message);
        Regles.Cloturer(m, Alice, Maintenant, BilanComplet(), echec: true, " Lecture seule ", " INC-42 ");
        Assert.Equal((Phase.Cloturee, IssueMep.Echec), (m.Phase, m.Issue));
        Assert.Equal(("Lecture seule", "INC-42"), (m.EtatService, m.ReferenceIncident));
        Assert.Equal(EtatMep.Echec, Regles.EtatDeLaMep(m, 0));
        Assert.Equal("échec (service dégradé)", Regles.IssueEnClair(m, 0));
    }

    [Fact]
    public void Echec_depuis_un_retour_arriere_incomplet_mais_pas_pendant_une_etape_en_cours()
    {
        var m = EnExecution();
        Regles.DeclencherRetourArriere(m, Alice, "Échec du déploiement", Maintenant);
        var r1 = E(m, "R0001");
        r1.Etat = EtatEtape.EnCours;
        Assert.Contains("encore en cours", Assert.Throws<RegleMetierException>(() =>
            Regles.Cloturer(m, Alice, Maintenant, BilanComplet(), echec: true, "Dégradé")).Message);
        r1.Etat = EtatEtape.Echec;
        // Sans échec, le service n'est pas rétabli : la clôture « repliée » est refusée.
        Assert.Contains("clôturer en échec", Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanComplet())).Message);
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Claire, Maintenant, BilanComplet(), echec: true, "Dégradé"));
        Regles.Cloturer(m, Bob, Maintenant, BilanComplet(), echec: true, "Dégradé");
        Assert.Equal(IssueMep.Echec, m.Issue);
        Assert.Equal("", m.ReferenceIncident);
    }

    [Fact]
    public void Echec_refuse_en_execution()
    {
        var m = EnExecution();
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanComplet(), echec: true, "Dégradé"));
    }

    // ---------- Pilotage ----------

    [Fact]
    public void Passer_le_pilotage_apres_le_go_l_ancien_pilote_devient_suppleant()
    {
        var m = EnExecution();
        Regles.ChangerPilote(m, Alice, estAdmin: false, P(m, Claire), ancienPiloteSuppleant: true, null, "Relève de 02:00", Maintenant, out var secours);
        Assert.False(secours);
        Assert.Equal(P(m, Claire).Id, m.PiloteId);
        Assert.Equal([P(m, Alice).Id, P(m, Bob).Id], m.SuppleantIds);
        Assert.Null(m.PilotageSecours);

        // Sans garder l'ancien pilote ni Bob.
        Regles.ChangerPilote(m, Claire, false, P(m, Bob), ancienPiloteSuppleant: false, [], "Relève", Maintenant, out _);
        Assert.Equal(P(m, Bob).Id, m.PiloteId);
        Assert.Empty(m.SuppleantIds);
    }

    [Fact]
    public void Pilotage_de_secours_par_un_administrateur_trace_et_retire_a_la_passation_suivante()
    {
        var m = EnExecution();
        var dave = new Participant { MepId = m.Id, Nom = "Dave", Role = "Astreinte", UtilisateurId = Dave };
        m.Participants.Add(dave);
        Assert.Throws<RegleMetierException>(() => Regles.ChangerPilote(m, Dave, estAdmin: false, dave, true, null, "Secours", Maintenant, out _));
        Regles.ChangerPilote(m, Dave, estAdmin: true, dave, ancienPiloteSuppleant: true, null, " Alice et Bob injoignables ", Maintenant, out var secours);
        Assert.True(secours);
        Assert.Equal(dave.Id, m.PiloteId);
        Assert.Equal([P(m, Alice).Id, P(m, Bob).Id], m.SuppleantIds);
        var s = m.PilotageSecours!;
        Assert.Equal((Dave, "Alice et Bob injoignables", P(m, Alice).Id, dave.Id), (s.ParId, s.Motif, s.AncienPiloteId, s.NouveauPiloteId));

        // Alice, revenue et suppléante, reprend la main par une passation : le secours n'est plus affiché.
        Regles.ChangerPilote(m, Alice, false, P(m, Alice), true, null, "De retour", Maintenant, out secours);
        Assert.False(secours);
        Assert.Null(m.PilotageSecours);
        Assert.Contains(dave.Id, m.SuppleantIds);
    }

    [Fact]
    public void Changer_de_pilote_est_refuse_en_preparation_une_fois_terminee_sans_motif_ou_sans_compte()
    {
        var preparation = Demo();
        Assert.Contains("section Personnes", Assert.Throws<RegleMetierException>(() =>
            Regles.ChangerPilote(preparation, Alice, false, P(preparation, Claire), true, null, "x", Maintenant, out _)).Message);

        var m = EnExecution();
        Assert.Throws<RegleMetierException>(() => Regles.ChangerPilote(m, Alice, false, P(m, Claire), true, null, " ", Maintenant, out _));
        Assert.Contains("déjà le pilote", Assert.Throws<RegleMetierException>(() =>
            Regles.ChangerPilote(m, Alice, false, P(m, Alice), true, null, "x", Maintenant, out _)).Message);
        var sansCompte = new Participant { MepId = m.Id, Nom = "Prestataire" };
        m.Participants.Add(sansCompte);
        Assert.Contains("aucun compte", Assert.Throws<RegleMetierException>(() =>
            Regles.ChangerPilote(m, Alice, false, sansCompte, true, null, "x", Maintenant, out _)).Message);

        var close = Cloturee();
        Assert.Contains("terminée", Assert.Throws<RegleMetierException>(() =>
            Regles.ChangerPilote(close, Dave, true, P(close, Claire), true, null, "x", Maintenant, out _)).Message);
    }

    // ---------- Supprimer ----------

    [Fact]
    public void Suppression_reservee_aux_mep_jamais_lancees_avec_motif_et_nom_saisi()
    {
        var m = Demo();
        var nom = Regles.NomComplet(m);
        Assert.Equal($"{m.Outil} {m.VersionCible}", nom);
        Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppression(m, " ", nom));
        Assert.Contains("saisir exactement", Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppression(m, "Erreur", m.Outil)).Message);
        Regles.VerifierSuppression(m, "Créée par erreur", $"  {nom} ");

        // Un Go, même suivi d'un retour en préparation puis d'une annulation, interdit la suppression.
        var lancee = EnExecution();
        Regles.RevenirEnPreparation(lancee, Alice, "Problème");
        Assert.Contains("déjà eu un Go", Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppression(lancee, "x", nom)).Message);
        Regles.Annuler(lancee, Alice, false, "Abandon", "Rien de déployé", Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.VerifierSuppression(lancee, "x", nom));

        // Une MEP annulée sans avoir été lancée se supprime.
        var annulee = Demo();
        Regles.Annuler(annulee, Alice, false, "Doublon", null, Maintenant);
        Regles.VerifierSuppression(annulee, "Doublon", nom);
    }
}
