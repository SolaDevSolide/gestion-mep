namespace GestionMep.Domain;

/// <summary>MEP de démonstration reprise à l'identique du classeur Modele_MEP_test_navigation.xlsm.</summary>
public static class DonneesDemo
{
    /// <param name="Modele">Modèle « Modèle : déploiement API standard » construit à partir de la démo.</param>
    public record Jeu(Mep Mep, Mep Modele, List<SystemeCatalogue> Systemes, List<ResolutionType> Resolutions, List<CritereGoNoGo> Criteres);

    /// <summary>Approbations exigées avant le Go dans la démonstration.</summary>
    public const int ApprobationsMinimum = 1;

    /// <summary>Critères go / no-go de démonstration.</summary>
    public static List<CritereGoNoGo> CriteresGoNoGo() =>
    [
        new() { Ordre = 1, Libelle = "Équipe présente et joignable" },
        new() { Ordre = 2, Libelle = "Sauvegarde disponible" },
        new() { Ordre = 3, Libelle = "Utilisateurs prévenus de l'interruption" },
        new() { Ordre = 4, Libelle = "Supervision en place" },
    ];

    /// <summary>Catalogue global de démonstration : systèmes et résolutions types.</summary>
    public static (List<SystemeCatalogue> Systemes, List<ResolutionType> Resolutions) Catalogue()
    {
        var bdd = new SystemeCatalogue { Nom = "BDD", Description = "Base de données de test", Cible = "Cible fictive, aucune connexion" };
        var api = new SystemeCatalogue { Nom = "API", Description = "Service API de test", Cible = "Cible fictive, aucune connexion" };
        var web = new SystemeCatalogue { Nom = "WEB", Description = "Interface web de test", Cible = "Cible fictive, aucune connexion" };
        var fixApi = new ResolutionType
        {
            Titre = "API : réponse de test absente",
            Symptomes = "L'appel de test de l'API ne renvoie aucune réponse ou une erreur 5xx.",
            Diagnostic = "SIMULATION : lire « service API arrêté » dans le journal fictif.",
            SystemeCatalogueId = api.Id,
            Corrections =
            [
                new() { Ordre = 1, Instruction = "SIMULATION : lire « réponse fictive rétablie ».", ResultatAttendu = "La phrase a été lue ; aucune manipulation réelle." },
            ],
            VerificationInstruction = "SIMULATION : relire le résultat de la correction.",
            VerificationAttendu = "La correction est conforme.",
        };
        var droitsBdd = new ResolutionType
        {
            Titre = "BDD : utilisateur applicatif retiré des droits",
            Symptomes = "La connexion à la base échoue avec « permission denied for table … » ou « role does not exist ».",
            Diagnostic = "psql -h <hôte> -U admin -d <base> -c \"\\du app_user\"\nL'utilisateur app_user est absent ou n'a plus les droits.",
            SystemeCatalogueId = bdd.Id,
            Corrections =
            [
                new() { Ordre = 1, Instruction = "psql -h <hôte> -U admin -d <base> -c \"GRANT CONNECT ON DATABASE <base> TO app_user;\"", ResultatAttendu = "GRANT" },
                new() { Ordre = 2, Instruction = "psql -h <hôte> -U admin -d <base> -c \"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO app_user;\"", ResultatAttendu = "GRANT" },
            ],
            VerificationInstruction = "psql -h <hôte> -U app_user -d <base> -c \"SELECT 1;\"",
            VerificationAttendu = "La requête renvoie 1 ligne, sans erreur de droits.",
        };
        return ([bdd, api, web], [fixApi, droitsBdd]);
    }

