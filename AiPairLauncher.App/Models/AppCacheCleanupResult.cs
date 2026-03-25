namespace AiPairLauncher.App.Models;

public sealed class AppCacheCleanupResult
{
    public string SessionFilePath { get; init; } = string.Empty;

    public string AutomationPromptDirectory { get; init; } = string.Empty;

    public bool SessionFileDeleted { get; init; }

    public int DeletedPromptFileCount { get; init; }

    public bool PromptDirectoryDeleted { get; init; }

    public string Summary
    {
        get
        {
            var sessionText = SessionFileDeleted ? "已清理最近会话记录" : "最近会话记录本来就是空的";
            var promptText = DeletedPromptFileCount > 0
                ? $"已清理 {DeletedPromptFileCount} 个自动提示临时文件"
                : "自动提示临时文件本来就是空的";

            return $"{sessionText}；{promptText}。";
        }
    }
}
