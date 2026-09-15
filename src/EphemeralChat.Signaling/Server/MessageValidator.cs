using System.Text.RegularExpressions;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// Input validation for the signaling protocol. The server deliberately does
/// not verify that a peerId is the hash of the supplied public key — that
/// binding check is a client-side authentication concern (Milestone 3+).
/// </summary>
public static partial class MessageValidator
{
    [GeneratedRegex("^[A-Z2-7]{26}$")]
    private static partial Regex PeerIdPattern();

    public static bool IsValidPeerId(string? peerId) =>
        peerId is not null && PeerIdPattern().IsMatch(peerId);

    /// <returns>null when valid, otherwise a human-readable reason.</returns>
    public static string? ValidateRegistration(string? peerId, string? publicKeyBase64)
    {
        if (!IsValidPeerId(peerId))
        {
            return "invalid peer id format";
        }

        if (string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            return "missing public key";
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            return "public key is not valid base64";
        }

        if (keyBytes.Length is < 64 or > 1024)
        {
            return "public key size is out of range";
        }

        return null;
    }

    public static string? ValidateTarget(string? targetId, string selfId)
    {
        if (!IsValidPeerId(targetId))
        {
            return "invalid target peer id format";
        }

        return targetId == selfId ? "cannot target self" : null;
    }
}
