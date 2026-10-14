namespace GestionMep.Domain;

// Données de démonstration : comptes et MEP supplémentaires, une par état du cycle de vie. Chaque MEP est construite en
// préparation puis amenée dans son état en jouant les règles du domaine (approbation, Go, étapes, résolutions, recette,
// retour arrière, clôture) avec une horloge simulée : états, révisions, exécutants, horodatages et journal sont cohérents.
public static partial class DonneesDemo
{
    /// <summary>Compte de démonstration (mot de passe <see cref="MotDePasse"/>), proposé par la connexion rapide.</summary>
    /// <param name="Description">Rôle dans les MEP de démonstration, affiché sur l'écran de connexion.</param>
    public record CompteDemo(string Login, string Nom, bool EstAdmin, string Description);

    /// <summary>Mot de passe des comptes de démonstration.</summary>
    public const string MotDePasse = "demo";

    public static readonly IReadOnlyList<CompteDemo> Comptes =
    [
        new("alice", "Alice", true, "Administratrice : pilote de la plupart des MEP, peut réinitialiser la démonstration."),
        new("bob", "Bob", false, "Suppléant du pilote ; pilote de « Annuaire des agences » et « Extranet partenaires »."),
        new("claire", "Claire", false, "Approbatrice des MEP, responsable technique WEB."),
    ];

    /// <summary>Login d'un compte de démonstration (déjà normalisé : minuscules, sans espaces).</summary>
    public static bool EstCompteDemo(string login) => Comptes.Any(c => c.Login == login);

    // Outils des MEP de démonstration supplémentaires (un état du cycle de vie chacune).
    public const string OutilPortail = "Portail de démonstration";
    public const string OutilIncomplete = "Facturation électronique";
    public const string OutilPrete = "Annuaire des agences";
    public const string OutilExecution = "Paiement mobile";
    public const string OutilRecette = "Portail RH";
    public const string OutilRetourArriere = "Messagerie interne";
    public const string OutilReussie = "Gestion des stocks";
    public const string OutilRepliee = "Extranet partenaires";
    public const string OutilSimplifiee = "Formulaire de contact";

    /// <summary>MEP de démonstration et son journal (horodatages relatifs au moment de l'amorçage).</summary>
    public record MepDemo(Mep Mep, List<EvenementJournal> Journal);

    private const string Sim = "SIMULATION : lire sans rien lancer.\n\n";

    /// <summary>
    /// MEP de démonstration supplémentaires : préparation incomplète, prête pour le Go, exécution en cours, recette en cours,
    /// retour arrière en cours, clôturée réussie, clôturée repliée, et une MEP simplifiée en cours d'exécution. Le catalogue, les résolutions types et les critères sont
    /// ceux de <see cref="Creer"/> (mêmes identifiants) ; les comptes sont ceux de <see cref="Comptes"/>.
    /// </summary>
    public static List<MepDemo> Scenarios(Guid alice, Guid bob, Guid claire, IReadOnlyList<SystemeCatalogue> catalogue,
        IReadOnlyList<ResolutionType> resolutions, IReadOnlyList<CritereGoNoGo> criteres, DateTimeOffset maintenant)
    {
        var equipe = new Equipe(new Compte(alice, "Alice"), new Compte(bob, "Bob"), new Compte(claire, "Claire"));
        var c = new Contexte(equipe, catalogue, resolutions, criteres, maintenant);
        return [Incomplete(c), Prete(c), EnExecution(c), EnRecette(c), EnRetourArriere(c), Reussie(c), Repliee(c), Simplifiee(c)];
    }

    private sealed record Compte(Guid Id, string Nom);

    private sealed record Equipe(Compte Alice, Compte Bob, Compte Claire);

    private sealed record Contexte(Equipe Equipe, IReadOnlyList<SystemeCatalogue> Catalogue, IReadOnlyList<ResolutionType> Resolutions,
        IReadOnlyList<CritereGoNoGo> Criteres, DateTimeOffset Maintenant)
    {
        public SystemeCatalogue Sys(string nom) => Catalogue.Single(s => s.Nom == nom);
        public ResolutionType Res(int i) => Resolutions[i];

        /// <summary>Jour J + <paramref name="jours"/> à 22:00 UTC (échéance de retour arrière).</summary>
        public DateTimeOffset Soir(int jours) => new(Maintenant.UtcDateTime.Date.AddDays(jours).AddHours(22), TimeSpan.Zero);

        /// <summary>Jour J + <paramref name="jours"/> à 19:00 UTC (Go d'une MEP passée).</summary>
        public DateTimeOffset Debut(int jours) => new(Maintenant.UtcDateTime.Date.AddDays(jours).AddHours(19), TimeSpan.Zero);
    }

    /// <summary>
    /// Déroulé simulé d'une MEP : chaque action applique la règle du domaine à l'instant <see cref="T"/> et écrit dans le
    /// journal ce qu'écrirait l'API (mêmes types et libellés).
    /// </summary>
    private sealed class Deroule(Mep mep, DateTimeOffset debut)
    {
        public Mep Mep { get; } = mep;
        public DateTimeOffset T { get; private set; } = debut;
        public List<EvenementJournal> Journal { get; } = [];

        public Deroule A(DateTimeOffset t)
        {
            if (t < T) throw new InvalidOperationException($"Démonstration {Mep.Outil} : horloge ramenée en arrière.");
            T = t;
            return this;
        }

        public Deroule Plus(int minutes) => A(T.AddMinutes(minutes));

        private void Ev(Compte? qui, string type, string detail, bool memeVersion = false)
        {
            if (!memeVersion) Mep.Version++;
            Journal.Add(new EvenementJournal
            {
                MepId = Mep.Id, Quand = T, UtilisateurId = qui?.Id, Auteur = qui?.Nom ?? "Système", Type = type, Detail = detail,
            });
        }

        public void Creer(Compte qui)
        {
            Mep.CreeLe = T;
            Mep.CreeParUtilisateurId = qui.Id;
            Ev(qui, "mep.creee", Mep.Type == TypeMep.Simplifiee ? $"MEP simplifiée créée : {Mep.Outil}" : $"MEP créée : {Mep.Outil}");
        }

        public void Note(Compte qui, string type, string detail) => Ev(qui, type, detail);

        public void Approbateurs(Compte qui, params Compte[] approbateurs)
        {
            Regles.DefinirApprobateurs(Mep, approbateurs.Select(a => (a.Id, a.Nom)).ToList());
            Ev(qui, "approbateurs.modifies", $"Approbateurs : {string.Join(", ", approbateurs.Select(a => a.Nom))}");
        }

        public void Relire(Compte qui)
        {
            Regles.ExigerPreparation(Mep);
            if (Mep.Participants.All(p => p.UtilisateurId != qui.Id)) throw new InvalidOperationException("Relecture par un non-participant.");
            Mep.ProcedureRelueParId = qui.Id;
            Mep.ProcedureRelueLe = T;
            Ev(qui, "procedure.relue", "Procédure relue");
        }

        public void Approuver(Compte qui, string commentaire)
        {
            Regles.Decider(Mep, qui.Id, DecisionApprobation.Approuvee, commentaire, T);
            Ev(qui, "approbation.approuvee", $"MEP approuvée par {qui.Nom}{(commentaire.Length == 0 ? "" : $" : {commentaire}")}");
        }

        public void Go(Compte qui, IReadOnlyList<CritereGoNoGo> criteres)
        {
            Regles.PasserEnExecution(Mep, qui.Id, criteres.Select(x => x.Id).ToList(), criteres, ApprobationsMinimum, T);
            Ev(qui, "gonogo.go", $"Go ({string.Join(", ", Mep.GoNoGo.Select(g => g.Libelle))})");
            Ev(qui, "phase.execution", "Plan passé en exécution", memeVersion: true);
        }

        public void Demarrer(Compte qui, Etape e)
        {
            Regles.Demarrer(Mep, e, qui.Id, T);
            Ev(qui, "etape.demarree", $"{e.Code} démarrée par {Mep.Participants.Single(p => p.Id == e.ExecutantId).Nom}");
        }

        public void Reussir(Compte qui, Etape e, string preuve, string? commentaire = null)
        {
            Regles.Terminer(Mep, e, EtatEtape.Reussie, preuve, resultatVerifie: true, qui.Id, T, commentaire);
            Ev(qui, "etape.reussie", $"{e.Code} réussie : {preuve}");
        }

        public void Echouer(Compte qui, Etape e, string preuve)
        {
            Regles.Terminer(Mep, e, EtatEtape.Echec, preuve, resultatVerifie: false, qui.Id, T);
            Ev(qui, "etape.echec", $"{e.Code} en échec : {preuve}");
        }

        public void Commenter(Compte qui, Etape e, string texte)
        {
            Regles.Commenter(Mep, e, texte, qui.Id, estAdmin: false);
            Ev(qui, "etape.commentaire", $"Commentaire sur {e.Code} : {texte}");
        }

