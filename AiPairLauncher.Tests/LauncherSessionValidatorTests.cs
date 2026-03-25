using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class LauncherSessionValidatorTests
{
    [Fact(DisplayName = "test_validate_session_panes_success")]
    public void ValidateSessionPanesSuccess()
    {
        var session = AutomationTestHelpers.CreateSession();
        IReadOnlyList<PaneInfo> panes =
        [
            new PaneInfo { PaneId = 1, Workspace = session.Workspace, LeftCol = 0, Cols = 100, Rows = 40 },
            new PaneInfo { PaneId = 2, Workspace = session.Workspace, LeftCol = 100, Cols = 100, Rows = 40 },
            new PaneInfo { PaneId = 3, Workspace = session.Workspace, LeftCol = 0, Cols = 100, Rows = 40 },
            new PaneInfo { PaneId = 4, Workspace = session.Workspace, LeftCol = 100, Cols = 100, Rows = 40 },
        ];

        LauncherSessionValidator.EnsurePaneTopology(session, panes);
    }

    [Fact(DisplayName = "test_validate_session_panes_requires_live_ids")]
    public void ValidateSessionPanesRequiresLiveIds()
    {
        var session = AutomationTestHelpers.CreateSession();
        IReadOnlyList<PaneInfo> panes =
        [
            new PaneInfo { PaneId = 3, Workspace = session.Workspace, LeftCol = 0, Cols = 100, Rows = 40 },
            new PaneInfo { PaneId = 4, Workspace = session.Workspace, LeftCol = 100, Cols = 100, Rows = 40 },
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LauncherSessionValidator.EnsurePaneTopology(session, panes));

        Assert.Contains("pane 已失效", exception.Message);
    }

    [Fact(DisplayName = "test_validate_session_panes_requires_any_pane")]
    public void ValidateSessionPanesRequiresAnyPane()
    {
        var session = AutomationTestHelpers.CreateSession();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LauncherSessionValidator.EnsurePaneTopology(session, []));

        Assert.Contains("没有任何可用 pane", exception.Message);
    }
}
