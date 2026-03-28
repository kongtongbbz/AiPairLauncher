using System.IO;

namespace AiPairLauncher.App.Services;

internal static class TaskMdPathResolver
{
    private const string LegacyTaskMdFileName = "task.md";

    public static string BuildDefault(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return Path.Combine(workingDirectory, ".aipair", LegacyTaskMdFileName);
    }

    public static string BuildLegacy(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return Path.Combine(workingDirectory, LegacyTaskMdFileName);
    }

    public static string ResolveExistingOrDefault(string workingDirectory)
    {
        var defaultPath = BuildDefault(workingDirectory);
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        var legacyPath = BuildLegacy(workingDirectory);
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        return defaultPath;
    }
}
