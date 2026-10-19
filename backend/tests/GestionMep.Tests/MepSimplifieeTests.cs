using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>MEP simplifiée (issue #56) : suppléants implicites, plan sans criticité, résolutions du jour J, changements de type.</summary>
public class MepSimplifieeTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);

    /// <summary>MEP de démonstration passée en simplifiée (Alice pilote, Bob et Claire suppléants implicites).</summary>
    private static DonneesDemo.Jeu Simplifiee()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        Regles.PasserEnSimplifiee(j.Mep);
        return j;
    }

    private static void Go(Mep m)
    {
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Regles.PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
    }

    [Fact]
    public void Une_etape_modele_inseree_perd_duree_criticite_et_resolutions_liees()
    {
        var m = Simplifiee().Mep;
        var modele = new ModeleEtape
        {
            Action = "Purge du cache", Criticite = Criticite.Elevee, DureeEstimeeMinutes = 10, ResolutionIds = [Guid.NewGuid()],
            SystemeCatalogueIds = [m.Systemes[0].CatalogueId!.Value],
        };
        var (etape, _) = Regles.InsererEtapeModele(m, modele, TypePlan.Deploiement, new(), null, modele.ResolutionIds);
        Assert.Null(etape.Criticite);
        Assert.Null(etape.DureeEstimeeMinutes);
        Assert.Empty(etape.ResolutionIds);
        Assert.Equal(modele.Id, etape.ModeleEtapeId);
    }

    [Fact]
    public void Un_administrateur_ajoute_d_office_n_est_qu_executant_meme_dans_une_mep_simplifiee()
    {
        var m = Simplifiee().Mep;
        var eve = new Participant { MepId = m.Id, Nom = "Eve", Role = "Exécutant", UtilisateurId = Dave, AjouteDOffice = true };
        m.Participants.Add(eve);
        Assert.DoesNotContain(eve.Id, Regles.Suppleants(m));
        Assert.False(Regles.EstPilote(m, Dave));
        // Bob et Claire, personnes de la MEP, restent suppléants implicites.
        Assert.True(Regles.EstPilote(m, Bob) && Regles.EstPilote(m, Claire));
        // Passée en complète, elle n'est pas reprise parmi les suppléants choisis.
        Regles.PasserEnComplete(m);
        Assert.DoesNotContain(eve.Id, m.SuppleantIds);
    }

    [Fact]
    public void Designe_pilote_l_administrateur_ajoute_d_office_devient_une_personne_a_part_entiere()
    {
        var m = Simplifiee().Mep;
        Go(m);
        var eve = new Participant { MepId = m.Id, Nom = "Eve", Role = "Prise en main", UtilisateurId = Dave, AjouteDOffice = true };
        m.Participants.Add(eve);
        Regles.ChangerPilote(m, Dave, estAdmin: true, eve, ancienPiloteSuppleant: true, null, "Alice injoignable", Maintenant, out var secours);
        Assert.True(secours);
        Assert.False(eve.AjouteDOffice);
        // Relevée par Alice, Eve reste suppléante implicite.
        Regles.ChangerPilote(m, Alice, estAdmin: false, m.Participants.Single(p => p.UtilisateurId == Alice), true, null, "De retour", Maintenant, out _);
        Assert.Contains(eve.Id, Regles.Suppleants(m));
    }

    [Fact]
    public void Complete_par_defaut()
    {
        Assert.Equal(TypeMep.Complete, new Mep().Type);
        Assert.Equal(TypeMep.Complete, DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep.Type);
    }

    [Fact]
    public void Passer_en_simplifiee_efface_durees_criticites_resolutions_liees_versions_et_suppleants_choisis()
    {
        var m = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        var attendu = Regles.EffacementsPourSimplifier(m);
        Assert.True(attendu.Durees > 0 && attendu.Criticites > 0 && attendu.ResolutionsLiees > 0 && attendu.Versions);
        Assert.Equal(1, attendu.Suppleants);

        var effaces = Regles.PasserEnSimplifiee(m);

        Assert.Equal(attendu, effaces);
        Assert.Equal(TypeMep.Simplifiee, m.Type);
        Assert.Equal("", m.VersionActuelle);
        Assert.Equal("", m.VersionCible);
        Assert.Empty(m.SuppleantIds);
        Assert.All(m.Etapes, e =>
        {
            Assert.Null(e.DureeEstimeeMinutes);
            Assert.Null(e.Criticite);
            Assert.Empty(e.ResolutionIds);
        });
        // Le reste est conservé : instructions, résultats attendus, tests unitaires, retour arrière.
        Assert.All(m.Etapes, e => Assert.NotEmpty(e.ResultatAttendu));
        Assert.NotEmpty(m.EtapesDu(TypePlan.RetourArriere));
        Assert.ThrowsAny<RegleMetierException>(() => Regles.PasserEnSimplifiee(m));
    }

    [Fact]
    public void Suppleants_implicites_toutes_les_personnes_sauf_le_pilote()
    {
        var m = Simplifiee().Mep;
        var bob = m.Participants.Single(p => p.UtilisateurId == Bob);
        var claire = m.Participants.Single(p => p.UtilisateurId == Claire);
        Assert.Equal([bob.Id, claire.Id], Regles.Suppleants(m));
        Assert.True(Regles.EstPilote(m, Claire));
        Assert.True(Regles.PeutEditerContenu(m, Claire, estAdmin: false));
        Assert.False(Regles.EstPilote(m, Dave));

        // Une personne ajoutée devient suppléante ; sans pilote, personne ne l'est.
        m.Participants.Add(new Participant { MepId = m.Id, Nom = "Dave", UtilisateurId = Dave });
        Assert.True(Regles.EstPilote(m, Dave));
        m.PiloteId = null;
        Assert.Empty(Regles.Suppleants(m));
        Assert.False(Regles.EstPilote(m, Bob));
    }

    [Fact]
    public void Verifier_le_plan_ne_reclame_ni_criticite_ni_resolutions()
    {
        var m = Simplifiee().Mep;
        Assert.Empty(Regles.ErreursContenu(m, Maintenant));
        Assert.Equal(StatutDisponibilite.Disponible, Regles.Disponibilite(m, E(m, "E0004"), Maintenant).Statut);

        // Responsable technique absent : pas d'avertissement sur les résolutions liées.
        m.ResponsablesTechniques[1].Disponibilite = DisponibiliteJourJ.Absent;
        Assert.DoesNotContain(Regles.Avertissements(m), a => a.Contains("résolution", StringComparison.OrdinalIgnoreCase));

        // Une MEP complète sans criticité est incomplète.
        var complete = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        E(complete, "E0004").Criticite = null;
        Assert.Contains("E0004 : compléter Criticité.", Regles.ErreursContenu(complete, Maintenant));
    }

    [Fact]
    public void Etape_d_une_simplifiee_sans_duree_ni_criticite_ni_resolutions()
    {
        var m = Simplifiee().Mep;
        var e = new Etape { DureeEstimeeMinutes = 10, Criticite = Criticite.Elevee, ResolutionIds = [Guid.NewGuid()] };
        Regles.AppliquerTypeAEtape(m, e);
        Assert.Null(e.DureeEstimeeMinutes);
        Assert.Null(e.Criticite);
        Assert.Empty(e.ResolutionIds);

        var complete = new Etape { DureeEstimeeMinutes = 10, Criticite = Criticite.Elevee };
        Regles.AppliquerTypeAEtape(new Mep(), complete);
        Assert.Equal(10, complete.DureeEstimeeMinutes);
    }

    [Fact]
    public void Jour_j_toutes_les_resolutions_des_systemes_de_l_etape_sont_proposees()
    {
        var j = Simplifiee();
        var m = j.Mep;
        var e5 = E(m, "E0005"); // API et LDAP, en échec
        var (api, bdd, web) = (j.Resolutions[0], j.Resolutions[1], j.Resolutions[2]);
        var tous = new ResolutionType { Titre = "Tous systèmes", SystemeCatalogueIds = [] };
        var inactive = new ResolutionType { Titre = "Ancienne", SystemeCatalogueIds = [.. api.SystemeCatalogueIds], Actif = false };

        Assert.True(Regles.EstResolutionProposee(m, e5, api));
        Assert.True(Regles.EstResolutionProposee(m, e5, tous));
        Assert.False(Regles.EstResolutionProposee(m, e5, bdd));
        Assert.False(Regles.EstResolutionProposee(m, e5, web));
        Assert.False(Regles.EstResolutionProposee(m, e5, inactive));

        Go(m);
        var exec = Regles.DemarrerResolution(m, e5, api, Claire, Maintenant);
        Assert.Equal(api.Titre, exec.Titre);
        Assert.ThrowsAny<RegleMetierException>(() => Regles.DemarrerResolution(m, e5, bdd, Bob, Maintenant));
    }

    [Fact]
    public void Passer_en_complete_conserve_tout_et_garde_les_suppleants()
    {
        var m = Simplifiee().Mep;
        var suppleants = Regles.Suppleants(m);
        Assert.Equal(2, Regles.PasserEnComplete(m));
        Assert.Equal(TypeMep.Complete, m.Type);
        Assert.Equal(suppleants, m.SuppleantIds);
        // Criticité réclamée pour toutes les étapes, comme pour toute MEP complète.
        Assert.Contains(Regles.ErreursContenu(m, Maintenant), e => e.EndsWith("compléter Criticité.", StringComparison.Ordinal));
        Assert.ThrowsAny<RegleMetierException>(() => Regles.PasserEnComplete(m));
    }

    [Fact]
    public void Type_modifiable_seulement_en_preparation_et_jamais_pour_un_modele()
    {
        var j = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
        Assert.ThrowsAny<RegleMetierException>(() => Regles.PasserEnSimplifiee(j.Modele));
        Go(j.Mep);
        Assert.ThrowsAny<RegleMetierException>(() => Regles.PasserEnSimplifiee(j.Mep));
    }

    [Fact]
    public void Duplication_garde_le_type_mais_un_modele_est_complet()
    {
        var m = Simplifiee().Mep;
        var copie = Regles.Dupliquer(m, commeModele: false, Dave, Maintenant);
        Assert.Equal(TypeMep.Simplifiee, copie.Type);
        Assert.Empty(copie.SuppleantIds);

        var modele = Regles.Dupliquer(m, commeModele: true, Dave, Maintenant);
        Assert.Equal(TypeMep.Complete, modele.Type);
        Assert.Equal(2, modele.SuppleantIds.Count);
    }
}
