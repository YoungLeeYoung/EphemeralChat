using System.Security.Cryptography;

namespace EphemeralChat.Security.Identity;

/// <summary>
/// A local peer identity: one ECDSA P-256 key pair plus its derived Peer ID.
/// The private key never leaves this object (and its encrypted store).
/// </summary>
public sealed class LocalIdentity : IDisposable
{
    private readonly ECDsa _key;

    public LocalIdentity(ECDsa key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        PeerId = PeerIdCalculator.FromPublicKey(key);
    }

    public string PeerId { get; }

    public byte[] ExportPublicKey() => _key.ExportSubjectPublicKeyInfo();

    public byte[] ExportPrivateKeyPkcs8() => _key.ExportPkcs8PrivateKey();

    public void Dispose() => _key.Dispose();
}
