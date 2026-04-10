using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Superplay.Shared;

namespace Superplay.Server.Services;

/// <summary>
/// Redis-backed idempotency store. Uses SET NX with TTL for atomic duplicate detection.
/// Shared across multiple server instances since Redis is the central store.
///
/// Key schema:
///   idempotency:{requestId}       → serialized response JSON (or "processing" while in-flight)
///   TTL auto-expires entries.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    private const string KeyPrefix = "idempotency:";
    private const string ProcessingMarker = "__processing__";

    /// <summary>
    /// Initializes the store with a Redis connection and TTL from configuration.
    /// </summary>
    public RedisIdempotencyStore(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;
        var ttlSeconds = configuration.GetValue<int?>("Idempotency:TtlSeconds") ?? Defaults.IdempotencyTtlSeconds;
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
    }

    private IDatabase Db => _redis.GetDatabase();

    /// <inheritdoc />
    /// <remarks>
    /// Uses SET NX (set if not exists) with TTL. Returns false if the key already exists,
    /// meaning the request was already seen.
    /// </remarks>
    public bool TryMarkAsProcessing(string requestId)
    {
        return Db.StringSet(Key(requestId), ProcessingMarker, _ttl, When.NotExists);
    }

    /// <inheritdoc />
    public void SetResponse(string requestId, string serializedResponse)
    {
        Db.StringSet(Key(requestId), serializedResponse, _ttl);
    }

    /// <inheritdoc />
    public string? GetCachedResponse(string requestId)
    {
        var value = Db.StringGet(Key(requestId));
        if (value.IsNullOrEmpty || value == ProcessingMarker)
        {
            return null;
        }
        return value.ToString();
    }

    private static string Key(string requestId) => $"{KeyPrefix}{requestId}";
}
