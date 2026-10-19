namespace GestionMep.Domain;

public static partial class DonneesDemo
{
    public const string OutilHistoriqueReussie = "Portail clients";
    public const string OutilHistoriqueReferentiel = "Référentiel produits";
    public const string OutilHistoriqueIncidents = "Batch de nuit comptable";
    public const string OutilHistoriqueRepliee = "Prise de rendez-vous";

    /// <summary>
    /// Historique des mois passés (#59) : MEP clôturées d'autres outils, pour que la recherche avancée et l'historique d'un
    /// système aient des résultats. Portail clients 4.0 (il y a trois mois et demi, réussie, WEB et API), Référentiel produits
    /// 1.2 (deux mois et demi, réussie, MDM et BDD), Batch de nuit comptable 7.0 (sept semaines, réussie avec un échec repris sur
    /// BDD) et Prise de rendez-vous 2.1 (trois semaines, repliée après un échec sur API).
    /// </summary>
    private static List<MepDemo> Historique(Contexte c) => [PortailClients(c), Referentiel(c), BatchComptable(c), RendezVous(c)];

    /// <summary>Communication d'une MEP passée : créneau annoncé autour du Go, validée par les utilisateurs si elle a réussi.</summary>
    private static void Communique(Mep mep, DateTimeOffset go, string reference, NiveauRisque risque, string benefices, string utilisateurs,
        string action, int heures, string? validee = null) =>
        mep.Communication = new Communication
        {
            ReferenceDemande = reference, NiveauRisque = risque, Benefices = benefices, UtilisateursImpactes = utilisateurs,
            ArretApplicatif = true, ActionUtilisateurs = action, HeureLimite = Heure(go.AddMinutes(-30)), DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go), HeureFinAnnoncee = Heure(go.AddHours(heures)),
            ValideeParUtilisateurs = validee is not null, ValideePar = validee ?? "", ValideeLe = validee is null ? null : Jour(go.AddDays(1)),
        };

    // ---------- Portail clients 4.0 : réussie ----------

    private static MepDemo PortailClients(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilHistoriqueReussie, "3.9", "4.0",
            "Refonte de l'espace client : historique des commandes et préférences de contact.", "Espace client indisponible 15 minutes.", 15,
            e => e.Alice, e => e.Bob);
        var go = c.Debut(-105);
        mep.EcheanceRepli = go.AddHours(4);
        var web = Sys(mep, c.Sys("WEB"), "Espace client 4.0", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Snapshot du volume `web-prod` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service commandes : historique client", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état, réinstallable depuis le dépôt d'artefacts.");
        Objets(mep, web, c.Sys("WEB"), "4.0.0", "portail-web.tar.gz");
        Objets(mep, api, c.Sys("API"), "4.0.0", "commandes-api.jar");
        Responsables(mep, (bob, [api, web], "Service commandes et espace client", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Snapshot du portail", [web], 10, Criticite.Moderee,
            Sim + "Dans la console de virtualisation, créer un snapshot `avant-4.0` du volume `web-prod`.", "Snapshot visible dans la liste."));
        var e2 = E(mep, 2, 1, "Déploiement du service commandes", [api], 15, Criticite.Elevee,
            Sim + Code("cd /opt/commandes && ./deploy.sh 4.0"), "`GET /sante` renvoie 200 avec la version 4.0.", e1);
        var e3 = E(mep, 3, 2, "Publication de l'espace client 4.0", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh portail 4.0"), "L'espace client affiche l'historique des commandes.", e2);
        var r1 = R(mep, 1, 0, "Restaurer le portail 3.9", [web], 10, Sim + "Restaurer le snapshot `avant-4.0` du volume `web-prod`.", "La page 3.9 s'affiche.");
        R(mep, 2, 1, "Réinstaller le service commandes 3.9", [api], 10, Sim + Code("cd /opt/commandes && ./deploy.sh 3.9"), "`GET /sante` renvoie la version 3.9.", r1);
        mep.PlanRepli = Repli(20, "Portail et service commandes 3.9.", "Aucune donnée à restaurer.");
        Communique(mep, go, "CHG-DEMO-0188", NiveauRisque.Modere, "Historique des commandes dans l'espace client\nPréférences de contact modifiables en ligne",
            "clients du portail (fictif)", "terminer les commandes en cours", 1, "Service Clients (fictif)");
        Fiche(mep, web, "Espace client", ("Se connecter avec le client de test et ouvrir « Mes commandes ».", "Les 12 dernières commandes s'affichent."));

        var eq = c.Equipe;
        var d = new Deroule(c.Resolutions, mep, go.AddDays(-7));
        d.Creer(eq.Alice);
        d.Plus(15).Approbateurs(eq.Alice, eq.Claire);
        d.A(go.AddDays(-3)).Relire(eq.Bob);
        d.A(go.AddDays(-2)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Claire, e1);
        d.Plus(7).Reussir(eq.Claire, e1, "snap-4a12 — fictif");
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(12).Reussir(eq.Bob, e2, "Version 4.0 en ligne — fictif");
        d.Plus(1).Demarrer(eq.Claire, e3);
        d.Plus(13).Reussir(eq.Claire, e3, "Espace client 4.0 publié — fictif");
        d.Plus(3).Recette(eq.Alice);
        d.Plus(4).Tester(eq.Claire, "Espace client", 1, ResultatControle.Conforme, "12 commandes.");
        d.Plus(3).Cloturer(eq.Alice, "Déploiement en 40 minutes pour 40 prévues.", "Aucun.", "");
        return d.Fin();
    }

    // ---------- Référentiel produits 1.2 : réussie ----------

    private static MepDemo Referentiel(Contexte c)
    {
        var (mep, alice, bob, _) = NouvelleMep(c, OutilHistoriqueReferentiel, "1.1", "1.2",
            "Nouvelles catégories de produits poussées vers le MDM.", "Modifications du référentiel gelées 5 minutes.", 5,
            e => e.Bob, e => e.Alice);
        var go = c.Debut(-75);
        mep.EcheanceRepli = go.AddHours(3);
        var mdm = Sys(mep, c.Sys("MDM"), "Catégories de produits", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Le MDM garde l'historique des versions : il restaure lui-même les catégories.");
        var bdd = Sys(mep, c.Sys("BDD"), "Paramètres des catégories", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde de la table `parametres` (E0001).");
        Objets(mep, mdm, c.Sys("MDM"), "1.2.0", "mdm-rest");
        Objets(mep, bdd, c.Sys("BDD"), "1.2.0", "parametres");
        Responsables(mep, (bob, [mdm], "Flux MDM", DisponibiliteJourJ.Joignable), (alice, [bdd], "Base produits", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la table parametres", [bdd], 10, Criticite.Moderee,
            Sim + Code("pg_dump -h bdd-demo -U admin -t parametres produits > /sauvegardes/parametres.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Ajout des catégories", [bdd], 10, Criticite.Moderee,
            Sim + Code("psql -h bdd-demo -U admin -d produits -f categories-1.2.sql"), "38 catégories présentes.", e1);
        var e3 = E(mep, 3, 2, "Publication vers le MDM", [mdm, bdd], 15, Criticite.Elevee,
            Sim + Code("./publier-categories.sh --mdm"), "Les 38 catégories sont visibles dans le MDM.", e2);
        var r1 = R(mep, 1, 0, "Restaurer la table parametres", [bdd], 10, Sim + Code("psql -h bdd-demo -U admin produits < /sauvegardes/parametres.sql"), "Restauration sans erreur.");
        R(mep, 2, 1, "Republier les catégories 1.1", [mdm], 10, Sim + Code("./publier-categories.sh --mdm --version 1.1"), "32 catégories dans le MDM.", r1);
        mep.PlanRepli = Repli(20, "Catégories 1.1.", "Table `parametres` restaurée depuis E0001.");
        Communique(mep, go, "CHG-DEMO-0236", NiveauRisque.Faible, "Nouvelles catégories disponibles dans le MDM\nRecherche par catégorie dans les applications",
            "gestionnaires du référentiel (fictif)", "ne pas modifier de fiche produit", 1, "Équipe Référentiel (fictif)");
        Fiche(mep, mdm, "Catégories dans le MDM", ("Chercher la catégorie « Jardin » dans le MDM.", "Catégorie présente avec ses 4 sous-catégories."));

        var eq = c.Equipe;
        var d = new Deroule(c.Resolutions, mep, go.AddDays(-5));
        d.Creer(eq.Bob);
        d.Plus(10).Approbateurs(eq.Bob, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Alice);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Bob, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(6).Reussir(eq.Alice, e1, "parametres.sql — fictif");
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(8).Reussir(eq.Alice, e2, "38 catégories — fictif");
        d.Plus(1).Demarrer(eq.Bob, e3);
        d.Plus(12).Reussir(eq.Bob, e3, "Catégories publiées — fictif");
        d.Plus(2).Recette(eq.Bob);
        d.Plus(3).Tester(eq.Bob, "Catégories dans le MDM", 1, ResultatControle.Conforme, "4 sous-catégories.");
        d.Plus(2).Cloturer(eq.Bob, "MEP sans incident, en 35 minutes.", "Aucun.", "");
        return d.Fin();
    }

    // ---------- Batch de nuit comptable 7.0 : réussie avec un échec repris ----------

    private static MepDemo BatchComptable(Contexte c)
    {
        var (mep, _, bob, _) = NouvelleMep(c, OutilHistoriqueIncidents, "6.9", "7.0",
            "Nouveau calendrier des traitements comptables et tables de facturation adaptées.", "Facturation suspendue pendant la MEP.", 45,
            e => e.Alice, e => e.Bob);
        var go = c.Debut(-48);
        mep.EcheanceRepli = go.AddHours(4);
        var bdd = Sys(mep, c.Sys("BDD"), "Tables de facturation", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde des tables `factures` (E0001).");
        var batch = Sys(mep, c.Sys("BATCH"), "Chaînes comptables de nuit", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Export des paramètres de planification la veille (`planification-6.9.json`).");
        Objets(mep, bdd, c.Sys("BDD"), "7.0.0", "factures", "lignes_facture");
        Objets(mep, batch, c.Sys("BATCH"), "7.0.0", "FACT01", "FACT03");
        Responsables(mep, (bob, [batch], "Chaînes comptables", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde des tables de facturation", [bdd], 20, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin -t factures -t lignes_facture compta > /sauvegardes/factures.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration des tables de facturation", [bdd], 20, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d compta -f migrations/7.0.sql"), "Tables migrées, aucune erreur.", e1);
        var e3 = E(mep, 3, 1, "Déploiement des chaînes comptables 7.0", [batch], 15, Criticite.Moderee,
            Sim + Code("ordonnanceur deployer compta 7.0"), "Version 7.0 visible dans l'ordonnanceur.", e1);
        var e4 = E(mep, 4, 2, "Activation du nouveau calendrier", [batch], 10, Criticite.Moderee,
            Sim + Code("ordonnanceur activer calendrier-compta-7"), "FACT01 planifiée à 01:00, FACT03 à 03:00.", e2, e3);
        var r1 = R(mep, 1, 0, "Restaurer les tables de facturation", [bdd], 30, Sim + Code("psql -h bdd-demo -U admin compta < /sauvegardes/factures.sql"), "Restauration sans erreur.");
        R(mep, 2, 1, "Recharger le calendrier 6.9", [batch], 10, Sim + Code("ordonnanceur importer planification-6.9.json"), "Calendrier 6.9 rechargé.", r1);
        mep.PlanRepli = Repli(40, "Chaînes comptables 6.9 et leur calendrier.", "Tables de facturation restaurées depuis E0001.");
        Communique(mep, go, "CHG-DEMO-0287", NiveauRisque.Eleve, "Traitements comptables plus tôt dans la nuit\nFactures disponibles dès 6 h",
            "service Comptabilité (fictif)", "ne pas lancer de facturation manuelle", 2, "Service Comptabilité (fictif)");
        Fiche(mep, bdd, "Tables de facturation", (Code("psql -h bdd-demo -U lecture -d compta -c \"SELECT count(*) FROM factures;\""), "Nombre de factures inchangé."));
        Fiche(mep, batch, "Calendrier comptable", ("Vérifier la planification dans l'ordonnanceur.", "FACT01 à 01:00, FACT03 à 03:00."));

        var eq = c.Equipe;
        var d = new Deroule(c.Resolutions, mep, go.AddDays(-6));
        d.Creer(eq.Alice);
        d.Plus(20).Approbateurs(eq.Alice, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Bob);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(17).Reussir(eq.Alice, e1, "factures.sql (2,4 Go) — fictif");
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(1).Demarrer(eq.Bob, e3);
        // Échec repris : la migration attend la fin d'un traitement qui verrouillait la table, puis est relancée avec succès.
        d.Plus(8).Echouer(eq.Alice, e2, "Erreur fictive : verrou sur la table factures pendant 6 minutes.");
        d.Plus(3).Reussir(eq.Bob, e3, "Version 7.0 déployée — fictif");
        d.Plus(4).Commenter(eq.Alice, e2, "Export comptable de la veille encore en cours : arrêt demandé, puis migration relancée.");
        d.Plus(3).Reprendre(eq.Alice, e2);
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(14).Reussir(eq.Alice, e2, "Migration 7.0 appliquée — fictif");
        d.Plus(1).Demarrer(eq.Bob, e4);
        d.Plus(5).Reussir(eq.Bob, e4, "Calendrier 7.0 actif — fictif");
        d.Plus(4).Recette(eq.Alice);
        d.Plus(3).Tester(eq.Alice, "Tables de facturation", 1, ResultatControle.Conforme, "Nombre inchangé.");
        d.Plus(2).Tester(eq.Bob, "Calendrier comptable", 1, ResultatControle.Conforme, "Planification conforme.");
        d.Plus(3).Cloturer(eq.Alice,
            "MEP terminée dans l'échéance malgré l'incident.",
            "E0002 en échec : verrou sur la table factures posé par l'export de la veille. Migration relancée après l'arrêt de l'export (12 minutes d'arrêt).",
            "Suspendre l'export comptable avant les prochaines migrations des tables de facturation.");
        return d.Fin();
    }

    // ---------- Prise de rendez-vous 2.1 : repliée ----------

    private static MepDemo RendezVous(Contexte c)
    {
        var (mep, alice, bob, _) = NouvelleMep(c, OutilHistoriqueRepliee, "2.0", "2.1",
            "Rappel des rendez-vous par SMS et créneaux du samedi.", "Prise de rendez-vous suspendue pendant la bascule (10 minutes).", 10,
            e => e.Bob, e => e.Alice);
        var go = c.Debut(-20);
        mep.EcheanceRepli = go.AddHours(3);
        var api = Sys(mep, c.Sys("API"), "Service rendez-vous 2.1", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Archive de `/opt/rendez-vous` (E0002).");
        var bdd = Sys(mep, c.Sys("BDD"), "Table des clients : consentement SMS", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde de la table `clients` (E0001).");
        Objets(mep, api, c.Sys("API"), "2.1.0", "application-prd.yml");
        Objets(mep, bdd, c.Sys("BDD"), "2.1.0", "clients");
        Responsables(mep, (bob, [api], "Service rendez-vous", DisponibiliteJourJ.Joignable), (alice, [bdd], "Base clients", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde de la table clients", [bdd], 10, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin -t clients rdv > /sauvegardes/clients-rdv.sql"), "Fichier présent, sans erreur."));
        var e2 = Sauvegarde(E(mep, 2, 0, "Sauvegarde du service rendez-vous", [api], 10, Criticite.Moderee,
            Sim + Code("tar czf /sauvegardes/rendez-vous-2.0.tgz /opt/rendez-vous"), "Archive créée."));
        var e3 = E(mep, 3, 1, "Ajout du consentement SMS", [bdd], 10, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d rdv -f migrations/2.1.sql"), "Colonne `consentement_sms` présente.", e1, e2);
        var e4 = E(mep, 4, 1, "Bascule du service rendez-vous 2.1", [api], 15, Criticite.Elevee,
            Sim + Code("cd /opt/rendez-vous && ./deploy.sh 2.1"), "Moins de 0,5 % d'erreurs sur les appels pendant 10 minutes.", e1, e2);
        E(mep, 5, 2, "Prise d'un rendez-vous de bout en bout", [api, bdd], 10, Criticite.Elevee,
            "Prendre un rendez-vous un samedi avec le client de test.", "Rendez-vous enregistré et SMS de rappel planifié.", e3, e4);
        var r1 = R(mep, 1, 0, "Réinstaller le service rendez-vous 2.0", [api], 10, Sim + Code("cd /opt/rendez-vous && ./deploy.sh 2.0"), "`GET /sante` renvoie la version 2.0.");
        var r2 = R(mep, 2, 0, "Retirer la colonne de consentement", [bdd], 5, Sim + Code("psql -h bdd-demo -U admin -d rdv -c \"ALTER TABLE clients DROP COLUMN consentement_sms;\""), "Colonne retirée.");
        var r3 = R(mep, 3, 1, "Vérifier une prise de rendez-vous", [api], 5, "Prendre un rendez-vous en semaine avec le client de test.", "Rendez-vous enregistré.", r1, r2);
        mep.PlanRepli = Repli(20, "Service rendez-vous 2.0.", "Colonne ajoutée retirée ; rendez-vous inchangés.");
        Communique(mep, go, "CHG-DEMO-0329", NiveauRisque.Modere, "Rappel des rendez-vous par SMS\nCréneaux du samedi",
            "conseillers des agences (fictif)", "ne pas prendre de rendez-vous pendant la bascule", 1);
        Fiche(mep, api, "Rendez-vous du samedi", ("Prendre un rendez-vous un samedi avec le client de test.", "Rendez-vous enregistré."));

        var eq = c.Equipe;
        var d = new Deroule(c.Resolutions, mep, go.AddDays(-8));
        d.Creer(eq.Bob);
        d.Plus(25).Approbateurs(eq.Bob, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Alice);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Bob, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(6).Reussir(eq.Bob, e2, "rendez-vous-2.0.tgz — fictif");
        d.Plus(2).Reussir(eq.Alice, e1, "clients-rdv.sql (640 Mo) — fictif");
        d.Plus(1).Demarrer(eq.Alice, e3);
        d.Plus(1).Demarrer(eq.Bob, e4);
        d.Plus(6).Reussir(eq.Alice, e3, "Colonne ajoutée — fictif");
        d.Plus(12).Echouer(eq.Bob, e4, "Erreur fictive : erreurs 502 au-delà de 2 % des appels.");
        d.Plus(4).DeclencherRepli(eq.Bob, "Taux d'erreur trop élevé et aucune résolution connue : repli décidé.");
        d.Plus(1).Demarrer(eq.Bob, r1);
        d.Plus(1).Demarrer(eq.Alice, r2);
        d.Plus(3).Reussir(eq.Alice, r2, "Colonne retirée — fictif");
        d.Plus(5).Reussir(eq.Bob, r1, "Version 2.0 de nouveau en ligne — fictif");
        d.Plus(1).Demarrer(eq.Bob, r3);
        d.Plus(4).Reussir(eq.Bob, r3, "Rendez-vous enregistré — fictif");
        d.Plus(3).Cloturer(eq.Bob,
            "Repli en 14 minutes, plus rapide que le test.",
            "E0004 en échec : erreurs 502 au-delà de 2 % des appels, dues à une limite de connexions du nouveau service.",
            "Relever la limite de connexions et faire un test de charge avant de reprogrammer la 2.1.");
        return d.Fin();
    }
}
