using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EphemeralChat.Core.Signaling;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// Per-connection signaling logic. Strict forwarding role: payloads are relayed
/// immediately between linked peers and are never inspected, stored, or logged.
/// </summary>
public sealed class SignalingServer
{
    private readonly PeerRegistry _peers;
    private readonly PendingRequestRegistry _pending;
    private readonly SignalingSessions _sessions;
    private readonly SignalingServerOptions _options;
    private readonly TimeProvider _clock;

    public SignalingServer(
        PeerRegistry peers,
        PendingRequestRegistry pending,
        SignalingSessions sessions,
        SignalingServerOptions options,
        TimeProvider? clock = null)
    {
        _peers = peers;
        _pending = pending;
        _sessions = sessions;
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        PeerConnection? peer = null;
        try
        {
            peer = await RegisterHandshakeAsync(socket, ct);
            await ReceiveLoopAsync(peer, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
        {
            // Expected disconnect paths; cleanup happens in finally.
        }
        finally
        {
            if (peer is not null)
            {
                await DetachPeerAsync(peer, CancellationToken.None);
            }

            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, "server closing", CancellationToken.None);
                }
            }
            catch (Exception)
            {
                // Socket may already be gone.
            }
        }
    }

    /// <summary>Called by the sweeper when a peer outlived its presence TTL.</summary>
    public Task HandleIdlePeerAsync(PeerConnection peer, CancellationToken ct)
    {
        // The sweeper already removed the peer from the registry; make the
        // cleanup idempotent in case the peer's own handler raced us.
        _peers.Remove(peer.PeerId);
        _sessions.RemovePeer(peer.PeerId);
        _pending.RemoveByRequester(peer.PeerId);
        peer.Socket.Abort();
        return BroadcastPresenceAsync(peer.PeerId, publicKey: null, online: false, ct);
    }

    /// <summary>Called by the sweeper when a connect-request expired unanswered.</summary>
    public Task HandleExpiredRequestAsync(PendingRequest request, CancellationToken ct) =>
        !_peers.TryGet(request.RequesterId, out var requester)
            ? Task.CompletedTask
            : requester.SendAsync(
                new SignalingMessage
                {
                    Type = SignalingProtocol.MessageTypes.ConnectTimeout,
                    Payload = JsonSerializer.SerializeToElement(new { peerId = request.TargetId }, SignalingJson.Options)
                },
                ct);

    private async Task<PeerConnection> RegisterHandshakeAsync(WebSocket socket, CancellationToken ct)
    {
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshakeCts.CancelAfter(_options.RegistrationTimeout);

        var buffer = new byte[_options.MaxMessageBytes + 1];
        string? json = await ReceiveTextAsync(socket, buffer, handshakeCts.Token);
        if (json is null)
        {
            throw new WebSocketException("connection closed before registration");
        }

        SignalingMessage? message = TryDeserialize(json);
        if (message?.Type != SignalingProtocol.MessageTypes.Register)
        {
            throw new WebSocketException("first message must be register");
        }

        string? publicKey = null;
        if (message.Payload is { } payload && payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("publicKey", out var keyElement) &&
            keyElement.ValueKind == JsonValueKind.String)
        {
            publicKey = keyElement.GetString();
        }

        string? validationError = MessageValidator.ValidateRegistration(message.From, publicKey);
        if (validationError is not null)
        {
            await SendToSocketAsync(socket, ErrorMessage("registration-rejected", validationError), ct);
            throw new WebSocketException(validationError);
        }

        var connection = new PeerConnection(message.From!, socket, publicKey!);
        if (!_peers.TryRegister(connection))
        {
            await SendToSocketAsync(socket, ErrorMessage("registration-rejected", "peer id already in use"), ct);
            throw new WebSocketException("duplicate peer id");
        }

        connection.LastSeen = _clock.GetUtcNow();
        await connection.SendAsync(
            new SignalingMessage
            {
                Type = SignalingProtocol.MessageTypes.Registered,
                Payload = JsonSerializer.SerializeToElement(new { peerId = connection.PeerId }, SignalingJson.Options)
            },
            ct);
        await connection.SendAsync(PeersListMessage(connection.PeerId), ct);
        await BroadcastPresenceAsync(connection.PeerId, connection.PublicKeyBase64, online: true, ct);
        return connection;
    }

    private async Task ReceiveLoopAsync(PeerConnection peer, CancellationToken ct)
    {
        var buffer = new byte[_options.MaxMessageBytes + 1];
        while (!ct.IsCancellationRequested && peer.Socket.State == WebSocketState.Open)
        {
            string? json = await ReceiveTextAsync(peer.Socket, buffer, ct);
            if (json is null)
            {
                break;
            }

            _peers.MarkSeen(peer.PeerId);

            SignalingMessage? message = TryDeserialize(json);
            if (message is null || string.IsNullOrEmpty(message.Type))
            {
                await peer.SendAsync(ErrorMessage("bad-message", "message is not a valid signaling envelope"), ct);
                continue;
            }

            await DispatchAsync(peer, message, ct);
        }
    }

    private async Task DispatchAsync(PeerConnection peer, SignalingMessage message, CancellationToken ct)
    {
        switch (message.Type)
        {
            case SignalingProtocol.MessageTypes.Heartbeat:
                // LastSeen was already refreshed.
                break;

            case SignalingProtocol.MessageTypes.ConnectRequest:
                await HandleConnectRequestAsync(peer, message, ct);
                break;

            case SignalingProtocol.MessageTypes.ConnectAccept:
                await HandleConnectResponseAsync(peer, message, SignalingProtocol.MessageTypes.ConnectAccepted, ct);
                break;

            case SignalingProtocol.MessageTypes.ConnectReject:
                await HandleConnectResponseAsync(peer, message, SignalingProtocol.MessageTypes.ConnectRejected, ct);
                break;

            case SignalingProtocol.MessageTypes.Offer or SignalingProtocol.MessageTypes.Answer or SignalingProtocol.MessageTypes.Ice:
                await HandleForwardAsync(peer, message, ct);
                break;

            default:
                await peer.SendAsync(
                    ErrorMessage("unsupported-type", $"'{message.Type}' is not a valid client message"), ct);
                break;
        }
    }

    private async Task HandleConnectRequestAsync(PeerConnection peer, SignalingMessage message, CancellationToken ct)
    {
        string? error = MessageValidator.ValidateTarget(message.To, peer.PeerId);
        if (error is not null)
        {
            await peer.SendAsync(ErrorMessage("invalid-request", error), ct);
            return;
        }

        if (!_peers.TryGet(message.To!, out var target))
        {
            await peer.SendAsync(ErrorMessage("peer-not-online", "target peer is not registered"), ct);
            return;
        }

        if (_sessions.IsPaired(peer.PeerId, message.To!))
        {
            await peer.SendAsync(ErrorMessage("already-connected", "a session already exists with this peer"), ct);
            return;
        }

        if (!_pending.TryAdd(peer.PeerId, message.To!, _clock.GetUtcNow(), _options.PendingRequestTtl))
        {
            await peer.SendAsync(ErrorMessage("request-pending", "a previous request is still awaiting a response"), ct);
            return;
        }

        await target.SendAsync(
            new SignalingMessage { Type = SignalingProtocol.MessageTypes.ConnectRequest, From = peer.PeerId }, ct);
    }

    private async Task HandleConnectResponseAsync(
        PeerConnection peer, SignalingMessage message, string replyType, CancellationToken ct)
    {
        string? error = MessageValidator.ValidateTarget(message.To, peer.PeerId);
        if (error is not null)
        {
            await peer.SendAsync(ErrorMessage("invalid-request", error), ct);
            return;
        }

        PendingRequest? pending = _pending.TryPop(message.To!, peer.PeerId);
        if (pending is null)
        {
            await peer.SendAsync(ErrorMessage("no-pending-request", "no connect-request matches this response"), ct);
            return;
        }

        if (replyType == SignalingProtocol.MessageTypes.ConnectAccepted)
        {
            _sessions.AddPair(pending.RequesterId, pending.TargetId);
        }

        if (_peers.TryGet(pending.RequesterId, out var requester))
        {
            await requester.SendAsync(
                new SignalingMessage { Type = replyType, From = peer.PeerId }, ct);
        }
    }

    private async Task HandleForwardAsync(PeerConnection peer, SignalingMessage message, CancellationToken ct)
    {
        string? error = MessageValidator.ValidateTarget(message.To, peer.PeerId);
        if (error is not null)
        {
            await peer.SendAsync(ErrorMessage("invalid-request", error), ct);
            return;
        }

        if (!_peers.TryGet(message.To!, out var target))
        {
            await peer.SendAsync(ErrorMessage("peer-not-online", "target peer is not registered"), ct);
            return;
        }

        if (!_sessions.IsPaired(peer.PeerId, message.To!))
        {
            await peer.SendAsync(
                ErrorMessage("no-active-session", "accept a connection request before exchanging session data"), ct);
            return;
        }

        // Relay as-is: the payload (SDP / ICE) is opaque to the server and is
        // never stored anywhere.
        await target.SendAsync(
            new SignalingMessage
            {
                Type = message.Type,
                From = peer.PeerId,
                To = message.To,
                Payload = message.Payload
            },
            ct);
    }

    private async Task DetachPeerAsync(PeerConnection peer, CancellationToken ct)
    {
        _peers.Remove(peer.PeerId);
        _sessions.RemovePeer(peer.PeerId);
        _pending.RemoveByRequester(peer.PeerId);
        await BroadcastPresenceAsync(peer.PeerId, publicKey: null, online: false, ct);
    }

    private async Task BroadcastPresenceAsync(string subjectId, string? publicKey, bool online, CancellationToken ct)
    {
        var message = new SignalingMessage
        {
            Type = SignalingProtocol.MessageTypes.Presence,
            Payload = JsonSerializer.SerializeToElement(
                new { peerId = subjectId, publicKey, online }, SignalingJson.Options)
        };

        foreach (PeerConnection otherPeer in _peers.Snapshot())
        {
            if (otherPeer.PeerId == subjectId)
            {
                continue;
            }

            try
            {
                await otherPeer.SendAsync(message, ct);
            }
            catch (Exception)
            {
                // The peer is closing; its own handler cleans it up.
            }
        }
    }

    private SignalingMessage PeersListMessage(string forPeerId) =>
        new()
        {
            Type = SignalingProtocol.MessageTypes.Peers,
            Payload = JsonSerializer.SerializeToElement(
                new
                {
                    peers = _peers.Snapshot()
                        .Where(p => p.PeerId != forPeerId)
                        .Select(p => new { peerId = p.PeerId, publicKey = p.PublicKeyBase64 })
                },
                SignalingJson.Options)
        };

    private static SignalingMessage ErrorMessage(string code, string description) =>
        new()
        {
            Type = SignalingProtocol.MessageTypes.Error,
            Payload = JsonSerializer.SerializeToElement(new { code, message = description }, SignalingJson.Options)
        };

    private static SignalingMessage? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SignalingMessage>(json, SignalingJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (true)
        {
            ValueWebSocketReceiveResult result =
                await socket.ReceiveAsync(buffer.AsMemory(total), ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            total += result.Count;
            if (total > _options.MaxMessageBytes)
            {
                await SendToSocketAsync(socket, ErrorMessage("message-too-large", "signaling message exceeds the size limit"), ct);
                return null;
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }
    }

    private static async Task SendToSocketAsync(WebSocket socket, SignalingMessage message, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, SignalingJson.Options);
        if (socket.State == WebSocketState.Open)
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
    }
}
