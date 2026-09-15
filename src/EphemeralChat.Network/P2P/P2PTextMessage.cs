namespace EphemeralChat.Network.P2P;

/// <summary>
/// An in-memory text chat payload. No copy is retained by the transport after
/// delivery; persistence is deliberately outside the P2P layer.
/// </summary>
public sealed record P2PTextMessage(
    string MessageId,
    string Content,
    DateTimeOffset SentAt);
