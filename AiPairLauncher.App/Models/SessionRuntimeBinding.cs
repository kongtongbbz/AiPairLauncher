namespace AiPairLauncher.App.Models;

public sealed class SessionRuntimeBinding
{
    public string SessionId { get; set; } = string.Empty;

    public int GuiPid { get; set; }

    public string SocketPath { get; set; } = string.Empty;

    public int LeftPaneId { get; set; }

    public int RightPaneId { get; set; }

    public int? ClaudeObserverPaneId { get; set; }

    public int? CodexObserverPaneId { get; set; }

    public bool IsAlive { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public SessionRuntimeBinding Clone()
    {
        return new SessionRuntimeBinding
        {
            SessionId = SessionId,
            GuiPid = GuiPid,
            SocketPath = SocketPath,
            LeftPaneId = LeftPaneId,
            RightPaneId = RightPaneId,
            ClaudeObserverPaneId = ClaudeObserverPaneId,
            CodexObserverPaneId = CodexObserverPaneId,
            IsAlive = IsAlive,
            UpdatedAt = UpdatedAt,
        };
    }
}
