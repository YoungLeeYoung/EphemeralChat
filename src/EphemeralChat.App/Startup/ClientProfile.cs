using System.IO;
using System.Text.RegularExpressions;

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

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ProfileNamePattern();

    public ClientProfile(string? name, string localAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        Name = name;
        IdentityDirectory = name is null
            ? Path.Combine(localAppDataDirectory, "EphemeralChat")
            : Path.Combine(localAppDataDirectory, "EphemeralChat", "Profiles", name);
    }

    public string? Name { get; }

    public string IdentityDirectory { get; }

    public string DisplayName => Name ?? "Default";

    public static ClientProfile ResolveStartup(string[] args) =>
        Resolve(args, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static ClientProfile Resolve(string[] args, string localAppDataDirectory)
    {
        string? profileName = ParseProfileName(args);
        return new ClientProfile(profileName, localAppDataDirectory);
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
}
