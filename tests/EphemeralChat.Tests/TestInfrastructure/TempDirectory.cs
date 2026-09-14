namespace EphemeralChat.Tests.TestInfrastructure;

/// <summary>
/// A unique temp directory that is removed (best effort) on dispose.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string FullPath { get; }

    public TempDirectory()
    {
        FullPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ephemeralchat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
