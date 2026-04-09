using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Superplay.Server.Networking;
using Superplay.Server.Routing;
using Superplay.Server.Services;
using Superplay.Shared.Messages;

namespace Superplay.Server.Handlers;

/// <summary>
/// Handles player login via DeviceId.
/// Creates a new player record on first login, or retrieves the existing one.
/// Rejects login if the player is already connected (duplicate session prevention).
/// </summary>
public sealed class LoginHandler : IMessageHandler
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<LoginHandler> _logger;

    /// <inheritdoc />
    public string MessageType => "Login";

    /// <summary>
    /// Initializes the login handler with player persistence, connection tracking, and logging.
    /// </summary>
    public LoginHandler(
        IPlayerRepository playerRepository,
        IConnectionManager connectionManager,
        ILogger<LoginHandler> logger)
    {
        _playerRepository = playerRepository;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Looks up the player by DeviceId. Creates a new player if the device is unknown.
    /// Rejects the login if the player already has an active WebSocket session.
    /// </remarks>
    public async Task<object> HandleAsync(string? playerId, string rawPayload, WebSocket socket, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<LoginRequest>(rawPayload, SerializerOptions.Default);
        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("DeviceId is required");
        }

        _logger.LogInformation("Login attempt for device {DeviceId}", request.DeviceId);

        // Lookup or create player
        var existingPlayerId = await _playerRepository.GetPlayerIdByDeviceAsync(request.DeviceId, cancellationToken);

        if (existingPlayerId is not null)
        {
            // Check for duplicate connection
            if (_connectionManager.IsOnline(existingPlayerId))
            {
                throw new InvalidOperationException($"Player {existingPlayerId} is already connected");
            }

            var (coins, rolls) = await _playerRepository.GetAllResourcesAsync(existingPlayerId, cancellationToken);

            _logger.LogInformation(
                "Returning player {PlayerId} logged in from device {DeviceId} with Coins={Coins}, Rolls={Rolls}",
                existingPlayerId, request.DeviceId, coins, rolls);

            return new LoginResponse
            {
                PlayerId = existingPlayerId,
                Coins = coins,
                Rolls = rolls
            };
        }

        // New player
        var newPlayerId = await _playerRepository.CreatePlayerAsync(request.DeviceId, cancellationToken);

        _logger.LogInformation("New player {PlayerId} created for device {DeviceId}", newPlayerId, request.DeviceId);

        return new LoginResponse
        {
            PlayerId = newPlayerId,
            Coins = 0,
            Rolls = 0
        };
    }
}
