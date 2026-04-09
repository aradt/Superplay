using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Superplay.Server.Handlers;
using Superplay.Server.Networking;
using Superplay.Server.Services;
using Superplay.Shared.Enums;
using Superplay.Shared.Messages;

namespace Superplay.Tests.Handlers;

public sealed class SendGiftHandlerTests
{
    private readonly Mock<IPlayerRepository> _repoMock = new();
    private readonly Mock<IConnectionManager> _connMock = new();
    private readonly Mock<ILogger<SendGiftHandler>> _loggerMock = new();
    private readonly SendGiftHandler _sut;
    private readonly Mock<WebSocket> _socketMock = new();

    public SendGiftHandlerTests()
    {
        _sut = new SendGiftHandler(_repoMock.Object, _connMock.Object, _loggerMock.Object);
        _socketMock.Setup(s => s.State).Returns(WebSocketState.Open);
    }

    private static string SerializePayload(object payload)
    {
        return JsonSerializer.Serialize(payload, SerializerOptions.Default);
    }

    [Fact]
    public void MessageType_IsSendGift()
    {
        Assert.Equal("SendGift", _sut.MessageType);
    }

    [Fact]
    public async Task HandleAsync_ValidGift_DeductsFromSenderAndCreditsReceiver()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 50
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((150L, 50L));
        _connMock.Setup(c => c.GetSocket("friend-1")).Returns((WebSocket?)null);

        var result = await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<SendGiftResponse>(result);
        Assert.Equal(ResourceType.Coins, response.ResourceType);
        Assert.Equal(150, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_InsufficientFunds_ThrowsInvalidOperation()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 9999
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 9999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((long, long)?)null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("Insufficient", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_RecipientDoesNotExist_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "unknown-player",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("unknown-player", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_NullPlayerId_ThrowsInvalidOperation()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync(null, SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyPlayerId_ThrowsInvalidOperation()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.HandleAsync("", SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_SendGiftToSelf_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "sender",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("yourself", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_SendGiftToSelf_CaseInsensitive_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "SENDER",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("yourself", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_ZeroResourceValue_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 0
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_NegativeResourceValue_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = -10
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_EmptyFriendPlayerId_ThrowsArgumentException()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RecipientOnline_PushesGiftEvent()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 25
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((75L, 125L));

        var recipientSocket = new Mock<WebSocket>();
        recipientSocket.Setup(s => s.State).Returns(WebSocketState.Open);
        recipientSocket
            .Setup(s => s.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _connMock.Setup(c => c.GetSocket("friend-1")).Returns(recipientSocket.Object);

        await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        // Verify a message was sent to recipient's socket
        recipientSocket.Verify(
            s => s.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RecipientOnline_GiftEventContainsCorrectData()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Rolls,
            ResourceValue = 5
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Rolls, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((10L, 15L));

        ArraySegment<byte> capturedBytes = default;
        var recipientSocket = new Mock<WebSocket>();
        recipientSocket.Setup(s => s.State).Returns(WebSocketState.Open);
        recipientSocket
            .Setup(s => s.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((bytes, _, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        _connMock.Setup(c => c.GetSocket("friend-1")).Returns(recipientSocket.Object);

        await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        // Deserialize the captured GiftEvent envelope
        var json = System.Text.Encoding.UTF8.GetString(capturedBytes.Array!, capturedBytes.Offset, capturedBytes.Count);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, SerializerOptions.Default);
        Assert.NotNull(envelope);
        Assert.Equal("GiftEvent", envelope.Type);
        Assert.True(envelope.Success);

        var giftEvent = envelope.DeserializePayload<GiftEvent>();
        Assert.NotNull(giftEvent);
        Assert.Equal("sender", giftEvent.FromPlayerId);
        Assert.Equal(ResourceType.Rolls, giftEvent.ResourceType);
        Assert.Equal(5, giftEvent.ResourceValue);
        Assert.Equal(15, giftEvent.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_RecipientOffline_DoesNotAttemptSend()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((90L, 10L));
        _connMock.Setup(c => c.GetSocket("friend-1")).Returns((WebSocket?)null);

        var result = await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        // Gift still succeeds even though recipient is offline
        var response = Assert.IsType<SendGiftResponse>(result);
        Assert.Equal(90, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_GiftEventPushFails_DoesNotFailGiftOperation()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((90L, 10L));

        var recipientSocket = new Mock<WebSocket>();
        recipientSocket.Setup(s => s.State).Returns(WebSocketState.Open);
        recipientSocket
            .Setup(s => s.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSocketException("Connection lost"));

        _connMock.Setup(c => c.GetSocket("friend-1")).Returns(recipientSocket.Object);

        // Should not throw — the gift transfer already succeeded
        var result = await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<SendGiftResponse>(result);
        Assert.Equal(90, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_ValidGift_ReturnsRollsCorrectly()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Rolls,
            ResourceValue = 3
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Rolls, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync((7L, 3L));
        _connMock.Setup(c => c.GetSocket("friend-1")).Returns((WebSocket?)null);

        var result = await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<SendGiftResponse>(result);
        Assert.Equal(ResourceType.Rolls, response.ResourceType);
        Assert.Equal(7, response.NewBalance);
    }

    [Fact]
    public async Task HandleAsync_VerifiesRecipientExistsBeforeTransfer()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "nonexistent",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None));

        // TransferResourceAsync should never be called if the recipient doesn't exist
        _repoMock.Verify(
            r => r.TransferResourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ResourceType>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RecipientSocketClosedState_DoesNotSend()
    {
        var request = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 10
        };
        _repoMock.Setup(r => r.PlayerExistsAsync("friend-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.TransferResourceAsync("sender", "friend-1", ResourceType.Coins, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((90L, 10L));

        var recipientSocket = new Mock<WebSocket>();
        recipientSocket.Setup(s => s.State).Returns(WebSocketState.Closed);
        _connMock.Setup(c => c.GetSocket("friend-1")).Returns(recipientSocket.Object);

        var result = await _sut.HandleAsync("sender", SerializePayload(request), _socketMock.Object, CancellationToken.None);

        var response = Assert.IsType<SendGiftResponse>(result);
        Assert.Equal(90, response.NewBalance);

        recipientSocket.Verify(
            s => s.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