        public void AutoriserNonRetour(Compte qui, Etape e)
        {
            Regles.AutoriserNonRetour(Mep, e, qui.Id, T);
            Ev(qui, "etape.non-retour-autorise", $"Passage du point de non-retour {e.Code} autorisé");
        }

        public ExecutionResolution LancerResolution(Compte qui, Etape e, ResolutionType r)
        {
            var x = Regles.DemarrerResolution(Mep, e, r, qui.Id, T);
            Ev(qui, "resolution.demarree", $"Résolution « {x.Titre} » démarrée sur {e.Code}");
            return x;
        }

        public void Corriger(Compte qui, ExecutionResolution x, int ordre, ResultatControle resultat, string observation)
        {
            Regles.ValiderCorrection(Mep, x, ordre, resultat, observation, qui.Id, T);
            var suite = observation.Length == 0 ? "" : $" : {observation}";
            Ev(qui, "resolution.correction", resultat == ResultatControle.Conforme ? $"Correction {ordre} conforme{suite}" : $"Correction {ordre} en échec{suite}");
            if (x.Statut == StatutResolution.Echouee) EchecResolution(x);
        }

        public void Verifier(Compte qui, ExecutionResolution x, ResultatControle resultat, string observation)
        {
            Regles.ValiderVerification(Mep, x, resultat, observation, qui.Id, T);
            var e = Mep.Etapes.Single(s => s.Id == x.EtapeId);
            if (x.Statut == StatutResolution.Reussie)
                Ev(qui, "resolution.reussie", $"Résolution « {x.Titre} » réussie sur {e.Code} : {e.Code} peut être redémarrée", memeVersion: true);
            else EchecResolution(x);
        }

        private void EchecResolution(ExecutionResolution x)
        {
            var e = Mep.Etapes.Single(s => s.Id == x.EtapeId);
            Ev(null, "resolution.echouee", $"Résolution « {x.Titre} » en échec sur {e.Code}", memeVersion: true);
            if (Mep.Phase == Phase.RetourArriere)
                Ev(null, "repli.declenche", $"Retour arrière déclenché automatiquement : {Mep.RetourArriereMotif}", memeVersion: true);
        }

        public void Recette(Compte qui)
        {
            Regles.PasserEnRecette(Mep, qui.Id, T);
            Ev(qui, "phase.recette", "MEP passée en recette");
        }

        public void Tester(Compte qui, string fiche, int ordre, ResultatControle resultat, string observation)
        {
            var f = Mep.FichesRecette.Single(x => x.Titre == fiche);
            var t = f.Etapes.Single(x => x.Ordre == ordre);
            Regles.ValiderEtapeTest(Mep, f, t, resultat, observation, qui.Id, estAdmin: false, T);
            Ev(qui, "recette.test", $"{Regles.NomRecette(f)} étape {t.Ordre} {(resultat == ResultatControle.Conforme ? "conforme" : "en échec")} : {t.Observation}");
        }

        public void DeclencherRepli(Compte qui, string motif)
        {
            Regles.DeclencherRetourArriere(Mep, qui.Id, motif, T);
            Ev(qui, "repli.declenche", $"Retour arrière déclenché par {qui.Nom} : {motif}");
        }

        public void Cloturer(Compte qui, string ceQuiAMarche, string incidents, string actions)
        {
            Regles.Cloturer(Mep, qui.Id, T, new Bilan { CeQuiAMarche = ceQuiAMarche, Incidents = incidents, Actions = actions });
            Ev(qui, "mep.cloturee", $"MEP clôturée : {(Mep.Issue == IssueMep.Reussie ? "réussie" : "repliée")}");
        }

        public MepDemo Fin() => new(Mep, Journal);
    }

    // ---------- Construction commune ----------

    // Créneau annoncé aux utilisateurs (section Communication) : jour et heure à Paris, fuseau des MEP de démonstration.
    private static readonly TimeZoneInfo Paris = TrouverParis();

