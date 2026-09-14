using EphemeralChat.Security.Identity;
using EphemeralChat.Security.Identity.Persistence;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Security;

public class LocalIdentityServiceTests
{
    private static LocalIdentityService CreateService(TempDirectory temp) =>
        new(new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector()));

    [Fact]
    public void LoadOrCreate_CreatesAndPersistsANewIdentity()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);

        using LocalIdentity identity = service.LoadOrCreate();

        Assert.NotEmpty(identity.PeerId);
        Assert.True(service is not null);
        Assert.True(new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector()).Exists());
    }

    [Fact]
    public void LoadOrCreate_ReturnsTheSameIdentityAcrossServiceInstances()
    {
        using var temp = new TempDirectory();
        byte[] publicKeyA;
        string peerId;
        {
            var service = CreateService(temp);
        using (var identity = service.LoadOrCreate())
        {
            peerId = identity.PeerId;
            publicKeyA = identity.ExportPublicKey();
        }
        }

        var secondService = CreateService(temp);
        using LocalIdentity loaded = secondService.LoadOrCreate();

        Assert.Equal(peerId, loaded.PeerId);
        Assert.Equal(publicKeyA, loaded.ExportPublicKey());
    }

    [Fact]
    public void Load_ReturnsNull_AfterDelete()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        using (var identity = service.LoadOrCreate())
        {
        }

        new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector()).Delete();

        Assert.Null(service.Load());
    }
}
