using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Serilog;
using Superplay.Shared.Messages;

namespace Superplay.Client;

/// <summary>
/// WebSocket game client that communicates with the Superplay server.
///
/// Uses a single background read loop that classifies incoming messages:
///   - Response messages (type ends with "Response") are routed to a Channel
///     where SendAndReceiveAsync awaits them.
///   - Server-push events (e.g., "GiftEvent") are handled inline by the listener.
///
/// This avoids concurrent reads on the ClientWebSocket while still supporting
/// asynchronous event delivery.
/// </summary>
public sealed class GameClient : IDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ILogger _logger;
    private readonly Uri _serverUri;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<MessageEnvelope> _responseChannel = Channel.CreateUnbounded<MessageEnvelope>();
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    /// <summary>Maximum allowed size for a single WebSocket message (64 KB).</summary>
    private const int MaxMessageSize = 64 * 1024;

    /// <summary>The player ID assigned by the server after a successful login.</summary>
    public string? PlayerId { get; private set; }

    /// <summary>Whether the underlying WebSocket connection is currently open.</summary>
    public bool IsConnected => _socket.State == WebSocketState.Open;

    /// <summary>
    /// Creates a new game client targeting the specified server URL.
    /// </summary>
    /// <param name="serverUrl">WebSocket URL of the game server (e.g., ws://localhost:5000/ws).</param>
    /// <param name="logger">Serilog logger instance for structured logging.</param>
    public GameClient(string serverUrl, ILogger logger)
    {
        _serverUri = new Uri(serverUrl);
        _logger = logger;
    }

    /// <summary>
    /// Connects to the game server and starts the background read loop for incoming messages.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Connecting to {ServerUri}...", _serverUri);
        await _socket.ConnectAsync(_serverUri, cancellationToken);
        _logger.Information("Connected to server");

        // Start single background read loop
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ReadLoopAsync(_listenerCts.Token), _listenerCts.Token);
    }

    /// <summary>
    /// Sends a Login request with the given device ID and stores the returned player ID on success.
    /// </summary>
    public async Task<LoginResponse?> LoginAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest { DeviceId = deviceId };
        var response = await SendAndReceiveAsync<LoginResponse>("Login", request, cancellationToken);

        if (response is not null)
        {
            PlayerId = response.PlayerId;
            _logger.Information("Logged in as player {PlayerId} (Coins: {Coins}, Rolls: {Rolls})",
                response.PlayerId, response.Coins, response.Rolls);
        }

        return response;
    }

    /// <summary>
    /// Sends an UpdateResources request to adjust the player's balance for the given resource.
    /// </summary>
    public async Task<UpdateResourcesResponse?> UpdateResourcesAsync(
        Shared.Enums.ResourceType resourceType, long value, CancellationToken cancellationToken = default)
    {
        var request = new UpdateResourcesRequest { ResourceType = resourceType, ResourceValue = value };
        var response = await SendAndReceiveAsync<UpdateResourcesResponse>("UpdateResources", request, cancellationToken);

        if (response is not null)
        {
            _logger.Information("{ResourceType} updated. New balance: {Balance}",
                response.ResourceType, response.NewBalance);
        }

        return response;
    }

    /// <summary>
    /// Sends a gift of the specified resource to another player.
    /// </summary>
    public async Task<SendGiftResponse?> SendGiftAsync(
        string friendPlayerId,
        Shared.Enums.ResourceType resourceType,
        long value,
        CancellationToken cancellationToken = default)
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = friendPlayerId,
            ResourceType = resourceType,
            ResourceValue = value
        };
        var response = await SendAndReceiveAsync<SendGiftResponse>("SendGift", request, cancellationToken);

        if (response is not null)
        {
            _logger.Information("Gift sent. Your {ResourceType} balance: {Balance}",
                response.ResourceType, response.NewBalance);
        }

        return response;
    }

    /// <summary>
    /// Gracefully disconnects from the server, cancels the read loop, and completes the response channel.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_listenerCts is not null)
        {
            await _listenerCts.CancelAsync();
        }

        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during disconnect");
            }
        }

        // Signal the response channel so any awaiting reader unblocks
        _responseChannel.Writer.TryComplete();

        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger.Information("Disconnected from server");
    }

    /// <summary>
    /// Sends a request and waits for the corresponding response from the background read loop.
    /// Uses a SemaphoreSlim to prevent interleaved WebSocket writes.
    /// </summary>
    private async Task<TResponse?> SendAndReceiveAsync<TResponse>(
        string messageType, object payload, CancellationToken cancellationToken) where TResponse : class
    {
        var envelope = MessageEnvelope.Request(messageType, payload);
        var json = JsonSerializer.Serialize(envelope, SerializerOptions.Default);
        var bytes = Encoding.UTF8.GetBytes(json);

        // Serialize sends to avoid interleaving frames
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            _logger.Debug("Sending {MessageType} request", messageType);

            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }

        // Wait for the response from the background read loop
        MessageEnvelope response;
        try
        {
            response = await _responseChannel.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            _logger.Error("Connection closed while waiting for {MessageType} response", messageType);
            return null;
        }

        if (response.Success != true)
        {
            _logger.Warning("{MessageType}: {Error}", messageType, response.Error);
            return null;
        }

        return response.DeserializePayload<TResponse>();
    }

    /// <summary>
    /// Single read loop running on a background task.
    /// All reads go through here to avoid concurrent WebSocket reads.
    /// Messages are classified as either responses (routed to the channel) or events (handled inline).
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxMessageSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveFullMessageAsync(buffer, cancellationToken);
                if (message is null)
                {
                    break; // Connection closed
                }

                var envelope = JsonSerializer.Deserialize<MessageEnvelope>(message, SerializerOptions.Default);
                if (envelope is null)
                {
                    _logger.Warning("Received unparseable message, ignoring");
                    continue;
                }

                if (IsServerPushEvent(envelope.Type))
                {
                    HandleEvent(envelope);
                }
                else
                {
                    // Response to a request -- route to the awaiting caller
                    await _responseChannel.Writer.WriteAsync(envelope, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.Warning(ex, "WebSocket error in read loop");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error in read loop");
        }
        finally
        {
            _responseChannel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Reads a complete WebSocket message, accumulating fragments until EndOfMessage.
    /// Returns null on close or if the message exceeds <see cref="MaxMessageSize"/>.
    /// </summary>
    private async Task<string?> ReceiveFullMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);

            if (ms.Length > MaxMessageSize)
            {
                _logger.Warning("Message exceeded {MaxSize} bytes, dropping", MaxMessageSize);
                return null;
            }
        }
        while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(ms.ToArray());
        _logger.Debug("Received: {Json}", json);
        return json;
    }

    /// <summary>
    /// Server-push events are messages whose type does NOT end with "Response".
    /// E.g., "GiftEvent", "Error".
    /// </summary>
    private static bool IsServerPushEvent(string messageType)
    {
        return messageType.EndsWith("Event", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Processes a server-push event (e.g., GiftEvent) and logs it to the console.
    /// </summary>
    private void HandleEvent(MessageEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "GiftEvent":
                var gift = envelope.DeserializePayload<GiftEvent>();
                if (gift is not null)
                {
                    _logger.Information(
                        "[EVENT] Received gift from {FromPlayerId}: {Amount} {ResourceType} (new balance: {Balance})",
                        gift.FromPlayerId, gift.ResourceValue, gift.ResourceType, gift.NewBalance);
                }
                break;

            default:
                _logger.Information("[EVENT] {Type}: {Payload}",
                    envelope.Type, envelope.Payload?.GetRawText() ?? "{}");
                break;
        }
    }

    /// <summary>
    /// Releases the WebSocket, cancellation token source, and send lock.
    /// </summary>
    public void Dispose()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _sendLock.Dispose();
        _socket.Dispose();
    }
}
