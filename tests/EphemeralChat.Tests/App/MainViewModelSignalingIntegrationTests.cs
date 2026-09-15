using System.Collections.Concurrent;
using System.Security.Cryptography;
using EphemeralChat.App.ViewModels;
using EphemeralChat.Core.Models;
using EphemeralChat.Network.Signaling;
using EphemeralChat.Network.P2P;
using EphemeralChat.Security.Identity;
using EphemeralChat.Signaling;
using EphemeralChat.Signaling.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace EphemeralChat.Tests.App;

public class MainViewModelSignalingIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static async Task<(WebApplication App, int Port)> StartServerAsync()
    {
        WebApplication app = SignalingServerHost.Create(
            new SignalingServerOptions(), "http://127.0.0.1:0");
        await app.StartAsync();
        return (app, SignalingServerHost.GetBoundPort(app));
    }

    private static Uri WsUri(int port) => new($"ws://127.0.0.1:{port}/ws");

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

        throw new TimeoutException("expected UI signaling state did not arrive");
    }

    [Fact]
    public async Task UiConnectRequest_Accept_UpdatesBothPeers()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            using var identityA = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            using var identityB = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            var viewModelA = new MainViewModel(WsUri(port));
            var viewModelB = new MainViewModel(WsUri(port));
            viewModelA.SetLocalIdentity(identityA);
            viewModelB.SetLocalIdentity(identityB);

            viewModelA.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModelA.ConnectionStatus == "Signaling: Connected");
            viewModelB.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModelB.ConnectionStatus == "Signaling: Connected");

            viewModelA.TargetPeerId = identityB.PeerId;
            viewModelA.SendConnectRequestCommand.Execute(null);
            await WaitUntilAsync(() => viewModelB.HasIncomingRequest);
            Assert.Equal(identityA.PeerId, viewModelB.IncomingRequestDescription.Split(' ')[^1]);

            viewModelB.AcceptRequestCommand.Execute(null);
            await WaitUntilAsync(() => viewModelA.RequestStatus == $"Accepted by {identityB.PeerId}");
            Assert.Equal($"Accepted request from {identityA.PeerId}", viewModelB.RequestStatus);
            Assert.Equal($"Connected to {identityB.PeerId}", viewModelA.ConnectedPeerDescription);
            Assert.Equal($"Connected to {identityA.PeerId}", viewModelB.ConnectedPeerDescription);

            // After accept, A should not be able to send a duplicate request to B.
            Assert.False(viewModelA.SendConnectRequestCommand.CanExecute(null));

            viewModelA.Dispose();
            viewModelB.Dispose();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UiConnectRequest_Reject_UpdatesBothPeers()
    {
        var (app, port) = await StartServerAsync();
        try
        {
            using var identityA = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            using var identityB = new LocalIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            var viewModelA = new MainViewModel(WsUri(port));
            var viewModelB = new MainViewModel(WsUri(port));
            viewModelA.SetLocalIdentity(identityA);
            viewModelB.SetLocalIdentity(identityB);

            viewModelA.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModelA.ConnectionStatus == "Signaling: Connected");
            viewModelB.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModelB.ConnectionStatus == "Signaling: Connected");

            viewModelA.TargetPeerId = identityB.PeerId;
            viewModelA.SendConnectRequestCommand.Execute(null);
            await WaitUntilAsync(() => viewModelB.HasIncomingRequest);

            viewModelB.RejectRequestCommand.Execute(null);
            await WaitUntilAsync(() => viewModelA.RequestStatus == $"Rejected by {identityB.PeerId}");
            Assert.Equal($"Rejected request from {identityA.PeerId}", viewModelB.RequestStatus);
            Assert.Equal("Not connected to any peer", viewModelA.ConnectedPeerDescription);
            Assert.Equal("Not connected to any peer", viewModelB.ConnectedPeerDescription);

            viewModelA.Dispose();
            viewModelB.Dispose();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task SignalingDisconnect_KeepsAnEstablishedP2pSessionAlive()
    {
        var (app, port) = await StartServerAsync();
        var fakeManager = new ConnectedFakeP2pManager();
        MainViewModel? viewModel = null;
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var identity = new LocalIdentity(key);
            viewModel = new MainViewModel(
                WsUri(port),
                _ => fakeManager);
            viewModel.SetLocalIdentity(identity);

            viewModel.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.ConnectionStatus == "Signaling: Connected");
            fakeManager.RaiseConnected();
            await WaitUntilAsync(() => viewModel.P2pStatus == "Direct P2P: Connected");
            Assert.Equal("Direct P2P: Connected", viewModel.P2pStatus);

            using var shutdownCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await app.StopAsync(shutdownCts.Token);
            await WaitUntilAsync(() => viewModel.ConnectionStatus == "Signaling: Offline");

            Assert.Equal("Direct P2P: Connected", viewModel.P2pStatus);
            Assert.False(fakeManager.Disposed);
        }
        finally
        {
            viewModel?.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UiSendsAndReceivesP2pTextMessages()
    {
        var (app, port) = await StartServerAsync();
        var fakeManager = new ConnectedFakeP2pManager();
        MainViewModel? viewModel = null;
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var identity = new LocalIdentity(key);
            viewModel = new MainViewModel(
                WsUri(port),
                _ => fakeManager);
            viewModel.SetLocalIdentity(identity);

            viewModel.ConnectSignalingCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.ConnectionStatus == "Signaling: Connected");
            fakeManager.RaiseConnected();
            await WaitUntilAsync(() => viewModel.P2pStatus == "Direct P2P: Connected");

            viewModel.DraftMessage = "  hello over p2p  ";
            viewModel.SendCommand.Execute(null);
            await WaitUntilAsync(() =>
            {
                ChatMessage[] snapshot = viewModel.Messages.ToArray();
                return snapshot.Any(message =>
                    message.Sender == "Local" &&
                    message.Content == "hello over p2p");
            });
            Assert.Equal(string.Empty, viewModel.DraftMessage);

            fakeManager.RaiseIncomingText(identity.PeerId, "reply over p2p");
            await WaitUntilAsync(() =>
            {
                ChatMessage[] snapshot = viewModel.Messages.ToArray();
                return snapshot.Any(message =>
                    message.Sender == identity.PeerId &&
                    message.Content == "reply over p2p");
            });
            Assert.Equal(2, viewModel.Messages.Count);
        }
        finally
        {
            viewModel?.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class ConnectedFakeP2pManager : IP2PConnectionManager
    {
        public event Action<P2PConnectionState>? ConnectionStateChanged;

#pragma warning disable CS0067
        public event Action? DataChannelOpened;
        public event Action<string>? OperationFailed;
        public event Action<string, P2PTextMessage>? TextMessageReceived;
#pragma warning restore CS0067

        public string? RemotePeerId { get; private set; }

        public P2PConnectionState State { get; private set; } = P2PConnectionState.Connected;

        public bool Disposed { get; private set; }

        public void RaiseConnected() => ConnectionStateChanged?.Invoke(P2PConnectionState.Connected);

        public Task StartOutgoingAsync(string remotePeerId, CancellationToken ct = default)
        {
            RemotePeerId = remotePeerId;
            return Task.CompletedTask;
        }

        public Task PrepareIncomingAsync(string remotePeerId, CancellationToken ct = default)
        {
            RemotePeerId = remotePeerId;
            return Task.CompletedTask;
        }

        public Task<P2PTextMessage> SendTextAsync(string content, CancellationToken ct = default) =>
            Task.FromResult(new P2PTextMessage(
                Guid.NewGuid().ToString("N"),
                content,
                DateTimeOffset.UtcNow));

        public void RaiseIncomingText(string fromPeerId, string content) =>
            TextMessageReceived?.Invoke(
                fromPeerId,
                new P2PTextMessage(Guid.NewGuid().ToString("N"), content, DateTimeOffset.UtcNow));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
