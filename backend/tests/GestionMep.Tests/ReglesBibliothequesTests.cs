using GestionMep.Domain;
using static GestionMep.Domain.Regles;

namespace GestionMep.Tests;

/// <summary>Bibliothèque d'étapes modèles (transformer, insérer une copie) et compteurs d'utilisations des bibliothèques.</summary>
public class ReglesBibliothequesTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static DonneesDemo.Jeu Jeu() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
    private static Systeme S(Mep m, string nom) => m.Systemes.Single(s => s.Nom == nom);
    private static Guid Cat(DonneesDemo.Jeu j, string nom) => j.Systemes.Single(s => s.Nom == nom).Id;

    private static ModeleEtape Modele(params Guid[] catalogue) => new()
    {
        Action = "Sauvegarde de la base", SystemeCatalogueIds = [.. catalogue], Type = TypeEtape.Sauvegarde, Criticite = Criticite.Elevee,
        DureeEstimeeMinutes = 15, Instructions = "```\npg_dump clients\n```", ResultatAttendu = "Fichier créé", Fin = FinEtape.Verification,
        SauvegardeEmplacement = "/sauvegardes", SauvegardeMethodeRecuperation = "pg_restore", Version = 1,
    };

    [Fact]
    public void Transformer_une_etape_reprend_la_fiche_sans_point_de_non_retour_ni_liens()
    {
        var jeu = Jeu();
        var mep = jeu.Mep;
        var etape = mep.Etapes.First(e => e.PointDeNonRetour);
        var maison = new Systeme { MepId = mep.Id, Nom = "Outil maison" };
        mep.Systemes.Add(maison);
        etape.SystemeIds = [.. etape.SystemeIds, maison.Id, etape.SystemeIds[0]];

        var (modele, horsCatalogue) = ModeleDepuisEtape(mep, etape, "  Bascule type  ");

        Assert.Equal("Bascule type", modele.Action);
        Assert.Equal(["Outil maison"], horsCatalogue);
        Assert.Equal(etape.SystemeIds.Select(id => mep.Systemes.Single(s => s.Id == id).CatalogueId).OfType<Guid>().Distinct(), modele.SystemeCatalogueIds);
        Assert.Equal((etape.Type, etape.Criticite, etape.DureeEstimeeMinutes, etape.Fin), (modele.Type, modele.Criticite, modele.DureeEstimeeMinutes, modele.Fin));
        Assert.Equal(etape.ResolutionIds, modele.ResolutionIds);
        Assert.Empty(ErreursModeleEtape(modele));
        // Sans nom donné : l'action de l'étape.
        Assert.Equal(etape.Action.Trim(), ModeleDepuisEtape(mep, etape, " ").Modele.Action);
    }

    [Fact]
    public void Une_etape_modele_standard_perd_ses_champs_de_sauvegarde_et_exige_nom_et_systeme()
    {
        var m = Modele();
        m.Action = " ";
        m.DureeEstimeeMinutes = -1;
        Assert.Equal(3, ErreursModeleEtape(m).Count);
        m.Type = TypeEtape.Standard;
        m.SystemeCatalogueIds = [Guid.Empty, Guid.Empty];
        NormaliserModeleEtape(m);
        Assert.Equal(("", ""), (m.SauvegardeEmplacement, m.SauvegardeMethodeRecuperation));
        Assert.Single(m.SystemeCatalogueIds);
    }

    [Fact]
    public void Inserer_une_copie_mappe_les_systemes_du_catalogue_et_signale_les_absents()
    {
        var jeu = Jeu();
        var mep = jeu.Mep;
        var absent = Guid.NewGuid();
        var resolutionActive = jeu.Resolutions[0].Id;
        var modele = Modele(Cat(jeu, "BDD"), absent);
        modele.ResolutionIds = [resolutionActive, jeu.Resolutions[1].Id];
        var nbLignes = Lignes(mep, TypePlan.Deploiement).Count;
        var codes = mep.Etapes.Select(e => e.Code).ToList();

        var (etape, absents) = InsererEtapeModele(mep, modele, TypePlan.Deploiement, new(), null, [resolutionActive]);

        Assert.Equal([absent], absents);
        Assert.Equal([S(mep, "BDD").Id], etape.SystemeIds);
        Assert.Equal(modele.Id, etape.ModeleEtapeId);
        Assert.DoesNotContain(etape.Code, codes);
        Assert.Equal(ProchainCode(mep.Etapes.Where(e => e != etape)), etape.Code);
        Assert.False(etape.PointDeNonRetour);
        Assert.Equal((TypeEtape.Sauvegarde, "/sauvegardes", FinEtape.Verification), (etape.Type, etape.SauvegardeEmplacement, etape.Fin));
        Assert.Equal([resolutionActive], etape.ResolutionIds);
        // Sans place : étape isolée, en haut du graphe.
        Assert.Empty(etape.Dependances);
        Assert.Equal(0, etape.Ligne);

        // Après une carte : elle l'attend, une ligne plus bas.
        var derniere = mep.EtapesDu(TypePlan.Deploiement).Last(e => e != etape);
        var (deuxieme, _) = InsererEtapeModele(mep, Modele(Cat(jeu, "API")), TypePlan.Deploiement, new(Apres: derniere.Id), null, []);
        Assert.Equal([derniere.Id], deuxieme.Dependances);
        Assert.Equal(nbLignes, Lignes(mep, TypePlan.Deploiement).Count - 1);
        Assert.Same(deuxieme, Lignes(mep, TypePlan.Deploiement)[^1].Single());
    }

    [Fact]
    public void Une_copie_prend_l_environnement_unique_de_chaque_systeme_et_aucun_s_il_y_a_le_choix()
    {
        var jeu = Jeu();
        var mep = jeu.Mep;
        S(mep, "BDD").Environnements = [new EnvironnementSysteme { Nom = "Préproduction" }, new EnvironnementSysteme { Nom = "Production", Production = true }];
        S(mep, "API").Environnements = [new EnvironnementSysteme { Nom = "Production", Production = true }];

        var (etape, _) = InsererEtapeModele(mep, Modele(Cat(jeu, "BDD"), Cat(jeu, "API")), TypePlan.Deploiement, new(), null, []);

        var env = Assert.Single(etape.Environnements);
        Assert.Equal((S(mep, "API").Id, "Production"), (env.SystemeId, env.Nom));
        Assert.Empty(ErreursEnvironnements(mep));
    }

    [Fact]
    public void Le_retour_arriere_refuse_une_etape_modele_de_sauvegarde()
    {
        var jeu = Jeu();
        Assert.ThrowsAny<RegleMetierException>(() => InsererEtapeModele(jeu.Mep, Modele(Cat(jeu, "BDD")), TypePlan.RetourArriere, new(), null, []));
        var normale = Modele(Cat(jeu, "BDD"));
        normale.Type = TypeEtape.Standard;
        var (etape, _) = InsererEtapeModele(jeu.Mep, normale, TypePlan.RetourArriere, new(), null, []);
        Assert.StartsWith("R", etape.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_mep_compte_chaque_element_de_bibliotheque_une_seule_fois_et_un_modele_de_mep_jamais()
    {
        var jeu = Jeu();
        var mep = jeu.Mep;
        mep.ElementsBibliothequeComptes = [];
        var premiers = NouveauxElementsBibliotheque(mep);
        Assert.Equal(ElementsBibliotheque(mep).Order(), premiers);
        Assert.Empty(NouveauxElementsBibliotheque(mep));

        var modele = Modele(Cat(jeu, "BDD"));
        var (etape, _) = InsererEtapeModele(mep, modele, TypePlan.Deploiement, new(), null, []);
        Assert.Equal([modele.Id], NouveauxElementsBibliotheque(mep));
        // Retirée puis remise : déjà comptée.
        mep.Etapes.Remove(etape);
        InsererEtapeModele(mep, modele, TypePlan.Deploiement, new(), null, []);
        Assert.Empty(NouveauxElementsBibliotheque(mep));

        Assert.True(jeu.Modele.EstModele);
        InsererEtapeModele(jeu.Modele, modele, TypePlan.Deploiement, new(), null, []);
        Assert.Empty(NouveauxElementsBibliotheque(jeu.Modele));
    }

    [Fact]
    public void La_duplication_garde_le_lien_vers_l_etape_modele_et_le_modele_de_recette()
    {
        var jeu = Jeu();
        var modele = Modele(Cat(jeu, "BDD"));
        var (etape, _) = InsererEtapeModele(jeu.Mep, modele, TypePlan.Deploiement, new(), null, []);
        var fiche = jeu.Mep.FichesRecette[0];
        fiche.ModeleRecetteId = Guid.NewGuid();

        var copie = Dupliquer(jeu.Mep, false, Alice, Maintenant);

        Assert.Equal(modele.Id, copie.Etapes.Single(e => e.Code == etape.Code).ModeleEtapeId);
        Assert.Equal(fiche.ModeleRecetteId, copie.FichesRecette.Single(f => f.Titre == fiche.Titre).ModeleRecetteId);
        // La copie n'a encore rien compté : sa première sauvegarde comptera ses éléments.
        Assert.Contains(modele.Id, NouveauxElementsBibliotheque(copie));
    }

    [Fact]
    public void Demonstration_des_bibliotheques_coherente_avec_les_mep()
    {
        var jeu = Jeu();
        var scenarios = DonneesDemo.Scenarios(Alice, Bob, Claire, jeu.Systemes, jeu.Resolutions, jeu.Criteres, Maintenant);
        DonneesDemo.DaterResolutions(jeu.Resolutions, Maintenant);
        var etapes = DonneesDemo.EtapesModeles(jeu.Systemes, jeu.Resolutions, Maintenant);
        var recettes = DonneesDemo.ModelesRecette(jeu.Systemes, Maintenant);
        List<Mep> meps = [jeu.Mep, jeu.Modele, .. scenarios.Select(s => s.Mep)];
        DonneesDemo.RelierAuxBibliotheques(meps, etapes, recettes);

        // Étapes modèles valides, réparties sur tous les systèmes, références connues.
        Assert.All(etapes, m => Assert.Empty(ErreursModeleEtape(m)));
        Assert.All(etapes, m => Assert.All(m.SystemeCatalogueIds, id => Assert.Contains(jeu.Systemes, s => s.Id == id)));
        Assert.All(etapes, m => Assert.All(m.ResolutionIds, id => Assert.Contains(jeu.Resolutions, r => r.Id == id)));
        Assert.All(jeu.Systemes, s => Assert.Contains(etapes, m => m.SystemeCatalogueIds.Contains(s.Id)));
        Assert.Contains(etapes, m => m.Type == TypeEtape.Sauvegarde);
        Assert.Contains(etapes, m => m.SystemeCatalogueIds.Count > 1);
        Assert.Equal(etapes.Count, etapes.Select(m => m.Action).Distinct().Count());

        // Les copies pointent vers des modèles existants et reprennent leur action.
        var copies = meps.SelectMany(m => m.Etapes).Where(e => e.ModeleEtapeId is not null).ToList();
        Assert.NotEmpty(copies);
        Assert.All(copies, e => Assert.Equal(etapes.Single(m => m.Id == e.ModeleEtapeId).Type, e.Type));

        // Compteurs : au moins les MEP de la démonstration qui les utilisent (le modèle de MEP ne compte pas).
        var reelles = meps.Where(m => !m.EstModele).ToList();
        int Usage(Guid id) => reelles.Count(m => ElementsBibliotheque(m).Contains(id));
        Assert.All(etapes, m => Assert.True(m.Utilisations >= Usage(m.Id), $"{m.Action} : {m.Utilisations} < {Usage(m.Id)}"));
        Assert.All(recettes, m => Assert.True(m.Utilisations >= Usage(m.Id), $"{m.Titre} : {m.Utilisations} < {Usage(m.Id)}"));
        Assert.All(jeu.Resolutions, r => Assert.True(r.Utilisations >= Usage(r.Id), $"{r.Titre} : {r.Utilisations} < {Usage(r.Id)}"));
        // Déjà comptés : modifier une MEP de démonstration ne les compte pas une seconde fois.
        Assert.All(reelles, m => Assert.Empty(NouveauxElementsBibliotheque(m)));
        Assert.Empty(jeu.Modele.ElementsBibliothequeComptes);
    }
}
