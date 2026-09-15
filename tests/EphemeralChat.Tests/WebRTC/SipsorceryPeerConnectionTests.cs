using EphemeralChat.WebRTC;

namespace EphemeralChat.Tests.WebRTC;

public class SipsorceryPeerConnectionTests
{
    [Fact]
    public async Task TwoLocalPeerConnections_CanCompleteDirectWebRTC()
    {
        await using var caller = new SipsorceryPeerConnection();
        await using var callee = new SipsorceryPeerConnection();

        TaskCompletionSource connectedOnCaller = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource connectedOnCallee = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource dataChannelOnCaller = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource dataChannelOnCallee = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // SIPSorcery can raise local candidates while setLocalDescription runs
        // inside CreateOfferAsync, so listeners must be attached beforehand.
        var callerCandidates = new List<string>();
        var calleeCandidates = new List<string>();
        caller.IceCandidateGenerated += callerCandidates.Add;
        callee.IceCandidateGenerated += calleeCandidates.Add;

        caller.ConnectionStateChanged += state =>
        {
            if (state == WebRtcConnectionState.Connected)
            {
                connectedOnCaller.TrySetResult();
            }
        };
        callee.ConnectionStateChanged += state =>
        {
            if (state == WebRtcConnectionState.Connected)
            {
                connectedOnCallee.TrySetResult();
            }
        };
        caller.DataChannelOpened += () => dataChannelOnCaller.TrySetResult();
        callee.DataChannelOpened += () => dataChannelOnCallee.TrySetResult();
        caller.ConnectionStateChanged += state => Console.WriteLine($"caller state: {state}");
        callee.ConnectionStateChanged += state => Console.WriteLine($"callee state: {state}");

        caller.IceCandidateGenerated += candidate => Console.WriteLine($"caller candidate: {candidate}");
        callee.IceCandidateGenerated += candidate => Console.WriteLine($"callee candidate: {candidate}");

        await caller.CreateDataChannelAsync("ephemeralchat-test");
        string offer = await caller.CreateOfferAsync();
        string answer = await callee.AcceptOfferAsync(offer);
        await caller.AcceptAnswerAsync(answer);
        Console.WriteLine($"caller offer: {offer}");
        Console.WriteLine($"callee answer: {answer}");

        // SIPSorcery commonly emits host candidates inline in the local
        // description and may not raise onicecandidate for both peers. Those
        // inline candidates are sufficient for this direct-loopback connection.
        Assert.Contains("a=candidate:", offer, StringComparison.Ordinal);
        Assert.Contains("a=candidate:", answer, StringComparison.Ordinal);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
               (callerCandidates.Count == 0 || calleeCandidates.Count == 0))
        {
            await Task.Delay(50);
        }


        await Task.WhenAll(connectedOnCaller.Task, connectedOnCallee.Task).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(dataChannelOnCaller.Task, dataChannelOnCallee.Task).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
