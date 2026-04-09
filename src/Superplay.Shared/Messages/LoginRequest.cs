namespace Superplay.Shared.Messages;

/// <summary>
/// Client-to-server request to authenticate or register using a device identifier.
/// If the device is unknown, a new player record is created automatically.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>Unique device identifier used to look up or create the player account.</summary>
    public required string DeviceId { get; init; }
}
