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

/// <summary>Réglages modifiables sans redémarrage et contrôle de la configuration.</summary>
public static partial class LigneDeCommande
{
    /// <summary>Le nombre d'approbations vit dans les paramètres métier, mais se règle avec les autres.</summary>
    private const string Approbations = "approbations-minimum";

    private static async Task<int> ReglagesLister()
    {
        var config = Configuration();
        await using var db = Base(config);
        if (EnJson)
        {
            EcrireJson(new
            {
                Reglages = await Reglages.Lister(db, config),
                ApprobationsMinimum = await new ServiceMep(db, TimeProvider.System, new DiffuseurMep(config)).ApprobationsMinimum(),
            });
            return 0;
        }
        var largeur = Reglages.Catalogue.Max(d => d.Nom.Length) + 2;
        foreach (var groupe in (await Reglages.Lister(db, config)).GroupBy(r => r.Groupe))
        {
            Ecrire(groupe.Key);
            foreach (var r in groupe)
            {
                var origine = r.Origine switch
                {
                    "console" => $"modifié le {r.ModifieLe:yyyy-MM-dd HH:mm} par {r.ModifiePar}" + (r.ValeurFichier is { } f ? $", {Installation.NomFichier} : {Texte(f)}" : ""),
                    "fichier" => $"{Installation.NomFichier} ({r.Variable})",
                    _ => "valeur par défaut",
                };
                Ecrire($"  {r.Nom.PadRight(largeur)}{Texte(r.Valeur)}   [{origine}]");
                foreach (var a in r.Alertes) Ecrire($"  {"".PadRight(largeur)}Attention : {a}");
            }
        }
        Ecrire("Approbations");
        Ecrire($"  {Approbations.PadRight(largeur)}{(await db.Parametres.AsNoTracking().Select(p => (int?)p.ApprobationsMinimum).FirstOrDefaultAsync() ?? 1)}");
        Ecrire();
        Ecrire($"Lus au démarrage, dans le {Installation.FichierDemarrage} :");
        Ecrire(Installation.EstDocker
            ? "PORT, ADRESSE_ECOUTE, POSTGRES_USER, POSTGRES_PASSWORD, DEMO, ADMIN_LOGIN, ADMIN_MOT_DE_PASSE, MEP_IMAGE, COMPOSE_PROFILES."
            : "BASE_DE_DONNEES, DOSSIER_DONNEES, PORT, ADRESSE_ECOUTE, DEMO, ADMIN_LOGIN, ADMIN_MOT_DE_PASSE.");
        return 0;
    }

