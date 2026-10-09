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

        await db.Database.MigrateAsync();

        if (await db.Utilisateurs.AnyAsync())
        {
            // Base du jalon 1 déjà amorcée : on n'ajoute que le catalogue de démonstration manquant, sans toucher aux MEP.
            if (config.GetValue<bool>("Demo:Activer") && !await db.SystemesCatalogue.AnyAsync() && !await db.ResolutionsTypes.AnyAsync())
            {
                var (systemes, resolutions) = DonneesDemo.Catalogue();
                foreach (var r in resolutions) { r.Version = 1; r.ModifieeLe = temps.GetUtcNow(); }
                db.SystemesCatalogue.AddRange(systemes);
                db.ResolutionsTypes.AddRange(resolutions);
                await db.SaveChangesAsync();
                journal.LogInformation("Catalogue et résolutions types de démonstration ajoutés.");
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
            var mep = jeu.Mep;
            mep.CreeLe = maintenant;
            mep.Version = 1;
            foreach (var r in jeu.Resolutions) r.ModifieeLe = maintenant;
            foreach (var r in jeu.Resolutions) r.Version = 1;
            db.SystemesCatalogue.AddRange(jeu.Systemes);
            db.ResolutionsTypes.AddRange(jeu.Resolutions);
            db.Meps.Add(mep);
            db.Journal.Add(new EvenementJournal
            {
                MepId = mep.Id, Quand = maintenant, UtilisateurId = alice.Id, Auteur = alice.NomAffiche,
                Type = "mep.creee", Detail = "MEP de démonstration créée",
            });
            await db.SaveChangesAsync();
            journal.LogInformation("Données de démonstration créées (comptes alice, bob, claire / mot de passe demo).");
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
}
