using System.Net.WebSockets;
using System.Text.Json;
using EphemeralChat.Core.Signaling;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// One registered peer: its WebSocket plus presence metadata. Sends are
/// serialized per peer because WebSocket permits a single concurrent send.
/// </summary>
public sealed class PeerConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public PeerConnection(string peerId, WebSocket socket, string publicKeyBase64)
    {
        PeerId = peerId;
        Socket = socket;
        PublicKeyBase64 = publicKeyBase64;
        LastSeen = DateTimeOffset.MinValue;
    }

    public string PeerId { get; }

    public WebSocket Socket { get; }

    public string PublicKeyBase64 { get; }

    public DateTimeOffset LastSeen { get; set; }

    public async Task SendAsync(SignalingMessage message, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, SignalingJson.Options);
        await _sendLock.WaitAsync(ct);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
