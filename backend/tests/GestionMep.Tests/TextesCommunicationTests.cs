using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Variables des modèles de texte, modèles fournis et duplication de la communication (règles du domaine, sans base).</summary>
public class TextesCommunicationTests
{
    private static Mep MepExemple()
    {
        var mep = new Mep { Outil = "Portail", VersionActuelle = "1.0", VersionCible = "2.0", Objet = "Refonte", FuseauHoraire = "Europe/Paris", IndisponibiliteMinutes = 150 };
        var pilote = new Participant { MepId = mep.Id, Nom = "Alice", Contact = "poste 12" };
        var dev = new Participant { MepId = mep.Id, Nom = "Bob" };
        mep.Participants.AddRange([pilote, dev]);
        mep.PiloteId = pilote.Id;
        mep.ResponsablesTechniques.Add(new ResponsableTechnique { ParticipantId = dev.Id, Perimetre = "API" });
        var bdd = new Systeme
        {
            MepId = mep.Id, Nom = "BDD", Impact = "Nouveau schéma",
            Environnements = [new EnvironnementSysteme { Nom = "Préproduction" }, new EnvironnementSysteme { Nom = "Production", Production = true }],
        };
        var web = new Systeme { MepId = mep.Id, Nom = "WEB", Environnements = [new EnvironnementSysteme { Nom = "Recette" }] };
        mep.Systemes.AddRange([bdd, web]);
        mep.ObjetsImpactes.Add(new ObjetImpacte { MepId = mep.Id, SystemeId = web.Id, Categorie = "Packages", Nom = "portail-web", Version = "2.0" });
        mep.ObjetsImpactes.Add(new ObjetImpacte { MepId = mep.Id, SystemeId = bdd.Id, Categorie = "Tables", Nom = "clients" });
        return mep;
    }

    private static Dictionary<string, ValeurVariable> Valeurs(Mep mep, Equipe? equipe = null, IReadOnlyDictionary<Guid, string>? pieces = null) =>
        VariablesTexte.Valeurs(mep, equipe, "https://mep.exemple.invalid/meps/1", pieces ?? new Dictionary<Guid, string>());

    [Fact]
    public void Chaque_variable_a_une_valeur_et_une_cle_unique()
    {
        var v = Valeurs(MepExemple());
        Assert.Equal(VariablesTexte.Toutes.Count, VariablesTexte.Toutes.Select(x => x.Cle).Distinct().Count());
        Assert.Equal(VariablesTexte.Toutes.Select(x => x.Cle).Order(), v.Keys.Order());
        Assert.All(VariablesTexte.Toutes, x => Assert.Matches("^[a-z0-9_]+$", x.Cle));
    }

    [Fact]
    public void Les_valeurs_sont_mises_en_forme_pour_un_courriel()
    {
        var mep = MepExemple();
        mep.Communication = new Communication
        {
            NiveauRisque = NiveauRisque.Modere, Benefices = "- Plus rapide\n\n* Plus simple ", ArretApplicatif = true,
            HeureLimite = new TimeOnly(19, 30), DateAnnoncee = new DateOnly(2026, 10, 20), HeureDebutAnnoncee = new TimeOnly(20, 0),
        };
        var v = Valeurs(mep);
        Assert.Equal("mardi 20 octobre 2026", v["date"].Texte);
        Assert.Equal("20h00", v["heure_debut"].Texte);
        Assert.True(v["heure_fin"].EstManquante);
        Assert.Equal("2 h 30", v["indisponibilite"].Texte);
        Assert.Equal("oui, pendant 2 h 30", v["arret_applicatif"].Texte);
        Assert.Equal("19h30", v["heure_limite"].Texte);
        Assert.Equal("modéré", v["niveau_risque"].Texte);
        Assert.Equal(["Plus rapide", "Plus simple"], v["benefices"].Liste!);
        Assert.Equal(["BDD : Nouveau schéma", "WEB"], v["systemes"].Liste!);
        Assert.Equal(["BDD : clients (Tables)", "WEB : portail-web (Packages) 2.0"], v["objets_impactes"].Liste!);
        Assert.Equal(["BDD : Préproduction, Production", "WEB : Recette"], v["environnements"].Liste!);
        Assert.Equal("oui (BDD)", v["touche_production"].Texte);
        Assert.Equal(["Bob : API"], v["responsables_techniques"].Liste!);
        Assert.Equal("Alice", v["pilote"].Texte);
        Assert.Equal("poste 12", v["pilote_contact"].Texte);
        Assert.Equal("https://mep.exemple.invalid/meps/1", v["lien_mep"].Texte);
        // Informations absentes : à compléter.
        Assert.True(v["reference_demande"].EstManquante);
        Assert.True(v["validation_utilisateurs"].EstManquante);
        Assert.True(v["issue"].EstManquante);
        Assert.True(v["equipe"].EstManquante);
        Assert.True(v["signature"].EstManquante);
    }

