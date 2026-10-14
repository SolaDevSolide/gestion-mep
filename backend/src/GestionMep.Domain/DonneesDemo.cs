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

    /// <summary>
    /// Bibliothèque de recettes de démonstration : fiches types rattachées aux systèmes du catalogue (aucune pour MDM), avec
    /// leur nombre d'utilisations et leur date de création. Plusieurs fiches des MEP de démonstration en sont des copies
    /// (<see cref="RelierAuxBibliotheques"/>).
    /// </summary>
    public static List<ModeleRecette> ModelesRecette(IReadOnlyList<SystemeCatalogue> catalogue, DateTimeOffset maintenant)
    {
        Guid Id(string nom) => catalogue.First(s => s.Nom == nom).Id;
        ModeleRecette Modele(string systeme, string titre, int utilisations, int ilYaJours, params (string Instructions, string Attendu)[] etapes) => new()
        {
            Titre = titre, SystemeCatalogueId = Id(systeme), Version = 1, ModifieLe = maintenant.AddDays(-ilYaJours), CreeLe = maintenant.AddDays(-ilYaJours),
            Utilisations = utilisations,
            Etapes = etapes.Select((t, i) => new EtapeModeleRecette { Ordre = i + 1, Instructions = t.Instructions, ResultatAttendu = t.Attendu }).ToList(),
        };
        return
        [
            Modele("WEB", "Connexion au portail", 6, 210,
                ("Ouvrir le portail et se connecter avec le compte de test de la recette.", "Le tableau de bord s'affiche avec le menu principal."),
                ("Se déconnecter puis ouvrir de nouveau une page interne.", "La page de connexion s'affiche.")),
            Modele("WEB", "Affichage sur mobile", 1, 35,
                ("Ouvrir les pages principales sur un téléphone de test.", "Les pages s'affichent sans défilement horizontal.")),
            Modele("API", TitreRecetteSanteApi, 9, 240,
                ("SIMULATION : lire sans lancer.\n\n" + Code("curl -fsS https://commandes.exemple.invalid/sante"), "Réponse 200 avec la nouvelle version.")),
            Modele("API", "Temps de réponse des appels clés", 3, 120,
                (Code("curl -fsS -w '%{time_total}' https://commandes.exemple.invalid/commandes/test"), "Réponse en moins d'une seconde.")),
            Modele("BDD", "Droits de l'utilisateur applicatif", 4, 180,
                ("Se connecter avec l'utilisateur applicatif, puis lire une table métier.\n\n" + Code("psql -h bdd-demo -U app_user -d clients -c \"SELECT count(*) FROM clients;\""),
                    "Lecture autorisée ; le nombre de lignes est cohérent avec la veille.")),
            Modele("LDAP", TitreRecetteGroupe, 3, 150,
                ("Se connecter avec un compte membre du nouveau groupe.", "Le compte appartient au groupe et accède à l'application.")),
            Modele("BATCH", TitreRecettePlanification, 2, 90,
                ("Vérifier la planification du traitement dans l'ordonnanceur.", "Le traitement est planifié à l'heure prévue.")),
        ];
    }

    private const string TitreRecetteSanteApi = "Santé de l'API";
    private const string TitreRecetteGroupe = "Connexion d'un compte du groupe applicatif";
    private const string TitreRecettePlanification = "Planification dans l'ordonnanceur";

    /// <summary>
    /// Bibliothèque d'étapes de démonstration : étapes types de chaque système du catalogue (une sur plusieurs systèmes),
    /// normales et de sauvegarde, avec leur nombre d'utilisations (MEP de la démonstration et MEP plus anciennes) et leur
    /// date de création. Les étapes des MEP de démonstration qui en sont des copies sont reliées par <see cref="RelierAuxBibliotheques"/>.
    /// </summary>
    public static List<ModeleEtape> EtapesModeles(IReadOnlyList<SystemeCatalogue> catalogue, IReadOnlyList<ResolutionType> resolutions,
        DateTimeOffset maintenant)
    {
        Guid Id(string nom) => catalogue.First(s => s.Nom == nom).Id;
        const string simulation = "SIMULATION : lire sans rien lancer.\n\n";
        ModeleEtape M(string systemes, string action, Criticite criticite, int duree, string instructions, string attendu, int utilisations,
            int ilYaJours, FinEtape fin = FinEtape.TestUnitaire, params ResolutionType[] resolutionsLiees) => new()
        {
            Action = action, SystemeCatalogueIds = systemes.Split('+').Select(Id).ToList(), Criticite = criticite, DureeEstimeeMinutes = duree,
            Instructions = instructions, ResultatAttendu = attendu, Fin = fin, ResolutionIds = resolutionsLiees.Select(r => r.Id).ToList(),
            Utilisations = utilisations, CreeLe = maintenant.AddDays(-ilYaJours).AddHours(-(ilYaJours % 7)), Version = 1 + ilYaJours % 3,
            ModifieLe = maintenant.AddDays(-ilYaJours / 2),
        };
        ModeleEtape Sauvegarde(ModeleEtape m)
        {
            m.Type = TypeEtape.Sauvegarde;
            m.SauvegardeEmplacement = "Dossier fictif `/sauvegardes-demo/<MEP>` (aucun fichier réel)";
            m.SauvegardeMethodeRecuperation = RecuperationDemo;
            return m;
        }
        var (fixApi, droitsBdd, cdnWeb, disqueBdd) = (resolutions[0], resolutions[1], resolutions[2], resolutions[6]);
        return
        [
            // BDD
            Sauvegarde(M("BDD", ModeleSauvegardeBase, Criticite.Elevee, 20,
                simulation + "Faire la sauvegarde **complète** de la base avant toute modification.\n\n" + Code("pg_dump -h bdd-demo -U admin <base> > /sauvegardes/<base>.sql"),
                "Fichier de sauvegarde présent, taille supérieure à 0, aucune erreur.", 14, 230, FinEtape.TestUnitaire, disqueBdd)),
            Sauvegarde(M("BDD", ModeleSauvegardeTables, Criticite.Elevee, 15,
                simulation + Code("pg_dump -h bdd-demo -U admin -t <table> <base> > /sauvegardes/<table>.sql"), "Un fichier par table, sans erreur.", 9, 160)),
            M("BDD", ModeleMigration, Criticite.Elevee, 20,
                simulation + "Lancer les scripts du dossier `migrations/` dans l'ordre, avec le compte de déploiement.\n\n" + Code("psql -h bdd-demo -U admin -d <base> -f migrations/<version>.sql"),
                "Toutes les migrations appliquées, aucune erreur ; la table de version porte le dernier numéro.", 14, 200, FinEtape.TestUnitaire, droitsBdd),
            M("BDD", "Contrôle du nombre de lignes après migration", Criticite.Moderee, 5,
                Code("psql -h bdd-demo -U lecture -d <base> -c \"SELECT count(*) FROM <table>;\"") + "\n\nComparer avec le relevé d'avant la migration.",
                "Écart nul, ou égal au nombre de lignes ajoutées par le script.", 7, 140),
            M("BDD", "Recalcul des statistiques de la base", Criticite.Faible, 10,
                simulation + Code("psql -h bdd-demo -U admin -d <base> -c \"VACUUM ANALYZE;\""), "Commande terminée sans erreur.", 3, 60, FinEtape.Verification),
            M("BDD", ModeleRestaurationBase, Criticite.Bloquante, 30,
                simulation + Code("psql -h bdd-demo -U admin <base> < /sauvegardes/<base>.sql"), "Restauration sans erreur.", 11, 220),
            M("BDD", "Ouverture d'une session de maintenance", Criticite.Faible, 2,
                "Se connecter avec le compte de déploiement et vérifier la base cible.\n\n" + Code("SELECT current_database();"), "Session ouverte sur la bonne base.", 0, 4,
                FinEtape.Verification),
            // API
            Sauvegarde(M("API", ModeleSauvegardeService, Criticite.Moderee, 10,
                simulation + Code("tar czf /sauvegardes/<service>-<version>.tgz /opt/<service>"), "Archive créée, taille supérieure à 0.", 12, 235)),
            M("API", ModeleDeploiementService, Criticite.Elevee, 20,
                "Le déploiement redémarre le service : prévenir le support **avant** de lancer.\n\n" + Code("cd /opt/<service> && ./deploy.sh <version>"),
                "`GET /sante` renvoie 200 avec la nouvelle version.", 19, 238, FinEtape.TestUnitaire, fixApi),
            M("API", ModeleReinstallationService, Criticite.Bloquante, 15,
                simulation + Code("cd /opt/<service> && ./deploy.sh <version précédente>"), "`GET /sante` renvoie la version précédente.", 9, 190),
            M("API", "Vérification des journaux après redémarrage", Criticite.Faible, 10,
                simulation + Code("journalctl -u <service> --since \"10 min ago\" | grep -i error"), "Aucune erreur depuis le redémarrage.", 6, 100,
                FinEtape.Verification, fixApi),
            // WEB
            M("WEB", ModelePublicationSite, Criticite.Moderee, 20,
                simulation + Code("npm ci && npm run build && ./publier.sh <site>"), "Build terminé, la page d'accueil affiche la nouvelle version.", 12, 205,
                FinEtape.TestUnitaire, cdnWeb),
            Sauvegarde(M("WEB", ModeleSnapshotWeb, Criticite.Moderee, 10,
                simulation + "Dans la console de virtualisation :\n1. sélectionner le volume du site\n2. **Créer un snapshot** nommé `avant-<version>`",
                "Snapshot visible dans la liste.", 6, 170, FinEtape.Verification)),
            M("WEB", "Vidage des caches du frontal", Criticite.Moderee, 5,
                simulation + Code("./publier.sh <site> --purge-seule"), "Les pages sont servies avec les nouveaux fichiers.", 4, 80, FinEtape.Verification, cdnWeb),
            M("WEB+API", "Bascule du trafic vers la nouvelle version", Criticite.Bloquante, 5,
                "Modifier le pool du répartiteur de charge et activer le nouveau nœud.", "Le trafic passe par le nouveau nœud, sans erreur 5xx.", 3, 45),
            // LDAP
            M("LDAP", ModeleGroupesLdap, Criticite.Elevee, 15,
                simulation + Code("ldapadd -H ldap://annuaire-demo -D cn=admin -f <groupes>.ldif"), "Les groupes sont visibles dans l'annuaire avec leurs membres.", 4, 150,
                FinEtape.TestUnitaire, resolutions[5]),
            Sauvegarde(M("LDAP", "Export LDIF de la branche applicative", Criticite.Moderee, 5,
                simulation + Code("ldapsearch -H ldap://annuaire-demo -b ou=applis,dc=demo > /sauvegardes/annuaire.ldif"), "Fichier LDIF non vide.", 3, 150)),
            // MDM
            M("MDM", "Publication du référentiel vers les abonnés", Criticite.Elevee, 15,
                "Lancer la publication du référentiel vers les applications abonnées depuis la console du MDM.",
                "Publication « terminée », aucun abonné en erreur.", 2, 70),
            // BATCH
            M("BATCH", "Suspension des traitements de nuit", Criticite.Elevee, 5,
                simulation + Code("ordonnanceur suspendre <traitement>"), "Aucun traitement « en cours » ni « planifié ».", 5, 130, FinEtape.Verification),
            M("BATCH", ModeleReactivationBatch, Criticite.Moderee, 10,
                simulation + Code("ordonnanceur activer <traitement>"), "Le traitement est planifié à l'heure prévue.", 5, 129, FinEtape.TestUnitaire, resolutions[4]),
            Sauvegarde(M("BATCH", "Export des paramètres de planification", Criticite.Moderee, 5,
                simulation + Code("ordonnanceur exporter --format json > /sauvegardes/planification.json"), "Fichier présent et lisible.", 2, 95)),
            // Plusieurs systèmes
            M("BDD+API+WEB", ModeleControleFinal, Criticite.Elevee, 15,
                "- [ ] Passer une commande ou une opération de test\n- [ ] Vérifier qu'elle apparaît en base\n- [ ] Vérifier les tableaux de bord",
                "Tous les contrôles sont verts.", 16, 225, FinEtape.Verification),
        ];
    }

    private const string ModeleSauvegardeBase = "Sauvegarde complète de la base";
    private const string ModeleSauvegardeTables = "Sauvegarde des tables modifiées";
    private const string ModeleMigration = "Exécution des scripts de migration";
    private const string ModeleRestaurationBase = "Restauration de la base depuis la sauvegarde";
    private const string ModeleSauvegardeService = "Sauvegarde du service applicatif";
    private const string ModeleDeploiementService = "Déploiement d'une nouvelle version du service";
    private const string ModeleReinstallationService = "Réinstallation de la version précédente du service";
    private const string ModelePublicationSite = "Publication du site web";
    private const string ModeleSnapshotWeb = "Snapshot du volume web";
    private const string ModeleGroupesLdap = "Création de groupes dans l'annuaire";
    private const string ModeleReactivationBatch = "Réactivation des traitements de nuit";
    private const string ModeleControleFinal = "Contrôle final de bout en bout";

    /// <summary>Étapes des MEP de démonstration insérées depuis la bibliothèque d'étapes, par outil (le modèle de MEP reprend celles du portail).</summary>
    private static readonly Dictionary<string, (string Code, string Modele)[]> CopiesEtapes = new()
    {
        [OutilPortail] =
        [
            ("E0001", ModeleSauvegardeBase), ("E0002", ModeleSauvegardeService), ("E0003", ModeleSnapshotWeb), ("E0004", ModeleMigration),
            ("E0005", ModeleDeploiementService), ("E0006", ModelePublicationSite), ("E0009", ModeleControleFinal),
            ("R0001", ModeleReinstallationService), ("R0002", ModeleRestaurationBase),
        ],
        [OutilIncomplete] = [("E0001", ModeleSauvegardeTables), ("E0002", ModeleMigration)],
        [OutilPrete] =
        [
            ("E0001", ModeleSauvegardeService), ("E0002", ModeleGroupesLdap), ("E0003", ModeleDeploiementService), ("E0004", ModelePublicationSite),
            ("E0005", ModeleControleFinal), ("R0001", ModeleReinstallationService),
        ],
        [OutilExecution] =
        [
            ("E0001", ModeleSauvegardeTables), ("E0002", ModeleSauvegardeService), ("E0003", ModeleMigration), ("E0004", ModeleDeploiementService),
            ("E0005", ModelePublicationSite), ("E0007", ModeleControleFinal), ("R0001", ModeleReinstallationService),
        ],
        [OutilRecette] =
        [
            ("E0001", ModeleSauvegardeBase), ("E0002", ModeleMigration), ("E0003", ModeleDeploiementService), ("E0004", ModelePublicationSite),
            ("R0002", ModeleReinstallationService), ("R0003", ModeleRestaurationBase),
        ],
        [OutilRetourArriere] =
        [
            ("E0001", ModeleSauvegardeBase), ("E0002", ModeleSauvegardeService), ("E0003", ModeleMigration), ("E0004", ModeleDeploiementService),
            ("E0006", ModeleControleFinal), ("R0001", ModeleReinstallationService), ("R0002", ModeleRestaurationBase),
        ],
        [OutilReussie] =
        [
            ("E0001", ModeleSauvegardeBase), ("E0002", ModeleMigration), ("E0003", ModeleDeploiementService), ("E0005", ModeleReactivationBatch),
            ("R0001", ModeleRestaurationBase), ("R0002", ModeleReinstallationService),
        ],
        [OutilRepliee] =
        [
            ("E0001", ModeleSnapshotWeb), ("E0002", ModeleSauvegardeService), ("E0003", ModeleDeploiementService), ("R0002", ModeleReinstallationService),
        ],
    };

    /// <summary>Fiches de recette des MEP de démonstration insérées depuis la bibliothèque de recettes (outil, titre de la fiche, modèle).</summary>
    private static readonly (string Outil, string Fiche, string Modele)[] CopiesRecettes =
    [
        (OutilPrete, "API annuaire", TitreRecetteSanteApi), (OutilPrete, "Groupes d'agence", TitreRecetteGroupe),
        (OutilExecution, "API paiement", TitreRecetteSanteApi), (OutilRecette, "Accès gestionnaires", TitreRecetteGroupe),
        (OutilReussie, "API stocks", TitreRecetteSanteApi), (OutilReussie, "Calcul nocturne", TitreRecettePlanification),
        (OutilRepliee, "API partenaires", TitreRecetteSanteApi),
    ];

    /// <summary>
    /// Relie les MEP de démonstration aux bibliothèques : étapes copiées d'une étape modèle, fiches copiées d'un modèle de
    /// recette. Les éléments utilisés (résolutions comprises) sont notés comme déjà comptés, leurs compteurs les incluant :
    /// modifier ensuite une MEP de démonstration ne les compte pas une seconde fois.
    /// </summary>
    public static void RelierAuxBibliotheques(IEnumerable<Mep> meps, IReadOnlyList<ModeleEtape> etapes, IReadOnlyList<ModeleRecette> recettes)
    {
        foreach (var mep in meps)
        {
            var outil = mep.EstModele ? OutilPortail : mep.Outil;
            foreach (var (code, modele) in CopiesEtapes.GetValueOrDefault(outil, []))
                mep.Etapes.Single(e => e.Code == code).ModeleEtapeId = etapes.Single(m => m.Action == modele).Id;
            foreach (var (_, fiche, modele) in CopiesRecettes.Where(c => c.Outil == outil))
                mep.FichesRecette.Single(f => f.Titre == fiche).ModeleRecetteId = recettes.Single(m => m.Titre == modele).Id;
            mep.FichesRecette = [.. mep.FichesRecette];
            if (!mep.EstModele) mep.ElementsBibliothequeComptes = [.. Regles.ElementsBibliotheque(mep).Order()];
        }
    }

    /// <summary>
    /// Résolutions types de démonstration (ordre de <see cref="Catalogue"/>) : date de création et nombre d'utilisations
    /// (MEP de la démonstration et MEP plus anciennes).
    /// </summary>
    public static void DaterResolutions(IReadOnlyList<ResolutionType> resolutions, DateTimeOffset maintenant)
    {
        (int Utilisations, int IlYaJours)[] valeurs = [(12, 250), (8, 245), (7, 215), (3, 120), (2, 85), (1, 60), (4, 110)];
        foreach (var (r, (n, jours)) in resolutions.Zip(valeurs))
        {
            r.Utilisations = n;
            r.CreeeLe = maintenant.AddDays(-jours);
        }
    }

    /// <summary>
    /// Catalogue global de démonstration : systèmes (BDD, API, WEB, LDAP, MDM, BATCH, dans cet ordre)
    /// et résolutions types (API, BDD, WEB, API + BDD, BATCH, LDAP, BDD, dans cet ordre).
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
        var chaineBloquee = new ResolutionType
        {
            Titre = "BATCH : chaîne nocturne bloquée en attente",
            Symptomes = "Après la relance, la chaîne reste à l'état « en attente » dans l'ordonnanceur.",
            Diagnostic = "SIMULATION : lister les dépendances non satisfaites.\n\n" + Code("ordonnanceur dependances <traitement>"),
            SystemeCatalogueIds = [batch.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = Code("ordonnanceur liberer <traitement>"), ResultatAttendu = "La chaîne passe « planifiée »." },
            ],
            VerificationInstruction = "Consulter la chaîne dans l'ordonnanceur.",
            VerificationAttendu = "Chaîne planifiée à l'heure prévue.",
        };
        var groupeLdap = new ResolutionType
        {
            Titre = "LDAP : groupe absent après import",
            Symptomes = "`ldapsearch` ne trouve pas le nouveau groupe ; les comptes de l'agence n'ont pas accès.",
            Diagnostic = Code("ldapsearch -H ldap://annuaire-demo -b ou=groupes,dc=demo \"(cn=<groupe>)\""),
            SystemeCatalogueIds = [ldap.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = Code("ldapadd -H ldap://annuaire-demo -D cn=admin -f <groupe>.ldif"), ResultatAttendu = "adding new entry" },
            ],
            VerificationInstruction = "Se connecter avec un compte du groupe.",
            VerificationAttendu = "Connexion acceptée.",
        };
        var disqueBdd = new ResolutionType
        {
            Titre = "BDD : espace disque insuffisant pour la sauvegarde",
            Symptomes = "La sauvegarde échoue avec `No space left on device`.",
            Diagnostic = Code("df -h /sauvegardes"),
            SystemeCatalogueIds = [bdd.Id],
            Corrections =
            [
                new() { Ordre = 1, Instruction = "Supprimer les sauvegardes de plus de 30 jours du dossier `/sauvegardes`.", ResultatAttendu = "Au moins 20 % d'espace libre." },
                new() { Ordre = 2, Instruction = "Relancer la sauvegarde.", ResultatAttendu = "Fichier de sauvegarde présent, sans erreur." },
            ],
            VerificationInstruction = Code("ls -lh /sauvegardes"),
            VerificationAttendu = "La sauvegarde du jour est présente.",
        };
        return ([bdd, api, web, ldap, mdm, batch], [fixApi, droitsBdd, cdnWeb, connexionBascule, chaineBloquee, groupeLdap, disqueBdd]);
    }

    /// <summary>Nom de l'équipe de démonstration d'Alice et Bob (équipe du pilote de la plupart des MEP).</summary>
    public const string NomEquipe = "Exploitation applicative";

    /// <summary>Nom de l'équipe de démonstration de Claire.</summary>
    public const string NomEquipeWeb = "Développement web";

    /// <summary>Nom du modèle de texte créé par l'équipe <see cref="NomEquipe"/> (en plus des quatre modèles fournis).</summary>
    public const string NomModeleAstreinte = "Prévenir l'astreinte";

    /// <summary>Équipe de démonstration et ses modèles de texte.</summary>
    public record EquipeDemo(Domain.Equipe Equipe, List<ModeleTexte> Modeles);

    /// <summary>
    /// Équipes de démonstration, chacune avec un contact support, une signature et ses modèles de texte : « Exploitation
    /// applicative » (Alice et Bob) a adapté l'annonce fournie (modifiée par Alice) et créé un modèle pour l'astreinte ;
    /// « Développement web » (Claire) garde les modèles fournis tels quels.
    /// </summary>
    public static List<EquipeDemo> Equipes(Guid alice, Guid bob, Guid claire, DateTimeOffset maintenant)
    {
        var exploitation = new Domain.Equipe
        {
            Nom = NomEquipe, Description = "Équipe fictive de la démonstration : exploitation et pilotage des MEP.", MembreIds = [alice, bob],
            CreeeLe = maintenant, ContactSupport = "support-exploitation@exemple.invalid (fictif)", Signature = "L'équipe Exploitation applicative",
        };
        var modeles = ModelesTexteFournis.Manquants(exploitation.Id, [], maintenant);
        var annonce = modeles.Single(m => m.CleFournie == "annonce");
        annonce.Corps = annonce.Corps.Replace("{{signature}}", "Merci de votre compréhension.\n\n{{signature}}", StringComparison.Ordinal);
        annonce.Version = 2;
        annonce.ModifieLe = maintenant.AddDays(-3);
        annonce.ModifieParId = alice;
        modeles.Add(new ModeleTexte
        {
            EquipeId = exploitation.Id, Nom = NomModeleAstreinte, Usage = UsageModeleTexte.Autre,
            Objet = "[Astreinte] MEP {{application}} {{version_cible}} le {{date}}",
            Corps = """
                Bonjour,

                Une mise en production de **{{application}}** ({{version_actuelle}} → {{version_cible}}) est prévue le **{{date}}** de {{heure_debut}} à {{heure_fin}}.

                Niveau de risque : **{{niveau_risque}}**. Référence : {{reference_demande}}.

                **Systèmes concernés**
                {{systemes}}

                **Responsables techniques joignables**
                {{responsables_techniques}}

                Pilote : {{pilote}} ({{pilote_contact}}). Suivi de la MEP : {{lien_mep}}

                {{signature}}
                """,
            Version = 1, CreeLe = maintenant.AddDays(-5), ModifieLe = maintenant.AddDays(-5), ModifieParId = bob,
        });

        var web = new Domain.Equipe
        {
            Nom = NomEquipeWeb, Description = "Équipe fictive de la démonstration : portail et sites publics.", MembreIds = [claire],
            CreeeLe = maintenant, ContactSupport = "assistance-web@exemple.invalid (fictif)", Signature = "Claire, pour l'équipe Développement web",
        };
        return [new(exploitation, modeles), new(web, ModelesTexteFournis.Manquants(web.Id, [], maintenant))];
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

        // Communication : de quoi remplir les textes à copier ; l'indisponibilité prévue reste à saisir (information manquante signalée).
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0412",
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Suivi des commandes en temps réel dans le portail\nCommandes visibles dans le MDM dès leur validation\nRecherche d'une commande par numéro client",
            UtilisateursImpactes = "conseillers des agences et service Commandes (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "enregistrer vos commandes en cours et quitter le portail",
            HeureLimite = new TimeOnly(19, 30),
            DateAnnoncee = DateOnly.FromDateTime(maintenant.UtcDateTime.Date.AddDays(1)),
            HeureDebutAnnoncee = new TimeOnly(20, 0),
            HeureFinAnnoncee = new TimeOnly(23, 0),
        };

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
