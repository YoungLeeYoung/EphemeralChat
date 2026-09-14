namespace EphemeralChat.Core.Models;

/// <summary>
/// A single chat message held in memory for the duration of a session only.
/// Milestone 0 uses it to verify the MVVM data flow; real transport arrives later.
/// </summary>
public sealed record ChatMessage(
    string Sender,
    string Content,
    DateTimeOffset SentAt);
