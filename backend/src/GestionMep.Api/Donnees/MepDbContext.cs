using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Donnees;

public class MepDbContext(DbContextOptions<MepDbContext> options) : DbContext(options)
{
    public DbSet<Utilisateur> Utilisateurs => Set<Utilisateur>();
    public DbSet<Mep> Meps => Set<Mep>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Systeme> Systemes => Set<Systeme>();
    public DbSet<Livrable> Livrables => Set<Livrable>();
    public DbSet<Etape> Etapes => Set<Etape>();
    public DbSet<EvenementJournal> Journal => Set<EvenementJournal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Les identifiants sont générés côté application : sans cela, EF prendrait une entité neuve
        // portant déjà un Guid pour une entité existante (et tenterait un UPDATE).
        b.Entity<Utilisateur>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Login).IsUnique();
        });

        b.Entity<Mep>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            // Garde-fou contre deux écritures simultanées (en plus de la comparaison manuelle avec la version du client).
            e.Property(x => x.Version).IsConcurrencyToken();
            e.OwnsOne(x => x.Sauvegarde);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Systemes).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Livrables).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Etapes).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CreeLe);
        });

        b.Entity<Participant>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Systeme>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Livrable>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<Etape>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Dependances).HasColumnType("uuid[]");
            e.HasIndex(x => new { x.MepId, x.Code }).IsUnique();
        });

        b.Entity<EvenementJournal>(e =>
        {
            e.HasOne<Mep>().WithMany().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MepId, x.Id });
        });
    }
}
