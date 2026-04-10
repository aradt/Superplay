using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Superplay.Shared;
using Superplay.Shared.Enums;

namespace Superplay.Server.Services;

/// <summary>
/// SQLite-backed player repository using EF Core.
///
/// Table schema:
///   Players (PlayerId PK, DeviceId UNIQUE, Coins, Rolls)
///
/// Registered as a Singleton. Creates a new DbContext per operation via
/// IServiceScopeFactory to avoid thread-safety issues with EF Core's DbContext.
/// Uses explicit transactions for atomic cross-player transfers.
/// </summary>
public sealed class SqlitePlayerRepository : IPlayerRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlitePlayerRepository> _logger;

    /// <summary>
    /// Initializes the repository with a scope factory (for creating DbContext instances) and logger.
    /// </summary>
    public SqlitePlayerRepository(IServiceScopeFactory scopeFactory, ILogger<SqlitePlayerRepository> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Creates a new scoped DbContext for a single operation.</summary>
    private GameDbContext CreateDbContext()
    {
        var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GameDbContext>();
    }

    /// <inheritdoc />
    public async Task<string?> GetPlayerIdByDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId, cancellationToken);

        return player?.PlayerId;
    }

    /// <inheritdoc />
    public async Task<string> CreatePlayerAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var playerId = Guid.NewGuid().ToString("N");

        db.Players.Add(new PlayerEntity
        {
            PlayerId = playerId,
            DeviceId = deviceId,
            Coins = 0,
            Rolls = 0
        });

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created player {PlayerId} for device {DeviceId}", playerId, deviceId);
        return playerId;
    }

    /// <inheritdoc />
    public async Task<long> GetResourceAsync(string playerId, ResourceType resourceType, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        if (player is null)
            return 0;

        return resourceType switch
        {
            ResourceType.Coins => player.Coins,
            ResourceType.Rolls => player.Rolls,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unknown resource type")
        };
    }

    /// <inheritdoc />
    public async Task<(long Coins, long Rolls)> GetAllResourcesAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        if (player is null)
            return (0, 0);

        return (player.Coins, player.Rolls);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a conditional UPDATE to ensure the balance won't go below zero.
    /// Returns -1 if the update would result in a negative balance.
    /// </remarks>
    public async Task<long> UpdateResourceAsync(string playerId, ResourceType resourceType, long delta, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var fieldName = ResourceField(resourceType);

        // Conditional update: only apply if result >= 0 and <= max balance
        var rowsAffected = await db.Database.ExecuteSqlRawAsync(
            "UPDATE Players SET " + fieldName + " = " + fieldName + " + {0} WHERE PlayerId = {1} AND " + fieldName + " + {0} >= 0 AND " + fieldName + " + {0} <= {2}",
            new object[] { delta, playerId, Defaults.MaxResourceBalance },
            cancellationToken);

        if (rowsAffected == 0)
        {
            var exists = await db.Players.AsNoTracking().AnyAsync(p => p.PlayerId == playerId, cancellationToken);
            if (!exists)
            {
                _logger.LogWarning("UpdateResource failed: player {PlayerId} not found", playerId);
                return 0;
            }

            // Check which bound was violated
            var current = await db.Players.AsNoTracking()
                .Where(p => p.PlayerId == playerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            if (current + delta < 0)
            {
                _logger.LogWarning(
                    "Update rejected: player {PlayerId} {ResourceType} would go below zero (delta={Delta})",
                    playerId, resourceType, delta);
                return -1;
            }

            _logger.LogWarning(
                "Update rejected: player {PlayerId} {ResourceType} would exceed max balance (delta={Delta})",
                playerId, resourceType, delta);
            return -2;
        }

        var newBalance = await db.Players
            .AsNoTracking()
            .Where(p => p.PlayerId == playerId)
            .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
            .FirstOrDefaultAsync(cancellationToken);

        _logger.LogDebug(
            "Updated {ResourceType} for player {PlayerId}: delta={Delta}, newBalance={NewBalance}",
            resourceType, playerId, delta, newBalance);

        return newBalance;
    }

    /// <inheritdoc />
    public async Task<(long SenderBalance, long RecipientBalance)?> TransferResourceAsync(
        string fromPlayerId,
        string toPlayerId,
        ResourceType resourceType,
        long amount,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var fieldName = ResourceField(resourceType);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var senderBalance = await db.Players
                .Where(p => p.PlayerId == fromPlayerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            if (senderBalance < amount)
            {
                _logger.LogWarning(
                    "Gift transfer failed: player {FromPlayerId} has insufficient {ResourceType} (requested {Amount}, has {Balance})",
                    fromPlayerId, resourceType, amount, senderBalance);
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var recipientBalance = await db.Players
                .Where(p => p.PlayerId == toPlayerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            if (recipientBalance + amount > Defaults.MaxResourceBalance)
            {
                _logger.LogWarning(
                    "Gift transfer failed: recipient {ToPlayerId} would exceed max {ResourceType} balance",
                    toPlayerId, resourceType);
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE Players SET " + fieldName + " = " + fieldName + " - {0} WHERE PlayerId = {1}",
                new object[] { amount, fromPlayerId },
                cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE Players SET " + fieldName + " = " + fieldName + " + {0} WHERE PlayerId = {1}",
                new object[] { amount, toPlayerId },
                cancellationToken);

            var senderNew = await db.Players
                .AsNoTracking()
                .Where(p => p.PlayerId == fromPlayerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            var recipientNew = await db.Players
                .AsNoTracking()
                .Where(p => p.PlayerId == toPlayerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Gift transferred: {Amount} {ResourceType} from {FromPlayerId} (balance={SenderBalance}) to {ToPlayerId} (balance={RecipientBalance})",
                amount, resourceType, fromPlayerId, senderNew, toPlayerId, recipientNew);

            return (senderNew, recipientNew);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PlayerExistsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        return await db.Players
            .AsNoTracking()
            .AnyAsync(p => p.PlayerId == playerId, cancellationToken);
    }

    /// <summary>Maps a <see cref="ResourceType"/> to its SQLite column name.</summary>
    private static string ResourceField(ResourceType type) => type switch
    {
        ResourceType.Coins => "Coins",
        ResourceType.Rolls => "Rolls",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type")
    };
}
