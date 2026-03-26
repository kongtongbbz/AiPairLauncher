using System.IO;
using System.Text.Json.Serialization;

namespace AiPairLauncher.App.Models;

public sealed class ManagedSessionRecord
{
    public required LauncherSession Session { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? LaunchProfileId { get; set; }

    public string GroupName { get; set; } = "默认";

    public bool IsPinned { get; set; }

    public bool IsArchived { get; set; }

    public SessionRuntimeBinding RuntimeBinding { get; set; } = new();

    public SessionStatusSnapshot StatusSnapshot { get; set; } = SessionStatusSnapshot.CreateDefault();

    [JsonIgnore]
    public string SessionId => Session.SessionId;

    [JsonIgnore]
    public SessionHealthStatus HealthStatus
    {
        get => StatusSnapshot.Status;
        set => StatusSnapshot.Status = value;
    }

    [JsonIgnore]
    public string HealthDetail
    {
        get => StatusSnapshot.StatusDetail;
        set => StatusSnapshot.StatusDetail = value;
    }

    [JsonIgnore]
    public string LastSummary
    {
        get => StatusSnapshot.LastSummary;
        set => StatusSnapshot.LastSummary = value;
    }

    [JsonIgnore]
    public string? LastError
    {
        get => StatusSnapshot.LastError;
        set => StatusSnapshot.LastError = value;
    }

    [JsonIgnore]
    public DateTimeOffset LastSeenAt
    {
        get => StatusSnapshot.LastActivityAt;
        set => StatusSnapshot.LastActivityAt = value;
    }

    [JsonIgnore]
    public DateTimeOffset UpdatedAt
    {
        get => StatusSnapshot.UpdatedAt;
        set => StatusSnapshot.UpdatedAt = value;
    }

    [JsonIgnore]
    public string HealthDisplayText => HealthStatus switch
    {
        SessionHealthStatus.Running => "运行中",
        SessionHealthStatus.Waiting => "等待中",
        SessionHealthStatus.Idle => "空闲",
        SessionHealthStatus.Error => "错误",
        SessionHealthStatus.Detached => "已断开",
        _ => "空闲",
    };

    [JsonIgnore]
    public string LastSeenDisplay => LastSeenAt == default
        ? "暂无"
        : LastSeenAt.LocalDateTime.ToString("MM-dd HH:mm");

    [JsonIgnore]
    public string WorkingDirectoryName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Session.WorkingDirectory))
            {
                return "未命名目录";
            }

            var normalizedPath = Session.WorkingDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Path.GetFileName(normalizedPath);
        }
    }

    [JsonIgnore]
    public string ModeSummary => $"Claude {Session.ClaudePermissionMode} / Codex {Session.CodexMode}";

    [JsonIgnore]
    public bool UsesWorktree =>
        Session.WorkingDirectory.Contains(
            $"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || Session.WorkingDirectory.Contains(
            $"{Path.AltDirectorySeparatorChar}.worktrees{Path.AltDirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string WorktreeDisplayText => UsesWorktree ? "worktree" : "主目录";

    [JsonIgnore]
    public string BadgeSummary
    {
        get
        {
            var badges = new List<string>();
            if (IsPinned)
            {
                badges.Add("置顶");
            }

            if (!string.IsNullOrWhiteSpace(LaunchProfileId))
            {
                badges.Add("模板");
            }

            if (Session.AutomationEnabledAtLaunch)
            {
                badges.Add("自动");
            }

            if (UsesWorktree)
            {
                badges.Add("worktree");
            }

            return badges.Count == 0 ? "标准" : string.Join(" · ", badges);
        }
    }

    [JsonIgnore]
    public int PendingActionCount
    {
        get
        {
            var count = 0;
            if (StatusSnapshot.NeedsApproval)
            {
                count += 1;
            }

            if (HealthStatus is SessionHealthStatus.Waiting or SessionHealthStatus.Error or SessionHealthStatus.Detached)
            {
                count += 1;
            }

            return count;
        }
    }

    [JsonIgnore]
    public string PreviewSnippet
    {
        get
        {
            var claude = string.IsNullOrWhiteSpace(StatusSnapshot.ClaudePreview) ? "暂无输出" : StatusSnapshot.ClaudePreview.Trim();
            var codex = string.IsNullOrWhiteSpace(StatusSnapshot.CodexPreview) ? "暂无输出" : StatusSnapshot.CodexPreview.Trim();
            return $"Claude: {claude}{Environment.NewLine}Codex: {codex}";
        }
    }

    [JsonIgnore]
    public string ArchiveActionLabel => IsArchived ? "恢复" : "归档";

    public ManagedSessionRecord Clone()
    {
        return new ManagedSessionRecord
        {
            Session = Session.Clone(),
            DisplayName = DisplayName,
            LaunchProfileId = LaunchProfileId,
            GroupName = GroupName,
            IsPinned = IsPinned,
            IsArchived = IsArchived,
            RuntimeBinding = RuntimeBinding.Clone(),
            StatusSnapshot = StatusSnapshot.Clone(),
        };
    }
}
