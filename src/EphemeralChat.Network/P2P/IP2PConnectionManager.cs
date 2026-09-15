namespace EphemeralChat.Network.P2P;

/// <summary>
/// Application-facing P2P lifecycle. Implementations coordinate the signaling
/// plane without exposing signaling JSON or WebRTC objects.
/// </summary>
public interface IP2PConnectionManager : IAsyncDisposable
{
    event Action<P2PConnectionState>? ConnectionStateChanged;

    event Action? DataChannelOpened;

    event Action<string, P2PTextMessage>? TextMessageReceived;

    event Action<string>? OperationFailed;

    string? RemotePeerId { get; }

    P2PConnectionState State { get; }

    Task StartOutgoingAsync(string remotePeerId, CancellationToken ct = default);

    Task PrepareIncomingAsync(string remotePeerId, CancellationToken ct = default);

    Task<P2PTextMessage> SendTextAsync(string content, CancellationToken ct = default);
}
