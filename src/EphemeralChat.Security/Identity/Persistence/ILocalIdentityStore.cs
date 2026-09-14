namespace EphemeralChat.Security.Identity.Persistence;

/// <summary>
/// Persists exactly one local identity. Implementations are expected to store
/// key material only in protected (encrypted) form.
/// </summary>
public interface ILocalIdentityStore
{
    bool Exists();

    void Save(IdentityStorageData data);

    IdentityStorageData? Load();

    void Delete();
}
