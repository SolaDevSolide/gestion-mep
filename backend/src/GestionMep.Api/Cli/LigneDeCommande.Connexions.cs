using System.Globalization;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>
/// « gestion-mep connexions … » : connexions aux API externes (#111), avec les mêmes règles et le même journal
/// d'administration que la console (auteur « Ligne de commande »). Création et modification par un fichier JSON au format
/// de l'export (le modèle a trop de champs pour des options) ; secrets uniquement par l'entrée standard.
/// </summary>
public static partial class LigneDeCommande
{
    /// <summary>Valeur d'un secret dans un export : à remplacer par « connexions secret » (jamais la valeur).</summary>
    public const string SecretASaisir = "<à saisir>";
    private const string FormatExportConnexions = "gestion-mep/connexions";

    // Propriétés plutôt que champs : le registre (LigneDeCommande.Commandes.cs) les lit pendant son initialisation, et l'ordre
    // d'initialisation des champs statiques d'une classe partielle répartie sur plusieurs fichiers n'est pas garanti.
    private static GroupeCommandes GroupeConnexions => new("connexions", "Connexions aux API externes",
        "Une connexion se désigne par son nom (guillemets s'il contient des espaces) ou son identifiant (8 premiers caractères suffisent). "
        + "Les secrets ne passent jamais en argument : --secret-stdin.");

