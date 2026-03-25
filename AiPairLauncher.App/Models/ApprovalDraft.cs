namespace AiPairLauncher.App.Models;

public sealed class ApprovalDraft
{
    public int StageId { get; init; }

    public PacketKind SourceKind { get; init; }

    public ReviewDecision? ReviewDecision { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public string CodexBrief { get; init; } = string.Empty;
}
