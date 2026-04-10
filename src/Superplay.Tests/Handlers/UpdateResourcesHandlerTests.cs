using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Superplay.Server.Handlers;
using Superplay.Server.Services;
using Superplay.Shared.Enums;
using Superplay.Shared.Messages;

namespace Superplay.Tests.Handlers;

public sealed class UpdateResourcesHandlerTests
{
    private readonly Mock<IPlayerRepository> _repoMock = new();
    private readonly Mock<ILogger<UpdateResourcesHandler>> _loggerMock = new();
    private readonly UpdateResourcesHandler _sut;
    private readonly Mock<WebSocket> _socketMock = new();

    public UpdateResourcesHandlerTests()
    {
        _sut = new UpdateResourcesHandler(_repoMock.Object, _loggerMock.Object);
        _socketMock.Setup(s => s.State).Returns(WebSocketState.Open);
    }

    private static string SerializePayload(object payload)
    {
        return JsonSerializer.Serialize(payload, SerializerOptions.Default);
    }

    [Fact]
    public void MessageType_IsUpdateResources()
    {
        Assert.Equal("UpdateResources", _sut.MessageType);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesCoinsAndReturnsNewBalance()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 50 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Coins, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(150);

        var result = await _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<UpdateResourcesResponse>(result);
        Assert.Equal(ResourceType.Coins, response.ResourceType);
        Assert.Equal(150, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesRolls()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Rolls, ResourceValue = 10 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Rolls, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30);

        var result = await _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<UpdateResourcesResponse>(result);
        Assert.Equal(ResourceType.Rolls, response.ResourceType);
        Assert.Equal(30, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_NegativeDelta_DeductsAndReturnsNewBalance()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = -20 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Coins, -20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(80);

        var result = await _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<UpdateResourcesResponse>(result);
        Assert.Equal(80, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_NullPlayerId_ThrowsInvalidOperation()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 10 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyPlayerId_ThrowsInvalidOperation()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 10 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync("", SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ZeroResourceValue_ThrowsArgumentException()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 0 };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("non-zero", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_InvalidPayload_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("player-1", "null", _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_CallsRepositoryWithCorrectParameters()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Rolls, ResourceValue = 7 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-42", ResourceType.Rolls, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        await _sut.HandleAsync("player-42", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        _repoMock.Verify(r => r.UpdateResourceAsync("player-42", ResourceType.Rolls, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NegativeDelta_WouldGoBelowZero_ThrowsInvalidOperation()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = -100 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Coins, -100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(-1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("Insufficient Coins", exception.Message);
        Assert.Contains("-100", exception.Message);
        Assert.Contains("negative balance", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_NegativeDelta_BalanceStaysAtZero_Succeeds()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Rolls, ResourceValue = -10 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Rolls, -10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<UpdateResourcesResponse>(result);
        Assert.Equal(ResourceType.Rolls, response.ResourceType);
        Assert.Equal(0, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_PositiveDelta_AlwaysSucceeds()
    {
        var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 500 };
        _repoMock.Setup(r => r.UpdateResourceAsync("player-1", ResourceType.Coins, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(500);

        var result = await _sut.HandleAsync("player-1", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<UpdateResourcesResponse>(result);
        Assert.Equal(ResourceType.Coins, response.ResourceType);
        Assert.Equal(500, response.NewBalance);
    }
}
