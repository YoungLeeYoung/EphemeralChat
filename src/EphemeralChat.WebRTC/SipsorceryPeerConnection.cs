using System.Text.Json;
using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace EphemeralChat.WebRTC;

/// <summary>
/// SIPSorcery-backed peer connection. Only the WebRTC transport lives here;
/// signaling transport and application semantics are handled elsewhere.
/// </summary>
public sealed class SipsorceryPeerConnection : IWebRtcPeerConnection
{
    private readonly RTCPeerConnection _connection;
    private RTCDataChannel? _dataChannel;
    private static readonly JsonSerializerOptions DescriptionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action<string>? IceCandidateGenerated;
    public event Action<WebRtcConnectionState>? ConnectionStateChanged;
    public event Action? DataChannelOpened;
    public event Action<string>? MessageReceived;

    public async Task CreateDataChannelAsync(string label, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RTCDataChannel channel = await _connection.createDataChannel(label, new RTCDataChannelInit())
            ?? throw new InvalidOperationException("the WebRTC implementation did not create the data channel");

        channel.onopen += () => DataChannelOpened?.Invoke();
        channel.onmessage += (_, _, data) =>
            MessageReceived?.Invoke(System.Text.Encoding.UTF8.GetString(data));
        _dataChannel = channel;
    }

    public SipsorceryPeerConnection()
    {
        _connection = new RTCPeerConnection();
        _connection.onicecandidate += candidate =>
            IceCandidateGenerated?.Invoke(candidate.toJSON());
        _connection.onconnectionstatechange += state =>
            ConnectionStateChanged?.Invoke(MapState(state));
        _connection.ondatachannel += channel =>
        {
            // SIPSorcery raises ondatachannel only after the remote-created
            // channel has sent its DCEP ACK. Unlike locally created channels,
            // it does not raise the inner channel's onopen event.
            _dataChannel = channel;
            DataChannelOpened?.Invoke();
            channel.onmessage += (_, _, data) =>
                MessageReceived?.Invoke(System.Text.Encoding.UTF8.GetString(data));
        };
    }

    public async Task<string> CreateOfferAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RTCSessionDescriptionInit offer = _connection.createOffer();
        await _connection.setLocalDescription(offer);
        return offer.toJSON();
    }

    public async Task<string> AcceptOfferAsync(string offerSdpJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureDescriptionSucceeded(
            _connection.setRemoteDescription(ParseDescription(offerSdpJson, "offer")));
        RTCSessionDescriptionInit answer = _connection.createAnswer();
        await _connection.setLocalDescription(answer);
        return answer.toJSON();
    }

    public Task AcceptAnswerAsync(string answerSdpJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureDescriptionSucceeded(
            _connection.setRemoteDescription(ParseDescription(answerSdpJson, "answer")));
        return Task.CompletedTask;
    }

    public Task AddIceCandidateAsync(string candidateSdpJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RTCIceCandidateInit candidate = JsonSerializer.Deserialize<RTCIceCandidateInit>(
            candidateSdpJson) ?? throw new ArgumentException("ICE candidate JSON is empty", nameof(candidateSdpJson));
        _connection.addIceCandidate(candidate);
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RTCDataChannel? channel = _dataChannel;
        if (channel is null || channel.readyState != RTCDataChannelState.open)
        {
            throw new InvalidOperationException("the WebRTC data channel is not open");
        }

        channel.send(payload);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _connection.Close("normal");
        }
        catch (Exception)
        {
            // Closing an already-failed connection can throw; disposal must
            // still complete. No payload data is retained by this class.
        }

        return ValueTask.CompletedTask;
    }

    private static RTCSessionDescriptionInit ParseDescription(string sdpJson, string expectedType)
    {
        RTCSessionDescriptionInit? description = JsonSerializer.Deserialize<RTCSessionDescriptionInit>(
            sdpJson, DescriptionJsonOptions);
        if (description is null || string.IsNullOrWhiteSpace(description.sdp))
        {
            throw new ArgumentException("Session description JSON is empty", nameof(sdpJson));
        }

        if (!description.type.ToString().Equals(expectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Session description type '{description.type}' does not match expected '{expectedType}'",
                nameof(sdpJson));
        }

        return description;
    }

    private static void EnsureDescriptionSucceeded(SetDescriptionResultEnum result)
    {
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"WebRTC session description was rejected: {result}");
        }
    }

    private static WebRtcConnectionState MapState(RTCPeerConnectionState state) => state switch
    {
        RTCPeerConnectionState.@new => WebRtcConnectionState.New,
        RTCPeerConnectionState.connecting => WebRtcConnectionState.Connecting,
        RTCPeerConnectionState.connected => WebRtcConnectionState.Connected,
        RTCPeerConnectionState.disconnected => WebRtcConnectionState.Disconnected,
        RTCPeerConnectionState.failed => WebRtcConnectionState.Failed,
        RTCPeerConnectionState.closed => WebRtcConnectionState.Closed,
        _ => WebRtcConnectionState.Failed
    };
}
