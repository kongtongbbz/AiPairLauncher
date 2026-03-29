using System.IO;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AutoCollaborationCoordinatorTests
{
    [Fact(DisplayName = "test_phase1_executor_routes_bootstrap_prompt_to_selected_role")]
    public async Task Phase1ExecutorRoutesBootstrapPromptToSelectedRoleAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        var settings = AutomationTestHelpers.CreateSettingsWithExecutors(
            phase1Executor: AgentRole.Codex,
            phase2Executor: AgentRole.Claude,
            phase3Executor: AgentRole.Codex,
            phase4Executor: AgentRole.Claude);

        await coordinator.StartAsync(session, settings);

        Assert.True(fakeWezTerm.SentAutomationPrompts.Count >= 1);
        Assert.Equal(AgentRole.Codex, fakeWezTerm.SentAutomationPrompts[0].Role);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_phase2_prompt_routes_to_phase2_executor")]
    public async Task Phase2PromptRoutesToPhase2ExecutorAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = TaskMdPathResolver.BuildDefault(session.WorkingDirectory);

        var settings = AutomationTestHelpers.CreateSettingsWithExecutors(
            phase1Executor: AgentRole.Claude,
            phase2Executor: AgentRole.Codex,
            phase3Executor: AgentRole.Codex,
            phase4Executor: AgentRole.Claude);

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.PendingPlan,
                role: AgentRole.Claude));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("进入 Phase 2");

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 2));
        Assert.Equal(AgentRole.Codex, fakeWezTerm.SentAutomationPrompts.Last().Role);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_phase3_prompt_routes_to_phase3_executor_after_phase2_approval")]
    public async Task Phase3PromptRoutesToPhase3ExecutorAfterPhase2ApprovalAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = TaskMdPathResolver.BuildDefault(session.WorkingDirectory);

        var settings = AutomationTestHelpers.CreateSettingsWithExecutors(
            phase1Executor: AgentRole.Claude,
            phase2Executor: AgentRole.Codex,
            phase3Executor: AgentRole.Claude,
            phase4Executor: AgentRole.Claude);

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.PendingPlan,
                role: AgentRole.Claude));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("进入 Phase 2");
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 2));
        fakeWezTerm.SetPaneText(
            session.RightPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.Planned,
                role: AgentRole.Codex));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("进入 Phase 3");
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 3));
        Assert.Equal(AgentRole.Claude, fakeWezTerm.SentAutomationPrompts.Last().Role);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_phase4_prompt_routes_to_phase4_executor_on_complete")]
    public async Task Phase4PromptRoutesToPhase4ExecutorOnCompleteAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = TaskMdPathResolver.BuildDefault(session.WorkingDirectory);

        var settings = AutomationTestHelpers.CreateSettingsWithExecutors(
            phase1Executor: AgentRole.Claude,
            phase2Executor: AgentRole.Claude,
            phase3Executor: AgentRole.Codex,
            phase4Executor: AgentRole.Codex);

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.PendingPlan,
                role: AgentRole.Claude));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("进入 Phase 2");
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 2));
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.Planned));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync("进入 Phase 3");
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudePlan));

        fakeWezTerm.SetPaneText(
            session.RightPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase3Execution,
                stageId: 1,
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.InProgress,
                role: AgentRole.Codex));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(
            session.RightPaneId,
            AutomationTestHelpers.BuildPhaseExecutionReportPacket(1, taskMdPath, TaskMdStatus.InProgress, role: AgentRole.Codex));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(
            session.RightPaneId,
            AutomationTestHelpers.BuildPhaseReviewDecisionPacket(
                AutomationPhase.Phase3Execution,
                stageId: 1,
                decision: "complete",
                taskMdPath: taskMdPath,
                taskMdStatus: TaskMdStatus.InProgress,
                role: AgentRole.Codex));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 4));
        Assert.Equal(AgentRole.Codex, fakeWezTerm.SentAutomationPrompts.Last().Role);

        await coordinator.StopAsync();
    }
    [Fact(DisplayName = "test_deduplicate_same_packet")]
    public async Task DeduplicateSamePacketAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateManualSettings());
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
        await coordinator.StartAsync(session, CreateManualSettings());
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

    [Fact(DisplayName = "test_full_auto_first_stage_dispatch_without_manual_approval")]
    public async Task FullAutoFirstStageDispatchWithoutManualApprovalAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1, "首阶段", "执行首阶段"));

        await coordinator.StartAsync(session, CreateFullAutoSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.False(state.HasPendingApproval);
        Assert.True(state.AutoAdvanceEnabled);
        Assert.Equal(1, state.AutoApprovedStageCount);
        Assert.Equal(2, fakeWezTerm.SentAutomationPrompts.Count);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_full_auto_next_stage_dispatches_second_round_automatically")]
    public async Task FullAutoNextStageDispatchesSecondRoundAutomaticallyAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "next_stage", "执行第二阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageId == 2));

        var state = coordinator.GetCurrentState();
        Assert.Equal(2, state.CurrentStageId);
        Assert.False(state.HasPendingApproval);
        Assert.Equal(2, state.AutoApprovedStageCount);
        Assert.Equal(4, fakeWezTerm.SentAutomationPrompts.Count);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_manual_first_stage_then_auto_requires_only_first_approval")]
    public async Task ManualFirstStageThenAutoRequiresOnlyFirstApprovalAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateManualFirstThenAutoSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var pendingState = coordinator.GetCurrentState();
        Assert.Equal("当前推进策略要求首阶段人工审批。", pendingState.InterventionReason);
        Assert.False(pendingState.AutoAdvanceEnabled);

        await coordinator.ApproveAsync("首阶段人工确认");
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));
        Assert.True(coordinator.GetCurrentState().AutoAdvanceEnabled);

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "next_stage", "执行第二阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageId == 2));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.AutoApprovedStageCount);
        Assert.False(state.HasPendingApproval);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_retry_stage_auto_retries_until_limit_then_waits_manual_approval")]
    public async Task RetryStageAutoRetriesUntilLimitThenWaitsManualApprovalAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings(maxRetryPerStage: 1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1, "首轮执行完成"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(1, "retry_stage", "重试阶段一-第一次"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageRetryCount == 1));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1, "第二轮执行完成"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(1, "retry_stage", "重试阶段一-第二次"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.Equal(2, state.CurrentStageRetryCount);
        Assert.Contains("自动重试次数已超过 1 次", state.InterventionReason);
    }

    [Fact(DisplayName = "test_next_stage_requires_strict_increment")]
    public async Task NextStageRequiresStrictIncrementAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(3, "next_stage", "错误阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("next_stage 必须推进到阶段 2", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_retry_stage_requires_same_stage_id")]
    public async Task RetryStageRequiresSameStageIdAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "retry_stage", "错误重试"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("必须保持当前阶段 1", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_guardrail_max_stage_switches_to_pending_user_approval")]
    public async Task GuardrailMaxStageSwitchesToPendingUserApprovalAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings(maxAutoStages: 1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "next_stage", "执行第二阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var state = coordinator.GetCurrentState();
        Assert.Equal(2, state.CurrentStageId);
        Assert.Contains("已达到最大自动阶段数 1", state.InterventionReason);
        Assert.True(state.AutoAdvanceEnabled);
        Assert.Equal(3, fakeWezTerm.SentAutomationPrompts.Count);
    }

    [Fact(DisplayName = "test_state_flow_plan_approve_execute_review_next_stage")]
    public async Task StateFlowPlanApproveExecuteReviewNextStageAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateManualSettings());
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

    [Fact(DisplayName = "test_first_stage_zero_is_rejected")]
    public async Task FirstStageZeroIsRejectedAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildStagePlanPacket(0, "零阶段", "执行零阶段"));

        await coordinator.StartAsync(session, CreateManualSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("stage_id 非法: 0", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_state_flow_review_complete_stops_automation")]
    public async Task StateFlowReviewCompleteStopsAutomationAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateManualSettings());
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
        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.RejectAsync("请缩小改动范围");

        Assert.Equal(AutomationStageStatus.WaitingForClaudePlan, coordinator.GetCurrentState().Status);
        Assert.Contains("请缩小改动范围", fakeWezTerm.SentAutomationPrompts.Last().Prompt);
        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_reject_identical_stage_plan_in_new_round_is_reprocessed")]
    public async Task RejectIdenticalStagePlanInNewRoundIsReprocessedAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var stagePlanPacket = AutomationTestHelpers.BuildStagePlanPacket(1, "阶段一计划", "执行阶段一");

        fakeWezTerm.SetPaneText(session.LeftPaneId, stagePlanPacket);
        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.RejectAsync("请重新整理阶段一");
        fakeWezTerm.SetPaneText(session.LeftPaneId, RepeatPacket(stagePlanPacket));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.Equal("上轮计划已被人工退回，请确认重拟结果。", state.InterventionReason);
        Assert.Equal("执行阶段一", state.PendingApproval!.CodexBrief);
        Assert.Null(state.LastError);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_reject_later_stage_revision_prompt_uses_current_stage_id")]
    public async Task RejectLaterStageRevisionPromptUsesCurrentStageIdAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(2, "next_stage", "执行第二阶段"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.RejectAsync("第二阶段计划需要调整");

        var prompt = fakeWezTerm.SentAutomationPrompts.Last().Prompt;
        Assert.Contains("退回阶段: 2", prompt);
        Assert.Contains("stage_id: 2", prompt);
    }

    [Fact(DisplayName = "test_retry_identical_execution_report_in_new_round_is_reprocessed")]
    public async Task RetryIdenticalExecutionReportInNewRoundIsReprocessedAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var executionReportPacket = AutomationTestHelpers.BuildExecutionReportPacket(1, "首轮执行完成");

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, executionReportPacket);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildReviewDecisionPacket(1, "retry_stage", "重试阶段一"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageRetryCount == 1));

        fakeWezTerm.SetPaneText(session.RightPaneId, RepeatPacket(executionReportPacket));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.Equal(1, state.CurrentStageRetryCount);
        Assert.Null(state.LastError);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_identical_review_decision_after_identical_report_advances_new_round")]
    public async Task IdenticalReviewDecisionAfterIdenticalReportAdvancesNewRoundAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var executionReportPacket = AutomationTestHelpers.BuildExecutionReportPacket(1, "首轮执行完成");
        var retryDecisionPacket = AutomationTestHelpers.BuildReviewDecisionPacket(1, "retry_stage", "重试阶段一");

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, CreateFullAutoSettings(maxRetryPerStage: 3));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, executionReportPacket);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(session.LeftPaneId, retryDecisionPacket);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageRetryCount == 1));

        fakeWezTerm.SetPaneText(session.RightPaneId, RepeatPacket(executionReportPacket));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        await Task.Delay(450);
        Assert.Equal(AutomationStageStatus.WaitingForClaudeReview, coordinator.GetCurrentState().Status);
        Assert.Null(coordinator.GetCurrentState().LastError);

        fakeWezTerm.SetPaneText(session.LeftPaneId, RepeatPacket(retryDecisionPacket));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageRetryCount == 2));

        var state = coordinator.GetCurrentState();
        Assert.Equal(1, state.CurrentStageId);
        Assert.Equal(2, state.CurrentStageRetryCount);
        Assert.Equal(6, fakeWezTerm.SentAutomationPrompts.Count);
        Assert.Null(state.LastError);

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

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), CreateFullAutoSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("pane 丢失", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_pause_on_timeout")]
    public async Task PauseOnTimeoutAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), CreateFullAutoSettings(timeoutSeconds: 1));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval, 8000));
        Assert.Equal(AutomationInterventionKind.Timeout, coordinator.GetCurrentState().PendingApproval?.InterventionKind);
        Assert.Contains("未收到新的结构化输出", coordinator.GetCurrentState().InterventionReason);
    }

    [Fact(DisplayName = "test_pane_output_change_resets_timeout_while_waiting_for_codex_report")]
    public async Task PaneOutputChangeResetsTimeoutWhileWaitingForCodexReportAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var settings = CreateManualSettings(timeoutSeconds: 2);

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
        var settings = CreateManualSettings(timeoutSeconds: 2);

        fakeWezTerm.SetPaneText(session.LeftPaneId, AutomationTestHelpers.BuildStagePlanPacket(1));
        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(
            () => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval,
            10000));
        Assert.Equal(AutomationInterventionKind.Timeout, coordinator.GetCurrentState().PendingApproval?.InterventionKind);
        Assert.Contains("未收到新的结构化输出", coordinator.GetCurrentState().InterventionReason);
    }

    [Fact(DisplayName = "test_timeout_intervention_can_continue_waiting")]
    public async Task TimeoutInterventionCanContinueWaitingAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), CreateFullAutoSettings(timeoutSeconds: 1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval, 4000));

        await coordinator.ContinueWaitingAsync();

        Assert.Equal(AutomationStageStatus.WaitingForClaudePlan, coordinator.GetCurrentState().Status);
        Assert.Contains("继续等待阶段", coordinator.GetCurrentState().LastPacketSummary);

        await coordinator.StopAsync();
    }

    [Fact(DisplayName = "test_timeout_intervention_can_retry_current_stage")]
    public async Task TimeoutInterventionCanRetryCurrentStageAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), CreateFullAutoSettings(timeoutSeconds: 1));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval, 4000));

        var sentPromptCount = fakeWezTerm.SentAutomationPrompts.Count;
        await coordinator.RetryCurrentStageAsync();

        Assert.True(fakeWezTerm.SentAutomationPrompts.Count > sentPromptCount);
        Assert.Equal(AutomationStageStatus.WaitingForClaudePlan, coordinator.GetCurrentState().Status);
        Assert.Contains("已手动重试阶段", coordinator.GetCurrentState().LastPacketSummary);

        await coordinator.StopAsync();
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
        await coordinator.StartAsync(AutomationTestHelpers.CreateSession(), CreateFullAutoSettings());

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("工作区信任确认", coordinator.GetCurrentState().LastError);
    }

    [Fact(DisplayName = "test_start_requires_automation_session")]
    public async Task StartRequiresAutomationSessionAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(AutomationTestHelpers.CreateManualSession(), CreateFullAutoSettings()));

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
            AdvancePolicy = AutomationAdvancePolicy.FullAutoLoop,
            PollIntervalMilliseconds = 200,
            CaptureLines = 220,
            SubmitOnSend = true,
            NoProgressTimeoutSeconds = 2,
            MaxAutoStages = 8,
            MaxRetryPerStage = 2,
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.StartAsync(AutomationTestHelpers.CreateSession(), settings));

        Assert.Contains("任务目标不能为空", exception.Message);
        Assert.Empty(fakeWezTerm.SentAutomationPrompts);
    }

    [Fact(DisplayName = "test_phase1_approval_routes_back_to_claude_for_phase2")]
    public async Task Phase1ApprovalRoutesBackToClaudeForPhase2Async()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.PendingPlan,
            """
### 阶段 1: 项目调研

- [ ] **T1.1**: 生成 task.md
""");

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                1,
                taskMdPath,
                TaskMdStatus.PendingPlan,
                "Phase 1 计划",
                "继续 Phase 2"));

        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudePlan &&
            coordinator.GetCurrentState().Phase == AutomationPhase.Phase2Planning));
        Assert.Equal(AgentRole.Claude, fakeWezTerm.SentAutomationPrompts.Last().Role);
        Assert.Contains("phase2_planning", fakeWezTerm.SentAutomationPrompts.Last().Prompt);
    }

    [Fact(DisplayName = "test_phase2_approval_routes_to_phase3_executor")]
    public async Task Phase2ApprovalRoutesToPhase3ExecutorAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.PendingPlan,
            """
### 阶段 1: 项目调研

- [ ] **T1.1**: 生成 task.md
""");

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                1,
                taskMdPath,
                TaskMdStatus.PendingPlan));

        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.Planned,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [ ] **T2.1**: 完成执行规划
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                1,
                taskMdPath,
                TaskMdStatus.Planned,
                "Phase 2 计划",
                "继续 Phase 3"));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudePlan &&
            coordinator.GetCurrentState().Phase == AutomationPhase.Phase3Execution));
        Assert.Equal(AgentRole.Codex, fakeWezTerm.SentAutomationPrompts.Last().Role);
        Assert.Contains("phase3_execution", fakeWezTerm.SentAutomationPrompts.Last().Prompt);
    }

    [Fact(DisplayName = "test_phase3_complete_enters_phase4_and_phase4_complete_finishes")]
    public async Task Phase3CompleteEntersPhase4AndPhase4CompleteFinishesAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.PendingPlan,
            """
### 阶段 1: 项目调研

- [ ] **T1.1**: 生成 task.md
""");

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                1,
                taskMdPath,
                TaskMdStatus.PendingPlan));

        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.Planned,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [x] **T2.1**: 完成执行规划

