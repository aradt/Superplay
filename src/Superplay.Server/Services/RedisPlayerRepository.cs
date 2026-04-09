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
    public async Task<string> CreatePlayerAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var playerId = Guid.NewGuid().ToString("N");

        var batch = Db.CreateBatch();

        var setDeviceTask = batch.StringSetAsync(DeviceKey(deviceId), playerId);
        var setPlayerTask = batch.HashSetAsync(PlayerKey(playerId), new[]
        {
            new HashEntry("deviceId", deviceId),
            new HashEntry("coins", 0),
            new HashEntry("rolls", 0)
        });

        batch.Execute();

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
    public async Task<long> UpdateResourceAsync(string playerId, ResourceType resourceType, long delta, CancellationToken cancellationToken = default)
    {
        var newBalance = await Db.HashIncrementAsync(PlayerKey(playerId), ResourceField(resourceType), delta);

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
