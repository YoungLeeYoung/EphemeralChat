namespace EphemeralChat.WebRTC;

public sealed class SipsorceryPeerConnectionFactory : IWebRtcPeerConnectionFactory
{
    public IWebRtcPeerConnection Create() => new SipsorceryPeerConnection();
}
