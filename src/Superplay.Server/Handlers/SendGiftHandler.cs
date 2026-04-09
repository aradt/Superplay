using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Superplay.Server.Networking;
using Superplay.Server.Routing;
using Superplay.Server.Services;
using Superplay.Shared.Messages;

namespace Superplay.Server.Handlers;

/// <summary>
/// Handles sending gifts between players.
/// Uses a Lua script for atomic fund validation + transfer.
/// If the recipient is online, pushes a GiftEvent to their WebSocket.
/// </summary>
public sealed class SendGiftHandler : IMessageHandler
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<SendGiftHandler> _logger;

    /// <inheritdoc />
    public string MessageType => "SendGift";

    /// <summary>
    /// Initializes the handler with player persistence, connection tracking, and logging.
    /// </summary>
    public SendGiftHandler(
        IPlayerRepository playerRepository,
        IConnectionManager connectionManager,
        ILogger<SendGiftHandler> logger)
    {
        _playerRepository = playerRepository;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Validates the request, performs an atomic transfer via Lua script,
    /// and pushes a GiftEvent to the recipient if they are online.
    /// The gift is committed regardless of whether the event push succeeds.
    /// </remarks>
    public async Task<object> HandleAsync(string? playerId, string rawPayload, WebSocket socket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            throw new InvalidOperationException("Must be logged in to send gifts");
        }

        var request = JsonSerializer.Deserialize<SendGiftRequest>(rawPayload, SerializerOptions.Default);
        if (request is null || string.IsNullOrWhiteSpace(request.FriendPlayerId))
        {
            throw new ArgumentException("Invalid SendGift request");
        }

        if (request.ResourceValue <= 0)
        {
            throw new ArgumentException("ResourceValue must be positive");
        }

        if (string.Equals(playerId, request.FriendPlayerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Cannot send a gift to yourself");
        }

        // Verify recipient exists
        var recipientExists = await _playerRepository.PlayerExistsAsync(request.FriendPlayerId, cancellationToken);
        if (!recipientExists)
        {
            throw new ArgumentException($"Player {request.FriendPlayerId} does not exist");
        }

        // Atomic transfer via Lua script
        var result = await _playerRepository.TransferResourceAsync(
            playerId, request.FriendPlayerId, request.ResourceType, request.ResourceValue, cancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Insufficient {request.ResourceType} to send gift of {request.ResourceValue}");
        }

        var (senderBalance, recipientBalance) = result.Value;

        _logger.LogInformation(
            "Player {SenderId} gifted {Amount} {ResourceType} to {RecipientId}",
            playerId, request.ResourceValue, request.ResourceType, request.FriendPlayerId);

        // Push GiftEvent to recipient if they are online
        await TryPushGiftEventAsync(
            playerId, request.FriendPlayerId, request.ResourceType, request.ResourceValue,
            recipientBalance, cancellationToken);

        return new SendGiftResponse
        {
            ResourceType = request.ResourceType,
            NewBalance = senderBalance
        };
    }

    /// <summary>
    /// Best-effort push of a GiftEvent to the recipient's WebSocket.
    /// Failures are logged but do not roll back the gift transfer.
    /// </summary>
    private async Task TryPushGiftEventAsync(
        string fromPlayerId,
        string toPlayerId,
        Shared.Enums.ResourceType resourceType,
        long amount,
        long recipientNewBalance,
        CancellationToken cancellationToken)
    {
        var recipientSocket = _connectionManager.GetSocket(toPlayerId);
        if (recipientSocket is null || recipientSocket.State != WebSocketState.Open)
        {
            _logger.LogDebug("Recipient {RecipientId} is offline, gift event not pushed", toPlayerId);
            return;
        }

        try
        {
            var giftEvent = new GiftEvent
            {
                FromPlayerId = fromPlayerId,
                ResourceType = resourceType,
                ResourceValue = amount,
                NewBalance = recipientNewBalance
            };

            var envelope = MessageEnvelope.SuccessResponse("GiftEvent", giftEvent);
            var json = JsonSerializer.Serialize(envelope, SerializerOptions.Default);
            var bytes = Encoding.UTF8.GetBytes(json);

            await recipientSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

            _logger.LogDebug("Pushed GiftEvent to online player {RecipientId}", toPlayerId);
        }
        catch (Exception ex)
        {
            // Don't fail the gift operation if we can't push the event.
            // The recipient will see updated balances on their next action.
            _logger.LogWarning(ex, "Failed to push GiftEvent to player {RecipientId}", toPlayerId);
        }
    }
}
