using Superplay.Shared.Enums;

namespace Superplay.Server.Services;

/// <summary>
/// Abstracts player data persistence.
/// All operations should be atomic at the Redis level.
/// </summary>
public interface IPlayerRepository
{
    /// <summary>
    /// Gets the player ID associated with a device, or null if this is a new device.
    /// </summary>
    Task<string?> GetPlayerIdByDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new player record and returns the generated player ID.
    /// Sets initial resource balances to zero.
    /// </summary>
    Task<string> CreatePlayerAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current balance for a specific resource type.
    /// </summary>
    Task<long> GetResourceAsync(string playerId, ResourceType resourceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets both coin and roll balances for a player.
    /// </summary>
    Task<(long Coins, long Rolls)> GetAllResourcesAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments (or decrements) a resource and returns the new balance.
    /// </summary>
    Task<long> UpdateResourceAsync(string playerId, ResourceType resourceType, long delta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transfers a resource from one player to another.
    /// Returns (senderNewBalance, recipientNewBalance) or null if sender has insufficient funds.
    /// </summary>
    Task<(long SenderBalance, long RecipientBalance)?> TransferResourceAsync(
        string fromPlayerId,
        string toPlayerId,
        ResourceType resourceType,
        long amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a player exists.
    /// </summary>
    Task<bool> PlayerExistsAsync(string playerId, CancellationToken cancellationToken = default);
}
