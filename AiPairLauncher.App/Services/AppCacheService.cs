using System.IO;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AppCacheService : IAppCacheService
{
    private readonly string _sessionFilePath;
    private readonly string _automationPromptDirectory;

    public AppCacheService(ISessionStore sessionStore)
        : this(
            sessionStore?.SessionFilePath ?? throw new ArgumentNullException(nameof(sessionStore)),
            Path.Combine(Path.GetTempPath(), "AiPairLauncher", "automation-prompts"))
    {
    }

    public AppCacheService(string sessionFilePath, string automationPromptDirectory)
    {
        _sessionFilePath = sessionFilePath ?? throw new ArgumentNullException(nameof(sessionFilePath));
        _automationPromptDirectory = automationPromptDirectory ?? throw new ArgumentNullException(nameof(automationPromptDirectory));
    }

    public Task<AppCacheCleanupResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sessionDeleted = false;
        if (File.Exists(_sessionFilePath))
        {
            File.Delete(_sessionFilePath);
            sessionDeleted = true;
        }

        var deletedPromptFiles = 0;
        var promptDirectoryDeleted = false;
        if (Directory.Exists(_automationPromptDirectory))
        {
            deletedPromptFiles = Directory
                .EnumerateFiles(_automationPromptDirectory, "*", SearchOption.AllDirectories)
                .Count();

            Directory.Delete(_automationPromptDirectory, recursive: true);
            promptDirectoryDeleted = true;
            TryDeleteEmptyParentDirectory(Path.GetDirectoryName(_automationPromptDirectory));
        }

        return Task.FromResult(new AppCacheCleanupResult
        {
            SessionFilePath = _sessionFilePath,
            AutomationPromptDirectory = _automationPromptDirectory,
            SessionFileDeleted = sessionDeleted,
            DeletedPromptFileCount = deletedPromptFiles,
            PromptDirectoryDeleted = promptDirectoryDeleted,
        });
    }

    private static void TryDeleteEmptyParentDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            return;
        }

        Directory.Delete(directoryPath, recursive: false);
    }
}
