using Superplay.Shared.Enums;

namespace Superplay.Shared.Messages;

/// <summary>
/// Server-to-client response after a successful resource update,
/// confirming the affected resource and the resulting balance.
/// </summary>
public sealed record UpdateResourcesResponse
{
    /// <summary>Which resource was updated.</summary>
    public required ResourceType ResourceType { get; init; }

    /// <summary>The player's balance for this resource after the update.</summary>
    public long NewBalance { get; init; }
}
