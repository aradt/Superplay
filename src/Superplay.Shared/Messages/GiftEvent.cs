using Superplay.Shared.Enums;

namespace Superplay.Shared.Messages;

/// <summary>
/// Server-push event sent to an online recipient when another player gifts them resources.
/// Delivered as a best-effort notification; the gift is committed regardless of delivery success.
/// </summary>
public sealed record GiftEvent
{
    /// <summary>The player who sent the gift.</summary>
    public required string FromPlayerId { get; init; }

    /// <summary>Which resource type was gifted.</summary>
    public required ResourceType ResourceType { get; init; }

    /// <summary>Amount of the resource that was gifted.</summary>
    public long ResourceValue { get; init; }

    /// <summary>The recipient's updated balance for this resource after receiving the gift.</summary>
    public long NewBalance { get; init; }
}