    private static async Task<int> ReglagesDefinir(string nom, string valeur)
    {
        var config = Configuration();
        await using var db = Base(config);
        if (nom == Approbations)
        {
            if (!int.TryParse(valeur, System.Globalization.CultureInfo.InvariantCulture, out var n))
                throw ErreurHttp.Saisie("Le nombre minimum d'approbations doit être un entier positif ou nul.");
            var enregistre = await ParametresEndpoints.EnregistrerApprobations(db, n, null);
            Informer($"Réglage enregistré : {Approbations} = {enregistre}.");
            Resultat(new { Nom = Approbations, Valeur = enregistre.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return 0;
        }
        var r = await Reglages.Definir(db, config, Reglages.Trouver(nom), valeur, null, DateTimeOffset.UtcNow);
        Informer($"Réglage enregistré : {r.Nom} = {Texte(r.Valeur)}. L'application l'applique sous 10 secondes, sans redémarrage.");
        foreach (var a in r.Alertes) Avertir($"Attention : {a}");
        Resultat(r);
        return 0;
    }

    private static async Task<int> ReglagesRetablir(string nom)
    {
        var config = Configuration();
        await using var db = Base(config);
        var r = await Reglages.Retablir(db, config, Reglages.Trouver(nom), null);
        Resultat(r);
        Informer($"Réglage rétabli : {r.Nom} = {Texte(r.Valeur)} ({(r.Origine == "fichier" ? Installation.NomFichier : "valeur par défaut")}).");
        return 0;
    }


    /// <summary>Contrôles de démarrage, lisibles par un humain ; code 1 si un contrôle bloquant échoue.</summary>
    private static async Task<int> VerifierConfig()
    {
        var config = Configuration();
        var erreurs = 0;
        var controles = new List<ControleDto>();
        Resultat(controles);
        void Ok(string m) { Ecrire($"[ok]        {m}"); controles.Add(new("ok", m)); }
        void Attention(string m) { Ecrire($"[attention] {m}"); controles.Add(new("attention", m)); }
        void Erreur(string m) { Ecrire($"[erreur]    {m}"); controles.Add(new("erreur", m)); erreurs++; }

        Ok($"Environnement : {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}");
        if (config.GetValue<bool>("Demo:Activer")) Attention("Mode démonstration activé (DEMO=true) : comptes alice, bob, claire / demo. À désactiver sur un serveur partagé.");

        VerifierDossier("Pièces jointes", string.IsNullOrWhiteSpace(config["Stockage:Dossier"]) ? StockagePiecesJointes.DossierParDefaut : config["Stockage:Dossier"]!, obligatoire: true);
        VerifierDossier("Sauvegardes", Sauvegardes.DossierConfigure(config), obligatoire: true);
        if (config["Cles:Dossier"] is { Length: > 0 } cles) VerifierDossier("Clés de session", cles, obligatoire: true);
        else Attention("Cles:Dossier non défini : les sessions seront perdues à chaque recréation du conteneur.");

        if (config.GetConnectionString("Mep") is not { Length: > 0 } chaine)
        {
            Erreur("ConnectionStrings:Mep absente (variable ConnectionStrings__Mep).");
            return 1;
        }
        if (new Npgsql.NpgsqlConnectionStringBuilder(chaine).Password is { } mdpBase && AuthentificationLocale.MotDePasseInitialRefuse(mdpBase) is not null)
            Attention("Mot de passe de la base faible ou d'exemple (POSTGRES_PASSWORD) : à changer hors poste de développement.");
        if (!config.GetValue(Reglages.HttpsObligatoire, true))
            Attention("HTTPS non obligatoire (mode d'essai) : les mots de passe peuvent circuler en clair. « gestion-mep reglages definir https-obligatoire oui ».");
        var charte = ServiceCharte.LireFichier(ServiceCharte.CheminFichier(config));
        if (!charte.Present) Ok($"Charte visuelle : pas de fichier {charte.Chemin} (facultatif).");
        else if (charte.Erreur is { } erreurCharte) Attention($"Charte visuelle : fichier {charte.Chemin} invalide, ignoré : {erreurCharte} (« gestion-mep charte verifier »).");
        else Ok($"Charte visuelle : fichier {charte.Chemin} valide.");
        if (config["Admin:MotDePasse"] is { Length: > 0 })
            Attention($"ADMIN_MOT_DE_PASSE est encore défini : il ne sert qu'au premier démarrage, le retirer du {Installation.NomFichier}.");
        await using var db = Base(config);
        try
        {
            if (!await db.Database.CanConnectAsync()) { Erreur("Base PostgreSQL injoignable avec ConnectionStrings:Mep."); return 1; }
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException or ArgumentException)
        {
            Erreur($"Base PostgreSQL injoignable : {e.Message}");
            return 1;
        }
        Ok("Base PostgreSQL joignable.");
        var enAttente = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (enAttente.Count > 0)
        {
            Attention($"{enAttente.Count} migration(s) en attente (appliquées au démarrage, ou par « gestion-mep migrer »).");
            return erreurs > 0 ? 1 : 0;
        }
        Ok("Schéma de la base à jour.");
        var admins = await db.Utilisateurs.CountAsync(u => u.EstAdmin && u.Actif);
        if (admins == 0) Erreur("Aucun administrateur actif : « gestion-mep comptes creer <login> --admin ».");
        else Ok($"{admins} administrateur(s) actif(s).");
        var sso = await SsoEndpoints.Charger(db);
        if (!sso.Active) Ok("Connexion unique désactivée (facultative : « gestion-mep sso afficher »).");
        else if (ConnexionUnique.Anomalies(sso, Cles(config), pourActiver: true) is { Count: > 0 } anomalies)
            Erreur($"Connexion unique active mais inutilisable : {string.Join(" ", anomalies)}");
        else
        {
            Ok($"Connexion unique active ({ConnexionUnique.AutoriteEffective(sso)}).");
            if (AlerteExpiration(sso) is { } alerte) Attention(alerte);
        }
        return erreurs > 0 ? 1 : 0;

        void VerifierDossier(string libelle, string chemin, bool obligatoire)
        {
            try
            {
                Directory.CreateDirectory(chemin);
                var essai = Path.Combine(chemin, $".essai-{Guid.NewGuid():N}");
                File.WriteAllText(essai, "");
                File.Delete(essai);
                Ok($"{libelle} : {chemin} accessible en écriture.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (obligatoire) Erreur($"{libelle} : {chemin} non accessible en écriture ({e.Message}).");
                else Attention($"{libelle} : {chemin} non accessible en écriture.");
            }
        }
    }

    private sealed record ControleDto(string Niveau, string Message);
}
