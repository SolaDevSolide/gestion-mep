namespace GestionMep.Api.Cli;

/// <summary>
/// Liste des commandes : nom (« groupe verbe »), usage, aide détaillée et exécution. Elle sert à tout : exécution, aide
/// (<c>--help</c>), référence de la documentation (<c>aide --markdown</c>) et complétion des shells (<c>completion</c>).
/// </summary>
public static partial class LigneDeCommande
{
    private sealed record Commande(string Nom, string Usage, string Resume, Func<Appel, Task<int>> Executer)
    {
        public (string Option, string Description)[] Options { get; init; } = [];
        public string[] Exemples { get; init; } = [];
        public string? Details { get; init; }
        /// <summary>Autres noms acceptés (anciens noms, raccourcis), complets : « sauvegarder », « journal ».</summary>
        public string[] Alias { get; init; } = [];
        /// <summary>Contenu de la sortie --json, quand ce n'est pas l'enveloppe { code, messages, avertissements, resultat } seule.</summary>
        public string? Json { get; init; }
        /// <summary>Opération destructive : confirmation par --oui (ou question au terminal).</summary>
        public bool Destructive { get; init; }
        public string Groupe => Nom.Contains(' ') ? Nom[..Nom.IndexOf(' ')] : Nom;
        public string Verbe => Nom.Contains(' ') ? Nom[(Nom.IndexOf(' ') + 1)..] : "";
    }

    private sealed record GroupeCommandes(string Nom, string Titre, string? Note = null);

    private static readonly GroupeCommandes[] Groupes =
    [
        new("comptes", "Comptes", "Un compte se désigne par son login."),
        new("equipes", "Équipes", "Une équipe se désigne par son nom (guillemets s'il contient des espaces) ou son identifiant ; une personne peut appartenir à plusieurs équipes."),
        new("sso", "Connexion unique (Microsoft Entra ID ou OpenID Connect)"),
        new("reglages", "Réglages du serveur, modifiables sans redémarrage", "Ancien nom accepté : « reglage »."),
        new("criteres", "Critères go / no-go", "Un critère se désigne par son n° dans « criteres lister » ou par son identifiant."),
        new("charte", "Charte visuelle (couleurs de l'interface et des exports Excel)", "Ordre : charte de la console > fichier charte.json > charte par défaut."),
        GroupeConnexions,
        GroupeLiaisons,
        new("journal", "Journal d'administration"),
        new("systemes", "Catalogue des systèmes", "Un système se désigne par son nom (sans tenir compte des majuscules) ou son identifiant."),
        new("categories", "Catégories d'objets proposées par défaut à chaque système"),
        new("resolutions", "Résolutions types", "Une résolution se désigne par son identifiant (8 premiers caractères suffisent) ou son titre exact."),
        new("etapes-modeles", "Étapes modèles (bibliothèque)", "Une étape modèle se désigne par son identifiant (8 premiers caractères suffisent)."),
        new("modeles-recette", "Modèles de recette (bibliothèque)", "Un modèle se désigne par son identifiant (8 premiers caractères suffisent) ou son titre exact."),
        new("modeles-texte", "Modèles de texte des équipes (annonce, rappel, demande de MEP…)", "Un modèle se désigne par son identifiant (8 premiers caractères suffisent)."),
        new("meps", "MEP et modèles de MEP", "Une MEP se désigne par son identifiant (8 premiers caractères suffisent, colonne ID de « meps lister »)."),
        new("pieces-jointes", "Pièces jointes"),
        new("sauvegardes", "Sauvegardes et restauration"),
        new("config", "Configuration lue au démarrage"),
        new("demo", "Mode démonstration (DEMO=true uniquement)"),
        new("migrer", "Base de données"),
        new("mettre-a-jour", "Mise à jour de l'application"),
        new("sante", "Santé de l'application en marche"),
        new("version", "Version"),
        new("completion", "Complétion des commandes dans le shell"),
        new("aide", "Aide"),
    ];

    private static readonly (string, string) OptionOui = ("--oui", "Confirme l'opération sans question (obligatoire hors terminal)");

    private static Task<int> Fait(int code) => Task.FromResult(code);