    [Fact]
    public void Sans_environnement_production_ni_objet_les_variables_le_disent()
    {
        var mep = MepExemple();
        mep.Systemes[0].Environnements = [new EnvironnementSysteme { Nom = "Recette" }];
        mep.ObjetsImpactes.Clear();
        var v = Valeurs(mep);
        Assert.Equal("non", v["touche_production"].Texte);
        Assert.True(v["objets_impactes"].EstManquante);
        foreach (var s in mep.Systemes) s.Environnements = [];
        v = Valeurs(mep);
        Assert.True(v["environnements"].EstManquante);
        Assert.True(v["touche_production"].EstManquante);
    }

    [Fact]
    public void Sans_creneau_annonce_la_date_et_les_heures_sont_celles_du_go_et_de_la_cloture()
    {
        var mep = MepExemple();
        mep.DemarrageAutoriseLe = new DateTimeOffset(2026, 10, 20, 18, 5, 0, TimeSpan.Zero); // 20h05 à Paris
        mep.ClotureeLe = new DateTimeOffset(2026, 10, 20, 21, 40, 0, TimeSpan.Zero);
        mep.Issue = IssueMep.Repliee;
        mep.Bilan = new Bilan { CeQuiAMarche = "La bascule", Incidents = "", Actions = "Revoir\nle script" };
        mep.Communication.NiveauRisque = NiveauRisque.NonEvalue;
        var v = Valeurs(mep);
        Assert.Equal("mardi 20 octobre 2026", v["date"].Texte);
        Assert.Equal("20h05", v["heure_debut"].Texte);
        Assert.Equal("23h40", v["heure_fin"].Texte);
        Assert.Equal("mardi 20 octobre 2026 à 20h05", v["demarree_le"].Texte);
        Assert.Equal("repliée (retour arrière effectué)", v["issue"].Texte);
        Assert.Equal(["Ce qui a marché : La bascule", "Actions : Revoir le script"], v["bilan"].Liste!);
        Assert.True(v["niveau_risque"].EstManquante);
        Assert.Equal("non", v["arret_applicatif"].Texte);
    }

    [Fact]
    public void La_validation_cite_son_auteur_sa_date_et_sa_preuve()
    {
        var mep = MepExemple();
        var preuve = Guid.NewGuid();
        mep.Communication = new Communication { ValideeParUtilisateurs = true, ValideePar = "Claire", ValideeLe = new DateOnly(2026, 10, 15), PreuveValidationId = preuve };
        var v = Valeurs(mep, pieces: new Dictionary<Guid, string> { [preuve] = "PV.pdf" });
        Assert.Equal("validée par Claire le 15/10/2026 (preuve : PV.pdf)", v["validation_utilisateurs"].Texte);
        // Pièce supprimée depuis : la preuve n'est plus citée.
        Assert.Equal("validée par Claire le 15/10/2026", Valeurs(mep)["validation_utilisateurs"].Texte);
    }