    private static Commande[] CommandesConnexions =>
    [
        new("connexions lister", "", "Connexions : état, dernier appel, déclencheurs", a => { a.Rien(0); return ConnexionsLister(); })
        {
            Json = "tableau des connexions, comme GET /api/admin/connexions",
            Exemples = ["gestion-mep connexions lister", "gestion-mep connexions lister --json"],
        },
        new("connexions afficher", "<connexion>", "Réglages d'une connexion ; secrets : « enregistré », « absent » ou « illisible », jamais leur valeur",
            a => { a.Rien(1); return ConnexionsAfficher(a.Arg(0, "<connexion>")); })
        {
            Json = "la connexion, comme GET /api/admin/connexions/{id}",
            Exemples = ["gestion-mep connexions afficher \"Canal Teams Exploitation\""],
        },
        new("connexions exporter", "[<connexion>] [fichier.json|-]", "Exporte une connexion (ou toutes) en JSON, sans aucun secret",
            a => ConnexionsExporter(a.Args))
        {
            Details = $"Chaque secret est remplacé par « {SecretASaisir} ». Le fichier se réimporte sur un autre serveur, puis les secrets se saisissent avec « connexions secret ».",
            Exemples = ["gestion-mep connexions exporter connexions.json", "gestion-mep connexions exporter \"Tickets de changement\" -"],
        },
        new("connexions importer", "<fichier.json|-> [--remplacer|--ignorer-existantes] [--essai]",
            "Crée ou met à jour des connexions depuis un fichier au format de l'export", a => ConnexionsImporter(a.Arg(0, "<fichier.json|->"), a.Depuis(1)))
        {
            Options = [("--remplacer", "Met à jour les connexions de même nom (secrets gardés si l'adresse garde son domaine)"),
                ("--ignorer-existantes", "Laisse telles quelles les connexions de même nom"), ("--essai", "Vérifie tout sans rien enregistrer")],
            Details = "Une connexion dont un secret manque est créée désactivée ; la commande dit quoi saisir. Une erreur sur une connexion annule tout l'import.",
            Json = "resultat : { creees, modifiees, ignorees, secretsASaisir }",
            Exemples = ["gestion-mep connexions importer connexions.json --essai", "gestion-mep connexions importer connexions.json --remplacer"],
        },
        new("connexions secret", "<connexion> --champ authentification|adresse|hmac|entete:<Nom> (--secret-stdin|--effacer)",
            "Remplace (ou efface) un secret de la connexion, lu sur l'entrée standard", a => ConnexionsSecret(a.Arg(0, "<connexion>"), a.Depuis(1)))
        {
            Options = [("--champ authentification", "Jeton, mot de passe, clé ou secret client, selon l'authentification"),
                ("--champ adresse", "Adresse complète secrète (Teams Workflows, Slack) ; un autre domaine efface les autres secrets"),
                ("--champ hmac", "Secret de la signature X-Gestion-Mep-Signature"), ("--champ entete:<Nom>", "En-tête secret (créé s'il n'existe pas)"),
                ("--secret-stdin", "Lit le secret sur l'entrée standard (ou le demande au terminal, sans l'afficher) ; jamais en argument"),
                ("--effacer", "Retire la signature ou l'en-tête secret")],
            Exemples = ["gestion-mep connexions secret \"Tickets de changement\" --champ authentification --secret-stdin < mot-de-passe.txt",
                "gestion-mep connexions secret \"Canal Teams Exploitation\" --champ adresse --secret-stdin < adresse.txt"],
        },
        new("connexions activer", "<connexion>", "Active la connexion (tous ses secrets doivent être saisis)", a => { a.Rien(1); return ConnexionsChangerEtat(a.Arg(0, "<connexion>"), true); })
        {
            Exemples = ["gestion-mep connexions activer \"Tickets de changement\""],
        },
        new("connexions desactiver", "<connexion>", "Désactive la connexion : plus aucun appel ne part", a => { a.Rien(1); return ConnexionsChangerEtat(a.Arg(0, "<connexion>"), false); })
        {
            Exemples = ["gestion-mep connexions desactiver \"Tickets de changement\""],
        },
        new("connexions essayer", "<connexion> [--mep <id>] [--evenement <clé>] [--commentaire \"…\"] [--apercu]",
            "Envoie vraiment le message (précédé de « [Essai] ») et montre la réponse ; --apercu le rend sans l'envoyer",
            a => ConnexionsEssayer(a.Arg(0, "<connexion>"), a.Depuis(1)))
        {
            Options = [("--mep <id>", "MEP dont prendre les informations (sinon des valeurs d'exemple)"),
                ("--evenement <clé>", "Événement : etape.echec, gonogo.go, envoi.manuel… (défaut : le dernier du journal de la MEP)"),
                ("--commentaire \"…\"", "Valeur de {{commentaire}}"), ("--apercu", "Montre la requête sans l'envoyer")],
            Details = "Code de retour 1 si l'outil répond en erreur ou ne répond pas.",
            Json = "--apercu : la requête, comme POST /api/admin/connexions/apercu ; sinon resultat : comme POST /api/admin/connexions/essai",
            Exemples = ["gestion-mep connexions essayer \"Canal Teams Exploitation\" --mep 3f2a9c1e --evenement etape.echec",
                "gestion-mep connexions essayer Supervision --apercu"],
        },
        new("connexions appels", "[<connexion>] [--echecs] [--depuis AAAA-MM-JJ] [--limite n]", "Derniers appels, du plus récent au plus ancien",
            a => ConnexionsAppels(a.Args))
        {
            Options = [("--echecs", "Seulement les échecs définitifs"), ("--depuis AAAA-MM-JJ", "Appels de ce jour et après"), ("--limite n", "Nombre d'appels (défaut 50, 500 au plus)")],
            Json = "tableau des appels, comme GET /api/admin/connexions/appels",
            Exemples = ["gestion-mep connexions appels --echecs", "gestion-mep connexions appels \"Canal Teams Exploitation\" --limite 10"],
        },
        new("connexions renvoyer", "<appel>", "Renvoie un appel terminé avec le même message (le serveur l'envoie dans les secondes qui suivent)",
            a => { a.Rien(1); return ConnexionsRenvoyer(a.Arg(0, "<appel>")); })
        {
            Json = "resultat : { appelId }",
            Exemples = ["gestion-mep connexions renvoyer 6c1e2f0a"],
        },
        new("connexions supprimer", "<connexion> [--oui]", "Supprime la connexion et son historique d'appels", a => ConnexionsSupprimer(a.Arg(0, "<connexion>"), a.Depuis(1)))
        {
            Destructive = true,
            Options = [OptionOui],
            Exemples = ["gestion-mep connexions supprimer \"Slack astreinte\" --oui"],
        },
    ];

    private sealed record FichierConnexions(string Format, int Version, DateTimeOffset ExporteLe, List<ConnexionApiSaisie> Connexions);

    private static ServiceConnexions ServiceConnexionsCli(MepDbContext db, IConfiguration config) =>
        new(db, config, TimeProvider.System, new SecretsConnexion(Cles(config)), new SignalAppels());

