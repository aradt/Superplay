using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Superplay.Shared.Enums;

namespace Superplay.Server.Services;

/// <summary>
/// Redis-backed player repository using hash sets for player data.
///
/// Key schema:
///   device:{deviceId}  -> string (playerId)
///   player:{playerId}  -> hash { deviceId, coins, rolls }
///
/// Uses HINCRBY for atomic single-resource updates and a Lua script
/// for atomic cross-player gift transfers (check + debit + credit in one round trip).
/// </summary>
public sealed class RedisPlayerRepository : IPlayerRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPlayerRepository> _logger;

    // Lua script for atomic resource update with floor of zero:
    // KEYS[1] = player hash
    // ARGV[1] = resource field name, ARGV[2] = delta
    // Returns: new balance, or -1 if the update would go below zero
    private const string UpdateScript = """
        local current = tonumber(redis.call('HGET', KEYS[1], ARGV[1]) or '0')
        local delta = tonumber(ARGV[2])
        local newBalance = current + delta
        if newBalance < 0 then
            return -1
        end
        return redis.call('HINCRBY', KEYS[1], ARGV[1], delta)
        """;

    // Lua script for atomic gift transfer:
    // KEYS[1] = sender player hash, KEYS[2] = recipient player hash
    // ARGV[1] = resource field name, ARGV[2] = transfer amount
    // Returns: { senderNewBalance, recipientNewBalance } or -1 if insufficient funds
    private const string TransferScript = """
        local senderBalance = tonumber(redis.call('HGET', KEYS[1], ARGV[1]) or '0')
        local amount = tonumber(ARGV[2])
        if senderBalance < amount then
            return {-1, -1}
        end
        local senderNew = redis.call('HINCRBY', KEYS[1], ARGV[1], -amount)
        local recipientNew = redis.call('HINCRBY', KEYS[2], ARGV[1], amount)
        return {senderNew, recipientNew}
        """;

    /// <summary>
    /// Initializes the repository with a Redis connection and logger.
    /// </summary>
    public RedisPlayerRepository(IConnectionMultiplexer redis, ILogger<RedisPlayerRepository> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>Gets the active Redis database instance.</summary>
    private IDatabase Db => _redis.GetDatabase();

    /// <inheritdoc />
    public async Task<string?> GetPlayerIdByDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync(DeviceKey(deviceId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a Redis transaction (MULTI/EXEC) to atomically create both the
    /// device-to-player mapping and the player hash. Either both keys are set or neither.
    /// </remarks>
    public async Task<string> CreatePlayerAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var playerId = Guid.NewGuid().ToString("N");

        var transaction = Db.CreateTransaction();

        var setDeviceTask = transaction.StringSetAsync(DeviceKey(deviceId), playerId);
        var setPlayerTask = transaction.HashSetAsync(PlayerKey(playerId), new[]
        {
            new HashEntry("deviceId", deviceId),
            new HashEntry("coins", 0),
            new HashEntry("rolls", 0)
        });

        var committed = await transaction.ExecuteAsync();
        if (!committed)
        {
            throw new InvalidOperationException($"Failed to create player for device {deviceId}");
        }

        await setDeviceTask;
        await setPlayerTask;

        _logger.LogInformation("Created player {PlayerId} for device {DeviceId}", playerId, deviceId);
        return playerId;
    }

    /// <inheritdoc />
    public async Task<long> GetResourceAsync(string playerId, ResourceType resourceType, CancellationToken cancellationToken = default)
    {
        var value = await Db.HashGetAsync(PlayerKey(playerId), ResourceField(resourceType));
        return (long)value;
    }

    /// <inheritdoc />
    public async Task<(long Coins, long Rolls)> GetAllResourcesAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var values = await Db.HashGetAsync(PlayerKey(playerId), new RedisValue[] { "coins", "rolls" });
        var coins = values[0].IsNull ? 0 : (long)values[0];
        var rolls = values[1].IsNull ? 0 : (long)values[1];
        return (coins, rolls);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a Lua script to atomically check that the balance won't go below zero before applying the delta.
    /// Returns -1 if the update would result in a negative balance.
    /// </remarks>
    public async Task<long> UpdateResourceAsync(string playerId, ResourceType resourceType, long delta, CancellationToken cancellationToken = default)
    {
        var result = (long)await Db.ScriptEvaluateAsync(
            UpdateScript,
            new RedisKey[] { PlayerKey(playerId) },
            new RedisValue[] { ResourceField(resourceType), delta });

        if (result == -1)
        {
            _logger.LogWarning(
                "Update rejected: player {PlayerId} {ResourceType} would go below zero (delta={Delta})",
                playerId, resourceType, delta);
            return -1;
        }

        _logger.LogDebug(
            "Updated {ResourceType} for player {PlayerId}: delta={Delta}, newBalance={NewBalance}",
            resourceType, playerId, delta, result);

        return result;
    }

    /// <inheritdoc />
    public async Task<(long SenderBalance, long RecipientBalance)?> TransferResourceAsync(
        string fromPlayerId,
        string toPlayerId,
        ResourceType resourceType,
        long amount,
        CancellationToken cancellationToken = default)
    {
        var result = await Db.ScriptEvaluateAsync(
            TransferScript,
            new RedisKey[] { PlayerKey(fromPlayerId), PlayerKey(toPlayerId) },
            new RedisValue[] { ResourceField(resourceType), amount });

        var results = (long[])result!;

        if (results[0] == -1)
        {
            _logger.LogWarning(
                "Gift transfer failed: player {FromPlayerId} has insufficient {ResourceType} (requested {Amount})",
                fromPlayerId, resourceType, amount);
            return null;
        }

        _logger.LogInformation(
            "Gift transferred: {Amount} {ResourceType} from {FromPlayerId} (balance={SenderBalance}) to {ToPlayerId} (balance={RecipientBalance})",
            amount, resourceType, fromPlayerId, results[0], toPlayerId, results[1]);

        return (results[0], results[1]);
    }

    /// <inheritdoc />
    public async Task<bool> PlayerExistsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return await Db.KeyExistsAsync(PlayerKey(playerId));
    }

    /// <summary>Builds the Redis key for a device-to-player mapping.</summary>
    private static string DeviceKey(string deviceId) => $"device:{deviceId}";

    /// <summary>Builds the Redis key for a player's hash set.</summary>
    private static string PlayerKey(string playerId) => $"player:{playerId}";

    /// <summary>Maps a <see cref="ResourceType"/> to its Redis hash field name.</summary>
    private static string ResourceField(ResourceType type) => type switch
    {
        ResourceType.Coins => "coins",
        ResourceType.Rolls => "rolls",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type")
    };
}
