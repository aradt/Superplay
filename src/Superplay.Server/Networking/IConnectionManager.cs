using System.Net.WebSockets;

namespace Superplay.Server.Networking;

/// <summary>
/// Manages active WebSocket connections indexed by player ID.
/// Used for duplicate login detection and pushing events to online players.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Registers a player's WebSocket connection. Returns false if the player is already connected.
    /// </summary>
    bool TryAdd(string playerId, WebSocket socket);

    /// <summary>
    /// Removes a player's WebSocket connection.
    /// </summary>
    bool TryRemove(string playerId);

    /// <summary>
    /// Gets the WebSocket for an online player, or null if not connected.
    /// </summary>
    WebSocket? GetSocket(string playerId);

    /// <summary>
    /// Checks whether a player is currently connected.
    /// </summary>
    bool IsOnline(string playerId);
}
