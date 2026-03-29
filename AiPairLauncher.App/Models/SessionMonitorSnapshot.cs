namespace AiPairLauncher.App.Models;

public sealed class SessionMonitorSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public SessionHealthStatus HealthStatus { get; init; } = SessionHealthStatus.Idle;

    public string HealthDetail { get; init; } = "等待检测";

    public SessionDisconnectReason DisconnectReason { get; init; } = SessionDisconnectReason.None;

    public DateTimeOffset? DisconnectedAt { get; init; }

    public int CurrentBackoffSeconds { get; init; } = 8;

    public DateTimeOffset? NextHealthProbeAt { get; init; }

    public bool ZombieDetected { get; init; }

    public string RecoveryHint { get; init; } = "会话在线，无需恢复操作。";

    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.Now;

    public bool Changed { get; init; }
}
