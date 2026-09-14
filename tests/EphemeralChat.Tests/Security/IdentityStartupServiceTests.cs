using System.IO;
using EphemeralChat.Security.Identity;
using EphemeralChat.Security.Identity.Persistence;
using EphemeralChat.Tests.TestInfrastructure;

namespace EphemeralChat.Tests.Security;

public class IdentityStartupServiceTests
{
    private static LocalIdentityService CreateService(TempDirectory temp) =>
        new(new DpapiFileIdentityStore(temp.FullPath, new DpapiIdentityKeyProtector()));

    private static string IdentityFilePath(TempDirectory temp) =>
        Path.Combine(temp.FullPath, "identity.dat");

    private static void CorruptIdentityFile(TempDirectory temp) =>
        File.WriteAllBytes(IdentityFilePath(temp), [0x12, 0x34, 0x56, 0x78]);

    private static IdentityStartupService.RecoveryPrompt FailingPrompt() =>
        _ => throw new InvalidOperationException("Recovery prompt must not be shown");

    [Fact]
    public void LoadOrRecover_OnFirstLaunch_CreatesIdentityWithoutPrompting()
    {
        using var temp = new TempDirectory();
        var startup = new IdentityStartupService(CreateService(temp));

        var outcome = startup.LoadOrRecover(FailingPrompt());

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.WasRebuilt);
        Assert.NotNull(outcome.Identity);
        Assert.True(File.Exists(IdentityFilePath(temp)));
    }

    [Fact]
    public void LoadOrRecover_WithHealthyIdentity_LoadsWithoutPrompting()
    {
        using var temp = new TempDirectory();
        string originalPeerId;
        {
            var service = CreateService(temp);
        using (var identity = service.LoadOrCreate())
        {
            originalPeerId = identity.PeerId;
        }
        }

        var startup = new IdentityStartupService(CreateService(temp));
        var outcome = startup.LoadOrRecover(FailingPrompt());

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.WasRebuilt);
        Assert.Equal(originalPeerId, outcome.Identity!.PeerId);
    }

    [Fact]
    public void LoadOrRecover_WithCorruptIdentity_PromptsInsteadOfThrowing()
    {
        using var temp = new TempDirectory();
        CorruptIdentityFile(temp);
        var startup = new IdentityStartupService(CreateService(temp));
        var promptCalled = false;
        string? shownDescription = null;

        var outcome = startup.LoadOrRecover(description =>
        {
            promptCalled = true;
            shownDescription = description;
            return IdentityRecoveryChoice.Cancel;
        });

        Assert.True(promptCalled);
        Assert.False(string.IsNullOrWhiteSpace(shownDescription));
        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Identity);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public void LoadOrRecover_Cancel_KeepsTheIdentityFileUnchanged()
    {
        using var temp = new TempDirectory();
        CorruptIdentityFile(temp);
        byte[] before = File.ReadAllBytes(IdentityFilePath(temp));
        var startup = new IdentityStartupService(CreateService(temp));

        var outcome = startup.LoadOrRecover(_ => IdentityRecoveryChoice.Cancel);

        Assert.False(outcome.Succeeded);
        byte[] after = File.ReadAllBytes(IdentityFilePath(temp));
        Assert.Equal(before, after);
    }

    [Fact]
    public void LoadOrRecover_Rebuild_DeletesCorruptFileAndCreatesNewIdentity()
    {
        using var temp = new TempDirectory();
        CorruptIdentityFile(temp);
        byte[] corruptBytes = File.ReadAllBytes(IdentityFilePath(temp));
        var startup = new IdentityStartupService(CreateService(temp));

        var outcome = startup.LoadOrRecover(_ => IdentityRecoveryChoice.Rebuild);

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.WasRebuilt);
        Assert.NotNull(outcome.Identity);
        Assert.NotEqual(corruptBytes, File.ReadAllBytes(IdentityFilePath(temp)));
    }

    [Fact]
    public void LoadOrRecover_AfterRebuild_PeerIdIsStableAcrossRestart()
    {
        using var temp = new TempDirectory();
        CorruptIdentityFile(temp);
        var firstStartup = new IdentityStartupService(CreateService(temp));
        var rebuilt = firstStartup.LoadOrRecover(_ => IdentityRecoveryChoice.Rebuild);

        var restartedStartup = new IdentityStartupService(CreateService(temp));
        var restarted = restartedStartup.LoadOrRecover(FailingPrompt());

        Assert.True(rebuilt.Succeeded);
        Assert.True(restarted.Succeeded);
        Assert.False(restarted.WasRebuilt);
        Assert.Equal(rebuilt.Identity!.PeerId, restarted.Identity!.PeerId);
    }

    [Fact]
    public void LoadOrRecover_WithFormatCorruptedIdentity_StillRecovers()
    {
        using var temp = new TempDirectory();
        // A valid DPAPI envelope whose payload is not a valid identity file:
        // exercises the format-validation (InvalidDataException) path.
        var protector = new DpapiIdentityKeyProtector();
        byte[] badPayload = System.Text.Encoding.ASCII.GetBytes("not an identity file");
        File.WriteAllBytes(IdentityFilePath(temp), protector.Protect(badPayload));
        var startup = new IdentityStartupService(CreateService(temp));
        var promptCalled = false;

        var outcome = startup.LoadOrRecover(_ =>
        {
            promptCalled = true;
            return IdentityRecoveryChoice.Rebuild;
        });

        Assert.True(promptCalled);
        Assert.True(outcome.Succeeded);
        Assert.True(outcome.WasRebuilt);
    }
}
