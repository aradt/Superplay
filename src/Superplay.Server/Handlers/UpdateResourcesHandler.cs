using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Superplay.Server.Routing;
using Superplay.Server.Services;
using Superplay.Shared.Messages;

namespace Superplay.Server.Handlers;

/// <summary>
/// Handles resource updates (add/remove coins or rolls).
/// Uses atomic HINCRBY in Redis to avoid race conditions.
/// </summary>
public sealed class UpdateResourcesHandler : IMessageHandler
{
    private readonly IPlayerRepository _playerRepository;
    private readonly ILogger<UpdateResourcesHandler> _logger;

    /// <inheritdoc />
    public string MessageType => "UpdateResources";

    /// <summary>
    /// Initializes the handler with player persistence and logging.
    /// </summary>
    public UpdateResourcesHandler(IPlayerRepository playerRepository, ILogger<UpdateResourcesHandler> logger)
    {
        _playerRepository = playerRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>Applies a delta to the player's resource balance using atomic Redis HINCRBY.</remarks>
    public async Task<object> HandleAsync(string? playerId, string rawPayload, WebSocket socket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            throw new InvalidOperationException("Must be logged in to update resources");
        }

        var request = JsonSerializer.Deserialize<UpdateResourcesRequest>(rawPayload, SerializerOptions.Default);
        if (request is null)
        {
            throw new ArgumentException("Invalid UpdateResources request");
        }

        if (request.ResourceValue == 0)
        {
            throw new ArgumentException("ResourceValue must be non-zero");
        }

        var newBalance = await _playerRepository.UpdateResourceAsync(
            playerId, request.ResourceType, request.ResourceValue, cancellationToken);

        if (newBalance == -1)
        {
            throw new InvalidOperationException(
                $"Insufficient {request.ResourceType}: update of {request.ResourceValue} would result in a negative balance");
        }

        _logger.LogInformation(
            "Player {PlayerId} updated {ResourceType} by {Delta}, new balance: {NewBalance}",
            playerId, request.ResourceType, request.ResourceValue, newBalance);

        return new UpdateResourcesResponse
        {
            ResourceType = request.ResourceType,
            NewBalance = newBalance
        };
    }
}
