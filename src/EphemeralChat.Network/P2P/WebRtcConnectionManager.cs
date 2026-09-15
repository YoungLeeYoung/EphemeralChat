using System.Text.Json;
using EphemeralChat.Network.Signaling;
using EphemeralChat.WebRTC;

namespace EphemeralChat.Network.P2P;

/// <summary>
/// Coordinates one P2P WebRTC session. The signaling client is used only for
/// ephemeral offer/answer/ICE forwarding; it never carries application data and
/// this class never stores payloads after negotiation.
/// </summary>
public sealed class WebRtcConnectionManager : IP2PConnectionManager
{
    public const string DefaultDataChannelLabel = "ephemeralchat-control";

    private readonly SignalingClient _signalingClient;
    private readonly IWebRtcPeerConnectionFactory _factory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Queue<string> _pendingRemoteCandidates = new();
    private IWebRtcPeerConnection? _connection;
    private string? _remotePeerId;
    private bool _isInitiator;
    private bool _hasRemoteDescription;
    private bool _isDisposed;
    private P2PConnectionState _state = P2PConnectionState.Idle;

    public WebRtcConnectionManager(
        SignalingClient signalingClient,
        IWebRtcPeerConnectionFactory? factory = null)
    {
        _signalingClient = signalingClient;
        _factory = factory ?? new SipsorceryPeerConnectionFactory();
        _signalingClient.OfferReceived += OnOfferReceived;
        _signalingClient.AnswerReceived += OnAnswerReceived;
        _signalingClient.IceCandidateReceived += OnIceCandidateReceived;
    }

    public event Action<P2PConnectionState>? ConnectionStateChanged;

    public event Action? DataChannelOpened;

    public event Action<string>? OperationFailed;

    /// <summary>
    /// Creates the real implementation while hiding the WebRTC factory type
    /// from UI-facing code.
    /// </summary>
    public static IP2PConnectionManager Create(SignalingClient signalingClient) =>
        new WebRtcConnectionManager(signalingClient);

    public string? RemotePeerId => _remotePeerId;

    public P2PConnectionState State => _state;

    public async Task StartOutgoingAsync(string remotePeerId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                throw new InvalidOperationException("a P2P connection is already active");
            }

            IWebRtcPeerConnection connection = _factory.Create();
            AttachConnection(connection);
            _remotePeerId = remotePeerId;
            _isInitiator = true;
            _hasRemoteDescription = false;
            _pendingRemoteCandidates.Clear();
            SetState(P2PConnectionState.Connecting);

            await connection.CreateDataChannelAsync(DefaultDataChannelLabel, ct);
            string offerSdp = await connection.CreateOfferAsync(ct);
            await _signalingClient.SendOfferAsync(
                remotePeerId, ParsePayload(offerSdp, "offer"), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ResetLockedAsync();
            SetState(P2PConnectionState.Failed);
            OperationFailed?.Invoke("P2P offer could not be created or sent");
            throw new InvalidOperationException("P2P connection setup failed", ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task PrepareIncomingAsync(string remotePeerId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                if (_remotePeerId == remotePeerId)
                {
                    return;
                }

                throw new InvalidOperationException("a P2P connection is already active");
            }

            IWebRtcPeerConnection connection = _factory.Create();
            AttachConnection(connection);
            _remotePeerId = remotePeerId;
            _isInitiator = false;
            _hasRemoteDescription = false;
            _pendingRemoteCandidates.Clear();
            SetState(P2PConnectionState.Connecting);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ResetLockedAsync();
            SetState(P2PConnectionState.Failed);
            OperationFailed?.Invoke("P2P session could not be prepared");
            throw new InvalidOperationException("P2P session preparation failed", ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async void OnOfferReceived(string fromPeerId, JsonElement payload)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_connection is null || _isInitiator || _remotePeerId != fromPeerId)
            {
                return;
            }

            string answerSdp = await _connection.AcceptOfferAsync(payload.GetRawText());
            _hasRemoteDescription = true;
            await FlushRemoteCandidatesLockedAsync();
            await _signalingClient.SendAnswerAsync(
                fromPeerId, ParsePayload(answerSdp, "answer"));
        }
        catch (Exception ex)
        {
            _ = ResetLockedAsync();
            SetState(P2PConnectionState.Failed);
            OperationFailed?.Invoke("P2P offer processing failed");
            _ = Task.FromException(ex); // Preserve the failure for diagnostics.
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async void OnAnswerReceived(string fromPeerId, JsonElement payload)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_connection is null || !_isInitiator || _remotePeerId != fromPeerId)
            {
                return;
            }

            await _connection.AcceptAnswerAsync(payload.GetRawText());
            _hasRemoteDescription = true;
            await FlushRemoteCandidatesLockedAsync();
        }
        catch (Exception ex)
        {
            _ = ResetLockedAsync();
            SetState(P2PConnectionState.Failed);
            OperationFailed?.Invoke("P2P answer processing failed");
            _ = Task.FromException(ex); // Preserve the failure for diagnostics.
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async void OnIceCandidateReceived(string fromPeerId, JsonElement payload)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_connection is null || _remotePeerId != fromPeerId)
            {
                return;
            }

