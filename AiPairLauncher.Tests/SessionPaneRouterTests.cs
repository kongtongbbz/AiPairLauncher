using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class SessionPaneRouterTests
{
    [Fact(DisplayName = "test_transfer_router_prefers_session_top_panes_for_left_to_right")]
    public void TransferRouterPrefersSessionTopPanesForLeftToRight()
    {
        var session = AutomationTestHelpers.CreateSession();
        var request = new SendContextRequest
        {
            Workspace = session.Workspace,
            LastLines = 120,
            Instruction = "test",
            Submit = true,
            FromLeftToRight = true,
        };

        var result = SessionPaneRouter.ResolveTransferPaneIds(session, request);

        Assert.Equal(session.LeftPaneId, result.SourcePaneId);
        Assert.Equal(session.RightPaneId, result.TargetPaneId);
    }

    [Fact(DisplayName = "test_transfer_router_prefers_session_top_panes_for_right_to_left")]
    public void TransferRouterPrefersSessionTopPanesForRightToLeft()
    {
        var session = AutomationTestHelpers.CreateSession();
        var request = new SendContextRequest
        {
            Workspace = session.Workspace,
            LastLines = 120,
            Instruction = "test",
            Submit = true,
            FromLeftToRight = false,
        };

        var result = SessionPaneRouter.ResolveTransferPaneIds(session, request);

        Assert.Equal(session.RightPaneId, result.SourcePaneId);
        Assert.Equal(session.LeftPaneId, result.TargetPaneId);
    }

    [Fact(DisplayName = "test_transfer_router_respects_explicit_pane_ids")]
    public void TransferRouterRespectsExplicitPaneIds()
    {
        var session = AutomationTestHelpers.CreateSession();
        var request = new SendContextRequest
        {
            Workspace = session.Workspace,
            LastLines = 120,
            Instruction = "test",
            Submit = true,
            SourcePaneId = 9,
            TargetPaneId = 10,
            FromLeftToRight = true,
        };

        var result = SessionPaneRouter.ResolveTransferPaneIds(session, request);

        Assert.Equal(9, result.SourcePaneId);
        Assert.Equal(10, result.TargetPaneId);
    }
}
