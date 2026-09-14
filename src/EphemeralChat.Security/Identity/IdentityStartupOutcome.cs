namespace EphemeralChat.Security.Identity;

/// <summary>
/// The result of application startup identity handling: either a usable
/// identity (existing, first-launch, or rebuilt) or no identity plus a
/// human-readable reason. Failure to load must never crash the app.
/// </summary>
public sealed class IdentityStartupOutcome
{
    private IdentityStartupOutcome(LocalIdentity? identity, bool wasRebuilt, string? failureReason)
    {
        Identity = identity;
        WasRebuilt = wasRebuilt;
        FailureReason = failureReason;
    }

    public LocalIdentity? Identity { get; }

    public bool WasRebuilt { get; }

    public string? FailureReason { get; }

    public bool Succeeded => Identity is not null;

    public static IdentityStartupOutcome Loaded(LocalIdentity identity, bool wasRebuilt = false) =>
        new(identity ?? throw new ArgumentNullException(nameof(identity)), wasRebuilt, null);

    public static IdentityStartupOutcome NoIdentity(string failureReason) =>
        new(null, false, failureReason);
}
