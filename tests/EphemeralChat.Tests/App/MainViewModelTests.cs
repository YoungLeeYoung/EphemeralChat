using System.Security.Cryptography;
using EphemeralChat.App.ViewModels;
using EphemeralChat.Security.Identity;

namespace EphemeralChat.Tests.App;

public class MainViewModelTests
{
    [Fact]
    public void LocalPeerDescription_ShowsPeerId_WhenIdentityIsSet()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var identity = new LocalIdentity(key);
        var viewModel = new MainViewModel();

        viewModel.SetLocalIdentity(identity);

        Assert.Equal($"Peer ID: {identity.PeerId}", viewModel.LocalPeerDescription);
        Assert.Equal("Profile: Default", viewModel.ProfileDescription);
    }

    [Fact]
    public void LocalPeerDescription_ShowsPlaceholder_WhenIdentityIsMissing()
    {
        var viewModel = new MainViewModel();

        Assert.Equal("No local identity loaded", viewModel.LocalPeerDescription);
    }

    [Fact]
    public void Send_DoesNotSendWithoutConnectedP2pSession()
    {
        var viewModel = new MainViewModel { DraftMessage = "  hello  " };

        viewModel.SendCommand.Execute(null);

        Assert.Empty(viewModel.Messages);
        Assert.Equal("  hello  ", viewModel.DraftMessage);
    }
}
