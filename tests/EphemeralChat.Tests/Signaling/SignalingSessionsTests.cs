using EphemeralChat.Signaling.Server;

namespace EphemeralChat.Tests.Signaling;

public class SignalingSessionsTests
{
    private const string Alice = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Bob = "BCDEFGHIJKLMNOPQRSTUVWXYZ2";

    private readonly SignalingSessions _sessions = new();

    [Fact]
    public void AddPair_LinksBothDirections()
    {
        _sessions.AddPair(Alice, Bob);

        Assert.True(_sessions.IsPaired(Alice, Bob));
        Assert.True(_sessions.IsPaired(Bob, Alice));
    }

    [Fact]
    public void RemovePeer_UnlinksAllPartners()
    {
        _sessions.AddPair(Alice, Bob);

        IReadOnlyList<string> formerPartners = _sessions.RemovePeer(Alice);

        Assert.Equal(Bob, Assert.Single(formerPartners));
        Assert.False(_sessions.IsPaired(Alice, Bob));
        Assert.False(_sessions.IsPaired(Bob, Alice));
    }
}
