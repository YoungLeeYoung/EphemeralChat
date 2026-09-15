using System.Text.Encodings.Web;
using System.Text.Json;

namespace EphemeralChat.Network.P2P;

/// <summary>
/// Serializes the Milestone 4 text envelope. This stays in the Network layer so
/// UI code never owns the DataChannel wire format.
/// </summary>
public static class P2PTextProtocol
{
    public const string MessageType = "text";
    public const int MaxContentBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(P2PTextMessage message)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(message.Content) > MaxContentBytes)
        {
            throw new ArgumentException("text message content is too large", nameof(message));
        }

        return JsonSerializer.Serialize(
            new TextEnvelope(MessageType, message.MessageId, message.Content, message.SentAt),
            JsonOptions);
    }

    public static bool TryDeserialize(string payload, out P2PTextMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxContentBytes + 512)
        {
            return false;
        }

        try
        {
            TextEnvelope? envelope = JsonSerializer.Deserialize<TextEnvelope>(payload, JsonOptions);
            if (envelope?.Type != MessageType ||
                string.IsNullOrWhiteSpace(envelope.MessageId) ||
                envelope.Content is null ||
                envelope.SentAt == default)
            {
                return false;
            }

            if (System.Text.Encoding.UTF8.GetByteCount(envelope.Content) > MaxContentBytes)
            {
                return false;
            }

            message = new P2PTextMessage(envelope.MessageId, envelope.Content, envelope.SentAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record TextEnvelope(
        string Type,
        string MessageId,
        string Content,
        DateTimeOffset SentAt);
}
