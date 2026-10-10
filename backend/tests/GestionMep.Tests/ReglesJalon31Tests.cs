using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du jalon 3.1 : responsables techniques, droits d'édition, avertissements, motif de prise de main, retour en préparation.</summary>
public class ReglesJalon31Tests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Participant P(Mep m, string nom) => m.Participants.Single(p => p.Nom == nom);
    private static ResponsableTechnique Rt(Mep m, string nom) => m.ResponsablesTechniques.Single(r => r.ParticipantId == P(m, nom).Id);

    // ---------- Démo et droits ----------
    [Fact]
    public void La_demo_a_deux_responsables_techniques_distincts_du_pilote_et_aucun_avertissement()
    {
        var m = Demo();
        Assert.Equal(2, m.ResponsablesTechniques.Count);
        Assert.All(m.ResponsablesTechniques, r => Assert.Equal(DisponibiliteJourJ.Joignable, r.Disponibilite));
        Assert.Equal([m.Systemes.Single(s => s.Nom == "API").Id, m.Systemes.Single(s => s.Nom == "MDM").Id], Rt(m, "Bob").SystemeIds);
        Assert.Equal([m.Systemes.Single(s => s.Nom == "WEB").Id], Rt(m, "Claire").SystemeIds);
        Assert.DoesNotContain(m.ResponsablesTechniques, r => r.ParticipantId == m.PiloteId);
        Assert.Empty(Regles.Avertissements(m));
    }

    [Fact]
    public void Edition_du_contenu_reservee_aux_responsables_techniques_pilote_suppleants_et_admins()
    {
        var m = Demo();
        Assert.True(Regles.PeutEditerContenu(m, Alice, false)); // pilote
        Assert.True(Regles.PeutEditerContenu(m, Bob, false)); // suppléant et responsable technique
        Assert.True(Regles.PeutEditerContenu(m, Claire, false)); // responsable technique
        Assert.False(Regles.PeutEditerContenu(m, Dave, false));
        Assert.True(Regles.PeutEditerContenu(m, Dave, true)); // administrateur

        m.ResponsablesTechniques.RemoveAll(r => r.ParticipantId == P(m, "Claire").Id);
        Assert.False(Regles.PeutEditerContenu(m, Claire, false));
        Assert.False(Regles.EstResponsableTechnique(m, Claire));
        Assert.True(Regles.EstResponsableTechnique(m, Bob));
    }

    // ---------- Définition de la liste ----------
    [Fact]
    public void Un_participant_n_apparait_qu_une_fois_et_doit_exister_dans_la_mep()
    {
        var m = Demo();
        var bob = P(m, "Bob").Id;
        var doublon = Assert.Throws<RegleMetierException>(() => Regles.DefinirResponsablesTechniques(m,
            [new() { ParticipantId = bob }, new() { ParticipantId = bob }]));
        Assert.Contains("Bob", doublon.Message);
        Assert.Throws<RegleMetierException>(() => Regles.DefinirResponsablesTechniques(m, [new() { ParticipantId = Guid.NewGuid() }]));
        Assert.Throws<RegleMetierException>(() => Regles.DefinirResponsablesTechniques(m, [new() { ParticipantId = bob, SystemeIds = [Guid.NewGuid()] }]));
        Assert.Equal(2, m.ResponsablesTechniques.Count); // rien n'a changé

        Regles.DefinirResponsablesTechniques(m, [new() { ParticipantId = bob, Perimetre = "  API  ", ContactJourJ = " 06 00 " }]);
        Assert.Equal("API", m.ResponsablesTechniques.Single().Perimetre);
        Assert.Equal("06 00", m.ResponsablesTechniques.Single().ContactJourJ);
    }

    [Fact]
    public void La_liste_des_responsables_techniques_est_reservee_a_la_preparation()
    {
        var m = Demo();
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Regles.DefinirResponsablesTechniques(m, []));
    }

    // ---------- Avertissements ----------
    [Fact]
    public void Aucun_responsable_technique_donne_un_avertissement_et_pas_une_anomalie()
    {
        var m = Demo();
        m.ResponsablesTechniques.Clear();
        Assert.Equal(["Aucun responsable technique renseigné."], Regles.Avertissements(m));
        Assert.DoesNotContain(Regles.ErreursPlan(m, Maintenant, 0), e => e.Contains("responsable technique"));
    }

    [Fact]
    public void Pilote_aussi_responsable_technique_demande_un_suppleant_qui_connait_le_plan()
    {
        var m = Demo();
        m.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = P(m, "Alice").Id });
        Assert.Equal(["Alice est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan."], Regles.Avertissements(m));
    }

    [Fact]
    public void Responsable_technique_absent_demande_une_resolution_type_sur_ses_etapes_et_celles_de_ses_systemes()
    {
        var m = Demo();
        Rt(m, "Bob").Disponibilite = DisponibiliteJourJ.Absent;
        // Bob est responsable de E0002, E0005, E0007 et R0001 (seule E0005 a une résolution), et responsable technique des systèmes
        // API et MDM : E0008 (WEB + API, Claire) est aussi concernée.
        var avertissements = Regles.Avertissements(m);
        Assert.Contains("E0002 : Bob sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.", avertissements);
        Assert.Contains("R0001 : Bob sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.", avertissements);
        Assert.Contains("E0008 : Bob sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.", avertissements);
        Assert.DoesNotContain(avertissements, a => a.StartsWith("E0005")); // a une résolution type, des instructions et un résultat attendu
        Assert.DoesNotContain(avertissements, a => a.StartsWith("E0003")); // Claire, système WEB

        // Une résolution type règle E0002 ; les instructions manquantes la rendent à nouveau signalée.
        E(m, "E0002").ResolutionIds = [Guid.NewGuid()];
        Assert.DoesNotContain(Regles.Avertissements(m), a => a.StartsWith("E0002"));
        E(m, "E0002").Instructions = "";
        Assert.Contains(Regles.Avertissements(m), a => a.StartsWith("E0002"));
    }

    [Fact]
    public void Les_avertissements_ne_bloquent_pas_le_plan()
    {
        var m = Demo();
        Rt(m, "Bob").Disponibilite = DisponibiliteJourJ.Absent;
        Assert.NotEmpty(Regles.Avertissements(m));
        var erreurs = Regles.ErreursPlan(m, Maintenant, 1);
        Assert.DoesNotContain(erreurs, e => e.Contains("sera absent"));
    }

    // ---------- Retour en préparation et point de non-retour ----------
    [Fact]
    public void Revenir_en_preparation_est_refuse_une_fois_le_point_de_non_retour_franchi()
    {
        var m = Demo();
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        Regles.AutoriserReprise(m, E(m, "E0005"), Alice, Maintenant);
        foreach (var (code, qui) in new[] { ("E0004", Alice), ("E0005", Bob), ("E0006", Claire) })
        {
            Regles.Demarrer(m, E(m, code), qui, Maintenant);
            Regles.Terminer(m, E(m, code), EtatEtape.Reussie, "OK", true, qui, Maintenant);
        }
        Regles.AutoriserNonRetour(m, E(m, "E0007"), Alice, Maintenant);
        Regles.Demarrer(m, E(m, "E0007"), Bob, Maintenant);
        Regles.Terminer(m, E(m, "E0007"), EtatEtape.Reussie, "OK", true, Bob, Maintenant);

        var ex = Assert.Throws<RegleMetierException>(() => Regles.RevenirEnPreparation(m, Alice));
        Assert.Contains("E0007", ex.Message);
        Assert.Contains("point de non-retour", ex.Message);
        Assert.Equal(Phase.Execution, m.Phase);
    }

    // ---------- Prise de main ----------
    [Fact]
    public void Prendre_la_main_exige_un_motif()
    {
        var m = Demo();
        m.Phase = Phase.Execution;
        var alice = P(m, "Alice");
        E(m, "E0006").Etat = EtatEtape.EnCours; // la prise de main vise une étape en cours
        E(m, "E0006").ExecutantId = P(m, "Claire").Id;
        foreach (var motif in new[] { "", "   " })
        {
            var ex = Assert.Throws<RegleMetierException>(() => Regles.PrendreLaMain(m, E(m, "E0006"), alice, motif));
            Assert.Contains("pourquoi", ex.Message);
        }
        Assert.Equal(P(m, "Claire").Id, E(m, "E0006").ExecutantId);
        Regles.PrendreLaMain(m, E(m, "E0006"), alice, "Claire est en congé");
        Assert.Equal(alice.Id, E(m, "E0006").ExecutantId);
    }

    // ---------- Duplication ----------
    [Fact]
    public void La_duplication_copie_les_responsables_techniques_avec_les_nouveaux_identifiants()
    {
        var source = Demo();
        Rt(source, "Bob").Disponibilite = DisponibiliteJourJ.Absent;
        var copie = Regles.Dupliquer(source, null, false, Alice, Maintenant);
        Assert.Equal(2, copie.ResponsablesTechniques.Count);
        var bob = copie.ResponsablesTechniques.Single(r => r.ParticipantId == P(copie, "Bob").Id);
        Assert.Equal(DisponibiliteJourJ.Absent, bob.Disponibilite);
        Assert.Equal("Service commandes et flux MDM", bob.Perimetre);
        Assert.Equal([copie.Systemes.Single(s => s.Nom == "API").Id, copie.Systemes.Single(s => s.Nom == "MDM").Id], bob.SystemeIds);
        Assert.DoesNotContain(source.Participants, p => p.Id == bob.ParticipantId);
        Assert.NotSame(source.ResponsablesTechniques[0], copie.ResponsablesTechniques[0]);
    }

    [Fact]
    public void Nom_de_la_copie()
    {
        var source = Demo();
        Assert.Equal("Portail de démonstration (copie)", Regles.Dupliquer(source, null, false, Alice, Maintenant).Outil);
        Assert.Equal("Portail de démonstration (copie)", Regles.Dupliquer(source, null, true, Alice, Maintenant).Outil);
        var modele = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Modele;
        Assert.Equal("déploiement API standard", Regles.Dupliquer(modele, "Préproduction", false, Alice, Maintenant).Outil);
        Assert.Equal("Modèle : déploiement API standard (copie)", Regles.Dupliquer(modele, null, true, Alice, Maintenant).Outil);
    }

    [Fact]
    public void Environnement_vide_a_la_duplication_reprend_celui_de_la_source()
    {
        var source = Demo();
        Assert.Equal("Production", Regles.Dupliquer(source, "", false, Alice, Maintenant).Environnement);
        Assert.Equal("Production", Regles.Dupliquer(source, "  ", false, Alice, Maintenant).Environnement);
        Assert.Equal("Recette", Regles.Dupliquer(source, " Recette ", false, Alice, Maintenant).Environnement);
    }
}
