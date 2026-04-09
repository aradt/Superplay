using System.Net.WebSockets;
using Moq;
using Superplay.Server.Networking;

namespace Superplay.Tests.Networking;

public sealed class ConnectionManagerTests
{
    private readonly ConnectionManager _sut = new();

    private static WebSocket CreateMockSocket()
    {
        // Using a mock since we can't easily instantiate a real WebSocket
        var mock = new Mock<WebSocket>();
        mock.Setup(s => s.State).Returns(WebSocketState.Open);
        return mock.Object;
    }

    [Fact]
    public void TryAdd_ReturnsTrue_ForNewPlayer()
    {
        var socket = CreateMockSocket();

        var result = _sut.TryAdd("player-1", socket);

        Assert.True(result);
    }

    [Fact]
    public void TryAdd_ReturnsFalse_ForDuplicatePlayer()
    {
        var socket1 = CreateMockSocket();
        var socket2 = CreateMockSocket();
        _sut.TryAdd("player-1", socket1);

        var result = _sut.TryAdd("player-1", socket2);

        Assert.False(result);
    }

    [Fact]
    public void TryAdd_IsCaseInsensitive()
    {
        var socket1 = CreateMockSocket();
        var socket2 = CreateMockSocket();
        _sut.TryAdd("Player-1", socket1);

        // Same player ID, different casing — should be treated as duplicate
        var result = _sut.TryAdd("player-1", socket2);

        Assert.False(result);
    }

    [Fact]
    public void TryRemove_ReturnsTrue_WhenPlayerExists()
    {
        _sut.TryAdd("player-1", CreateMockSocket());

        var result = _sut.TryRemove("player-1");

        Assert.True(result);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenPlayerDoesNotExist()
    {
        var result = _sut.TryRemove("player-nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void TryRemove_AllowsReaddingPlayer()
    {
        var socket = CreateMockSocket();
        _sut.TryAdd("player-1", socket);
        _sut.TryRemove("player-1");

        var result = _sut.TryAdd("player-1", socket);

        Assert.True(result);
    }

    [Fact]
    public void GetSocket_ReturnsSocket_WhenPlayerIsConnected()
    {
        var socket = CreateMockSocket();
        _sut.TryAdd("player-1", socket);

        var result = _sut.GetSocket("player-1");

        Assert.Same(socket, result);
    }

    [Fact]
    public void GetSocket_ReturnsNull_WhenPlayerIsNotConnected()
    {
        var result = _sut.GetSocket("player-unknown");

        Assert.Null(result);
    }

    [Fact]
    public void GetSocket_IsCaseInsensitive()
    {
        var socket = CreateMockSocket();
        _sut.TryAdd("Player-1", socket);

        Assert.Same(socket, _sut.GetSocket("player-1"));
        Assert.Same(socket, _sut.GetSocket("PLAYER-1"));
    }

    [Fact]
    public void IsOnline_ReturnsTrue_WhenPlayerIsConnected()
    {
        _sut.TryAdd("player-1", CreateMockSocket());

        Assert.True(_sut.IsOnline("player-1"));
    }

    [Fact]
    public void IsOnline_ReturnsFalse_WhenPlayerIsNotConnected()
    {
        Assert.False(_sut.IsOnline("player-unknown"));
    }

    [Fact]
    public void IsOnline_ReturnsFalse_AfterPlayerRemoved()
    {
        _sut.TryAdd("player-1", CreateMockSocket());
        _sut.TryRemove("player-1");

        Assert.False(_sut.IsOnline("player-1"));
    }

    [Fact]
    public void IsOnline_IsCaseInsensitive()
    {
        _sut.TryAdd("Player-1", CreateMockSocket());

        Assert.True(_sut.IsOnline("player-1"));
        Assert.True(_sut.IsOnline("PLAYER-1"));
    }

    [Fact]
    public async Task ConcurrentAccess_MultiplePlayersCanBeAddedSimultaneously()
    {
        const int playerCount = 100;
        var tasks = new Task[playerCount];

        for (var i = 0; i < playerCount; i++)
        {
            var playerId = $"player-{i}";
            tasks[i] = Task.Run(() => _sut.TryAdd(playerId, CreateMockSocket()));
        }

        await Task.WhenAll(tasks);

        for (var i = 0; i < playerCount; i++)
        {
            Assert.True(_sut.IsOnline($"player-{i}"));
        }
    }

    [Fact]
    public async Task ConcurrentAccess_AddAndRemoveSamePlayerFromMultipleThreads()
    {
        // Exercise the thread-safety of ConcurrentDictionary:
        // many threads trying to add/remove the same key should not throw.
        const int iterations = 1000;
        var tasks = new Task[iterations];
        var socket = CreateMockSocket();

        for (var i = 0; i < iterations; i++)
        {
            if (i % 2 == 0)
                tasks[i] = Task.Run(() => _sut.TryAdd("contested-player", socket));
            else
                tasks[i] = Task.Run(() => _sut.TryRemove("contested-player"));
        }

        // Should complete without exceptions
        await Task.WhenAll(tasks);
    }

    [Fact]
    public void GetSocket_ReturnsNull_AfterRemove()
    {
        _sut.TryAdd("player-1", CreateMockSocket());
        _sut.TryRemove("player-1");

        Assert.Null(_sut.GetSocket("player-1"));
    }

    [Fact]
    public void MultiplePlayersCanCoexist()
    {
        var socket1 = CreateMockSocket();
        var socket2 = CreateMockSocket();
        var socket3 = CreateMockSocket();

        _sut.TryAdd("player-1", socket1);
        _sut.TryAdd("player-2", socket2);
        _sut.TryAdd("player-3", socket3);

        Assert.Same(socket1, _sut.GetSocket("player-1"));
        Assert.Same(socket2, _sut.GetSocket("player-2"));
        Assert.Same(socket3, _sut.GetSocket("player-3"));
    }
}
