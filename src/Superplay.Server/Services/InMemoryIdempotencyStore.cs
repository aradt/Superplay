using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Superplay.Shared;

namespace Superplay.Server.Services;

/// <summary>
/// In-memory idempotency store using ConcurrentDictionary with automatic TTL-based expiry.
/// Entries older than the configured TTL are cleaned up periodically.
///
/// Configuration (appsettings.json):
///   "Idempotency": { "TtlSeconds": 300, "CleanupIntervalSeconds": 60 }
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore, IDisposable
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _entries = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _entryTtl;

    public InMemoryIdempotencyStore(IConfiguration configuration)
    {
        var ttlSeconds = configuration.GetValue<int?>("Idempotency:TtlSeconds") ?? Defaults.IdempotencyTtlSeconds;
        var cleanupSeconds = configuration.GetValue<int?>("Idempotency:CleanupIntervalSeconds") ?? Defaults.IdempotencyCleanupIntervalSeconds;

        _entryTtl = TimeSpan.FromSeconds(ttlSeconds);
        var cleanupInterval = TimeSpan.FromSeconds(cleanupSeconds);

        _cleanupTimer = new Timer(_ => Cleanup(), null, cleanupInterval, cleanupInterval);
    }

    /// <inheritdoc />
    public bool TryMarkAsProcessing(string requestId)
    {
        var entry = new IdempotencyEntry { CreatedAt = DateTime.UtcNow };
        return _entries.TryAdd(requestId, entry);
    }

    /// <inheritdoc />
    public void SetResponse(string requestId, string serializedResponse)
    {
        if (_entries.TryGetValue(requestId, out var entry))
        {
            entry.Response = serializedResponse;
        }
    }

    /// <inheritdoc />
    public string? GetCachedResponse(string requestId)
    {
        if (_entries.TryGetValue(requestId, out var entry))
        {
            return entry.Response;
        }
        return null;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - _entryTtl;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>Stops the cleanup timer.</summary>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    private sealed class IdempotencyEntry
    {
        public DateTime CreatedAt { get; init; }
        public string? Response { get; set; }
    }
}
