namespace AiPairLauncher.App.Models;

public sealed class SessionMonitorSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public SessionHealthStatus HealthStatus { get; init; } = SessionHealthStatus.Idle;

    public string HealthDetail { get; init; } = "等待检测";

    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.Now;

    public bool Changed { get; init; }
}
