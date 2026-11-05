using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;

namespace TMConcept.Storage.Database;

/// <summary>
/// The encrypted database (SQLite3 Multiple Ciphers; the whole file is encrypted, table names included). One initial schema created with
/// EnsureCreated: no migrations before the first public data (no backward compatibility yet). Times are stored as UTC ticks.
/// Deleting a profile removes its concepts, queue, image records, embeddings and video records; deleting a concept removes its embedding and
/// queue entry and leaves its videos (their link becomes empty).
/// </summary>
public sealed class ConceptDbContext(DbContextOptions<ConceptDbContext> options) : DbContext(options)
{
    /// <summary>Profiles.</summary>
    public DbSet<Profile> Profiles => Set<Profile>();

    /// <summary>Sample images.</summary>
    public DbSet<ProfileImage> ProfileImages => Set<ProfileImage>();

    /// <summary>Concepts.</summary>
    public DbSet<Concept> Concepts => Set<Concept>();

    /// <summary>Concept embeddings.</summary>
    public DbSet<ConceptEmbedding> Embeddings => Set<ConceptEmbedding>();

    /// <summary>History digests.</summary>
    public DbSet<HistoryDigest> HistoryDigests => Set<HistoryDigest>();

    /// <summary>Runs.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>Production queue.</summary>
    public DbSet<QueueItem> QueueItems => Set<QueueItem>();

    /// <summary>Videos.</summary>
    public DbSet<VideoRecord> Videos => Set<VideoRecord>();

    /// <summary>Video cost lines.</summary>
    public DbSet<VideoCost> VideoCosts => Set<VideoCost>();

    /// <summary>API keys.</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>Step models.</summary>
    public DbSet<StepModelChoice> StepModels => Set<StepModelChoice>();

    /// <summary>Price overrides.</summary>
    public DbSet<ModelPriceOverride> PriceOverrides => Set<ModelPriceOverride>();

    /// <summary>Settings documents.</summary>
    public DbSet<SettingRow> Settings => Set<SettingRow>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<Profile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(p => p.Id);
        });
        modelBuilder.Entity<ProfileImage>(e =>
        {
            e.ToTable("profile_images");
            e.HasKey(i => i.Id);
            e.HasOne<Profile>().WithMany().HasForeignKey(i => i.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Concept>(e =>
        {
            e.ToTable("concepts");
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.ProfileId, c.Number }).IsUnique();
            e.HasIndex(c => new { c.ProfileId, c.Status });
            e.HasOne<Profile>().WithMany().HasForeignKey(c => c.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConceptEmbedding>(e =>
        {
            e.ToTable("concept_embeddings");
            e.HasKey(c => c.ConceptId);
            e.HasIndex(c => new { c.ProfileId, c.Model });
            e.HasOne<Concept>().WithMany().HasForeignKey(c => c.ConceptId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HistoryDigest>(e =>
        {
            e.ToTable("history_digests");
            e.HasKey(h => h.ProfileId);
            e.HasOne<Profile>().WithMany().HasForeignKey(h => h.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Run>(e =>
        {
            e.ToTable("runs");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.StartedAtUtc);
        });
        modelBuilder.Entity<QueueItem>(e =>
        {
            e.ToTable("queue_items");
            e.HasKey(q => q.Id);
            e.HasIndex(q => new { q.ProfileId, q.Position });
            e.HasOne<Profile>().WithMany().HasForeignKey(q => q.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Concept>().WithMany().HasForeignKey(q => q.ConceptId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<VideoRecord>(e =>
        {
            e.ToTable("videos");
            e.HasKey(v => v.Id);
            e.HasIndex(v => new { v.ProfileId, v.CreatedAtUtc });
            e.HasOne<Profile>().WithMany().HasForeignKey(v => v.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Concept>().WithMany().HasForeignKey(v => v.ConceptId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<VideoCost>(e =>
        {
            e.ToTable("video_costs");
            e.HasKey(c => c.Id);
            e.HasOne<VideoRecord>().WithMany().HasForeignKey(c => c.VideoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.Provider);
        });
        modelBuilder.Entity<StepModelChoice>(e =>
        {
            e.ToTable("step_models");
            e.HasKey(s => s.Step);
        });
        modelBuilder.Entity<ModelPriceOverride>(e =>
        {
            e.ToTable("model_overrides");
            e.HasKey(o => new { o.Provider, o.ModelId });
        });
        modelBuilder.Entity<SettingRow>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
        });
    }
}
