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
    private int _localHostCandidates;
    private int _localSrflxCandidates;
    private int _localRelayCandidates;
    private int _remoteHostCandidates;
    private int _remoteSrflxCandidates;
    private int _remoteRelayCandidates;
    private CancellationTokenSource? _iceWatchdogCts;

    private static readonly TimeSpan IceWatchdogDelay = TimeSpan.FromSeconds(10);

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

    public event Action<string, P2PTextMessage>? TextMessageReceived;

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
            ResetIceCounters();
            SetState(P2PConnectionState.Connecting);

            await connection.CreateDataChannelAsync(DefaultDataChannelLabel, ct);
            string offerSdp = await connection.CreateOfferAsync(ct);
            await _signalingClient.SendOfferAsync(
                remotePeerId, ParsePayload(offerSdp, "offer"), ct);
            StartIceWatchdog();
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
            ResetIceCounters();
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

    public async Task<P2PTextMessage> SendTextAsync(string content, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IWebRtcPeerConnection? connection = _connection;
        if (connection is null || _remotePeerId is null || _state != P2PConnectionState.Connected)
        {
            throw new InvalidOperationException("a connected P2P session is required");
        }

        var message = new P2PTextMessage(
            Guid.NewGuid().ToString("N"),
            content,
            DateTimeOffset.UtcNow);
        await connection.SendTextAsync(P2PTextProtocol.Serialize(message), ct);
        return message;
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
            StartIceWatchdog();
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
            CountRemoteCandidate(candidateJson);
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

    private void OnMessageReceived(string payload)
    {
        string? remotePeerId = _remotePeerId;
        if (_isDisposed || remotePeerId is null)
        {
            return;
        }

        if (!P2PTextProtocol.TryDeserialize(payload, out P2PTextMessage? message) || message is null)
        {
            OperationFailed?.Invoke("P2P text message was malformed");
            return;
        }

        TextMessageReceived?.Invoke(remotePeerId, message);
    }

    private void AttachConnection(IWebRtcPeerConnection connection)
    {
        connection.ConnectionStateChanged += OnPeerConnectionStateChanged;
        connection.DataChannelOpened += OnDataChannelOpened;
        connection.MessageReceived += OnMessageReceived;
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
            CountLocalCandidate(candidateJson);
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

        if (state is WebRtcConnectionState.Failed or WebRtcConnectionState.Disconnected)
        {
            CancelIceWatchdog();
            OperationFailed?.Invoke($"connection {state} — {BuildIceSummary()}");
        }
    }

    private void OnDataChannelOpened()
    {
        CancelIceWatchdog();
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
        CancelIceWatchdog();
        IWebRtcPeerConnection? connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            connection.ConnectionStateChanged -= OnPeerConnectionStateChanged;
            connection.DataChannelOpened -= OnDataChannelOpened;
            connection.MessageReceived -= OnMessageReceived;
            connection.IceCandidateGenerated -= OnLocalIceCandidateGenerated;
            await connection.DisposeAsync();
        }

        _remotePeerId = null;
        _isInitiator = false;
        _hasRemoteDescription = false;
        _pendingRemoteCandidates.Clear();
        ResetIceCounters();
    }

    private void ResetIceCounters()
    {
        Interlocked.Exchange(ref _localHostCandidates, 0);
        Interlocked.Exchange(ref _localSrflxCandidates, 0);
        Interlocked.Exchange(ref _localRelayCandidates, 0);
        Interlocked.Exchange(ref _remoteHostCandidates, 0);
        Interlocked.Exchange(ref _remoteSrflxCandidates, 0);
        Interlocked.Exchange(ref _remoteRelayCandidates, 0);
    }

    private void CountLocalCandidate(string candidateJson)
    {
        switch (ExtractCandidateType(candidateJson))
        {
            case "host": Interlocked.Increment(ref _localHostCandidates); break;
            case "srflx": Interlocked.Increment(ref _localSrflxCandidates); break;
            case "relay": Interlocked.Increment(ref _localRelayCandidates); break;
        }
    }

    private void CountRemoteCandidate(string candidateJson)
    {
        switch (ExtractCandidateType(candidateJson))
        {
            case "host": Interlocked.Increment(ref _remoteHostCandidates); break;
            case "srflx": Interlocked.Increment(ref _remoteSrflxCandidates); break;
            case "relay": Interlocked.Increment(ref _remoteRelayCandidates); break;
        }
    }

    private static string ExtractCandidateType(string candidateJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(candidateJson);
            string candidate = document.RootElement.ValueKind == JsonValueKind.Object &&
                               document.RootElement.TryGetProperty("candidate", out JsonElement value) &&
                               value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

            if (candidate.Contains("typ host", StringComparison.Ordinal)) return "host";
            if (candidate.Contains("typ srflx", StringComparison.Ordinal)) return "srflx";
            if (candidate.Contains("typ prflx", StringComparison.Ordinal)) return "prflx";
            if (candidate.Contains("typ relay", StringComparison.Ordinal)) return "relay";
            return "unknown";
        }
        catch (JsonException)
        {
            return "unknown";
        }
    }

    private string BuildIceSummary() =>
        $"local ICE: {Interlocked.CompareExchange(ref _localHostCandidates, 0, 0)} host/" +
        $"{Interlocked.CompareExchange(ref _localSrflxCandidates, 0, 0)} srflx/" +
        $"{Interlocked.CompareExchange(ref _localRelayCandidates, 0, 0)} relay, " +
        $"remote ICE: {Interlocked.CompareExchange(ref _remoteHostCandidates, 0, 0)} host/" +
        $"{Interlocked.CompareExchange(ref _remoteSrflxCandidates, 0, 0)} srflx/" +
        $"{Interlocked.CompareExchange(ref _remoteRelayCandidates, 0, 0)} relay";

    private void StartIceWatchdog()
    {
        CancelIceWatchdog();
        var cts = new CancellationTokenSource();
        _iceWatchdogCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(IceWatchdogDelay, cts.Token);
                if (_state is P2PConnectionState.Connecting or P2PConnectionState.Idle)
                {
                    int localSrflx = Interlocked.CompareExchange(ref _localSrflxCandidates, 0, 0);
                    if (localSrflx == 0)
                    {
                        OperationFailed?.Invoke(
                            "no public (srflx) ICE candidates gathered — STUN servers unreachable or blocked");
                    }
                    else
                    {
                        OperationFailed?.Invoke(
                            $"srflx candidates gathered but connection stalled — {BuildIceSummary()}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void CancelIceWatchdog()
    {
        Interlocked.Exchange(ref _iceWatchdogCts, null)?.Cancel();
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
