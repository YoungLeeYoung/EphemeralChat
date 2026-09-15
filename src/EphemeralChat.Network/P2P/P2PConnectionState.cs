namespace EphemeralChat.Network.P2P;

/// <summary>
/// UI-facing P2P state. It deliberately does not expose SDP, ICE, or library
/// objects to the application layer.
/// </summary>
public enum P2PConnectionState
{
    Idle,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed
}
