namespace Superplay.Shared.Messages;

/// <summary>
/// Server-to-client response after a successful login, containing the player's identity
/// and current resource balances.
/// </summary>
public sealed record LoginResponse
{
    /// <summary>The server-assigned player identifier (GUID without hyphens).</summary>
    public required string PlayerId { get; init; }

    /// <summary>Current coin balance at the time of login.</summary>
    public long Coins { get; init; }

    /// <summary>Current rolls balance at the time of login.</summary>
    public long Rolls { get; init; }
}
