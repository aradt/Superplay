using Superplay.Shared.Enums;

namespace Superplay.Shared.Messages;

/// <summary>
/// Server-to-client response after a successful gift transfer,
/// confirming the sender's updated balance for the gifted resource.
/// </summary>
public sealed record SendGiftResponse
{
    /// <summary>Which resource was gifted.</summary>
    public required ResourceType ResourceType { get; init; }

    /// <summary>The sender's balance for this resource after the transfer.</summary>
    public long NewBalance { get; init; }
}
