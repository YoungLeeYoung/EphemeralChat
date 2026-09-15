using System.Collections.Concurrent;
using System.Security.Cryptography;
using EphemeralChat.App.ViewModels;
using EphemeralChat.Network.Signaling;
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
            await WaitUntilAsync(() => viewModelA.Peers.Any(p => p.PeerId == identityB.PeerId));

            viewModelA.SelectedPeer = viewModelA.Peers.Single(p => p.PeerId == identityB.PeerId);
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
            await WaitUntilAsync(() => viewModelA.Peers.Any(p => p.PeerId == identityB.PeerId));

            viewModelA.SelectedPeer = viewModelA.Peers.Single(p => p.PeerId == identityB.PeerId);
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
}
