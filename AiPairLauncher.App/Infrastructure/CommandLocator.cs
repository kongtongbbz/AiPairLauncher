using System.IO;
using System.Runtime.InteropServices;

namespace AiPairLauncher.App.Infrastructure;

public sealed class CommandLocator
{
    private static readonly string[] DefaultExtensions = [".exe", ".cmd", ".bat", ".ps1"];

    public string? Resolve(string commandName, IEnumerable<string>? preferredPaths = null)
    {
        if (Path.IsPathFullyQualified(commandName) && File.Exists(commandName))
        {
            return commandName;
        }

        if (preferredPaths is not null)
        {
            foreach (var candidate in preferredPaths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var expandedPath = Environment.ExpandEnvironmentVariables(candidate);
                if (File.Exists(expandedPath))
                {
                    return expandedPath;
                }
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathParts = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = BuildExtensions();

        foreach (var pathPart in pathParts)
        {
            foreach (var ext in extensions)
            {
                var fileName = commandName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? commandName
                    : commandName + ext;
                var fullPath = Path.Combine(pathPart, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildExtensions()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [string.Empty];
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return DefaultExtensions;
        }

        var parsed = pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static ext => !string.IsNullOrWhiteSpace(ext))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? DefaultExtensions : parsed;
    }
}
