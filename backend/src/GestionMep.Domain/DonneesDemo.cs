namespace GestionMep.Domain;

/// <summary>MEP de démonstration reprise à l'identique du classeur Modele_MEP_test_navigation.xlsm.</summary>
public static class DonneesDemo
{
    public static Mep Creer(Guid? alice, Guid? bob, Guid? claire, DateTimeOffset maintenant)
    {
        var mep = new Mep
        {
            Outil = "Portail de démonstration",
            VersionActuelle = "1.0-démo",
            VersionCible = "2.0-démo",
            Objet = "Tester les boutons, filtres, dépendances et résolutions sans agir sur un système réel.",
            DateMep = DateOnly.FromDateTime(maintenant.UtcDateTime),
            FuseauHoraire = "Europe/Paris",
            // Comme l'Excel : échéance initialisée à demain 23:59.
            EcheanceRepli = new DateTimeOffset(maintenant.UtcDateTime.Date.AddDays(1).AddHours(23).AddMinutes(59), TimeSpan.Zero),
            CreeParUtilisateurId = alice,
        };

        var pAlice = Participant(mep, "Alice", "Pilote / BDD", "Fictif — poste A", alice);
        var pBob = Participant(mep, "Bob", "Opérateur API", "Fictif — poste B", bob);
        var pClaire = Participant(mep, "Claire", "Opératrice WEB", "Fictif — poste C", claire);
        mep.PiloteId = pAlice.Id;
        mep.SuppleantId = pBob.Id;

        var bdd = Systeme(mep, "BDD", "Base de test");
        var api = Systeme(mep, "API", "Service de test");
        var web = Systeme(mep, "WEB", "Interface de test");

        mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = "Package fictif", Version = "2.0-démo", Emplacement = "Aucun téléchargement à effectuer", SystemeId = api.Id });

        var e1 = Etape(mep, 1, "Sauvegarde fictive vérifiée", bdd, pAlice, Criticite.Elevee, EtatEtape.Reussie,
            "SIMULATION : lire la note « sauvegarde disponible ». Ne lancer aucune commande.",
            "La note indique sauvegarde disponible.", "Prérempli : contrôle fictif validé.");
        var e2 = Etape(mep, 2, "Vérifier le service API fictif", api, pBob, Criticite.Moderee, EtatEtape.AFaire,
            "SIMULATION : lire cette fiche et écrire « API démo OK » dans la preuve.", "La preuve contient API démo OK.", "", e1);
        var e3 = Etape(mep, 3, "Vérifier la page WEB fictive", web, pClaire, Criticite.Faible, EtatEtape.AFaire,
            "SIMULATION : lire cette fiche et écrire « WEB démo OK » dans la preuve.", "La preuve contient WEB démo OK.", "", e1);
        Etape(mep, 4, "Valider le parcours complet fictif", web, pClaire, Criticite.Elevee, EtatEtape.AFaire,
            "SIMULATION : attendre les deux préalables puis écrire « Parcours démo OK ».",
            "Les deux préalables sont réussis et la preuve est renseignée.", "", e2, e3);
        Etape(mep, 5, "Incident API simulé à corriger", api, pBob, Criticite.Moderee, EtatEtape.Echec,
            "SIMULATION : ouvrir la fiche de résolution. Ne modifier aucun service réel.",
            "La résolution et son contrôle final sont Conforme.", "Erreur fictive : réponse de test absente.", e1);

        mep.ProcedureRelueParId = bob;
        mep.ProcedureRelueLe = bob is null ? null : maintenant;
        return mep;
    }

    private static Participant Participant(Mep mep, string nom, string role, string contact, Guid? utilisateur)
    {
        var p = new Participant { MepId = mep.Id, Nom = nom, Role = role, Contact = contact, UtilisateurId = utilisateur };
        mep.Participants.Add(p);
        return p;
    }

    private static Systeme Systeme(Mep mep, string nom, string impact)
    {
        var s = new Systeme { MepId = mep.Id, Nom = nom, Cible = "Cible fictive, aucune connexion", Impact = impact };
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
