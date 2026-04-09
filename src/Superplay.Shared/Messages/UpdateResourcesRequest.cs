using Superplay.Shared.Enums;

namespace Superplay.Shared.Messages;

/// <summary>
/// Client-to-server request to adjust a player's resource balance.
/// Positive values add resources; negative values subtract.
/// </summary>
public sealed record UpdateResourcesRequest
{
    /// <summary>Which resource to update.</summary>
    public required ResourceType ResourceType { get; init; }

    /// <summary>
    /// The delta to apply. Positive adds to the balance, negative subtracts.
    /// Must be non-zero.
    /// </summary>
    public long ResourceValue { get; init; }
}
