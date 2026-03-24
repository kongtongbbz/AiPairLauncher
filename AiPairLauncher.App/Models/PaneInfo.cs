namespace AiPairLauncher.App.Models;

public sealed class PaneInfo
{
    public required int PaneId { get; init; }

    public required string Workspace { get; init; }

    public string? Title { get; init; }

    public string? CurrentDirectory { get; init; }

    public bool IsActive { get; init; }

    public int LeftCol { get; init; }

    public int Rows { get; init; }

    public int Cols { get; init; }
}

