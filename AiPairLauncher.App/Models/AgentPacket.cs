namespace AiPairLauncher.App.Models;

public sealed class AgentPacket
{
    public AgentRole Role { get; init; }

    public PacketKind Kind { get; init; }

    public int StageId { get; init; }

    public string Fingerprint { get; init; } = string.Empty;

    public string RawText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public string? Status { get; init; }

    public ReviewDecision? Decision { get; init; }

    public IReadOnlyList<string> CompletedItems { get; init; } = [];

    public IReadOnlyList<string> VerificationItems { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];

    public string ReviewFocus { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string CodexBrief { get; init; } = string.Empty;

    public string PacketSummary
    {
        get
        {
            var roleText = Role == AgentRole.Claude ? "Claude" : "Codex";
            var kindText = Kind switch
            {
                PacketKind.StagePlan => "阶段计划",
                PacketKind.ExecutionReport => "执行回报",
                PacketKind.ReviewDecision => "审定结论",
                _ => "未知包",
            };

            var headline = Title;
            if (string.IsNullOrWhiteSpace(headline))
            {
                headline = Summary;
            }

            if (string.IsNullOrWhiteSpace(headline))
            {
                headline = Body;
            }

            headline = headline
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "无摘要";

            return $"{roleText}/{kindText} 阶段 {StageId}: {headline}";
        }
    }
}
