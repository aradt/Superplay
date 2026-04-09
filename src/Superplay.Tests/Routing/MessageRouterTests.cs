using System.Net.WebSockets;
using Moq;
using Superplay.Server.Routing;

namespace Superplay.Tests.Routing;

public sealed class MessageRouterTests
{
    private static Mock<IMessageHandler> CreateMockHandler(string messageType)
    {
        var mock = new Mock<IMessageHandler>();
        mock.Setup(h => h.MessageType).Returns(messageType);
        return mock;
    }

    [Fact]
    public void GetHandler_ReturnsCorrectHandler_ForRegisteredType()
    {
        var loginHandler = CreateMockHandler("Login");
        var giftHandler = CreateMockHandler("SendGift");
        var router = new MessageRouter([loginHandler.Object, giftHandler.Object]);

        var result = router.GetHandler("Login");

        Assert.Same(loginHandler.Object, result);
    }

    [Fact]
    public void GetHandler_ReturnsNull_ForUnknownType()
    {
        var handler = CreateMockHandler("Login");
        var router = new MessageRouter([handler.Object]);

        var result = router.GetHandler("NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetHandler_IsCaseInsensitive()
    {
        var handler = CreateMockHandler("Login");
        var router = new MessageRouter([handler.Object]);

        Assert.Same(handler.Object, router.GetHandler("login"));
        Assert.Same(handler.Object, router.GetHandler("LOGIN"));
        Assert.Same(handler.Object, router.GetHandler("Login"));
    }

    [Fact]
    public void Constructor_ThrowsOnDuplicateMessageType()
    {
        var handler1 = CreateMockHandler("Login");
        var handler2 = CreateMockHandler("Login");

        Assert.Throws<ArgumentException>(() => new MessageRouter([handler1.Object, handler2.Object]));
    }

    [Fact]
    public void Constructor_ThrowsOnDuplicateMessageType_CaseInsensitive()
    {
        var handler1 = CreateMockHandler("Login");
        var handler2 = CreateMockHandler("login");

        Assert.Throws<ArgumentException>(() => new MessageRouter([handler1.Object, handler2.Object]));
    }

    [Fact]
    public void RegisteredTypes_ReturnsAllRegisteredTypes()
    {
        var handler1 = CreateMockHandler("Login");
        var handler2 = CreateMockHandler("SendGift");
        var handler3 = CreateMockHandler("UpdateResources");
        var router = new MessageRouter([handler1.Object, handler2.Object, handler3.Object]);

        var types = router.RegisteredTypes;

        Assert.Equal(3, types.Count);
        Assert.Contains("Login", types);
        Assert.Contains("SendGift", types);
        Assert.Contains("UpdateResources", types);
    }

    [Fact]
    public void RegisteredTypes_ReturnsEmptyCollection_WhenNoHandlers()
    {
        var router = new MessageRouter([]);

        Assert.Empty(router.RegisteredTypes);
    }

    [Fact]
    public void GetHandler_ReturnsNull_WhenNoHandlersRegistered()
    {
        var router = new MessageRouter([]);

        Assert.Null(router.GetHandler("Login"));
    }

    [Fact]
    public void GetHandler_RoutesToCorrectHandler_WithMultipleRegistered()
    {
        var login = CreateMockHandler("Login");
        var gift = CreateMockHandler("SendGift");
        var update = CreateMockHandler("UpdateResources");
        var router = new MessageRouter([login.Object, gift.Object, update.Object]);

        Assert.Same(login.Object, router.GetHandler("Login"));
        Assert.Same(gift.Object, router.GetHandler("SendGift"));
        Assert.Same(update.Object, router.GetHandler("UpdateResources"));
    }
}
