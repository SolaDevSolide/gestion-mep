using System.Text.RegularExpressions;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// MEP de démonstration dans chaque état du cycle de vie (DonneesDemo.Scenarios) : construites en jouant les règles, elles
/// doivent être dans l'état annoncé et cohérentes avec ces mêmes règles (plan, disponibilités, horodatages, journal).
/// </summary>
public class DonneesDemoTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 12, 14, 30, 0, TimeSpan.Zero);

    private static List<DonneesDemo.MepDemo> Scenarios(DateTimeOffset? maintenant = null)
    {
        var t = maintenant ?? Maintenant;
        var jeu = DonneesDemo.Creer(Alice, Bob, Claire, t);
        return DonneesDemo.Scenarios(Alice, Bob, Claire, jeu.Systemes, jeu.Resolutions, jeu.Criteres, t);
    }

    private static Mep Mep(string outil) => Scenarios().Single(s => s.Mep.Outil == outil).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static string Dispo(Mep m, string code) => Regles.Disponibilite(m, E(m, code), Maintenant).Libelle;

    [Fact]
    public void Huit_mep_une_par_etat_sans_modele()
    {
        var meps = Scenarios().Select(s => s.Mep).ToList();
        Assert.Equal(8, meps.Count);
        Assert.Equal(8, meps.Select(m => m.Outil).Distinct().Count());
        Assert.All(meps, m => Assert.False(m.EstModele));
        Assert.Equal(
            [Phase.Preparation, Phase.Preparation, Phase.Execution, Phase.Recette, Phase.RetourArriere, Phase.Cloturee, Phase.Cloturee, Phase.Execution],
            meps.Select(m => m.Phase).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23 * 60 + 50)] // amorçage juste avant minuit (UTC) : échéances et horloge restent cohérentes
    public void Chaque_mep_est_coherente_avec_les_regles(int minutesDansLaJournee)
    {
        var maintenant = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).AddMinutes(minutesDansLaJournee);
        foreach (var (mep, journal) in Scenarios(maintenant))
        {
            // Graphe valide, identifiants et participants cohérents.
            Assert.Null(Regles.ErreurGraphe(mep, TypePlan.Deploiement));
            Assert.Null(Regles.ErreurGraphe(mep, TypePlan.RetourArriere));
            Assert.All(mep.Etapes, e => Assert.Equal(mep.Id, e.MepId));
            Assert.All(mep.Participants, p => Assert.NotNull(p.UtilisateurId));
            Assert.True(Regles.CompteDuPilote(mep) is not null, mep.Outil);
            // Étapes démarrées : exécutant, début, révision ; terminées : fin après le début.
            foreach (var e in mep.Etapes)
            {
                Regles.Disponibilite(mep, e, maintenant); // ne lève jamais
                if (e.Etat == EtatEtape.AFaire && e.Revision == 0)
                {
                    Assert.Null(e.ExecutantId);
                    continue;
                }
                Assert.Contains(mep.Participants, p => p.Id == e.ExecutantId);
                Assert.NotNull(e.DebutReel);
                Assert.True(e.DebutReel <= maintenant, $"{mep.Outil} {e.Code}");
                Assert.True(e.Revision >= 1);
                if (e.Etat is EtatEtape.Reussie or EtatEtape.Echec) Assert.True(e.FinReelle >= e.DebutReel, $"{mep.Outil} {e.Code}");
            }
            // Journal : rattaché à la MEP, chronologique, dans le passé, commencé par la création.
            Assert.NotEmpty(journal);
            Assert.All(journal, ev => Assert.Equal(mep.Id, ev.MepId));
            Assert.Equal("mep.creee", journal[0].Type);
            Assert.Equal(mep.CreeLe, journal[0].Quand);
            Assert.True(journal.Zip(journal.Skip(1)).All(p => p.First.Quand <= p.Second.Quand), mep.Outil);
            Assert.True(journal[^1].Quand <= maintenant, mep.Outil);
            Assert.True(mep.Version >= 1);
            // Aucun retour arrière automatique dû à l'échéance, ni à l'amorçage ni ensuite.
            Assert.NotEqual(Regles.MotifEcheance, mep.RetourArriereMotif);
            var phase = mep.Phase;
            Assert.False(Regles.AppliquerEcheance(mep, maintenant), mep.Outil);
            Assert.False(Regles.AppliquerEcheance(mep, maintenant.AddHours(24)), mep.Outil);
            Assert.Equal(phase, mep.Phase);
            if (mep.Phase is Phase.Execution or Phase.Recette) Assert.True(mep.EcheanceRepli > maintenant.AddDays(1), mep.Outil);
            if (mep.Phase != Phase.Preparation)
            {
                Assert.NotNull(mep.DemarrageAutoriseLe);
                Assert.Equal(Criteres().Count, mep.GoNoGo.Count);
                Assert.True(mep.DemarrageAutoriseLe < mep.EcheanceRepli);
                Assert.All(mep.Approbations, a => Assert.Equal(DecisionApprobation.Approuvee, a.Decision));
            }
        }
    }

    private static List<CritereGoNoGo> Criteres() => DonneesDemo.CriteresGoNoGo();

    [Fact]
    public void Preparation_incomplete_montre_ses_anomalies()
    {
        var m = Mep(DonneesDemo.OutilIncomplete);
        Assert.Equal(Phase.Preparation, m.Phase);
        var erreurs = Regles.ErreursPlan(m, Maintenant, DonneesDemo.ApprobationsMinimum);
        foreach (var attendue in new[]
                 {
                     "Retour arrière : préciser quand le déclencher.",
                     "Sauvegarde : indiquer si BATCH doit être sauvegardé.", "Sauvegarde de WEB non requise : justifier.",
                     "E0003 : compléter Instructions exactes.", "E0005 : remplacer l'action d'exemple par une action réelle.",
                     "Recette « Export nocturne » : ajouter au moins une étape de test.", "La procédure doit être relue par un opérateur.",
                     "Il faut 1 approbation(s).",
                     "Section « Contrôles de bout en bout » dans le bac : la placer dans le plan ou la supprimer.",
                 })
            Assert.Contains(attendue, erreurs);
        // Sections : deux placées et reliées par leurs cercles, une dans le bac ; les préalables effectifs sont ceux du plan.
        Assert.Equal(["Tables factures", "Export et téléchargement", "Contrôles de bout en bout"], m.Sections.Select(s => s.Titre).ToArray());
        string[] Prealables(string code) => m.Etapes.Single(e => e.Code == code).Dependances
            .Select(id => m.Etapes.Single(e => e.Id == id).Code).Order().ToArray();
        Assert.Equal(["E0001"], Prealables("E0002"));
        Assert.Equal(["E0001"], Prealables("E0003"));
        Assert.Equal(["E0001"], Prealables("E0004"));
        Assert.Equal(["E0002", "E0003", "E0004"], Prealables("E0005"));
        Assert.Equal(["E0001"], m.Etapes.Single(e => e.Code == "E0004").DependancesDerivees.Select(id => m.Etapes.Single(e => e.Id == id).Code));
        Assert.Contains(Regles.Avertissements(m), a => a.StartsWith("Section « Contrôles de bout en bout », cercle d'entrée « Début »", StringComparison.Ordinal));
        Assert.Contains(Regles.Avertissements(m), a => a.Contains("Claire sera absent", StringComparison.Ordinal));
        // L'échéance de retour arrière est facultative : son absence n'est ni une anomalie ni un blocage des étapes.
        Assert.Null(m.EcheanceRepli);
        Assert.DoesNotContain(erreurs, e => e.Contains("échéance", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual("Échéance à renseigner", Dispo(m, "E0001"));
    }

    [Fact]
    public void Preparation_prete_pour_le_go()
    {
        var m = Mep(DonneesDemo.OutilPrete);
        Assert.Equal(Phase.Preparation, m.Phase);
        Assert.Empty(Regles.ErreursPlan(m, Maintenant, DonneesDemo.ApprobationsMinimum));
        Assert.Equal(DecisionApprobation.Approuvee, Assert.Single(m.Approbations).Decision);
        Assert.Equal(Alice, m.ProcedureRelueParId);
        Assert.Equal(Bob, Regles.CompteDuPilote(m));
        Assert.True(Regles.EstPilote(m, Alice)); // suppléante
        // Le Go passe avec tous les critères cochés.
        var criteres = Criteres();
        Regles.PasserEnExecution(m, Bob, criteres.Select(c => c.Id).ToList(), criteres, 1, Maintenant);
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void Execution_en_cours_avec_un_echec_a_resoudre_et_un_point_de_non_retour_a_venir()
    {
        var m = Mep(DonneesDemo.OutilExecution);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal("Terminée", Dispo(m, "E0001"));
        Assert.Equal("Terminée", Dispo(m, "E0003"));
        Assert.Equal("En cours", Dispo(m, "E0004"));
        // Claire applique la résolution type de la purge du cache CDN (une correction faite, vérification à faire).
        Assert.Equal("Résolution en cours", Dispo(m, "E0005"));
        Assert.NotEmpty(E(m, "E0005").ResolutionIds);
        Assert.NotEmpty(E(m, "E0005").Commentaire);
        var pnr = E(m, "E0006");
        Assert.True(pnr.PointDeNonRetour);
        Assert.Null(pnr.NonRetourAutoriseParId);
        Assert.False(Regles.NonRetourFranchi(m));
        Assert.StartsWith("Attend", Dispo(m, "E0006"));
        Assert.Equal(Bob, m.Participants.Single(p => p.Id == E(m, "E0004").ExecutantId).UtilisateurId);
        var resolution = Assert.Single(m.Resolutions);
        Assert.Equal((StatutResolution.EnCours, E(m, "E0005").Id), (resolution.Statut, resolution.EtapeId));
        // Deux étapes en cours (Bob dépasse sa durée), le plan rangé en cinq sections.
        Assert.Equal(["E0004", "E0009"], m.Etapes.Where(e => e.Etat == EtatEtape.EnCours).Select(e => e.Code).Order());
        Assert.Equal(5, m.Sections.Count);
        Assert.All(m.Etapes.Where(e => e.Plan == TypePlan.Deploiement && e.Code != "E0006"), e => Assert.NotNull(e.SectionId));
    }

    [Fact]
    public void Mep_simplifiee_en_execution_avec_les_resolutions_de_ses_systemes()
    {
        var jeu = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        var m = DonneesDemo.Scenarios(Alice, Bob, Claire, jeu.Systemes, jeu.Resolutions, jeu.Criteres, Maintenant)
            .Single(s => s.Mep.Outil == DonneesDemo.OutilSimplifiee).Mep;
        Assert.Equal(TypeMep.Simplifiee, m.Type);
        Assert.Equal(Phase.Execution, m.Phase);
        Assert.Equal("", m.VersionCible);
        Assert.Empty(m.SuppleantIds);
        // Environnements par système et objets impactés (avec leur version) comme dans une MEP complète.
        Assert.All(m.Systemes, s => Assert.NotEmpty(s.Environnements));
        Assert.All(m.Etapes, e => Assert.NotEmpty(e.Environnements));
        Assert.Equal(["messagerie-api.jar 1.8.1", "parametres adresse du support corrigée", "portail-web.tar.gz formulaire corrigé"],
            m.ObjetsImpactes.Select(o => $"{o.Nom} {o.Version}").Order(StringComparer.Ordinal));
        // Claire pilote ; Alice et Bob sont suppléants sans avoir été choisis.
        Assert.Equal(Claire, Regles.CompteDuPilote(m));
        Assert.True(Regles.EstPilote(m, Alice) && Regles.EstPilote(m, Bob));
        Assert.All(m.Etapes, e =>
        {
            Assert.Null(e.Criticite);
            Assert.Null(e.DureeEstimeeMinutes);
            Assert.Empty(e.ResolutionIds);
        });
        Assert.Equal("Arrêt / résolution", Dispo(m, "E0002"));
        // E0002 touche API et BDD : les résolutions de ces systèmes sont proposées, pas celle du WEB.
        var proposees = jeu.Resolutions.Where(r => Regles.EstResolutionProposee(m, E(m, "E0002"), r)).Select(r => r.Titre).ToList();
        Assert.Equal(
            ["API + BDD : connexion refusée après bascule", "API : réponse de test absente", "BDD : espace disque insuffisant pour la sauvegarde",
             "BDD : utilisateur applicatif retiré des droits"],
            proposees.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Recette_en_cours_partiellement_validee_apres_une_resolution_reussie()
    {
        var m = Mep(DonneesDemo.OutilRecette);
        Assert.Equal(Phase.Recette, m.Phase);
        Assert.All(m.EtapesDu(TypePlan.Deploiement), e => Assert.Equal(EtatEtape.Reussie, e.Etat));
        var resolution = Assert.Single(m.Resolutions);
        Assert.Equal(StatutResolution.Reussie, resolution.Statut);
        Assert.Equal(E(m, "E0002").Id, resolution.EtapeId);
        var tests = m.FichesRecette.SelectMany(f => f.Etapes).ToList();
        Assert.Contains(tests, t => t.Resultat == ResultatControle.Conforme);
        Assert.Contains(tests, t => t.Resultat == ResultatControle.Echec);
        Assert.Contains(tests, t => t.Resultat is null);
        Assert.All(tests.Where(t => t.Resultat is not null), t => Assert.False(string.IsNullOrWhiteSpace(t.Observation)));
        Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, new Bilan { CeQuiAMarche = "x", Incidents = "y" }));
    }

    [Fact]
    public void Retour_arriere_en_cours_declenche_par_une_resolution_en_echec()
    {
        var m = Mep(DonneesDemo.OutilRetourArriere);
        Assert.Equal(Phase.RetourArriere, m.Phase);
        Assert.Null(m.RetourArriereDeclenchePar);
        Assert.Contains("Résolution", m.RetourArriereMotif, StringComparison.Ordinal);
        Assert.Equal(StatutResolution.Echouee, Assert.Single(m.Resolutions).Statut);
        Assert.Equal(EtatEtape.Reussie, E(m, "R0001").Etat);
        Assert.Equal(EtatEtape.EnCours, E(m, "R0002").Etat);
        Assert.Equal("Attend R0002", Dispo(m, "R0003"));
        Assert.Equal("Retour arrière déclenché", Dispo(m, "E0005"));
    }

    [Fact]
    public void Cloturees_reussie_et_repliee_avec_bilan()
    {
        var reussie = Mep(DonneesDemo.OutilReussie);
        Assert.Equal((Phase.Cloturee, IssueMep.Reussie), (reussie.Phase, reussie.Issue));
        Assert.NotNull(reussie.Bilan);
        Assert.True(Regles.NonRetourFranchi(reussie));
        Assert.Equal(Alice, E(reussie, "E0004").NonRetourAutoriseParId);
        Assert.All(reussie.FichesRecette.SelectMany(f => f.Etapes), t => Assert.Equal(ResultatControle.Conforme, t.Resultat));
        Assert.True(reussie.ClotureeLe < Maintenant.AddDays(-5));

        var repliee = Mep(DonneesDemo.OutilRepliee);
        Assert.Equal((Phase.Cloturee, IssueMep.Repliee), (repliee.Phase, repliee.Issue));
        Assert.NotNull(repliee.Bilan);
        Assert.Equal(Bob, repliee.RetourArriereDeclenchePar);
        Assert.Equal(Bob, repliee.ClotureeParId);
        Assert.All(repliee.EtapesDu(TypePlan.RetourArriere), e => Assert.Equal(EtatEtape.Reussie, e.Etat));
        Assert.Equal(EtatEtape.Echec, E(repliee, "E0005").Etat);
    }

    [Fact]
    public void Comptes_de_demonstration()
    {
        Assert.Equal(["alice", "bob", "claire"], DonneesDemo.Comptes.Select(c => c.Login).ToArray());
        Assert.True(DonneesDemo.Comptes.Single(c => c.Login == "alice").EstAdmin);
        Assert.True(DonneesDemo.EstCompteDemo("bob"));
        Assert.False(DonneesDemo.EstCompteDemo("admin"));
        Assert.False(DonneesDemo.EstCompteDemo("Bob")); // login normalisé attendu
    }

    [Fact]
    public void Communication_remplie_les_textes_fournis_sont_complets_sauf_pour_la_preparation_incomplete()
    {
        var equipe = DonneesDemo.Equipes(Alice, Bob, Claire, Maintenant)[0].Equipe;
        var annonce = ModelesTexteFournis.Trouver("annonce")!;
        foreach (var m in Scenarios().Select(s => s.Mep))
        {
            var valeurs = VariablesTexte.Valeurs(m, equipe, "https://mep.exemple.invalid/meps/1", new Dictionary<Guid, string>());
            var manquantes = Regex.Matches(annonce.Objet + annonce.Corps, @"\{\{(\w+)\}\}")
                .Select(x => x.Groups[1].Value).Where(c => valeurs[c].EstManquante).ToList();
            if (m.Outil == DonneesDemo.OutilIncomplete)
            {
                // Communication commencée : référence et créneau encore à renseigner, signalés dans les textes.
                Assert.True(valeurs["reference_demande"].EstManquante);
                Assert.Contains("date", manquantes);
                Assert.False(valeurs["benefices"].EstManquante);
            }
            else
            {
                // Une MEP simplifiée n'a pas de versions : la version cible reste à compléter.
                if (m.Type == TypeMep.Simplifiee) Assert.Equal(["version_cible"], manquantes.Distinct());
                else Assert.Empty(manquantes);
                Assert.StartsWith("CHG-DEMO-", m.Communication.ReferenceDemande, StringComparison.Ordinal);
                Assert.NotEqual(NiveauRisque.NonEvalue, m.Communication.NiveauRisque);
            }
        }
        var reussie = Mep(DonneesDemo.OutilReussie);
        Assert.True(reussie.Communication.ValideeParUtilisateurs);
        Assert.NotNull(reussie.Communication.ValideeLe);
    }

    [Fact]
    public void Equipes_de_demonstration_avec_leurs_modeles_de_texte()
    {
        var equipes = DonneesDemo.Equipes(Alice, Bob, Claire, Maintenant);
        Assert.Equal([DonneesDemo.NomEquipe, DonneesDemo.NomEquipeWeb], equipes.Select(e => e.Equipe.Nom).ToArray());
        Assert.All(equipes, e =>
        {
            Assert.NotEmpty(e.Equipe.ContactSupport);
            Assert.NotEmpty(e.Equipe.Signature);
            Assert.All(e.Modeles, m => Assert.Equal(e.Equipe.Id, m.EquipeId));
            Assert.Equal(ModelesTexteFournis.Tous.Select(f => f.Cle).Order(), e.Modeles.Select(m => m.CleFournie).OfType<string>().Order());
        });
        var exploitation = equipes[0].Modeles;
        Assert.True(ModelesTexteFournis.EstModifie(exploitation.Single(m => m.CleFournie == "annonce")));
        var astreinte = exploitation.Single(m => m.Nom == DonneesDemo.NomModeleAstreinte);
        Assert.Null(astreinte.CleFournie);
        var cles = VariablesTexte.Toutes.Select(v => v.Cle).ToHashSet();
        Assert.All(Regex.Matches(astreinte.Objet + astreinte.Corps, @"\{\{(\w+)\}\}"), x => Assert.Contains(x.Groups[1].Value, cles));
        Assert.All(equipes[1].Modeles, m => Assert.False(ModelesTexteFournis.EstModifie(m)));
    }
}
