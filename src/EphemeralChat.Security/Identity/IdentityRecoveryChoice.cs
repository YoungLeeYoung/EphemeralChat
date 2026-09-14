namespace EphemeralChat.Security.Identity;

/// <summary>
/// The user's decision when the stored identity cannot be loaded.
/// </summary>
public enum IdentityRecoveryChoice
{
    /// <summary>Keep the existing (unreadable) identity file untouched.</summary>
    Cancel,

    /// <summary>Delete the unreadable file and create a brand-new identity.</summary>
    Rebuild
}
