using System.IO;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AppCacheServiceTests : IDisposable
{
    private readonly string _rootPath;

    public AppCacheServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact(DisplayName = "test_clear_app_cache_removes_session_and_prompt_files")]
    public async Task ClearAppCacheRemovesSessionAndPromptFilesAsync()
    {
        var sessionFilePath = Path.Combine(_rootPath, "session.json");
        var promptDirectory = Path.Combine(_rootPath, "automation-prompts");
        Directory.CreateDirectory(promptDirectory);
        await File.WriteAllTextAsync(sessionFilePath, "{}");
        await File.WriteAllTextAsync(Path.Combine(promptDirectory, "claude-1.txt"), "abc");
        await File.WriteAllTextAsync(Path.Combine(promptDirectory, "codex-1.txt"), "xyz");

        var service = new AppCacheService(sessionFilePath, promptDirectory);

        var result = await service.ClearAsync();

        Assert.True(result.SessionFileDeleted);
        Assert.Equal(2, result.DeletedPromptFileCount);
        Assert.True(result.PromptDirectoryDeleted);
        Assert.False(File.Exists(sessionFilePath));
        Assert.False(Directory.Exists(promptDirectory));
    }

    [Fact(DisplayName = "test_clear_app_cache_is_idempotent_when_cache_missing")]
    public async Task ClearAppCacheIsIdempotentWhenCacheMissingAsync()
    {
        var sessionFilePath = Path.Combine(_rootPath, "session.json");
        var promptDirectory = Path.Combine(_rootPath, "automation-prompts");
        var service = new AppCacheService(sessionFilePath, promptDirectory);

        var result = await service.ClearAsync();

        Assert.False(result.SessionFileDeleted);
        Assert.Equal(0, result.DeletedPromptFileCount);
        Assert.False(result.PromptDirectoryDeleted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
