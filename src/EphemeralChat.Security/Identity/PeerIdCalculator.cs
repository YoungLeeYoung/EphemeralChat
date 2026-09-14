using System.Security.Cryptography;
using EphemeralChat.Security.Primitives;

namespace EphemeralChat.Security.Identity;

/// <summary>
/// Derives the stable Peer ID from an ECDSA P-256 public key:
/// PeerId = Base32(SHA-256(Qx || Qy)[0..16]), unpadded — 128 bits of strength.
/// Same key always yields the same ID.
/// </summary>
public static class PeerIdCalculator
{
    private const int CoordinateSize = 32;
    private const int HashLength = 32;
    private const int PeerIdBytes = 16;

    public static string FromPublicKey(ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        ECParameters parameters = publicKey.ExportParameters(includePrivateParameters: false);
        Span<byte> input = stackalloc byte[CoordinateSize * 2];
        NormalizeCoordinate(parameters.Q.X).CopyTo(input);
        NormalizeCoordinate(parameters.Q.Y).CopyTo(input[CoordinateSize..]);

        Span<byte> hash = stackalloc byte[HashLength];
        SHA256.HashData(input, hash);

        return Base32Encoder.Encode(hash[..PeerIdBytes]);
    }

    private static byte[] NormalizeCoordinate(byte[]? coordinate)
    {
        if (coordinate is null || coordinate.Length == 0)
        {
            throw new InvalidOperationException("Public key is missing a curve point coordinate.");
        }

        if (coordinate.Length > CoordinateSize)
        {
            throw new InvalidOperationException("Curve point coordinate is unexpectedly long.");
        }

        // Export may strip leading zero bytes; restore fixed-width big-endian form
        // so the derivation is deterministic regardless of encoding quirks.
        var normalized = new byte[CoordinateSize];
        Buffer.BlockCopy(coordinate, 0, normalized, CoordinateSize - coordinate.Length, coordinate.Length);
        return normalized;
    }
}