            string candidateJson = payload.GetRawText();
            if (_hasRemoteDescription)
            {
                await _connection.AddIceCandidateAsync(candidateJson);
            }
            else
            {
                _pendingRemoteCandidates.Enqueue(candidateJson);
            }
        }
        catch (Exception ex)
        {
            OperationFailed?.Invoke("P2P ICE candidate processing failed");
            _ = Task.FromException(ex); // Preserve the failure for diagnostics.
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void AttachConnection(IWebRtcPeerConnection connection)
    {
        connection.ConnectionStateChanged += OnPeerConnectionStateChanged;
        connection.DataChannelOpened += OnDataChannelOpened;
        connection.IceCandidateGenerated += OnLocalIceCandidateGenerated;
        _connection = connection;
    }

    private async void OnLocalIceCandidateGenerated(string candidateJson)
    {
        string? remotePeerId = _remotePeerId;
        if (_isDisposed || remotePeerId is null)
        {
            return;
        }

        try
        {
            await _signalingClient.SendIceCandidateAsync(
                remotePeerId, ParsePayload(candidateJson, "ice"));
        }
        catch (Exception)
        {
            // The signaling disconnect path reports transport failures; ICE
            // send failures are surfaced through the resulting P2P timeout.
        }
    }

    private void OnPeerConnectionStateChanged(WebRtcConnectionState state)
    {
        P2PConnectionState mapped = state switch
        {
            WebRtcConnectionState.New => P2PConnectionState.Idle,
            WebRtcConnectionState.Connecting => P2PConnectionState.Connecting,
            WebRtcConnectionState.Connected => P2PConnectionState.Connected,
            WebRtcConnectionState.Disconnected => P2PConnectionState.Disconnected,
            WebRtcConnectionState.Failed => P2PConnectionState.Failed,
            WebRtcConnectionState.Closed => P2PConnectionState.Closed,
            _ => P2PConnectionState.Failed
        };

        SetState(mapped);
    }

    private void OnDataChannelOpened()
    {
        DataChannelOpened?.Invoke();
        SetState(P2PConnectionState.Connected);
    }

    private async Task FlushRemoteCandidatesLockedAsync()
    {
        if (_connection is null)
        {
            return;
        }

        while (_pendingRemoteCandidates.Count > 0)
        {
            string candidate = _pendingRemoteCandidates.Dequeue();
            await _connection.AddIceCandidateAsync(candidate);
        }
    }

    private async Task ResetLockedAsync()
    {
        IWebRtcPeerConnection? connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            connection.ConnectionStateChanged -= OnPeerConnectionStateChanged;
            connection.DataChannelOpened -= OnDataChannelOpened;
            connection.IceCandidateGenerated -= OnLocalIceCandidateGenerated;
            await connection.DisposeAsync();
        }

        _remotePeerId = null;
        _isInitiator = false;
        _hasRemoteDescription = false;
        _pendingRemoteCandidates.Clear();
    }

    private void SetState(P2PConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(state);
    }

    private static JsonElement ParsePayload(string json, string expectedKind)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{expectedKind} payload is not valid JSON", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _signalingClient.OfferReceived -= OnOfferReceived;
        _signalingClient.AnswerReceived -= OnAnswerReceived;
        _signalingClient.IceCandidateReceived -= OnIceCandidateReceived;
        await _lifecycleLock.WaitAsync();
        try
        {
            await ResetLockedAsync();
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }
}
