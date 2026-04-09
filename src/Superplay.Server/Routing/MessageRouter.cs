namespace Superplay.Server.Routing;

/// <summary>
/// Routes incoming messages to the appropriate handler based on message type.
/// Handlers are registered at startup via dependency injection.
/// </summary>
public sealed class MessageRouter
{
    private readonly Dictionary<string, IMessageHandler> _handlers;

    /// <summary>
    /// Initializes the router by building a case-insensitive lookup of handlers keyed by their message type.
    /// </summary>
    /// <param name="handlers">All registered <see cref="IMessageHandler"/> instances from DI.</param>
    public MessageRouter(IEnumerable<IMessageHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            h => h.MessageType,
            h => h,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Looks up a handler for the given message type.
    /// </summary>
    public IMessageHandler? GetHandler(string messageType)
    {
        _handlers.TryGetValue(messageType, out var handler);
        return handler;
    }

    /// <summary>
    /// Returns all registered message types (useful for logging on startup).
    /// </summary>
    public IReadOnlyCollection<string> RegisteredTypes => _handlers.Keys;
}