    private static readonly Commande[] Registre =
    [
        // ---------- Comptes ----------
        new("comptes lister", "", "Liste les comptes : login, administrateur, état, nom", a => { a.Rien(0); return Lister(); })
        {
            Json = "tableau des comptes, comme GET /api/admin/utilisateurs",
            Exemples = ["gestion-mep comptes lister", "gestion-mep comptes lister --json"],
        },
        new("comptes afficher", "<login>", "Détail d'un compte : rôle, contact, équipes, comptes reliés, expertises", a => { a.Rien(1); return AfficherCompte(a.Arg(0, "<login>")); })
        {
            Json = "le compte, comme GET /api/admin/utilisateurs",
            Exemples = ["gestion-mep comptes afficher jdupont"],
        },
        new("comptes creer", "<login> [--nom \"Nom\"] [--admin] [--role \"Rôle\"] [--contact \"Contact\"]",
            "Crée un compte avec un mot de passe provisoire, à changer à la première connexion", a => Creer(a.Arg(0, "<login>"), a.Depuis(1)))
        {
            Options = [("--nom \"Nom\"", "Nom affiché (défaut : le login)"), ("--admin", "Rôle administrateur"), ("--role \"Rôle\"", "Rôle (DBA, développeur…)"),
                ("--contact \"Contact\"", "Téléphone, courriel…")],
            Details = "Le mot de passe provisoire n'est affiché qu'une fois (même avec --quiet) : le transmettre par un canal sûr.",
            Json = "resultat : { compte, motDePasseProvisoire }",
            Exemples = ["gestion-mep comptes creer jdupont --nom \"Jean Dupont\" --role DBA --contact \"poste 1234\" --admin"],
        },
        new("comptes modifier", "<login> [options]", "Modifie un compte : seules les options données changent", a => ModifierCompte(a.Arg(0, "<login>"), a.Depuis(1)))
        {
            Options = [("--nom \"Nom\"", "Nom affiché (ne peut pas imiter le nom ou le login d'un autre compte)"),
                ("--admin oui|non", "Rôle administrateur (le dernier administrateur actif le garde)"), ("--role \"Rôle\"", "Rôle"), ("--contact \"Contact\"", "Contact"),
                ("--ajouter-expertise <SYSTÈME>", "Ajoute (ou met à jour) une expertise sur un système du catalogue"),
                ("--detail \"texte\"", "Détail de l'expertise ajoutée juste avant"), ("--retirer-expertise <SYSTÈME>", "Retire une expertise")],
            Json = "resultat : le compte modifié",
            Exemples = ["gestion-mep comptes modifier jdupont --nom \"J. Dupont\" --admin non",
                "gestion-mep comptes modifier jdupont --ajouter-expertise BDD --detail \"Base clients\" --retirer-expertise API"],
        },
        new("comptes expertises", "<login>", "Expertises du compte", a => { a.Rien(1); return Expertises(a.Arg(0, "<login>")); })
        {
            Json = "tableau { systemeCatalogueId, systeme, detail }",
            Exemples = ["gestion-mep comptes expertises jdupont"],
        },
        new("comptes reinitialiser-mdp", "<login>", "Nouveau mot de passe provisoire (à changer à la connexion) ; ferme les sessions du compte",
            a => { a.Rien(1); return Reinitialiser(a.Arg(0, "<login>")); })
        {
            Details = "Administrateur perdu ou bloqué : comptes reinitialiser-mdp <login>, puis comptes reactiver <login> s'il était désactivé.",
            Json = "resultat : { login, motDePasseProvisoire }",
            Exemples = ["gestion-mep comptes reinitialiser-mdp admin", "gestion-mep comptes reinitialiser-mdp jdupont --json   (pour un script : resultat.motDePasseProvisoire)"],
        },
        new("comptes desactiver", "<login>", "Désactive le compte et ferme ses sessions (sans effet s'il l'est déjà)", a => { a.Rien(1); return ChangerEtat(a.Arg(0, "<login>"), actif: false); })
        {
            Exemples = ["gestion-mep comptes desactiver jdupont"],
        },
        new("comptes reactiver", "<login>", "Réactive le compte (sans effet s'il est déjà actif)", a => { a.Rien(1); return ChangerEtat(a.Arg(0, "<login>"), actif: true); })
        {
            Exemples = ["gestion-mep comptes reactiver jdupont"],
        },
        new("comptes relier", "<login> <login-principal> [--desactiver]",
            "Relie deux comptes d'une même personne : elle s'affiche sous le nom du compte principal, rien n'est fusionné ni supprimé",
            a => Relier(a.Arg(0, "<login>"), a.Arg(1, "<login-principal>"), a.Depuis(2)))
        {
            Options = [("--desactiver", "Désactive aussi le compte relié")],
            Exemples = ["gestion-mep comptes relier jean.dupont@entreprise.fr jdupont --desactiver"],
        },
        new("comptes delier", "<login>", "Défait la liaison : le compte reprend son propre nom", a => { a.Rien(1); return Delier(a.Arg(0, "<login>")); })
        {
            Exemples = ["gestion-mep comptes delier jean.dupont@entreprise.fr"],
        },

        // ---------- Équipes ----------
        new("equipes lister", "", "Équipes et nombre de membres", a => { a.Rien(0); return EquipesLister(); })
        {
            Json = "tableau des équipes, comme GET /api/equipes",
            Exemples = ["gestion-mep equipes lister"],
        },
        new("equipes afficher", "<équipe>", "Description, informations des modèles de texte et membres d'une équipe", a => { a.Rien(1); return EquipesAfficher(a.Arg(0, "<équipe>")); })
        {
            Json = "{ equipe, membres }",
            Exemples = ["gestion-mep equipes afficher \"Développement web\""],
        },
        new("equipes creer", "\"<nom>\" [--description \"…\"] [--contact-support \"…\"] [--signature \"…\"]", "Crée une équipe", a => EquipesCreer(a.Arg(0, "<nom>"), a.Depuis(1)))
        {
            Options = [("--description \"…\"", "Description"), ("--contact-support \"…\"", "Contact support repris par les modèles de texte"),
                ("--signature \"…\"", "Signature des modèles de texte (\\n pour un retour à la ligne)")],
            Json = "resultat : l'équipe",
            Exemples = ["gestion-mep equipes creer \"Exploitation\" --description \"Équipe d'astreinte\""],
        },
        new("equipes modifier", "<équipe> [--nom \"…\"] [--description \"…\"] [--contact-support \"…\"] [--signature \"…\"]",
            "Modifie une équipe : seules les options données changent", a => EquipesModifier(a.Arg(0, "<équipe>"), a.Depuis(1)))
        {
            Options = [("--nom \"…\"", "Nouveau nom"), ("--description \"…\"", "Description"),
                ("--contact-support \"…\"", "Contact support des modèles de texte (page Modèles de texte)"),
                ("--signature \"…\"", "Signature des modèles de texte (\\n pour un retour à la ligne)")],
            Json = "resultat : l'équipe",
            Exemples = ["gestion-mep equipes modifier Exploitation --nom \"Exploitation N2\"",
                "gestion-mep equipes modifier Exploitation --contact-support \"astreinte@entreprise.fr\" --signature \"L'équipe Exploitation\\nPoste 1234\""],
        },
        new("equipes ajouter", "<équipe> <login> [<login>…]", "Ajoute des comptes à l'équipe (déjà membres : sans effet)",
            a => EquipesMembres(a.Arg(0, "<équipe>"), [a.Arg(1, "<login>"), .. a.Depuis(2)], ajouter: true))
        {
            Exemples = ["gestion-mep equipes ajouter Exploitation jdupont mmartin"],
        },
        new("equipes retirer", "<équipe> <login> [<login>…]", "Retire des comptes de l'équipe (non membres : sans effet)",
            a => EquipesMembres(a.Arg(0, "<équipe>"), [a.Arg(1, "<login>"), .. a.Depuis(2)], ajouter: false))
        {
            Exemples = ["gestion-mep equipes retirer Exploitation mmartin"],
        },
        new("equipes supprimer", "<équipe>", "Supprime l'équipe (comptes et MEP inchangés)", a => { a.Rien(1); return EquipesSupprimer(a.Arg(0, "<équipe>")); })
        {
            Exemples = ["gestion-mep equipes supprimer \"Exploitation N2\""],
        },

        // ---------- Connexion unique ----------
        new("sso afficher", "", "Réglages de la connexion unique, ce qui manque, expiration du secret", a => { a.Rien(0); return SsoAfficher(); })
        {
            Json = "réglages, comme GET /api/admin/sso (jamais le secret)",
            Exemples = ["gestion-mep sso afficher"],
        },
        new("sso configurer", "[options]", "Modifie les réglages : seules les options données changent", a => SsoConfigurer(a.Args))
        {
            Options = [("--annuaire <id>", "Microsoft : ID de l'annuaire (locataire), GUID ou domaine"),
                ("--emetteur <url>", "Autre fournisseur OpenID Connect : adresse https de l'émetteur"), ("--client <id>", "ID d'application (client)"),
                ("--secret-stdin", "Lit le secret client sur l'entrée standard (ou le demande au terminal, sans l'afficher)"),
                ("--expiration <AAAA-MM-JJ|aucune>", "Date d'expiration du secret, pour être prévenu à temps"),
                ("--role-admin <valeur|aucun>", "Rôle d'application du fournisseur qui donne le rôle administrateur"),
                ("--creation-auto oui|non", "Crée le compte à la première connexion (défaut oui)"), ("--libelle \"texte\"", "Texte du bouton de la page de connexion")],
            Details = "Le secret ne se passe jamais en argument (il resterait dans l'historique du shell et la liste des processus) : --secret-stdin.",
            Exemples = ["gestion-mep sso configurer --annuaire entreprise.onmicrosoft.com --client 0f1e2d3c-… --expiration 2027-06-30",
                "gestion-mep sso configurer --secret-stdin < secret.txt"],
        },
        new("sso tester", "", "Vérifie que le fournisseur reconnaît l'application et le secret (code 1 si un contrôle échoue)", a => { a.Rien(0); return SsoTester(); })
        {
            Json = "resultat : tableau { libelle, ok, detail }",
            Exemples = ["gestion-mep sso tester"],
        },
        new("sso activer", "", "Affiche le bouton de connexion unique (sans redémarrage)", a => { a.Rien(0); return SsoChangerEtat(true); })
        {
            Exemples = ["gestion-mep sso activer"],
        },
        new("sso desactiver", "", "Retire le bouton de connexion unique ; les liaisons sont conservées", a => { a.Rien(0); return SsoChangerEtat(false); })
        {
            Exemples = ["gestion-mep sso desactiver"],
        },
        new("sso comptes", "", "Comptes reliés à un compte du fournisseur", a => { a.Rien(0); return SsoComptes(); })
        {
            Json = "tableau des liaisons",
            Exemples = ["gestion-mep sso comptes"],
        },
        new("sso delier", "<login>", "Supprime la liaison d'un compte avec le fournisseur", a => { a.Rien(1); return SsoDelier(a.Arg(0, "<login>")); })
        {
            Exemples = ["gestion-mep sso delier jdupont"],
        },

        // ---------- Réglages ----------
        new("reglages lister", "", "Réglages : valeur, origine (console, fichier, défaut), alertes", a => { a.Rien(0); return ReglagesLister(); })
        {
            Json = "{ reglages, approbationsMinimum }",
            Exemples = ["gestion-mep reglages lister"],
        },
        new("reglages definir", "<nom> <valeur>", "Modifie un réglage ; appliqué sous 10 secondes, sans redémarrage (remplace la valeur du fichier)",
            a => { a.Rien(2); return ReglagesDefinir(a.Arg(0, "<nom>"), a.Args.Length > 1 ? a.Args[1] : throw new ErreurUsage("<valeur> attendue")); })
        {
            Details = "Le nom est celui de la variable du fichier .env, en minuscules avec des tirets (URL_PUBLIQUE → url-publique) ; "
                + "approbations-minimum : approbations exigées avant le Go. Définir la valeur qu'il a déjà ne change rien.",
            Json = "resultat : le réglage",
            Exemples = ["gestion-mep reglages definir url-publique https://mep.entreprise.fr", "gestion-mep reglages definir lien-signalement \"\"      (valeur vide : lien masqué)",
                "gestion-mep reglages definir approbations-minimum 2"],
        },
        new("reglages retablir", "<nom>", "Revient à la valeur du fichier de configuration (ou à la valeur par défaut)", a => { a.Rien(1); return ReglagesRetablir(a.Arg(0, "<nom>")); })
        {
            Json = "resultat : le réglage",
            Exemples = ["gestion-mep reglages retablir Stockage:QuotaMepMo"],
        },

        // ---------- Critères go / no-go ----------
        new("criteres lister", "", "Critères go / no-go, numérotés (n°)", a => { a.Rien(0); return CriteresLister(); })
        {
            Json = "tableau des critères",
            Exemples = ["gestion-mep criteres lister"],
        },
        new("criteres ajouter", "\"<libellé>\" [--ordre n] [--actif oui|non]", "Ajoute un critère", a => CriteresAjouter(a.Arg(0, "<libellé>"), a.Depuis(1)))
        {
            Options = [("--ordre n", "Position dans la liste"), ("--actif oui|non", "Proposé dans les MEP (défaut oui)")],
            Json = "resultat : le critère",
            Exemples = ["gestion-mep criteres ajouter \"Astreinte prévenue\" --ordre 5"],
        },
        new("criteres modifier", "<n°|id> [--libelle \"…\"] [--ordre n] [--actif oui|non]", "Modifie un critère", a => CriteresModifier(a.Arg(0, "<n°|id>"), a.Depuis(1)))
        {
            Options = [("--libelle \"…\"", "Libellé"), ("--ordre n", "Position"), ("--actif oui|non", "Proposé dans les MEP")],
            Json = "resultat : le critère",
            Exemples = ["gestion-mep criteres modifier 3 --libelle \"Astreinte prévenue et joignable\" --actif non"],
        },
        new("criteres supprimer", "<n°|id>", "Supprime un critère (les MEP gardent leur copie)", a => { a.Rien(1); return CriteresSupprimer(a.Arg(0, "<n°|id>")); })
        {
            Exemples = ["gestion-mep criteres supprimer 3"],
        },

        // ---------- Charte visuelle ----------
        new("charte afficher", "", "Charte en vigueur, origine (console, fichier, défaut), thème sombre et lisibilité", a => { a.Rien(0); return CharteAfficher(); })
        {
            Json = "charte, comme GET /api/admin/charte",
            Exemples = ["gestion-mep charte afficher"],
        },
        new("charte exporter", "[fichier.json|-]", "Charte en vigueur au format du fichier charte.json (défaut : sortie standard)",
            a => { a.Rien(1); return CharteExporter(a.ArgFacultatif(0)); })
        {
            Exemples = ["docker compose exec -T app gestion-mep charte exporter > charte.json"],
        },
        new("charte importer", "<fichier.json|-> [--essai]", "Remplace la charte de la console (le fichier charte.json n'est plus lu)", a => CharteImporter(a.Arg(0, "<fichier.json|->"), a.Depuis(1)))
        {
            Options = [("--essai", "Contrôle le fichier sans rien enregistrer")],
            Exemples = ["docker compose exec -T app gestion-mep charte importer - --essai < charte.json"],
        },
        new("charte verifier", "[fichier.json|-]", "Valide un fichier, ou la charte en vigueur et le fichier charte.json : format et lisibilité (code 1 si invalide)",
            a => { a.Rien(1); return CharteVerifier(a.ArgFacultatif(0)); })
        {
            Exemples = ["gestion-mep charte verifier", "gestion-mep charte verifier /tmp/charte.json"],
        },
        new("charte definir", "<rôle> <RRGGBB>", "Change une couleur : primaire, secondaire, tertiaire (ou « primaire » pour la lier), succes, avertissement, erreur, info",
            a => { a.Rien(2); return CharteDefinir(a.Arg(0, "<rôle>"), a.Args.Length > 1 ? a.Args[1] : throw new ErreurUsage("<RRGGBB> attendu")); })
        {
            Details = "Dans un shell, « # » commence un commentaire : écrire la couleur sans « # » ou entre guillemets.",
            Exemples = ["gestion-mep charte definir primaire 0F766E", "gestion-mep charte definir tertiaire primaire"],
        },
        new("charte retablir", "", "Efface la charte de la console : retour au fichier charte.json, sinon à la charte par défaut", a => { a.Rien(0); return CharteRetablir(); })
        {
            Exemples = ["gestion-mep charte retablir"],
        },

        // ---------- Connexions aux API externes (LigneDeCommande.Connexions.cs) ----------
        .. CommandesConnexions,

        // ---------- Liaison avec Noosphere (LigneDeCommande.Liaisons.cs) ----------
        .. CommandesLiaisons,

        // ---------- Journal d'administration ----------
        new("journal lister", "[--depuis AAAA-MM-JJ] [--limite n] [--type <type>]", "Journal d'administration, du plus récent au plus ancien (200 événements par défaut)",
            a => Journal(a.Args))
        {
            Alias = ["journal"],
            Options = [("--depuis AAAA-MM-JJ", "Événements depuis ce jour (UTC)"), ("--limite n", "Nombre d'événements (200 par défaut)"),
                ("--type <type>", "Type d'événement (compte.cree…) ou famille (compte : tous les compte.…)")],
            Json = "tableau { id, quand, auteur, auteurLogin, type, detail }",
            Exemples = ["gestion-mep journal --depuis 2026-10-01 --limite 500", "gestion-mep journal --type sauvegarde --json"],
        },

        // ---------- Catalogue des systèmes ----------
        new("systemes lister", "", "Systèmes, environnements (* production) et nombre d'objets", a => { a.Rien(0); return SystemesLister(); })
        {
            Json = "tableau des systèmes",
            Exemples = ["gestion-mep systemes lister"],
        },
        new("systemes afficher", "<SYSTÈME>", "Fiche : environnements, catégories et objets", a => { a.Rien(1); return SystemesAfficher(a.Arg(0, "<SYSTÈME>")); })
        {
            Json = "{ systeme, objets }",
            Exemples = ["gestion-mep systemes afficher BDD"],
        },
        new("systemes historique", "<SYSTÈME> [--excel fichier.xlsx]", "MEP qui ont touché le système : jour, état, environnements, échecs d'étape",
            a => SystemesHistorique(a.Arg(0, "<SYSTÈME>"), a.Depuis(1)))
        {
            Options = [("--excel fichier.xlsx", "Écrit le classeur de l'onglet « Historique des MEP » de la fiche")],
            Json = "historique, comme GET /api/catalogue/systemes/{id}/historique",
            Exemples = ["gestion-mep systemes historique BDD --environnement Production"],
        },
        new("systemes ajouter", "<nom> [--description \"…\"] [--cible \"…\"] [--actif oui|non]", "Ajoute un système au catalogue", a => SystemesAjouter(a.Arg(0, "<nom>"), a.Depuis(1)))
        {
            Options = [("--description \"…\"", "Description"), ("--cible \"…\"", "Serveur ou adresse"), ("--actif oui|non", "Proposé dans les MEP (défaut oui)")],
            Json = "resultat : le système",
            Exemples = ["gestion-mep systemes ajouter CRM --description \"Relation client\" --cible crm01.entreprise.fr"],
        },
        new("systemes modifier", "<SYSTÈME> [--nom \"…\"] [--description \"…\"] [--cible \"…\"] [--actif oui|non]", "Modifie un système", a => SystemesModifier(a.Arg(0, "<SYSTÈME>"), a.Depuis(1)))
        {
            Options = [("--nom \"…\"", "Nouveau nom"), ("--description \"…\"", "Description"), ("--cible \"…\"", "Serveur ou adresse"), ("--actif oui|non", "Proposé dans les MEP")],
            Json = "resultat : le système",
            Exemples = ["gestion-mep systemes modifier crm --nom CRM2 --actif non"],
        },
        new("systemes supprimer", "<SYSTÈME>", "Supprime un système, ou le désactive s'il est utilisé (MEP, résolution type)", a => { a.Rien(1); return SystemesSupprimer(a.Arg(0, "<SYSTÈME>")); })
        {
            Exemples = ["gestion-mep systemes supprimer CRM2"],
        },
        new("systemes environnement", "<SYSTÈME> <nom> [--cible \"…\"] [--production oui|non] [--retirer]", "Ajoute, modifie ou retire un environnement de la fiche",
            a => SystemesEnvironnement(a.Arg(0, "<SYSTÈME>"), a.Arg(1, "<nom>"), a.Depuis(2)))
        {
            Options = [("--cible \"…\"", "Serveur ou adresse de l'environnement"), ("--production oui|non", "Environnement de production"), ("--retirer", "Retire l'environnement")],
            Exemples = ["gestion-mep systemes environnement BDD Préproduction --cible pg-preprod", "gestion-mep systemes environnement BDD Production --production oui"],
        },
        new("systemes categorie", "<SYSTÈME> \"<catégorie>\" [--renommer \"…\"] [--retirer]", "Ajoute, renomme ou retire (avec ses objets) une catégorie de la fiche",
            a => SystemesCategorie(a.Arg(0, "<SYSTÈME>"), a.Arg(1, "<catégorie>"), a.Depuis(2)))
        {
            Options = [("--renommer \"…\"", "Nouveau nom de la catégorie"), ("--retirer", "Retire la catégorie et ses objets")],
            Exemples = ["gestion-mep systemes categorie ERP Tables", "gestion-mep systemes categorie ERP Tables --renommer \"Tables SQL\""],
        },
        new("systemes objet", "<SYSTÈME> \"<catégorie>\" \"<nom>\" [--type \"…\"] [--libelle \"…\"] [--precision \"…\"] [--retirer]",
            "Ajoute, modifie ou retire un objet de la fiche", a => SystemesObjet(a.Arg(0, "<SYSTÈME>"), a.Arg(1, "<catégorie>"), a.Arg(2, "<nom>"), a.Depuis(3)))
        {
            Options = [("--type \"…\"", "Type (Table, Service…)"), ("--libelle \"…\"", "Libellé"), ("--precision \"…\"", "Précision"), ("--retirer", "Retire l'objet")],
            Exemples = ["gestion-mep systemes objet BDD Tables clients --type Table --libelle \"Fiches clients\""],
        },
        new("categories lister", "", "Catégories d'objets proposées par défaut à chaque système", a => { a.Rien(0); return CategoriesLister(); })
        {
            Json = "tableau de noms",
            Exemples = ["gestion-mep categories lister"],
        },
        new("categories ajouter", "\"<nom>\"", "Ajoute une catégorie par défaut", a => { a.Rien(1); return CategoriesModifier(a.Arg(0, "<nom>"), ajouter: true); })
        {
            Exemples = ["gestion-mep categories ajouter Licences"],
        },
        new("categories retirer", "\"<nom>\"", "Retire une catégorie par défaut (les systèmes qui l'utilisent la gardent)", a => { a.Rien(1); return CategoriesModifier(a.Arg(0, "<nom>"), ajouter: false); })
        {
            Exemples = ["gestion-mep categories retirer Licences"],
        },

        // ---------- Résolutions types ----------
        new("resolutions lister", "[--systeme <SYSTÈME>] [--inactives]", "Résolutions types", a => ResolutionsLister(a.Args))
        {
            Options = [("--systeme <SYSTÈME>", "Seulement celles de ce système"), ("--inactives", "Aussi les fiches désactivées")],
            Json = "tableau, comme GET /api/resolutions",
            Exemples = ["gestion-mep resolutions lister"],
        },
        new("resolutions afficher", "<id|titre>", "Fiche complète : symptômes, diagnostic, corrections, vérification", a => { a.Rien(1); return ResolutionsAfficher(a.Arg(0, "<id|titre>")); })
        {
            Json = "la résolution, comme GET /api/resolutions/{id}",
            Exemples = ["gestion-mep resolutions afficher 5c2e81d0", "gestion-mep resolutions afficher \"LDAP : compte de service verrouillé\""],
        },
        new("resolutions supprimer", "<id|titre>", "Supprime, ou désactive si elle a déjà servi", a => { a.Rien(1); return ResolutionsSupprimer(a.Arg(0, "<id|titre>")); })
        {
            Exemples = ["gestion-mep resolutions supprimer 5c2e81d0"],
        },
        new("resolutions exporter", "[fichier.json|-]", "Résolutions actives en JSON, systèmes désignés par leur nom (défaut : sortie standard)",
            a => { a.Rien(1); return ResolutionsExporter(a.ArgFacultatif(0)); })
        {
            Exemples = ["docker compose exec -T app gestion-mep resolutions exporter > resolutions.json"],
        },
        new("resolutions importer", "<fichier.json|-> [--remplacer|--ignorer-existantes] [--essai]", "Crée les résolutions d'un export (code 1 si une fiche est refusée)",
            a => ResolutionsImporter(a.Arg(0, "<fichier.json|->"), a.Depuis(1)))
        {
            Options = [("--remplacer", "Une fiche du même titre est remplacée (nouvelle version, historique gardé)"),
                ("--ignorer-existantes", "Une fiche du même titre est ignorée (par défaut : refusée)"), ("--essai", "Contrôle sans rien enregistrer")],
            Json = "tableau { titre, resultat (creee, remplacee, ignoree, refusee), motif }",
            Exemples = ["docker compose exec -T app gestion-mep resolutions importer - --essai < resolutions.json"],
        },

        // ---------- Bibliothèques ----------
        new("etapes-modeles lister", "[--systeme <SYSTÈME>]", "Étapes modèles, les plus utilisées d'abord", a => EtapesModelesLister(a.Args))
        {
            Options = [("--systeme <SYSTÈME>", "Seulement celles qui touchent ce système")],
            Json = "tableau, comme GET /api/etapes-modeles",
            Exemples = ["gestion-mep etapes-modeles lister --systeme BDD"],
        },
        new("etapes-modeles afficher", "<id>", "Détail d'une étape modèle", a => { a.Rien(1); return EtapesModelesAfficher(a.Arg(0, "<id>")); })
        {
            Json = "l'étape modèle, comme GET /api/etapes-modeles/{id}",
            Exemples = ["gestion-mep etapes-modeles afficher c324d884"],
        },
        new("etapes-modeles supprimer", "<id>", "Supprime une étape modèle (les étapes déjà insérées dans des MEP ne changent pas)",
            a => { a.Rien(1); return EtapesModelesSupprimer(a.Arg(0, "<id>")); })
        {
            Exemples = ["gestion-mep etapes-modeles supprimer c324d884"],
        },
        new("modeles-recette lister", "[--systeme <SYSTÈME>]", "Modèles de recette, les plus utilisés d'abord", a => ModelesRecetteLister(a.Args))
        {
            Options = [("--systeme <SYSTÈME>", "Seulement ceux de ce système")],
            Json = "tableau, comme GET /api/modeles-recette",
            Exemples = ["gestion-mep modeles-recette lister --systeme API"],
        },
        new("modeles-recette afficher", "<id|titre>", "Détail d'un modèle de recette : étapes de test", a => { a.Rien(1); return ModelesRecetteAfficher(a.Arg(0, "<id|titre>")); })
        {
            Json = "le modèle, comme GET /api/modeles-recette/{id}",
            Exemples = ["gestion-mep modeles-recette afficher \"Santé de l'API\""],
        },
        new("modeles-recette supprimer", "<id|titre>", "Supprime un modèle de recette (les fiches déjà insérées ne changent pas)",
            a => { a.Rien(1); return ModelesRecetteSupprimer(a.Arg(0, "<id|titre>")); })
        {
            Exemples = ["gestion-mep modeles-recette supprimer 55bd291b"],
        },
        new("modeles-texte lister", "[--equipe <équipe>]", "Modèles de texte des équipes", a => ModelesTexteLister(a.Args))
        {
            Options = [("--equipe <équipe>", "Seulement ceux de cette équipe")],
            Json = "tableau { id, equipe, nom, usage, fourni, version, modifieLe }",
            Exemples = ["gestion-mep modeles-texte lister --equipe \"Développement web\""],
        },
        new("modeles-texte afficher", "<id>", "Objet et corps d'un modèle de texte", a => { a.Rien(1); return ModelesTexteAfficher(a.Arg(0, "<id>")); })
        {
            Json = "le modèle { id, equipe, nom, usage, objet, corps, fourni, version, modifieLe }",
            Exemples = ["gestion-mep modeles-texte afficher b02d490e"],
        },
        new("modeles-texte supprimer", "<id>", "Supprime un modèle de texte (un modèle fourni ne se supprime pas : le rétablir)",
            a => { a.Rien(1); return ModelesTexteSupprimer(a.Arg(0, "<id>")); })
        {
            Exemples = ["gestion-mep modeles-texte supprimer 7e1f0c42"],
        },
        new("modeles-texte retablir", "<id>", "Rétablit le texte d'origine d'un modèle fourni", a => { a.Rien(1); return ModelesTexteRetablir(a.Arg(0, "<id>")); })
        {
            Exemples = ["gestion-mep modeles-texte retablir b02d490e"],
        },
        new("modeles-texte variables", "", "Variables utilisables dans les modèles ({{mep.outil}}…)", a => { a.Rien(0); return ModelesTexteVariables(); })
        {
            Json = "tableau { cle, libelle, groupe, description, liste }",
            Exemples = ["gestion-mep modeles-texte variables"],
        },

        // ---------- MEP ----------
        new("meps lister", "[filtres] [--modeles] [--excel fichier.xlsx]", "MEP (ou modèles), plus récentes d'abord, avec les filtres de la page d'accueil", a => MepsLister(a.Args))
        {
            Options = [("--recherche \"…\"", "Outil, version, objet, personnes, systèmes (sans accents ni casse)"),
                ("--systeme <SYSTÈME,…>", "Systèmes (nom du catalogue, ou nom saisi à la main dans les MEP)"), ("--environnement <env,…>", "Environnements, sur ces systèmes"),
                ("--echec", "Un échec d'étape survenu (sur les systèmes filtrés)"), ("--equipe <équipe,…>", "Une personne de l'équipe y participe"),
                ("--personne <login>", "Personne (login, nom ou identifiant ; comptes reliés compris)"),
                ("--role <rôle,…>", "Avec --personne : pilote, suppleant, responsable, executant, approbateur"),
                ("--etat <état,…>", "preparation, reportee, execution, recette, retour-arriere, reussie, incidents, repliee, echec, annulee"),
                ("--phase <phase>", "preparation, execution, recette, retour-arriere, cloturee, annulee"),
                ("--du AAAA-MM-JJ", "Jour du Go (à défaut, de création) à partir de"), ("--au AAAA-MM-JJ", "Jusqu'à ce jour inclus"),
                ("--type complete|simplifiee", "Type de MEP"), ("--modeles", "Les modèles au lieu des MEP"),
                ("--excel fichier.xlsx", "Écrit la liste filtrée et ses échecs en classeur Excel")],
            Json = "tableau, comme GET /api/meps",
            Exemples = ["gestion-mep meps lister --phase execution", "gestion-mep meps lister --systeme BDD --environnement Production --echec",
                "gestion-mep meps lister --personne alice --role pilote,executant --du 2026-01-01", "gestion-mep meps lister --etat incidents,repliee --excel /tmp/meps.xlsx"],
        },
        new("meps afficher", "<id>", "Résumé d'une MEP : identité, phase, personnes, systèmes, avancement", a => { a.Rien(1); return MepsAfficher(a.Arg(0, "<id>")); })
        {
            Json = "la MEP complète, comme GET /api/meps/{id}",
            Exemples = ["gestion-mep meps afficher 3f2a9c1e", "gestion-mep meps afficher 3f2a9c1e --json"],
        },
        new("meps journal", "<id> [--type <famille>] [--recherche \"…\"] [--limite n] [--apres <n° d'événement>]",
            "Journal d'une MEP, du plus récent au plus ancien", a => MepsJournal(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Options = [("--type <famille>", "Famille de l'onglet Journal (etape, phase, approbation…) ou type exact"),
                ("--recherche \"…\"", "Texte cherché dans le détail, l'auteur ou le login"), ("--limite n", $"Nombre d'événements (1 à {Services.FiltreJournal.LimiteMax})"),
                ("--apres <n°>", "Seulement les événements plus récents que ce n° (suivre une MEP depuis un script)")],
            Json = "tableau { id, quand, auteur, auteurLogin, type, detail, etapeId }",
            Exemples = ["gestion-mep meps journal 3f2a9c1e --limite 20", "gestion-mep meps journal 3f2a9c1e --apres 1520 --json"],
        },
        new("meps exporter", "<id> [fichier.json|-]", "MEP complète, suivi des tests unitaires et journal, en JSON (archive, audit, importation)",
            a => { a.Rien(2); return MepsExporter(a.Arg(0, "<id>"), a.ArgFacultatif(1)); })
        {
            Details = "Le fichier contient aussi le contenu réimportable de la MEP (« meps importer »). Les fichiers des pièces jointes restent dans les sauvegardes.",
            Exemples = ["docker compose exec -T app gestion-mep meps exporter 3f2a9c1e > mep-3f2a9c1e.json"],
        },
        new("meps excel", "<id> <vue> [fichier.xlsx]", "Une vue en classeur Excel : preparation, etapes, retour-arriere, recette, objets, journal, rapport",
            a => { a.Rien(3); return MepsExcel(a.Arg(0, "<id>"), a.Arg(1, "<vue>"), a.ArgFacultatif(2)); })
        {
            Details = "Sans fichier, le nom proposé par l'application, dans le dossier courant. « rapport » : rapport de fin d'une MEP clôturée.",
            Exemples = ["gestion-mep meps excel 3f2a9c1e etapes /tmp/etapes.xlsx"],
        },
        new("meps importer", "<fichier.json|-> --pour <login> [--modele] [--essai]",
            "Crée une MEP en préparation (ou un modèle) depuis un fichier de « meps exporter », comme une duplication",
            a => MepsImporter(a.Arg(0, "<fichier.json|->"), a.Depuis(1)))
        {
            Options = [("--pour <login>", "Compte qui reçoit la MEP : il en devient participant et responsable technique (créateur)"),
                ("--modele", "Crée un modèle au lieu d'une MEP"), ("--essai", "Contrôle le fichier et affiche les correspondances sans rien créer")],
            Details = "Comme une duplication : plan, étapes, sections, systèmes, personnes, recette et communication sont repris, avec de nouveaux "
                + "identifiants ; ni dates, ni résultats, ni décisions d'approbation, ni journal, ni pièces jointes. Venant d'un autre serveur, les comptes sont "
                + "retrouvés par leur login et les systèmes du catalogue par leur nom ; ce qui manque reste saisi à la main (personne sans compte, "
                + "système hors catalogue) et est signalé.",
            Json = "resultat : { id, outil, estModele }",
            Exemples = ["docker compose exec -T app gestion-mep meps importer - --pour jdupont --modele < modele-paie.json"],
        },
        new("meps dupliquer", "<id> --pour <login> [--modele]", "Crée une MEP en préparation depuis un modèle ou une autre MEP (ou un modèle depuis une MEP)",
            a => MepsDupliquer(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Options = [("--pour <login>", "Compte qui reçoit la copie : il en devient participant et responsable technique"),
                ("--modele", "Crée un modèle au lieu d'une MEP")],
            Details = "Même copie que le bouton Dupliquer : ni dates, ni résultats, ni décisions d'approbation ; une MEP créée depuis un modèle reprend son outil sans « Modèle : ».",
            Json = "resultat : { id, outil, estModele }",
            Exemples = ["gestion-mep meps dupliquer 7c41d2aa --pour jdupont      (MEP mensuelle depuis son modèle)"],
        },
        new("meps annuler", "<id> --motif \"…\" [--etat-production \"…\"] [--oui]", "Annule une MEP en préparation (administrateur) ; elle reste consultable",
            a => MepsAnnuler(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [("--motif \"…\"", "Motif de l'annulation (obligatoire)"), ("--etat-production \"…\"", "État laissé en production (obligatoire après un Go)"), OptionOui],
            Details = "Une MEP s'annule depuis la préparation : une MEP en cours revient d'abord en préparation dans l'application.",
            Exemples = ["gestion-mep meps annuler 3f2a9c1e --motif \"Doublon de la MEP 7c41d2aa\" --oui"],
        },
        new("meps supprimer", "<id> --motif \"…\" [--oui]", "Supprime définitivement une MEP jamais lancée, ou un modèle, avec ses pièces jointes",
            a => MepsSupprimer(a.Arg(0, "<id>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [("--motif \"…\"", "Motif, inscrit au journal d'administration (obligatoire)"), OptionOui],
            Details = "Une MEP qui a eu un Go ne se supprime pas : l'annuler pour garder sa trace.",
            Exemples = ["gestion-mep meps supprimer 3f2a9c1e --motif \"Créée par erreur\" --oui"],
        },
        new("meps piloter", "<id> <login> --motif \"…\" [--ancien-pilote-suppleant oui|non]",
            "Pilotage de secours après le Go : confie le pilotage à un compte (le pilote est injoignable)",
            a => MepsPiloter(a.Arg(0, "<id>"), a.Arg(1, "<login>"), a.Depuis(2)))
        {
            Options = [("--motif \"…\"", "Motif (obligatoire), inscrit au journal de la MEP et au journal d'administration"),
                ("--ancien-pilote-suppleant oui|non", "L'ancien pilote reste suppléant (défaut oui)")],
            Exemples = ["gestion-mep meps piloter 3f2a9c1e bmartin --motif \"Pilote malade, relève de l'astreinte\""],
        },

        // ---------- Pièces jointes ----------
        new("pieces-jointes utilisation", "[--limite n]", "Place occupée (fichiers et disque), compression, MEP les plus lourdes, plafonds et espace disque",
            a => PiecesUtilisation(a.Args))
        {
            Alias = ["pieces-jointes etat"],
            Options = [("--limite n", "Nombre de MEP détaillées (10 par défaut)")],
            Json = "{ dossier, total, totalStocke, nombre, compression, resolutions, meps, plafonds, disque }",
            Exemples = ["gestion-mep pieces-jointes utilisation --limite 20"],
        },
        new("pieces-jointes lister", "<id de MEP>", "Pièces jointes d'une MEP : nom, taille, date, auteur, étape", a => { a.Rien(1); return PiecesLister(a.Arg(0, "<id de MEP>")); })
        {
            Json = "tableau, comme GET /api/meps/{id}/pieces-jointes",
            Exemples = ["gestion-mep pieces-jointes lister 3f2a9c1e"],
        },
        new("pieces-jointes extraire", "<id> [fichier|-]", "Copie le fichier d'une pièce jointe (défaut : son nom, dans le dossier courant)",
            a => { a.Rien(2); return PiecesExtraire(a.Arg(0, "<id>"), a.ArgFacultatif(1)); })
        {
            Exemples = ["docker compose exec -T app gestion-mep pieces-jointes extraire 9b0c4f11 - > preuve.png"],
        },
        new("pieces-jointes verifier", "[--isoler-orphelins]", "Contrôle que chaque pièce jointe a son fichier, au contenu intact (empreinte SHA-256) ; code 1 sinon ; signale les fichiers orphelins",
            a => PiecesVerifier(a.Args))
        {
            Options = [("--isoler-orphelins", "Déplace les fichiers sans pièce jointe (plus d'une heure) dans le sous-dossier « orphelins », comme au démarrage")],
            Json = "resultat : { pieces, manquantes, abimees, orphelins, orphelinsIsoles }",
            Exemples = ["gestion-mep pieces-jointes verifier", "gestion-mep pieces-jointes verifier --isoler-orphelins"],
        },

        new("pieces-jointes compresser", "[--essai] [--tout]", "Compresse sans perte les pièces déposées avant la compression (reprend où elle s'est arrêtée)",
            a => PiecesJointesCompresser(a.Args, decompresser: false))
        {
            Options = [("--essai", "Gain attendu, sans rien écrire"), ("--tout", "Réexamine aussi les pièces laissées telles quelles (après une baisse du gain minimal)")],
            Details = "Application en marche ou non, pièce par pièce ; Ctrl+C s'arrête proprement entre deux pièces. Code 1 si des pièces n'ont pas pu être traitées.",
            Json = "{ essai, total, examinees, traitees, inchangees, absentes, echecs, avant, apres, interrompu }",
            Exemples = ["gestion-mep pieces-jointes compresser --essai", "gestion-mep pieces-jointes compresser"],
        },
        new("pieces-jointes decompresser", "[--essai]", "Remet les fichiers compressés dans leur forme d'origine", a => PiecesJointesCompresser(a.Args, decompresser: true))
        {
            Options = [("--essai", "Place nécessaire, sans rien écrire")],
            Details = "S'arrête avant de laisser moins que l'espace libre minimal. Pour garder les prochains dépôts tels quels : reglages definir pieces-compression non.",
            Json = "{ essai, total, examinees, traitees, inchangees, absentes, echecs, avant, apres, interrompu }",
            Exemples = ["gestion-mep pieces-jointes decompresser --essai"],
        },

        // ---------- Sauvegardes ----------
        new("sauvegardes creer", "[fichier.tar.gz]", "Archive la base et les pièces jointes (défaut : dossier des sauvegardes)", a => { a.Rien(1); return Sauvegarder(a.ArgFacultatif(0)); })
        {
            Alias = ["sauvegarder"],
            Json = "resultat : { fichier, taille, lignes, pieces }",
            Exemples = ["gestion-mep sauvegarder", "gestion-mep sauvegardes creer /data/sauvegardes/avant-intervention.tar.gz"],
        },
        new("sauvegardes lister", "", "Archives du dossier des sauvegardes", a => { a.Rien(0); return SauvegardesLister(); })
        {
            Json = "tableau { nom, chemin, taille, modifieeLe, lisible, application, migration, lignes, pieces }",
            Exemples = ["gestion-mep sauvegardes lister"],
        },
        new("sauvegardes verifier", "<archive>", "Contrôle une archive sans rien restaurer : contenu, empreintes des pièces jointes, version (code 1 si invalide)",
            a => { a.Rien(1); return SauvegardesVerifier(a.Arg(0, "<archive>")); })
        {
            Json = "resultat : { archive, application, migration, lignes, pieces, problemes }",
            Exemples = ["gestion-mep sauvegardes verifier sauvegarde-20261029-020000.tar.gz"],
        },
        new("sauvegardes purger", "--garder n [--essai] [--oui]", "Supprime les archives les plus anciennes du dossier des sauvegardes, en gardant les n plus récentes",
            a => SauvegardesPurger(a.Args))
        {
            Destructive = true,
            Options = [("--garder n", "Nombre d'archives à garder (1 au moins)"), ("--essai", "Affiche ce qui serait supprimé, sans rien supprimer"), OptionOui],
            Json = "resultat : { supprimees, gardees }",
            Exemples = ["gestion-mep sauvegardes purger --garder 14 --oui      (après la sauvegarde de la nuit)"],
        },
        new("sauvegardes restaurer", "<archive> [--oui]", "Remplace base et pièces jointes par l'archive (application arrêtée)",
            a => SauvegardesRestaurer(a.Args))
        {
            Destructive = true,
            Options = [OptionOui],
            Details = "L'archive se donne par son nom dans le dossier des sauvegardes ou par son chemin. Les données actuelles sont d'abord "
                + "sauvegardées (avant-import-…). Refusée si l'application tourne encore (autre connexion à la base).",
            Exemples = ["sudo systemctl stop gestion-mep && sudo gestion-mep sauvegardes restaurer sauvegarde-20261029-093511.tar.gz --oui"],
        },
        new("sauvegardes importer", "<fichier.tar.gz>", "Comme « sauvegardes restaurer », sans confirmation ni contrôle d'arrêt (déménagement scripté)",
            a => { a.Rien(1); return Importer(a.Arg(0, "<fichier.tar.gz>")); })
        {
            Alias = ["importer"],
            Exemples = ["docker compose run --rm -v \"$PWD/sauvegardes:/import:ro\" app importer /import/sauvegarde-AAAAMMJJ-HHMMSS.tar.gz"],
        },

        // ---------- Configuration, base, version ----------
        new("config afficher", "", "Configuration lue au démarrage (secrets masqués)", a => { a.Rien(0); return ConfigAfficher(); })
        {
            Json = "{ demarrage, reglages, connexionUnique, erreurBase }",
            Exemples = ["gestion-mep config afficher"],
        },
        new("config verifier", "", "Vérifie base, migrations, dossiers, comptes administrateurs, mots de passe d'exemple (code 1 si problème)",
            a => { a.Rien(0); return VerifierConfig(); })
        {
            Json = "resultat : tableau { niveau (ok, attention, erreur), message }",
            Exemples = ["gestion-mep config verifier"],
        },
        new("migrer", "", "Applique les migrations de la base sans démarrer l'application (sauvegarde préalable, contrôles)", a => { a.Rien(0); return Migrer(); })
        {
            Exemples = ["gestion-mep migrer"],
        },
        new("mettre-a-jour", "[--oui] [--version x.y.z]", "Dernière version publiée ; avec --oui, demande son installation au service de mise à jour",
            a => MettreAJour(a.Args))
        {
            Options = [("--oui", "Demande l'installation (refusée pendant une MEP en cours)"), ("--version x.y.z", "Autre version plus récente (registre interne)")],
            Json = "état, comme GET /api/admin/mises-a-jour",
            Exemples = ["gestion-mep mettre-a-jour"],
        },
        new("sante", "[--prete] [--url <adresse>]", "Interroge l'application en marche : vivante, ou prête à servir (base, schéma, échéances)", a => Sante(a.Args))
        {
            Options = [("--prete", "Disponibilité complète (/api/sante/prete), comme le HEALTHCHECK de l'image"),
                ("--url <adresse>", "Adresse de l'application (défaut : adresse locale d'écoute, ou variable SANTE_URL)")],
            Details = "Code 0 si l'application répond (et est prête avec --prete), 1 sinon.",
            Json = "{ url, statut, http, detail }",
            Exemples = ["gestion-mep sante --prete", "gestion-mep sante --prete --url https://mep.entreprise.fr --json"],
        },
        new("version", "", "Version de l'application", a => { a.Rien(0); return Fait(Version()); })
        {
            Alias = ["--version"],
            Json = "{ version, complete, installation }",
            Exemples = ["gestion-mep version", "gestion-mep --version --json"],
        },
        new("completion", "<bash|zsh|powershell>", "Script de complétion des commandes pour le shell",
            a => { a.Rien(1); return Fait(Completion(a.Arg(0, "<bash|zsh|powershell>"))); })
        {
            Exemples = ["sudo gestion-mep completion bash > /etc/bash_completion.d/gestion-mep",
                "gestion-mep completion zsh > \"${fpath[1]}/_gestion-mep\"",
                "gestion-mep completion powershell | Out-String | Invoke-Expression      (dans $PROFILE)"],
        },
        new("aide", "[groupe] [action] [--markdown]", "Liste des commandes, ou aide d'un groupe ou d'une commande", a => Fait(AfficherAide(a.Args)))
        {
            Options = [("--markdown", "Référence complète au format Markdown (documentation)")],
            Exemples = ["gestion-mep aide comptes", "gestion-mep comptes creer --help"],
        },

        // ---------- Démonstration ----------
        new("demo reinitialiser", "[--oui]", "Remet la base dans l'état initial de la démonstration ; refusée hors démonstration", a => DemoReinitialiser(a.Args))
        {
            Destructive = true,
            Options = [OptionOui],
            Details = "Comptes, MEP, catalogue, résolutions, critères, paramètres, réglages et journaux sont recréés ; les pièces jointes sont "
                + "supprimées ; les sessions ouvertes sont fermées.",
            Json = "{ meps, fichiersSupprimes }",
            Exemples = ["docker compose exec app gestion-mep demo reinitialiser --oui"],
        },
    ];

    /// <summary>Premiers mots qui désignent une commande (sinon l'application web démarre).</summary>
    private static readonly HashSet<string> NomsDeTete =
    [
        .. Groupes.Select(g => g.Nom), .. Registre.SelectMany(c => c.Alias).Select(a => a.Split(' ')[0]), "reglage",
    ];
}
