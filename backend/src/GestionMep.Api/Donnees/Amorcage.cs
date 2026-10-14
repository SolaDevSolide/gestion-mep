using GestionMep.Api.Auth;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Donnees;

/// <summary>Migration de la base puis premiers comptes : démonstration (Demo:Activer) ou administrateur (Admin:Login / Admin:MotDePasse).</summary>
public static class Amorcage
{
    public static async Task Executer(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MepDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var hasher = sp.GetRequiredService<IPasswordHasher<Utilisateur>>();
        var temps = sp.GetRequiredService<TimeProvider>();
        var journal = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Amorcage");

        if (config.GetValue<bool>("Demo:Activer") && !sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
            journal.LogWarning("Mode démonstration activé hors développement (Demo:Activer / DEMO=true) : comptes alice, bob, claire avec le mot de passe « demo ». À désactiver sur un serveur partagé.");

        // Migrations en attente : sauvegarde préalable, contrôle des données, puis suppression de la sauvegarde si rien n'est perdu.
        var sauvegardes = new Sauvegardes(Sauvegardes.DossierConfigure(config), sp.GetRequiredService<StockagePiecesJointes>().Dossier,
            m => journal.LogInformation("{Message}", m), m => journal.LogError("{Message}", m));
        await sauvegardes.MigrerAvecSauvegarde(db, "Mise à jour au démarrage");
        await PlacerEtapesAnciennes(db, journal);
        await IsolerPiecesOrphelines(db, sp.GetRequiredService<StockagePiecesJointes>(), temps, journal);

        if (await db.Utilisateurs.AnyAsync())
        {
            // Base d'un jalon précédent déjà amorcée : on n'ajoute que les catalogues de démonstration manquants, sans toucher aux MEP.
            if (config.GetValue<bool>("Demo:Activer") && !await db.SystemesCatalogue.AnyAsync() && !await db.ResolutionsTypes.AnyAsync())
            {
                var (systemes, resolutions) = DonneesDemo.Catalogue();
                foreach (var r in resolutions) { r.Version = 1; r.ModifieeLe = temps.GetUtcNow(); }
                db.SystemesCatalogue.AddRange(systemes);
                db.ResolutionsTypes.AddRange(resolutions);
                db.VersionsResolutionType.AddRange(resolutions.Select(r => VersionResolutionType.De(r, "creee")));
                await db.SaveChangesAsync();
                journal.LogInformation("Catalogue et résolutions types de démonstration ajoutés.");
            }
            if (config.GetValue<bool>("Demo:Activer") && !await db.CriteresGoNoGo.AnyAsync())
            {
                db.CriteresGoNoGo.AddRange(DonneesDemo.CriteresGoNoGo());
                await db.SaveChangesAsync();
                journal.LogInformation("Critères go / no-go de démonstration ajoutés.");
            }
            return;
        }

        if (config.GetValue<bool>("Demo:Activer"))
        {
            await CreerDemonstration(db, hasher, temps.GetUtcNow());
            await db.SaveChangesAsync();
            journal.LogInformation("Données de démonstration créées (comptes alice, bob, claire / mot de passe demo ; Claire approuve la MEP ; "
                + "{Nombre} MEP de démonstration).", await db.Meps.CountAsync(m => !m.EstModele));
            return;
        }

        var login = config["Admin:Login"];
        var motDePasse = config["Admin:MotDePasse"];
        if (string.IsNullOrWhiteSpace(login))
        {
            journal.LogWarning("Aucun utilisateur : définir Admin:Login (ADMIN_LOGIN) ou Demo:Activer pour pouvoir se connecter.");
            return;
        }
        // Mot de passe initial : jamais celui de l'exemple, et toujours à changer à la première connexion (il reste écrit dans .env).
        if (!string.IsNullOrEmpty(motDePasse) && AuthentificationLocale.MotDePasseInitialRefuse(motDePasse) is { } raison)
        {
            journal.LogError("ADMIN_MOT_DE_PASSE refusé ({Raison}) : aucun compte créé. Le corriger dans .env, ou le laisser vide pour obtenir "
                + "un mot de passe provisoire dans ce journal, puis redémarrer.", raison);
            return;
        }
        var admin = new Utilisateur { Login = login.Trim().ToLowerInvariant(), NomAffiche = login.Trim(), EstAdmin = true };
        if (string.IsNullOrEmpty(motDePasse))
        {
            var provisoire = AuthentificationLocale.PoserMotDePasseProvisoire(admin, hasher);
            // Mot de passe en fin de message, sans ponctuation collée : il se copie tel quel depuis le journal
            // (docker compose logs, journalctl, Observateur d'événements).
            journal.LogWarning("Compte administrateur {Login} créé, à changer à la première connexion. Mot de passe provisoire : {MotDePasse}",
                admin.Login, provisoire);
        }
        else
        {
            admin.MotDePasseHash = hasher.HashPassword(admin, motDePasse);
            admin.DoitChangerMotDePasse = true;
            journal.LogInformation("Compte administrateur {Login} créé ; son mot de passe sera à changer à la première connexion.", admin.Login);
        }
        db.Utilisateurs.Add(admin);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Données d'une base neuve en mode démonstration, ajoutées au contexte sans les enregistrer : comptes de
    /// <see cref="DonneesDemo.Comptes"/> (mot de passe <see cref="DonneesDemo.MotDePasse"/>), catalogue, résolutions types, bibliothèque de recettes,
    /// critères go / no-go, paramètre d'approbation, modèle, MEP « Portail de démonstration » et MEP de démonstration dans
    /// chaque état du cycle de vie (<see cref="DonneesDemo.Scenarios"/>), avec leur journal. Sert à l'amorçage et à la
    /// réinitialisation de la démonstration (<see cref="Services.Demonstration"/>).
    /// </summary>
    public static async Task CreerDemonstration(MepDbContext db, IPasswordHasher<Utilisateur> hasher, DateTimeOffset maintenant)
    {
        var comptes = DonneesDemo.Comptes.Select(c =>
        {
            var u = new Utilisateur { Login = c.Login, NomAffiche = c.Nom, EstAdmin = c.EstAdmin };
            u.MotDePasseHash = hasher.HashPassword(u, DonneesDemo.MotDePasse);
            db.Utilisateurs.Add(u);
            return u;
        }).ToList();
        var (alice, bob, claire) = (comptes[0], comptes[1], comptes[2]);
        var jeu = DonneesDemo.Creer(alice.Id, bob.Id, claire.Id, maintenant);
        DonneesDemo.CompleterProfils(alice, bob, claire, jeu.Systemes);
        var mep = jeu.Mep;
        mep.CreeLe = maintenant;
        mep.Version = 1;
        // Le modèle est créé juste avant : la MEP de démonstration reste en tête de liste.
        var modele = jeu.Modele;
        modele.CreeLe = maintenant.AddSeconds(-1);
        modele.Version = 1;
        foreach (var r in jeu.Resolutions) r.ModifieeLe = maintenant;
        foreach (var r in jeu.Resolutions) r.Version = 1;
        db.SystemesCatalogue.AddRange(jeu.Systemes);
        db.ResolutionsTypes.AddRange(jeu.Resolutions);
        db.VersionsResolutionType.AddRange(jeu.Resolutions.Select(r => VersionResolutionType.De(r, "creee")));
        db.CriteresGoNoGo.AddRange(jeu.Criteres);
        db.ModelesRecette.AddRange(DonneesDemo.ModelesRecette(jeu.Systemes, maintenant));
        var parametres = await db.Parametres.FirstOrDefaultAsync(p => p.Id == ParametresGlobaux.IdUnique);
        if (parametres is null) db.Parametres.Add(parametres = new ParametresGlobaux());
        parametres.ApprobationsMinimum = DonneesDemo.ApprobationsMinimum;
        db.Meps.Add(modele);
        db.Meps.Add(mep);
        db.Journal.Add(new EvenementJournal
        {
            MepId = modele.Id, Quand = maintenant, UtilisateurId = alice.Id, Auteur = alice.NomAffiche,
            Type = "mep.creee", Detail = "Modèle de démonstration créé",
        });
        db.Journal.Add(new EvenementJournal
        {
            MepId = mep.Id, Quand = maintenant, UtilisateurId = alice.Id, Auteur = alice.NomAffiche,
            Type = "mep.creee", Detail = "MEP de démonstration créée",
        });
        // Une MEP par état du cycle de vie, construite en jouant les règles du domaine (journal compris).
        foreach (var demo in DonneesDemo.Scenarios(alice.Id, bob.Id, claire.Id, jeu.Systemes, jeu.Resolutions, jeu.Criteres, maintenant))
        {
            db.Meps.Add(demo.Mep);
            db.Journal.AddRange(demo.Journal);
        }
    }

    /// <summary>
    /// Étapes d'avant le jalon 4 (marquées <c>Ligne = -1</c> par la migration Jalon4) : lignes calculées d'après le graphe
    /// des dépendances (<see cref="Regles.CalculerLignes"/>), plan par plan. Sans effet une fois fait.
    /// </summary>
    private static async Task PlacerEtapesAnciennes(MepDbContext db, ILogger journal)
    {
        var plans = await db.Etapes.Where(e => e.Ligne < 0).Select(e => new { e.MepId, e.Plan }).Distinct().ToListAsync();
        if (plans.Count == 0) return;
        foreach (var p in plans)
            Regles.CalculerLignes(await db.Etapes.Where(e => e.MepId == p.MepId && e.Plan == p.Plan).ToListAsync());
        await db.SaveChangesAsync();
        journal.LogInformation("Lignes du graphe calculées pour {Nombre} plan(s) existant(s).", plans.Count);
    }

    /// <summary>
    /// Fichiers de pièces jointes sans ligne en base (arrêt pendant un dépôt) : déplacés dans le sous-dossier
    /// <c>orphelins</c> s'ils ont plus d'une heure.
    /// </summary>
    private static async Task IsolerPiecesOrphelines(MepDbContext db, StockagePiecesJointes stockage, TimeProvider temps, ILogger journal)
    {
        var connus = (await db.PiecesJointes.AsNoTracking().Select(p => p.Id).ToListAsync()).ToHashSet();
        var n = stockage.IsolerOrphelins(connus, TimeSpan.FromHours(1), temps.GetUtcNow());
        if (n > 0)
            journal.LogWarning("{Nombre} fichier(s) de pièce jointe sans enregistrement en base déplacé(s) dans {Dossier}.",
                n, Path.Combine(stockage.Dossier, StockagePiecesJointes.DossierOrphelins));
    }
}
