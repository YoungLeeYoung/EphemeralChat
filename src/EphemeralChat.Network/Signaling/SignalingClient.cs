using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using EphemeralChat.Core.Signaling;

namespace EphemeralChat.Network.Signaling;

/// <summary>
/// Signaling-plane client transport. Carries only connection setup messages;
/// chat/file/voice data never flows through this class (data plane is WebRTC,
/// Milestone 3+).
/// </summary>
public sealed partial class SignalingClient : IAsyncDisposable
{
    private readonly string _peerId;
    private readonly string _publicKeyBase64;
    private readonly TimeSpan _heartbeatInterval;
    private SemaphoreSlim? _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCts;

    public SignalingClient(string peerId, string publicKeyBase64, TimeSpan? heartbeatInterval = null)
    {
        if (!PeerIdPattern().IsMatch(peerId))
        {
            throw new ArgumentException("peer id must be 26 uppercase Base32 characters", nameof(peerId));
        }

        _peerId = peerId;
        _publicKeyBase64 = publicKeyBase64;
        _heartbeatInterval = heartbeatInterval ?? SignalingProtocol.HeartbeatInterval;
    }

    [GeneratedRegex("^[A-Z2-7]{26}$")]
    private static partial Regex PeerIdPattern();

    public event Action<PeerPresence>? PresenceChanged;
    public event Action<IReadOnlyList<PeerPresence>>? PeerListReceived;
    public event Action<string>? ConnectionRequested;
    public event Action<string, ConnectionRequestOutcome>? RequestResponded;
    public event Action<string, JsonElement>? OfferReceived;
    public event Action<string, JsonElement>? AnswerReceived;
    public event Action<string, JsonElement>? IceCandidateReceived;
    public event Action<string>? ErrorReceived;
    public event Action? Disconnected;

    public string PeerId => _peerId;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAndRegisterAsync(Uri serverUri, CancellationToken ct = default)
    {
        if (_socket is not null)
        {
            throw new InvalidOperationException("client is already connected");
        }

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(serverUri, ct);
        await SendCoreAsync(
            new SignalingMessage
            {
                Type = SignalingProtocol.MessageTypes.Register,
                From = _peerId,
                Payload = JsonSerializer.SerializeToElement(
                    new { publicKey = _publicKeyBase64 }, SignalingJson.Options)
            },
            ct);

        // Handshake: consume messages until the server confirms registration.
        while (true)
        {
            SignalingMessage message = await ReceiveOneAsync(ct);
            if (message.Type == SignalingProtocol.MessageTypes.Registered)
            {
                break;
            }

            if (message.Type == SignalingProtocol.MessageTypes.Error)
            {
                throw new SignalingException(ExtractError(message));
            }

            Dispatch(message);
        }

        _sessionCts = new CancellationTokenSource();
        CancellationToken sessionToken = _sessionCts.Token;
        _ = Task.Run(() => ReceiveLoopAsync(sessionToken), CancellationToken.None);
        _ = Task.Run(() => HeartbeatLoopAsync(sessionToken), CancellationToken.None);
    }

    public Task SendConnectRequestAsync(string toPeerId, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.ConnectRequest, toPeerId, ct);

    public Task SendAcceptAsync(string toPeerId, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.ConnectAccept, toPeerId, ct);

    public Task SendRejectAsync(string toPeerId, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.ConnectReject, toPeerId, ct);

    public Task SendOfferAsync(string toPeerId, JsonElement payload, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.Offer, toPeerId, ct, payload);

    public Task SendAnswerAsync(string toPeerId, JsonElement payload, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.Answer, toPeerId, ct, payload);

    public Task SendIceCandidateAsync(string toPeerId, JsonElement payload, CancellationToken ct = default) =>
        SendDirectedAsync(SignalingProtocol.MessageTypes.Ice, toPeerId, ct, payload);

    public async ValueTask DisposeAsync()
    {
        _sessionCts?.Cancel();
        _socket?.Abort();
        _socket?.Dispose();
        _socket = null;
        Interlocked.Exchange(ref _sendLock, null)?.Dispose();
        await Task.CompletedTask;
    }

    private Task SendDirectedAsync(string type, string toPeerId, CancellationToken ct, JsonElement? payload = null)
    {
        if (!PeerIdPattern().IsMatch(toPeerId))
        {
            throw new ArgumentException("invalid target peer id", nameof(toPeerId));
        }

        return SendCoreAsync(new SignalingMessage { Type = type, To = toPeerId, Payload = payload }, ct);
    }

    private async Task SendCoreAsync(SignalingMessage message, CancellationToken ct)
    {
        ClientWebSocket socket = _socket ?? throw new InvalidOperationException("client is not connected");
        SemaphoreSlim sendLock = _sendLock ?? throw new InvalidOperationException("client is disposed");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, SignalingJson.Options);

