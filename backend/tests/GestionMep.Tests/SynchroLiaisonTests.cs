using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles de la liaison sans base ni réseau : catalogue → systèmes, MEP → événement de déploiement, anti-rebond.</summary>
public class SynchroLiaisonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationNoosphere App(string id, string nom, string domaine = "fin", string cycle = "live", string? description = null) =>
        new(id, nom, description ?? $"Description {nom}", domaine, "eq", "app", cycle, null, $"https://n.test/applications/{id}");

    private static CatalogueNoosphere Catalogue(params ApplicationNoosphere[] apps) =>
        new("2026-10-31T12:00:00Z", "N", "https://n.test", [new("fin", "Finance"), new("rh", "RH")], [new("eq", "Équipe", null)], [.. apps], []);

    [Fact]
    public void Suivre_cree_ou_rattache_par_nom_ou_au_systeme_choisi()
    {
        var l = new Liaison { Id = Guid.NewGuid() };
        var local = new SystemeCatalogue { Nom = "Facturation", Description = "locale", Environnements = [new() { Nom = "Production", Production = true }] };
        var autre = new SystemeCatalogue { Nom = "Ancien nom" };
        var systemes = new List<SystemeCatalogue> { local, autre };
        var c = Catalogue(App("billing", "Facturation"), App("pay", "Paie"), App("crm", "CRM"));
        var bilan = new BilanSynchro();

        // Même nom (sans tenir compte de la casse ni des accents) : rattachée ; nom et description viennent de Noosphere.
        var s = SynchroLiaison.Suivre(l, c, c.Applications![0], systemes, null, T0, bilan);
        Assert.Same(local, s);
        Assert.Equal("Description Facturation", s.Description);
        Assert.Single(s.Environnements); // local
        Assert.Equal("Finance", s.Source!.Domaine);
        Assert.Equal("Équipe", s.Source.Equipe);
        // Système désigné : rattachée et renommée.
        var r = SynchroLiaison.Suivre(l, c, c.Applications[1], systemes, autre.Id, T0, bilan);
        Assert.Same(autre, r);
        Assert.Equal("Paie", r.Nom);
        // Aucun système : créé.
        var n = SynchroLiaison.Suivre(l, c, c.Applications[2], systemes, null, T0, bilan);
        Assert.Contains(n, bilan.Nouveaux);
        Assert.Equal("CRM", n.Nom);
        Assert.Equal(["CRM"], bilan.Crees);
        Assert.Equal(["Facturation", "Ancien nom"], bilan.Rattaches);
        // Un système déjà suivi ne se rattache pas à une deuxième application.
        Assert.Throws<RegleMetierException>(() => SynchroLiaison.Suivre(l, c, App("x", "X"), systemes, local.Id, T0, new BilanSynchro()));
        Assert.Equal(["billing", "crm", "pay"], l.ApplicationsConnues.Order());
    }

    [Fact]
    public void Nom_pris_par_un_autre_systeme_suffixe_ou_garde()
    {
        var l = new Liaison { Id = Guid.NewGuid() };
        var autreLiaison = new SystemeCatalogue { Nom = "CRM", Source = new SourceNoosphere { LiaisonId = Guid.NewGuid(), ApplicationId = "crm" } };
        var systemes = new List<SystemeCatalogue> { autreLiaison, new() { Nom = "Paie" } };
        var c = Catalogue(App("crm", "CRM"));
        var cree = SynchroLiaison.Suivre(l, c, c.Applications![0], systemes, null, T0, new BilanSynchro());
        Assert.Equal("CRM (2)", cree.Nom);
        // Renommage vers un nom déjà pris : le système garde son nom, avec un avertissement.
        var bilan = SynchroLiaison.Appliquer(l, Catalogue(App("crm", "Paie")), systemes, T0);
        Assert.Equal("CRM (2)", cree.Nom);
        Assert.Single(bilan.Avertissements);
    }

    [Fact]
    public void Retiree_ou_absente_desactive_sans_supprimer_et_retour_reactive()
    {
        var l = new Liaison { Id = Guid.NewGuid() };
        var systemes = new List<SystemeCatalogue>();
        var c = Catalogue(App("a", "A"), App("b", "B"), App("c", "C"));
        foreach (var app in c.Applications!) SynchroLiaison.Suivre(l, c, app, systemes, null, T0, new BilanSynchro());
        systemes.Single(s => s.Nom == "C").Actif = false; // désactivé à la main : jamais réactivé par la liaison

        // A retirée, B supprimée ou devenue restreinte (absente), C inchangée.
        var bilan = SynchroLiaison.Appliquer(l, Catalogue(App("a", "A", cycle: "retired"), App("c", "C")), systemes, T0.AddMinutes(15));
        Assert.Equal(3, systemes.Count);
        var a = systemes.Single(s => s.Nom == "A");
        var b = systemes.Single(s => s.Nom == "B");
        Assert.False(a.Actif);
        Assert.Equal("retired", a.Source!.Cycle);
        Assert.False(b.Actif);
        Assert.True(b.Source!.Absente);
        Assert.Equal(["A", "B"], bilan.Desactives.Order());
        // Une seconde lecture identique ne change plus rien.
        Assert.False(SynchroLiaison.Appliquer(l, Catalogue(App("a", "A", cycle: "retired"), App("c", "C")), systemes, T0.AddMinutes(30)).AChange);

        // A remise en service, B de nouveau visible : réactivées ; C reste inactive.
        var retour = SynchroLiaison.Appliquer(l, Catalogue(App("a", "A"), App("b", "B"), App("c", "C")), systemes, T0.AddHours(1));
        Assert.True(a.Actif);
        Assert.True(b.Actif);
        Assert.False(b.Source.Absente);
        Assert.False(systemes.Single(s => s.Nom == "C").Actif);
        Assert.Equal(["A", "B"], retour.Reactives.Order());
    }

    [Fact]
    public void Suivi_automatique_des_nouvelles_applications_des_domaines_suivis()
    {
        var l = new Liaison { Id = Guid.NewGuid(), DomainesSuivis = ["fin"], ApplicationsConnues = ["old"] };
        var systemes = new List<SystemeCatalogue>();
        var bilan = SynchroLiaison.Appliquer(l, Catalogue(App("old", "Ancienne"), App("new", "Nouvelle"), App("rh1", "Paie RH", "rh"),
            App("gone", "Retirée", cycle: "retired")), systemes, T0);
        Assert.Equal(["Nouvelle"], bilan.Crees);
        Assert.Single(systemes);
        Assert.Contains("rh1", l.ApplicationsConnues);

        // Option coupée : une nouvelle application du domaine n'est pas suivie.
        l.SuiviAutomatique = false;
        SynchroLiaison.Appliquer(l, Catalogue(App("new2", "Encore")), systemes, T0);
        Assert.Single(systemes);
        Assert.Contains("new2", l.ApplicationsConnues);
    }

    [Fact]
    public void Suivi_automatique_met_de_cote_une_application_au_nom_proche_d_un_systeme_local()
    {
        // Le rapprochement par le nom se confirme toujours : ni rattachée ni créée, l'application attend le choix de l'administrateur.
        var l = new Liaison { Id = Guid.NewGuid(), DomainesSuivis = ["fin"], ApplicationsConnues = ["old"] };
        var local = new SystemeCatalogue { Nom = "Facturation" };
        var proche = new SystemeCatalogue { Nom = "Portail" };
        var systemes = new List<SystemeCatalogue> { local, proche };
        var c = Catalogue(App("old", "Ancienne"), App("billing", "facturation"), App("portal", "Portail RH"), App("crm", "CRM"));
        var bilan = SynchroLiaison.Appliquer(l, c, systemes, T0);
        Assert.Equal(["CRM"], bilan.Crees);
        Assert.Empty(bilan.Rattaches);
        Assert.Equal(["facturation", "Portail RH"], bilan.ARapprocher);
        Assert.Null(local.Source);
        Assert.Null(proche.Source);
        Assert.Equal(3, systemes.Count);
        Assert.Equal(["billing", "portal"], l.ApplicationsARapprocher);

        // Lectures suivantes : toujours en attente, même suivi automatique coupé, jamais rattachées seules.
        l.SuiviAutomatique = false;
        SynchroLiaison.Appliquer(l, c, systemes, T0.AddMinutes(15));
        Assert.Null(local.Source);
        Assert.Equal(["billing", "portal"], l.ApplicationsARapprocher);

        // Le choix de l'administrateur la sort de l'attente.
        SynchroLiaison.Suivre(l, c, c.Applications![1], systemes, local.Id, T0, new BilanSynchro());
        Assert.Equal("billing", local.Source!.ApplicationId);
        Assert.Equal(["portal"], l.ApplicationsARapprocher);

        // Le système proche a disparu : plus rien à confirmer, le système est créé ; une application disparue n'attend plus.
        systemes.Remove(proche);
        var suite = SynchroLiaison.Appliquer(l, c, systemes, T0.AddMinutes(30));
        Assert.Equal(["Portail RH"], suite.Crees);
        Assert.Empty(l.ApplicationsARapprocher);
        l.ApplicationsARapprocher = ["gone"];
        SynchroLiaison.Appliquer(l, c, systemes, T0.AddMinutes(45));
        Assert.Empty(l.ApplicationsARapprocher);
    }

    [Fact]
    public void Coupure_rend_les_systemes_locaux()
    {
        var l = new Liaison { Id = Guid.NewGuid() };
        var systemes = new List<SystemeCatalogue>();
        var c = Catalogue(App("a", "A"));
        SynchroLiaison.Suivre(l, c, c.Applications![0], systemes, null, T0, new BilanSynchro());
        systemes.Add(new SystemeCatalogue { Nom = "Autre", Source = new SourceNoosphere { LiaisonId = Guid.NewGuid() } });
        Assert.Equal(1, SynchroLiaison.RendreLocaux(l.Id, systemes));
        Assert.Null(systemes[0].Source);
        Assert.Equal("A", systemes[0].Nom);
        Assert.NotNull(systemes[1].Source);
    }

    [Fact]
    public void Noms_proches_pour_les_suggestions()
    {
        Assert.True(SynchroLiaison.NomsProches("Facturation", "facturation"));
        Assert.True(SynchroLiaison.NomsProches("Réseau-Clients", "reseau clients"));
        Assert.True(SynchroLiaison.NomsProches("Facturation", "Factura7ion"));
        Assert.True(SynchroLiaison.NomsProches("Portail RH", "Portail"));
        Assert.False(SynchroLiaison.NomsProches("API", "WEB"));
        Assert.False(SynchroLiaison.NomsProches("", "WEB"));
    }

    // ---------- Événements de déploiement ----------

    private static (Mep Mep, Dictionary<Guid, string> Apps) MepSuivie(Phase phase)
    {
        var cat = Guid.NewGuid();
        var autre = Guid.NewGuid();
        var mep = new Mep
        {
            Outil = "Billing", VersionCible = "4.2", Phase = phase, FuseauHoraire = "Europe/Paris",
            Communication = new Communication { DateAnnoncee = new DateOnly(2026, 11, 12), HeureDebutAnnoncee = new TimeOnly(21, 0), HeureFinAnnoncee = new TimeOnly(1, 0) },
        };
        mep.Systemes =
        [
            new Systeme { MepId = mep.Id, Nom = "Facturation", CatalogueId = cat, Environnements = [new() { Nom = "Production", Production = true }, new() { Nom = "Recette" }] },
            new Systeme { MepId = mep.Id, Nom = "Local", CatalogueId = autre, Environnements = [new() { Nom = "Interne" }] },
        ];
        return (mep, new Dictionary<Guid, string> { [cat] = "billing" });
    }

    [Theory]
    [InlineData(Phase.Preparation, "preparation")]
    [InlineData(Phase.Execution, "execution")]
    [InlineData(Phase.RetourArriere, "rollback")]
    [InlineData(Phase.Recette, "acceptance")]
    [InlineData(Phase.Cloturee, "closed")]
    [InlineData(Phase.Annulee, "cancelled")]
    public void Phase_vers_le_contrat(Phase phase, string attendue)
    {
        Assert.Equal(attendue, SynchroLiaison.PhaseContrat(phase));
        var (mep, apps) = MepSuivie(phase);
        var e = SynchroLiaison.Evenement(mep, apps, "https://mep.example/", T0);
        Assert.Equal(attendue, e.Phase);
        Assert.Equal(phase == Phase.Cloturee ? "success" : null, e.Issue);
    }

    [Fact]
    public void Issue_vers_le_contrat()
    {
        var (mep, _) = MepSuivie(Phase.Cloturee);
        mep.Issue = IssueMep.Reussie;
        Assert.Equal("success", SynchroLiaison.IssueContrat(mep));
        mep.EchecsEtapes = 1;
        Assert.Equal("success-with-incidents", SynchroLiaison.IssueContrat(mep));
        mep.Issue = IssueMep.Repliee;
        Assert.Equal("rolled-back", SynchroLiaison.IssueContrat(mep));
        mep.Issue = IssueMep.Echec;
        Assert.Equal("failed", SynchroLiaison.IssueContrat(mep));
        mep.Phase = Phase.Recette;
        Assert.Null(SynchroLiaison.IssueContrat(mep));
    }

    [Fact]
    public void Evenement_complet_report_et_suppression()
    {
        var (mep, apps) = MepSuivie(Phase.Preparation);
        var e = SynchroLiaison.Evenement(mep, apps, "https://mep.example/", T0);
        Assert.Equal(mep.Id, e.DeploiementId);
        Assert.Equal("Billing 4.2", e.Titre);
        Assert.False(e.Reportee);
        Assert.Equal("2026-11-12T20:00:00Z", e.DebutPrevu);
        Assert.Equal("2026-11-13T00:00:00Z", e.FinPrevue); // fin avant le début : le lendemain
        Assert.Equal($"https://mep.example/meps/{mep.Id}", e.Url);
        Assert.Equal(["billing"], e.ApplicationIds);
        Assert.Equal(["Production", "Recette"], e.Environnements.Select(x => x.Nom));
        Assert.True(e.Environnements[0].Production);
        Assert.Equal("2026-10-31T12:00:00Z", e.MisAJourLe);

        // Report en cours avec date visée : postponed, créneau à la date visée.
        mep.Report = new Report { Motif = "Gel", DateVisee = new DateOnly(2026, 12, 1) };
        var r = SynchroLiaison.Evenement(mep, apps, "https://mep.example", T0);
        Assert.True(r.Reportee);
        Assert.Equal("2026-12-01T20:00:00Z", r.DebutPrevu);
        // Sans heure de fin : début + indisponibilité ; sans date : null.
        mep.Report = null;
        mep.Communication.HeureFinAnnoncee = null;
        mep.IndisponibiliteMinutes = 90;
        Assert.Equal("2026-11-12T21:30:00Z", SynchroLiaison.Evenement(mep, apps, "https://mep.example", T0).FinPrevue);
        mep.Communication.DateAnnoncee = null;
        var sansDate = SynchroLiaison.Evenement(mep, apps, "https://mep.example", T0);
        Assert.Null(sansDate.DebutPrevu);
        Assert.Null(sansDate.FinPrevue);

        // L'empreinte ignore updatedAt, pas le reste.
        Assert.Equal(SynchroLiaison.Empreinte(sansDate), SynchroLiaison.Empreinte(sansDate with { MisAJourLe = "2030-01-01T00:00:00Z" }));
        Assert.NotEqual(SynchroLiaison.Empreinte(sansDate), SynchroLiaison.Empreinte(sansDate with { Phase = "execution" }));

        var supprimee = SynchroLiaison.Supprimee(e, T0.AddDays(1));
        Assert.Equal("cancelled", supprimee.Phase);
        Assert.Null(supprimee.Issue);
        Assert.Equal(e.ApplicationIds, supprimee.ApplicationIds);
    }

    [Fact]
    public void Anti_rebond_de_30_secondes_et_nouvel_essai()
    {
        var file = new FileDeploiements();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        file.Signaler(m1, T0);
        file.Signaler(m1, T0.AddSeconds(20)); // regroupée avec la première
        file.Signaler(m2, T0.AddSeconds(10));
        Assert.Empty(file.Prendre(T0.AddSeconds(29)));
        Assert.Equal(T0.AddSeconds(30), file.ProchaineEcheance());
        Assert.Equal([m1], file.Prendre(T0.AddSeconds(30)));
        Assert.Equal([m2], file.Prendre(T0.AddSeconds(40)));
        Assert.Equal(0, file.Count);
        file.Replanifier([m1], T0.AddMinutes(2));
        Assert.Empty(file.Prendre(T0.AddMinutes(1)));
        Assert.Equal([m1], file.Prendre(T0.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromSeconds(30), GestionMep.Api.Services.TachesLiaisons.Attente(1));
        Assert.Equal(TimeSpan.FromMinutes(2), GestionMep.Api.Services.TachesLiaisons.Attente(2));
        Assert.Equal(TimeSpan.FromMinutes(10), GestionMep.Api.Services.TachesLiaisons.Attente(3));
    }

    [Fact]
    public void Lecture_de_la_reponse_d_appairage_controle_le_mac()
    {
        using var s = CryptoLiaison.NouvelleCleSignature();
        using var c = CryptoLiaison.NouvelleCleChiffrement();
        var locales = new ClesLocales { Signature = CryptoLiaison.Jwk(s), Chiffrement = CryptoLiaison.Jwk(c) };
        using var ps = CryptoLiaison.NouvelleCleSignature();
        using var pc = CryptoLiaison.NouvelleCleChiffrement();
        var id = Guid.NewGuid();
        System.Text.Json.Nodes.JsonObject Reponse(string nonce) => new()
        {
            ["linkId"] = id.ToString(),
            ["peer"] = new System.Text.Json.Nodes.JsonObject
            {
                ["product"] = "noosphere", ["instanceName"] = "N", ["url"] = "https://n.test", ["version"] = "1",
                ["signingKey"] = CryptoLiaison.Jwk(ps).VersJson(), ["encryptionKey"] = CryptoLiaison.Jwk(pc).VersJson(),
            },
            ["capabilities"] = new System.Text.Json.Nodes.JsonArray("catalog:read"),
            ["nonce"] = nonce,
        };
        var bonne = Reponse("n1");
        bonne["mac"] = CryptoLiaison.Mac("7K4Q29XDM3PA", bonne);
        var l = GestionMep.Api.Services.ServiceLiaisons.LireReponseAppairage(System.Text.Encoding.UTF8.GetBytes(bonne.ToJsonString()), "7K4Q29XDM3PA", "n1",
            locales, "https://n.test", out _);
        Assert.NotNull(l);
        Assert.Equal(id, l.Id);
        Assert.Equal(["catalog:read"], l.Capacites);
        Assert.Equal(CryptoLiaison.Kid(CryptoLiaison.Jwk(ps)), l.KidPair);

        var fausse = Reponse("n1");
        fausse["mac"] = CryptoLiaison.Mac("AUTRECODE999", fausse);
        Assert.Null(GestionMep.Api.Services.ServiceLiaisons.LireReponseAppairage(System.Text.Encoding.UTF8.GetBytes(fausse.ToJsonString()), "7K4Q29XDM3PA", "n1",
            locales, "https://n.test", out var refus));
        Assert.Contains("MAC", refus);
        var nonce = Reponse("n2");
        nonce["mac"] = CryptoLiaison.Mac("7K4Q29XDM3PA", nonce);
        Assert.Null(GestionMep.Api.Services.ServiceLiaisons.LireReponseAppairage(System.Text.Encoding.UTF8.GetBytes(nonce.ToJsonString()), "7K4Q29XDM3PA", "n1",
            locales, "https://n.test", out refus));
        Assert.Contains("nonce", refus);
    }
}
