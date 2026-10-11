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
            Utilisateur Creer(string login, string nom, bool admin)
            {
                var u = new Utilisateur { Login = login, NomAffiche = nom, EstAdmin = admin };
                u.MotDePasseHash = hasher.HashPassword(u, "demo");
                db.Utilisateurs.Add(u);
                return u;
            }
            var alice = Creer("alice", "Alice", true);
            var bob = Creer("bob", "Bob", false);
            var claire = Creer("claire", "Claire", false);
            var maintenant = temps.GetUtcNow();
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
            await db.SaveChangesAsync();
            journal.LogInformation("Données de démonstration créées (comptes alice, bob, claire / mot de passe demo ; Claire approuve la MEP).");
            return;
        }

        var login = config["Admin:Login"];
        var motDePasse = config["Admin:MotDePasse"];
        if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrEmpty(motDePasse))
        {
            var admin = new Utilisateur { Login = login.Trim().ToLowerInvariant(), NomAffiche = login.Trim(), EstAdmin = true };
            admin.MotDePasseHash = hasher.HashPassword(admin, motDePasse);
            db.Utilisateurs.Add(admin);
            await db.SaveChangesAsync();
            journal.LogInformation("Compte administrateur {Login} créé.", admin.Login);
        }
        else
        {
            journal.LogWarning("Aucun utilisateur : définir Admin:Login et Admin:MotDePasse (ou Demo:Activer) pour pouvoir se connecter.");
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
