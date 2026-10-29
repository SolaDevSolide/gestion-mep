namespace GestionMep.Domain;

/// <summary>
/// Modèle de départ d'une connexion : ce qu'il remplit (méthode, format, authentification, message, déclencheurs suggérés),
/// tout restant modifiable. <paramref name="UrlSecrete"/> : l'adresse porte la clé (Teams Workflows, Slack).
/// </summary>
public record ModeleConnexionFourni(ModeleConnexion Modele, string Libelle, string Description, IReadOnlyList<string> Etiquettes,
    MethodeHttp Methode, FormatCorps Format, AuthentificationApi Authentification, bool UrlSecrete, bool Signature, string UrlExemple,
    string Corps, IReadOnlyList<string> Declencheurs, bool EnvoiManuel, string LibelleEnvoiManuel);

/// <summary>
/// Les modèles de départ proposés par « Nouvelle connexion ». Les connecteurs Office 365 de Teams (« Webhook entrant ») sont
/// retirés par Microsoft au profit des flux Workflows : le modèle Teams vise Workflows, qui attend une carte adaptative. Le
/// modèle « Outil de tickets » reste générique : l'adresse et les noms des champs sont à adapter à l'outil de l'entreprise.
/// </summary>
public static class ModelesConnexion
{
    public static readonly IReadOnlyList<ModeleConnexionFourni> Tous =
    [
        new(ModeleConnexion.Teams, "Microsoft Teams",
            "Carte dans un canal, par un flux Workflows « Lors de la réception d'une requête webhook ».",
            ["POST JSON", "adresse secrète"], MethodeHttp.Post, FormatCorps.Json, AuthentificationApi.Aucune, true, false,
            "https://prod-00.westeurope.logic.azure.com:443/workflows/…",
            """
            {
              "type": "message",
              "attachments": [
                {
                  "contentType": "application/vnd.microsoft.card.adaptive",
                  "content": {
                    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                    "type": "AdaptiveCard",
                    "version": "1.4",
                    "body": [
                      { "type": "TextBlock", "weight": "Bolder", "size": "Medium", "wrap": true,
                        "text": "{{essai}}{{evenement}} : {{application}} {{version_cible}}" },
                      { "type": "TextBlock", "wrap": true, "text": "{{evenement_detail}}" },
                      { "type": "TextBlock", "wrap": true, "text": "{{commentaire}}" },
                      { "type": "FactSet", "facts": [
                        { "title": "Pilote", "value": "{{pilote}}" },
                        { "title": "Avancement", "value": "{{avancement}}" },
                        { "title": "Fin estimée", "value": "{{fin_estimee}}" }
                      ] }
                    ],
                    "actions": [
                      { "type": "Action.OpenUrl", "title": "Ouvrir la MEP", "url": "{{lien_mep}}" }
                    ]
                  }
                }
              ]
            }
            """,
            ["gonogo.go", "etape.echec", "repli.declenche", "mep.cloturee"], true, "Prévenir le canal Teams"),
        new(ModeleConnexion.Slack, "Slack", "Message dans un canal, par un webhook entrant.",
            ["POST JSON", "adresse secrète"], MethodeHttp.Post, FormatCorps.Json, AuthentificationApi.Aucune, true, false,
            "https://hooks.slack.com/services/…",
            """
            {
              "text": "{{essai}}*{{evenement}}* : {{application}} {{version_cible}}\n{{evenement_detail}}\n{{commentaire}}\n<{{lien_mep}}|Ouvrir la MEP>"
            }
            """,
            ["gonogo.go", "repli.declenche", "mep.cloturee"], true, "Prévenir le canal Slack"),
        new(ModeleConnexion.WebhookJson, "Webhook JSON",
            "Toutes les informations de l'événement en JSON, signées pour que le destinataire vérifie l'origine.",
            ["POST JSON", "signature HMAC"], MethodeHttp.Post, FormatCorps.Json, AuthentificationApi.Aucune, false, true,
            "https://supervision.entreprise.fr/hooks/mep",
            """
            {
              "evenement": "{{evenement_cle}}",
              "libelle": "{{evenement}}",
              "quand": "{{quand_iso}}",
              "essai": "{{essai}}",
              "appel": "{{appel_id}}",
              "mep": {{donnees_json}}
            }
            """,
            [.. DeclencheursConnexion.Tous.Where(d => !d.Frequent).Select(d => d.Cle)], false, ""),
        new(ModeleConnexion.Tickets, "Outil de tickets",
            "Crée ou complète un ticket de changement avec un compte de service.",
            ["POST JSON", "identifiant et mot de passe"], MethodeHttp.Post, FormatCorps.Json, AuthentificationApi.Basic, false, false,
            "https://tickets.entreprise.fr/api/changements",
            """
            {
              "titre": "{{essai}}MEP {{application}} {{version_cible}} : {{evenement}}",
              "reference": "{{reference_demande}}",
              "description": "{{evenement_detail}}",
              "date": "{{quand_iso}}",
              "systemes": {{systemes}},
              "lien": "{{lien_mep}}"
            }
            """,
            ["gonogo.go", "mep.cloturee", "mep.annulee"], false, ""),
        new(ModeleConnexion.Libre, "Partir de zéro", "Requête vide : méthode, adresse, en-têtes et message à écrire.",
            [], MethodeHttp.Post, FormatCorps.Json, AuthentificationApi.Aucune, false, false, "", "{\n  \n}", [], false, ""),
    ];

    public static ModeleConnexionFourni Trouver(ModeleConnexion m) => Tous.First(x => x.Modele == m);
}
