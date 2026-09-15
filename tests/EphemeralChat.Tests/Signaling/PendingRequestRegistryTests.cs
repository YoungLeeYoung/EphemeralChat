using EphemeralChat.Signaling.Server;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Signaling;

public class PendingRequestRegistryTests
{
    private readonly MutableClock _clock = new();
    private readonly PendingRequestRegistry _registry = new();

    private const string Alice = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Bob = "BCDEFGHIJKLMNOPQRSTUVWXYZ2";

    [Fact]
    public void TryAdd_RejectsSecondPendingRequestFromSameRequester()
    {
        Assert.True(_registry.TryAdd(Alice, Bob, _clock.UtcNow, TimeSpan.FromSeconds(30)));
        Assert.False(_registry.TryAdd(Alice, Bob, _clock.UtcNow, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void TryPop_OnlyMatchesTheExactTarget()
    {
        _registry.TryAdd(Alice, Bob, _clock.UtcNow, TimeSpan.FromSeconds(30));

        Assert.Null(_registry.TryPop(Alice, Alice));
        Assert.Null(_registry.TryPop(Bob, Alice));

        PendingRequest? popped = _registry.TryPop(Alice, Bob);
        Assert.NotNull(popped);
        Assert.Equal(Alice, popped.RequesterId);
        Assert.Equal(Bob, popped.TargetId);
        Assert.Null(_registry.TryPop(Alice, Bob));
    }

    [Fact]
    public void RemoveExpired_RemovesOnlyRequestsPastTheirTtl()
    {
        _registry.TryAdd(Alice, Bob, _clock.UtcNow, TimeSpan.FromSeconds(30));
        _clock.UtcNow += TimeSpan.FromSeconds(29);

        Assert.Empty(_registry.RemoveExpired(_clock.UtcNow));

        _clock.UtcNow += TimeSpan.FromSeconds(2);
        IReadOnlyList<PendingRequest> expired = _registry.RemoveExpired(_clock.UtcNow);

        PendingRequest request = Assert.Single(expired);
        Assert.Equal(Alice, request.RequesterId);
        Assert.Equal(Bob, request.TargetId);
    }
}
