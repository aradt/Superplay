using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Superplay.Server.Networking;

/// <summary>
/// Thread-safe in-memory store of active player WebSocket connections.
/// Singleton lifetime — shared across all request-handling threads.
/// </summary>
public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryAdd(string playerId, WebSocket socket)
    {
        return _connections.TryAdd(playerId, socket);
    }

    /// <inheritdoc />
    public bool TryRemove(string playerId)
    {
        return _connections.TryRemove(playerId, out _);
    }

    /// <inheritdoc />
    public WebSocket? GetSocket(string playerId)
    {
        _connections.TryGetValue(playerId, out var socket);
        return socket;
    }

    /// <inheritdoc />
    public bool IsOnline(string playerId)
    {
        return _connections.ContainsKey(playerId);
    }
}
