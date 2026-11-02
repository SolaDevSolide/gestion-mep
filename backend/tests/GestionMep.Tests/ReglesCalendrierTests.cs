using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Calendrier et périodes de gel (#129) : créneau d'une MEP (enregistré en UTC, calculé dans son fuseau), répétitions d'une
/// période dans son fuseau, portée d'un gel, Go bloqué sans dérogation, dérogation accordée par un approbateur qui n'est pas
/// le demandeur, conflits sur un même système, variable {{gel}}.
/// </summary>
public class ReglesCalendrierTests
{
    // Portail de démonstration : Alice pilote, Bob suppléant, Claire approbatrice ; Dave n'est pas de la MEP.
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();
    private static readonly Guid Bdd = Guid.NewGuid(), Web = Guid.NewGuid();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;

    private static Systeme Sys(Guid? catalogue, string nom, params (string Nom, bool Prod)[] envs) => new()
    {
        CatalogueId = catalogue, Nom = nom, Environnements = [.. envs.Select(e => new EnvironnementSysteme { Nom = e.Nom, Production = e.Prod })],
    };

    private static PeriodeGel Gel(DateTimeOffset debut, DateTimeOffset fin, RecurrenceGel r = RecurrenceGel.Aucune, string nom = "Gel") =>
        new() { Nom = nom, Debut = debut, Fin = fin, Recurrence = r, FuseauHoraire = "Europe/Paris" };

    private static DateTimeOffset Paris(int a, int m, int j, int h = 0, int min = 0) =>
        Fuseaux.Instant(new DateOnly(a, m, j), new TimeOnly(h, min), "Europe/Paris");

    /// <summary>MEP de démonstration annoncée le jour donné, aux heures données (Paris).</summary>
    private static Mep Annoncee(DateOnly jour, TimeOnly? debut = null, TimeOnly? fin = null)
    {
        var m = Demo();
        (m.DateMep, m.HeureDebut, m.HeureFin) = (null, null, null);
        m.Communication.DateAnnoncee = jour;
        m.Communication.HeureDebutAnnoncee = debut;
        m.Communication.HeureFinAnnoncee = fin;
        return m;
    }

    // ---------- Créneau ----------

    [Fact]
    public void Le_creneau_prevu_vient_de_l_annonce_dans_le_fuseau_de_la_mep_et_s_enregistre_en_utc()
    {
        var m = Annoncee(new DateOnly(2026, 11, 20), new TimeOnly(20, 0), new TimeOnly(1, 30));
        var f = Regles.FenetreDe(m);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 19, 0, 0, TimeSpan.Zero), f.Debut);
        // Fin avant le début : le lendemain.
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 30, 0, TimeSpan.Zero), f.Fin);
        Assert.False(f.JourneeEntiere);
        Assert.Equal(TimeSpan.Zero, f.Debut!.Value.Offset);

        m.FuseauHoraire = "America/New_York";
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 1, 0, 0, TimeSpan.Zero), Regles.FenetreDe(m).Debut);
    }

    [Fact]
    public void Sans_heure_de_debut_la_mep_occupe_la_journee_locale_entiere_et_sans_date_elle_n_est_pas_au_calendrier()
    {
        var m = Annoncee(new DateOnly(2026, 3, 29));
        var f = Regles.FenetreDe(m);
        Assert.True(f.JourneeEntiere);
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero), f.Debut);
        // Passage à l'heure d'été ce jour-là : la journée locale dure 23 heures.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero), f.Fin);

        m.Communication.DateAnnoncee = null;
        Assert.Null(Regles.FenetreDe(m).Debut);
        var modele = Annoncee(new DateOnly(2026, 11, 20), new TimeOnly(20, 0));
        modele.EstModele = true;
        Assert.Null(Regles.FenetreDe(modele).Debut);
    }

    [Fact]
    public void Une_mep_reportee_prend_sa_date_visee_et_une_mep_lancee_part_du_go_avec_la_duree_prevue()
    {
        var m = Annoncee(new DateOnly(2026, 11, 20), new TimeOnly(20, 0), new TimeOnly(22, 0));
        Regles.Reporter(m, Alice, "Recette en retard", new DateOnly(2026, 11, 27), Maintenant);
        Assert.Equal(Paris(2026, 11, 27, 20), Regles.FenetreDe(m).Debut);

        var lancee = Annoncee(new DateOnly(2026, 11, 2), new TimeOnly(20, 0), new TimeOnly(22, 0));
        Regles.Decider(lancee, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(lancee, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        var f = Regles.FenetreDe(lancee);
        Assert.Equal(Maintenant, f.Debut);
        Assert.Equal(Maintenant.AddHours(2), f.Fin);
        // Encore en cours après la durée prévue : le créneau s'étend jusqu'à maintenant.
        Assert.Equal(Maintenant.AddHours(5), Regles.FinEffective(f.Debut!.Value, f.Fin, true, Maintenant.AddHours(5)));

        Regles.MettreAJourCalendrier(lancee);
        Assert.Equal(Regles.FormatCalendrier, lancee.Calendrier.Format);
        Assert.Equal(Maintenant, lancee.Calendrier.Debut);
    }

    [Fact]
    public void Deux_creneaux_se_chevauchent_fin_exclue_et_un_instant_touche_le_creneau_ou_il_tombe()
    {
        var t = Maintenant;
        Assert.True(Regles.Chevauchent(t, t.AddHours(2), t.AddHours(1), t.AddHours(3)));
        Assert.False(Regles.Chevauchent(t, t.AddHours(1), t.AddHours(1), t.AddHours(2)));
        Assert.True(Regles.Chevauchent(t.AddMinutes(30), t.AddMinutes(30), t, t.AddHours(1)));
        Assert.False(Regles.Chevauchent(t.AddHours(1), t.AddHours(1), t, t.AddHours(1)));
        Assert.True(Regles.Chevauchent(t, t, t, t));
    }

    // ---------- Répétitions ----------

    [Fact]
    public void Une_periode_hebdomadaire_garde_son_heure_locale_au_changement_d_heure()
    {
        // Chaque vendredi de 18h à 22h (Paris), depuis le 16 octobre 2026 ; l'heure d'hiver arrive le 25 octobre.
        var p = Gel(Paris(2026, 10, 16, 18), Paris(2026, 10, 16, 22), RecurrenceGel.Hebdomadaire);
        var o = Regles.Occurrences(p, Paris(2026, 10, 1), Paris(2026, 11, 7)).ToList();
        Assert.Equal(4, o.Count);
        Assert.All(o, x => Assert.Equal(18, Fuseaux.Local(x.Debut, "Europe/Paris").Hour));
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero), o[0].Debut);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero), o[2].Debut);
        Assert.All(o, x => Assert.Equal(TimeSpan.FromHours(4), x.Fin - x.Debut));
    }

    [Fact]
    public void Une_periode_mensuelle_reste_au_dernier_jour_des_mois_courts_et_fin_de_mois_garde_l_ecart_avec_la_fin_du_mois()
    {
        var mensuelle = Gel(Paris(2026, 1, 31), Paris(2026, 2, 1), RecurrenceGel.Mensuelle);
        var jours = Regles.Occurrences(mensuelle, Paris(2026, 1, 1), Paris(2026, 5, 1)).Select(o => Fuseaux.Jour(o.Debut, "Europe/Paris").Day).ToList();
        Assert.Equal([31, 28, 31, 30], jours);

        // Clôture comptable : les trois derniers jours du mois (29 → 31 janvier), jusqu'au 30 avril.
        var cloture = Gel(Paris(2026, 1, 29), Paris(2026, 2, 1), RecurrenceGel.FinDeMois, "Clôture comptable");
        cloture.FinRecurrence = new DateOnly(2026, 3, 31);
        var occurrences = Regles.Occurrences(cloture, Paris(2026, 1, 1), Paris(2026, 6, 1)).ToList();
        Assert.Equal([new DateOnly(2026, 1, 29), new DateOnly(2026, 2, 26), new DateOnly(2026, 3, 29)],
            occurrences.Select(o => Fuseaux.Jour(o.Debut, "Europe/Paris")));
        Assert.Equal([new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1)],
            occurrences.Select(o => Fuseaux.Jour(o.Fin, "Europe/Paris")));

        var annuelle = Gel(Paris(2026, 12, 20), Paris(2027, 1, 4), RecurrenceGel.Annuelle);
        Assert.Equal(2, Regles.Occurrences(annuelle, Paris(2027, 1, 1), Paris(2028, 1, 2)).Count());
    }

    [Fact]
    public void Une_periode_mal_saisie_est_refusee_avec_chaque_motif()
    {
        var p = Gel(Paris(2026, 1, 10), Paris(2026, 1, 1), RecurrenceGel.Hebdomadaire, nom: " ");
        p.FuseauHoraire = "Mars/Olympus";
        var erreurs = Regles.ErreursPeriodeGel(p);
        Assert.Contains("Le nom de la période de gel est obligatoire.", erreurs);
        Assert.Contains("La fin de la période de gel doit être après son début.", erreurs);
        Assert.Contains(erreurs, e => e.StartsWith("Fuseau horaire inconnu", StringComparison.Ordinal));
        var longue = Gel(Paris(2026, 1, 1), Paris(2026, 1, 10), RecurrenceGel.Hebdomadaire);
        Assert.Contains(Regles.ErreursPeriodeGel(longue), e => e.Contains("chevaucherait", StringComparison.Ordinal));
        Assert.Empty(Regles.ErreursPeriodeGel(Gel(Paris(2026, 1, 1), Paris(2026, 1, 3))));
    }

    // ---------- Portée ----------

    [Fact]
    public void Un_gel_concerne_tous_les_systemes_ou_les_siens_et_les_environnements_nommes_ou_la_production()
    {
        var bddProd = Sys(Bdd, "BDD", ("Production", true));
        var webRecette = Sys(Web, "WEB", ("Recette", false));
        var libre = Sys(null, "Outil maison", ("Préproduction", false));
        var p = Gel(Maintenant, Maintenant.AddDays(1));
        Assert.True(Regles.Concerne(p, []));
        p.SystemeCatalogueIds = [Bdd];
        Assert.True(Regles.Concerne(p, [bddProd]));
        Assert.False(Regles.Concerne(p, [webRecette, libre]));
        p.SystemeCatalogueIds = [];
        p.ProductionSeulement = true;
        Assert.True(Regles.Concerne(p, [webRecette, bddProd]));
        Assert.False(Regles.Concerne(p, [webRecette, libre]));
        Assert.False(Regles.Concerne(p, []));
        p.ProductionSeulement = false;
        p.Environnements = ["preproduction"];
        Assert.True(Regles.Concerne(p, [libre]));
        Assert.False(Regles.Concerne(p, [bddProd]));
    }

    // ---------- Go et dérogation ----------

    private static Mep Prete()
    {
        var m = Annoncee(new DateOnly(2026, 11, 2), new TimeOnly(10, 0), new TimeOnly(12, 0));
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        return m;
    }

    private static void Go(Mep m, IReadOnlyCollection<PeriodeGel> gels) =>
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, new ContexteVerification(1), Maintenant, gels);

    [Fact]
    public void Le_go_pendant_un_gel_est_refuse_sans_derogation_et_signale_en_preparation()
    {
        var m = Prete();
        var gel = Gel(Maintenant.AddHours(-1), Maintenant.AddHours(1), nom: "Fêtes");
        var e = Assert.Throws<GoBloqueParGelException>(() => Go(m, [gel]));
        Assert.Equal(Phase.Preparation, m.Phase);
        Assert.Contains("« Fêtes »", e.Message);
        Assert.Equal(gel.Id, Assert.Single(e.Gels).Periode.Id);
        Assert.Single(Regles.GelsDeLaMep(m, [gel], Maintenant));

        // Hors de sa portée (un autre système), le gel ne bloque rien.
        gel.SystemeCatalogueIds = [Web];
        Go(m, [gel]);
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void Le_go_couvre_la_duree_prevue_un_gel_qui_commence_pendant_la_mep_le_bloque_aussi()
    {
        var m = Prete();
        var gel = Gel(Maintenant.AddHours(1), Maintenant.AddHours(3));
        Assert.Single(Regles.GelsAuGo(m, [gel], Maintenant));
        Assert.Throws<GoBloqueParGelException>(() => Go(m, [gel]));
        Assert.Empty(Regles.GelsAuGo(m, [gel], Maintenant.AddHours(-3)));
    }

    [Fact]
    public void Une_derogation_demandee_par_le_pilote_avec_motif_et_accordee_par_l_approbateur_permet_le_go()
    {
        var m = Prete();
        var gel = Gel(Maintenant.AddHours(-1), Maintenant.AddHours(1));
        var o = Assert.Single(Regles.GelsConcernes(m, [gel], Maintenant));
        Assert.Throws<DroitInsuffisantException>(() => Regles.DemanderDerogation(m, Dave, o, "Correctif urgent", Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.DemanderDerogation(m, Alice, o, "  ", Maintenant));
        var d = Regles.DemanderDerogation(m, Alice, o, "Correctif de sécurité urgent", Maintenant);
        Assert.Throws<RegleMetierException>(() => Regles.DemanderDerogation(m, Bob, o, "Encore", Maintenant));
        Assert.Throws<GoBloqueParGelException>(() => Go(m, [gel]));

        // Ni le demandeur, ni le suppléant, ni quelqu'un qui n'est pas approbateur désigné.
        Assert.Throws<DroitInsuffisantException>(() => Regles.DeciderDerogation(m, Alice, d, true, "", Maintenant));
        Assert.Throws<DroitInsuffisantException>(() => Regles.DeciderDerogation(m, Bob, d, true, "", Maintenant));
        Assert.Throws<DroitInsuffisantException>(() => Regles.DeciderDerogation(m, Dave, d, true, "", Maintenant));
        Assert.Throws<RegleMetierException>(() => Regles.DeciderDerogation(m, Claire, d, false, "", Maintenant));
        Regles.DeciderDerogation(m, Claire, d, true, "Risque accepté", Maintenant);
        Assert.Equal(StatutDerogation.Accordee, d.Statut);
        Assert.Equal(Claire, d.DecideeParId);
        Assert.Throws<RegleMetierException>(() => Regles.DeciderDerogation(m, Claire, d, true, "", Maintenant));

        Go(m, [gel]);
        Assert.Equal(Phase.Execution, m.Phase);
    }

    [Fact]
    public void Une_derogation_ne_s_accorde_jamais_par_un_compte_relie_du_demandeur()
    {
        var m = Prete();
        var aliceBis = Guid.NewGuid();
        m.Approbations.Add(new Approbation { UtilisateurId = aliceBis, Nom = "Alice (second compte)" });
        var annuaire = new AnnuairePersonnes(
        [
            new(Alice, null, "Alice", "alice", true), new(aliceBis, Alice, "Alice", "alice.admin", true), new(Claire, null, "Claire", "claire", true),
            new(Bob, null, "Bob", "bob", true),
        ], []);
        var o = Regles.GelsConcernes(m, [Gel(Maintenant.AddHours(-1), Maintenant.AddHours(1))], Maintenant)[0];
        var d = Regles.DemanderDerogation(m, Alice, o, "Urgent", Maintenant);
        var refus = Assert.Throws<DroitInsuffisantException>(() => Regles.DeciderDerogation(m, aliceBis, d, true, "", Maintenant, annuaire));
        Assert.Contains("pilote, par un compte relié", refus.Message);
    }

    [Fact]
    public void Une_derogation_d_une_periode_repetee_ne_vaut_que_pour_son_occurrence()
    {
        var m = Prete();
        var gel = Gel(Maintenant.AddDays(-7).AddHours(-1), Maintenant.AddDays(-7).AddHours(1), RecurrenceGel.Hebdomadaire);
        var o = Assert.Single(Regles.GelsAuGo(m, [gel], Maintenant));
        m.DerogationsGel.Add(new DerogationGel { PeriodeGelId = gel.Id, Occurrence = o.Debut.AddDays(-7), Statut = StatutDerogation.Accordee });
        Assert.Throws<GoBloqueParGelException>(() => Go(m, [gel]));
        var d = Regles.DemanderDerogation(m, Alice, o, "Urgent", Maintenant);
        Regles.DeciderDerogation(m, Claire, d, true, "", Maintenant);
        Go(m, [gel]);
    }

    [Fact]
    public void Une_derogation_retiree_bloque_de_nouveau_le_go_et_se_redemande()
    {
        var m = Prete();
        var gel = Gel(Maintenant.AddHours(-1), Maintenant.AddHours(1));
        var o = Regles.GelsAuGo(m, [gel], Maintenant)[0];
        var d = Regles.DemanderDerogation(m, Alice, o, "Urgent", Maintenant);
        Regles.DeciderDerogation(m, Claire, d, true, "", Maintenant);
        Assert.Throws<DroitInsuffisantException>(() => Regles.RetirerDerogation(m, Claire, d));
        Regles.RetirerDerogation(m, Bob, d);
        Assert.Throws<GoBloqueParGelException>(() => Go(m, [gel]));
        Assert.Equal(StatutDerogation.Demandee, Regles.DemanderDerogation(m, Alice, o, "Toujours urgent", Maintenant).Statut);
    }

    // ---------- Conflits ----------

    [Fact]
    public void Deux_mep_sur_le_meme_systeme_au_meme_moment_sont_en_conflit_sans_rien_bloquer()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var bdd = Bdd.ToString();
        var elements = new List<ElementCalendrier>
        {
            new(a, Maintenant, Maintenant.AddHours(2), false, [bdd, "libre:outil maison"]),
            new(b, Maintenant.AddHours(1), Maintenant.AddHours(3), false, [bdd]),
            new(c, Maintenant.AddHours(1), Maintenant.AddHours(3), false, [Web.ToString()]),
            // Lancée sans fin connue : elle s'étend jusqu'à maintenant (4 h plus tard), et rejoint B.
            new(d, Maintenant.AddHours(-2), null, true, [bdd]),
        };
        var conflits = Regles.Conflits(elements, Maintenant.AddHours(2));
        Assert.Equal(new[] { b, d }.Order(), conflits[a].Select(x => x.AutreMepId).Order());
        Assert.Equal([bdd], conflits[a].Single(x => x.AutreMepId == b).Systemes);
        Assert.Empty(conflits[c]);
        Assert.Contains(conflits[d], x => x.AutreMepId == b);
        Assert.Equal("libre:outil maison", Regles.CleSysteme(new Systeme { Nom = " Outil Maison " }));
    }

    // ---------- Modèles de texte ----------

    [Fact]
    public void La_variable_gel_decrit_la_periode_et_la_derogation_et_vaut_aucune_hors_gel()
    {
        var m = Prete();
        var gel = Gel(Paris(2026, 11, 2), Paris(2026, 11, 3), nom: "Clôture comptable");
        string? Nom(Guid id) => id == Claire ? "Claire Durand" : null;
        Assert.Equal("aucune", Regles.TexteGel(m, [], Nom, Maintenant));
        Assert.Equal("« Clôture comptable » du 02/11/2026 0h00 au 03/11/2026 0h00 (Europe/Paris) : sans dérogation",
            Regles.TexteGel(m, [gel], Nom, Maintenant));
        var d = Regles.DemanderDerogation(m, Alice, Regles.GelsDeLaMep(m, [gel], Maintenant)[0], "Urgent", Maintenant);
        Assert.EndsWith(": dérogation demandée, en attente d'accord", Regles.TexteGel(m, [gel], Nom, Maintenant));
        Regles.DeciderDerogation(m, Claire, d, true, "", Maintenant);
        Assert.EndsWith(": dérogation accordée par Claire Durand", Regles.TexteGel(m, [gel], Nom, Maintenant));
        var valeurs = VariablesTexte.Valeurs(m, null, null, new Dictionary<Guid, string>(), Maintenant, gel: Regles.TexteGel(m, [gel], Nom, Maintenant));
        Assert.StartsWith("« Clôture comptable »", valeurs["gel"].Texte);

        m.Communication.DateAnnoncee = null;
        Assert.Null(Regles.TexteGel(m, [gel], Nom, Maintenant));
    }
}
