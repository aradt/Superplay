using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Superplay.Server.Services;

/// <summary>
/// EF Core DbContext for SQLite-backed player storage.
/// </summary>
public sealed class GameDbContext : DbContext
{
    /// <summary>
    /// Initializes the context with the provided options (connection string, etc.).
    /// </summary>
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
    {
    }

    /// <summary>Player records.</summary>
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerEntity>(entity =>
        {
            entity.HasKey(e => e.PlayerId);

            entity.HasIndex(e => e.DeviceId)
                .IsUnique();

            entity.Property(e => e.PlayerId)
                .HasMaxLength(32)
                .UseCollation("NOCASE");

            entity.Property(e => e.DeviceId)
                .IsRequired()
                .HasMaxLength(256)
                .UseCollation("NOCASE");

            entity.Property(e => e.Coins)
                .HasDefaultValue(0L);

            entity.Property(e => e.Rolls)
                .HasDefaultValue(0L);
        });
    }
}

/// <summary>
/// Represents a player record in the SQLite database.
/// </summary>
public sealed class PlayerEntity
{
    /// <summary>Unique player identifier (32-char hex GUID).</summary>
    [MaxLength(32)]
    public required string PlayerId { get; set; }

    /// <summary>The device that owns this player account.</summary>
    [MaxLength(256)]
    public required string DeviceId { get; set; }

    /// <summary>Current coin balance.</summary>
    public long Coins { get; set; }

    /// <summary>Current roll balance.</summary>
    public long Rolls { get; set; }
}
