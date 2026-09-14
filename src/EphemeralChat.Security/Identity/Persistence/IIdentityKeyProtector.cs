namespace EphemeralChat.Security.Identity.Persistence;

/// <summary>
/// Protects raw key material at rest. The production implementation uses DPAPI;
/// tests may substitute a fake to avoid OS dependencies.
/// </summary>
public interface IIdentityKeyProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}
