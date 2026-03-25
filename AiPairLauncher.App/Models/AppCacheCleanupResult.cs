namespace AiPairLauncher.App.Models;

public sealed class AppCacheCleanupResult
{
    public string LegacySessionFilePath { get; init; } = string.Empty;

    public string StateDatabasePath { get; init; } = string.Empty;

    public string AutomationPromptDirectory { get; init; } = string.Empty;

    public bool LegacySessionFileDeleted { get; init; }

    public bool StateDatabaseDeleted { get; init; }

    public int DeletedPromptFileCount { get; init; }

    public bool PromptDirectoryDeleted { get; init; }

    public string Summary
    {
        get
        {
            var sessionText = LegacySessionFileDeleted || StateDatabaseDeleted
                ? "已清理会话状态缓存"
                : "会话缓存本来就是空的";
            var promptText = DeletedPromptFileCount > 0
                ? $"已清理 {DeletedPromptFileCount} 个自动提示临时文件"
                : "自动提示临时文件本来就是空的";

            return $"{sessionText}；{promptText}。";
        }
    }
}