### 阶段 3: 执行

- [ ] **T3.1**: 实现阶段任务
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                1,
                taskMdPath,
                TaskMdStatus.Planned));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.InProgress,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [x] **T2.1**: 完成执行规划

### 阶段 3: 执行

- [ ] **T3.1**: 实现阶段任务
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase3Execution,
                1,
                taskMdPath,
                TaskMdStatus.InProgress,
                "Phase 3 执行计划",
                "执行 T3.1"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(
            session.RightPaneId,
            AutomationTestHelpers.BuildPhaseExecutionReportPacket(1, taskMdPath, TaskMdStatus.InProgress, taskRef: "T3.1"));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview));

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseReviewDecisionPacket(
                AutomationPhase.Phase3Execution,
                1,
                "complete",
                taskMdPath,
                TaskMdStatus.InProgress));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForClaudeReview &&
            coordinator.GetCurrentState().Phase == AutomationPhase.Phase4Review));

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.Done,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [x] **T2.1**: 完成执行规划

### 阶段 3: 执行

- [x] **T3.1**: 实现阶段任务

### 阶段 4: 复核

- [x] **T4.1**: 最终复核
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseReviewDecisionPacket(
                AutomationPhase.Phase4Review,
                1,
                "complete",
                taskMdPath,
                TaskMdStatus.Done));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.Completed));
    }

    [Fact(DisplayName = "test_phase3_execution_report_missing_phase_is_rejected")]
    public async Task Phase3ExecutionReportMissingPhaseIsRejectedAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var coordinator = new AutoCollaborationCoordinator(fakeWezTerm, new AgentPacketParser());
        var session = AutomationTestHelpers.CreateSession();
        var taskMdPath = AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.PendingPlan,
            """
### 阶段 1: 项目调研

- [ ] **T1.1**: 生成 task.md
""");

        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase1Research,
                1,
                taskMdPath,
                TaskMdStatus.PendingPlan));
        await coordinator.StartAsync(session, CreateManualSettings());
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.Planned,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [x] **T2.1**: 完成执行规划

