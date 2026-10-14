using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Environnements des systèmes et des étapes, fiche du catalogue et objets impactés (#54).</summary>
public class ReglesObjetsImpactesTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static DonneesDemo.Jeu Jeu() => DonneesDemo.Creer(Alice, Bob, Claire, Maintenant);
    private static SystemeCatalogue Fiche(DonneesDemo.Jeu j, string nom) => j.Systemes.Single(s => s.Nom == nom);
    private static Systeme Sys(Mep m, string nom) => m.Systemes.Single(s => s.Nom == nom);
    private static Etape E(Mep m, string code) => m.Etapes.Single(e => e.Code == code);

    [Fact]
    public void Les_donnees_de_demonstration_ont_des_fiches_completes_et_touchent_la_production()
    {
        var j = Jeu();
        Assert.All(j.Systemes, s =>
        {
            Assert.NotEmpty(s.Environnements);
            Assert.Single(s.Environnements, e => e.Production);
            Assert.NotEmpty(s.Categories);
            Assert.All(s.Objets, o => Assert.Contains(o.Categorie, s.Categories));
        });
        Assert.True(Fiche(j, "BATCH").Objets.Count(o => o.Categorie == "Ordonnancement") >= 15);
        Assert.True(Regles.ToucheProduction(j.Mep));
        Assert.NotEmpty(j.Mep.ObjetsImpactes);
        Assert.All(j.Mep.ObjetsImpactes, o => Assert.NotNull(o.ObjetCatalogueId));
        Assert.All(j.Mep.Etapes.Where(e => e.SystemeIds.Count > 0), e => Assert.NotEmpty(e.Environnements));
        Assert.Empty(Regles.ErreursEnvironnements(j.Mep));
        Assert.Equal(29, ParametresGlobaux.CategoriesObjetsParDefaut.Count);
    }

    [Fact]
    public void Environnements_d_un_systeme_du_catalogue_pris_dans_sa_fiche()
    {
        var j = Jeu();
        var bdd = Sys(j.Mep, "BDD");
        Regles.DefinirEnvironnementsSysteme(j.Mep, bdd, ["préproduction", " Production ", "PRODUCTION", ""], Fiche(j, "BDD").Environnements);
        Assert.Equal([("Préproduction", false), ("Production", true)], bdd.Environnements.Select(e => (e.Nom, e.Production)).ToArray());
        var ex = Assert.Throws<RegleMetierException>(() => Regles.DefinirEnvironnementsSysteme(j.Mep, bdd, ["Formation"], Fiche(j, "BDD").Environnements));
        Assert.Contains("n'existe pas dans la fiche BDD", ex.Message);

        // Un environnement retiré de la fiche depuis reste admis s'il était déjà choisi.
        var reduite = Fiche(j, "BDD").Environnements.Where(e => e.Nom != "Préproduction").ToList();
        Regles.DefinirEnvironnementsSysteme(j.Mep, bdd, ["Préproduction"], reduite);
        Assert.Equal("Préproduction", Assert.Single(bdd.Environnements).Nom);
    }

    [Fact]
    public void Environnements_d_un_systeme_libre_au_nom_libre_production_deduite()
    {
        var m = Jeu().Mep;
        var libre = new Systeme { MepId = m.Id, Nom = "ERP" };
        m.Systemes.Add(libre);
        Regles.DefinirEnvironnementsSysteme(m, libre, ["Intégration", "Prod-2"], null);
        Assert.Equal([("Intégration", false), ("Prod-2", true)], libre.Environnements.Select(e => (e.Nom, e.Production)).ToArray());
        m.Systemes.ForEach(s => s.Environnements = s == libre ? [new EnvironnementSysteme { Nom = "Intégration" }] : []);
        Assert.False(Regles.ToucheProduction(m));
    }

    [Fact]
    public void Environnements_d_une_etape_controles_et_tries()
    {
        var j = Jeu();
        var m = j.Mep;
        var bdd = Sys(m, "BDD");
        Regles.DefinirEnvironnementsSysteme(m, bdd, ["Préproduction", "Production"], Fiche(j, "BDD").Environnements);
        var etape = m.Etapes.First(e => e.SystemeIds.Contains(bdd.Id));
        Regles.DefinirEnvironnementsEtape(m, etape, [(bdd.Id, "production"), (bdd.Id, "Préproduction"), (bdd.Id, "Production")]);
        Assert.Equal(["Préproduction", "Production"], etape.Environnements.Select(e => e.Nom).ToArray());

        Assert.Contains("n'est pas choisi pour BDD",
            Assert.Throws<RegleMetierException>(() => Regles.DefinirEnvironnementsEtape(m, etape, [(bdd.Id, "Recette")])).Message);
        var autre = m.Systemes.First(s => !etape.SystemeIds.Contains(s.Id));
        Assert.Contains("n'est pas un système de l'étape",
            Assert.Throws<RegleMetierException>(() => Regles.DefinirEnvironnementsEtape(m, etape, [(autre.Id, "Production")])).Message);

        // Le système quitte l'étape : ses environnements aussi.
        etape.SystemeIds = etape.SystemeIds.Where(s => s != bdd.Id).ToList();
        Regles.NettoyerEnvironnementsEtape(etape);
        Assert.DoesNotContain(etape.Environnements, e => e.SystemeId == bdd.Id);
    }

    [Fact]
    public void Environnement_retire_du_systeme_mais_garde_par_une_etape_bloque_le_plan()
    {
        var j = Jeu();
        var m = j.Mep;
        var bdd = Sys(m, "BDD");
        Regles.DefinirEnvironnementsSysteme(m, bdd, ["Préproduction", "Production"], Fiche(j, "BDD").Environnements);
        var etape = m.Etapes.First(e => e.SystemeIds.Contains(bdd.Id) && e.Plan == TypePlan.Deploiement);
        Regles.DefinirEnvironnementsEtape(m, etape, [(bdd.Id, "Préproduction")]);
        Assert.Empty(Regles.ErreursEnvironnements(m));

        Regles.DefinirEnvironnementsSysteme(m, bdd, ["Production"], Fiche(j, "BDD").Environnements);
        var attendu = $"{etape.Code} : l'environnement Préproduction n'est plus choisi pour BDD (Préparation › Systèmes) ; le choisir de nouveau ou le retirer de l'étape.";
        Assert.Equal([attendu], Regles.ErreursEnvironnements(m));
        Assert.Contains(attendu, Regles.ErreursContenu(m, Maintenant));
        // L'étape peut garder cet environnement (affiché barré) ; il reste admis à l'enregistrement.
        Regles.DefinirEnvironnementsEtape(m, etape, [(bdd.Id, "Préproduction"), (bdd.Id, "Production")]);
        Assert.Equal(2, etape.Environnements.Count);
    }

    [Fact]
    public void Systeme_sans_environnement_donne_un_avertissement()
    {
        var m = Jeu().Mep;
        Sys(m, "WEB").Environnements = [];
        Assert.Contains("Aucun environnement choisi pour le système WEB (Préparation › Systèmes).", Regles.Avertissements(m));
    }

    [Fact]
    public void Choix_des_objets_d_un_systeme_dans_sa_fiche()
    {
        var j = Jeu();
        var m = j.Mep;
        var batch = new Systeme { MepId = m.Id, Nom = "BATCH", CatalogueId = Fiche(j, "BATCH").Id };
        m.Systemes.Add(batch);
        var fiche = Fiche(j, "BATCH");
        Guid Id(string nom) => fiche.Objets.Single(o => o.Nom == nom).Id;

        var bilan = Regles.DefinirObjetsSysteme(m, batch, fiche, [(Id("FACT01"), " v1 "), (Id("FACT03"), "v1"), (Id("FACT01"), "doublon")]);
        Assert.Equal(new Regles.BilanObjets(2, 0, 0, 0), bilan);
        var fact01 = m.ObjetsImpactes.Single(o => o.Nom == "FACT01");
        Assert.Equal(("Ordonnancement", "Chaîne", "Facturation mensuelle", "v1", false), (fact01.Categorie, fact01.Type, fact01.Libelle, fact01.Version, fact01.Repris));

        // Un objet repris (hors catalogue) n'est pas touché par le choix.
        m.ObjetsImpactes.Add(new ObjetImpacte { MepId = m.Id, SystemeId = batch.Id, Categorie = "Packages", Nom = "ancien", Repris = true });
        fiche.Objets.Single(o => o.Nom == "FACT01").Libelle = "Facturation du mois";
        bilan = Regles.DefinirObjetsSysteme(m, batch, fiche, [(Id("FACT01"), "v2"), (Id("EDTPDF"), "")]);
        Assert.Equal(new Regles.BilanObjets(1, 1, 1, 0), bilan);
        Assert.Equal(["EDTPDF", "FACT01", "ancien"], m.ObjetsImpactes.Where(o => o.SystemeId == batch.Id).Select(o => o.Nom).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Facturation du mois", m.ObjetsImpactes.Single(o => o.Nom == "FACT01").Libelle);

        Assert.Throws<RegleMetierException>(() => Regles.DefinirObjetsSysteme(m, batch, fiche, [(Guid.NewGuid(), "")]));
        var libre = new Systeme { MepId = m.Id, Nom = "Libre" };
        m.Systemes.Add(libre);
        Assert.Contains("n'est pas issu du catalogue", Assert.Throws<RegleMetierException>(() => Regles.DefinirObjetsSysteme(m, libre, fiche, [])).Message);
    }

    [Fact]
    public void L_import_ajoute_et_met_a_jour_sans_retirer()
    {
        var j = Jeu();
        var m = j.Mep;
        var bdd = Sys(m, "BDD");
        var fiche = Fiche(j, "BDD");
        var avant = m.ObjetsImpactes.Count;
        var clients = fiche.Objets.Single(o => o.Nom == "clients");
        var vues = fiche.Objets.Single(o => o.Nom == "v_clients_actifs");
        var bilan = Regles.ImporterObjets(m, [new(bdd, clients, "2.1"), new(bdd, vues, "nouvelle"), new(bdd, vues, "dernière")]);
        Assert.Equal(new Regles.BilanObjets(1, 1, 0, 0), bilan);
        Assert.Equal(avant + 1, m.ObjetsImpactes.Count);
        Assert.Equal("dernière", m.ObjetsImpactes.Single(o => o.Nom == "v_clients_actifs").Version);
        Assert.Equal(new Regles.BilanObjets(0, 0, 0, 1), Regles.ImporterObjets(m, [new(bdd, clients, "2.1")]));
    }

    [Fact]
    public void Fiche_du_catalogue_environnements_categories_et_objets()
    {
        var s = new SystemeCatalogue { Nom = "ERP" };
        Regles.DefinirEnvironnementsCatalogue(s, [("Recette", "erp-rec", null), ("Production", "erp-prd", null), ("Secours", "", true)]);
        Assert.Equal([false, true, true], s.Environnements.Select(e => e.Production).ToArray());
        Assert.Throws<RegleMetierException>(() => Regles.DefinirEnvironnementsCatalogue(s, [("A", "", null), ("a", "", null)]));

        Assert.Equal(0, Regles.DefinirCategoriesCatalogue(s, [("Tables", null), ("Vues", null)]));
        var table = Regles.EnregistrerObjetCatalogue(s, null, "tables", "Table", " clients ", "Clients", "");
        Assert.Equal(("Tables", "clients"), (table.Categorie, table.Nom));
        Assert.Contains("existe déjà", Assert.Throws<RegleMetierException>(() => Regles.EnregistrerObjetCatalogue(s, null, "Tables", "", "CLIENTS", "", "")).Message);
        Assert.Contains("n'existe pas dans la fiche", Assert.Throws<RegleMetierException>(() => Regles.EnregistrerObjetCatalogue(s, null, "Index", "", "x", "", "")).Message);
        var vue = Regles.EnregistrerObjetCatalogue(s, null, "Vues", "Vue", "v1", "", "");
        Regles.EnregistrerObjetCatalogue(s, vue.Id, "Vues", "Vue", "v_clients", "Vue des clients", "");
        Assert.Equal("v_clients", s.Objets.Single(o => o.Id == vue.Id).Nom);

        // Renommer « Tables » garde ses objets ; retirer « Vues » retire les siens.
        Assert.Equal(1, Regles.DefinirCategoriesCatalogue(s, [("Tables SQL", "Tables")]));
        Assert.Equal(["Tables SQL"], s.Categories);
        Assert.Equal(("Tables SQL", table.Id), (Assert.Single(s.Objets).Categorie, s.Objets[0].Id));

        Assert.Equal(["A", "b"], Regles.NormaliserCategoriesDefaut([" A ", "", "a", "b", null]));
    }
}
