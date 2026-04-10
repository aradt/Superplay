namespace Superplay.Shared;

/// <summary>
/// Default configuration values shared between server and client.
/// These are used as fallbacks when no explicit configuration is provided.
/// </summary>
public static class Defaults
{
    /// <summary>Default storage provider. "Sqlite" requires no external infrastructure.</summary>
    public const string StorageProvider = "Sqlite";

    /// <summary>SQLite connection string used when no override is configured.</summary>
    public const string SqliteConnectionString = "Data Source=superplay.db";

    /// <summary>Redis connection string used when no override is configured.</summary>
    public const string RedisConnectionString = "localhost:6379";

    /// <summary>Base HTTP URL the server listens on.</summary>
    public const string ServerUrl = "http://localhost:5000";

    /// <summary>Relative path for the WebSocket endpoint on the server.</summary>
    public const string WebSocketEndpoint = "/ws";

    /// <summary>Full WebSocket URL the client connects to by default.</summary>
    public const string ClientWebSocketUrl = "ws://localhost:5000/ws";

    /// <summary>Default idempotency TTL in seconds (5 minutes).</summary>
    public const int IdempotencyTtlSeconds = 300;

    /// <summary>Default idempotency cleanup interval in seconds (1 minute).</summary>
    public const int IdempotencyCleanupIntervalSeconds = 60;
}
