using System.IO;
using System.Text.RegularExpressions;
using EphemeralChat.Core.Signaling;

namespace EphemeralChat.App.Startup;

/// <summary>
/// Resolves an optional local-development profile from the command line.
/// With no profile, the production identity path is unchanged. With a profile,
/// identity files are isolated under <c>EphemeralChat\Profiles\&lt;name&gt;</c>.
/// This is only local instance isolation; it is not an account system.
/// </summary>
public sealed partial class ClientProfile
{
    private const string ProfileSwitch = "--profile";
    private const string ServerSwitch = "--server";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ProfileNamePattern();

    public ClientProfile(string? name, string localAppDataDirectory, Uri? serverUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        Name = name;
        ServerUri = serverUri;
        IdentityDirectory = name is null
            ? Path.Combine(localAppDataDirectory, "EphemeralChat")
            : Path.Combine(localAppDataDirectory, "EphemeralChat", "Profiles", name);
    }

    public string? Name { get; }

    public Uri? ServerUri { get; }

    public string IdentityDirectory { get; }

    public string DisplayName => Name ?? "Default";

    public static ClientProfile ResolveStartup(string[] args) =>
        Resolve(args, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static ClientProfile Resolve(string[] args, string localAppDataDirectory)
    {
        string? profileName = ParseProfileName(args);
        Uri? serverUri = ParseServerUri(args);
        return new ClientProfile(profileName, localAppDataDirectory, serverUri);
    }

    private static string? ParseProfileName(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            bool isSeparatedSwitch = argument.Equals(ProfileSwitch, StringComparison.OrdinalIgnoreCase);
            bool isInlineSwitch = argument.StartsWith(ProfileSwitch + "=", StringComparison.OrdinalIgnoreCase);

            if (!isSeparatedSwitch && !isInlineSwitch)
            {
                continue;
            }

            string? value;
            if (isInlineSwitch)
            {
                value = argument[(ProfileSwitch.Length + 1)..];
            }
            else
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("The --profile switch requires a profile name.");
                }

                value = args[index + 1];
            }

            if (string.IsNullOrWhiteSpace(value) || !ProfileNamePattern().IsMatch(value))
            {
                throw new ArgumentException(
                    "Profile names may contain 1-64 letters, digits, underscores, or hyphens.");
            }

            return value;
        }

        return null;
    }

    private static Uri? ParseServerUri(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            bool isSeparatedSwitch = argument.Equals(ServerSwitch, StringComparison.OrdinalIgnoreCase);
            bool isInlineSwitch = argument.StartsWith(ServerSwitch + "=", StringComparison.OrdinalIgnoreCase);

            if (!isSeparatedSwitch && !isInlineSwitch)
            {
                continue;
            }

            string? value;
            if (isInlineSwitch)
            {
                value = argument[(ServerSwitch.Length + 1)..];
            }
            else
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("The --server switch requires a signaling URI.");
                }

                value = args[index + 1];
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("The --server switch requires a signaling URI.");
            }

            return NormalizeServerUri(value.Trim());
        }

        return null;
    }

    private static Uri NormalizeServerUri(string value)
    {
        string candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : "ws://" + value;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            throw new ArgumentException("The --server value must be a ws:// or wss:// URI.");
        }

        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            var builder = new UriBuilder(uri) { Path = SignalingProtocol.WebSocketPath };
            uri = builder.Uri;
        }

        return uri;
    }
}
