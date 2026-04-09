using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Superplay.Shared.Enums;

namespace Superplay.Server.Services;

/// <summary>
/// SQLite-backed player repository using EF Core.
///
/// Table schema:
///   Players (PlayerId PK, DeviceId UNIQUE, Coins, Rolls)
///
/// Uses explicit transactions for atomic cross-player transfers.
/// SQLite is single-writer, so concurrent writes are serialized at the database level.
/// </summary>
public sealed class SqlitePlayerRepository : IPlayerRepository
{
    private readonly GameDbContext _db;
    private readonly ILogger<SqlitePlayerRepository> _logger;

    /// <summary>
    /// Initializes the repository with a database context and logger.
    /// </summary>
    public SqlitePlayerRepository(GameDbContext db, ILogger<SqlitePlayerRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetPlayerIdByDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId, cancellationToken);

        return player?.PlayerId;
    }

    /// <inheritdoc />
    public async Task<string> CreatePlayerAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var playerId = Guid.NewGuid().ToString("N");

        _db.Players.Add(new PlayerEntity
        {
            PlayerId = playerId,
            DeviceId = deviceId,
            Coins = 0,
            Rolls = 0
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created player {PlayerId} for device {DeviceId}", playerId, deviceId);
        return playerId;
    }

    /// <inheritdoc />
    public async Task<long> GetResourceAsync(string playerId, ResourceType resourceType, CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
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
        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        if (player is null)
            return (0, 0);

        return (player.Coins, player.Rolls);
    }

    /// <inheritdoc />
    public async Task<long> UpdateResourceAsync(string playerId, ResourceType resourceType, long delta, CancellationToken cancellationToken = default)
    {
        // Use raw SQL for atomic increment — avoids read-modify-write race conditions.
        var fieldName = ResourceField(resourceType);

        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Players SET " + fieldName + " = " + fieldName + " + {0} WHERE PlayerId = {1}",
            new object[] { delta, playerId },
            cancellationToken);

        if (rowsAffected == 0)
        {
            _logger.LogWarning("UpdateResource failed: player {PlayerId} not found", playerId);
            return 0;
        }

        // Read back the new balance.
        var newBalance = await _db.Players
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
        var fieldName = ResourceField(resourceType);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Read sender's current balance inside the transaction.
            var senderBalance = await _db.Players
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

            // Debit sender.
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Players SET " + fieldName + " = " + fieldName + " - {0} WHERE PlayerId = {1}",
                new object[] { amount, fromPlayerId },
                cancellationToken);

            // Credit recipient.
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE Players SET " + fieldName + " = " + fieldName + " + {0} WHERE PlayerId = {1}",
                new object[] { amount, toPlayerId },
                cancellationToken);

            // Read back new balances.
            var senderNew = await _db.Players
                .AsNoTracking()
                .Where(p => p.PlayerId == fromPlayerId)
                .Select(p => resourceType == ResourceType.Coins ? p.Coins : p.Rolls)
                .FirstOrDefaultAsync(cancellationToken);

            var recipientNew = await _db.Players
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
        return await _db.Players
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
