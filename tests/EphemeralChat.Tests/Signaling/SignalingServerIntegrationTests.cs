using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using EphemeralChat.Network.Signaling;
using EphemeralChat.Security.Identity;
using EphemeralChat.Signaling;
using EphemeralChat.Signaling.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;

namespace EphemeralChat.Tests.Signaling;

/// <summary>
/// End-to-end signaling plane tests: two real WebSocket clients against an
/// in-process server (same wiring as Program.cs, port 0, short TTLs).
/// </summary>
public class SignalingServerIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static async Task<(WebApplication App, int Port)> StartServerAsync(
        Func<SignalingServerOptions, SignalingServerOptions>? configure = null)
    {
        var options = new SignalingServerOptions
        {
            SweepInterval = TimeSpan.FromMilliseconds(50)
        };
        options = configure?.Invoke(options) ?? options;

        WebApplication app = SignalingServerHost.Create(options, "http://127.0.0.1:0");
        await app.StartAsync();
        return (app, SignalingServerHost.GetBoundPort(app));
    }

    private static (string PeerId, string PublicKeyBase64) NewPeer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var identity = new LocalIdentity(key);
        return (identity.PeerId, Convert.ToBase64String(identity.ExportPublicKey()));
    }

    private static Uri WsUri(int port) => new($"ws://127.0.0.1:{port}/ws");

    private static async Task<T> WaitForAsync<T>(
        ConcurrentQueue<T> queue, Func<T, bool>? filter = null)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var item) && (filter is null || filter(item)))
            {
                return item;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("expected signaling event did not arrive");
    }

    [Fact]
    public async Task TwoClients_CanRegister_AndSeeEachOthersPresence()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();

            var presenceOnA = new ConcurrentQueue<PeerPresence>();
            await using var clientA = new SignalingClient(peerA, keyA);
            clientA.PresenceChanged += presenceOnA.Enqueue;
            await clientA.ConnectAndRegisterAsync(WsUri(port));

            var listOnB = new ConcurrentQueue<IReadOnlyList<PeerPresence>>();
            var presenceOnB = new ConcurrentQueue<PeerPresence>();
            await using var clientB = new SignalingClient(peerB, keyB);
            clientB.PeerListReceived += listOnB.Enqueue;
            clientB.PresenceChanged += presenceOnB.Enqueue;
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            PeerPresence seenOnA = await WaitForAsync(presenceOnA, p => p.PeerId == peerB && p.IsOnline);
            IReadOnlyList<PeerPresence> listedOnB = await WaitForAsync(listOnB, l => l.Any(p => p.PeerId == peerA));

            Assert.Equal(keyB, seenOnA.PublicKeyBase64);
            Assert.Equal(keyA, listedOnB.Single(p => p.PeerId == peerA).PublicKeyBase64);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectRequest_Accept_EnablesSessionForwarding()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await using var clientB = new SignalingClient(peerB, keyB);

            var requestOnB = new ConcurrentQueue<string>();
            clientB.ConnectionRequested += requestOnB.Enqueue;
            var responseOnA = new ConcurrentQueue<(string From, ConnectionRequestOutcome Outcome)>();
            clientA.RequestResponded += (from, outcome) => responseOnA.Enqueue((from, outcome));
            var offersOnB = new ConcurrentQueue<(string From, JsonElement Payload)>();
            clientB.OfferReceived += (from, payload) => offersOnB.Enqueue((from, payload));
            var answersOnA = new ConcurrentQueue<(string From, JsonElement Payload)>();
            clientA.AnswerReceived += (from, payload) => answersOnA.Enqueue((from, payload));
            var iceOnA = new ConcurrentQueue<(string From, JsonElement Payload)>();
            clientA.IceCandidateReceived += (from, payload) => iceOnA.Enqueue((from, payload));
            var iceOnB = new ConcurrentQueue<(string From, JsonElement Payload)>();
            clientB.IceCandidateReceived += (from, payload) => iceOnB.Enqueue((from, payload));

            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            await clientA.SendConnectRequestAsync(peerB);
            Assert.Equal(peerA, await WaitForAsync(requestOnB));

            await clientB.SendAcceptAsync(peerA);
            (string from, ConnectionRequestOutcome outcome) =
                await WaitForAsync(responseOnA);
            Assert.Equal((peerB, ConnectionRequestOutcome.Accepted), (from, outcome));

            await clientA.SendOfferAsync(
                peerB, JsonSerializer.SerializeToElement(new { sdp = "offer-sdp" }));
            (string offerFrom, JsonElement offerPayload) = await WaitForAsync(offersOnB);
            Assert.Equal(peerA, offerFrom);
            Assert.Equal("offer-sdp", offerPayload.GetProperty("sdp").GetString());

            await clientB.SendAnswerAsync(
                peerA, JsonSerializer.SerializeToElement(new { sdp = "answer-sdp" }));
            (string answerFrom, JsonElement answerPayload) = await WaitForAsync(answersOnA);
            Assert.Equal(peerB, answerFrom);
            Assert.Equal("answer-sdp", answerPayload.GetProperty("sdp").GetString());

            await clientA.SendIceCandidateAsync(
                peerB, JsonSerializer.SerializeToElement(new { candidate = "ice-a" }));
            (string iceFrom, JsonElement icePayload) = await WaitForAsync(iceOnB);
            Assert.Equal(peerA, iceFrom);
            Assert.Equal("ice-a", icePayload.GetProperty("candidate").GetString());

            await clientB.SendIceCandidateAsync(
                peerA, JsonSerializer.SerializeToElement(new { candidate = "ice-b" }));
            (string iceFromA, JsonElement icePayloadA) = await WaitForAsync(iceOnA);
            Assert.Equal(peerB, iceFromA);
            Assert.Equal("ice-b", icePayloadA.GetProperty("candidate").GetString());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectRequest_Reject_NotifiesRequester_AndBlocksForwarding()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await using var clientB = new SignalingClient(peerB, keyB);

            var requestOnB = new ConcurrentQueue<string>();
            clientB.ConnectionRequested += requestOnB.Enqueue;
            var responseOnA = new ConcurrentQueue<(string From, ConnectionRequestOutcome Outcome)>();
            clientA.RequestResponded += (from, outcome) => responseOnA.Enqueue((from, outcome));
            var errorsOnB = new ConcurrentQueue<string>();
            clientB.ErrorReceived += errorsOnB.Enqueue;

            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            await clientA.SendConnectRequestAsync(peerB);
            Assert.Equal(peerA, await WaitForAsync(requestOnB));

            await clientB.SendRejectAsync(peerA);
            (string from, ConnectionRequestOutcome outcome) = await WaitForAsync(responseOnA);
            Assert.Equal((peerB, ConnectionRequestOutcome.Rejected), (from, outcome));

            await clientB.SendOfferAsync(peerA, JsonSerializer.SerializeToElement(new { sdp = "x" }));
            string error = await WaitForAsync(errorsOnB);
            Assert.Contains("no-active-session", error);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnansweredConnectRequest_ExpiresByTtl()
    {
        var (app, port) =
            await StartServerAsync(o => o with { PendingRequestTtl = TimeSpan.FromMilliseconds(600) });
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await using var clientB = new SignalingClient(peerB, keyB);

            var responseOnA = new ConcurrentQueue<(string From, ConnectionRequestOutcome Outcome)>();
            clientA.RequestResponded += (from, outcome) => responseOnA.Enqueue((from, outcome));

            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            await clientA.SendConnectRequestAsync(peerB);
            (string from, ConnectionRequestOutcome outcome) =
                await WaitForAsync(responseOnA, r => r.Outcome == ConnectionRequestOutcome.TimedOut);

            Assert.Equal(peerB, from);
            Assert.Equal(ConnectionRequestOutcome.TimedOut, outcome);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Offer_WithoutAcceptedSession_IsRejectedByServer()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await using var clientB = new SignalingClient(peerB, keyB);

            var errorsOnA = new ConcurrentQueue<string>();
            clientA.ErrorReceived += errorsOnA.Enqueue;

            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            await clientA.SendOfferAsync(peerB, JsonSerializer.SerializeToElement(new { sdp = "x" }));

            string error = await WaitForAsync(errorsOnA);
            Assert.Contains("no-active-session", error);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task DuplicatePeerId_Registration_IsRejected()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            await using var clientA1 = new SignalingClient(peerA, keyA);
            await using var clientA2 = new SignalingClient(peerA, keyA);
            await clientA1.ConnectAndRegisterAsync(WsUri(port));

            await Assert.ThrowsAsync<SignalingException>(
                () => clientA2.ConnectAndRegisterAsync(WsUri(port)));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectRequest_ToOfflinePeer_IsRejected_AndDoesNotQueueARequest()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (offlinePeer, _) = NewPeer();
            var errorsOnA = new ConcurrentQueue<string>();

            await using var clientA = new SignalingClient(peerA, keyA);
            clientA.ErrorReceived += errorsOnA.Enqueue;
            await clientA.ConnectAndRegisterAsync(WsUri(port));

            await clientA.SendConnectRequestAsync(offlinePeer);
            string error = await WaitForAsync(errorsOnA);

            Assert.Contains("peer-not-online", error);

            // If the offline request had been persisted/queued, this later
            // registration with the same Peer ID would receive a request.
            var (latePeer, lateKey) = NewPeer();
            var requestsOnLatePeer = new ConcurrentQueue<string>();
            await using var lateClient = new SignalingClient(latePeer, lateKey);
            lateClient.ConnectionRequested += requestsOnLatePeer.Enqueue;
            await lateClient.ConnectAndRegisterAsync(WsUri(port));
            await Task.Delay(100);

            Assert.Empty(requestsOnLatePeer);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MalformedConnectRequest_IsRejectedByTheClientValidationBoundary()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await clientA.ConnectAndRegisterAsync(WsUri(port));

            await Assert.ThrowsAsync<ArgumentException>(
                () => clientA.SendConnectRequestAsync("not-a-peer-id"));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task DuplicateConnectRequest_AfterAccept_IsRejectedByServer()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientA = new SignalingClient(peerA, keyA);
            await using var clientB = new SignalingClient(peerB, keyB);

            var requestOnB = new ConcurrentQueue<string>();
            clientB.ConnectionRequested += requestOnB.Enqueue;
            var responseOnA = new ConcurrentQueue<(string From, ConnectionRequestOutcome Outcome)>();
            clientA.RequestResponded += (from, outcome) => responseOnA.Enqueue((from, outcome));
            var errorsOnA = new ConcurrentQueue<string>();
            clientA.ErrorReceived += errorsOnA.Enqueue;

            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));

            // First request: accepted normally.
            await clientA.SendConnectRequestAsync(peerB);
            Assert.Equal(peerA, await WaitForAsync(requestOnB));
            await clientB.SendAcceptAsync(peerA);
            var (_, outcome) = await WaitForAsync(responseOnA);
            Assert.Equal(ConnectionRequestOutcome.Accepted, outcome);

            // Second request while already paired: server rejects it.
            await clientA.SendConnectRequestAsync(peerB);
            string error = await WaitForAsync(errorsOnA);
            Assert.Contains("already-connected", error);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disconnect_BroadcastsOfflinePresence()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            var (peerA, keyA) = NewPeer();
            var (peerB, keyB) = NewPeer();
            await using var clientB = new SignalingClient(peerB, keyB);
            var presenceOnB = new ConcurrentQueue<PeerPresence>();
            var listOnB = new ConcurrentQueue<IReadOnlyList<PeerPresence>>();
            clientB.PresenceChanged += presenceOnB.Enqueue;
            clientB.PeerListReceived += listOnB.Enqueue;

            var clientA = new SignalingClient(peerA, keyA);
            await clientA.ConnectAndRegisterAsync(WsUri(port));
            await clientB.ConnectAndRegisterAsync(WsUri(port));
            await WaitForAsync(listOnB, l => l.Any(p => p.PeerId == peerA));

            await clientA.DisposeAsync();

            PeerPresence offline = await WaitForAsync(presenceOnB, p => p.PeerId == peerA && !p.IsOnline);
            Assert.Null(offline.PublicKeyBase64);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
