using System.IO;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AppCacheService : IAppCacheService
{
    private readonly string _legacySessionFilePath;
    private readonly string _stateDatabasePath;
    private readonly string _automationPromptDirectory;

    public AppCacheService(ISessionStore sessionStore)
        : this(
            sessionStore?.LegacySessionFilePath ?? throw new ArgumentNullException(nameof(sessionStore)),
            sessionStore.DatabasePath,
            Path.Combine(Path.GetTempPath(), "AiPairLauncher", "automation-prompts"))
    {
    }

    public AppCacheService(string legacySessionFilePath, string stateDatabasePath, string automationPromptDirectory)
    {
        _legacySessionFilePath = legacySessionFilePath ?? throw new ArgumentNullException(nameof(legacySessionFilePath));
        _stateDatabasePath = stateDatabasePath ?? throw new ArgumentNullException(nameof(stateDatabasePath));
        _automationPromptDirectory = automationPromptDirectory ?? throw new ArgumentNullException(nameof(automationPromptDirectory));
    }

    public Task<AppCacheCleanupResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var legacySessionDeleted = false;
        if (File.Exists(_legacySessionFilePath))
        {
            File.Delete(_legacySessionFilePath);
            legacySessionDeleted = true;
        }

        var stateDatabaseDeleted = false;
        if (File.Exists(_stateDatabasePath))
        {
            File.Delete(_stateDatabasePath);
            stateDatabaseDeleted = true;
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
            LegacySessionFilePath = _legacySessionFilePath,
            StateDatabasePath = _stateDatabasePath,
            AutomationPromptDirectory = _automationPromptDirectory,
            LegacySessionFileDeleted = legacySessionDeleted,
            StateDatabaseDeleted = stateDatabaseDeleted,
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
