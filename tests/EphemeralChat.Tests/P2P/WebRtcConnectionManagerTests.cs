using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using EphemeralChat.Network.P2P;
using EphemeralChat.Network.Signaling;
using EphemeralChat.Security.Identity;
using EphemeralChat.Signaling;
using EphemeralChat.Signaling.Server;
using EphemeralChat.WebRTC;
using Microsoft.AspNetCore.Builder;

namespace EphemeralChat.Tests.P2P;

public class WebRtcConnectionManagerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AcceptedSignalingSession_RoutesOfferAnswerAndIce_ToPeerConnections()
    {
        WebApplication app = SignalingServerHost.Create(
            new SignalingServerOptions(), "http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var identityA = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            using var identityB = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            await using var clientA = new SignalingClient(
                identityA.PeerId, Convert.ToBase64String(identityA.ExportPublicKey()));
            await using var clientB = new SignalingClient(
                identityB.PeerId, Convert.ToBase64String(identityB.ExportPublicKey()));

            var factoryA = new FakePeerConnectionFactory();
            var factoryB = new FakePeerConnectionFactory();
            await using var managerA = new WebRtcConnectionManager(clientA, factoryA);
            await using var managerB = new WebRtcConnectionManager(clientB, factoryB);
            var requestOnB = new ConcurrentQueue<string>();
            clientB.ConnectionRequested += requestOnB.Enqueue;

            clientA.RequestResponded += (from, outcome) =>
            {
                if (outcome == ConnectionRequestOutcome.Accepted)
                {
                    _ = managerA.StartOutgoingAsync(identityB.PeerId);
                }
            };

            await managerB.PrepareIncomingAsync(identityA.PeerId);
            await clientA.ConnectAndRegisterAsync(new Uri($"ws://127.0.0.1:{SignalingServerHost.GetBoundPort(app)}/ws"));
            await clientB.ConnectAndRegisterAsync(new Uri($"ws://127.0.0.1:{SignalingServerHost.GetBoundPort(app)}/ws"));
            await clientA.SendConnectRequestAsync(identityB.PeerId);
            Assert.Equal(
                identityA.PeerId,
                await WaitForAsync(requestOnB, request => request == identityA.PeerId));
            await clientB.SendAcceptAsync(identityA.PeerId);

            FakePeerConnection connectionA = await WaitForAsync(
                () => factoryA.Connections.FirstOrDefault(c => c.Offer is not null));
            FakePeerConnection connectionB = await WaitForAsync(
                () => factoryB.Connections.FirstOrDefault(c => c.RemoteOffer is not null));

            Assert.Contains("test-offer", connectionA.Offer, StringComparison.Ordinal);
            Assert.Contains("test-offer", connectionB.RemoteOffer, StringComparison.Ordinal);
            await WaitUntilAsync(() => connectionA.RemoteAnswer is not null);
            Assert.Contains("test-answer", connectionA.RemoteAnswer, StringComparison.Ordinal);

            connectionA.RaiseLocalCandidate("{\"candidate\":\"ice-from-a\"}");
            await WaitUntilAsync(() =>
                connectionB.RemoteCandidates.Any(c => c.Contains("ice-from-a", StringComparison.Ordinal)));

            connectionB.RaiseLocalCandidate("{\"candidate\":\"ice-from-b\"}");
            await WaitUntilAsync(() =>
                connectionA.RemoteCandidates.Any(c => c.Contains("ice-from-b", StringComparison.Ordinal)));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<T> WaitForAsync<T>(ConcurrentQueue<T> queue, Func<T, bool>? filter = null)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out T? value) && (filter is null || filter(value)))
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("expected P2P coordination state did not arrive");
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> condition) where T : class
    {
        DateTime deadline = DateTime.UtcNow + TestTimeout;
        while (DateTime.UtcNow < deadline)
        {
            T? value = condition();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("expected P2P coordination state did not arrive");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("expected P2P coordination state did not arrive");
    }

    private sealed class FakePeerConnectionFactory : IWebRtcPeerConnectionFactory
    {
        public List<FakePeerConnection> Connections { get; } = new();

        public IWebRtcPeerConnection Create()
        {
            var connection = new FakePeerConnection();
            Connections.Add(connection);
            return connection;
        }
    }

    private sealed class FakePeerConnection : IWebRtcPeerConnection
    {
        public event Action<string>? IceCandidateGenerated;
        public event Action<WebRtcConnectionState>? ConnectionStateChanged;
        public event Action? DataChannelOpened;

        public string? CreatedDataChannel { get; private set; }
        public string? Offer { get; private set; }
        public string? RemoteOffer { get; private set; }
        public string? RemoteAnswer { get; private set; }
        public List<string> RemoteCandidates { get; } = new();

        public Task CreateDataChannelAsync(string label, CancellationToken ct = default)
        {
            CreatedDataChannel = label;
            DataChannelOpened?.Invoke();
            return Task.CompletedTask;
        }

        public Task<string> CreateOfferAsync(CancellationToken ct = default)
        {
            Offer = "{\"type\":\"offer\",\"sdp\":\"test-offer\"}";
            return Task.FromResult(Offer);
        }

        public Task<string> AcceptOfferAsync(string offerSdpJson, CancellationToken ct = default)
        {
            RemoteOffer = offerSdpJson;
            return Task.FromResult("{\"type\":\"answer\",\"sdp\":\"test-answer\"}");
        }

        public Task AcceptAnswerAsync(string answerSdpJson, CancellationToken ct = default)
        {
            RemoteAnswer = answerSdpJson;
            ConnectionStateChanged?.Invoke(WebRtcConnectionState.Connected);
            return Task.CompletedTask;
        }

        public Task AddIceCandidateAsync(string candidateSdpJson, CancellationToken ct = default)
        {
            RemoteCandidates.Add(candidateSdpJson);
            return Task.CompletedTask;
        }

        public void RaiseLocalCandidate(string candidateJson) =>
            IceCandidateGenerated?.Invoke(candidateJson);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
