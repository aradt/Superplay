using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Superplay.Server.Routing;
using Superplay.Server.Services;
using Superplay.Shared.Messages;

namespace Superplay.Server.Networking;

/// <summary>
/// Manages the lifecycle of a single WebSocket connection.
/// Reads messages in a loop, dispatches them to the router, sends back responses,
/// and cleans up the connection on disconnect.
/// </summary>
public sealed class WebSocketHandler
{
    private readonly MessageRouter _router;
    private readonly IConnectionManager _connectionManager;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<WebSocketHandler> _logger;

    /// <summary>Maximum allowed size for a single WebSocket message (64 KB).</summary>
    private const int MaxMessageSize = 64 * 1024;

    /// <summary>
    /// Initializes a new <see cref="WebSocketHandler"/> with its required dependencies.
    /// </summary>
    /// <param name="router">Routes incoming messages to the correct <see cref="IMessageHandler"/>.</param>
    /// <param name="connectionManager">Tracks active player connections for event delivery and duplicate detection.</param>
    /// <param name="logger">Logger scoped to this handler.</param>
    public WebSocketHandler(
        MessageRouter router,
        IConnectionManager connectionManager,
        IIdempotencyStore idempotencyStore,
        ILogger<WebSocketHandler> logger)
    {
        _router = router;
        _connectionManager = connectionManager;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full lifecycle of a single WebSocket connection: read loop, dispatch, response,
    /// and cleanup on disconnect. This method blocks until the connection closes.
    /// </summary>
    /// <param name="socket">The accepted WebSocket connection.</param>
    /// <param name="cancellationToken">Cancelled when the server is shutting down or the request is aborted.</param>
    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        string? playerId = null;
        _logger.LogInformation("New WebSocket connection accepted");

        try
        {
            var buffer = new byte[MaxMessageSize];

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveFullMessageAsync(socket, buffer, cancellationToken);
                if (message is null)
                {
                    break; // Client disconnected
                }

                var response = await ProcessMessageAsync(message, playerId, socket, cancellationToken);

                // Track player ID after successful login
                if (response.Type == "LoginResponse" && response.Success == true)
                {
                    var loginResponse = response.DeserializePayload<LoginResponse>();
                    if (loginResponse is not null)
                    {
                        playerId = loginResponse.PlayerId;
                        _connectionManager.TryAdd(playerId, socket);
                        _logger.LogInformation("Player {PlayerId} registered in connection manager", playerId);
                    }
                }

                await SendResponseAsync(socket, response, cancellationToken);
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("Client disconnected unexpectedly (player: {PlayerId})", playerId ?? "unknown");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Connection cancelled for player {PlayerId}", playerId ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in WebSocket loop for player {PlayerId}", playerId ?? "unknown");
        }
        finally
        {
            await CleanupAsync(socket, playerId);
        }
    }

    /// <summary>
    /// Reads a complete WebSocket message, accumulating fragments until EndOfMessage.
    /// Returns null if the client sent a Close frame or the message exceeds <see cref="MaxMessageSize"/>.
    /// </summary>
    private async Task<string?> ReceiveFullMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);

            if (ms.Length > MaxMessageSize)
            {
                _logger.LogWarning("Message exceeded maximum size of {MaxSize} bytes, closing connection", MaxMessageSize);
                return null;
            }
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Deserializes the raw JSON message, enforces authentication, and dispatches to the appropriate handler.
    /// Returns the response envelope to send back to the client.
    /// </summary>
    private async Task<MessageEnvelope> ProcessMessageAsync(
        string rawMessage, string? playerId, WebSocket socket, CancellationToken cancellationToken)
    {
        MessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MessageEnvelope>(rawMessage, SerializerOptions.Default);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize message");
            return MessageEnvelope.ErrorResponse("Error", "Invalid JSON format");
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            return MessageEnvelope.ErrorResponse("Error", "Message type is required");
        }

        var responseType = $"{envelope.Type}Response";

        // Require login before any other operation (except Login itself)
        if (playerId is null && !string.Equals(envelope.Type, "Login", StringComparison.OrdinalIgnoreCase))
        {
            return MessageEnvelope.ErrorResponse(responseType, "Must login first");
        }

        var handler = _router.GetHandler(envelope.Type);
        if (handler is null)
        {
            _logger.LogWarning("No handler registered for message type {MessageType}", envelope.Type);
            return MessageEnvelope.ErrorResponse(responseType, $"Unknown message type: {envelope.Type}");
        }

        // Idempotency check: skip Login (idempotent by nature) and messages without a RequestId
        if (!string.IsNullOrEmpty(envelope.RequestId)
            && !string.Equals(envelope.Type, "Login", StringComparison.OrdinalIgnoreCase))
        {
            var cached = _idempotencyStore.GetCachedResponse(envelope.RequestId);
            if (cached is not null)
            {
                _logger.LogInformation("Duplicate request {RequestId} for {MessageType}, returning cached response",
                    envelope.RequestId, envelope.Type);
                return JsonSerializer.Deserialize<MessageEnvelope>(cached, SerializerOptions.Default)!;
            }

            if (!_idempotencyStore.TryMarkAsProcessing(envelope.RequestId))
            {
                _logger.LogWarning("Request {RequestId} is already being processed", envelope.RequestId);
                return MessageEnvelope.ErrorResponse(responseType, "Request is already being processed");
            }
        }

        try
        {
            var rawPayload = envelope.Payload?.GetRawText() ?? "{}";
            var result = await handler.HandleAsync(playerId, rawPayload, socket, cancellationToken);
            var response = MessageEnvelope.SuccessResponse(responseType, result);
            CacheResponse(envelope.RequestId, response);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            // Business rule violations (insufficient funds, already connected, etc.)
            _logger.LogWarning("{MessageType} rejected: {Reason}", envelope.Type, ex.Message);
            var response = MessageEnvelope.ErrorResponse(responseType, ex.Message);
            CacheResponse(envelope.RequestId, response);
            return response;
        }
        catch (ArgumentException ex)
        {
            // Validation errors (invalid payload, missing fields, etc.)
            _logger.LogWarning("{MessageType} validation failed: {Reason}", envelope.Type, ex.Message);
            var response = MessageEnvelope.ErrorResponse(responseType, ex.Message);
            CacheResponse(envelope.RequestId, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling {MessageType}", envelope.Type);
            return MessageEnvelope.ErrorResponse(responseType, "An internal error occurred");
        }
    }

    /// <summary>
    /// Caches the response for a request ID so duplicate requests return the same result.
    /// </summary>
    private void CacheResponse(string? requestId, MessageEnvelope response)
    {
        if (string.IsNullOrEmpty(requestId)) return;
        var json = JsonSerializer.Serialize(response, SerializerOptions.Default);
        _idempotencyStore.SetResponse(requestId, json);
    }

    /// <summary>
    /// Serializes and sends a response envelope back to the client. No-ops if the socket is no longer open.
    /// </summary>
    private static async Task SendResponseAsync(WebSocket socket, MessageEnvelope response, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(response, SerializerOptions.Default);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Removes the player from the connection manager and performs a graceful WebSocket close handshake.
    /// </summary>
    private async Task CleanupAsync(WebSocket socket, string? playerId)
    {
        if (playerId is not null)
        {
            _connectionManager.TryRemove(playerId);
            _logger.LogInformation("Player {PlayerId} removed from connection manager", playerId);
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Server closing connection",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during WebSocket close handshake");
            }
        }

        _logger.LogInformation("WebSocket connection cleaned up (player: {PlayerId})", playerId ?? "unknown");
    }
}
