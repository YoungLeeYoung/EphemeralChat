namespace EphemeralChat.WebRTC;

/// <summary>
/// Transport-agnostic WebRTC peer connection abstraction. The signaling plane
/// delivers SDP and ICE through this interface; the WebRTC library details stay
/// behind it.
/// </summary>
public interface IWebRtcPeerConnection : IAsyncDisposable
{
    /// <summary>Fired when a local ICE candidate becomes available.</summary>
    event Action<string>? IceCandidateGenerated;

    /// <summary>Fired when the overall connection state changes.</summary>
    event Action<WebRtcConnectionState>? ConnectionStateChanged;

    /// <summary>Fired when the peer connection's data channel is open.</summary>
    event Action? DataChannelOpened;

    /// <summary>
    /// Creates the application data channel before the offer is produced.
    /// The answerer receives the same channel through <see cref="DataChannelOpened"/>.
    /// </summary>
    Task CreateDataChannelAsync(string label, CancellationToken ct = default);

    /// <summary>Creates a local SDP offer and starts gathering ICE candidates.</summary>
    Task<string> CreateOfferAsync(CancellationToken ct = default);

    /// <summary>Applies a remote offer and produces a local SDP answer.</summary>
    Task<string> AcceptOfferAsync(string offerSdpJson, CancellationToken ct = default);

    /// <summary>Applies the remote answer produced by the callee.</summary>
    Task AcceptAnswerAsync(string answerSdpJson, CancellationToken ct = default);

    /// <summary>Adds a remote ICE candidate (JSON serialized).</summary>
    Task AddIceCandidateAsync(string candidateSdpJson, CancellationToken ct = default);
}
