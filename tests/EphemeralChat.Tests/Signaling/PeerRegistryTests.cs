using System.Net.WebSockets;
using EphemeralChat.Signaling.Server;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Signaling;

public class PeerRegistryTests : IDisposable
{
    private readonly MutableClock _clock = new();
    private readonly PeerRegistry _registry;

    public PeerRegistryTests()
    {
        _registry = new PeerRegistry(_clock);
    }

    public void Dispose()
    {
        // PeerConnections own a semaphore; dispose the ones still registered.
        foreach (PeerConnection connection in _registry.Snapshot())
        {
            connection.DisposeAsync().AsTask().Wait();
        }
    }

    private static PeerConnection NewPeer(string peerId) =>
        new(peerId, new ClientWebSocket(), Convert.ToBase64String(new byte[91]));

    [Fact]
    public void TryRegister_RejectsDuplicatePeerId()
    {
        var first = NewPeer("ABCDEFGHIJKLMNOPQRSTUVWXYZ");

        Assert.True(_registry.TryRegister(first));

        var second = NewPeer("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        Assert.False(_registry.TryRegister(second));
    }

    [Fact]
    public void RemoveIdle_RemovesOnlyPeersBeyondIdleTimeout()
    {
        var staleId = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var freshId = "BCDEFGHIJKLMNOPQRSTUVWXYZ2";
        var stale = NewPeer(staleId);
        var fresh = NewPeer(freshId);
        _registry.TryRegister(stale);
        _registry.TryRegister(fresh);
        _clock.UtcNow += TimeSpan.FromMinutes(1);
        _registry.MarkSeen(freshId);

        IReadOnlyList<PeerConnection> removed = _registry.RemoveIdle(TimeSpan.FromSeconds(30));

        Assert.Equal(staleId, Assert.Single(removed).PeerId);
        Assert.False(_registry.TryGet(staleId, out _));
        Assert.True(_registry.TryGet(freshId, out _));
    }
}
