using GestionMep.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestionMep.Api.Donnees;

/// <summary>Stockage d'une liste d'objets en jsonb (conversion JSON explicite, avec détection des modifications sur le contenu).</summary>
internal static class ExtensionsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<List<T>> AsJson<T>(this PropertyBuilder<List<T>> p)
    {
        p.HasColumnType("jsonb");
        p.HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options)!));
        return p;
    }

    /// <summary>Objet unique facultatif stocké en jsonb (null : colonne NULL).</summary>
    public static PropertyBuilder<T?> AsJsonObjet<T>(this PropertyBuilder<T?> p) where T : class
    {
        p.HasColumnType("jsonb");
        p.HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<T>(v, Options),
            new ValueComparer<T?>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)));
        return p;
    }
}

public class MepDbContext(DbContextOptions<MepDbContext> options) : DbContext(options)
{
    public DbSet<Utilisateur> Utilisateurs => Set<Utilisateur>();
    public DbSet<Mep> Meps => Set<Mep>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Systeme> Systemes => Set<Systeme>();
    public DbSet<Livrable> Livrables => Set<Livrable>();
    public DbSet<Etape> Etapes => Set<Etape>();
    public DbSet<EvenementJournal> Journal => Set<EvenementJournal>();
    public DbSet<SystemeCatalogue> SystemesCatalogue => Set<SystemeCatalogue>();
    public DbSet<ResolutionType> ResolutionsTypes => Set<ResolutionType>();
    public DbSet<ExecutionResolution> ExecutionsResolution => Set<ExecutionResolution>();
    public DbSet<CritereGoNoGo> CriteresGoNoGo => Set<CritereGoNoGo>();
    public DbSet<ParametresGlobaux> Parametres => Set<ParametresGlobaux>();
    public DbSet<PieceJointe> PiecesJointes => Set<PieceJointe>();

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
            e.OwnsOne(x => x.PlanRepli);
            e.Property(x => x.SuppleantIds).HasColumnType("uuid[]");
            e.Property(x => x.Approbations).AsJson();
            e.Property(x => x.GoNoGo).AsJson();
            e.Property(x => x.TestsRecette).AsJson();
            e.Property(x => x.Bilan).AsJsonObjet();
            e.HasIndex(x => x.EstModele);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Systemes).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Livrables).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Etapes).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Resolutions).WithOne().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CreeLe);
        });

        b.Entity<Participant>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Systeme>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Livrable>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<Etape>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Dependances).HasColumnType("uuid[]");
            e.Property(x => x.ResolutionIds).HasColumnType("uuid[]");
            e.HasIndex(x => new { x.MepId, x.Code }).IsUnique();
        });

        b.Entity<SystemeCatalogue>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Nom).IsUnique();
        });

        b.Entity<ResolutionType>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Corrections).AsJson();
            // Verrou optimiste : la version reçue du client est comparée à la main, et EF protège contre les écritures simultanées.
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<ExecutionResolution>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Corrections).AsJson();
            e.HasIndex(x => x.EtapeId);
        });

        b.Entity<CritereGoNoGo>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<ParametresGlobaux>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            // Ligne unique créée par la migration, avec la valeur par défaut.
            e.HasData(new ParametresGlobaux { Id = ParametresGlobaux.IdUnique, ApprobationsMinimum = 1 });
        });

        b.Entity<PieceJointe>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.NomFichier).HasMaxLength(255);
            e.Property(x => x.TypeContenu).HasMaxLength(255);
            e.Property(x => x.Sha256).HasMaxLength(64);
            // Rattachements facultatifs : la suppression du parent supprime la ligne (le fichier est effacé par l'API).
            e.HasOne<Mep>().WithMany().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Etape>().WithMany().HasForeignKey(x => x.EtapeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ResolutionType>().WithMany().HasForeignKey(x => x.ResolutionTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ExecutionResolution>().WithMany().HasForeignKey(x => x.ExecutionResolutionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EvenementJournal>(e =>
        {
            e.HasOne<Mep>().WithMany().HasForeignKey(x => x.MepId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MepId, x.Id });
        });
    }
}
