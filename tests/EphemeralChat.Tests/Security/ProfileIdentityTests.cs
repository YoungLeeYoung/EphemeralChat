using EphemeralChat.App.Startup;
using EphemeralChat.Security.Identity;
using EphemeralChat.Security.Identity.Persistence;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Security;

public class ProfileIdentityTests
{
    [Fact]
    public void Profiles_CreateDistinctStableIdentities_WithoutOverwritingEachOther()
    {
        using var localAppData = new TempDirectory();
        ClientProfile profileA = ClientProfile.Resolve(["--profile", "A"], localAppData.FullPath);
        ClientProfile profileB = ClientProfile.Resolve(["--profile", "B"], localAppData.FullPath);
        string fileA = Path.Combine(profileA.IdentityDirectory, "identity.dat");
        string fileB = Path.Combine(profileB.IdentityDirectory, "identity.dat");

        string peerIdA;
        string peerIdB;
        byte[] firstFileA;
        byte[] firstFileB;
        {
            using var identityA = CreateIdentity(profileA.IdentityDirectory);
            using var identityB = CreateIdentity(profileB.IdentityDirectory);
            peerIdA = identityA.PeerId;
            peerIdB = identityB.PeerId;
            firstFileA = File.ReadAllBytes(fileA);
            firstFileB = File.ReadAllBytes(fileB);
        }

        using var restartedA = CreateIdentity(profileA.IdentityDirectory);
        using var restartedB = CreateIdentity(profileB.IdentityDirectory);
        byte[] secondFileA = File.ReadAllBytes(fileA);
        byte[] secondFileB = File.ReadAllBytes(fileB);

        Assert.NotEqual(peerIdA, peerIdB);
        Assert.Equal(peerIdA, restartedA.PeerId);
        Assert.Equal(peerIdB, restartedB.PeerId);
        Assert.Equal(firstFileA, secondFileA);
        Assert.Equal(firstFileB, secondFileB);
        Assert.NotEqual(firstFileA, firstFileB);
    }

    [Fact]
    public void ProfileIdentityFiles_AreStoredInSeparateProfileDirectories()
    {
        using var localAppData = new TempDirectory();
        ClientProfile profileA = ClientProfile.Resolve(["--profile=A"], localAppData.FullPath);
        ClientProfile profileB = ClientProfile.Resolve(["--profile=B"], localAppData.FullPath);

        using (CreateIdentity(profileA.IdentityDirectory))
        using (CreateIdentity(profileB.IdentityDirectory))
        {
            Assert.True(File.Exists(Path.Combine(
                localAppData.FullPath, "EphemeralChat", "Profiles", "A", "identity.dat")));
            Assert.True(File.Exists(Path.Combine(
                localAppData.FullPath, "EphemeralChat", "Profiles", "B", "identity.dat")));
            Assert.False(File.Exists(Path.Combine(
                localAppData.FullPath, "EphemeralChat", "identity.dat")));
        }
    }

    private static LocalIdentity CreateIdentity(string directory)
    {
        var service = new LocalIdentityService(
            new DpapiFileIdentityStore(directory, new DpapiIdentityKeyProtector()));
        return service.LoadOrCreate();
    }
}
