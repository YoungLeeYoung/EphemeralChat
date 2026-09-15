namespace EphemeralChat.Tests.TestInfrastructure;

/// <summary>A TimeProvider whose "now" tests can advance manually.</summary>
internal sealed class MutableClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
