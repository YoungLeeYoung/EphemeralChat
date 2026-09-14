namespace EphemeralChat.Security.Identity.Persistence;

/// <summary>
/// The payload handed to / returned from an <see cref="ILocalIdentityStore"/>:
/// the PKCS#8 private key bytes. Stores never expose these to callers other
/// than the identity service.
/// </summary>
public sealed record IdentityStorageData(byte[] PrivateKeyPkcs8);
