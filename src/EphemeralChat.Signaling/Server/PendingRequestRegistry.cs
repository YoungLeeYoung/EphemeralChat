using System.Collections.Concurrent;

namespace EphemeralChat.Signaling.Server;

public sealed record PendingRequest(string RequesterId, string TargetId, DateTimeOffset ExpiresAt);

/// <summary>
/// One pending connect-request per requester, each with a TTL. Requests that
/// are never answered simply expire and are removed — no queue, no storage.
/// </summary>
public sealed class PendingRequestRegistry
{
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();

    public bool TryAdd(string requesterId, string targetId, DateTimeOffset now, TimeSpan ttl) =>
        _pending.TryAdd(requesterId, new PendingRequest(requesterId, targetId, now + ttl));

    public PendingRequest? TryPop(string requesterId, string targetId)
    {
        if (!_pending.TryGetValue(requesterId, out var pending) || pending.TargetId != targetId)
        {
            return null;
        }

        return _pending.TryRemove(KeyValuePair.Create(requesterId, pending)) ? pending : null;
    }

    public void RemoveByRequester(string requesterId) => _pending.TryRemove(requesterId, out _);

    public IReadOnlyList<PendingRequest> RemoveExpired(DateTimeOffset now)
    {
        List<PendingRequest> expired = [];

        foreach (var (requesterId, request) in _pending)
        {
            if (request.ExpiresAt <= now &&
                _pending.TryRemove(KeyValuePair.Create(requesterId, request)))
            {
                expired.Add(request);
            }
        }

        return expired;
    }
}
