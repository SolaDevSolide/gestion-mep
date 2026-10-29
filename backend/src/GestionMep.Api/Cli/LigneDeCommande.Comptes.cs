using System.Globalization;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Cli;

/// <summary>Comptes et connexion unique (mêmes règles que la console d'administration).</summary>
public static partial class LigneDeCommande
{
    // ---------- Comptes ----------

    private static async Task<Utilisateur> Compte(MepDbContext db, string login)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        return await db.Utilisateurs.FirstOrDefaultAsync(u => u.Login == l) ?? throw ErreurHttp.NonTrouve($"Compte « {l} » introuvable (gestion-mep comptes lister).");
    }

    private static async Task<int> Lister()
    {
        await using var db = Base(Configuration());
        var comptes = await db.Utilisateurs.AsNoTracking().OrderBy(u => u.Login).ToListAsync();
        var annuaire = await Personnes.Charger(db);
        if (EnJson)
        {
            EcrireJson(comptes.Select(u => Personnes.Dto(u, annuaire)));
            return 0;
        }
        var logins = comptes.ToDictionary(u => u.Id, u => u.Login);
        var largeur = Math.Max(5, comptes.Count == 0 ? 0 : comptes.Max(u => u.Login.Length));
        Ecrire($"{"LOGIN".PadRight(largeur)}  ADMIN  ÉTAT        NOM");
        foreach (var u in comptes)
        {
            var etat = !u.Actif ? "désactivé" : u.DoitChangerMotDePasse ? "provisoire" : "actif";
            var relie = u.ComptePrincipalId is { } p && logins.TryGetValue(p, out var lp) ? $" (relié à {lp})" : "";
            Ecrire($"{u.Login.PadRight(largeur)}  {(u.EstAdmin ? "oui" : "non"),-5}  {etat,-10}  {annuaire.NomPersonne(u.Id) ?? u.NomAffiche}{relie}");
        }
        return 0;
    }

    private static async Task<int> AfficherCompte(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var annuaire = await Personnes.Charger(db);
        if (EnJson)
        {
            EcrireJson(Personnes.Dto(u, annuaire));
            return 0;
        }
        Ecrire($"Login          : {u.Login}");
        Ecrire($"Nom affiché    : {u.NomAffiche}");
        var autres = annuaire.ComptesDeLaPersonne(u.Id).Where(c => c.Id != u.Id).ToList();
        if (u.ComptePrincipalId is not null && autres.Count > 0)
            Ecrire($"Relié à        : {autres[0].NomAffiche} ({autres[0].Login}), compte principal : affiché sous ce nom");
        else if (autres.Count > 0)
            Ecrire($"Comptes reliés : {string.Join(", ", autres.Select(c => c.Login + (c.Actif ? "" : " (désactivé)")))}");
        var equipes = (await db.Equipes.AsNoTracking().ToListAsync()).Where(e => e.MembreIds.Contains(u.Id)).Select(e => e.Nom).Order().ToList();
        Ecrire($"Équipes        : {(equipes.Count == 0 ? "aucune" : string.Join(", ", equipes))}");
        Ecrire($"Administrateur : {(u.EstAdmin ? "oui" : "non")}");
        Ecrire($"État           : {(!u.Actif ? "désactivé" : u.DoitChangerMotDePasse ? "actif, mot de passe provisoire à changer" : "actif")}");
        Ecrire($"Rôle           : {Texte(u.Role)}");
        Ecrire($"Contact        : {Texte(u.Contact)}");
        await EcrireExpertises(db, u);
        return 0;
    }

    private static async Task EcrireExpertises(MepDbContext db, Utilisateur u)
    {
        if (u.Expertises.Count == 0)
        {
            Ecrire("Expertises     : aucune");
            return;
        }
        var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
        Ecrire("Expertises     :");
        foreach (var e in u.Expertises)
            Ecrire($"  {noms.GetValueOrDefault(e.SystemeCatalogueId, "(système supprimé)")}{(e.Detail.Length > 0 ? $" : {e.Detail}" : "")}");
    }

    private static async Task<int> Expertises(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        if (EnJson)
        {
            var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
            EcrireJson(u.Expertises.Select(e => new { e.SystemeCatalogueId, Systeme = noms.GetValueOrDefault(e.SystemeCatalogueId), e.Detail }));
            return 0;
        }
        await EcrireExpertises(db, u);
        return 0;
    }

    private static async Task<int> Creer(string login, string[] arguments)
    {
        var l = AuthentificationLocale.NormaliserLogin(login);
        if (l.Length == 0 || l.StartsWith('-')) throw new ErreurUsage("<login> attendu");
        var o = Options.Lire(arguments, ["--nom", "--role", "--contact"], "--admin");
        var nom = o.Valeur("--nom")?.Trim();
        var admin = o.A("--admin");
        await using var db = Base(Configuration());
        if (await db.Utilisateurs.AnyAsync(u => u.Login == l)) throw new ErreurHttp(1, $"Le compte « {l} » existe déjà.");
        var (compte, mdp) = await AdminEndpoints.Creer(db, null,
            new CompteSaisie(l, string.IsNullOrEmpty(nom) ? l : nom, admin, o.Valeur("--role"), o.Valeur("--contact")), new PasswordHasher<Utilisateur>());
        Informer($"Compte « {l} » créé{(admin ? " (administrateur)" : "")}.");
        Ecrire($"Mot de passe provisoire : {mdp}");
        Informer("À transmettre par un canal sûr ; il devra être changé à la première connexion.");
        Resultat(new { Compte = Personnes.Dto(compte, await Personnes.Charger(db)), MotDePasseProvisoire = mdp });
        return 0;
    }

    /// <summary>
    /// Nom, rôle administrateur, rôle et contact (mêmes règles que la console), puis expertises (mêmes règles que le profil) :
    /// « --detail » s'applique au « --ajouter-expertise » qui le précède.
    /// </summary>
    private static async Task<int> ModifierCompte(string login, string[] arguments)
    {
        var o = Options.Lire(arguments, ["--nom", "--admin", "--role", "--contact", "--ajouter-expertise", "--detail", "--retirer-expertise"]);
        if (o.Liste.Count == 0) throw new ErreurUsage("au moins une option à modifier attendue (--nom, --admin, --role, --contact, --ajouter-expertise…)");
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var resultats = new List<string>();

        if (o.A("--nom") || o.A("--admin") || o.A("--role") || o.A("--contact"))
        {
            var changements = await AdminEndpoints.Modifier(db, null, u, new CompteModification(
                o.Valeur("--nom") ?? u.NomAffiche, o.OuiNon("--admin") ?? u.EstAdmin, o.Valeur("--role") ?? u.Role, o.Valeur("--contact") ?? u.Contact));
            resultats.AddRange(changements);
        }

        if (o.A("--ajouter-expertise") || o.A("--retirer-expertise") || o.A("--detail"))
        {
            var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
            var expertises = u.Expertises.Select(e => new ExpertiseSaisie(e.SystemeCatalogueId, e.Detail)).ToList();
            var details = new List<string>();
            ExpertiseSaisie? derniere = null;
            foreach (var (nom, valeur) in o.Liste)
            {
                switch (nom)
                {
                    case "--ajouter-expertise":
                        var ajout = TrouverSysteme(catalogue, valeur!);
                        var existante = expertises.FindIndex(e => e.SystemeCatalogueId == ajout.Id);
                        derniere = new ExpertiseSaisie(ajout.Id, existante >= 0 ? expertises[existante].Detail : "");
                        if (existante >= 0) expertises[existante] = derniere;
                        else expertises.Add(derniere);
                        details.Add($"expertise {ajout.Nom} {(existante >= 0 ? "mise à jour" : "ajoutée")}");
                        break;
                    case "--detail":
                        if (derniere is null) throw new ErreurUsage("--detail suit un --ajouter-expertise");
                        var i = expertises.IndexOf(derniere);
                        expertises[i] = derniere = derniere with { Detail = valeur };
                        break;
                    case "--retirer-expertise":
                        var retrait = TrouverSysteme(catalogue, valeur!);
                        if (expertises.RemoveAll(e => e.SystemeCatalogueId == retrait.Id) == 0)
                            throw ErreurHttp.Saisie($"« {u.Login} » n'a pas d'expertise sur {retrait.Nom}.");
                        details.Add($"expertise {retrait.Nom} retirée");
                        derniere = null;
                        break;
                }
            }
            await AuthEndpoints.ModifierProfil(db, null, u, new ProfilSaisie(null, u.Role, u.Contact, expertises), string.Join(", ", details));
            resultats.AddRange(details);
        }

        Informer(resultats.Count == 0 ? $"Compte « {u.Login} » : aucun changement." : $"Compte « {u.Login} » modifié : {string.Join(", ", resultats)}.");
        Resultat(Personnes.Dto(u, await Personnes.Charger(db)));
        return 0;
    }

    private static async Task<int> Reinitialiser(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var mdp = await AdminEndpoints.ReinitialiserMotDePasse(db, null, u, new PasswordHasher<Utilisateur>());
        Ecrire($"Mot de passe provisoire de « {u.Login} » : {mdp}");
        Informer("Ses sessions ouvertes sont fermées ; il devra être changé à la prochaine connexion.");
        if (!u.Actif) Avertir($"Attention : le compte est désactivé (gestion-mep comptes reactiver {u.Login}).");
        Resultat(new { u.Login, MotDePasseProvisoire = mdp });
        return 0;
    }

    private static async Task<int> ChangerEtat(string login, bool actif)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var change = actif ? await AdminEndpoints.Reactiver(db, null, u) : await AdminEndpoints.Desactiver(db, null, u);
        Informer(change
            ? $"Compte « {u.Login} » {(actif ? "réactivé" : "désactivé")}."
            : $"Le compte « {u.Login} » est déjà {(actif ? "actif" : "désactivé")} : rien à faire.");
        Resultat(new { u.Login, u.Actif, Modifie = change });
        return 0;
    }

    private static async Task<int> Relier(string login, string loginPrincipal, string[] arguments)
    {
        var o = Options.Lire(arguments, [], "--desactiver");
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var principal = await Compte(db, loginPrincipal);
        var etaitActif = u.Actif;
        await AdminEndpoints.Relier(db, null, u, principal, o.A("--desactiver"));
        Informer($"Compte « {u.Login} » relié à « {principal.Login} » : il s'affiche désormais sous le nom « {principal.NomAffiche} »"
            + (etaitActif && !u.Actif ? " et il est désactivé." : "."));
        return 0;
    }

    private static async Task<int> Delier(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        await AdminEndpoints.Delier(db, null, u);
        Informer($"Liaison du compte « {u.Login} » défaite : il s'affiche de nouveau sous le nom « {u.NomAffiche} ».");
        return 0;
    }

    /// <summary>Mêmes clés que l'application (dossier Cles:Dossier) : le secret chiffré ici se déchiffre là-bas.</summary>
    private static IDataProtectionProvider Cles(IConfiguration config) =>
        config["Cles:Dossier"] is { Length: > 0 } dossier
            ? DataProtectionProvider.Create(new DirectoryInfo(dossier), b => b.SetApplicationName("gestion-mep"))
            : DataProtectionProvider.Create("gestion-mep");

    private static string? AlerteExpiration(ConfigurationSso c)
    {
        if (c.SecretExpireLe is not { } fin) return null;
        var jours = fin.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        return jours < 0 ? $"Le secret client a expiré le {fin:yyyy-MM-dd} : les connexions par le fournisseur échouent."
            : jours <= 30 ? $"Le secret client expire le {fin:yyyy-MM-dd} (dans {jours} jour(s)) : prévoyez son renouvellement."
            : null;
    }

    private static async Task<int> SsoAfficher()
    {
        var config = Configuration();
        await using var db = Base(config);
        var c = await SsoEndpoints.Charger(db);
        var cles = Cles(config);
        if (EnJson)
        {
            EcrireJson(SsoEndpoints.Dto(c, cles, ConnexionUnique.UrlRetour(config, null)));
            return 0;
        }
        var secret = c.SecretChiffre.Length == 0 ? "absent"
            : ConnexionUnique.Secret(c, cles) is null ? "illisible avec les clés actuelles : à saisir de nouveau"
            : "enregistré (chiffré)";
        if (c.SecretExpireLe is { } fin) secret += $", expire le {fin:yyyy-MM-dd}";
        var microsoft = c.Fournisseur == FournisseurSso.Microsoft;
        Ecrire($"Connexion unique   : {(c.Active ? "activée" : "désactivée")}");
        Ecrire($"Fournisseur        : {(microsoft ? "Microsoft Entra ID" : "OpenID Connect")}");
        Ecrire(microsoft ? $"Annuaire           : {Valeur(c.Annuaire)}" : $"Émetteur           : {Valeur(c.Autorite)}");
        Ecrire($"ID d'application   : {Valeur(c.ClientId)}");
        Ecrire($"Secret             : {secret}");
        Ecrire($"Comptes            : {(c.CreationAutomatique ? "créés à la première connexion" : "déjà liés seulement")}");
        Ecrire($"Rôle administrateur: {(c.RoleAdmin.Length > 0 ? $"rôle d'application « {c.RoleAdmin} »" : "géré dans l'application")}");
        Ecrire($"Bouton             : {ConnexionUnique.LibelleEffectif(c)}");
        Ecrire($"Adresse de retour  : {ConnexionUnique.UrlRetour(config, null) ?? $"<adresse publique>{ConnexionUnique.CheminRetour} (adresse publique non définie : gestion-mep reglages definir url-publique <adresse>)"}");
        foreach (var a in ConnexionUnique.Anomalies(c, cles, pourActiver: true)) Ecrire($"À compléter        : {a}");
        if (AlerteExpiration(c) is { } alerte) Avertir($"Attention : {alerte}");
        return 0;

        static string Valeur(string v) => v.Length > 0 ? v : "(vide)";
    }

    private static async Task<int> SsoConfigurer(string[] options)
    {
        if (options.Length == 0) throw new ErreurUsage("au moins une option à régler attendue (--annuaire, --emetteur, --client…)");
        var config = Configuration();
        await using var db = Base(config);
        var c = await SsoEndpoints.Charger(db);
        var fournisseur = c.Fournisseur;
        string annuaire = c.Annuaire, autorite = c.Autorite, client = c.ClientId, role = c.RoleAdmin, libelle = c.Libelle;
        var expiration = c.SecretExpireLe;
        var creation = c.CreationAutomatique;
        string? secret = null;
        var lireSecret = false;
        for (var i = 0; i < options.Length; i++)
        {
            string Suivant() => i + 1 < options.Length ? options[++i] : throw new ErreurUsage($"valeur manquante après {options[i]}");
            switch (options[i])
            {
                case "--annuaire": annuaire = Suivant(); fournisseur = FournisseurSso.Microsoft; break;
                case "--emetteur": autorite = Suivant(); fournisseur = FournisseurSso.OpenIdConnect; break;
                case "--client": client = Suivant(); break;
                case "--secret-stdin": lireSecret = true; break;
                case "--role-admin": role = Suivant() is "aucun" ? "" : options[i]; break;
                case "--libelle": libelle = Suivant(); break;
                case "--creation-auto":
                    creation = Suivant() switch
                    {
                        "oui" => true,
                        "non" => false,
                        _ => throw ErreurHttp.Saisie("--creation-auto attend oui ou non."),
                    };
                    break;
                case "--expiration":
                    var texte = Suivant();
                    expiration = texte == "aucune" ? null
                        : DateOnly.TryParseExact(texte, "yyyy-MM-dd", out var d) ? d
                        : throw ErreurHttp.Saisie("--expiration attend une date AAAA-MM-JJ ou « aucune ».");
                    break;
                default: throw new ErreurUsage(options[i].StartsWith('-') ? $"option inattendue « {options[i]} »" : $"argument inattendu « {options[i]} »");
            }
        }
        if (lireSecret)
        {
            secret = LireSecret();
            if (string.IsNullOrWhiteSpace(secret)) throw ErreurHttp.Saisie("Secret vide.");
        }
        await SsoEndpoints.Enregistrer(db, Cles(config), null,
            new SsoSaisie(fournisseur, annuaire, autorite, client, secret, expiration, creation, role, libelle), DateTimeOffset.UtcNow);
        Informer("Réglages enregistrés.");
        return await SsoAfficher();
    }

    /// <summary>Secret depuis l'entrée standard redirigée (fichier, tube), sinon saisi au clavier sans écho.</summary>
    private static string LireSecret(string invite = "Secret client")
    {
        if (Console.IsInputRedirected) return (Console.In.ReadToEnd()).Trim();
        if (S.NonInteractif) throw ErreurHttp.Saisie("--secret-stdin : rien à lire sur l'entrée standard (passer le secret par un tube ou un fichier : < secret.txt).");
        Console.Error.Write($"{invite} : ");
        var saisie = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (saisie.Length > 0) saisie.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) saisie.Append(k.KeyChar);
        }
        Console.Error.WriteLine();
        return saisie.ToString().Trim();
    }

    private static async Task<int> SsoTester()
    {
        var config = Configuration();
        await using var db = Base(config);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        var verifications = await ConnexionUnique.Tester(await SsoEndpoints.Charger(db), Cles(config), http);
        foreach (var v in verifications) Ecrire($"[{(v.Ok ? "ok" : "erreur"),-6}] {v.Libelle} : {v.Detail}");
        Resultat(verifications);
        return verifications.All(v => v.Ok) ? 0 : 1;
    }

    private static async Task<int> SsoChangerEtat(bool active)
    {
        var config = Configuration();
        await using var db = Base(config);
        await SsoEndpoints.ChangerEtat(db, Cles(config), null, active, DateTimeOffset.UtcNow);
        Informer(active
            ? "Connexion unique activée : le bouton apparaît sur la page de connexion (le mot de passe local reste possible)."
            : "Connexion unique désactivée : seuls les comptes locaux se connectent. Les liaisons sont conservées.");
        return 0;
    }

    private static async Task<int> SsoComptes()
    {
        await using var db = Base(Configuration());
        var liens = await SsoEndpoints.Identites(db).ToListAsync();
        if (EnJson)
        {
            EcrireJson(liens);
            return 0;
        }
        if (liens.Count == 0) { Ecrire("Aucun compte relié."); return 0; }
        var largeur = Math.Max(5, liens.Max(l => l.Login.Length));
        Ecrire($"{"LOGIN".PadRight(largeur)}  {"DERNIÈRE CONNEXION",-18}  IDENTIFIANT CHEZ LE FOURNISSEUR");
        foreach (var l in liens)
            Ecrire($"{l.Login.PadRight(largeur)}  {(l.DerniereConnexion is { } d ? d.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "jamais"),-18}  {l.Identifiant}");
        return 0;
    }

    private static async Task<int> SsoDelier(string login)
    {
        await using var db = Base(Configuration());
        var u = await Compte(db, login);
        var liens = await db.IdentitesExternes.Include(x => x.Utilisateur).Where(x => x.UtilisateurId == u.Id).ToListAsync();
        if (liens.Count == 0) { Informer($"Le compte « {u.Login} » n'est relié à aucun compte du fournisseur : rien à faire."); return 0; }
        foreach (var l in liens) await SsoEndpoints.Delier(db, null, l);
        Informer($"Liaison supprimée pour « {u.Login} ».");
        if (u.MotDePasseHash.Length == 0)
            Avertir($"Ce compte n'a pas de mot de passe : « gestion-mep comptes reinitialiser-mdp {u.Login} » pour lui en donner un.");
        return 0;
    }
}
