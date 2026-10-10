using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles du jalon 4 : graphe par lignes (liens par défaut, placer, règle des lignes, migration), sauvegarde par système, fiches de recette.</summary>
public class ReglesJalon4Tests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);
    private static Systeme S(Mep m, string nom) => m.Systemes.Single(s => s.Nom == nom);
    private static string[] Codes(Mep m, IEnumerable<Guid> ids) => ids.Select(id => m.Etapes.Single(e => e.Id == id).Code).ToArray();
    private static string[] Prealables(Mep m, string code) => Codes(m, E(m, code).Dependances);
    private static string[] Ligne(Mep m, int ligne, TypePlan plan = TypePlan.Deploiement) =>
        m.EtapesDu(plan).Where(e => e.Ligne == ligne).Select(e => e.Code).ToArray();
    private static Bilan BilanOk => new() { CeQuiAMarche = "Tout", Incidents = "Rien à signaler" };

    private static Mep EnRecette()
    {
        var m = Demo();
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        Regles.PasserEnRecette(m, Alice, Maintenant);
        return m;
    }

    // ---------- Démo ----------
    [Fact]
    public void La_demo_suit_la_maquette_ligne_par_ligne_sans_anomalie_ni_avertissement()
    {
        var m = Demo();
        Assert.Equal(["E0001", "E0002", "E0003"], Ligne(m, 0));
        Assert.Equal(["E0004", "E0005", "E0006"], Ligne(m, 1));
        Assert.Equal(["E0007"], Ligne(m, 2));
        Assert.Equal(["E0008"], Ligne(m, 3));
        Assert.Equal(["E0009"], Ligne(m, 4));
        Assert.Equal(["R0001", "R0002"], Ligne(m, 0, TypePlan.RetourArriere));
        Assert.Equal(["E0004", "E0005", "E0006"], Prealables(m, "E0007"));
        Assert.Equal(["API", "BDD", "MDM"], E(m, "E0007").SystemeIds.Select(id => m.Systemes.Single(s => s.Id == id).Nom).ToArray());
        Assert.True(E(m, "E0007").PointDeNonRetour);
        Assert.Equal(30, E(m, "E0005").DureeEstimeeMinutes);
        Assert.Contains("```", E(m, "E0005").Instructions);
        Assert.Equal(["BDD", "API", "WEB", "LDAP", "MDM"], m.Systemes.Select(s => s.Nom).ToArray());
        Assert.Equal(5, m.FichesRecette.Count);
        Assert.Equal(["Il faut 1 approbation(s)."], Regles.ErreursPlan(m, Maintenant, 1));
        Assert.Empty(Regles.Avertissements(m));
        Assert.Equal(["BDD", "API", "WEB", "LDAP", "MDM", "BATCH"], DonneesDemo.Catalogue().Systemes.Select(s => s.Nom).ToArray());
    }

    [Fact]
    public void Les_profils_de_demo_ont_role_contact_et_expertises()
    {
        var (catalogue, _) = DonneesDemo.Catalogue();
        Utilisateur alice = new() { Login = "alice" }, bob = new() { Login = "bob" }, claire = new() { Login = "claire" };
        DonneesDemo.CompleterProfils(alice, bob, claire, catalogue);
        string Nom(Guid id) => catalogue.Single(s => s.Id == id).Nom;
        Assert.Equal("Pilote / DBA", alice.Role);
        Assert.Equal(["BDD"], alice.Expertises.Select(x => Nom(x.SystemeCatalogueId)).ToArray());
        Assert.Equal("Développeur API", bob.Role);
        Assert.Equal(["API", "MDM"], bob.Expertises.Select(x => Nom(x.SystemeCatalogueId)).ToArray());
        Assert.Equal("Développeuse front", claire.Role);
        Assert.Equal(["WEB"], claire.Expertises.Select(x => Nom(x.SystemeCatalogueId)).ToArray());
        Assert.All([alice, bob, claire], u => Assert.NotEmpty(u.Contact));
    }

    // ---------- Liens par défaut et création ----------
    [Fact]
    public void Une_etape_creee_dans_une_ligne_attend_la_ligne_au_dessus_et_est_attendue_par_celle_d_en_dessous()
    {
        var m = Demo();
        var revisionE7 = E(m, "E0007").Revision;
        var nouvelle = new Etape { MepId = m.Id, Code = "E0010" };
        Regles.PlacerNouvelle(m, nouvelle, 1, 1);
        m.Etapes.Add(nouvelle);
        Regles.RelierParDefaut(m, nouvelle);

        Assert.Equal(["E0004", "E0010", "E0005", "E0006"], Ligne(m, 1));
        Assert.Equal(["E0001", "E0002", "E0003"], Prealables(m, "E0010"));
        Assert.Equal(["E0004", "E0005", "E0006", "E0010"], Prealables(m, "E0007"));
        Assert.Equal(revisionE7 + 1, E(m, "E0007").Revision);
        Assert.Equal([0, 1, 2, 3], m.EtapesDu(TypePlan.Deploiement).Where(e => e.Ligne == 1).Select(e => e.Ordre).ToArray());
    }

    [Fact]
    public void Sans_ligne_la_nouvelle_etape_ouvre_une_ligne_en_bas()
    {
        var m = Demo();
        var nouvelle = new Etape { MepId = m.Id, Code = "E0010" };
        Regles.PlacerNouvelle(m, nouvelle, null, null);
        m.Etapes.Add(nouvelle);
        Regles.RelierParDefaut(m, nouvelle);
        Assert.Equal(5, nouvelle.Ligne);
        Assert.Equal(0, nouvelle.Ordre);
        Assert.Equal(["E0009"], Prealables(m, "E0010"));
        Assert.Throws<RegleMetierException>(() => Regles.PlacerNouvelle(m, new Etape(), 8, null));
    }

    // ---------- Placer ----------
    [Fact]
    public void Placer_dans_la_meme_ligne_reordonne_sans_toucher_aux_liens()
    {
        var m = Demo();
        var avant = m.Etapes.ToDictionary(e => e.Code, e => (string.Join(",", e.Dependances), e.Revision));
        Regles.Placer(m, E(m, "E0006"), 1, 0, liensParDefaut: true);
        Assert.Equal(["E0006", "E0004", "E0005"], Ligne(m, 1));
        foreach (var e in m.Etapes) Assert.Equal(avant[e.Code], (string.Join(",", e.Dependances), e.Revision));
    }

    [Fact]
    public void Placer_sur_une_autre_ligne_recree_les_liens_par_defaut()
    {
        var m = Demo();
        Regles.Placer(m, E(m, "E0004"), 2, 0, liensParDefaut: true);
        Assert.Equal(["E0005", "E0006"], Ligne(m, 1));
        Assert.Equal(["E0004", "E0007"], Ligne(m, 2));
        Assert.Equal(["E0005", "E0006"], Prealables(m, "E0004"));
        Assert.Equal(["E0005", "E0006"], Prealables(m, "E0007"));
        Assert.Equal(["E0007", "E0004"], Prealables(m, "E0008"));
    }

    [Fact]
    public void Une_ligne_videe_disparait_et_les_suivants_orphelins_attendent_la_ligne_au_dessus()
    {
        var m = Demo();
        Regles.Placer(m, E(m, "E0007"), 0, 3, liensParDefaut: true);
        Assert.Equal(["E0001", "E0002", "E0003", "E0007"], Ligne(m, 0));
        Assert.Equal(["E0008"], Ligne(m, 2)); // l'ancienne ligne 2 est supprimée
        Assert.Equal(["E0009"], Ligne(m, 3));
        Assert.Empty(Prealables(m, "E0007"));
        Assert.Equal(["E0001", "E0002", "E0003", "E0007"], Prealables(m, "E0004"));
        // E0008 n'attendait que E0007 : elle attend maintenant toute la ligne au-dessus d'elle.
        Assert.Equal(["E0004", "E0005", "E0006"], Prealables(m, "E0008"));
        Assert.Null(Regles.ErreurGraphe(m, TypePlan.Deploiement));
    }

    [Fact]
    public void Placer_sans_liens_par_defaut_retire_seulement_les_liens_devenus_montants()
    {
        var m = Demo();
        Regles.Placer(m, E(m, "E0004"), 5, 0, liensParDefaut: false); // nouvelle ligne en bas
        Assert.Equal(5, E(m, "E0004").Ligne);
        Assert.Equal(["E0001", "E0002", "E0003"], Prealables(m, "E0004"));
        Assert.Equal(["E0005", "E0006"], Prealables(m, "E0007"));
        Assert.DoesNotContain(m.EtapesDu(TypePlan.Deploiement), e => e.Ligne < 5 && e.Dependances.Contains(E(m, "E0004").Id));
        Assert.Equal(1, E(m, "E0007").Revision); // préalables modifiés une fois : révision augmentée une fois
    }

    [Fact]
    public void Placer_refuse_une_ligne_inexistante_et_hors_preparation()
    {
        var m = Demo();
        Assert.Throws<RegleMetierException>(() => Regles.Placer(m, E(m, "E0004"), 6, 0, true));
        Assert.Throws<RegleMetierException>(() => Regles.Placer(m, E(m, "E0004"), -1, 0, true));
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Regles.Placer(m, E(m, "E0004"), 0, 0, true));
    }

    // ---------- Règle des lignes ----------
    [Fact]
    public void Un_lien_descend_toujours()
    {
        var m = Demo();
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, E(m, "E0004"), [E(m, "E0006").Id]));
        Assert.Equal("Un lien descend toujours : E0006 doit être sur une ligne au-dessus de E0004.", ex.Message);
        Assert.Equal(["E0001", "E0002", "E0003"], Prealables(m, "E0004"));
        // Un lien particulier peut sauter des lignes.
        Regles.DefinirDependances(m, E(m, "E0009"), [E(m, "E0001").Id, E(m, "E0008").Id]);
        Assert.Equal(["E0001", "E0008"], Prealables(m, "E0009"));
    }

    // ---------- Migration des lignes ----------
    [Fact]
    public void Les_lignes_des_etapes_existantes_suivent_la_profondeur_du_graphe_et_l_ancien_ordre()
    {
        Etape N(string code, int ordre, params Etape[] prealables) =>
            new() { Code = code, Ordre = ordre, Ligne = -1, Dependances = prealables.Select(p => p.Id).ToList() };
        var a = N("E0001", 1);
        var c = N("E0003", 3, a);
        var b = N("E0002", 2, a);
        var d = N("E0004", 4, b);
        var e = N("E0005", 5);
        var f = N("E0006", 6, a, d); // profondeur 1 + max(0, 2)
        Regles.CalculerLignes([d, f, c, e, b, a]);
        Assert.Equal((0, 0), (a.Ligne, a.Ordre));
        Assert.Equal((0, 1), (e.Ligne, e.Ordre));
        Assert.Equal((1, 0), (b.Ligne, b.Ordre));
        Assert.Equal((1, 1), (c.Ligne, c.Ordre));
        Assert.Equal((2, 0), (d.Ligne, d.Ordre));
        Assert.Equal((3, 0), (f.Ligne, f.Ordre));
    }

    // ---------- Suppression ----------
    [Fact]
    public void Supprimer_une_etape_du_milieu_garde_un_graphe_valide()
    {
        var m = Demo();
        Regles.RetirerEtape(m, E(m, "E0005"));
        Assert.Equal(["E0004", "E0006"], Prealables(m, "E0007"));
        Assert.Equal(["E0004", "E0006"], Ligne(m, 1));
        Assert.Null(Regles.ErreurGraphe(m, TypePlan.Deploiement));
    }

    // ---------- Sauvegarde par système ----------
    [Fact]
    public void Chaque_systeme_doit_dire_s_il_est_sauvegarde()
    {
        var m = Demo();
        var bdd = S(m, "BDD");
        Assert.Empty(Regles.ErreursSauvegarde(m));

        bdd.Sauvegarde.Statut = StatutSauvegarde.ARenseigner;
        Assert.Equal(["Sauvegarde : indiquer si BDD doit être sauvegardé."], Regles.ErreursSauvegarde(m));
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep, "", "  ");
        Assert.Equal(["Sauvegarde de BDD non requise : justifier."], Regles.ErreursSauvegarde(m));
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep, "", " Lecture seule ");
        Assert.Equal("Lecture seule", bdd.Sauvegarde.Justification);
        Assert.Equal("Sauvegarde BDD : non requise (Lecture seule)", Regles.DetailSauvegarde(bdd));
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
    }

    [Fact]
    public void Sauvegarde_pendant_la_mep_exige_une_etape_de_type_sauvegarde_sur_ce_systeme()
    {
        var m = Demo();
        var bdd = S(m, "BDD");
        Regles.DefinirType(E(m, "E0001"), TypeEtape.Standard, false);
        Assert.Contains("Sauvegarde de BDD pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système.", Regles.ErreursContenu(m, Maintenant));
        // Une étape de sauvegarde sur un autre système ne suffit pas ; sur plusieurs systèmes dont BDD, si.
        E(m, "E0002").SystemeIds = [S(m, "API").Id, bdd.Id];
        Assert.DoesNotContain(Regles.ErreursSauvegarde(m), x => x.Contains("BDD"));
        E(m, "E0002").SystemeIds = [S(m, "API").Id];
        Regles.DefinirSauvegardeSysteme(m, bdd, StatutSauvegarde.Requise, ModeSauvegarde.AvantMep, " pg_dump ", "");
        Assert.Empty(Regles.ErreursSauvegarde(m));
        Assert.Equal("pg_dump", bdd.Sauvegarde.Contenu);
        Assert.Equal("Sauvegarde BDD : requise (avant la MEP)", Regles.DetailSauvegarde(bdd));
        Assert.Equal("Sauvegarde API : requise (pendant la MEP)", Regles.DetailSauvegarde(S(m, "API")));
    }

    [Fact]
    public void Aucun_systeme_sauvegarde_donne_un_avertissement_et_la_sauvegarde_se_fixe_en_preparation()
    {
        var m = Demo();
        foreach (var s in m.Systemes) Regles.DefinirSauvegardeSysteme(m, s, StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep, "", "Rien à sauvegarder");
        Assert.Contains("Aucun système n'est sauvegardé.", Regles.Avertissements(m));
        Assert.Empty(Regles.ErreursSauvegarde(m));
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Regles.DefinirSauvegardeSysteme(m, S(m, "BDD"), StatutSauvegarde.Requise, ModeSauvegarde.AvantMep, "", ""));
    }

    // ---------- Fiches de recette : préparation ----------
    [Fact]
    public void Le_plan_exige_des_fiches_de_recette_completes()
    {
        var m = Demo();
        var web = m.FichesRecette[0];
        web.Etapes[1].ResultatAttendu = "";
        var vide = Regles.AjouterFicheRecette(m, null, "  ");
        var erreurs = Regles.ErreursContenu(m, Maintenant);
        Assert.Contains("Recette « Parcours de commande », étape 2 : compléter les instructions et le résultat attendu.", erreurs);
        Assert.Contains("Recette 6 : indiquer un titre.", erreurs);
        Assert.Contains("Recette 6 : ajouter au moins une étape de test.", erreurs);
        Assert.Equal("", vide.Titre);

        m.FichesRecette.Clear();
        Assert.Contains("Ajouter au moins une fiche de recette.", Regles.ErreursContenu(m, Maintenant));
        Assert.Equal(5, Regles.Avertissements(m).Count(a => a.StartsWith("Aucune recette pour le système ")));
        Assert.Contains("Aucune recette pour le système WEB.", Regles.Avertissements(m));
    }

    [Fact]
    public void Modifier_une_fiche_garde_l_identite_des_etapes_connues_et_l_ordre_recu()
    {
        var m = Demo();
        var fiche = m.FichesRecette[0];
        var (t1, t2) = (fiche.Etapes[0], fiche.Etapes[1]);
        Regles.ModifierFicheRecette(m, fiche, S(m, "API").Id, " Parcours ",
            [(t2.Id, " Commander ", "OK"), (null, "Nouvelle", "Attendu"), (Guid.NewGuid(), "Inconnue", "Attendu")]);
        Assert.Equal("Parcours", fiche.Titre);
        Assert.Equal(S(m, "API").Id, fiche.SystemeId);
        Assert.Equal(3, fiche.Etapes.Count);
        Assert.Same(t2, fiche.Etapes[0]);
        Assert.Equal((1, "Commander"), (t2.Ordre, t2.Instructions));
        Assert.DoesNotContain(fiche.Etapes, t => t.Id == t1.Id);
        Assert.Equal([1, 2, 3], fiche.Etapes.Select(t => t.Ordre).ToArray());

        Assert.Throws<RegleMetierException>(() => Regles.ModifierFicheRecette(m, fiche, Guid.NewGuid(), "x", []));
        Regles.SupprimerFicheRecette(m, m.FichesRecette[1]);
        Assert.Equal([1, 2, 3, 4], m.FichesRecette.Select(f => f.Ordre).ToArray());
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Regles.AjouterFicheRecette(m, null, "x"));
        Assert.Throws<RegleMetierException>(() => Regles.SupprimerFicheRecette(m, fiche));
    }

    // ---------- Fiches de recette : déroulé ----------
    [Fact]
    public void Une_etape_de_test_se_valide_en_recette_par_un_participant_relie_ou_un_admin()
    {
        var m = Demo();
        var fiche = m.FichesRecette[0];
        var t1 = fiche.Etapes[0];
        Assert.Throws<RegleMetierException>(() => Regles.ValiderEtapeTest(m, fiche, t1, ResultatControle.Conforme, "OK", Claire, false, Maintenant)); // préparation
        m = EnRecette();
        fiche = m.FichesRecette[0];
        (t1, var t2) = (fiche.Etapes[0], fiche.Etapes[1]);

        var ex = Assert.Throws<RegleMetierException>(() => Regles.ValiderEtapeTest(m, fiche, t1, ResultatControle.Conforme, "OK", Dave, false, Maintenant));
        Assert.Contains("participants", ex.Message);
        Assert.Throws<RegleMetierException>(() => Regles.ValiderEtapeTest(m, fiche, t1, ResultatControle.Conforme, " ", Claire, false, Maintenant));
        Regles.ValiderEtapeTest(m, fiche, t1, ResultatControle.Conforme, " Page affichée ", Claire, false, Maintenant);
        Assert.Equal(("Page affichée", Claire, Maintenant), (t1.Observation, t1.ParId!.Value, t1.Le!.Value));
        var deja = Assert.Throws<RegleMetierException>(() => Regles.ValiderEtapeTest(m, fiche, t1, ResultatControle.Echec, "Encore", Bob, false, Maintenant));
        Assert.Equal("Recette « Parcours de commande », étape 1 déjà validée : le pilote peut la faire refaire.", deja.Message);
        Regles.ValiderEtapeTest(m, fiche, t2, ResultatControle.Echec, "Erreur 500", Dave, true, Maintenant); // administrateur
    }

    [Fact]
    public void La_cloture_exige_toutes_les_etapes_de_test_conformes_et_le_pilote_fait_refaire()
    {
        var m = EnRecette();
        foreach (var f in m.FichesRecette)
            foreach (var t in f.Etapes) Regles.ValiderEtapeTest(m, f, t, ResultatControle.Conforme, "OK", Bob, false, Maintenant);
        var fiche = m.FichesRecette[0];
        var t2 = fiche.Etapes[1];

        Assert.Throws<RegleMetierException>(() => Regles.RefaireRecette(m, fiche, t2, Claire));
        Regles.RefaireRecette(m, fiche, t2, Alice);
        Assert.Null(t2.Resultat);
        Assert.Throws<RegleMetierException>(() => Regles.RefaireRecette(m, fiche, t2, Alice)); // pas encore validée
        Regles.ValiderEtapeTest(m, fiche, t2, ResultatControle.Echec, "Erreur 500", Claire, false, Maintenant);
        var refus = Assert.Throws<RegleMetierException>(() => Regles.Cloturer(m, Alice, Maintenant, BilanOk));
        Assert.Equal("Recette « Parcours de commande », étape 2 non conforme : clôture impossible.", refus.Message);

        // Toute la fiche à refaire (suppléant).
        Regles.RefaireRecette(m, fiche, null, Bob);
        Assert.All(fiche.Etapes, t => Assert.Null(t.Resultat));
        Assert.Throws<RegleMetierException>(() => Regles.RefaireRecette(m, fiche, null, Bob)); // rien de validé
        foreach (var t in fiche.Etapes) Regles.ValiderEtapeTest(m, fiche, t, ResultatControle.Conforme, "OK", Claire, false, Maintenant);
        Regles.Cloturer(m, Alice, Maintenant, BilanOk);
        Assert.Equal(IssueMep.Reussie, m.Issue);
    }

    // ---------- Duplication ----------
    [Fact]
    public void La_duplication_copie_lignes_systemes_durees_sauvegardes_et_fiches_sans_resultats()
    {
        var source = EnRecette();
        var f0 = source.FichesRecette[0];
        Regles.ValiderEtapeTest(source, f0, f0.Etapes[0], ResultatControle.Conforme, "OK", Alice, false, Maintenant);
        var copie = Regles.Dupliquer(source, null, false, Alice, Maintenant);
        Assert.Equal(source.EtapesTriees().Select(e => (e.Code, e.Ligne, e.Ordre, e.SystemeIds.Count, e.DureeEstimeeMinutes)),
            copie.EtapesTriees().Select(e => (e.Code, e.Ligne, e.Ordre, e.SystemeIds.Count, e.DureeEstimeeMinutes)));
        Assert.Equal(["API", "BDD", "MDM"], E(copie, "E0007").SystemeIds.Select(id => copie.Systemes.Single(s => s.Id == id).Nom).ToArray());
        Assert.All(copie.FichesRecette.SelectMany(f => f.Etapes), t => { Assert.Null(t.Resultat); Assert.Equal("", t.Observation); Assert.Null(t.ParId); });
        Assert.Equal(S(copie, "WEB").Id, copie.FichesRecette[0].SystemeId);
        Assert.DoesNotContain(copie.FichesRecette.SelectMany(f => f.Etapes), t => source.FichesRecette.SelectMany(f => f.Etapes).Any(s => s.Id == t.Id));
        Assert.Equal(StatutSauvegarde.NonRequise, S(copie, "LDAP").Sauvegarde.Statut);
    }
}
