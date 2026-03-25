using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AutoCollaborationCoordinatorTests
{
    [Fact(DisplayName = "test_deduplicate_same_packet")]
    public async Task DeduplicateSamePacketAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var settings = AutomationTestHelpers.CreateSettings();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 3));

        await Task.Delay(450);

        Assert.Equal(3, fakeWezTerm.SentAutomationPrompts.Count);
        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_ignore_processed_packet_from_previous_pane")]
    public async Task IgnoreProcessedPacketFromPreviousPaneAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, AutomationTestHelpers.CreateSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        await Task.Delay(600);
        Assert.Equal(AutomationStageStatus.WaitingForClaudeReview, coordinator.GetCurrentState().Status);
        Assert.Null(coordinator.GetCurrentState().LastError);

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            string.Join(Environment.NewLine, AutomationTestHelpers.BuildStagePlanPacket(1), AutomationTestHelpers.BuildReviewDecisionPacket(1, "complete", string.Empty)));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.Completed));
    }

    [Fact(DisplayName = "test_state_flow_plan_approve_execute_review_next_stage")]
    public async Task StateFlowPlanApproveExecuteReviewNextStageAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, AutomationTestHelpers.CreateSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("请继续");
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "next_stage", "执行第二阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var state = coordinator.GetCurrentState();
        Assert.Equal(2, state.CurrentStageId);
        Assert.True(state.HasPendingApproval);
        Assert.Equal("执行第二阶段", state.PendingApproval!.CodexBrief);
        Assert.Equal(3, fakeWezTerm.SentAutomationPrompts.Count);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_first_stage_zero_is_normalized_to_one")]
    public async Task FirstStageZeroIsNormalizedToOneAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildStagePlanPacket(0, "零阶段", "执行零阶段"));

        await coordinator.StartAsync(session, AutomationTestHelpers.CreateSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.Equal(1, state.PendingApproval!.StageId);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_state_flow_review_complete_stops_automation")]
    public async Task StateFlowReviewCompleteStopsAutomationAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, AutomationTestHelpers.CreateSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(1, "complete", string.Empty));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.Completed));
    }

    [Fact(DisplayName = "test_reject_plan_returns_to_claude")]
    public async Task RejectPlanReturnsToClaudeAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, AutomationTestHelpers.CreateSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.RejectAsync("请缩小改动范围");

        Assert.Equal(AutomationStageStatus.WaitingForClaudePlan, coordinator.GetCurrentState().Status);
        Assert.Contains("请缩小改动范围", fakeWezTerm.SentAutomationPrompts.Last().Prompt);
        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_pause_on_wezterm_failure")]
    public async Task PauseOnWeztermFailureAsync()
    {
        var fakeWezTerm = new FakeWezTermService
        {
            ReadException = new InvalidOperationException("pane 丢失"),
        };
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), AutomationTestHelpers.CreateSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("pane 丢失", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_pause_on_timeout")]
    public async Task PauseOnTimeoutAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), AutomationTestHelpers.CreateSettings(timeoutSeconds: 1));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError, 4000));
        Assert.Contains("未收到新的结构化输出", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_pane_output_change_resets_timeout_while_waiting_for_codex_report")]
    public async Task PaneOutputChangeResetsTimeoutWhileWaitingForCodexReportAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var settings = AutomationTestHelpers.CreateSettings(timeoutSeconds: 2);

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        fakeWezTerm.SetPaneText(session.RightPaneId, "Codex 正在分析任务...");
        await coordinator.ApproveAsync(null);

        await Task.Delay(1200);
        fakeWezTerm.SetPaneText(session.RightPaneId, "Codex 正在修改文件并运行验证...");
        await Task.Delay(1200);

        Assert.Equal(AutomationStageStatus.WaitingForCodexReport, coordinator.GetCurrentState().Status);
        Assert.Null(coordinator.GetCurrentState().LastError);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_duplicate_previous_packet_still_times_out_without_new_progress")]
    public async Task DuplicatePreviousPacketStillTimesOutWithoutNewProgressAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var settings = AutomationTestHelpers.CreateSettings(timeoutSeconds: 2);

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(
            () => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError,
            5000));
        Assert.Contains("未收到新的结构化输出", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_pause_on_claude_trust_prompt")]
    public async Task PauseOnClaudeTrustPromptAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        fakeWezTerm.SetPaneText(1, """
Quick safety check:

1. Yes, I trust this folder
2. No, exit
""");

        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), AutomationTestHelpers.CreateSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("工作区信任确认", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_start_requires_automation_session")]
    public async Task StartRequiresAutomationSessionAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(AutomationTestHelpers.CreateManualSession(), AutomationTestHelpers.CreateSettings()));

        Assert.Contains("自动模式会话", exception.Message);
        Assert.Empty(fakeWezTerm.SentAutomationPrompts);
    }

    [Fact(DisplayName = "test_start_requires_task_prompt")]
    public async Task StartRequiresTaskPromptAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var settings = new AutomationSettings
        {
            InitialTaskPrompt = string.Empty,
            PollIntervalMilliseconds = 200,
            CaptureLines = 220,
            SubmitOnSend = true,
            NoProgressTimeoutSeconds = 2,
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.StartAsync(AutomationTestHelpers.CreateSession(), settings));

        Assert.Contains("任务目标不能为空", exception.Message);
        Assert.Empty(fakeWezTerm.SentAutomationPrompts);
    }
}
