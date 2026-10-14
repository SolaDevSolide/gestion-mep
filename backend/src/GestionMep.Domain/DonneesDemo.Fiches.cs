namespace GestionMep.Domain;

// Données de démonstration : fiches des systèmes du catalogue (environnements, catégories et objets connus) et objets
// impactés des MEP de démonstration, choisis dans ces fiches.
public static partial class DonneesDemo
{
    private static List<EnvironnementCatalogue> Environnements(string prefixe) =>
    [
        new() { Nom = "Développement", Cible = $"{prefixe}-dev" },
        new() { Nom = "Recette", Cible = $"{prefixe}-rec" },
        new() { Nom = "Préproduction", Cible = $"{prefixe}-pprd" },
        new() { Nom = "Production", Cible = $"{prefixe}-prd", Production = true },
    ];

    /// <summary>Remplit la fiche : catégories dans l'ordre donné, objets (type, nom, libellé, précision) de chacune.</summary>
    private static void RemplirFiche(SystemeCatalogue s, List<EnvironnementCatalogue> environnements,
        params (string Categorie, (string Type, string Nom, string Libelle, string Precision)[] Objets)[] categories)
    {
        s.Environnements = environnements;
        s.Categories = categories.Select(c => c.Categorie).ToList();
        s.Objets = categories.SelectMany(c => c.Objets.Select(o => new ObjetCatalogue
        {
            Categorie = c.Categorie, Type = o.Type, Nom = o.Nom, Libelle = o.Libelle, Precision = o.Precision,
        })).ToList();
    }

    /// <summary>Fiches de démonstration des six systèmes du catalogue (cibles fictives).</summary>
    private static void CompleterFiches(SystemeCatalogue bdd, SystemeCatalogue api, SystemeCatalogue web, SystemeCatalogue ldap, SystemeCatalogue mdm,
        SystemeCatalogue batch)
    {
        RemplirFiche(bdd, Environnements("bdd-demo"),
            ("Tables",
            [
                ("Table", "clients", "Fiches clients", "schéma public"),
                ("Table", "commandes", "Commandes des clients", "schéma public"),
                ("Table", "factures", "Factures émises", "schéma compta"),
                ("Table", "lignes_facture", "Lignes des factures", "schéma compta"),
                ("Table", "transactions", "Transactions de paiement", "schéma paiement"),
                ("Table", "conges", "Demandes de congés", "schéma rh"),
                ("Table", "messages", "Messages échangés", "schéma messagerie"),
                ("Table", "parametres", "Paramètres applicatifs", "schéma messagerie"),
                ("Table", "inventaire", "Inventaire des stocks", "schéma stocks"),
            ]),
            ("Vues",
            [
                ("Vue", "v_clients_actifs", "Clients ayant une commande en cours", ""),
                ("Vue", "v_factures_a_envoyer", "Factures à déposer sur la plateforme", "schéma compta"),
            ]),
            ("Procédures stockées",
            [
                ("Procédure", "p_recalcul_stocks", "Recalcul des stocks", "schéma stocks"),
                ("Procédure", "p_purge_journal", "Purge du journal de plus de 2 ans", ""),
            ]),
            ("Index",
            [
                ("Index", "ix_transactions_date", "Recherche des transactions par date", "schéma paiement"),
                ("Index", "ix_messages_texte", "Recherche plein texte des messages", "schéma messagerie"),
            ]),
            ("Sauvegarde / rollback", [("Script", "sauvegarde_complete.sh", "Sauvegarde pg_dump avant MEP", "")]));

        RemplirFiche(api, Environnements("srv-api"),
            ("Packages",
            [
                ("Archive", "commandes-api.jar", "Service commandes", "Java 21"),
                ("Archive", "annuaire-api.jar", "Service annuaire des agences", "Java 21"),
                ("Archive", "paiement-api.jar", "Service paiement", "Java 21"),
                ("Archive", "conges-api.jar", "Service congés", "Java 21"),
                ("Archive", "messagerie-api.jar", "Service messagerie", "Java 21"),
                ("Archive", "stocks-api.jar", "Service stocks", "Java 21"),
                ("Archive", "partenaires-api.jar", "Service partenaires", "Java 21"),
            ]),
            ("API",
            [
                ("Point d'accès", "/v2/commandes", "Lecture et création des commandes", ""),
                ("Point d'accès", "/v2/agences", "Recherche des agences", ""),
                ("Point d'accès", "/v1/paiements", "Paiements par carte et mobile", ""),
                ("Point d'accès", "/v1/conges", "Demandes de congés", ""),
            ]),
            ("Configuration", [("Fichier", "application-prd.yml", "Paramètres de production", "")]),
            ("Certificats", [("Certificat", "api.exemple.invalid", "Certificat TLS du service", "fictif")]));

        RemplirFiche(web, Environnements("srv-web"),
            ("Code source", [("Dépôt", "portail-web", "Portail client (front)", "")]),
            ("Packages",
            [
                ("Archive", "portail-web.tar.gz", "Fichiers statiques du portail", ""),
                ("Archive", "factures-pdf.tar.gz", "Module de téléchargement des factures", ""),
                ("Archive", "agences-web.tar.gz", "Page « Trouver une agence »", ""),
                ("Archive", "paiement-web.tar.gz", "Écran de paiement", ""),
                ("Archive", "rh-web.tar.gz", "Portail RH", ""),
                ("Archive", "messagerie-web.tar.gz", "Client web de messagerie", ""),
                ("Archive", "extranet-web.tar.gz", "Extranet partenaires", ""),
            ]),
            ("Configuration", [("Fichier", "nginx/portail.conf", "Hôte virtuel du portail", "")]));

        RemplirFiche(ldap, [new() { Nom = "Recette", Cible = "ldap-demo-rec" }, new() { Nom = "Production", Cible = "ldap-demo-prd", Production = true }],
            ("Sécurité",
            [
                ("Groupe", "cn=app-commandes", "Comptes de l'application commandes", ""),
                ("Groupe", "cn=agences-lecture", "Lecture de l'annuaire des agences", ""),
                ("Groupe", "cn=rh-gestionnaires", "Gestionnaires RH", ""),
                ("Groupe", "cn=partenaires", "Comptes des partenaires", ""),
            ]),
            ("Référentiels",
            [
                ("Branche", "ou=agences", "Agences et leurs horaires", ""),
                ("Branche", "ou=partenaires", "Partenaires de l'extranet", ""),
            ]));

        RemplirFiche(mdm, Environnements("mdm-demo"),
            ("Flux",
            [
                ("Flux", "flux-commandes", "Commandes poussées vers le MDM", "temps réel"),
                ("Flux", "flux-moyens-paiement", "Référentiel des moyens de paiement", "quotidien"),
            ]),
            ("Interfaces", [("Interface", "mdm-rest", "Interface REST du MDM", "")]));

        RemplirFiche(batch, Environnements("srv-batch"),
            ("Ordonnancement",
            [
                ("Chaîne", "FACT01", "Facturation mensuelle", "chaîne de nuit"),
                ("Chaîne", "FACT02", "Avoirs et régularisations", "chaîne de nuit"),
                ("Job", "FACT03", "Génération Factur-X", "après FACT01"),
                ("Job", "FACT04", "Dépôt sur la plateforme", "après FACT03"),
                ("Job", "FACT05", "Accusés de réception", "toutes les heures"),
                ("Job", "FACT06", "Relance des rejets", "quotidien 07:00"),
                ("Chaîne", "EXPCLI", "Export des clients", "quotidien 02:00"),
                ("Job", "EXPCTR", "Export des contrats", "quotidien 02:30"),
                ("Job", "IMPTAR", "Import des tarifs", "hebdomadaire"),
                ("Job", "PURJRN", "Purge des journaux", "mensuel"),
                ("Job", "STATMO", "Statistiques du mois", "mensuel"),
                ("Job", "ARCH01", "Archivage légal", "mensuel"),
                ("Job", "SAUV01", "Sauvegarde des fichiers", "quotidien 23:00"),
                ("Job", "CTLSLD", "Contrôle des soldes", "quotidien 05:00"),
                ("Job", "NOTIFC", "Notifications clients", "toutes les 15 min"),
                ("Job", "RAPPRO", "Rapprochement bancaire", "quotidien 06:00"),
                ("Job", "EDTPDF", "Édition des PDF", "après FACT03"),
                ("Job", "ENVMAI", "Envoi des courriels", "après EDTPDF"),
                ("Job", "STKREA", "Calcul des réassorts", "quotidien 01:00"),
            ]),
            ("Fichiers d'échange",
            [
                ("Fichier", "EXPCLI_AAAAMMJJ.csv", "Export clients vers le partenaire", ""),
                ("Fichier", "FACTX_AAAAMMJJ.zip", "Lot Factur-X vers la plateforme", ""),
                ("Fichier", "AR_PLATEFORME.xml", "Accusés de réception de la plateforme", ""),
                ("Fichier", "TARIFS.csv", "Tarifs du fournisseur", ""),
            ]),
            ("Packages",
            [
                ("Archive", "batch-facturation.jar", "Traitements de facturation", "Java 21"),
                ("Archive", "batch-exports.jar", "Exports partenaires", "Java 21"),
                ("Archive", "batch-stocks.jar", "Traitements des stocks", "Java 21"),
                ("Archive", "batch-commun.jar", "Bibliothèque commune", "Java 21"),
            ]));
    }

