using EphemeralChat.App.Startup;

namespace EphemeralChat.Tests.App;

public class ClientProfileTests
{
    [Fact]
    public void Resolve_WithoutProfile_UsesLegacyIdentityDirectory()
    {
        string localAppData = Path.Combine(Path.GetTempPath(), "ephemeralchat-profile-test");

        ClientProfile profile = ClientProfile.Resolve([], localAppData);

        Assert.Null(profile.Name);
        Assert.Equal("Default", profile.DisplayName);
        Assert.Equal(
            Path.Combine(localAppData, "EphemeralChat", "identity.dat"),
            Path.Combine(profile.IdentityDirectory, "identity.dat"));
    }

    [Fact]
    public void Resolve_WithProfile_IsolatesIdentityDirectory()
    {
        string localAppData = Path.Combine(Path.GetTempPath(), "ephemeralchat-profile-test");

        ClientProfile profileA = ClientProfile.Resolve(["--profile", "A"], localAppData);
        ClientProfile profileB = ClientProfile.Resolve(["--profile", "B"], localAppData);

        Assert.Equal("A", profileA.Name);
        Assert.Equal("B", profileB.Name);
        Assert.Equal(
            Path.Combine(localAppData, "EphemeralChat", "Profiles", "A", "identity.dat"),
            Path.Combine(profileA.IdentityDirectory, "identity.dat"));
        Assert.Equal(
            Path.Combine(localAppData, "EphemeralChat", "Profiles", "B", "identity.dat"),
            Path.Combine(profileB.IdentityDirectory, "identity.dat"));
        Assert.NotEqual(profileA.IdentityDirectory, profileB.IdentityDirectory);
    }

    [Theory]
    [InlineData("--profile", "A")]
    [InlineData("--profile=A")]
    [InlineData("--PROFILE", "A")]
    public void Resolve_AcceptsSupportedProfileSwitches(params string[] args)
    {
        Assert.Equal(
            "A",
            ClientProfile.Resolve(args, Path.GetTempPath()).Name);
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("--profile", "../secret")]
    [InlineData("--profile", "A/B")]
    [InlineData("--profile=")]
    public void Resolve_RejectsUnsafeOrIncompleteProfiles(params string[] args)
    {
        Assert.Throws<ArgumentException>(
            () => ClientProfile.Resolve(args, Path.GetTempPath()));
    }

    [Fact]
    public void Resolve_WithoutServer_ServerUriIsNull()
    {
        ClientProfile profile = ClientProfile.Resolve([], Path.GetTempPath());

        Assert.Null(profile.ServerUri);
    }

    [Fact]
    public void Resolve_ServerSwitch_NormalizesHostAndPortToWebSocketPath()
    {
        ClientProfile profile = ClientProfile.Resolve(
            ["--server", "203.0.113.10:8080"], Path.GetTempPath());

        Assert.Equal("ws://203.0.113.10:8080/ws", profile.ServerUri!.ToString());
    }

    [Fact]
    public void Resolve_ServerSwitch_InlineFullUriIsPreserved()
    {
        ClientProfile profile = ClientProfile.Resolve(
            ["--server=ws://203.0.113.10:8080"], Path.GetTempPath());

        Assert.Equal("ws://203.0.113.10:8080/ws", profile.ServerUri!.ToString());
    }

    [Fact]
    public void Resolve_ServerSwitch_HostWithoutPortDefaultsToWsScheme()
    {
        ClientProfile profile = ClientProfile.Resolve(
            ["--server", "203.0.113.10"], Path.GetTempPath());

        Assert.Equal("ws://203.0.113.10/ws", profile.ServerUri!.ToString());
    }

    [Fact]
    public void Resolve_ServerSwitch_WssSchemeAndCustomPathArePreserved()
    {
        ClientProfile profile = ClientProfile.Resolve(
            ["--server", "wss://chat.example.com/gw"], Path.GetTempPath());

        Assert.Equal("wss://chat.example.com/gw", profile.ServerUri!.ToString());
    }

    [Theory]
    [InlineData("--server")]
    [InlineData("--server=")]
    [InlineData("--server", "ftp://203.0.113.10")]
    [InlineData("--server", "not a uri at all")]
    public void Resolve_RejectsInvalidServerSwitches(params string[] args)
    {
        Assert.Throws<ArgumentException>(
            () => ClientProfile.Resolve(args, Path.GetTempPath()));
    }
}