        await sendLock.WaitAsync(ct);
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new SignalingException("signaling connection is not open");
            }

            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Dispatch(await ReceiveOneAsync(ct));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Disconnected?.Invoke();
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_heartbeatInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await SendCoreAsync(
                        new SignalingMessage { Type = SignalingProtocol.MessageTypes.Heartbeat },
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    return; // Receive loop reports the disconnect.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<SignalingMessage> ReceiveOneAsync(CancellationToken ct)
    {
        ClientWebSocket socket = _socket ?? throw new InvalidOperationException("client is not connected");
        var buffer = new byte[SignalingProtocol.MaxMessageBytes + 1];
        int total = 0;

        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(total), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new SignalingException("connection closed by the server");
            }

            total += result.Count;
            if (total > SignalingProtocol.MaxMessageBytes)
            {
                throw new SignalingException("server message exceeds the size limit");
            }

            if (result.EndOfMessage)
            {
                SignalingMessage? message = JsonSerializer.Deserialize<SignalingMessage>(
                    buffer.AsSpan(0, total), SignalingJson.Options);
                return message ?? throw new SignalingException("server sent an empty message");
            }
        }
    }

    private void Dispatch(SignalingMessage message)
    {
        switch (message.Type)
        {
            case SignalingProtocol.MessageTypes.Presence:
                PresenceChanged?.Invoke(ParsePresence(message.Payload));
                break;

            case SignalingProtocol.MessageTypes.Peers:
                PeerListReceived?.Invoke(ParsePeerList(message.Payload));
                break;

            case SignalingProtocol.MessageTypes.ConnectRequest:
                if (message.From is not null)
                {
                    ConnectionRequested?.Invoke(message.From);
                }

                break;

            case SignalingProtocol.MessageTypes.ConnectAccepted:
            case SignalingProtocol.MessageTypes.ConnectRejected:
                if (message.From is not null)
                {
                    RequestResponded?.Invoke(
                        message.From,
                        message.Type == SignalingProtocol.MessageTypes.ConnectAccepted
                            ? ConnectionRequestOutcome.Accepted
                            : ConnectionRequestOutcome.Rejected);
                }

                break;

            case SignalingProtocol.MessageTypes.ConnectTimeout:
                string target = message.Payload is { } timeoutPayload &&
                                timeoutPayload.ValueKind == JsonValueKind.Object &&
                                timeoutPayload.TryGetProperty("peerId", out var idElement) &&
                                idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()!
                    : throw new SignalingException("connect-timeout message is malformed");
                RequestResponded?.Invoke(target, ConnectionRequestOutcome.TimedOut);
                break;

            case SignalingProtocol.MessageTypes.Offer:
                RaisePayloadEvent(OfferReceived, message);
                break;

            case SignalingProtocol.MessageTypes.Answer:
                RaisePayloadEvent(AnswerReceived, message);
                break;

            case SignalingProtocol.MessageTypes.Ice:
                RaisePayloadEvent(IceCandidateReceived, message);
                break;

            case SignalingProtocol.MessageTypes.Error:
                ErrorReceived?.Invoke(ExtractError(message));
                break;
        }
    }

    private void RaisePayloadEvent(
        Action<string, JsonElement>? handler, SignalingMessage message)
    {
        if (handler is null || message.From is null)
        {
            return;
        }

        if (message.Payload is not { } payload)
        {
            throw new SignalingException($"'{message.Type}' message is missing its payload");
        }

        handler(message.From, payload);
    }

    private static PeerPresence ParsePresence(JsonElement? payloadOpt)
    {
        if (payloadOpt is not { } payload || payload.ValueKind != JsonValueKind.Object)
        {
            throw new SignalingException("presence message is malformed");
        }

        string peerId = payload.TryGetProperty("peerId", out var idElement) &&
                        idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()!
            : throw new SignalingException("presence message is missing peerId");
        string? publicKey = payload.TryGetProperty("publicKey", out var keyElement) &&
                            keyElement.ValueKind == JsonValueKind.String
            ? keyElement.GetString()
            : null;
        bool online = payload.TryGetProperty("online", out var onlineElement) &&
                      onlineElement.ValueKind == JsonValueKind.True;
        return new PeerPresence(peerId, publicKey, online);
    }

    private static IReadOnlyList<PeerPresence> ParsePeerList(JsonElement? payloadOpt)
    {
        if (payloadOpt is not { } payload ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("peers", out var peersElement) ||
            peersElement.ValueKind != JsonValueKind.Array)
        {
            throw new SignalingException("peers message is malformed");
        }

        List<PeerPresence> peers = [];
        foreach (JsonElement entry in peersElement.EnumerateArray())
        {
            peers.Add(new PeerPresence(
                entry.GetProperty("peerId").GetString()!,
                entry.TryGetProperty("publicKey", out var keyElement) &&
                keyElement.ValueKind == JsonValueKind.String
                    ? keyElement.GetString()
                    : null,
                IsOnline: true));
        }

        return peers;
    }

    private static string ExtractError(SignalingMessage message)
    {
        if (message.Payload is { } payload && payload.ValueKind == JsonValueKind.Object)
        {
            string code = payload.TryGetProperty("code", out var codeElement) &&
                          codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()!
                : "error";
            string? description = payload.TryGetProperty("message", out var messageElement) &&
                                  messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;
            return description is null ? code : $"{code}: {description}";
        }

        return "signaling error";
    }
}
