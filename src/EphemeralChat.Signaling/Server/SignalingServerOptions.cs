using EphemeralChat.Core.Signaling;

namespace EphemeralChat.Signaling.Server;

public sealed record SignalingServerOptions
{
    /// <summary>Peers that stay silent longer than this are evicted.</summary>
    public TimeSpan PeerIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Unanswered connect-requests expire after this.</summary>
    public TimeSpan PendingRequestTtl { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RegistrationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaxMessageBytes { get; init; } = SignalingProtocol.MaxMessageBytes;
}
