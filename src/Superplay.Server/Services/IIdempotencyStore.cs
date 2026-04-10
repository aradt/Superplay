namespace Superplay.Server.Services;

/// <summary>
/// Tracks processed request IDs to prevent duplicate execution.
/// Implementations should auto-expire entries after a configurable TTL.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Tries to mark a request ID as being processed.
    /// Returns false if the request ID was already seen (duplicate).
    /// </summary>
    bool TryMarkAsProcessing(string requestId);

    /// <summary>
    /// Stores the cached response for a completed request.
    /// </summary>
    void SetResponse(string requestId, string serializedResponse);

    /// <summary>
    /// Gets the cached response for a previously completed request, or null if not found.
    /// </summary>
    string? GetCachedResponse(string requestId);
}