    public static Jeu Creer(Guid? alice, Guid? bob, Guid? claire, DateTimeOffset maintenant)
    {
        var (catalogue, resolutions) = Catalogue();
        var mep = new Mep
        {
            Outil = "Portail de démonstration",
            VersionActuelle = "1.0-démo",
            VersionCible = "2.0-démo",
            Objet = "Tester les boutons, filtres, dépendances et résolutions sans agir sur un système réel.",
            DateMep = DateOnly.FromDateTime(maintenant.UtcDateTime),
            FuseauHoraire = "Europe/Paris",
            Environnement = "Production",
            BilanActive = true,
            // Comme l'Excel : échéance initialisée à demain 23:59.
            EcheanceRepli = new DateTimeOffset(maintenant.UtcDateTime.Date.AddDays(1).AddHours(23).AddMinutes(59), TimeSpan.Zero),
            CreeParUtilisateurId = alice,
        };

        var pAlice = Participant(mep, "Alice", "Pilote / BDD", "Fictif — poste A", alice);
        var pBob = Participant(mep, "Bob", "Opérateur API", "Fictif — poste B", bob);
        var pClaire = Participant(mep, "Claire", "Opératrice WEB", "Fictif — poste C", claire);
        mep.PiloteId = pAlice.Id;
        // Bob est suppléant ; Claire n'est pas suppléante : elle approuve la MEP (décision en attente).
        mep.SuppleantIds = [pBob.Id];
        if (claire is { } c) mep.Approbations.Add(new Approbation { UtilisateurId = c, Nom = "Claire" });

        var bdd = Systeme(mep, catalogue[0], "Base de test");
        var api = Systeme(mep, catalogue[1], "Service de test");
        var web = Systeme(mep, catalogue[2], "Interface de test");

        mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = "Package fictif", Version = "2.0-démo", Emplacement = "Aucun téléchargement à effectuer", SystemeId = api.Id });

        var e1 = Etape(mep, 1, "Sauvegarde fictive vérifiée", bdd, pAlice, Criticite.Elevee, EtatEtape.Reussie,
            "SIMULATION : lire la note « sauvegarde disponible ». Ne lancer aucune commande.",
            "La note indique sauvegarde disponible.", "Prérempli : contrôle fictif validé.");
        // La sauvegarde est réalisée pendant la MEP, par E0001.
        e1.Type = TypeEtape.Sauvegarde;
        mep.Sauvegarde.Mode = ModeSauvegarde.PendantMep;
        var e2 = Etape(mep, 2, "Vérifier le service API fictif", api, pBob, Criticite.Moderee, EtatEtape.AFaire,
            "SIMULATION : lire cette fiche et écrire « API démo OK » dans la preuve.", "La preuve contient API démo OK.", "", e1);
        var e3 = Etape(mep, 3, "Vérifier la page WEB fictive", web, pClaire, Criticite.Faible, EtatEtape.AFaire,
            "SIMULATION : lire cette fiche et écrire « WEB démo OK » dans la preuve.", "La preuve contient WEB démo OK.", "", e1);
        var e4 = Etape(mep, 4, "Valider le parcours complet fictif", web, pClaire, Criticite.Elevee, EtatEtape.AFaire,
            "SIMULATION : attendre les deux préalables puis écrire « Parcours démo OK ».",
            "Les deux préalables sont réussis et la preuve est renseignée.", "", e2, e3);
        e4.PointDeNonRetour = true;
        var e5 = Etape(mep, 5, "Incident API simulé à corriger", api, pBob, Criticite.Moderee, EtatEtape.Echec,
            "SIMULATION : ouvrir la fiche de résolution. Ne modifier aucun service réel.",
            "La résolution et son contrôle final sont Conforme.", "Erreur fictive : réponse de test absente.", e1);
        e5.ResolutionIds = [resolutions[0].Id];
        e1.ResolutionIds = [resolutions[1].Id];

        // Onglet « Retour arrière » de l'Excel
        mep.PlanRepli = new PlanRepli
        {
            QuandDeclencher = "Échec d'une étape sans résolution, résolution en échec, ou échéance atteinte.",
            DureeTesteeMinutes = 30,
            VersionARestaurer = "Version 1.0-démo fictive.",
            DonneesDepuisSauvegarde = "Aucune donnée réelle : exercice.",
            SiRepliEchoue = "Prévenir le pilote et l'astreinte fictive.",
        };
        var r1 = Etape(mep, 1, "Retour arrière simulé", api, pBob, Criticite.Elevee, EtatEtape.AFaire,
            "SIMULATION : lire la phrase « version fictive 1.0 rétablie ». Ne lancer aucune commande.",
            "La phrase a été lue et la preuve fictive saisie.", "");
        r1.Plan = TypePlan.RetourArriere;
        r1.Code = "R0001";
        var r2 = Etape(mep, 2, "Vérifier le service rétabli", web, pClaire, Criticite.Elevee, EtatEtape.AFaire,
            "SIMULATION : écrire « Service 1.0 OK » dans la preuve.", "La preuve contient Service 1.0 OK.", "", r1);
        r2.Plan = TypePlan.RetourArriere;
        r2.Code = "R0002";

        mep.TestsRecette =
        [
            new() { Ordre = 1, Libelle = "Ouvrir la page d'accueil fictive", ResultatAttendu = "SIMULATION : la page affiche « Portail 2.0-démo ».", ResponsableId = pClaire.Id },
            new() { Ordre = 2, Libelle = "Appeler l'API de test", ResultatAttendu = "SIMULATION : l'appel renvoie « API 2.0-démo OK ».", ResponsableId = pBob.Id },
        ];

        mep.ProcedureRelueParId = bob;
        mep.ProcedureRelueLe = bob is null ? null : maintenant;

        var modele = Regles.Dupliquer(mep, "", commeModele: true, alice ?? Guid.Empty, maintenant);
        modele.Outil = "Modèle : déploiement API standard";
        modele.VersionActuelle = "";
        modele.VersionCible = "";
        modele.Objet = "Modèle réutilisable : déployer une nouvelle version de l'API avec sauvegarde, point de non-retour et recette.";
        modele.CreeParUtilisateurId = alice;
        return new Jeu(mep, modele, catalogue, resolutions, CriteresGoNoGo());
    }

    private static Participant Participant(Mep mep, string nom, string role, string contact, Guid? utilisateur)
    {
        var p = new Participant { MepId = mep.Id, Nom = nom, Role = role, Contact = contact, UtilisateurId = utilisateur };
        mep.Participants.Add(p);
        return p;
    }

    private static Systeme Systeme(Mep mep, SystemeCatalogue c, string impact)
    {
        var s = new Systeme { MepId = mep.Id, Nom = c.Nom, Cible = c.Cible, Impact = impact, CatalogueId = c.Id };
        mep.Systemes.Add(s);
        return s;
    }

    private static Etape Etape(Mep mep, int n, string action, Systeme systeme, Participant responsable, Criticite criticite,
        EtatEtape etat, string instructions, string attendu, string preuve, params Etape[] prealables)
    {
        var e = new Etape
        {
            MepId = mep.Id, Code = $"E{n:0000}", Ordre = n, Action = action, SystemeId = systeme.Id,
            ResponsableId = responsable.Id, Criticite = criticite, Etat = etat, Instructions = instructions,
            ResultatAttendu = attendu, Preuve = preuve, Dependances = prealables.Select(p => p.Id).ToList(),
            Revision = etat == EtatEtape.AFaire ? 0 : 1,
        };
        mep.Etapes.Add(e);
        return e;
    }
}