    [Fact]
    public void L_equipe_donne_le_contact_support_et_la_signature_par_defaut()
    {
        var equipe = new Equipe { Nom = "Infrastructure" };
        var v = Valeurs(MepExemple(), equipe);
        Assert.Equal("Infrastructure", v["equipe"].Texte);
        Assert.Equal("L'équipe Infrastructure", v["signature"].Texte);
        Assert.True(v["contact_support"].EstManquante);
        equipe.Signature = "L'infra";
        equipe.ContactSupport = "poste 4550";
        v = Valeurs(MepExemple(), equipe);
        Assert.Equal("L'infra", v["signature"].Texte);
        Assert.Equal("poste 4550", v["contact_support"].Texte);
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(150, "2 h 30")]
    [InlineData(125, "2 h 05")]
    public void Les_durees_se_lisent_en_heures_et_minutes(int minutes, string attendu) => Assert.Equal(attendu, VariablesTexte.Duree(minutes));

    [Fact]
    public void Les_modeles_fournis_n_utilisent_que_des_variables_connues()
    {
        var cles = VariablesTexte.Toutes.Select(v => v.Cle).ToHashSet();
        Assert.Equal(4, ModelesTexteFournis.Tous.Count);
        Assert.Equal(4, ModelesTexteFournis.Tous.Select(f => f.Cle).Distinct().Count());
        foreach (var f in ModelesTexteFournis.Tous)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(f.Objet + f.Corps, @"\{\{\s*([a-z0-9_]+)\s*\}\}"))
                Assert.Contains(m.Groups[1].Value, cles);
    }

    [Fact]
    public void Un_modele_fourni_se_retablit_et_un_modele_d_equipe_non()
    {
        var equipe = Guid.NewGuid();
        var copies = ModelesTexteFournis.Manquants(equipe, ["rappel"], DateTimeOffset.UtcNow);
        Assert.Equal(["annonce", "demande-mep", "compte-rendu"], copies.Select(c => c.CleFournie!));
        var annonce = copies[0];
        Assert.False(ModelesTexteFournis.EstModifie(annonce));
        annonce.Corps = "Autre texte";
        annonce.Nom = "Annonce";
        Assert.True(ModelesTexteFournis.EstModifie(annonce));
        ModelesTexteFournis.Retablir(annonce);
        Assert.False(ModelesTexteFournis.EstModifie(annonce));
        Assert.Equal("Annonce aux utilisateurs", annonce.Nom);

        var maison = new ModeleTexte { EquipeId = equipe, Nom = "Maison", Corps = "x" };
        Assert.False(ModelesTexteFournis.EstModifie(maison));
        Assert.Throws<RegleMetierException>(() => ModelesTexteFournis.Retablir(maison));
    }

    [Fact]
    public void La_duplication_reprend_la_description_du_changement_mais_pas_la_demande_ni_la_validation()
    {
        var source = MepExemple();
        source.Communication = new Communication
        {
            ReferenceDemande = "CHG-1", NiveauRisque = NiveauRisque.Eleve, Benefices = "Plus rapide", UtilisateursImpactes = "Agences",
            ArretApplicatif = true, ActionUtilisateurs = "quitter", HeureLimite = new TimeOnly(19, 0), DateAnnoncee = new DateOnly(2026, 10, 20),
            ValideeParUtilisateurs = true, ValideePar = "Claire",
        };
        var copie = Regles.Dupliquer(source, false, Guid.NewGuid(), DateTimeOffset.UtcNow).Communication;
        Assert.Equal(NiveauRisque.Eleve, copie.NiveauRisque);
        Assert.Equal("Plus rapide", copie.Benefices);
        Assert.Equal("Agences", copie.UtilisateursImpactes);
        Assert.True(copie.ArretApplicatif);
        Assert.Equal("quitter", copie.ActionUtilisateurs);
        Assert.Equal(new TimeOnly(19, 0), copie.HeureLimite);
        Assert.Equal("", copie.ReferenceDemande);
        Assert.Null(copie.DateAnnoncee);
        Assert.False(copie.ValideeParUtilisateurs);
        Assert.Equal("", copie.ValideePar);
    }

    [Fact]
    public void Chaque_variable_est_documentee()
    {
        var doc = File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "docs", "Modèles-de-texte.md"));
        Assert.All(VariablesTexte.Toutes, v => Assert.Contains($"`{{{{{v.Cle}}}}}`", doc));
        Assert.All(VariablesTexte.Toutes.Select(v => v.Groupe).Distinct(), g => Assert.Contains($"### {g}", doc));
    }
}
