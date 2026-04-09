using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Superplay.Server.Handlers;
using Superplay.Server.Routing;

namespace Superplay.Tests.Routing;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMessageHandlers_RegistersAllHandlersFromServerAssembly()
    {
        var services = new ServiceCollection();

        // Add required dependencies for the handlers
        services.AddLogging();
        services.AddSingleton<Server.Networking.IConnectionManager, Server.Networking.ConnectionManager>();
        services.AddSingleton<Server.Services.IPlayerRepository>(
            new Moq.Mock<Server.Services.IPlayerRepository>().Object);

        // Use the Server assembly which contains the real handlers
        services.AddMessageHandlers(typeof(LoginHandler).Assembly);

        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IMessageHandler>().ToList();

        // Verify all three handlers are registered
        Assert.Equal(3, handlers.Count);
        Assert.Contains(handlers, h => h.MessageType == "Login");
        Assert.Contains(handlers, h => h.MessageType == "UpdateResources");
        Assert.Contains(handlers, h => h.MessageType == "SendGift");
    }

    [Fact]
    public void AddMessageHandlers_RegistersMessageRouter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Server.Networking.IConnectionManager, Server.Networking.ConnectionManager>();
        services.AddSingleton<Server.Services.IPlayerRepository>(
            new Moq.Mock<Server.Services.IPlayerRepository>().Object);

        services.AddMessageHandlers(typeof(LoginHandler).Assembly);

        var provider = services.BuildServiceProvider();
        var router = provider.GetService<MessageRouter>();

        Assert.NotNull(router);
    }

    [Fact]
    public void AddMessageHandlers_RouterCanResolveRegisteredHandlers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Server.Networking.IConnectionManager, Server.Networking.ConnectionManager>();
        services.AddSingleton<Server.Services.IPlayerRepository>(
            new Moq.Mock<Server.Services.IPlayerRepository>().Object);

        services.AddMessageHandlers(typeof(LoginHandler).Assembly);

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<MessageRouter>();

        Assert.NotNull(router.GetHandler("Login"));
        Assert.NotNull(router.GetHandler("UpdateResources"));
        Assert.NotNull(router.GetHandler("SendGift"));
    }

    [Fact]
    public void AddMessageHandlers_DoesNotRegisterAbstractOrInterfaceTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Server.Networking.IConnectionManager, Server.Networking.ConnectionManager>();
        services.AddSingleton<Server.Services.IPlayerRepository>(
            new Moq.Mock<Server.Services.IPlayerRepository>().Object);

        services.AddMessageHandlers(typeof(LoginHandler).Assembly);

        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IMessageHandler>().ToList();

        // Should only have concrete handler implementations, not IMessageHandler itself
        Assert.All(handlers, h => Assert.False(h.GetType().IsAbstract));
        Assert.All(handlers, h => Assert.False(h.GetType().IsInterface));
    }

    [Fact]
    public void AddMessageHandlers_WithEmptyAssembly_RegistersNoHandlers()
    {
        var services = new ServiceCollection();

        // Use an assembly that has no IMessageHandler implementations
        services.AddMessageHandlers(typeof(Superplay.Shared.Messages.MessageEnvelope).Assembly);

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<MessageRouter>();

        Assert.Empty(router.RegisteredTypes);
    }

    [Fact]
    public void AddMessageHandlers_HandlerInstancesAreResolvedAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Server.Networking.IConnectionManager, Server.Networking.ConnectionManager>();
        services.AddSingleton<Server.Services.IPlayerRepository>(
            new Moq.Mock<Server.Services.IPlayerRepository>().Object);

        services.AddMessageHandlers(typeof(LoginHandler).Assembly);

        var provider = services.BuildServiceProvider();

        var handlers1 = provider.GetServices<IMessageHandler>().ToList();
        var handlers2 = provider.GetServices<IMessageHandler>().ToList();

        // Singleton: same instances on repeated resolution
        for (var i = 0; i < handlers1.Count; i++)
        {
            Assert.Same(handlers1[i], handlers2[i]);
        }
    }
}
