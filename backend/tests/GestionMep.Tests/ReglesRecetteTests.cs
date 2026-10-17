using GestionMep.Domain;
using static GestionMep.Domain.Regles;

namespace GestionMep.Tests;

/// <summary>Recette en blocs, bibliothèque de recettes et validation par les métiers (analyse d'un fichier réimporté).</summary>
public class ReglesRecetteTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid(), Dave = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly List<CritereGoNoGo> Criteres = DonneesDemo.CriteresGoNoGo();

    private static Mep Demo() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
    private static Systeme S(Mep m, string nom) => m.Systemes.Single(s => s.Nom == nom);

    private static Mep EnRecette()
    {
        var m = Demo();
        Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        PasserEnExecution(m, Alice, Criteres.Select(c => c.Id).ToList(), Criteres, 1, Maintenant);
        foreach (var e in m.EtapesDu(TypePlan.Deploiement)) e.Etat = EtatEtape.Reussie;
        PasserEnRecette(m, Alice, Maintenant);
        return m;
    }

    /// <summary>Une ligne de fichier par étape de la recette, sans validation.</summary>
    private static List<LigneValidation> Lignes(Mep m) =>
        m.FichesRecette.SelectMany(f => f.Etapes.Select(t => new LigneValidation(f.Id, t.Id, $"{f.Titre} {t.Ordre}", "", "", "", null))).ToList();

    private static List<LigneValidation> Avec(List<LigneValidation> lignes, int index, string validation, string constate, string par, DateOnly? date = null,
        bool dateLisible = true)
    {
        lignes[index] = lignes[index] with { Validation = validation, Constate = constate, ValidePar = par, Date = date, DateLisible = dateLisible };
        return lignes;
    }

    // ---------- Une fiche appartient à un seul système ----------
    [Fact]
    public void Une_fiche_reste_dans_son_systeme_et_une_ancienne_fiche_sans_systeme_se_rattache()
    {
        var m = Demo();
        var fiche = m.FichesRecette[0];
        var ex = Assert.Throws<RegleMetierException>(() => ModifierFicheRecette(m, fiche, S(m, "API").Id, "x", []));
        Assert.Equal("Une fiche de recette reste dans son système : créez une nouvelle fiche dans l'autre système.", ex.Message);
        ModifierFicheRecette(m, fiche, S(m, "WEB").Id, "Même système", []);

        // Reprise : une fiche d'avant (sans système) reste visible et modifiable, et peut être rattachée.
        fiche.SystemeId = null;
        Assert.Contains($"Recette « Même système » : sans système, à rattacher à un système de la MEP.", Avertissements(m));
        ModifierFicheRecette(m, fiche, null, "Toujours sans système", []);
        Assert.Null(fiche.SystemeId);
        ModifierFicheRecette(m, fiche, S(m, "BDD").Id, "Rattachée", []);
        Assert.Equal(S(m, "BDD").Id, fiche.SystemeId);
        Assert.Throws<RegleMetierException>(() => AjouterFicheRecette(m, Guid.NewGuid(), "Système inconnu"));
    }

    // ---------- Bibliothèque ----------
    [Fact]
    public void Un_modele_s_enregistre_depuis_une_fiche_et_s_insere_par_copie()
    {
        var m = Demo();
        var source = m.FichesRecette[0];
        var modele = new ModeleRecette { Titre = source.Titre, Etapes = EtapesPourModele(source) };
        Assert.Empty(ErreursModeleRecette(modele));
        Assert.Equal(source.Etapes.Select(t => t.Instructions), modele.Etapes.Select(t => t.Instructions));

        var copie = InsererModeleRecette(m, S(m, "BDD").Id, modele);
        Assert.Equal((6, S(m, "BDD").Id, source.Titre), (copie.Ordre, copie.SystemeId!.Value, copie.Titre));
        Assert.Equal(source.Etapes.Count, copie.Etapes.Count);
        Assert.DoesNotContain(copie.Etapes, t => source.Etapes.Any(s => s.Id == t.Id));
        // Copie : modifier le modèle ne change pas la fiche insérée.
        modele.Etapes[0].Instructions = "Changé";
        Assert.NotEqual("Changé", copie.Etapes[0].Instructions);

        var vide = new ModeleRecette { Etapes = [new EtapeModeleRecette { Ordre = 1, Instructions = "x" }] };
        Assert.Equal(["Donner un titre au modèle.", "Étape 1 : compléter le déroulé et le résultat attendu."], ErreursModeleRecette(vide));
        Assert.Contains("Ajouter au moins une étape de test.", ErreursModeleRecette(new ModeleRecette { Titre = "t" }));

        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => InsererModeleRecette(m, S(m, "BDD").Id, modele));
    }

    // ---------- Version de la recette ----------
    [Fact]
    public void La_version_de_la_recette_change_avec_le_contenu_mais_pas_avec_les_resultats()
    {
        var m = EnRecette();
        var v = VersionRecette(m);
        Assert.Equal(16, v.Length);
        var f = m.FichesRecette[0];
        ValiderEtapeTest(m, f, f.Etapes[0], ResultatControle.Conforme, "OK", Claire, false, Maintenant);
        Assert.Equal(v, VersionRecette(m));
        f.Etapes[0].ResultatAttendu += " (modifié)";
        Assert.NotEqual(v, VersionRecette(m));
    }

    // ---------- Validation par les métiers ----------
    [Fact]
    public void Un_fichier_coherent_renseigne_les_resultats_avec_le_nom_et_la_date_du_metier()
    {
        var m = EnRecette();
        var lignes = Avec(Avec(Lignes(m), 0, "Validé", "Page affichée", "Marie Métier", new DateOnly(2026, 10, 18)), 1, "refusé", "Erreur 500", "Paul");
        lignes[2] = lignes[2] with { Constate = "commencé", ValidePar = "Paul" }; // sans validation : ignorée
        var a = AnalyserValidation(m, m.Id, VersionRecette(m), lignes);
        Assert.Empty(a.Erreurs);
        Assert.Equal((2, 0, 1), (a.Changements.Count, a.Inchangees, a.SansValidation));

        AppliquerValidation(m, a, Claire, false, Maintenant);
        var (t1, t2) = (m.FichesRecette[0].Etapes[0], m.FichesRecette[0].Etapes[1]);
        Assert.Equal((ResultatControle.Conforme, "Page affichée", "Marie Métier", new DateOnly(2026, 10, 18), Claire),
            (t1.Resultat!.Value, t1.Observation, t1.ValideePar, t1.ValideeLe!.Value, t1.ParId!.Value));
        Assert.Equal((ResultatControle.Echec, "Erreur 500", (DateOnly?)null), (t2.Resultat!.Value, t2.Observation, t2.ValideeLe));

        // Le même fichier réimporté ne change plus rien ; une saisie dans l'application efface le nom du métier.
        var encore = AnalyserValidation(m, m.Id, VersionRecette(m), lignes);
        Assert.Equal((0, 2), (encore.Changements.Count, encore.Inchangees));
        RefaireRecette(m, m.FichesRecette[0], t1, Alice);
        ValiderEtapeTest(m, m.FichesRecette[0], t1, ResultatControle.Conforme, "OK", Bob, false, Maintenant);
        Assert.Equal(("", (DateOnly?)null), (t1.ValideePar, t1.ValideeLe));
    }

    [Fact]
    public void Une_validation_sans_resultat_constate_est_conforme_au_resultat_attendu()
    {
        var m = EnRecette();
        var a = AnalyserValidation(m, m.Id, VersionRecette(m), Avec(Lignes(m), 0, "VALIDE", "", "Marie"));
        Assert.Equal(ObservationValideeSansCommentaire, Assert.Single(a.Changements).Observation);
    }

    [Fact]
    public void Un_fichier_incoherent_est_refuse_en_entier_avec_toutes_les_raisons()
    {
        var m = EnRecette();
        var f = m.FichesRecette[0];
        ValiderEtapeTest(m, f, f.Etapes[0], ResultatControle.Conforme, "OK dans l'appli", Claire, false, Maintenant);
        var lignes = Lignes(m);
        Avec(lignes, 0, "Refusé", "KO", "Marie");                      // contredit un résultat déjà saisi
        Avec(lignes, 1, "Peut-être", "", "Marie");                     // valeur inconnue
        Avec(lignes, 2, "Validé", "OK", "");                           // sans nom
        Avec(lignes, 3, "Refusé", "", "Paul", null, dateLisible: false); // refus sans constat, date illisible
        Avec(lignes, 4, "Validé", "OK", "Paul");                       // correcte, mais rien n'est appliqué
        var a = AnalyserValidation(m, m.Id, VersionRecette(m), lignes);
        Assert.Equal(4, a.Erreurs.Count);
        Assert.Contains(a.Erreurs, e => e.Contains("déjà « Conforme » dans l'application", StringComparison.Ordinal));
        Assert.Contains(a.Erreurs, e => e.Contains("validation « Peut-être » inconnue (colonne « Validation » : Validé ou Refusé attendu)", StringComparison.Ordinal));
        Assert.Contains(a.Erreurs, e => e.Contains("indiquer qui a validé", StringComparison.Ordinal));
        Assert.Contains(a.Erreurs, e => e.Contains("date illisible", StringComparison.Ordinal) && e.Contains("décrire le résultat constaté du refus", StringComparison.Ordinal));

        var ex = Assert.Throws<RegleMetierException>(() => AppliquerValidation(m, a, Claire, false, Maintenant));
        Assert.StartsWith("Fichier refusé", ex.Message);
        Assert.Null(lignes.Skip(4).Select(l => m.FichesRecette.SelectMany(x => x.Etapes).Single(t => t.Id == l.EtapeId)).First().Resultat);
    }

    [Fact]
    public void Un_fichier_d_une_autre_mep_ou_d_une_recette_modifiee_est_refuse()
    {
        var m = EnRecette();
        var autre = AnalyserValidation(m, Guid.NewGuid(), VersionRecette(m), Lignes(m));
        Assert.Equal(["Ce fichier est l'export de la recette d'une autre MEP."], autre.Erreurs);
        var modifiee = AnalyserValidation(m, m.Id, "0000000000000000", Lignes(m));
        Assert.Equal(["La recette a été modifiée dans l'application depuis l'export de ce fichier : exportez-la de nouveau et reportez-y les validations."],
            modifiee.Erreurs);
        var lignes = Lignes(m);
        lignes.Add(lignes[0]);
        Assert.Contains(AnalyserValidation(m, m.Id, VersionRecette(m), lignes).Erreurs, e => e.EndsWith("étape présente deux fois dans le fichier.", StringComparison.Ordinal));
    }

    [Fact]
    public void La_validation_d_un_fichier_suit_les_droits_et_la_phase_de_la_recette()
    {
        var m = Demo();
        var a = AnalyserValidation(m, m.Id, VersionRecette(m), Avec(Lignes(m), 0, "Validé", "OK", "Marie"));
        Assert.Throws<RegleMetierException>(() => AppliquerValidation(m, a, Claire, false, Maintenant)); // préparation
        m = EnRecette();
        a = AnalyserValidation(m, m.Id, VersionRecette(m), Avec(Lignes(m), 0, "Validé", "OK", "Marie"));
        var ex = Assert.Throws<RegleMetierException>(() => AppliquerValidation(m, a, Dave, false, Maintenant));
        Assert.Contains("participants", ex.Message);
        AppliquerValidation(m, a, Dave, true, Maintenant); // administrateur
    }

    [Theory]
    [InlineData("Validé", ResultatControle.Conforme)]
    [InlineData(" valide ", ResultatControle.Conforme)]
    [InlineData("REFUSÉ", ResultatControle.Echec)]
    [InlineData("Refuse", ResultatControle.Echec)]
    [InlineData("Validée", ResultatControle.Conforme)]
    [InlineData(" refusée", ResultatControle.Echec)]
    [InlineData("OK", null)]
    [InlineData("Conforme", null)]
    public void Les_valeurs_de_validation_acceptees(string valeur, ResultatControle? attendu) => Assert.Equal(attendu, LireValidation(valeur));
}
