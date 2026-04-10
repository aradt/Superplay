using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Superplay.Server.Handlers;
using Superplay.Server.Networking;
using Superplay.Server.Services;
using Superplay.Shared;
using Superplay.Shared.Messages;

namespace Superplay.Tests.Handlers;

public sealed class LoginHandlerTests
{
    private readonly Mock<IPlayerRepository> _repoMock = new();
    private readonly Mock<IConnectionManager> _connMock = new();
    private readonly Mock<ILogger<LoginHandler>> _loggerMock = new();
    private readonly LoginHandler _sut;
    private readonly Mock<WebSocket> _socketMock = new();

    public LoginHandlerTests()
    {
        _sut = new LoginHandler(_repoMock.Object, _connMock.Object, _loggerMock.Object);
        _socketMock.Setup(s => s.State).Returns(WebSocketState.Open);
    }

    private static string SerializePayload(object payload)
    {
        return JsonSerializer.Serialize(payload, SerializerOptions.Default);
    }

    [Fact]
    public void MessageType_IsLogin()
    {
        Assert.Equal("Login", _sut.MessageType);
    }

    [Fact]
    public async Task HandleAsync_NewDevice_CreatesPlayerAndReturnsZeroBalances()
    {
        var request = new LoginRequest { DeviceId = "new-device" };
        _repoMock.Setup(r => r.GetPlayerIdByDeviceAsync("new-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _repoMock.Setup(r => r.CreatePlayerAsync("new-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync("player-new");

        var result = await _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<LoginResponse>(result);
        Assert.Equal("player-new", response.PlayerId);
        Assert.Equal(0, response.Coins);
        Assert.Equal(0, response.Rolls);
        _repoMock.Verify(r => r.CreatePlayerAsync("new-device", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExistingDevice_ReturnsExistingPlayerWithBalances()
    {
        var request = new LoginRequest { DeviceId = "existing-device" };
        _repoMock.Setup(r => r.GetPlayerIdByDeviceAsync("existing-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync("player-existing");
        _connMock.Setup(c => c.IsOnline("player-existing")).Returns(false);
        _repoMock.Setup(r => r.GetAllResourcesAsync("player-existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((100L, 50L));

        var result = await _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<LoginResponse>(result);
        Assert.Equal("player-existing", response.PlayerId);
        Assert.Equal(100, response.Coins);
        Assert.Equal(50, response.Rolls);
        _repoMock.Verify(r => r.CreatePlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingDevice_AlreadyConnected_ThrowsInvalidOperation()
    {
        var request = new LoginRequest { DeviceId = "existing-device" };
        _repoMock.Setup(r => r.GetPlayerIdByDeviceAsync("existing-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync("player-existing");
        _connMock.Setup(c => c.IsOnline("player-existing")).Returns(true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("already connected", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_EmptyDeviceId_ThrowsArgumentException()
    {
        var request = new LoginRequest { DeviceId = "" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WhitespaceDeviceId_ThrowsArgumentException()
    {
        var request = new LoginRequest { DeviceId = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyJsonPayload_ThrowsJsonException_DueToMissingRequiredProperty()
    {
        // LoginRequest.DeviceId is `required`, so deserializing "{}" throws JsonException
        await Assert.ThrowsAsync<JsonException>(() =>
            _sut.HandleAsync(null, "{}", _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MalformedJson_ThrowsJsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() =>
            _sut.HandleAsync(null, "not-json", _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NewDevice_DoesNotCheckConnectionManager()
    {
        var request = new LoginRequest { DeviceId = "brand-new" };
        _repoMock.Setup(r => r.GetPlayerIdByDeviceAsync("brand-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _repoMock.Setup(r => r.CreatePlayerAsync("brand-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-player-id");

        await _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None);

        _connMock.Verify(c => c.IsOnline(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DeviceIdTooLong_ThrowsArgumentException()
    {
        var request = new LoginRequest { DeviceId = new string('x', Defaults.MaxDeviceIdLength + 1) };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("must not exceed 256 characters", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_DeviceIdAtMaxLength_Succeeds()
    {
        var deviceId = new string('x', Defaults.MaxDeviceIdLength);
        var request = new LoginRequest { DeviceId = deviceId };
        _repoMock.Setup(r => r.GetPlayerIdByDeviceAsync(deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _repoMock.Setup(r => r.CreatePlayerAsync(deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("player-max-device");

        var result = await _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<LoginResponse>(result);
        Assert.Equal("player-max-device", response.PlayerId);
        Assert.Equal(0, response.Coins);
        Assert.Equal(0, response.Rolls);
    }
}