    private static async Task<ConnexionApi> TrouverConnexion(MepDbContext db, string texte)
    {
        var toutes = await db.ConnexionsApi.ToListAsync();
        if (toutes.FirstOrDefault(c => c.Nom.Equals(texte.Trim(), StringComparison.OrdinalIgnoreCase)) is { } parNom) return parNom;
        if (IdentifiantOuPrefixe(texte, toutes.Select(c => c.Id), "connexions") is { } id && toutes.FirstOrDefault(c => c.Id == id) is { } parId) return parId;
        throw ErreurHttp.NonTrouve($"Connexion introuvable : « {texte} » (voir « gestion-mep connexions lister »).");
    }

    private static string LibelleEtat(EtatConnexion e) => e switch
    {
        EtatConnexion.Active => "Active",
        EtatConnexion.Desactivee => "Désactivée",
        EtatConnexion.EnErreur => "En erreur",
        _ => "Jamais appelée",
    };

    private static string LibelleAppel(AppelResumeDto a) =>
        $"{Date(a.CreeLe)} {a.Etat switch { EtatAppel.Reussi => "réussi", EtatAppel.Echec => "échec", EtatAppel.EnCours => "en cours", _ => "en attente" }}"
        + (a.Code is { } c ? $" ({c})" : "");

    private static async Task<int> ConnexionsLister()
    {
        var config = Configuration();
        await using var db = Base(config);
        var liste = await ServiceConnexionsCli(db, config).Lister();
        if (EnJson)
        {
            EcrireJson(liste);
            return Codes.Succes;
        }
        if (liste.Count == 0)
        {
            Ecrire("Aucune connexion. Créer : console d'administration › API externes, ou « gestion-mep connexions importer ».");
            return Codes.Succes;
        }
        var largeur = Math.Max(3, liste.Max(c => c.Nom.Length));
        Ecrire($"{"ID",-8}  {"NOM".PadRight(largeur)}  {"ÉTAT",-14}  {"DERNIER APPEL",-30}  DÉCLENCHEURS");
        foreach (var c in liste)
        {
            var declencheurs = c.Declencheurs.Select(DeclencheursConnexion.Libelle).Concat(c.EnvoiManuel ? ["envoi depuis une MEP"] : []);
            Ecrire($"{c.Id.ToString()[..8]}  {c.Nom.PadRight(largeur)}  {LibelleEtat(c.Etat),-14}  {(c.DernierAppel is { } d ? LibelleAppel(d) : "—"),-30}  {string.Join(", ", declencheurs)}");
        }
        return Codes.Succes;
    }

    private static string EtatSecret(SecretEtatDto? s) => s is null ? "absent" : s.Lisible ? $"enregistré ({(s.RemplaceLe is { } d ? Date(d) : "?")}, {s.RemplacePar})" : "illisible : à saisir de nouveau";

