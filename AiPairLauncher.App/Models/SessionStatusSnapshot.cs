namespace AiPairLauncher.App.Models;

public sealed class SessionStatusSnapshot
{
    public string SessionId { get; set; } = string.Empty;

    public SessionHealthStatus Status { get; set; } = SessionHealthStatus.Idle;

    public string StatusDetail { get; set; } = "等待检测";

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.Now;

    public string? LastError { get; set; }

    public string LastSummary { get; set; } = "暂无";

    public bool NeedsApproval { get; set; }

    public int? AutomationStageId { get; set; }

    public int AutomationRetryCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public SessionStatusSnapshot Clone()
    {
        return new SessionStatusSnapshot
        {
            SessionId = SessionId,
            Status = Status,
            StatusDetail = StatusDetail,
            LastActivityAt = LastActivityAt,
            LastError = LastError,
            LastSummary = LastSummary,
            NeedsApproval = NeedsApproval,
            AutomationStageId = AutomationStageId,
            AutomationRetryCount = AutomationRetryCount,
            UpdatedAt = UpdatedAt,
        };
    }

    public static SessionStatusSnapshot CreateDefault()
    {
        return new SessionStatusSnapshot();
    }
}
