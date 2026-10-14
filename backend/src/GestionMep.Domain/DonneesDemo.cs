namespace GestionMep.Domain;

/// <summary>
/// Données de démonstration : la MEP « Portail de démonstration » reprend l'exemple de la maquette du jalon 4
/// (graphe par lignes, systèmes BDD, API, WEB, LDAP, MDM). Tout est fictif : aucune commande n'est à lancer.
/// Les comptes et les MEP dans les autres états du cycle de vie sont dans DonneesDemo.Scenarios.cs.
/// </summary>
public static partial class DonneesDemo
{
    /// <param name="Modele">Modèle « Modèle : déploiement API standard » construit à partir de la démo.</param>
    public record Jeu(Mep Mep, Mep Modele, List<SystemeCatalogue> Systemes, List<ResolutionType> Resolutions, List<CritereGoNoGo> Criteres);

    /// <summary>Approbations exigées avant le Go dans la démonstration.</summary>
    public const int ApprobationsMinimum = 1;

    private const string Fictif = "Cible fictive, aucune connexion";

    /// <summary>Bloc de code markdown (format des instructions, voir le jalon 4).</summary>
    private static string Code(string commande) => $"```\n{commande}\n```";

    /// <summary>Emplacement fictif d'une sauvegarde de démonstration (nom du système ou code de l'étape).</summary>
    private static string EmplacementDemo(string nom) => $"Dossier fictif `/sauvegardes-demo/{nom}` (aucun fichier réel)";

    private const string RecuperationDemo = "Restauration décrite dans le plan de retour arrière (simulation : rien à lancer).";

    /// <summary>Étape de type Sauvegarde, avec son emplacement et sa méthode de récupération fictifs.</summary>
    private static void EtapeSauvegarde(Etape e)
    {
        e.Type = TypeEtape.Sauvegarde;
        e.SauvegardeEmplacement = EmplacementDemo(e.Code);
        e.SauvegardeMethodeRecuperation = RecuperationDemo;
    }

    /// <summary>Critères go / no-go de démonstration.</summary>
    public static List<CritereGoNoGo> CriteresGoNoGo() =>
    [
        new() { Ordre = 1, Libelle = "Équipe présente et joignable" },
        new() { Ordre = 2, Libelle = "Sauvegarde disponible" },
        new() { Ordre = 3, Libelle = "Utilisateurs prévenus de l'interruption" },
        new() { Ordre = 4, Libelle = "Supervision en place" },
    ];

    /// <summary>Bibliothèque de recettes de démonstration : quelques fiches types rattachées aux systèmes WEB, API et BDD du catalogue.</summary>
    public static List<ModeleRecette> ModelesRecette(IReadOnlyList<SystemeCatalogue> catalogue, DateTimeOffset maintenant)
    {
        Guid Id(string nom) => catalogue.First(s => s.Nom == nom).Id;
        ModeleRecette Modele(string systeme, string titre, params (string Instructions, string Attendu)[] etapes) => new()
        {
            Titre = titre, SystemeCatalogueId = Id(systeme), Version = 1, ModifieLe = maintenant,
            Etapes = etapes.Select((t, i) => new EtapeModeleRecette { Ordre = i + 1, Instructions = t.Instructions, ResultatAttendu = t.Attendu }).ToList(),
        };
        return
        [
            Modele("WEB", "Connexion au portail",
                ("Ouvrir le portail et se connecter avec le compte de test de la recette.", "Le tableau de bord s'affiche avec le menu principal."),
                ("Se déconnecter puis ouvrir de nouveau une page interne.", "La page de connexion s'affiche.")),
            Modele("API", "Santé de l'API",
                ("SIMULATION : lire sans lancer.\n\n" + Code("curl -fsS https://commandes.exemple.invalid/sante"), "Réponse 200 avec la nouvelle version.")),
            Modele("BDD", "Droits de l'utilisateur applicatif",
                ("Se connecter avec l'utilisateur applicatif, puis lire une table métier.\n\n" + Code("psql -h bdd-demo -U app_user -d clients -c \"SELECT count(*) FROM clients;\""),
                    "Lecture autorisée ; le nombre de lignes est cohérent avec la veille.")),
        ];
    }

