using System.Security.Cryptography;
using EphemeralChat.Security.Identity.Persistence;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Security;

public class DpapiFileIdentityStoreTests
{
    [Fact]
    public void Load_ReturnsNull_WhenNothingWasSaved()
    {
        using var temp = new TempDirectory();
        var store = new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector());

        Assert.False(store.Exists());
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsKeyMaterial()
    {
        using var temp = new TempDirectory();
        var store = new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector());
        byte[] pkcs8;
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            pkcs8 = key.ExportPkcs8PrivateKey();
        }

        store.Save(new IdentityStorageData(pkcs8));

        Assert.True(store.Exists());
        IdentityStorageData loaded = store.Load() ?? throw new InvalidOperationException("Load returned null");
        Assert.Equal(pkcs8, loaded.PrivateKeyPkcs8);
    }

    [Fact]
    public void SavedFile_DoesNotContainPlaintextKeyMaterial()
    {
        using var temp = new TempDirectory();
        var store = new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector());
        using var identityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] pkcs8 = identityKey.ExportPkcs8PrivateKey();

        store.Save(new IdentityStorageData(pkcs8));

        byte[] fileBytes = File.ReadAllBytes(Path.Combine(temp.FullPath, "identity.dat"));
        Assert.Equal(-1, fileBytes.AsSpan().IndexOf(pkcs8.AsSpan()));
    }

    [Fact]
    public void Load_ThrowsCryptographicException_WhenFileWasTamperedWith()
    {
        using var temp = new TempDirectory();
        var store = new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector());
        byte[] pkcs8;
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            pkcs8 = key.ExportPkcs8PrivateKey();
        }

        store.Save(new IdentityStorageData(pkcs8));
        string filePath = Path.Combine(temp.FullPath, "identity.dat");
        byte[] fileBytes = File.ReadAllBytes(filePath);
        fileBytes[^1] ^= 0xFF;
        File.WriteAllBytes(filePath, fileBytes);

        Assert.ThrowsAny<CryptographicException>(() => store.Load());
    }

    [Fact]
    public void Delete_RemovesTheStoredIdentity()
    {
        using var temp = new TempDirectory();
        var store = new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector());
        byte[] pkcs8;
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            pkcs8 = key.ExportPkcs8PrivateKey();
        }

        store.Save(new IdentityStorageData(pkcs8));
        store.Delete();

        Assert.False(store.Exists());
        Assert.Null(store.Load());
    }
}
