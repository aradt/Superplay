using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Superplay.Server.Routing;

/// <summary>
/// DI registration helpers for the message-routing infrastructure.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Scans the specified assembly for all <see cref="IMessageHandler"/> implementations and registers them
    /// as singletons, along with a <see cref="MessageRouter"/> that indexes them by message type.
    /// This makes adding new handlers as simple as creating a new class -- no manual wiring needed.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="assembly">
    /// The assembly to scan. Defaults to the calling assembly when null.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMessageHandlers(this IServiceCollection services, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IMessageHandler).IsAssignableFrom(t));

        foreach (var handlerType in handlerTypes)
        {
            services.AddSingleton(typeof(IMessageHandler), handlerType);
        }

        services.AddSingleton<MessageRouter>();

        return services;
    }
}