    /// <summary>
    /// Catalogue global de démonstration : systèmes (BDD, API, WEB, LDAP, MDM, BATCH, dans cet ordre)
    /// et résolutions types (API, BDD, WEB, API + BDD, dans cet ordre).
    /// </summary>
    public static (List<SystemeCatalogue> Systemes, List<ResolutionType> Resolutions) Catalogue()
    {
        var bdd = new SystemeCatalogue { Nom = "BDD", Description = "Base de données de test", Cible = Fictif };
        var api = new SystemeCatalogue { Nom = "API", Description = "Service API de test", Cible = Fictif };
        var web = new SystemeCatalogue { Nom = "WEB", Description = "Interface web de test", Cible = Fictif };
        var ldap = new SystemeCatalogue { Nom = "LDAP", Description = "Annuaire LDAP", Cible = Fictif };
        var mdm = new SystemeCatalogue { Nom = "MDM", Description = "MDM Phoenix", Cible = Fictif };
        var batch = new SystemeCatalogue { Nom = "BATCH", Description = "Traitements nocturnes", Cible = Fictif };
        var fixApi = new ResolutionType
        {
            Titre = "API : réponse de test absente",
            Symptomes = "Après le déploiement, l'appel de test de l'API ne renvoie **aucune réponse** ou une erreur **5xx**.",
            Diagnostic = "SIMULATION : lire le journal fictif du service.\n\n" + Code("journalctl -u commandes --since \"10 min ago\" | tail -50"),
            SystemeCatalogueIds = [api.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = "SIMULATION : lire sans lancer.\n\n" + Code("sudo systemctl restart commandes"), ResultatAttendu = "Le service passe à « active (running) »." },
            ],
            VerificationInstruction = Code("curl -fsS https://commandes.exemple.invalid/sante"),
            VerificationAttendu = "Réponse 200 avec la nouvelle version.",
        };
        var droitsBdd = new ResolutionType
        {
            Titre = "BDD : utilisateur applicatif retiré des droits",
            Symptomes = "La connexion à la base échoue avec `permission denied for table …` ou `role does not exist`.",
            Diagnostic = Code("psql -h <hôte> -U admin -d <base> -c \"\\du app_user\"") + "\n\nL'utilisateur app_user est absent ou n'a plus les droits.",
            SystemeCatalogueIds = [bdd.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = Code("psql -h <hôte> -U admin -d <base> -c \"GRANT CONNECT ON DATABASE <base> TO app_user;\""), ResultatAttendu = "GRANT" },
                new() { Ordre = 2, Instruction = Code("psql -h <hôte> -U admin -d <base> -c \"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO app_user;\""), ResultatAttendu = "GRANT" },
            ],
            VerificationInstruction = Code("psql -h <hôte> -U app_user -d <base> -c \"SELECT 1;\""),
            VerificationAttendu = "La requête renvoie 1 ligne, sans erreur de droits.",
        };
        var cdnWeb = new ResolutionType
        {
            Titre = "WEB : purge du cache CDN refusée",
            Symptomes = "La publication échoue avec **403** au moment de la purge du cache.",
            Diagnostic = "Vérifier dans la console CDN que le **jeton de purge** n'a pas expiré.",
            SystemeCatalogueIds = [web.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = "Générer un nouveau jeton dans la console CDN, puis le mettre dans `CDN_TOKEN` sur le serveur de build.", ResultatAttendu = "Nouveau jeton enregistré." },
                new() { Ordre = 2, Instruction = Code("./publier.sh --purge-seule"), ResultatAttendu = "Purge terminée." },
            ],
            VerificationInstruction = "Ouvrir le portail en navigation privée.",
            VerificationAttendu = "La nouvelle version s'affiche.",
        };
        var connexionBascule = new ResolutionType
        {
            Titre = "API + BDD : connexion refusée après bascule",
            Symptomes = "Après la bascule des données, l'API journalise `connection refused` ou `password authentication failed` vers la base.",
            Diagnostic = "SIMULATION : vérifier la chaîne de connexion de l'API.\n\n" + Code("grep -i datasource /opt/commandes/application.properties"),
            SystemeCatalogueIds = [api.Id, bdd.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = "Remettre l'hôte et l'utilisateur de la nouvelle base dans la configuration de l'API.", ResultatAttendu = "Configuration corrigée." },
                new() { Ordre = 2, Instruction = Code("sudo systemctl restart commandes"), ResultatAttendu = "Le service passe à « active (running) »." },
            ],
            VerificationInstruction = Code("curl -fsS https://commandes.exemple.invalid/sante"),
            VerificationAttendu = "Réponse 200 et base joignable.",
        };
        return ([bdd, api, web, ldap, mdm, batch], [fixApi, droitsBdd, cdnWeb, connexionBascule]);
    }

    /// <summary>Profils des comptes de démonstration (rôle, contact, expertises sur le catalogue de <see cref="Catalogue"/>).</summary>
    public static void CompleterProfils(Utilisateur alice, Utilisateur bob, Utilisateur claire, IReadOnlyList<SystemeCatalogue> catalogue)
    {
        Guid Id(string nom) => catalogue.Single(s => s.Nom == nom).Id;
        alice.Role = "Pilote / DBA";
        alice.Contact = "Fictif — poste A, 01 00 00 00 01";
        alice.Expertises = [new() { SystemeCatalogueId = Id("BDD"), Detail = "Expert de la base clients" }];
        bob.Role = "Développeur API";
        bob.Contact = "Fictif — poste B, 01 00 00 00 02";
        bob.Expertises =
        [
            new() { SystemeCatalogueId = Id("API"), Detail = "Service commandes" },
            new() { SystemeCatalogueId = Id("MDM"), Detail = "Flux de synchronisation vers le MDM" },
        ];
        claire.Role = "Développeuse front";
        claire.Contact = "Fictif — poste C, 01 00 00 00 03";
        claire.Expertises = [new() { SystemeCatalogueId = Id("WEB"), Detail = "Portail client" }];
    }

    public static Jeu Creer(Guid? alice, Guid? bob, Guid? claire, DateTimeOffset maintenant)
    {
        var (catalogue, resolutions) = Catalogue();
        var (fixApi, droitsBdd, cdnWeb) = (resolutions[0], resolutions[1], resolutions[2]);
        var mep = new Mep
        {
            Outil = OutilPortail,
            VersionActuelle = "1.0-démo",
            VersionCible = "2.0-démo",
            Objet = "Tester le graphe des étapes, les recettes et les résolutions sans agir sur un système réel.",
            FuseauHoraire = "Europe/Paris",
            Environnement = "Production",
            BilanActive = true,
            // Échéance initialisée à demain 23:59.
            EcheanceRepli = new DateTimeOffset(maintenant.UtcDateTime.Date.AddDays(1).AddHours(23).AddMinutes(59), TimeSpan.Zero),
            CreeParUtilisateurId = alice,
        };

        var pAlice = Participant(mep, "Alice", "Pilote / DBA", "Fictif — poste A, 01 00 00 00 01", alice);
        var pBob = Participant(mep, "Bob", "Développeur API", "Fictif — poste B, 01 00 00 00 02", bob);
        var pClaire = Participant(mep, "Claire", "Développeuse front", "Fictif — poste C, 01 00 00 00 03", claire);
        mep.PiloteId = pAlice.Id;
        // Bob est suppléant ; Claire n'est pas suppléante : elle approuve la MEP (décision en attente).
        mep.SuppleantIds = [pBob.Id];
        if (claire is { } c) mep.Approbations.Add(new Approbation { UtilisateurId = c, Nom = "Claire" });

        var bdd = Systeme(mep, catalogue[0], "Base clients : nouveau schéma");
        var api = Systeme(mep, catalogue[1], "Service commandes 2.0");
        var web = Systeme(mep, catalogue[2], "Portail client");
        var ldap = Systeme(mep, catalogue[3], "Lecture du groupe app-commandes");
        var mdm = Systeme(mep, catalogue[4], "Commandes poussées au nouveau format");

        // Sauvegardes : BDD, API et WEB pendant la MEP (étapes E0001 à E0003) ; LDAP et MDM non requises.
        Sauvegarde(bdd, StatutSauvegarde.Requise, "Sauvegarde **complète** de la base `clients` (E0001).");
        Sauvegarde(api, StatutSauvegarde.Requise, "Archive de `/opt/commandes` (E0002).");
        Sauvegarde(web, StatutSauvegarde.Requise, "Snapshot du volume `web-prod` (E0003).");
        Sauvegarde(ldap, StatutSauvegarde.NonRequise, justification: "Annuaire seulement lu par l'API : rien n'y est modifié.");
        Sauvegarde(mdm, StatutSauvegarde.NonRequise, justification: "Le MDM garde l'historique des versions : il restaure lui-même les commandes.");

        mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = "Package fictif", Version = "2.0-démo", Emplacement = "Aucun téléchargement à effectuer", SystemeId = api.Id });

        // Responsables techniques : Bob (API, MDM) et Claire (WEB), joignables ; Alice reste pilote.
        mep.ResponsablesTechniques =
        [
            new() { ParticipantId = pBob.Id, SystemeIds = [api.Id, mdm.Id], Perimetre = "Service commandes et flux MDM", Disponibilite = DisponibiliteJourJ.Joignable, ContactJourJ = "Fictif — poste B" },
            new() { ParticipantId = pClaire.Id, SystemeIds = [web.Id], Perimetre = "Portail client", Disponibilite = DisponibiliteJourJ.Joignable, ContactJourJ = "Fictif — poste C" },
        ];

        const string simulation = "SIMULATION : lire sans rien lancer.\n\n";
        // Ligne 0 : les sauvegardes (déjà faites).
        var e1 = Etape(mep, TypePlan.Deploiement, 1, 0, "Sauvegarde de la base clients", [bdd], pAlice, 20, Criticite.Elevee, EtatEtape.Reussie,
            simulation + "Faire la sauvegarde **complète** de la base `clients` avant toute modification.\n\n" + Code("pg_dump -h bdd-demo -U admin clients > /sauvegardes/clients.sql"),
            "Fichier de sauvegarde présent, taille supérieure à 0, aucune erreur.", "clients.sql (2,3 Go) — fictif");
        EtapeSauvegarde(e1);
        e1.ResolutionIds = [droitsBdd.Id];
        var e2 = Etape(mep, TypePlan.Deploiement, 2, 0, "Sauvegarde du service commandes", [api], pBob, 15, Criticite.Moderee, EtatEtape.Reussie,
            simulation + Code("tar czf /sauvegardes/commandes.tgz /opt/commandes"), "Archive créée.", "commandes.tgz OK — fictif");
        EtapeSauvegarde(e2);
        var e3 = Etape(mep, TypePlan.Deploiement, 3, 0, "Snapshot du portail", [web], pClaire, 15, Criticite.Moderee, EtatEtape.Reussie,
            simulation + "Dans la console de virtualisation :\n1. sélectionner le volume `web-prod`\n2. **Créer un snapshot** nommé `avant-2.0`",
            "Snapshot visible dans la liste.", "snap-0f3a — fictif");
        EtapeSauvegarde(e3);
        // Le snapshot se constate dans la console : simple vérification, sans résultat constaté à saisir.
        e3.Fin = FinEtape.Verification;
        e3.ResultatConstate = "";
        // Ligne 1 : migration, déploiement (en échec), publication.
        var e4 = Etape(mep, TypePlan.Deploiement, 4, 1, "Migration du schéma", [bdd], pAlice, 20, Criticite.Elevee, EtatEtape.AFaire,
            simulation + Code("psql -h bdd-demo -U admin -d clients -f migrations/2.0.sql"), "Toutes les migrations appliquées, aucune erreur.", "", e1, e2, e3);
        var e5 = Etape(mep, TypePlan.Deploiement, 5, 1, "Déploiement de l'API 2.0", [api, ldap], pBob, 30, Criticite.Elevee, EtatEtape.Echec,
            "Le déploiement redémarre le service : prévenir le support **avant** de lancer.\n\n" + Code("cd /opt/commandes && ./deploy.sh 2.0")
                + "\n\nPuis vérifier que l'API s'authentifie bien sur l'annuaire (groupe `app-commandes`).",
            "`GET /sante` renvoie 200 avec la version 2.0.", "Erreur fictive : réponse de test absente.", e1, e2, e3);
        e5.ResolutionIds = [fixApi.Id];
        var e6 = Etape(mep, TypePlan.Deploiement, 6, 1, "Publication du portail", [web], pClaire, 25, Criticite.Moderee, EtatEtape.AFaire,
            simulation + Code("npm ci && npm run build && ./publier.sh"), "Build terminé, fichiers publiés.", "", e1, e2, e3);
        e6.ResolutionIds = [cdnWeb.Id];
        // Ligne 2 : point de non-retour.
        var e7 = Etape(mep, TypePlan.Deploiement, 7, 2, "Bascule des données", [api, bdd, mdm], pBob, 15, Criticite.Bloquante, EtatEtape.AFaire,
            "**Irréversible** : les anciennes commandes sont converties au nouveau format et poussées vers le MDM.\n\n" + Code("./migrer-donnees.sh --appliquer"),
            "Les commandes existantes sont visibles dans la nouvelle API et dans le MDM.", "", e4, e5, e6);
        e7.PointDeNonRetour = true;
        e7.ResolutionIds = [resolutions[3].Id];
        var e8 = Etape(mep, TypePlan.Deploiement, 8, 3, "Bascule du portail vers l'API 2.0", [web, api], pClaire, 15, Criticite.Elevee, EtatEtape.AFaire,
            "Mettre `API_URL=https://commandes.exemple.invalid/v2` dans la configuration du portail, puis redémarrer.", "Le portail affiche les commandes.", "", e7);
        Etape(mep, TypePlan.Deploiement, 9, 4, "Contrôle final", [bdd, api, web], pAlice, 15, Criticite.Elevee, EtatEtape.AFaire,
            "- [ ] Passer une commande test\n- [ ] Vérifier qu'elle apparaît dans le MDM\n- [ ] Vérifier les tableaux de bord", "Tous les contrôles sont verts.", "", e8);

        // Plan de retour arrière
        mep.PlanRepli = new PlanRepli
        {
            QuandDeclencher = "Échec d'une étape sans résolution, résolution en échec, ou échéance atteinte.",
            DureeTesteeMinutes = 30,
            VersionARestaurer = "Version 1.0-démo fictive.",
            DonneesDepuisSauvegarde = "Aucune donnée réelle : exercice.",
            SiRepliEchoue = "Prévenir le pilote et l'astreinte fictive.",
        };
        var r1 = Etape(mep, TypePlan.RetourArriere, 1, 0, "Réinstaller l'API 1.0", [api], pBob, 20, Criticite.Bloquante, EtatEtape.AFaire,
            simulation + Code("cd /opt/commandes && ./deploy.sh 1.0"), "`GET /sante` renvoie la version 1.0.", "");
        var r2 = Etape(mep, TypePlan.RetourArriere, 2, 0, "Restaurer la base clients", [bdd], pAlice, 30, Criticite.Bloquante, EtatEtape.AFaire,
            simulation + Code("psql -h bdd-demo -U admin clients < /sauvegardes/clients.sql"), "Restauration sans erreur.", "");
        Etape(mep, TypePlan.RetourArriere, 3, 1, "Vérifier le service", [web], pClaire, 10, Criticite.Bloquante, EtatEtape.AFaire,
            "Ouvrir le portail et passer une **commande test**.", "Commande test enregistrée.", "", r1, r2);

        // Recette : une fiche par système.
        Recette(mep, web, "Parcours de commande",
            ("Ouvrir le portail en navigation privée.", "SIMULATION : la page affiche « Portail 2.0-démo »."),
            ("Passer une **commande test**.", "La commande apparaît dans « Mes commandes »."));
        Recette(mep, api, "Appels de l'API", (Code("curl -fsS https://commandes.exemple.invalid/sante"), "SIMULATION : réponse 200 « API 2.0-démo OK »."));
        Recette(mep, bdd, "Tables clients", (Code("psql -h bdd-demo -U app_user -d clients -c \"SELECT count(*) FROM clients;\""), "Le nombre de clients est inchangé."));
        Recette(mep, ldap, "Authentification annuaire", ("Se connecter au portail avec un compte du groupe `app-commandes`.", "Connexion acceptée."));
        Recette(mep, mdm, "Synchronisation MDM", ("Rechercher la commande test dans le MDM.", "La commande est présente au nouveau format."));

        mep.ProcedureRelueParId = bob;
        mep.ProcedureRelueLe = bob is null ? null : maintenant;

        var modele = Regles.Dupliquer(mep, "", commeModele: true, alice ?? Guid.Empty, maintenant);
        modele.Environnement = "";
        modele.Outil = "Modèle : déploiement API standard";
        modele.VersionActuelle = "";
        modele.VersionCible = "";
        modele.Objet = "Modèle réutilisable : déployer une nouvelle version de l'API avec sauvegardes, point de non-retour et recette.";
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

    private static void Sauvegarde(Systeme s, StatutSauvegarde statut, string contenu = "", string justification = "") =>
        s.Sauvegarde = new SauvegardeSysteme
        {
            Statut = statut, Moment = statut == StatutSauvegarde.Requise ? ModeSauvegarde.PendantMep : ModeSauvegarde.AvantMep,
            Contenu = contenu, Justification = justification,
            Emplacement = statut == StatutSauvegarde.Requise ? EmplacementDemo(s.Nom) : "",
            MethodeRecuperation = statut == StatutSauvegarde.Requise ? RecuperationDemo : "",
        };

    private static void Recette(Mep mep, Systeme systeme, string titre, params (string Instructions, string Attendu)[] etapes) =>
        mep.FichesRecette.Add(new FicheRecette
        {
            Ordre = mep.FichesRecette.Count + 1, SystemeId = systeme.Id, Titre = titre,
            Etapes = etapes.Select((t, i) => new EtapeTest { Ordre = i + 1, Instructions = t.Instructions, ResultatAttendu = t.Attendu }).ToList(),
        });

    /// <summary>
    /// Étape n° <paramref name="n"/> du plan, placée en fin de la ligne <paramref name="ligne"/>. Plus de responsable prévu :
    /// <paramref name="qui"/> n'est retenu comme exécutant que si l'étape a déjà été démarrée (état autre que À faire).
    /// </summary>
    private static Etape Etape(Mep mep, TypePlan plan, int n, int ligne, string action, Systeme[] systemes, Participant qui, int duree,
        Criticite criticite, EtatEtape etat, string instructions, string attendu, string preuve, params Etape[] prealables)
    {
        var e = new Etape
        {
            MepId = mep.Id, Plan = plan, Code = $"{(plan == TypePlan.Deploiement ? 'E' : 'R')}{n:0000}", Ligne = ligne,
            Ordre = mep.Etapes.Count(x => x.Plan == plan && x.Ligne == ligne), Action = action, SystemeIds = systemes.Select(s => s.Id).ToList(),
            DureeEstimeeMinutes = duree, ExecutantId = etat == EtatEtape.AFaire ? null : qui.Id, Criticite = criticite, Etat = etat, Instructions = instructions,
            ResultatAttendu = attendu, Preuve = preuve, ResultatConstate = etat is EtatEtape.Reussie or EtatEtape.Echec ? preuve : "", Dependances = prealables.Select(p => p.Id).ToList(),
            Revision = etat == EtatEtape.AFaire ? 0 : 1,
        };
        mep.Etapes.Add(e);
        return e;
    }
}
