using System.Net.WebSockets;

namespace Superplay.Server.Routing;

/// <summary>
/// Contract for all WebSocket message handlers.
/// Each handler declares its message type and processes incoming messages.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// The message type this handler responds to (e.g., "Login", "UpdateResources", "SendGift").
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Processes the incoming message payload and returns a response payload.
    /// </summary>
    /// <param name="playerId">The authenticated player ID (null for Login handler).</param>
    /// <param name="rawPayload">The raw JSON payload from the message envelope.</param>
    /// <param name="socket">The WebSocket connection for this client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response payload object to be serialized back to the client.</returns>
    Task<object> HandleAsync(string? playerId, string rawPayload, WebSocket socket, CancellationToken cancellationToken);
}
