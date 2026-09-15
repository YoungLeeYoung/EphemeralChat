using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// In-memory presence table. Entries are evicted when idle beyond the
/// configured timeout; nothing is ever persisted. Clock is injectable for
/// deterministic TTL tests.
/// </summary>
public sealed class PeerRegistry
{
    private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
    private readonly TimeProvider _clock;

    public PeerRegistry(TimeProvider clock)
    {
        _clock = clock;
    }

    public bool TryRegister(PeerConnection connection) =>
        _peers.TryAdd(connection.PeerId, connection);

    public bool TryGet(string peerId, [NotNullWhen(true)] out PeerConnection? connection) =>
        _peers.TryGetValue(peerId, out connection);

    public PeerConnection? Remove(string peerId) =>
        _peers.TryRemove(peerId, out var removed) ? removed : null;

    public IReadOnlyList<PeerConnection> Snapshot() => [.. _peers.Values];

    public void MarkSeen(string peerId)
    {
        if (_peers.TryGetValue(peerId, out var connection))
        {
            connection.LastSeen = _clock.GetUtcNow();
        }
    }

    public IReadOnlyList<PeerConnection> RemoveIdle(TimeSpan idleTimeout)
    {
        DateTimeOffset cutoff = _clock.GetUtcNow() - idleTimeout;
        List<PeerConnection> removed = [];

        foreach (var (peerId, connection) in _peers)
        {
            if (connection.LastSeen < cutoff &&
                _peers.TryRemove(KeyValuePair.Create(peerId, connection)))
            {
                removed.Add(connection);
            }
        }

        return removed;
    }
}