### 阶段 3: 执行

- [ ] **T3.1**: 实现阶段任务
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                1,
                taskMdPath,
                TaskMdStatus.Planned));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);

        AutomationTestHelpers.WriteTaskMd(
            session,
            TaskMdStatus.InProgress,
            """
### 阶段 1: 项目调研

- [x] **T1.1**: 生成 task.md

### 阶段 2: 计划编排

- [x] **T2.1**: 完成执行规划

### 阶段 3: 执行

- [ ] **T3.1**: 实现阶段任务
""");
        fakeWezTerm.SetPaneText(
            session.LeftPaneId,
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase3Execution,
                1,
                taskMdPath,
                TaskMdStatus.InProgress));
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));
        await coordinator.ApproveAsync(null);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport));

        fakeWezTerm.SetPaneText(session.RightPaneId, AutomationTestHelpers.BuildExecutionReportPacket(1));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => coordinator.GetCurrentState().Status == AutomationStageStatus.PausedOnError));
        Assert.Contains("phase3_execution", coordinator.GetCurrentState().LastError);
    }

    private static AutomationSettings CreateManualSettings(int timeoutSeconds = 2)
    {
        return AutomationTestHelpers.CreateSettings(
            timeoutSeconds: timeoutSeconds,
            advancePolicy: AutomationAdvancePolicy.ManualEachStage);
    }

    private static AutomationSettings CreateManualFirstThenAutoSettings(int timeoutSeconds = 2)
    {
        return AutomationTestHelpers.CreateSettings(
            timeoutSeconds: timeoutSeconds,
            advancePolicy: AutomationAdvancePolicy.ManualFirstStageThenAuto);
    }

    private static AutomationSettings CreateFullAutoSettings(
        int timeoutSeconds = 2,
        int maxAutoStages = 8,
        int maxRetryPerStage = 2)
    {
        return AutomationTestHelpers.CreateSettings(
            timeoutSeconds: timeoutSeconds,
            advancePolicy: AutomationAdvancePolicy.FullAutoLoop,
            maxAutoStages: maxAutoStages,
            maxRetryPerStage: maxRetryPerStage);
    }

    private static int ResolvePaneId(LauncherSession session, AgentRole role)
    {
        return role == AgentRole.Codex ? session.RightPaneId : session.LeftPaneId;
    }

    private static async Task<string> AdvanceToExecutionReadyAsync(
        FakeWezTermService fakeWezTerm,
        AutoCollaborationCoordinator coordinator,
        LauncherSession session,
        AutomationSettings settings)
    {
        fakeWezTerm.SetPaneText(
            ResolvePaneId(session, settings.Phase1Executor),
            AutomationTestHelpers.BuildStagePlanPacket(1, role: settings.Phase1Executor));

        await coordinator.StartAsync(session, settings);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() => fakeWezTerm.SentAutomationPrompts.Count >= 2));

        var taskMdPath = TaskMdPathResolver.BuildDefault(session.WorkingDirectory);
        fakeWezTerm.SetPaneText(
            ResolvePaneId(session, settings.Phase2Executor),
            AutomationTestHelpers.BuildPhaseStagePlanPacket(
                AutomationPhase.Phase2Planning,
                1,
                taskMdPath,
                TaskMdStatus.Planned,
                role: settings.Phase2Executor));

        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.PendingUserApproval));

        await coordinator.ApproveAsync(null);
        Assert.True(await AutomationTestHelpers.WaitForConditionAsync(() =>
            coordinator.GetCurrentState().Status == AutomationStageStatus.WaitingForCodexReport &&
            coordinator.GetCurrentState().CurrentStageId == 1));

        return taskMdPath;
    }

    private static string RepeatPacket(string packet)
    {
        return string.Join(Environment.NewLine, packet, packet);
    }
}
