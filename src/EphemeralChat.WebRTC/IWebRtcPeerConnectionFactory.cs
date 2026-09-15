namespace EphemeralChat.WebRTC;

/// <summary>
/// Creates isolated peer connections. Keeping this behind a factory allows the
/// connection coordinator and application layer to be tested without real ICE.
/// </summary>
public interface IWebRtcPeerConnectionFactory
{
    IWebRtcPeerConnection Create();
}