    /// <summary>
    /// Objets impactés d'un système de la MEP : objets de la fiche du catalogue désignés par leur nom, tous avec la même
    /// version (ou correction).
    /// </summary>
    private static void Objets(Mep mep, Systeme systeme, SystemeCatalogue fiche, string version, params string[] noms)
    {
        foreach (var nom in noms)
        {
            var o = fiche.Objets.Single(x => x.Nom == nom);
            mep.ObjetsImpactes.Add(new ObjetImpacte
            {
                MepId = mep.Id, SystemeId = systeme.Id, ObjetCatalogueId = o.Id, Categorie = o.Categorie, Type = o.Type, Nom = o.Nom,
                Libelle = o.Libelle, Precision = o.Precision, Version = version,
            });
        }
    }

    /// <summary>Environnements de la MEP sur un système, pris dans la fiche du catalogue par leur nom.</summary>
    private static void EnvironnementsDe(Systeme systeme, SystemeCatalogue fiche, params string[] noms) =>
        systeme.Environnements = noms.Select(n => fiche.Environnements.Single(e => e.Nom == n))
            .Select(e => new EnvironnementSysteme { Nom = e.Nom, Production = e.Production }).ToList();

    /// <summary>Environnements d'une étape : pour chaque système désigné, les environnements nommés.</summary>
    private static void EnvironnementsEtape(Etape etape, params (Systeme Systeme, string[] Noms)[] environnements) =>
        etape.Environnements = environnements.SelectMany(x => x.Noms.Select(n => new EnvironnementEtape { SystemeId = x.Systeme.Id, Nom = n })).ToList();

    /// <summary>Par défaut, une étape agit sur tous les environnements choisis pour chacun de ses systèmes.</summary>
    private static List<EnvironnementEtape> EnvironnementsParDefaut(IEnumerable<Systeme> systemes) =>
        systemes.SelectMany(s => s.Environnements.Select(e => new EnvironnementEtape { SystemeId = s.Id, Nom = e.Nom })).ToList();
}
