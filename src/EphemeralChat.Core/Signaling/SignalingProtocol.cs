namespace EphemeralChat.Core.Signaling;

/// <summary>
/// Protocol-wide constants shared by the signaling server and clients.
/// Retention policy: the server holds presence, pending requests, and session
/// links in memory only, each bounded by a TTL; message payloads are forwarded
/// immediately and never stored.
/// </summary>
public static class SignalingProtocol
{
    public const string WebSocketPath = "/ws";
    public const int DefaultPort = 8080;
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>How often registered clients prove liveness.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    public static class MessageTypes
    {
        public const string Register = "register";
        public const string Registered = "registered";
        public const string Peers = "peers";
        public const string Presence = "presence";
        public const string Heartbeat = "heartbeat";
        public const string ConnectRequest = "connect-request";
        public const string ConnectAccept = "connect-accept";
        public const string ConnectReject = "connect-reject";
        public const string ConnectAccepted = "connect-accepted";
        public const string ConnectRejected = "connect-rejected";
        public const string ConnectTimeout = "connect-timeout";
        public const string Offer = "offer";
        public const string Answer = "answer";
        public const string Ice = "ice";
        public const string Error = "error";
    }
}
