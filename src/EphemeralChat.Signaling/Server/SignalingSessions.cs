using System.Collections.Concurrent;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// Accepted connect-requests become symmetric session links. Offer/answer/ICE
/// forwarding is only allowed between linked peers, so random peers cannot
/// push payloads at each other. Links live in memory and disappear when
/// either peer disconnects.
/// </summary>
public sealed class SignalingSessions
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _pairs = new();

    public void AddPair(string peerA, string peerB)
    {
        _pairs.GetOrAdd(peerA, _ => new ConcurrentDictionary<string, byte>()).TryAdd(peerB, 0);
        _pairs.GetOrAdd(peerB, _ => new ConcurrentDictionary<string, byte>()).TryAdd(peerA, 0);
    }

    public bool IsPaired(string peerA, string peerB) =>
        _pairs.TryGetValue(peerA, out var partners) && partners.ContainsKey(peerB);

    public IReadOnlyList<string> RemovePeer(string peerId)
    {
        if (!_pairs.TryRemove(peerId, out var partners))
        {
            return [];
        }

        List<string> formerPartners = [];
        foreach (string other in partners.Keys)
        {
            formerPartners.Add(other);
            if (_pairs.TryGetValue(other, out var backLinks))
            {
                backLinks.TryRemove(peerId, out _);
            }
        }

        return formerPartners;
    }
}
