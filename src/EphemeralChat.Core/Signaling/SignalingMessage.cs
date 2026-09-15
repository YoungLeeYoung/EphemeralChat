using System.Text.Json;

namespace EphemeralChat.Core.Signaling;

/// <summary>
/// The single JSON envelope used on the signaling plane (both directions).
/// <see cref="From"/> is filled by the server when forwarding; clients only
/// set it on <c>register</c>. <see cref="Payload"/> is a type-specific JSON
/// object and is treated as opaque by the server.
/// </summary>
public sealed class SignalingMessage
{
    public string Type { get; set; } = string.Empty;

    public string? From { get; set; }

    public string? To { get; set; }

    public JsonElement? Payload { get; set; }
}