    private static TimeZoneInfo TrouverParis()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    private static DateOnly Jour(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, Paris).DateTime);

    /// <summary>Heure à la demi-heure inférieure, comme on l'annonce.</summary>
    private static TimeOnly Heure(DateTimeOffset t)
    {
        var h = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, Paris).DateTime);
        return new TimeOnly(h.Hour, h.Minute < 30 ? 0 : 30);
    }

    /// <summary>
    /// Nouvelle MEP en production avec les trois comptes comme participants (profils de démonstration), le pilote et ses
    /// suppléants. Claire est l'approbatrice, sauf si elle pilote.
    /// </summary>
    private static (Mep Mep, Participant Alice, Participant Bob, Participant Claire) NouvelleMep(Contexte c, string outil, string actuelle,
        string cible, string objet, string impacts, int? indisponibilite, Func<Equipe, Compte> pilote, params Func<Equipe, Compte>[] suppleants)
    {
        var eq = c.Equipe;
        var mep = new Mep
        {
            Outil = outil, VersionActuelle = actuelle, VersionCible = cible, Objet = objet, Impacts = impacts, FuseauHoraire = "Europe/Paris",
            BilanActive = true, IndisponibiliteMinutes = indisponibilite,
        };
        var pAlice = Participant(mep, "Alice", "Pilote / DBA", "Fictif — poste A, 01 00 00 00 01", eq.Alice.Id);
        var pBob = Participant(mep, "Bob", "Développeur API", "Fictif — poste B, 01 00 00 00 02", eq.Bob.Id);
        var pClaire = Participant(mep, "Claire", "Développeuse front", "Fictif — poste C, 01 00 00 00 03", eq.Claire.Id);
        Participant P(Compte compte) => mep.Participants.Single(p => p.UtilisateurId == compte.Id);
        mep.PiloteId = P(pilote(eq)).Id;
        mep.SuppleantIds = suppleants.Select(s => P(s(eq)).Id).ToList();
        Regles.VerifierSuppleants(mep, mep.PiloteId, mep.SuppleantIds);
        return (mep, pAlice, pBob, pClaire);
    }

    private static Systeme Sys(Mep mep, SystemeCatalogue c, string impact, StatutSauvegarde statut, ModeSauvegarde moment, string contenu = "",
        string justification = "")
    {
        var s = Systeme(mep, c, impact);
        var requise = statut == StatutSauvegarde.Requise;
        Regles.DefinirSauvegardeSysteme(mep, s, statut, moment, contenu, justification, requise ? EmplacementDemo(s.Nom) : "", requise ? RecuperationDemo : "");
        return s;
    }

    private static void Responsables(Mep mep, params (Participant Qui, Systeme[] Systemes, string Perimetre, DisponibiliteJourJ Dispo)[] responsables) =>
        Regles.DefinirResponsablesTechniques(mep, responsables.Select(r => new ResponsableTechnique
        {
            ParticipantId = r.Qui.Id, SystemeIds = r.Systemes.Select(s => s.Id).ToList(), Perimetre = r.Perimetre, Disponibilite = r.Dispo,
            ContactJourJ = r.Dispo == DisponibiliteJourJ.Joignable ? $"Fictif — poste {r.Qui.Nom[0]}" : "",
        }).ToList());

    /// <summary>Fiche de recette ajoutée et remplie par les règles de préparation.</summary>
    private static void Fiche(Mep mep, Systeme systeme, string titre, params (string Instructions, string Attendu)[] etapes)
    {
        var f = Regles.AjouterFicheRecette(mep, systeme.Id, titre);
        Regles.ModifierFicheRecette(mep, f, systeme.Id, titre, etapes.Select(e => ((Guid?)null, e.Instructions, e.Attendu)));
    }

    /// <summary>Étape à faire n° <paramref name="n"/>, en fin de la ligne <paramref name="ligne"/> du plan.</summary>
    private static Etape Pas(Mep mep, TypePlan plan, int n, int ligne, string action, Systeme[] systemes, int duree, Criticite criticite,
        string instructions, string attendu, params Etape[] prealables)
    {
        var e = new Etape
        {
            MepId = mep.Id, Plan = plan, Code = $"{(plan == TypePlan.Deploiement ? 'E' : 'R')}{n:0000}", Ligne = ligne,
            Ordre = mep.Etapes.Count(x => x.Plan == plan && x.Ligne == ligne), Action = action, SystemeIds = systemes.Select(s => s.Id).ToList(),
            Environnements = EnvironnementsParDefaut(systemes), DureeEstimeeMinutes = duree, Criticite = criticite, Instructions = instructions, ResultatAttendu = attendu,
            Dependances = prealables.Select(p => p.Id).ToList(),
        };
        mep.Etapes.Add(e);
        return e;
    }

    private static Etape E(Mep mep, int n, int ligne, string action, Systeme[] systemes, int duree, Criticite criticite, string instructions,
        string attendu, params Etape[] prealables) =>
        Pas(mep, TypePlan.Deploiement, n, ligne, action, systemes, duree, criticite, instructions, attendu, prealables);

    private static Etape R(Mep mep, int n, int ligne, string action, Systeme[] systemes, int duree, string instructions, string attendu,
        params Etape[] prealables) =>
        Pas(mep, TypePlan.RetourArriere, n, ligne, action, systemes, duree, Criticite.Bloquante, instructions, attendu, prealables);

    private static Etape Sauvegarde(Etape e)
    {
        Regles.DefinirType(e, TypeEtape.Sauvegarde, pointDeNonRetour: false);
        e.SauvegardeEmplacement = EmplacementDemo(e.Code);
        e.SauvegardeMethodeRecuperation = RecuperationDemo;
        return e;
    }

    private static Etape NonRetour(Etape e)
    {
        Regles.DefinirType(e, e.Type, pointDeNonRetour: true);
        return e;
    }

    private static PlanRepli Repli(int duree, string version, string donnees) => new()
    {
        QuandDeclencher = "Échec d'une étape sans résolution, résolution en échec, ou échéance atteinte.",
        DureeTesteeMinutes = duree, VersionARestaurer = version, DonneesDepuisSauvegarde = donnees,
        SiRepliEchoue = "Prévenir le pilote et l'astreinte fictive (poste 01 00 00 00 99).",
    };

    // ---------- 1. Préparation incomplète ----------

    /// <summary>
    /// Préparation avec des anomalies visibles : pas d'échéance, sauvegarde BATCH à renseigner, sauvegarde WEB non justifiée,
    /// étape sans instructions, action d'exemple, fiche de recette vide, quand déclencher le repli non précisé, procédure non
    /// relue, approbation en attente ; Claire absente le jour J (avertissement).
    /// </summary>
    private static MepDemo Incomplete(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilIncomplete, "3.4", "3.5",
            "Factures au format Factur-X et envoi nocturne vers la plateforme de dématérialisation.",
            "Téléchargement des factures indisponible pendant la migration.", 30, e => e.Alice);
        var bdd = Sys(mep, c.Sys("BDD"), "Tables factures : colonnes Factur-X", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep,
            "Sauvegarde des tables `factures` et `lignes_facture` (E0001).");
        var batch = Systeme(mep, c.Sys("BATCH"), "Nouvel export nocturne des factures");
        var web = Sys(mep, c.Sys("WEB"), "Téléchargement des factures PDF", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep);
        // Les tables factures sont d'abord migrées en préproduction, puis en production.
        EnvironnementsDe(bdd, c.Sys("BDD"), "Préproduction", "Production");
        Objets(mep, bdd, c.Sys("BDD"), "colonnes Factur-X", "factures", "lignes_facture");
        Objets(mep, bdd, c.Sys("BDD"), "v3.5.0", "v_factures_a_envoyer");
        Objets(mep, batch, c.Sys("BATCH"), "v3.5.0", "FACT01", "FACT03", "FACT04", "batch-facturation.jar");
        Objets(mep, batch, c.Sys("BATCH"), "format 2026", "FACTX_AAAAMMJJ.zip");
        Objets(mep, web, c.Sys("WEB"), "v3.5.0", "factures-pdf.tar.gz");
        Responsables(mep,
            (bob, [batch, bdd], "Export nocturne et tables factures", DisponibiliteJourJ.Joignable),
            (claire, [web], "Module de téléchargement PDF", DisponibiliteJourJ.Absent));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde des tables factures", [bdd], 15, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin -t factures -t lignes_facture compta > /sauvegardes/factures.sql"),
            "Fichier de sauvegarde présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration des tables factures", [bdd], 20, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d compta -f migrations/3.5.sql"), "Colonnes Factur-X présentes, aucune erreur.", e1);
        var e3 = E(mep, 3, 1, "Déploiement de l'export nocturne", [batch], 20, Criticite.Moderee, "", "Le traitement apparaît dans l'ordonnanceur.", e1);
        var e4 = E(mep, 4, 1, "Publication du module PDF", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh factures-pdf 3.5"), "Le bouton « Télécharger (Factur-X) » s'affiche.", e1);
        var e5 = E(mep, 5, 2, "Exemple : vérifier une facture", [bdd, batch, web], 10, Criticite.Faible,
            "Ouvrir une facture et vérifier son contenu.", "La facture est conforme.", e2, e3, e4);
        // Incohérence à repérer à la relecture : la base est vérifiée en préproduction, l'export et le portail en production.
        EnvironnementsEtape(e5, (bdd, ["Préproduction"]), (batch, ["Production"]), (web, ["Production"]));
        R(mep, 1, 0, "Restaurer les tables factures", [bdd], 20, Sim + Code("psql -h bdd-demo -U admin compta < /sauvegardes/factures.sql"),
            "Restauration sans erreur.");
        mep.PlanRepli = Repli(25, "Version 3.4 fictive.", "Tables factures restaurées depuis la sauvegarde E0001.");
        mep.PlanRepli.QuandDeclencher = "";

        Fiche(mep, web, "Téléchargement d'une facture",
            ("Ouvrir « Mes factures » et télécharger la dernière facture.", "Le PDF s'ouvre et contient le bloc Factur-X."));
        Fiche(mep, batch, "Export nocturne");

        // Communication commencée : bénéfices et utilisateurs saisis, référence et créneau annoncé encore à renseigner.
        mep.Communication = new Communication
        {
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Factures au format Factur-X, acceptées par toutes les plateformes\nEnvoi automatique chaque nuit, sans dépôt manuel",
            UtilisateursImpactes = "service Comptabilité et clients professionnels (fictif)",
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-2).AddHours(-3));
        d.Creer(c.Equipe.Alice);
        d.Plus(25).Note(c.Equipe.Alice, "etape.creee", "E0005 créée");
        d.Plus(5).Approbateurs(c.Equipe.Alice, c.Equipe.Claire);
        d.A(c.Maintenant.AddDays(-1).AddHours(-5)).Note(c.Equipe.Bob, "recette.fiche-ajoutee", "Recette « Export nocturne » ajoutée");
        d.Plus(10).Note(c.Equipe.Bob, "responsables-techniques.modifies", "Responsables techniques : Bob, Claire");
        return d.Fin();
    }

    // ---------- 2. Préparation prête pour le Go ----------

    /// <summary>Plan complet, relu et approuvé : Bob (pilote) ou Alice (suppléante) peut donner le Go.</summary>
    private static MepDemo Prete(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilPrete, "1.8", "2.0",
            "Groupes LDAP par agence et nouvelle recherche d'agence sur le site public.", "Recherche d'agence indisponible 10 minutes.", 10,
            e => e.Bob, e => e.Alice);
        mep.EcheanceRepli = c.Soir(2);
        var ldap = Sys(mep, c.Sys("LDAP"), "Nouveaux groupes par agence", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Export LDIF complet de l'annuaire la veille au soir (fichier `annuaire-veille.ldif`).");
        var api = Sys(mep, c.Sys("API"), "Service annuaire 2.0", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Archive de `/opt/annuaire` (E0001).");
        var web = Sys(mep, c.Sys("WEB"), "Page « Trouver une agence »", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Page statique régénérée à chaque publication : la version 1.8 reste dans le dépôt.");
        Objets(mep, ldap, c.Sys("LDAP"), "nouvelle branche", "ou=agences");
        Objets(mep, ldap, c.Sys("LDAP"), "un groupe par agence", "cn=agences-lecture");
        Objets(mep, api, c.Sys("API"), "2.0.0", "annuaire-api.jar", "/v2/agences");
        Objets(mep, web, c.Sys("WEB"), "2.0.0", "agences-web.tar.gz");
        Responsables(mep,
            (bob, [api, ldap], "Service annuaire et groupes LDAP", DisponibiliteJourJ.Joignable),
            (claire, [web], "Page « Trouver une agence »", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde du service annuaire", [api], 10, Criticite.Moderee,
            Sim + Code("tar czf /sauvegardes/annuaire-1.8.tgz /opt/annuaire"), "Archive créée, taille supérieure à 0."));
        var e2 = E(mep, 2, 1, "Création des groupes d'agence", [ldap], 15, Criticite.Elevee,
            Sim + Code("ldapadd -H ldap://annuaire-demo -D cn=admin -f groupes-agences.ldif"), "42 groupes `agence-*` créés.", e1);
        e2.ResolutionIds = [];
        var e3 = E(mep, 3, 1, "Déploiement du service annuaire 2.0", [api], 20, Criticite.Elevee,
            Sim + Code("cd /opt/annuaire && ./deploy.sh 2.0"), "`GET /sante` renvoie 200 avec la version 2.0.", e1);
        e3.ResolutionIds = [c.Res(0).Id];
        var e4 = E(mep, 4, 2, "Publication de la page agences", [web], 15, Criticite.Moderee,
            Sim + Code("npm ci && npm run build && ./publier.sh agences"), "La carte des agences s'affiche.", e2, e3);
        e4.ResolutionIds = [c.Res(2).Id];
        E(mep, 5, 3, "Contrôle final", [ldap, api, web], 10, Criticite.Elevee,
            "- [ ] Rechercher « Lyon » sur le site public\n- [ ] Vérifier qu'un conseiller voit son groupe d'agence", "Tous les contrôles sont verts.", e4);
        var r1 = R(mep, 1, 0, "Réinstaller le service annuaire 1.8", [api], 15, Sim + Code("cd /opt/annuaire && ./deploy.sh 1.8"),
            "`GET /sante` renvoie la version 1.8.");
        var r2 = R(mep, 2, 0, "Supprimer les groupes d'agence", [ldap], 10, Sim + Code("ldapdelete -H ldap://annuaire-demo -D cn=admin -f groupes-agences.dn"),
            "Aucun groupe `agence-*` restant.");
        R(mep, 3, 1, "Republier l'ancienne page", [web], 10, Sim + Code("./publier.sh agences --version 1.8"), "L'ancienne page s'affiche.", r1, r2);
        mep.PlanRepli = Repli(30, "Service annuaire 1.8 et page agences 1.8.", "Annuaire rechargé depuis `annuaire-veille.ldif` si besoin.");

        Fiche(mep, web, "Recherche d'agence", ("Rechercher « Lyon » sur le site public.", "Trois agences s'affichent sur la carte."));
        Fiche(mep, api, "API annuaire", (Code("curl -fsS https://annuaire.exemple.invalid/agences?ville=Lyon"), "Réponse 200 avec 3 agences."));
        Fiche(mep, ldap, "Groupes d'agence", ("Se connecter avec un compte de l'agence de Lyon.", "Le compte appartient au groupe `agence-lyon`."));

        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0398",
            NiveauRisque = NiveauRisque.Faible,
            Benefices = "Recherche d'agence par ville ou code postal\nCarte des agences sur le site public\nDroits des conseillers rattachés à leur agence",
            UtilisateursImpactes = "conseillers des agences et visiteurs du site public (fictif)",
            ActionUtilisateurs = "enregistrer les fiches d'agence en cours de modification",
            HeureLimite = new TimeOnly(19, 45),
            DateAnnoncee = Jour(c.Maintenant.AddDays(1)),
            HeureDebutAnnoncee = new TimeOnly(20, 0),
            HeureFinAnnoncee = new TimeOnly(21, 30),
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-3).AddHours(-2));
        d.Creer(c.Equipe.Bob);
        d.Plus(40).Approbateurs(c.Equipe.Bob, c.Equipe.Claire);
        d.A(c.Maintenant.AddDays(-1).AddHours(-4)).Relire(c.Equipe.Alice);
        d.A(c.Maintenant.AddHours(-3)).Approuver(c.Equipe.Claire, "Plan relu, rien à redire.");
        return d.Fin();
    }

    // ---------- 3. Exécution en cours ----------

    /// <summary>
    /// Exécution depuis 80 minutes : sauvegardes et index réussis, déploiement de l'API en cours (Bob), publication de l'écran
    /// en échec avec une résolution type disponible, point de non-retour à venir. Échéance dans trois jours.
    /// </summary>
    private static MepDemo EnExecution(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilExecution, "4.1", "4.2",
            "Paiement par mobile (portefeuilles électroniques) dans l'application et sur le site.",
            "Paiements suspendus pendant l'activation (15 minutes).", 15, e => e.Alice, e => e.Bob);
        mep.EcheanceRepli = c.Soir(3);
        var bdd = Sys(mep, c.Sys("BDD"), "Table des transactions : nouveaux index", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep,
            "Sauvegarde de la table `transactions` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service paiement 4.2", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Archive de `/opt/paiement` (E0002).");
        var web = Sys(mep, c.Sys("WEB"), "Écran de paiement", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Écran publié depuis le dépôt : la version 4.1 se republie en une commande.");
        var mdm = Sys(mep, c.Sys("MDM"), "Référentiel des moyens de paiement", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Le MDM historise les versions du référentiel.");
        Objets(mep, bdd, c.Sys("BDD"), "4.2.0", "transactions", "ix_transactions_date");
        Objets(mep, api, c.Sys("API"), "4.2.0", "paiement-api.jar", "/v1/paiements");
        Objets(mep, web, c.Sys("WEB"), "4.2.0", "paiement-web.tar.gz");
        Objets(mep, mdm, c.Sys("MDM"), "paiement mobile ajouté", "flux-moyens-paiement");
        Responsables(mep,
            (bob, [api, mdm], "Service paiement et flux MDM", DisponibiliteJourJ.Joignable),
            (claire, [web], "Écran de paiement", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la table des transactions", [bdd], 15, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin -t transactions paiement > /sauvegardes/transactions.sql"), "Fichier présent, sans erreur."));
        var e2 = Sauvegarde(E(mep, 2, 0, "Sauvegarde du service paiement", [api], 10, Criticite.Moderee,
            Sim + Code("tar czf /sauvegardes/paiement-4.1.tgz /opt/paiement"), "Archive créée."));
        var e3 = E(mep, 3, 1, "Création des index de transactions", [bdd], 20, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d paiement -f migrations/4.2-index.sql"), "Index `ix_transactions_portefeuille` présent.", e1, e2);
        e3.ResolutionIds = [c.Res(1).Id];
        var e4 = E(mep, 4, 1, "Déploiement du service paiement 4.2", [api], 25, Criticite.Elevee,
            "Le déploiement redémarre le service : prévenir le support **avant** de lancer.\n\n" + Code("cd /opt/paiement && ./deploy.sh 4.2"),
            "`GET /sante` renvoie 200 avec la version 4.2.", e1, e2);
        e4.ResolutionIds = [c.Res(0).Id];
        var e5 = E(mep, 5, 1, "Publication de l'écran de paiement", [web], 20, Criticite.Moderee,
            Sim + Code("npm ci && npm run build && ./publier.sh paiement"), "Le bouton « Payer avec mon mobile » s'affiche.", e1, e2);
        e5.ResolutionIds = [c.Res(2).Id];
        var e6 = NonRetour(E(mep, 6, 2, "Activation des nouveaux moyens de paiement", [api, mdm, bdd], 15, Criticite.Bloquante,
            "**Irréversible** : les portefeuilles sont déclarés auprès du MDM et les premiers paiements partent.\n\n" + Code("./activer-portefeuilles.sh --appliquer"),
            "Les portefeuilles apparaissent dans le MDM ; un paiement test passe.", e3, e4, e5));
        e6.ResolutionIds = [c.Res(3).Id];
        E(mep, 7, 3, "Contrôle final", [api, web, bdd], 10, Criticite.Elevee,
            "- [ ] Payer 1 € avec un portefeuille de test\n- [ ] Vérifier la transaction en base\n- [ ] Vérifier le tableau de bord", "Tous les contrôles sont verts.", e6);
        var r1 = R(mep, 1, 0, "Réinstaller le service paiement 4.1", [api], 15, Sim + Code("cd /opt/paiement && ./deploy.sh 4.1"), "`GET /sante` renvoie la version 4.1.");
        var r2 = R(mep, 2, 0, "Supprimer les nouveaux index", [bdd], 10, Sim + Code("psql -h bdd-demo -U admin -d paiement -f migrations/4.2-index-retrait.sql"),
            "Index retirés, sans erreur.");
        R(mep, 3, 1, "Republier l'écran de paiement 4.1", [web], 10, Sim + Code("./publier.sh paiement --version 4.1"), "L'ancien écran s'affiche.", r1, r2);
        mep.PlanRepli = Repli(35, "Service paiement 4.1 et écran 4.1.", "Table des transactions restaurée depuis E0001 si les index l'ont abîmée.");

        Fiche(mep, web, "Paiement depuis le site", ("Payer 1 € avec le portefeuille de test sur le site.", "Le paiement est accepté et le reçu s'affiche."));
        Fiche(mep, api, "API paiement", (Code("curl -fsS https://paiement.exemple.invalid/sante"), "Réponse 200 « paiement 4.2 »."));
        Fiche(mep, bdd, "Transactions", (Code("psql -h bdd-demo -U lecture -d paiement -c \"SELECT count(*) FROM transactions WHERE jour = current_date;\""),
            "Le nombre de transactions du jour continue d'augmenter."));
        Fiche(mep, mdm, "Référentiel MDM", ("Rechercher « portefeuille » dans le référentiel des moyens de paiement.", "Deux portefeuilles actifs."));

        var eq = c.Equipe;
        var go = c.Maintenant.AddMinutes(-80);
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0405",
            NiveauRisque = NiveauRisque.Eleve,
            Benefices = "Paiement par portefeuille électronique dans l'application\nPaiement mobile sur le site, sans saisie de carte",
            UtilisateursImpactes = "clients de l'application mobile et du site (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "terminer les paiements en cours",
            HeureLimite = Heure(go.AddMinutes(-15)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(3)),
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-4).AddHours(-1));
        d.Creer(eq.Alice);
        d.Plus(30).Approbateurs(eq.Alice, eq.Claire);
        d.A(c.Maintenant.AddDays(-2)).Relire(eq.Bob);
        d.A(c.Maintenant.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(3).Demarrer(eq.Alice, e1);
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(10).Reussir(eq.Bob, e2, "paiement-4.1.tgz (180 Mo) — fictif");
        d.Plus(4).Reussir(eq.Alice, e1, "transactions.sql (4,1 Go) — fictif");
        d.Plus(2).Demarrer(eq.Alice, e3);
        d.Plus(1).Demarrer(eq.Claire, e5);
        d.Plus(17).Reussir(eq.Alice, e3, "CREATE INDEX (2 index) — fictif");
        d.Plus(7).Echouer(eq.Claire, e5, "Erreur fictive : 403 au moment de la purge du cache CDN.");
        d.Plus(2).Commenter(eq.Claire, e5, "Le jeton de purge a sans doute expiré : appliquer la résolution type « WEB : purge du cache CDN refusée ».");
        d.A(c.Maintenant.AddMinutes(-6)).Demarrer(eq.Bob, e4);
        return d.Fin();
    }

    // ---------- 3 bis. MEP simplifiée en exécution ----------

    /// <summary>
    /// Petit correctif mené en MEP simplifiée : Claire pilote, Alice et Bob sont suppléants sans avoir été choisis, ni versions
    /// ni durée, criticité ou résolutions liées. Exécution depuis 40 minutes : paramètre en base et formulaire publiés, service
    /// messages en échec sur API et BDD (toutes les résolutions de ces deux systèmes sont proposées, regroupées par système).
    /// Échéance dans trois jours.
    /// </summary>
    private static MepDemo Simplifiee(Contexte c)
    {
        var (mep, alice, bob, claire) = NouvelleMep(c, OutilSimplifiee, "", "",
            "Correctif : les messages du formulaire de contact partent vers l'ancienne adresse du support.",
            "Formulaire de contact indisponible 5 minutes.", 5, e => e.Claire);
        mep.Type = TypeMep.Simplifiee;
        mep.EcheanceRepli = c.Soir(3);
        var bdd = Sys(mep, c.Sys("BDD"), "Paramètre « adresse du support »", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Export de la table `parametres` la veille (`parametres-veille.sql`).");
        var api = Sys(mep, c.Sys("API"), "Service messages 1.8.1", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état, la version 1.8.0 reste dans le dépôt d'artefacts.");
        var web = Sys(mep, c.Sys("WEB"), "Formulaire de contact", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Page republiée depuis le dépôt.");
        Responsables(mep,
            (bob, [api, bdd], "Service messages et paramètres", DisponibiliteJourJ.Joignable),
            (claire, [web], "Formulaire de contact", DisponibiliteJourJ.Joignable));
        // Objets impactés : la version de chaque objet se garde, même sans version actuelle ni cible pour la MEP.
        Objets(mep, bdd, c.Sys("BDD"), "adresse du support corrigée", "parametres");
        Objets(mep, api, c.Sys("API"), "1.8.1", "messagerie-api.jar");
        Objets(mep, web, c.Sys("WEB"), "formulaire corrigé", "portail-web.tar.gz");

        var e1 = E(mep, 1, 0, "Nouvelle adresse du support dans les paramètres", [bdd], 0, Criticite.Faible,
            Sim + Code("psql -h bdd-demo -U admin -d messages -c \"UPDATE parametres SET valeur = 'support@exemple.invalid' WHERE cle = 'adresse_support';\""),
            "UPDATE 1");
        var e2 = E(mep, 2, 1, "Redémarrage du service messages 1.8.1", [api, bdd], 0, Criticite.Faible,
            Sim + Code("cd /opt/messages && ./deploy.sh 1.8.1"), "`GET /sante` renvoie 200 et la base est joignable.", e1);
        var e3 = E(mep, 3, 1, "Publication du formulaire corrigé", [web], 0, Criticite.Faible,
            Sim + Code("./publier.sh contact"), "Le formulaire affiche « Votre message a été envoyé au support ».", e1);
        var e4 = E(mep, 4, 2, "Envoi d'un message de test", [web, api], 0, Criticite.Faible,
            "Envoyer un message depuis le formulaire avec l'objet « Test MEP ».", "Le message arrive dans la boîte du support.", e2, e3);
        Regles.DefinirFin(e4, FinEtape.Verification);
        var r1 = R(mep, 1, 0, "Remettre l'ancienne adresse", [bdd], 0,
            Sim + Code("psql -h bdd-demo -U admin -d messages -f parametres-veille.sql"), "Ancienne adresse en place.");
        R(mep, 2, 1, "Republier le service messages 1.8.0", [api], 0, Sim + Code("cd /opt/messages && ./deploy.sh 1.8.0"),
            "`GET /sante` renvoie la version 1.8.0.", r1);
        // MEP simplifiée : les étapes n'ont ni durée, ni criticité, ni résolutions liées.
        foreach (var e in mep.Etapes) Regles.AppliquerTypeAEtape(mep, e);
        mep.PlanRepli = Repli(10, "Service messages 1.8.0.", "Table `parametres` rechargée depuis `parametres-veille.sql`.");

        Fiche(mep, web, "Formulaire de contact", ("Envoyer un message depuis le site public.", "Accusé de réception affiché."));
        Fiche(mep, api, "Service messages", (Code("curl -fsS https://messages.exemple.invalid/sante"), "Réponse 200, version 1.8.1."));
        Fiche(mep, bdd, "Paramètres", (Code("psql -h bdd-demo -U lecture -d messages -c \"SELECT valeur FROM parametres WHERE cle = 'adresse_support';\""),
            "support@exemple.invalid"));

        var eq = c.Equipe;
        var go = c.Maintenant.AddMinutes(-40);
        // Pas de version dans une MEP simplifiée : {{version_cible}} reste à compléter dans l'annonce.
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0414",
            NiveauRisque = NiveauRisque.Faible,
            Benefices = "Les messages du formulaire de contact arrivent au support",
            UtilisateursImpactes = "visiteurs du site public (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "aucune",
            HeureLimite = Heure(go),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(1)),
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-1).AddHours(-6));
        d.Creer(eq.Claire);
        d.Plus(20).Approbateurs(eq.Claire, eq.Alice);
        d.Plus(30).Relire(eq.Bob);
        d.A(c.Maintenant.AddHours(-2)).Approuver(eq.Alice, "");
        d.A(go).Go(eq.Claire, c.Criteres);
        d.Plus(2).Demarrer(eq.Bob, e1);
        d.Plus(3).Reussir(eq.Bob, e1, "UPDATE 1 — fictif");
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(1).Demarrer(eq.Claire, e3);
        d.Plus(4).Reussir(eq.Claire, e3, "Formulaire publié — fictif");
        d.Plus(3).Echouer(eq.Bob, e2, "Erreur fictive : password authentication failed for user \"messages\".");
        d.Plus(2).Commenter(eq.Bob, e2, "La connexion à la base est refusée depuis le redémarrage : voir les résolutions API et BDD proposées.");
        return d.Fin();
    }

    // ---------- 4. Recette en cours ----------

    /// <summary>
    /// Déploiement terminé (dont une migration réparée par une résolution type), recette en cours : une fiche conforme,
    /// une étape en échec à faire refaire, des étapes encore à tester. Échéance dans deux jours.
    /// </summary>
    private static MepDemo EnRecette(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilRecette, "2.3", "3.0",
            "Nouveau portail RH : demandes de congés en ligne et validation par les gestionnaires.",
            "Portail RH indisponible pendant la publication (20 minutes).", 20, e => e.Alice, e => e.Bob);
        mep.EcheanceRepli = c.Soir(2);
        var bdd = Sys(mep, c.Sys("BDD"), "Schéma des congés", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde de la base `rh` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service congés 3.0", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état : la version 2.3 se réinstalle depuis le dépôt d'artefacts.");
        var web = Sys(mep, c.Sys("WEB"), "Portail RH 3.0", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Snapshot du volume `rh-web` pris la veille (`avant-3.0`).");
        var ldap = Sys(mep, c.Sys("LDAP"), "Groupe rh-gestionnaires", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Annuaire seulement lu par le portail.");
        Objets(mep, bdd, c.Sys("BDD"), "3.0.0 : demi-journées", "conges");
        Objets(mep, api, c.Sys("API"), "3.0.0", "conges-api.jar", "/v1/conges");
        Objets(mep, web, c.Sys("WEB"), "3.0.0", "rh-web.tar.gz");
        Objets(mep, ldap, c.Sys("LDAP"), "nouveau groupe", "cn=rh-gestionnaires");
        Responsables(mep,
            (bob, [api, ldap], "Service congés et droits des gestionnaires", DisponibiliteJourJ.Joignable),
            (claire, [web], "Portail RH", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la base RH", [bdd], 15, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin rh > /sauvegardes/rh.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration du schéma des congés", [bdd], 15, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U app_rh -d rh -f migrations/3.0.sql"), "Tables `conges` et `soldes` présentes, aucune erreur.", e1);
        e2.ResolutionIds = [c.Res(1).Id];
        var e3 = E(mep, 3, 1, "Déploiement du service congés 3.0", [api], 20, Criticite.Elevee,
            Sim + Code("cd /opt/conges && ./deploy.sh 3.0"), "`GET /sante` renvoie 200 avec la version 3.0.", e1);
        e3.ResolutionIds = [c.Res(0).Id];
        var e4 = E(mep, 4, 2, "Publication du portail RH 3.0", [web], 20, Criticite.Moderee,
            Sim + Code("npm ci && npm run build && ./publier.sh rh"), "La page d'accueil affiche « Portail RH 3.0 ».", e2, e3);
        e4.ResolutionIds = [c.Res(2).Id];
        var e5 = E(mep, 5, 3, "Contrôle des droits gestionnaires", [ldap, api], 10, Criticite.Moderee,
            "Se connecter avec un compte du groupe `rh-gestionnaires` et ouvrir « Demandes à valider ».", "La liste des demandes s'affiche.", e4);
        var r1 = R(mep, 1, 0, "Republier le portail RH 2.3", [web], 15, Sim + "Restaurer le snapshot `avant-3.0` du volume `rh-web`.", "Le portail affiche la version 2.3.");
        var r2 = R(mep, 2, 0, "Réinstaller le service congés 2.3", [api], 15, Sim + Code("cd /opt/conges && ./deploy.sh 2.3"), "`GET /sante` renvoie la version 2.3.");
        R(mep, 3, 1, "Restaurer la base RH", [bdd], 25, Sim + Code("psql -h bdd-demo -U admin rh < /sauvegardes/rh.sql"), "Restauration sans erreur.", r1, r2);
        mep.PlanRepli = Repli(40, "Portail RH 2.3 et service congés 2.3.", "Base RH restaurée depuis E0001 (les demandes saisies depuis sont à ressaisir).");

        Fiche(mep, web, "Demande de congé",
            ("Se connecter avec un compte salarié de test et ouvrir « Mes congés ».", "Le solde de congés s'affiche."),
            ("Poser une demande de congé d'une journée.", "La demande apparaît « En attente »."));
        Fiche(mep, api, "API congés",
            (Code("curl -fsS https://rh.exemple.invalid/api/sante"), "Réponse 200 « conges 3.0 »."),
            (Code("curl -fsS -w '%{time_total}' https://rh.exemple.invalid/api/soldes/test"), "Réponse en moins d'une seconde."));
        Fiche(mep, bdd, "Soldes de congés",
            (Code("psql -h bdd-demo -U lecture -d rh -c \"SELECT count(*) FROM soldes;\""), "Autant de soldes que de salariés (1 250)."));
        Fiche(mep, ldap, "Accès gestionnaires",
            ("Se connecter avec un compte du groupe `rh-gestionnaires`.", "Le menu « Demandes à valider » est présent."),
            ("Valider la demande de congé de test.", "La demande passe « Validée »."));

        var eq = c.Equipe;
        var go = c.Maintenant.AddHours(-4);
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0407",
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Demandes de congés en ligne\nValidation par le gestionnaire depuis son tableau de bord\nSolde de congés à jour en temps réel",
            UtilisateursImpactes = "tous les salariés et les gestionnaires RH (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "enregistrer vos demandes de congés en cours",
            HeureLimite = Heure(go.AddMinutes(-30)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(5)),
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-5).AddHours(-2));
        d.Creer(eq.Alice);
        d.Plus(20).Approbateurs(eq.Alice, eq.Claire);
        d.A(c.Maintenant.AddDays(-2)).Relire(eq.Bob);
        d.A(c.Maintenant.AddDays(-1).AddHours(-3)).Approuver(eq.Claire, "OK, snapshot du portail vérifié.");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(13).Reussir(eq.Alice, e1, "rh.sql (850 Mo) — fictif");
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(1).Demarrer(eq.Bob, e3);
        d.Plus(7).Echouer(eq.Alice, e2, "Erreur fictive : permission denied for table conges.");
        d.Plus(6);
        var resolution = d.LancerResolution(eq.Alice, e2, c.Res(1));
        d.Plus(4).Corriger(eq.Alice, resolution, 1, ResultatControle.Conforme, "GRANT");
        d.Plus(2).Corriger(eq.Alice, resolution, 2, ResultatControle.Conforme, "GRANT");
        d.Plus(4).Verifier(eq.Alice, resolution, ResultatControle.Conforme, "SELECT 1 : 1 ligne, sans erreur de droits.");
        d.Plus(1).Reussir(eq.Bob, e3, "Version 3.0 en ligne — fictif");
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(13).Reussir(eq.Alice, e2, "Migration 3.0 appliquée — fictif", "Droits de app_rh rétablis par la résolution type avant la reprise.");
        d.Plus(2).Demarrer(eq.Claire, e4);
        d.Plus(23).Reussir(eq.Claire, e4, "Portail RH 3.0 publié — fictif");
        d.Plus(2).Demarrer(eq.Bob, e5);
        d.Plus(13).Reussir(eq.Bob, e5, "Liste des demandes visible — fictif");
        d.Plus(5).Recette(eq.Alice);
        d.Plus(10).Tester(eq.Claire, "Demande de congé", 1, ResultatControle.Conforme, "Solde de 25 jours affiché.");
        d.Plus(5).Tester(eq.Claire, "Demande de congé", 2, ResultatControle.Conforme, "Demande enregistrée « En attente ».");
        d.Plus(5).Tester(eq.Bob, "API congés", 1, ResultatControle.Conforme, "200 conges 3.0");
        d.Plus(5).Tester(eq.Bob, "API congés", 2, ResultatControle.Echec, "4,2 s au premier appel (cache froid), puis 0,3 s.");
        d.Plus(5).Tester(eq.Alice, "Accès gestionnaires", 1, ResultatControle.Conforme, "Menu présent pour le compte de test.");
        return d.Fin();
    }

    // ---------- 5. Retour arrière en cours ----------

    /// <summary>
    /// Déploiement de l'API en échec, résolution type en échec : retour arrière déclenché automatiquement. Le service 5.0 est
    /// réinstallé, la restauration de la base est en cours, la vérification reste à faire.
    /// </summary>
    private static MepDemo EnRetourArriere(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilRetourArriere, "5.0", "5.1",
            "Pièces jointes jusqu'à 50 Mo et nouvelle recherche dans la messagerie interne.",
            "Messagerie indisponible pendant le déploiement (30 minutes).", 30, e => e.Alice, e => e.Bob);
        mep.EcheanceRepli = c.Soir(1);
        var bdd = Sys(mep, c.Sys("BDD"), "Base des messages : index plein texte", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep,
            "Sauvegarde complète de la base `messagerie` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service messagerie 5.1", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Archive de `/opt/messagerie` (E0002).");
        var web = Sys(mep, c.Sys("WEB"), "Client web de messagerie", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Client publié depuis le dépôt : la version 5.0 se republie en une commande.");
        Objets(mep, bdd, c.Sys("BDD"), "5.1.0", "messages", "ix_messages_texte");
        Objets(mep, api, c.Sys("API"), "5.1.0", "messagerie-api.jar");
        Objets(mep, web, c.Sys("WEB"), "5.1.0", "messagerie-web.tar.gz");
        Responsables(mep,
            (bob, [api], "Service messagerie", DisponibiliteJourJ.Joignable),
            (claire, [web], "Client web", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la base des messages", [bdd], 15, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin messagerie > /sauvegardes/messagerie.sql"), "Fichier présent, sans erreur."));
        var e2 = Sauvegarde(E(mep, 2, 0, "Sauvegarde du service messagerie", [api], 10, Criticite.Moderee,
            Sim + Code("tar czf /sauvegardes/messagerie-5.0.tgz /opt/messagerie"), "Archive créée."));
        var e3 = E(mep, 3, 1, "Index plein texte des messages", [bdd], 25, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d messagerie -f migrations/5.1.sql"), "Index `ix_messages_recherche` présent.", e1, e2);
        var e4 = E(mep, 4, 1, "Déploiement du service messagerie 5.1", [api], 20, Criticite.Elevee,
            Sim + Code("cd /opt/messagerie && ./deploy.sh 5.1"), "`GET /sante` renvoie 200 avec la version 5.1.", e1, e2);
        e4.ResolutionIds = [c.Res(0).Id];
        var e5 = E(mep, 5, 2, "Publication du client web 5.1", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh messagerie 5.1"), "Le client affiche la nouvelle recherche.", e3, e4);
        E(mep, 6, 3, "Contrôle final", [bdd, api, web], 10, Criticite.Elevee,
            "- [ ] Envoyer un message avec une pièce jointe de 40 Mo\n- [ ] Le retrouver par la recherche", "Tous les contrôles sont verts.", e5);
        var r1 = R(mep, 1, 0, "Réinstaller le service messagerie 5.0", [api], 15, Sim + Code("cd /opt/messagerie && ./deploy.sh 5.0"), "`GET /sante` renvoie la version 5.0.");
        var r2 = R(mep, 2, 0, "Restaurer la base des messages", [bdd], 30, Sim + Code("psql -h bdd-demo -U admin messagerie < /sauvegardes/messagerie.sql"),
            "Restauration sans erreur.");
        R(mep, 3, 1, "Vérifier l'envoi d'un message", [web, api], 10, "Envoyer un message de test depuis le client web.", "Le message est reçu.", r1, r2);
        mep.PlanRepli = Repli(45, "Service messagerie 5.0.", "Base des messages restaurée depuis E0001 (messages reçus pendant la MEP perdus : prévenir les utilisateurs).");

        Fiche(mep, web, "Recherche de messages", ("Rechercher « réunion » dans la boîte de test.", "Les messages contenant « réunion » s'affichent."));
        Fiche(mep, api, "Pièces jointes", ("Envoyer un message avec une pièce jointe de 40 Mo.", "Le message part et la pièce jointe s'ouvre."));
        Fiche(mep, bdd, "Index plein texte", (Code("psql -h bdd-demo -U lecture -d messagerie -c \"\\di ix_messages_recherche\""), "L'index est listé."));

        var eq = c.Equipe;
        var go = c.Maintenant.AddMinutes(-75);
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0409",
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Pièces jointes jusqu'à 50 Mo\nRecherche dans le texte des messages",
            UtilisateursImpactes = "tous les utilisateurs de la messagerie interne (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "envoyer vos messages en cours et fermer la messagerie",
            HeureLimite = Heure(go.AddMinutes(-10)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(2)),
        };

        var d = new Deroule(mep, c.Maintenant.AddDays(-6).AddHours(-1));
        d.Creer(eq.Alice);
        d.Plus(15).Approbateurs(eq.Alice, eq.Claire);
        d.A(c.Maintenant.AddDays(-3)).Relire(eq.Bob);
        d.A(c.Maintenant.AddDays(-2)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(7).Reussir(eq.Bob, e2, "messagerie-5.0.tgz — fictif");
        d.Plus(2).Reussir(eq.Alice, e1, "messagerie.sql (12 Go) — fictif");
        d.Plus(2).Demarrer(eq.Alice, e3);
        d.Plus(1).Demarrer(eq.Bob, e4);
        d.Plus(15).Echouer(eq.Bob, e4, "Erreur fictive : GET /sante sans réponse après 5 minutes.");
        d.Plus(3);
        var resolution = d.LancerResolution(eq.Bob, e4, c.Res(0));
        d.Plus(2).Reussir(eq.Alice, e3, "Index créé — fictif");
        d.Plus(5).Corriger(eq.Bob, resolution, 1, ResultatControle.Echec, "Le service redémarre puis s'arrête : fichier de configuration 5.1 illisible.");
        d.Plus(4).Demarrer(eq.Bob, mep.Etapes.Single(x => x.Code == "R0001"));
        d.Plus(1).Demarrer(eq.Alice, mep.Etapes.Single(x => x.Code == "R0002"));
        d.Plus(11).Reussir(eq.Bob, mep.Etapes.Single(x => x.Code == "R0001"), "Version 5.0 de nouveau en ligne — fictif");
        return d.Fin();
    }

    // ---------- 6. Clôturée réussie ----------

    /// <summary>MEP d'il y a six jours : point de non-retour franchi avec l'accord du pilote, recette conforme, bilan rédigé.</summary>
    private static MepDemo Reussie(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilReussie, "6.2", "6.3",
            "Nouveau calcul des réassorts et inventaire tournant dans les entrepôts.", "Saisie des inventaires suspendue pendant la MEP.", 60,
            e => e.Alice, e => e.Bob);
        var go = c.Debut(-6);
        mep.EcheanceRepli = go.AddHours(5);
        var bdd = Sys(mep, c.Sys("BDD"), "Tables d'inventaire", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde de la base `stocks` (E0001).");
        var batch = Sys(mep, c.Sys("BATCH"), "Calcul nocturne des réassorts", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Export des paramètres de planification la veille (`planification-6.2.json`).");
        var api = Sys(mep, c.Sys("API"), "Service stocks 6.3", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état, réinstallable depuis le dépôt d'artefacts.");
        Objets(mep, bdd, c.Sys("BDD"), "6.3.0", "inventaire", "p_recalcul_stocks");
        Objets(mep, batch, c.Sys("BATCH"), "6.3.0", "STKREA", "batch-stocks.jar");
        Objets(mep, api, c.Sys("API"), "6.3.0", "stocks-api.jar");
        Responsables(mep,
            (bob, [api, batch], "Service stocks et calcul nocturne", DisponibiliteJourJ.Joignable),
            (claire, [], "Écrans d'inventaire (aucun changement)", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la base stocks", [bdd], 20, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin stocks > /sauvegardes/stocks.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration des tables d'inventaire", [bdd], 20, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d stocks -f migrations/6.3.sql"), "Table `inventaires_tournants` présente.", e1);
        var e3 = E(mep, 3, 1, "Déploiement du service stocks 6.3", [api], 15, Criticite.Elevee,
            Sim + Code("cd /opt/stocks && ./deploy.sh 6.3"), "`GET /sante` renvoie 200 avec la version 6.3.", e1);
        e3.ResolutionIds = [c.Res(0).Id];
        var e4 = NonRetour(E(mep, 4, 2, "Recalcul initial des stocks", [bdd, batch], 25, Criticite.Bloquante,
            "**Irréversible** : les stocks théoriques sont recalculés avec la nouvelle règle.\n\n" + Code("./recalcul-initial.sh --appliquer"),
            "Recalcul terminé, écart global inférieur à 0,5 %.", e2, e3));
        var e5 = E(mep, 5, 3, "Réactivation du calcul nocturne", [batch], 10, Criticite.Moderee,
            Sim + Code("ordonnanceur activer reassorts-nocturnes"), "Le traitement est planifié à 02:00.", e4);
        var r1 = R(mep, 1, 0, "Restaurer la base stocks", [bdd], 30, Sim + Code("psql -h bdd-demo -U admin stocks < /sauvegardes/stocks.sql"), "Restauration sans erreur.");
        var r2 = R(mep, 2, 0, "Réinstaller le service stocks 6.2", [api], 10, Sim + Code("cd /opt/stocks && ./deploy.sh 6.2"), "`GET /sante` renvoie la version 6.2.");
        R(mep, 3, 1, "Recharger les paramètres de planification", [batch], 10, Sim + Code("ordonnanceur importer planification-6.2.json"),
            "Paramètres 6.2 rechargés.", r1, r2);
        mep.PlanRepli = Repli(50, "Service stocks 6.2 et paramètres de planification 6.2.", "Base stocks restaurée depuis E0001.");

        Fiche(mep, bdd, "Inventaire tournant", (Code("psql -h bdd-demo -U lecture -d stocks -c \"SELECT count(*) FROM inventaires_tournants;\""), "12 entrepôts planifiés."));
        Fiche(mep, batch, "Calcul nocturne", ("Vérifier la planification dans l'ordonnanceur.", "« reassorts-nocturnes » planifié à 02:00."));
        Fiche(mep, api, "API stocks",
            (Code("curl -fsS https://stocks.exemple.invalid/sante"), "Réponse 200 « stocks 6.3 »."),
            (Code("curl -fsS https://stocks.exemple.invalid/stocks/ENT-01/REF-42"), "Stock théorique renvoyé."));

        var eq = c.Equipe;
        // Clôturée réussie : la nouvelle version a été validée par les utilisateurs le lendemain.
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0371",
            NiveauRisque = NiveauRisque.Eleve,
            Benefices = "Réassorts calculés selon les ventes réelles\nInventaire tournant sans fermer l'entrepôt",
            UtilisateursImpactes = "responsables d'entrepôt et service Logistique (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "terminer les inventaires en cours",
            HeureLimite = Heure(go.AddMinutes(-30)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(3)),
            ValideeParUtilisateurs = true,
            ValideePar = "Service Logistique (fictif)",
            ValideeLe = Jour(go.AddDays(1)),
        };

        var d = new Deroule(mep, go.AddDays(-5));
        d.Creer(eq.Alice);
        d.Plus(30).Approbateurs(eq.Alice, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Bob);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "Recalcul testé en préproduction, OK.");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(18).Reussir(eq.Alice, e1, "stocks.sql (3,2 Go) — fictif");
        d.Plus(2).Demarrer(eq.Alice, e2);
        d.Plus(1).Demarrer(eq.Bob, e3);
        d.Plus(16).Reussir(eq.Bob, e3, "Version 6.3 en ligne — fictif");
        d.Plus(4).Reussir(eq.Alice, e2, "Migration 6.3 appliquée — fictif");
        d.Plus(2).AutoriserNonRetour(eq.Alice, e4);
        d.Plus(1).Demarrer(eq.Bob, e4);
        d.Plus(22).Reussir(eq.Bob, e4, "Écart global 0,2 % — fictif");
        d.Plus(2).Demarrer(eq.Bob, e5);
        d.Plus(6).Reussir(eq.Bob, e5, "Planifié à 02:00 — fictif");
        d.Plus(5).Recette(eq.Alice);
        d.Plus(5).Tester(eq.Alice, "Inventaire tournant", 1, ResultatControle.Conforme, "12 lignes.");
        d.Plus(3).Tester(eq.Bob, "Calcul nocturne", 1, ResultatControle.Conforme, "Planifié à 02:00.");
        d.Plus(4).Tester(eq.Bob, "API stocks", 1, ResultatControle.Conforme, "200 stocks 6.3");
        d.Plus(2).Tester(eq.Claire, "API stocks", 2, ResultatControle.Conforme, "Stock de 37 unités renvoyé.");
        d.Plus(6).Cloturer(eq.Alice,
            "Déploiement dans les temps (1 h 45 pour 2 h prévues). Recalcul initial plus rapide que prévu.",
            "Rien à signaler.",
            "Ajouter le contrôle des réassorts du lendemain matin à la recette des prochaines versions.");
        return d.Fin();
    }

    // ---------- 7. Clôturée repliée ----------

    /// <summary>
    /// MEP d'il y a dix jours, pilotée par Bob : ouverture aux partenaires en échec sans résolution connue, retour arrière
    /// déclenché par le pilote, service rétabli, bilan rédigé.
    /// </summary>
    private static MepDemo Repliee(Contexte c)
    {
        var (mep, alice, _, claire) = NouvelleMep(c, OutilRepliee, "2.5", "3.0",
            "Nouvel extranet des partenaires : tableau de bord et suivi des commandes.", "Extranet indisponible pendant la publication (15 minutes).", 15,
            e => e.Bob, e => e.Alice);
        var go = c.Debut(-10);
        mep.EcheanceRepli = go.AddHours(4);
        var web = Sys(mep, c.Sys("WEB"), "Extranet 3.0", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Snapshot du volume `extranet` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service partenaires 3.0", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Archive de `/opt/partenaires` (E0002).");
        var ldap = Sys(mep, c.Sys("LDAP"), "Comptes partenaires", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Seul un attribut d'ouverture est ajouté aux comptes pilotes, retiré par le repli.");
        Objets(mep, web, c.Sys("WEB"), "3.0.0", "extranet-web.tar.gz");
        Objets(mep, api, c.Sys("API"), "3.0.0", "partenaires-api.jar");
        Objets(mep, ldap, c.Sys("LDAP"), "partenaires pilotes", "cn=partenaires", "ou=partenaires");
        Responsables(mep,
            (claire, [web], "Extranet", DisponibiliteJourJ.Joignable),
            (alice, [ldap], "Comptes partenaires", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Snapshot de l'extranet", [web], 10, Criticite.Moderee,
            Sim + "Dans la console de virtualisation, créer un snapshot `avant-3.0` du volume `extranet`.", "Snapshot visible dans la liste."));
        var e2 = Sauvegarde(E(mep, 2, 0, "Sauvegarde du service partenaires", [api], 10, Criticite.Moderee,
            Sim + Code("tar czf /sauvegardes/partenaires-2.5.tgz /opt/partenaires"), "Archive créée."));
        var e3 = E(mep, 3, 1, "Déploiement du service partenaires 3.0", [api], 15, Criticite.Elevee,
            Sim + Code("cd /opt/partenaires && ./deploy.sh 3.0"), "`GET /sante` renvoie 200 avec la version 3.0.", e1, e2);
        var e4 = E(mep, 4, 1, "Publication de l'extranet 3.0", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh extranet 3.0"), "La page d'accueil affiche le tableau de bord.", e1, e2);
        var e5 = E(mep, 5, 2, "Ouverture aux partenaires pilotes", [ldap, web], 15, Criticite.Elevee,
            Sim + Code("./ouvrir-extranet.sh --groupe partenaires-pilotes"), "Les 5 partenaires pilotes accèdent au tableau de bord en moins de 2 s.", e3, e4);
        E(mep, 6, 3, "Ouverture à tous les partenaires", [ldap, web], 10, Criticite.Elevee,
            Sim + Code("./ouvrir-extranet.sh --groupe partenaires"), "Tous les partenaires accèdent au nouvel extranet.", e5);
        var r1 = R(mep, 1, 0, "Republier l'extranet 2.5", [web], 15, Sim + "Restaurer le snapshot `avant-3.0` du volume `extranet`.", "La page d'accueil 2.5 s'affiche.");
        var r2 = R(mep, 2, 0, "Réinstaller le service partenaires 2.5", [api], 10, Sim + Code("cd /opt/partenaires && ./deploy.sh 2.5"), "`GET /sante` renvoie la version 2.5.");
        R(mep, 3, 1, "Vérifier la connexion d'un partenaire", [ldap, web], 10,
            "Se connecter avec le compte partenaire de test et ouvrir « Mes commandes ».", "La liste des commandes s'affiche.", r1, r2);
        mep.PlanRepli = Repli(35, "Extranet 2.5 et service partenaires 2.5.", "Aucune donnée à restaurer : le service 3.0 n'a pas modifié la base.");

        Fiche(mep, web, "Tableau de bord partenaire", ("Se connecter avec le compte partenaire de test.", "Le tableau de bord s'affiche en moins de 2 s."));
        Fiche(mep, api, "API partenaires", (Code("curl -fsS https://partenaires.exemple.invalid/sante"), "Réponse 200 « partenaires 3.0 »."));
        Fiche(mep, ldap, "Accès des partenaires pilotes", ("Vérifier l'attribut `extranet3` des comptes pilotes.", "Attribut présent sur les 5 comptes."));

        var eq = c.Equipe;
        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0352",
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Tableau de bord des partenaires\nSuivi des commandes en ligne",
            UtilisateursImpactes = "partenaires revendeurs (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "se déconnecter de l'extranet",
            HeureLimite = Heure(go.AddMinutes(-15)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(2)),
        };

        var d = new Deroule(mep, go.AddDays(-6));
        d.Creer(eq.Bob);
        d.Plus(20).Approbateurs(eq.Bob, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Alice);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Bob, c.Criteres);
        d.Plus(2).Demarrer(eq.Claire, e1);
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(8).Reussir(eq.Bob, e2, "partenaires-2.5.tgz — fictif");
        d.Plus(1).Reussir(eq.Claire, e1, "snap-7c21 — fictif");
        d.Plus(2).Demarrer(eq.Bob, e3);
        d.Plus(1).Demarrer(eq.Claire, e4);
        d.Plus(15).Reussir(eq.Bob, e3, "Version 3.0 en ligne — fictif");
        d.Plus(2).Reussir(eq.Claire, e4, "Extranet 3.0 publié — fictif");
        d.Plus(3).Demarrer(eq.Alice, e5);
        d.Plus(15).Echouer(eq.Alice, e5, "Erreur fictive : tableau de bord affiché en 8,2 s (attendu moins de 2 s).");
        d.Plus(5).DeclencherRepli(eq.Bob, "Temps de réponse inacceptable et aucune résolution connue : repli décidé avec l'équipe.");
        var r3 = mep.Etapes.Single(x => x.Code == "R0003");
        d.Plus(2).Demarrer(eq.Claire, r1);
        d.Plus(1).Demarrer(eq.Bob, r2);
        d.Plus(11).Reussir(eq.Bob, r2, "Version 2.5 de nouveau en ligne — fictif");
        d.Plus(6).Reussir(eq.Claire, r1, "Snapshot avant-3.0 restauré — fictif");
        d.Plus(2).Demarrer(eq.Alice, r3);
        d.Plus(8).Reussir(eq.Alice, r3, "Commandes du partenaire de test affichées — fictif");
        d.Plus(5).Cloturer(eq.Bob,
            "Repli complet en 35 minutes, comme lors du test. Les partenaires pilotes ont été prévenus à temps.",
            "Tableau de bord à 8,2 s : la requête des commandes n'utilise pas d'index sur la table des partenaires.",
            "Ajouter l'index manquant ; test de charge en préproduction avant de reprogrammer la MEP.");
        return d.Fin();
    }
}
