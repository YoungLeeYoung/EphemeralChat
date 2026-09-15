namespace EphemeralChat.Network.Signaling;

/// <summary>A peer's presence as seen through the signaling server.</summary>
public sealed record PeerPresence(string PeerId, string? PublicKeyBase64, bool IsOnline);

/// <summary>The outcome of a connection request initiated by this peer.</summary>
public enum ConnectionRequestOutcome
{
    Accepted,
    Rejected,
    TimedOut
}
