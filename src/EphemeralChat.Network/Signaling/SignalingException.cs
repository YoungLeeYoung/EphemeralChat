namespace EphemeralChat.Network.Signaling;

/// <summary>Protocol-level failure raised on the client side.</summary>
public sealed class SignalingException : Exception
{
    public SignalingException(string message) : base(message)
    {
    }
}
