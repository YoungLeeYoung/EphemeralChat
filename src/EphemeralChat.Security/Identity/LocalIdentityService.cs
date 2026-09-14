using System.Security.Cryptography;
using EphemeralChat.Security.Identity.Persistence;

namespace EphemeralChat.Security.Identity;

/// <summary>
/// Orchestrates identity lifecycle: creation, persistence, loading.
/// Corruption or wrong-user decryption errors propagate to the caller instead
/// of silently discarding the stored identity.
/// </summary>
public sealed class LocalIdentityService
{
    private readonly ILocalIdentityStore _store;

    public LocalIdentityService(ILocalIdentityStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public LocalIdentity CreateNew()
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new LocalIdentity(key);
    }

    public void Save(LocalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _store.Save(new IdentityStorageData(identity.ExportPrivateKeyPkcs8()));
    }

    /// <summary>
    /// Deletes the stored identity file. Callers must obtain explicit user
    /// consent first — this makes the current Peer ID unrecoverable.
    /// </summary>
    public void Delete() => _store.Delete();

    public LocalIdentity? Load()
    {
        IdentityStorageData? data = _store.Load();
        return data is null ? null : Import(data.PrivateKeyPkcs8);
    }

    public LocalIdentity LoadOrCreate()
    {
        if (_store.Load() is { } data)
        {
            return Import(data.PrivateKeyPkcs8);
        }

        LocalIdentity identity = CreateNew();
        Save(identity);
        return identity;
    }

    private static LocalIdentity Import(byte[] privateKeyPkcs8)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return new LocalIdentity(key);
    }
}
