using System.IO;
using System.Security.Cryptography;

namespace EphemeralChat.Security.Identity;

/// <summary>
/// UI-agnostic startup coordinator for the local identity.
///
/// Flow: load the stored identity; on first launch create and save one; if the
/// stored identity cannot be read (corruption, wrong Windows user, tampering),
/// ask the user through <see cref="RecoveryPrompt"/>. Cancel keeps the file
/// untouched and yields no identity; rebuild deletes the file and creates a
/// new identity. Peer ID replacement is never silent.
/// </summary>
public sealed class IdentityStartupService
{
    private readonly LocalIdentityService _identityService;

    public IdentityStartupService(LocalIdentityService identityService)
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
    }

    public delegate IdentityRecoveryChoice RecoveryPrompt(string failureDescription);

    public IdentityStartupOutcome LoadOrRecover(RecoveryPrompt promptForRecovery)
    {
        ArgumentNullException.ThrowIfNull(promptForRecovery);

        try
        {
            return LoadInternal();
        }
        catch (Exception ex) when (IsIdentityFileFailure(ex))
        {
            return RecoverWithPrompt(promptForRecovery, ex);
        }
    }

    private IdentityStartupOutcome LoadInternal()
    {
        LocalIdentity? existing = _identityService.Load();
        return existing is not null
            ? IdentityStartupOutcome.Loaded(existing)
            : IdentityStartupOutcome.Loaded(CreateAndSaveIdentity());
    }

    private IdentityStartupOutcome RecoverWithPrompt(RecoveryPrompt promptForRecovery, Exception cause)
    {
        IdentityRecoveryChoice choice = promptForRecovery(
            "The local identity could not be read (" + cause.Message + ").\n\n" +
            "Rebuild: deletes the unreadable identity file and creates a NEW identity " +
            "with a NEW Peer ID. The old identity cannot be recovered.\n" +
            "Cancel: keeps the file untouched and starts without an identity.");

        if (choice == IdentityRecoveryChoice.Cancel)
        {
            return IdentityStartupOutcome.NoIdentity(
                "The unreadable identity file was kept; no identity is loaded.");
        }

        try
        {
            _identityService.Delete();
            return IdentityStartupOutcome.Loaded(CreateAndSaveIdentity(), wasRebuilt: true);
        }
        catch (Exception ex) when (IsIdentityFileFailure(ex))
        {
            return IdentityStartupOutcome.NoIdentity("Rebuilding the identity failed: " + ex.Message);
        }
    }

    private LocalIdentity CreateAndSaveIdentity()
    {
        LocalIdentity identity = _identityService.CreateNew();
        _identityService.Save(identity);
        return identity;
    }

    private static bool IsIdentityFileFailure(Exception ex) =>
        ex is CryptographicException or InvalidDataException or IOException;
}
