using Superplay.Shared.Enums;

namespace Superplay.Shared.Messages;

/// <summary>
/// Client-to-server request to transfer resources from the current player to another player.
/// The transfer is atomic: either both balances change or neither does.
/// </summary>
public sealed record SendGiftRequest
{
    /// <summary>The recipient player's identifier. Must differ from the sender.</summary>
    public required string FriendPlayerId { get; init; }

    /// <summary>Which resource type to gift.</summary>
    public required ResourceType ResourceType { get; init; }

    /// <summary>Amount to transfer. Must be a positive value.</summary>
    public long ResourceValue { get; init; }
}
