namespace AiPairLauncher.App.Models;

public sealed class WorktreeLaunchContext
{
    public string WorkingDirectory { get; init; } = string.Empty;

    public bool UsedWorktree { get; init; }

    public string WorktreeStrategy { get; init; } = "none";

    public string? BranchName { get; init; }

    public string Summary { get; init; } = string.Empty;
}
