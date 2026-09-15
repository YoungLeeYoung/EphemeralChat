using System.Text.Json;
using System.Text.Json.Serialization;

namespace EphemeralChat.Core.Signaling;

public static class SignalingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