    private static async Task<int> ConnexionsAfficher(string texte)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        var dto = await svc.Dto(await TrouverConnexion(db, texte));
        if (EnJson)
        {
            EcrireJson(dto);
            return Codes.Succes;
        }
        Ecrire($"{dto.Nom} ({dto.Id})");
        if (dto.Description.Length > 0) Ecrire($"  {dto.Description}");
        Ecrire($"État            : {LibelleEtat(dto.Etat)}{(dto.DernierAppel is { } d ? $", dernier appel {LibelleAppel(d)}" : "")}");
        Ecrire($"Requête         : {dto.Methode.ToString().ToUpperInvariant()} {RequeteConnexion.UrlAffichee(dto.Url, dto.UrlSecrete)}");
        if (dto.UrlSecrete) Ecrire($"Adresse secrète : {EtatSecret(dto.AdresseSecrete)}");
        Ecrire($"Authentification: {dto.Authentification}{(dto.Utilisateur.Length > 0 ? $" ({dto.Utilisateur})" : "")}{(dto.NomEntete.Length > 0 ? $" (en-tête {dto.NomEntete})" : "")}"
            + $"{(dto.TokenUrl.Length > 0 ? $" ({dto.TokenUrl}, client {dto.ClientId})" : "")}");
        if (dto.Authentification != AuthentificationApi.Aucune) Ecrire($"Secret          : {EtatSecret(dto.Secret)}");
        if (dto.Signature) Ecrire($"Signature HMAC  : {EtatSecret(dto.SecretSignature)}");
        foreach (var e in dto.Entetes) Ecrire($"En-tête         : {e.Nom}: {(e.Secret ? $"secret, {EtatSecret(e.ValeurSecrete)}" : e.Valeur)}");
        Ecrire($"Message         : {dto.Format}, délai {dto.DelaiSecondes} s, {dto.Tentatives} tentative(s)");
        Ecrire($"Déclencheurs    : {(dto.Declencheurs.Count == 0 ? "aucun" : string.Join(", ", dto.Declencheurs.Select(DeclencheursConnexion.Libelle)))}");
        Ecrire($"Envoi depuis une MEP : {(dto.EnvoiManuel ? $"oui, « {dto.LibelleEnvoiManuel} »" : "non")}");
        Ecrire($"Modifiée        : {Date(dto.ModifieeLe)} par {dto.ModifieePar}");
        if (dto.Format != FormatCorps.Aucun && dto.Format != FormatCorps.Formulaire)
        {
            Ecrire();
            Ecrire(dto.Corps);
        }
        return Codes.Succes;
    }

    private static ConnexionApiSaisie Exporter(ConnexionApi c) => ServiceConnexions.Saisie(c) with
    {
        Version = null,
        AdresseSecrete = c.UrlSecrete is not null ? SecretASaisir : null,
        Secret = c.Authentification != AuthentificationApi.Aucune ? SecretASaisir : null,
        SecretSignature = c.SecretSignature is not null ? SecretASaisir : null,
        Entetes = [.. c.Entetes.Select(e => new EnteteConnexionSaisie(e.Nom, e.Secret ? null : e.Valeur, e.Secret, e.Secret ? SecretASaisir : null))],
    };

    private static async Task<int> ConnexionsExporter(string[] args)
    {
        var positionnels = args.Where(a => a == "-" || !a.StartsWith('-')).ToList();
        if (positionnels.Count != args.Length) throw new ErreurUsage($"option inattendue « {args.First(a => a != "-" && a.StartsWith('-'))} »");
        if (positionnels.Count > 2) throw new ErreurUsage($"argument inattendu « {positionnels[2]} »");
        string? nom = null, fichier = null;
        if (positionnels.Count == 2) (nom, fichier) = (positionnels[0], positionnels[1]);
        else if (positionnels.Count == 1)
        {
            if (positionnels[0] == "-" || positionnels[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fichier = positionnels[0];
            else nom = positionnels[0];
        }
        var config = Configuration();
        await using var db = Base(config);
        var connexions = nom is null ? await db.ConnexionsApi.AsNoTracking().OrderBy(c => c.Nom).ToListAsync() : [await TrouverConnexion(db, nom)];
        var contenu = new FichierConnexions(FormatExportConnexions, 1, DateTimeOffset.UtcNow, [.. connexions.Select(Exporter)]);
        if (await EcrireFichierOuSortie(fichier, contenu))
            Informer($"{connexions.Count} connexion(s) exportée(s) dans {fichier}, sans secret (« {SecretASaisir} »).");
        return Codes.Succes;
    }

    private static string? SansMarque(string? secret) => secret == SecretASaisir ? null : secret;

    private static async Task<int> ConnexionsImporter(string fichier, string[] args)
    {
        var o = Options.Lire(args, [], "--remplacer", "--ignorer-existantes", "--essai");
        if (o.A("--remplacer") && o.A("--ignorer-existantes")) throw new ErreurUsage("--remplacer et --ignorer-existantes s'excluent");
        var texte = await LireFichierOuEntree(fichier);
        List<ConnexionApiSaisie> saisies;
        try
        {
            using var doc = JsonDocument.Parse(texte);
            if (doc.RootElement.ValueKind == JsonValueKind.Array) saisies = doc.RootElement.Deserialize<List<ConnexionApiSaisie>>(OptionsJson) ?? [];
            else
            {
                var f = doc.RootElement.Deserialize<FichierConnexions>(OptionsJson);
                if (f?.Format != FormatExportConnexions) throw ErreurHttp.Saisie($"Fichier inattendu : « format » doit valoir « {FormatExportConnexions} » (fichier de « connexions exporter »).");
                saisies = f.Connexions ?? [];
            }
        }
        catch (JsonException e)
        {
            throw ErreurHttp.Saisie($"Fichier JSON illisible : {e.Message}");
        }
        var doublons = saisies.GroupBy(s => (s.Nom ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (doublons.Count > 0) throw ErreurHttp.Saisie($"Le fichier contient plusieurs fois : {string.Join(", ", doublons)}.");

        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        var existantes = await db.ConnexionsApi.ToListAsync();
        ConnexionApi? Existante(ConnexionApiSaisie s) => existantes.FirstOrDefault(c => c.Nom.Equals((s.Nom ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        var conflits = saisies.Where(s => Existante(s) is not null).Select(s => s.Nom!.Trim()).ToList();
        if (conflits.Count > 0 && !o.A("--remplacer") && !o.A("--ignorer-existantes"))
            throw ErreurHttp.Saisie($"Déjà présentes : {string.Join(", ", conflits)}. Relancer avec --remplacer (mise à jour) ou --ignorer-existantes.");

        int creees = 0, modifiees = 0, ignorees = 0;
        var aSaisir = new List<string>();
        var maintenant = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var brute in saisies)
        {
            var s = brute with
            {
                Version = null, AdresseSecrete = SansMarque(brute.AdresseSecrete), Secret = SansMarque(brute.Secret),
                SecretSignature = SansMarque(brute.SecretSignature),
                Entetes = brute.Entetes?.Select(e => e with { ValeurSecrete = SansMarque(e.ValeurSecrete) }).ToList(),
            };
            var c = Existante(s);
            if (c is not null && o.A("--ignorer-existantes"))
            {
                ignorees++;
                continue;
            }
            try
            {
                if (c is null)
                {
                    c = new ConnexionApi { CreeeLe = maintenant, Active = s.Active ?? true };
                    await svc.Appliquer(c, s with { Active = null }, JournalAdmin.AuteurLigneDeCommande, UsageSaisie.Importation);
                    c.ModifieeLe = maintenant;
                    c.ModifieePar = JournalAdmin.AuteurLigneDeCommande;
                    db.ConnexionsApi.Add(c);
                    JournalAdmin.Ecrire(db, null, "connexion-api.creee", $"{c.Nom} (importée)");
                    creees++;
                }
                else
                {
                    var changes = await svc.Appliquer(c, s, JournalAdmin.AuteurLigneDeCommande, UsageSaisie.Importation);
                    if (changes.Count > 0)
                    {
                        c.Version++;
                        JournalAdmin.Ecrire(db, null, "connexion-api.modifiee", $"{c.Nom} (importée) : {string.Join(", ", changes)}");
                    }
                    modifiees++;
                }
            }
            catch (ErreurHttp e)
            {
                throw new ErreurHttp(e.Statut, $"« {(s.Nom ?? "").Trim()} » : {e.Message} Rien n'a été importé.");
            }
            if (svc.SecretsManquants.Count > 0)
            {
                c.Active = false;
                aSaisir.AddRange(svc.SecretsManquants.Select(m => $"{c.Nom} : {m}"));
                Avertir($"« {c.Nom} » : secret(s) à saisir ({string.Join(", ", svc.SecretsManquants)}), connexion désactivée en attendant. "
                    + $"gestion-mep connexions secret \"{c.Nom}\" --champ {svc.SecretsManquants[0]} --secret-stdin < secret.txt");
            }
            await db.SaveChangesAsync();
        }
        Resultat(new { creees, modifiees, ignorees, secretsASaisir = aSaisir });
        if (o.A("--essai"))
        {
            await transaction.RollbackAsync();
            Informer($"Essai : {creees} connexion(s) seraient créées, {modifiees} mises à jour, {ignorees} ignorée(s). Rien n'a été enregistré.");
            return Codes.Succes;
        }
        await transaction.CommitAsync();
        Informer($"{creees} connexion(s) créée(s), {modifiees} mise(s) à jour, {ignorees} ignorée(s).");
        return Codes.Succes;
    }

    private static async Task<int> ConnexionsSecret(string texte, string[] args)
    {
        var o = Options.Lire(args, ["--champ"], "--secret-stdin", "--effacer");
        var champ = o.Valeur("--champ") ?? throw new ErreurUsage("--champ attendu (authentification, adresse, hmac ou entete:<Nom>)");
        if (o.A("--secret-stdin") == o.A("--effacer")) throw new ErreurUsage("--secret-stdin ou --effacer attendu (l'un ou l'autre)");
        var effacer = o.A("--effacer");
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        var c = await TrouverConnexion(db, texte);
        string? valeur = null;
        if (!effacer)
        {
            // --secret-stdin : l'entrée standard (fichier, tube, ou --non-interactif) ; au terminal, saisie sans écho comme « sso configurer ».
            valeur = Console.IsInputRedirected || S.NonInteractif
                ? (await Console.In.ReadToEndAsync()).Trim()
                : LireSecret($"Secret ({champ}) de « {c.Nom} »");
            if (valeur.Length == 0) throw ErreurHttp.Saisie("Secret vide : rien n'a été modifié.");
        }
        var s = ServiceConnexions.Saisie(c);
        switch (champ)
        {
            case "authentification":
                if (c.Authentification == AuthentificationApi.Aucune)
                    throw ErreurHttp.Saisie($"« {c.Nom} » n'a pas d'authentification : choisissez-en une dans la console (ou par un import).");
                if (effacer) throw ErreurHttp.Saisie("Le secret de l'authentification ne s'efface pas : changez le mode d'identification dans la console (ou par un import).");
                s = s with { Secret = valeur };
                break;
            case "adresse":
                if (effacer) throw ErreurHttp.Saisie("L'adresse secrète ne s'efface pas : remplacez-la, ou rendez l'adresse ordinaire dans la console.");
                s = s with { UrlSecrete = true, AdresseSecrete = valeur };
                break;
            case "hmac":
                s = effacer ? s with { Signature = false } : s with { Signature = true, SecretSignature = valeur };
                break;
            case var e when e.StartsWith("entete:", StringComparison.Ordinal) && e.Length > 7:
                var nom = e[7..];
                var entetes = (s.Entetes ?? []).Where(x => !string.Equals(x.Nom, nom, StringComparison.OrdinalIgnoreCase)).ToList();
                if (effacer && entetes.Count == (s.Entetes ?? []).Count) throw ErreurHttp.NonTrouve($"« {c.Nom} » n'a pas d'en-tête « {nom} ».");
                if (!effacer) entetes.Add(new EnteteConnexionSaisie(nom, null, true, valeur));
                s = s with { Entetes = entetes };
                break;
            default:
                throw new ErreurUsage($"--champ inconnu « {champ} » (authentification, adresse, hmac ou entete:<Nom>)");
        }
        var changes = await svc.Appliquer(c, s, JournalAdmin.AuteurLigneDeCommande, UsageSaisie.Importation);
        if (svc.SecretsManquants.Count > 0) c.Active = false;
        c.Version++;
        c.ModifieeLe = DateTimeOffset.UtcNow;
        c.ModifieePar = JournalAdmin.AuteurLigneDeCommande;
        JournalAdmin.Ecrire(db, null, "connexion-api.modifiee", $"{c.Nom} : {(changes.Count > 0 ? string.Join(", ", changes) : effacer ? $"{champ} effacé" : "secret remplacé")}");
        await db.SaveChangesAsync();
        Resultat(new { c.Id, c.Nom, champ, efface = effacer, secretsASaisir = svc.SecretsManquants, c.Active });
        Informer($"« {c.Nom} » : {(effacer ? "secret effacé" : "secret enregistré")} ({champ}).");
        if (svc.SecretsManquants.Count > 0)
            Avertir($"Reste à saisir : {string.Join(", ", svc.SecretsManquants)} ; la connexion reste désactivée.");
        else if (!c.Active) Informer($"Tous les secrets sont saisis : gestion-mep connexions activer \"{c.Nom}\"");
        return Codes.Succes;
    }

    private static async Task<int> ConnexionsChangerEtat(string texte, bool active)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        var c = await TrouverConnexion(db, texte);
        if (c.Active == active)
        {
            Informer($"« {c.Nom} » est déjà {(active ? "active" : "désactivée")} : rien à faire.");
            return Codes.Succes;
        }
        if (active) await ConnexionsEndpoints.ExigerSecrets(svc, c);
        c.Active = active;
        c.Version++;
        c.ModifieeLe = DateTimeOffset.UtcNow;
        c.ModifieePar = JournalAdmin.AuteurLigneDeCommande;
        JournalAdmin.Ecrire(db, null, active ? "connexion-api.activee" : "connexion-api.desactivee", c.Nom);
        await db.SaveChangesAsync();
        Resultat(new { c.Id, c.Nom, c.Active });
        Informer(active ? $"« {c.Nom} » activée : ses déclencheurs envoient de nouveau." : $"« {c.Nom} » désactivée : plus aucun appel ne part.");
        return Codes.Succes;
    }

    private static async Task<int> ConnexionsEssayer(string texte, string[] args)
    {
        var o = Options.Lire(args, ["--mep", "--evenement", "--commentaire"], "--apercu");
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        var c = await TrouverConnexion(db, texte);
        Guid? mepId = null;
        if (o.Valeur("--mep") is { } m)
            mepId = IdentifiantOuPrefixe(m, await db.Meps.Select(x => x.Id).ToListAsync(), "MEP") ?? throw ErreurHttp.NonTrouve($"MEP introuvable : « {m} ».");
        var saisie = new ApercuConnexionSaisie(ServiceConnexions.Saisie(c), c.Id, mepId, o.Valeur("--evenement"), null, o.Valeur("--commentaire"));
        var meps = ServiceMep(db, config);
        if (o.A("--apercu"))
        {
            var p = (await ConnexionsEndpoints.Preparer(saisie, null, svc, meps, db, UsageSaisie.Apercu)).Apercu;
            if (EnJson)
            {
                EcrireJson(p);
                return Codes.Succes;
            }
            Ecrire($"Événement : {p.Evenement.Libelle}{(p.MepNom is { } nom ? $" · {nom}" : " · valeurs d'exemple")}");
            Ecrire($"{p.Methode.ToString().ToUpperInvariant()} {p.Url}");
            foreach (var e in p.Entetes) Ecrire($"{e.Nom}: {e.Valeur}");
            Ecrire();
            Ecrire(p.Corps);
            if (p.Manquantes.Count > 0) Avertir($"Non renseigné dans cette MEP (envoyé vide) : {string.Join(", ", p.Manquantes)}");
            foreach (var e in p.Erreurs) Avertir($"Erreur ({ServiceConnexions.LibelleChamp(e.Champ)}, ligne {e.Ligne}, colonne {e.Colonne}) : {e.Message}");
            foreach (var x in p.Inconnues) Avertir($"Variable inconnue : {{{{{x}}}}}");
            return p.Erreurs.Count + p.Inconnues.Count == 0 ? Codes.Succes : Codes.Echec;
        }
        var reseau = new ReseauSortant(config, new ResolveurDns(), new RecepteurDemo(config, TimeProvider.System));
        var client = new ClientConnexionsApi(reseau, new SecretsConnexion(Cles(config)), TimeProvider.System);
        var r = await ConnexionsEndpoints.Essayer(saisie, null, svc, meps, db, client, CancellationToken.None);
        var t = r.Tentative;
        Resultat(r);
        Informer($"{r.Requete.Methode.ToString().ToUpperInvariant()} {r.Requete.Url} ({r.Requete.Evenement.Libelle}{(r.Requete.MepNom is { } n ? $", {n}" : "")})");
        Informer(t.Code is { } code ? $"Réponse {code} {t.Libelle} en {t.DureeMs.ToString(CultureInfo.InvariantCulture)} ms" : $"Pas de réponse : {t.Erreur}");
        if (t.Code is not null && t.Erreur.Length > 0) Informer(t.Erreur);
        if (t.ExtraitReponse.Length > 0) Informer(t.ExtraitReponse + (r.Tronque ? "\n(réponse coupée à 4 Ko)" : ""));
        return r.Reussi ? Codes.Succes : Codes.Echec;
    }

    private static async Task<int> ConnexionsAppels(string[] args)
    {
        var nom = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
        var o = Options.Lire(nom is null ? args : args[1..], ["--depuis", "--limite"], "--echecs");
        var limite = Math.Clamp(o.Entier("--limite") ?? 50, 1, 500);
        DateTimeOffset? depuis = null;
        if (o.Valeur("--depuis") is { } d)
            depuis = DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour)
                ? new DateTimeOffset(jour.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : throw ErreurHttp.Saisie("--depuis attend une date AAAA-MM-JJ.");
        var config = Configuration();
        await using var db = Base(config);
        var q = db.AppelsApi.AsNoTracking();
        if (nom is not null)
        {
            var id = (await TrouverConnexion(db, nom)).Id;
            q = q.Where(a => a.ConnexionId == id);
        }
        if (o.A("--echecs")) q = q.Where(a => a.Etat == EtatAppel.Echec && a.Origine != OrigineAppel.Essai);
        if (depuis is { } dd) q = q.Where(a => a.CreeLe >= dd);
        var noms = await db.ConnexionsApi.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nom);
        var appels = (await q.OrderByDescending(a => a.CreeLe).Take(limite).ToListAsync())
            .Select(a => ServiceConnexions.Resume(a, noms.GetValueOrDefault(a.ConnexionId, ""))).ToList();
        if (EnJson)
        {
            EcrireJson(appels);
            return Codes.Succes;
        }
        if (appels.Count == 0)
        {
            Ecrire("Aucun appel.");
            return Codes.Succes;
        }
        Ecrire($"{"ID",-8}  {"QUAND",-16}  {"CONNEXION",-28}  {"MEP · ÉVÉNEMENT",-40}  RÉSULTAT");
        foreach (var a in appels)
        {
            var mep = a.Origine == OrigineAppel.Essai ? $"{(a.MepNom.Length > 0 ? a.MepNom + " · " : "")}Essai" : $"{a.MepNom} · {a.Resume}";
            var resultat = a.Etat switch
            {
                EtatAppel.Reussi => $"{a.Code} réussi",
                EtatAppel.Echec => $"échec, {(a.Code is { } c ? c.ToString(CultureInfo.InvariantCulture) + ", " : "")}{a.Tentatives} tentative(s) : {a.Erreur}",
                EtatAppel.EnCours => "en cours",
                _ => $"en attente{(a.ProchaineTentative is { } p ? $" (tentative à {Date(p)})" : "")}",
            };
            Ecrire($"{a.Id.ToString()[..8]}  {Date(a.CreeLe),-16}  {Tronquer(a.Connexion, 28),-28}  {Tronquer(mep, 40),-40}  {resultat}");
        }
        return Codes.Succes;
    }

    private static string Tronquer(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static async Task<int> ConnexionsRenvoyer(string texte)
    {
        var config = Configuration();
        await using var db = Base(config);
        var svc = ServiceConnexionsCli(db, config);
        Guid? id = Guid.TryParse(texte, out var g) ? g : null;
        if (id is null)
        {
            var t = texte.Trim().ToLowerInvariant();
            if (t.Length < 4 || !t.All(Uri.IsHexDigit)) throw ErreurHttp.NonTrouve($"Appel introuvable : « {texte} ».");
            var ids = (await db.AppelsApi.AsNoTracking().Select(a => a.Id).ToListAsync()).Where(x => x.ToString().StartsWith(t, StringComparison.Ordinal)).ToList();
            id = ids.Count switch
            {
                0 => throw ErreurHttp.NonTrouve($"Appel introuvable : « {texte} » (voir « gestion-mep connexions appels »)."),
                1 => ids[0],
                _ => throw ErreurHttp.Saisie($"Plusieurs appels commencent par « {texte} » : donner l'identifiant complet."),
            };
        }
        var a = await db.AppelsApi.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw ErreurHttp.NonTrouve($"Appel introuvable : « {texte} ».");
        var c = await db.ConnexionsApi.AsNoTracking().FirstAsync(x => x.Id == a.ConnexionId);
        var nouveau = ConnexionsEndpoints.Renvoyer(a, c, null, svc, true);
        db.AppelsApi.Add(nouveau);
        await db.SaveChangesAsync();
        Resultat(new { appelId = nouveau.Id });
        Informer($"Appel {nouveau.Id.ToString()[..8]} mis en file : le serveur l'envoie dans les secondes qui suivent (« gestion-mep connexions appels » pour le suivre).");
        return Codes.Succes;
    }

    private static async Task<int> ConnexionsSupprimer(string texte, string[] args)
    {
        var o = Options.Lire(args, [], "--oui");
        var config = Configuration();
        await using var db = Base(config);
        var c = await TrouverConnexion(db, texte);
        var appels = await db.AppelsApi.CountAsync(a => a.ConnexionId == c.Id);
        Confirmer(o.A("--oui"), [$"La connexion « {c.Nom} » et son historique de {appels} appel(s) seront supprimés."],
            $"gestion-mep connexions supprimer \"{c.Nom}\" --oui");
        db.ConnexionsApi.Remove(c);
        JournalAdmin.Ecrire(db, null, "connexion-api.supprimee", $"{c.Nom}{(appels > 0 ? $" (historique de {appels} appel{(appels > 1 ? "s" : "")} supprimé)" : "")}");
        await db.SaveChangesAsync();
        Resultat(new { c.Id, c.Nom });
        Informer($"Connexion « {c.Nom} » supprimée.");
        return Codes.Succes;
    }
}
