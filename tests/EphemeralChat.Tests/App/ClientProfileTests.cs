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
}
