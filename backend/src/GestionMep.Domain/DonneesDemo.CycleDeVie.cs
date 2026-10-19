namespace GestionMep.Domain;

// Données de démonstration : sorties du cycle de vie (#93) — une MEP reportée, une MEP annulée après un Go et une MEP clôturée
// en échec après un retour arrière incomplet et un pilotage de secours.
public static partial class DonneesDemo
{
    // ---------- 8. Reportée ----------

    /// <summary>
    /// Préparation complète et approuvée, puis no-go la veille avec report : gel des changements pendant la clôture comptable.
    /// Alice pilote, Bob (suppléant) a reporté la MEP à dans neuf jours ; la préparation reste ouverte.
    /// </summary>
    private static MepDemo Reportee(Contexte c)
    {
        var (mep, alice, bob, claire) = NouvelleMep(c, OutilReportee, "1.6", "1.7",
            "Saisie des notes de frais depuis le téléphone et justificatifs photographiés.", "Saisie des notes de frais indisponible 20 minutes.", 20,
            e => e.Alice, e => e.Bob);
        var bdd = Sys(mep, c.Sys("BDD"), "Tables des notes de frais : justificatifs", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep,
            "Sauvegarde du schéma `frais` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service notes de frais 1.7", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état, la version 1.6 reste dans le dépôt d'artefacts.");
        var web = Sys(mep, c.Sys("WEB"), "Écran de saisie mobile", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Page republiée depuis le dépôt.");
        Responsables(mep,
            (bob, [api, bdd], "Service et tables des notes de frais", DisponibiliteJourJ.Joignable),
            (claire, [web], "Écran de saisie mobile", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde du schéma frais", [bdd], 10, Criticite.Moderee,
            Sim + Code("pg_dump -h bdd-demo -U admin -n frais compta > /sauvegardes/frais.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration des tables de justificatifs", [bdd], 15, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d compta -f migrations/frais-1.7.sql"), "Table `justificatifs` présente.", e1);
        var e3 = E(mep, 3, 2, "Déploiement du service notes de frais 1.7", [api], 15, Criticite.Elevee,
            Sim + Code("cd /opt/frais && ./deploy.sh 1.7"), "`GET /sante` renvoie 200 avec la version 1.7.", e2);
        E(mep, 4, 3, "Publication de l'écran mobile", [web], 10, Criticite.Moderee,
            Sim + Code("./publier.sh frais-mobile 1.7"), "L'écran « Nouvelle note » propose « Photographier le justificatif ».", e3);
        var r1 = R(mep, 1, 0, "Réinstaller le service 1.6", [api], 10, Sim + Code("cd /opt/frais && ./deploy.sh 1.6"), "`GET /sante` renvoie la version 1.6.");
        R(mep, 2, 1, "Restaurer le schéma frais", [bdd], 15, Sim + Code("psql -h bdd-demo -U admin compta < /sauvegardes/frais.sql"),
            "Restauration sans erreur.", r1);
        mep.PlanRepli = Repli(25, "Service notes de frais 1.6.", "Schéma `frais` restauré depuis E0001.");
        _ = alice;

        Fiche(mep, web, "Saisie mobile", ("Créer une note de frais depuis un téléphone et photographier un ticket.", "La note est enregistrée avec son justificatif."));
        Fiche(mep, api, "API notes de frais", (Code("curl -fsS https://frais.exemple.invalid/sante"), "Réponse 200, version 1.7."));

        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0415",
            NiveauRisque = NiveauRisque.Faible,
            Benefices = "Notes de frais saisies depuis le téléphone\nJustificatifs photographiés, plus de scan",
            UtilisateursImpactes = "tous les salariés (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "enregistrer les notes de frais en cours",
            // Créneau annoncé à la date visée par le report.
            HeureLimite = new TimeOnly(19, 30),
            DateAnnoncee = Jour(c.Maintenant.AddDays(9)),
            HeureDebutAnnoncee = new TimeOnly(20, 0),
            HeureFinAnnoncee = new TimeOnly(21, 0),
        };

        var eq = c.Equipe;
        var d = new Deroule(mep, c.Maintenant.AddDays(-8));
        d.Creer(eq.Alice);
        d.Plus(20).Approbateurs(eq.Alice, eq.Claire);
        d.A(c.Maintenant.AddDays(-3)).Relire(eq.Bob);
        d.A(c.Maintenant.AddDays(-2)).Approuver(eq.Claire, "");
        d.A(c.Maintenant.AddDays(-1).AddHours(-2)).NoGoEtReporter(eq.Bob,
            "Gel des changements pendant la clôture comptable de fin de mois : créneau refusé par la production.", Jour(c.Maintenant.AddDays(9)));
        return d.Fin();
    }

    // ---------- 9. Annulée après un Go ----------

    /// <summary>
    /// Go il y a quatre jours, deux étapes réussies, déploiement du service en échec (certificat du prestataire expiré), retour
    /// en préparation, puis annulation le lendemain : le prestataire change. L'état laissé en production est décrit.
    /// </summary>
    private static MepDemo Annulee(Contexte c)
    {
        var (mep, _, bob, claire) = NouvelleMep(c, OutilAnnulee, "2.1", "2.2",
            "Signature électronique des contrats depuis l'espace client, avec horodatage qualifié.", "Signature des contrats indisponible 15 minutes.", 15,
            e => e.Alice, e => e.Bob);
        var go = c.Debut(-4);
        mep.EcheanceRepli = go.AddHours(4);
        var bdd = Sys(mep, c.Sys("BDD"), "Tables des signatures", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep, "Sauvegarde du schéma `signature` (E0001).");
        var api = Sys(mep, c.Sys("API"), "Service signature 2.2", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Service sans état, la version 2.1 reste dans le dépôt d'artefacts.");
        var web = Sys(mep, c.Sys("WEB"), "Parcours de signature", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Page republiée depuis le dépôt.");
        Objets(mep, api, c.Sys("API"), "2.2.0", "api.exemple.invalid");
        Responsables(mep,
            (bob, [api, bdd], "Service et tables de signature", DisponibiliteJourJ.Joignable),
            (claire, [web], "Parcours de signature", DisponibiliteJourJ.Joignable));

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde du schéma signature", [bdd], 10, Criticite.Moderee,
            Sim + Code("pg_dump -h bdd-demo -U admin -n signature contrats > /sauvegardes/signature.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration des tables de signature", [bdd], 15, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d contrats -f migrations/signature-2.2.sql"), "Colonne `horodatage_qualifie` présente.", e1);
        var e3 = E(mep, 3, 2, "Déploiement du service signature 2.2", [api], 20, Criticite.Elevee,
            Sim + Code("cd /opt/signature && ./deploy.sh 2.2"), "`GET /sante` renvoie 200 et le prestataire répond.", e2);
        var e4 = E(mep, 4, 3, "Publication du parcours de signature", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh signature 2.2"), "Le bouton « Signer en ligne » s'affiche sur les contrats.", e3);
        E(mep, 5, 4, "Signature d'un contrat de test", [web, api], 10, Criticite.Elevee,
            "Signer le contrat de test depuis l'espace client fictif.", "Contrat signé et horodaté.", e4);
        var r1 = R(mep, 1, 0, "Réinstaller le service signature 2.1", [api], 10, Sim + Code("cd /opt/signature && ./deploy.sh 2.1"),
            "`GET /sante` renvoie la version 2.1.");
        R(mep, 2, 1, "Restaurer le schéma signature", [bdd], 15, Sim + Code("psql -h bdd-demo -U admin contrats < /sauvegardes/signature.sql"),
            "Restauration sans erreur.", r1);
        mep.PlanRepli = Repli(25, "Service signature 2.1.", "Schéma `signature` restauré depuis E0001 si besoin.");

        Fiche(mep, web, "Signature en ligne", ("Signer le contrat de test depuis l'espace client.", "Le contrat signé est téléchargeable."));
        Fiche(mep, api, "API signature", (Code("curl -fsS https://signature.exemple.invalid/sante"), "Réponse 200, prestataire joignable."));

        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0402",
            NiveauRisque = NiveauRisque.Modere,
            Benefices = "Contrats signés en ligne, sans impression\nHorodatage qualifié des signatures",
            UtilisateursImpactes = "clients et conseillers (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "terminer les signatures en cours",
            HeureLimite = Heure(go.AddMinutes(-30)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(2)),
        };

        var eq = c.Equipe;
        var d = new Deroule(mep, go.AddDays(-6));
        d.Creer(eq.Alice);
        d.Plus(15).Approbateurs(eq.Alice, eq.Claire);
        d.A(go.AddDays(-3)).Relire(eq.Bob);
        d.A(go.AddDays(-2)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Alice, c.Criteres);
        d.Plus(2).Demarrer(eq.Alice, e1);
        d.Plus(8).Reussir(eq.Alice, e1, "signature.sql (800 Mo) — fictif");
        d.Plus(1).Demarrer(eq.Alice, e2);
        d.Plus(12).Reussir(eq.Alice, e2, "Migration 2.2 appliquée — fictif");
        d.Plus(1).Demarrer(eq.Bob, e3);
        d.Plus(10).Echouer(eq.Bob, e3, "Erreur fictive : le prestataire de signature refuse la connexion (certificat expiré la veille).");
        d.Plus(15).RevenirEnPreparation(eq.Alice,
            "Certificat du prestataire expiré : pas de nouveau certificat avant lundi, reprise impossible ce soir.");
        d.A(go.AddDays(1).AddHours(-5)).Annuler(eq.Alice, estAdmin: true,
            "Le prestataire de signature change au 1er janvier : la version 2.2 est abandonnée, une nouvelle MEP sera préparée pour le nouveau prestataire.",
            "Tables de signature migrées en 2.2 (E0002), compatibles avec le service 2.1 toujours en ligne ; sauvegarde E0001 conservée 30 jours.");
        return d.Fin();
    }

    // ---------- 10. Clôturée en échec ----------

    /// <summary>
    /// MEP d'il y a trois jours pilotée par Bob, sans suppléant : nouvelle chaîne d'archivage en échec, retour arrière déclenché,
    /// restauration de la base en échec (sauvegarde incomplète). Bob injoignable : Alice (administratrice) prend le pilotage de
    /// secours, réinstalle la chaîne 1.4 et clôture la MEP en échec, service dégradé, avec un incident ouvert.
    /// </summary>
    private static MepDemo Echec(Contexte c)
    {
        var (mep, alice, bob, claire) = NouvelleMep(c, OutilEchec, "1.4", "2.0",
            "Archivage légal des courriers au format PDF/A et index de recherche des archives.", "Consultation des archives indisponible 30 minutes.", 30,
            e => e.Bob);
        var go = c.Debut(-3);
        mep.EcheanceRepli = go.AddHours(6);
        var bdd = Sys(mep, c.Sys("BDD"), "Index des courriers archivés", StatutSauvegarde.Requise, ModeSauvegarde.PendantMep,
            "Sauvegarde du schéma `archives` (E0001).");
        var batch = Sys(mep, c.Sys("BATCH"), "Chaîne d'archivage PDF/A", StatutSauvegarde.Requise, ModeSauvegarde.AvantMep,
            "Export de la planification la veille (`archivage-1.4.json`).");
        var web = Sys(mep, c.Sys("WEB"), "Consultation des archives", StatutSauvegarde.NonRequise, ModeSauvegarde.AvantMep,
            justification: "Page republiée depuis le dépôt.");
        Objets(mep, batch, c.Sys("BATCH"), "2.0.0", "ARCH01");
        Responsables(mep,
            (bob, [batch, bdd], "Chaîne d'archivage et index", DisponibiliteJourJ.Joignable),
            (claire, [web], "Consultation des archives", DisponibiliteJourJ.Joignable));
        _ = alice;

        var e1 = Sauvegarde(E(mep, 1, 0, "Sauvegarde du schéma archives", [bdd], 20, Criticite.Elevee,
            Sim + Code("pg_dump -h bdd-demo -U admin -n archives courrier > /sauvegardes/archives.sql"), "Fichier présent, sans erreur."));
        var e2 = E(mep, 2, 1, "Migration de l'index des archives", [bdd], 25, Criticite.Elevee,
            Sim + Code("psql -h bdd-demo -U admin -d courrier -f migrations/archives-2.0.sql"), "Table `courriers_index` au nouveau format.", e1);
        var e3 = E(mep, 3, 2, "Déploiement de la chaîne d'archivage 2.0", [batch], 20, Criticite.Elevee,
            Sim + Code("ordonnanceur deployer ARCH01 --version 2.0"), "La chaîne ARCH01 archive le lot de test sans rejet.", e2);
        var e4 = E(mep, 4, 3, "Publication de la consultation des archives", [web], 15, Criticite.Moderee,
            Sim + Code("./publier.sh archives 2.0"), "La recherche d'un courrier archivé répond en moins de 2 s.", e3);
        var r1 = R(mep, 1, 0, "Restaurer le schéma archives", [bdd], 30, Sim + Code("psql -h bdd-demo -U admin courrier < /sauvegardes/archives.sql"),
            "Restauration sans erreur.");
        var r2 = R(mep, 2, 0, "Réinstaller la chaîne d'archivage 1.4", [batch], 15, Sim + Code("ordonnanceur importer archivage-1.4.json"),
            "Chaîne ARCH01 1.4 planifiée.");
        R(mep, 3, 1, "Vérifier la consultation d'un courrier", [web, bdd], 10, "Rechercher le courrier de test dans la consultation des archives.",
            "Le courrier s'affiche.", r1, r2);
        mep.PlanRepli = Repli(55, "Chaîne d'archivage 1.4.", "Schéma `archives` restauré depuis E0001.");
        mep.PlanRepli.SiRepliEchoue = "Laisser la consultation en lecture seule, suspendre l'archivage de nuit et ouvrir un incident auprès de l'équipe "
            + "base de données (astreinte fictive, poste 01 00 00 00 99).";

        Fiche(mep, web, "Consultation des archives", ("Rechercher le courrier de test « ARC-2026-0001 ».", "Le courrier s'affiche en moins de 2 s."));
        Fiche(mep, batch, "Archivage de nuit", ("Lancer ARCH01 sur le lot de test.", "Lot archivé sans rejet."));

        mep.Communication = new Communication
        {
            ReferenceDemande = "CHG-DEMO-0407",
            NiveauRisque = NiveauRisque.Eleve,
            Benefices = "Courriers archivés au format légal PDF/A\nRecherche plein texte dans les archives",
            UtilisateursImpactes = "service Courrier et conseillers (fictif)",
            ArretApplicatif = true,
            ActionUtilisateurs = "ne pas lancer de recherche dans les archives pendant la MEP",
            HeureLimite = Heure(go.AddMinutes(-15)),
            DateAnnoncee = Jour(go),
            HeureDebutAnnoncee = Heure(go),
            HeureFinAnnoncee = Heure(go.AddHours(3)),
        };

        var eq = c.Equipe;
        var d = new Deroule(mep, go.AddDays(-7));
        d.Creer(eq.Bob);
        d.Plus(20).Approbateurs(eq.Bob, eq.Claire);
        d.A(go.AddDays(-2)).Relire(eq.Alice);
        d.A(go.AddDays(-1)).Approuver(eq.Claire, "");
        d.A(go).Go(eq.Bob, c.Criteres);
        d.Plus(2).Demarrer(eq.Bob, e1);
        d.Plus(18).Reussir(eq.Bob, e1, "archives.sql (4,1 Go) — fictif");
        d.Plus(1).Demarrer(eq.Bob, e2);
        d.Plus(24).Reussir(eq.Bob, e2, "Index migré — fictif");
        d.Plus(1).Demarrer(eq.Bob, e3);
        d.Plus(16).Echouer(eq.Bob, e3, "Erreur fictive : ARCH01 rejette 30 % du lot de test (format PDF/A-3 non reconnu).");
        d.Plus(6).DeclencherRepli(eq.Bob, "Rejets massifs et aucune résolution connue : retour arrière.");
        d.Plus(2).Demarrer(eq.Bob, r1);
        d.Plus(28).Echouer(eq.Bob, r1, "Erreur fictive : restauration interrompue, la sauvegarde E0001 ne contient pas la table `courriers_index`.");
        d.Plus(55).PilotageDeSecours(eq.Alice,
            "Bob injoignable depuis 21:30 (téléphone coupé) ; R0001 en échec, décision nécessaire avant l'ouverture du service à 07:00.");
        d.Plus(3).Demarrer(eq.Alice, r2);
        d.Plus(12).Reussir(eq.Alice, r2, "Chaîne ARCH01 1.4 replanifiée — fictif");
        d.Plus(20).CloturerEnEchec(eq.Alice,
            "Consultation des archives en lecture seule sur l'index 2.0, archivage de nuit suspendu.", "INC-DEMO-1042",
            "Sauvegarde et migration de l'index dans les temps. Chaîne 1.4 réinstallée en 12 minutes par le pilotage de secours.",
            "E0003 : la chaîne 2.0 rejette les courriers PDF/A-3. R0001 : sauvegarde E0001 incomplète (table `courriers_index` absente), "
                + "restauration impossible. Pilote injoignable pendant une heure.",
            "Reconstruire l'index depuis les courriers (incident INC-DEMO-1042). Vérifier le contenu des sauvegardes avant le Go. "
                + "Nommer un suppléant pour les MEP de nuit.");
        _ = e4;
        return d.Fin();
    }
}
