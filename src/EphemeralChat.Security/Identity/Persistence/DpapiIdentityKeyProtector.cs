using System.Security.Cryptography;

namespace EphemeralChat.Security.Identity.Persistence;

/// <summary>
/// DPAPI (CurrentUser scope) protection with app-specific optional entropy.
/// The entropy is not a secret; it only prevents other apps' DPAPI blobs from
/// being silently interchangeable with ours.
/// </summary>
public sealed class DpapiIdentityKeyProtector : IIdentityKeyProtector
{
    private static readonly byte[] Entropy = "EphemeralChat-Identity-v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
