using System.IO;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AutoCollaborationCoordinator : IAutoCollaborationCoordinator
{
    private const string PacketStartMarker = "[AIPAIR_PACKET]";
    private const string PacketEndMarker = "[/AIPAIR_PACKET]";

    private readonly IWezTermService _wezTermService;
    private readonly IAgentPacketParser _packetParser;
    private readonly TaskMdParser _taskMdParser = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, string> _paneSnapshots = new();
    private readonly Dictionary<string, ProcessedPacketState> _processedFingerprints = new(StringComparer.Ordinal);

    private AutomationRunState _state = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private LauncherSession? _session;
    private AutomationSettings _settings = new();
    private DateTimeOffset _lastProgressAt = DateTimeOffset.Now;
    private int _autoApprovedStageCount;
    private int _currentStageRetryCount;
    private int _currentPollingRound;
    private bool _isFirstStageManualGateSatisfied;
    private bool _forceManualApprovalForNextStagePlan;

    public AutoCollaborationCoordinator(IWezTermService wezTermService, IAgentPacketParser packetParser)
    {
        _wezTermService = wezTermService ?? throw new ArgumentNullException(nameof(wezTermService));
        _packetParser = packetParser ?? throw new ArgumentNullException(nameof(packetParser));
    }

    public event EventHandler<AutomationRunState>? StateChanged;

    public AutomationRunState GetCurrentState()
    {
        return _state;
    }

    public async Task StartAsync(LauncherSession session, AutomationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        if (!session.AutomationEnabledAtLaunch)
        {
            throw new InvalidOperationException("当前会话不是自动模式会话，请开启自动交互模式后重新启动会话。");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loopTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("自动编排已在运行中。");
            }

            _session = session;
            _settings = settings;
            _processedFingerprints.Clear();
            _paneSnapshots.Clear();
            _lastProgressAt = DateTimeOffset.Now;
            _autoApprovedStageCount = 0;
            _currentStageRetryCount = 0;
            _currentPollingRound = 0;
            _isFirstStageManualGateSatisfied = false;
            _forceManualApprovalForNextStagePlan = false;
            _loopCts = new CancellationTokenSource();

            UpdateState(
                phase: AutomationPhase.Phase1Research,
                status: AutomationStageStatus.BootstrappingClaude,
                currentStageId: null,
                statusDetail: "正在注入 Claude Phase 1 调研提示词",
                taskMdPath: BuildTaskMdPath(session),
                taskMdStatus: TaskMdStatus.Unknown);

            var bootstrapPrompt = AutomationPromptFactory.BuildClaudeBootstrapPrompt(
                session.WorkingDirectory,
                settings.InitialTaskPrompt,
                BuildTaskMdPath(session));
            await _wezTermService
                .SendAutomationPromptAsync(session, AgentRole.Claude, bootstrapPrompt, settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(
                phase: AutomationPhase.Phase1Research,
                status: AutomationStageStatus.WaitingForClaudePlan,
                currentStageId: null,
                statusDetail: "等待 Claude 输出 Phase 1 调研计划",
                lastPacketSummary: "已向 Claude 发送 Phase 1 调研提示词",
                taskMdPath: BuildTaskMdPath(session));

            _loopTask = Task.Run(() => PollLoopAsync(_loopCts.Token), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(
        LauncherSession session,
        AutomationSettings settings,
        AutomationRunState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        ValidateSettings(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _session = session;
            _settings = settings;
            _state = state;
            _lastProgressAt = state.UpdatedAt == default ? DateTimeOffset.Now : state.UpdatedAt;
            _autoApprovedStageCount = state.AutoApprovedStageCount;
            _currentStageRetryCount = state.CurrentStageRetryCount;
            _currentPollingRound = 0;
            _isFirstStageManualGateSatisfied = settings.AdvancePolicy != AutomationAdvancePolicy.ManualFirstStageThenAuto
                || state.AutoAdvanceEnabled;
            _forceManualApprovalForNextStagePlan = false;
            _loopCts = new CancellationTokenSource();

            if (state.IsActive && !state.HasPendingApproval)
            {
                _loopTask = Task.Run(() => PollLoopAsync(_loopCts.Token), CancellationToken.None);
            }

            StateChanged?.Invoke(this, _state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApproveAsync(string? userNote, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSession();

            if (_state.Status != AutomationStageStatus.PendingUserApproval || _state.PendingApproval is null)
            {
                throw new InvalidOperationException("当前没有待审批计划。");
            }

            if (_settings.AdvancePolicy == AutomationAdvancePolicy.ManualFirstStageThenAuto &&
                _state.PendingApproval.StageId == 1 &&
                _state.PendingApproval.Phase is AutomationPhase.None or AutomationPhase.Phase3Execution)
            {
                _isFirstStageManualGateSatisfied = true;
            }

            switch (_state.PendingApproval.Phase)
            {
                case AutomationPhase.Phase1Research:
                    EnsureTaskMdStatus(_state.PendingApproval, TaskMdStatus.PendingPlan);
                    await SendClaudePhasePromptAsync(
                        prompt: AutomationPromptFactory.BuildPhase2PlanningPrompt(
                            _session!.WorkingDirectory,
                            _settings.InitialTaskPrompt,
                            ResolveTaskMdPath(_state.PendingApproval)),
                        phase: AutomationPhase.Phase2Planning,
                        currentStageId: _state.PendingApproval.StageId,
                        statusDetail: "Phase 1 已批准，等待 Claude 输出 Phase 2 规划",
                        lastPacketSummary: $"已批准: {_state.PendingApproval.Title}",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case AutomationPhase.Phase2Planning:
                    EnsureTaskMdStatus(_state.PendingApproval, TaskMdStatus.Planned);
                    await SendClaudePhasePromptAsync(
                        prompt: AutomationPromptFactory.BuildPhase3KickoffPrompt(
                            _session!.WorkingDirectory,
                            _settings.InitialTaskPrompt,
                            ResolveTaskMdPath(_state.PendingApproval)),
                        phase: AutomationPhase.Phase3Execution,
                        currentStageId: null,
                        statusDetail: "Phase 2 已批准，等待 Claude 输出 Phase 3 首个执行计划",
                        lastPacketSummary: $"已批准: {_state.PendingApproval.Title}",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await DispatchToCodexAsync(_state.PendingApproval, userNote, isAutomatic: false, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RejectAsync(string? userNote, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSession();

            if (_state.Status != AutomationStageStatus.PendingUserApproval || _state.PendingApproval is null)
            {
                throw new InvalidOperationException("当前没有待退回计划。");
            }

            var prompt = AutomationPromptFactory.BuildClaudeRevisionPrompt(_state.PendingApproval, userNote, _settings.InitialTaskPrompt);
            await _wezTermService
                .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _forceManualApprovalForNextStagePlan = true;
            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(
                phase: _state.PendingApproval.Phase,
                status: AutomationStageStatus.WaitingForClaudePlan,
                currentStageId: _state.PendingApproval.StageId,
                statusDetail: $"已退回阶段 {_state.PendingApproval.StageId}，等待 Claude 重拟计划",
                lastPacketSummary: $"用户退回: {_state.PendingApproval.Title}",
                taskMdPath: ResolveTaskMdPath(_state.PendingApproval),
                taskMdStatus: _state.PendingApproval.TaskMdStatus,
                currentTaskRef: _state.PendingApproval.TaskRef);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? loopCts;
        Task? loopTask;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            loopCts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = null;

            UpdateState(
                phase: _state.Phase,
                status: AutomationStageStatus.Stopped,
                currentStageId: _state.CurrentStageId,
                statusDetail: "自动编排已停止",
                lastPacketSummary: _state.LastPacketSummary);
        }
        finally
        {
            _gate.Release();
        }

        loopCts?.Cancel();
        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 后台轮询被正常终止时不再抛出
            }
        }

        loopCts?.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.PollIntervalMilliseconds));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await PollOnceAsync(cancellationToken).ConfigureAwait(false);
                    if (_state.IsTerminal)
                    {
                        return;
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停止自动编排时会主动取消轮询
        }
        catch (Exception ex)
        {
            await PauseOnErrorAsync($"自动轮询失败: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        EnsureSession();

        if (_state.Status is not AutomationStageStatus.WaitingForClaudePlan
            and not AutomationStageStatus.WaitingForCodexReport
            and not AutomationStageStatus.WaitingForClaudeReview)
        {
            return;
        }

        var paneId = _state.Status == AutomationStageStatus.WaitingForCodexReport
            ? _session!.RightPaneId
            : _session!.LeftPaneId;

        var paneText = await _wezTermService
            .ReadPaneTextAsync(_session!, paneId, _settings.CaptureLines, cancellationToken)
            .ConfigureAwait(false);
        var hasPaneProgress = RefreshPaneProgress(paneId, paneText);

        DetectInteractivePromptOrThrow(paneText);

        var parseOutcome = _packetParser.ParseLatest(paneText);
        switch (parseOutcome.Status)
        {
            case PacketParseStatus.NoPacket:
                EnsureNotTimedOut(hasPaneProgress);
                return;
            case PacketParseStatus.Invalid:
                throw new InvalidOperationException(parseOutcome.ErrorMessage ?? "结构化数据包解析失败。");
            case PacketParseStatus.Success:
                break;
            default:
                throw new InvalidOperationException("未知的解析结果。");
        }

        var packet = parseOutcome.Packet ?? throw new InvalidOperationException("结构化包解析成功但没有生成数据包。");
        var packetOccurrenceCount = CountPacketOccurrences(paneText, packet);
        if (ShouldIgnoreProcessedPacket(packet, packetOccurrenceCount))
        {
            EnsureNotTimedOut(hasPaneProgress);
            return;
        }

        ValidatePacketForState(packet);
        _processedFingerprints[packet.Fingerprint] = new ProcessedPacketState(_currentPollingRound, packetOccurrenceCount);
        _lastProgressAt = DateTimeOffset.Now;

        switch (_state.Status)
        {
            case AutomationStageStatus.WaitingForClaudePlan:
                await ProcessStagePlanAsync(packet, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationStageStatus.WaitingForCodexReport:
                await ProcessExecutionReportAsync(packet, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationStageStatus.WaitingForClaudeReview:
                await ProcessReviewDecisionAsync(packet, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ProcessStagePlanAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var draft = BuildApprovalDraft(packet);
        if (_state.CurrentStageId != packet.StageId)
        {
            _currentStageRetryCount = 0;
        }

        if (_forceManualApprovalForNextStagePlan)
        {
            _forceManualApprovalForNextStagePlan = false;
            EnterPendingApproval(draft, packet.PacketSummary, "上轮计划已被人工退回，请确认重拟结果。");
            return;
        }

        var interventionReason = TryGetInterventionReason(draft);
        if (interventionReason is not null)
        {
            EnterPendingApproval(draft, packet.PacketSummary, interventionReason);
            return;
        }

        if (draft.Phase is AutomationPhase.Phase1Research or AutomationPhase.Phase2Planning)
        {
            EnterPendingApproval(draft, packet.PacketSummary, "Phase 1/2 计划必须人工审批。");
            return;
        }

        await DispatchToCodexAsync(draft, userNote: null, isAutomatic: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessExecutionReportAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var prompt = AutomationPromptFactory.BuildClaudeReviewPrompt(packet, _settings.InitialTaskPrompt);
        await _wezTermService
            .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
            .ConfigureAwait(false);

        UpdateState(
            phase: packet.Phase == AutomationPhase.None ? _state.Phase : packet.Phase,
            status: AutomationStageStatus.WaitingForClaudeReview,
            currentStageId: packet.StageId,
            statusDetail: $"已收到 Codex 的阶段 {packet.StageId} 回报，等待 Claude 审定",
            lastPacketSummary: packet.PacketSummary,
            currentTaskRef: packet.TaskRef,
            taskMdPath: packet.TaskMdPath ?? _state.TaskMdPath,
            taskMdStatus: packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus);
    }

    private async Task ProcessReviewDecisionAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        if (_state.Phase == AutomationPhase.Phase3Execution && packet.Decision == ReviewDecision.Complete)
        {
            var prompt = AutomationPromptFactory.BuildPhase4ReviewPrompt(
                _settings.InitialTaskPrompt,
                ResolveTaskMdPath(packet));
            await _wezTermService
                .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(
                phase: AutomationPhase.Phase4Review,
                status: AutomationStageStatus.WaitingForClaudeReview,
                currentStageId: packet.StageId,
                statusDetail: $"阶段 {packet.StageId} 已完成，等待 Claude 输出 Phase 4 最终复核",
                lastPacketSummary: packet.PacketSummary,
                taskMdPath: ResolveTaskMdPath(packet),
                taskMdStatus: packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus,
                currentTaskRef: packet.TaskRef);
            return;
        }

        if (_state.Phase == AutomationPhase.Phase4Review)
        {
            switch (packet.Decision)
            {
                case ReviewDecision.Complete:
                    EnsureTaskMdStatus(packet, TaskMdStatus.Done);
                    UpdateState(
                        phase: AutomationPhase.Phase4Review,
                        status: AutomationStageStatus.Completed,
                        currentStageId: packet.StageId,
                        statusDetail: $"Claude 已完成 Phase 4 复核，阶段 {packet.StageId} 闭环结束",
                        lastPacketSummary: packet.PacketSummary,
                        taskMdPath: ResolveTaskMdPath(packet),
                        taskMdStatus: TaskMdStatus.Done,
                        currentTaskRef: packet.TaskRef);
                    return;
                case ReviewDecision.RetryStage:
                    _currentStageRetryCount += 1;
                    await HandoffReviewDraftAsync(packet, cancellationToken).ConfigureAwait(false);
                    return;
                case ReviewDecision.Blocked:
                    UpdateState(
                        phase: AutomationPhase.Phase4Review,
                        status: AutomationStageStatus.PausedOnError,
                        currentStageId: packet.StageId,
                        statusDetail: $"Claude 在 Phase 4 将阶段 {packet.StageId} 标记为阻塞",
                        lastPacketSummary: packet.PacketSummary,
                        lastError: string.IsNullOrWhiteSpace(packet.Body) ? packet.Summary : packet.Body,
                        taskMdPath: ResolveTaskMdPath(packet),
                        taskMdStatus: packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus,
                        currentTaskRef: packet.TaskRef);
                    return;
                case ReviewDecision.NextStage:
                    throw new InvalidOperationException("Phase 4 不允许 next_stage。");
            }
        }

        switch (packet.Decision)
        {
            case ReviewDecision.NextStage:
                _currentStageRetryCount = 0;
                await HandoffReviewDraftAsync(packet, cancellationToken).ConfigureAwait(false);
                return;
            case ReviewDecision.RetryStage:
                _currentStageRetryCount += 1;
                await HandoffReviewDraftAsync(packet, cancellationToken).ConfigureAwait(false);
                return;
            case ReviewDecision.Complete:
                UpdateState(
                    phase: packet.Phase == AutomationPhase.None ? _state.Phase : packet.Phase,
                    status: AutomationStageStatus.Completed,
                    currentStageId: packet.StageId,
                    statusDetail: $"Claude 已判定阶段 {packet.StageId} 完成",
                    lastPacketSummary: packet.PacketSummary,
                    taskMdPath: ResolveTaskMdPath(packet),
                    taskMdStatus: packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus,
                    currentTaskRef: packet.TaskRef);
                return;
            case ReviewDecision.Blocked:
                UpdateState(
                    phase: packet.Phase == AutomationPhase.None ? _state.Phase : packet.Phase,
                    status: AutomationStageStatus.PausedOnError,
                    currentStageId: packet.StageId,
                    statusDetail: $"Claude 将阶段 {packet.StageId} 标记为阻塞",
                    lastPacketSummary: packet.PacketSummary,
                    lastError: string.IsNullOrWhiteSpace(packet.Body) ? packet.Summary : packet.Body,
                    taskMdPath: ResolveTaskMdPath(packet),
                    taskMdStatus: packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus,
                    currentTaskRef: packet.TaskRef);
                return;
            default:
                throw new InvalidOperationException("review_decision 缺少有效 decision。");
        }
    }

    private async Task HandoffReviewDraftAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var draft = BuildApprovalDraft(packet);
        if (_state.Phase == AutomationPhase.Phase4Review && draft.Phase == AutomationPhase.None)
        {
            draft = new ApprovalDraft
            {
                Phase = AutomationPhase.Phase3Execution,
                StageId = draft.StageId,
                TaskRef = draft.TaskRef,
                ParallelGroup = draft.ParallelGroup,
                Subagent = draft.Subagent,
                RetryCount = draft.RetryCount,
                SourceKind = draft.SourceKind,
                ReviewDecision = draft.ReviewDecision,
                TaskMdPath = draft.TaskMdPath,
                TaskMdStatus = draft.TaskMdStatus,
                Title = draft.Title,
                Summary = draft.Summary,
                Scope = draft.Scope,
                Steps = draft.Steps,
                AcceptanceCriteria = draft.AcceptanceCriteria,
                TaskProgress = draft.TaskProgress,
                CodexBrief = draft.CodexBrief,
            };
        }

        var interventionReason = TryGetInterventionReason(draft);
        if (interventionReason is not null)
        {
            EnterPendingApproval(draft, packet.PacketSummary, interventionReason);
            return;
        }

        await DispatchToCodexAsync(draft, userNote: null, isAutomatic: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchToCodexAsync(
        ApprovalDraft draft,
        string? userNote,
        bool isAutomatic,
        CancellationToken cancellationToken)
    {
        var prompt = AutomationPromptFactory.BuildCodexExecutionPrompt(draft, userNote);
        await _wezTermService
            .SendAutomationPromptAsync(_session!, AgentRole.Codex, prompt, _settings.SubmitOnSend, cancellationToken)
            .ConfigureAwait(false);

        if (isAutomatic)
        {
            _autoApprovedStageCount += 1;
        }

        _lastProgressAt = DateTimeOffset.Now;
        var actionText = isAutomatic ? "已自动发送" : "已批准并发送";
        UpdateState(
            phase: draft.Phase,
            status: AutomationStageStatus.WaitingForCodexReport,
            currentStageId: draft.StageId,
            statusDetail: $"{actionText}阶段 {draft.StageId} 给 Codex，等待执行回报",
            lastPacketSummary: $"{actionText}: {draft.Title}",
            currentTaskRef: draft.TaskRef,
            taskMdPath: ResolveTaskMdPath(draft),
            taskMdStatus: draft.TaskMdStatus);
    }

    private ApprovalDraft BuildApprovalDraft(AgentPacket packet)
    {
        return new ApprovalDraft
        {
            Phase = packet.Phase,
            StageId = packet.StageId,
            TaskRef = packet.TaskRef,
            ParallelGroup = packet.ParallelGroup,
            Subagent = packet.Subagent,
            RetryCount = packet.RetryCount,
            SourceKind = packet.Kind,
            ReviewDecision = packet.Decision,
            TaskMdPath = packet.TaskMdPath ?? _state.TaskMdPath,
            TaskMdStatus = packet.TaskMdStatus == TaskMdStatus.Unknown ? _state.TaskMdStatus : packet.TaskMdStatus,
            Title = packet.Title,
            Summary = packet.Summary,
            Scope = packet.Scope,
            Steps = packet.Steps,
            AcceptanceCriteria = packet.AcceptanceCriteria,
            TaskProgress = packet.TaskProgress,
            CodexBrief = packet.CodexBrief,
        };
    }

    private void ValidatePacketForState(AgentPacket packet)
    {
        switch (_state.Status)
        {
            case AutomationStageStatus.WaitingForClaudePlan:
                ValidateStagePlanPacket(packet);
                break;
            case AutomationStageStatus.WaitingForCodexReport:
                ValidateExecutionReportPacket(packet);
                break;
            case AutomationStageStatus.WaitingForClaudeReview:
                ValidateReviewDecisionPacket(packet);
                break;
        }
    }

    private bool ShouldIgnoreProcessedPacket(AgentPacket packet, int packetOccurrenceCount)
    {
        if (!_processedFingerprints.TryGetValue(packet.Fingerprint, out var processedPacketState))
        {
            return false;
        }

        if (processedPacketState.PollingRound == _currentPollingRound)
        {
            return true;
        }

        if (!IsPacketExpectedForCurrentState(packet))
        {
            return true;
        }

        return packetOccurrenceCount <= processedPacketState.OccurrenceCount;
    }

    private bool IsPacketExpectedForCurrentState(AgentPacket packet)
    {
        return _state.Status switch
        {
            AutomationStageStatus.WaitingForClaudePlan => IsStagePlanExpected(packet),
            AutomationStageStatus.WaitingForCodexReport => IsExecutionReportExpected(packet),
            AutomationStageStatus.WaitingForClaudeReview => IsReviewDecisionExpected(packet),
            _ => false,
        };
    }

    private bool IsStagePlanExpected(AgentPacket packet)
    {
        if (packet.Role != AgentRole.Claude || packet.Kind != PacketKind.StagePlan)
        {
            return false;
        }

        if (_state.Phase != AutomationPhase.None &&
            packet.Phase != AutomationPhase.None &&
            packet.Phase != _state.Phase)
        {
            return false;
        }

        if (!_state.CurrentStageId.HasValue)
        {
            return packet.StageId == 1;
        }

        return packet.StageId == _state.CurrentStageId.Value;
    }

    private bool IsExecutionReportExpected(AgentPacket packet)
    {
        return packet.Role == AgentRole.Codex &&
               packet.Kind == PacketKind.ExecutionReport &&
               (packet.Phase == AutomationPhase.None || packet.Phase == AutomationPhase.Phase3Execution) &&
               _state.CurrentStageId.HasValue &&
               packet.StageId == _state.CurrentStageId.Value;
    }

    private bool IsReviewDecisionExpected(AgentPacket packet)
    {
        if (packet.Role != AgentRole.Claude ||
            packet.Kind != PacketKind.ReviewDecision ||
            !_state.CurrentStageId.HasValue ||
            packet.Decision is null)
        {
            return false;
        }

        var currentStageId = _state.CurrentStageId.Value;
        if (_state.Phase == AutomationPhase.Phase4Review)
        {
            return packet.Decision.Value switch
            {
                ReviewDecision.RetryStage => packet.StageId == currentStageId,
                ReviewDecision.Complete => packet.StageId == currentStageId,
                ReviewDecision.Blocked => packet.StageId == currentStageId,
                _ => false,
            };
        }

        return packet.Decision.Value switch
        {
            ReviewDecision.NextStage => packet.StageId == currentStageId + 1,
            ReviewDecision.RetryStage => packet.StageId == currentStageId,
            ReviewDecision.Complete => packet.StageId == currentStageId,
            ReviewDecision.Blocked => packet.StageId == currentStageId,
            _ => false,
        };
    }

    private void ValidateStagePlanPacket(AgentPacket packet)
    {
        if (packet.Role != AgentRole.Claude || packet.Kind != PacketKind.StagePlan)
        {
            throw new InvalidOperationException("当前正在等待 Claude 的 stage_plan，但收到其他结构化包。");
        }

        var isLegacyBootstrapPacket =
            _state.Phase == AutomationPhase.Phase1Research &&
            !_state.CurrentStageId.HasValue &&
            packet.Phase == AutomationPhase.None &&
            packet.StageId == 1;

        if (_state.Phase != AutomationPhase.None &&
            packet.Phase == AutomationPhase.None &&
            !isLegacyBootstrapPacket)
        {
            throw new InvalidOperationException($"当前 phase 为 {_state.Phase}，收到的 stage_plan 缺少 phase。");
        }

        if (_state.Phase != AutomationPhase.None &&
            packet.Phase != _state.Phase &&
            !isLegacyBootstrapPacket)
        {
            throw new InvalidOperationException($"当前 phase 为 {_state.Phase}，但收到 {packet.Phase} 的 stage_plan。");
        }

        if (!_state.CurrentStageId.HasValue)
        {
            if (packet.StageId != 1)
            {
                throw new InvalidOperationException($"首次阶段计划必须从 1 开始，收到阶段 {packet.StageId}。");
            }

            return;
        }

        if (packet.StageId != _state.CurrentStageId.Value)
        {
            throw new InvalidOperationException($"重拟阶段计划时必须保持阶段号 {_state.CurrentStageId.Value}，收到阶段 {packet.StageId}。");
        }
    }

    private void ValidateExecutionReportPacket(AgentPacket packet)
    {
        if (packet.Role != AgentRole.Codex || packet.Kind != PacketKind.ExecutionReport)
        {
            throw new InvalidOperationException("当前正在等待 Codex 的 execution_report，但收到其他结构化包。");
        }

        if (_state.Phase == AutomationPhase.Phase3Execution && packet.Phase == AutomationPhase.None)
        {
            throw new InvalidOperationException("当前 Phase 3 执行要求 execution_report 显式携带 phase3_execution。");
        }

        if (packet.Phase != AutomationPhase.None && packet.Phase != AutomationPhase.Phase3Execution)
        {
            throw new InvalidOperationException($"execution_report 只允许 phase3_execution，收到 {packet.Phase}。");
        }

        if (!_state.CurrentStageId.HasValue || packet.StageId != _state.CurrentStageId.Value)
        {
            throw new InvalidOperationException($"execution_report 阶段号必须等于当前阶段 {_state.CurrentStageId ?? 0}，收到阶段 {packet.StageId}。");
        }
    }

    private void ValidateReviewDecisionPacket(AgentPacket packet)
    {
        if (packet.Role != AgentRole.Claude || packet.Kind != PacketKind.ReviewDecision)
        {
            throw new InvalidOperationException("当前正在等待 Claude 的 review_decision，但收到其他结构化包。");
        }

        if (!_state.CurrentStageId.HasValue)
        {
            throw new InvalidOperationException("当前没有活动阶段，无法审定 review_decision。");
        }

        if (packet.Decision is null)
        {
            throw new InvalidOperationException("review_decision 缺少有效 decision。");
        }

        var currentStageId = _state.CurrentStageId.Value;
        if (_state.Phase == AutomationPhase.Phase4Review)
        {
            switch (packet.Decision.Value)
            {
                case ReviewDecision.NextStage:
                    throw new InvalidOperationException("Phase 4 不允许 next_stage。");
                case ReviewDecision.RetryStage:
                    if (packet.StageId != currentStageId)
                    {
                        throw new InvalidOperationException($"Phase 4 retry_stage 必须保持当前阶段 {currentStageId}，收到阶段 {packet.StageId}。");
                    }

                    if (packet.Phase == AutomationPhase.None)
                    {
                        throw new InvalidOperationException("Phase 4 retry_stage 必须显式回退到 phase3_execution。");
                    }

                    if (packet.Phase != AutomationPhase.Phase3Execution)
                    {
                        throw new InvalidOperationException($"Phase 4 retry_stage 必须回退到 phase3_execution，收到 {packet.Phase}。");
                    }
                    break;
                case ReviewDecision.Complete:
                case ReviewDecision.Blocked:
                    if (packet.StageId != currentStageId)
                    {
                        throw new InvalidOperationException($"Phase 4 审定结论必须对应阶段 {currentStageId}，收到阶段 {packet.StageId}。");
                    }

                    if (packet.Phase == AutomationPhase.None)
                    {
                        throw new InvalidOperationException("Phase 4 终态必须显式携带 phase4_review。");
                    }

                    if (packet.Phase != AutomationPhase.Phase4Review)
                    {
                        throw new InvalidOperationException($"Phase 4 终态必须保持 phase4_review，收到 {packet.Phase}。");
                    }
                    break;
            }

            return;
        }

        switch (packet.Decision.Value)
        {
            case ReviewDecision.NextStage when packet.StageId != currentStageId + 1:
                throw new InvalidOperationException($"next_stage 必须推进到阶段 {currentStageId + 1}，收到阶段 {packet.StageId}。");
            case ReviewDecision.RetryStage when packet.StageId != currentStageId:
                throw new InvalidOperationException($"retry_stage 必须保持当前阶段 {currentStageId}，收到阶段 {packet.StageId}。");
            case ReviewDecision.Complete when packet.StageId != currentStageId:
            case ReviewDecision.Blocked when packet.StageId != currentStageId:
                throw new InvalidOperationException($"当前审定结论必须对应阶段 {currentStageId}，收到阶段 {packet.StageId}。");
        }

        if (_state.Phase == AutomationPhase.Phase3Execution && packet.Phase == AutomationPhase.None)
        {
            throw new InvalidOperationException("当前 Phase 3 审定要求 review_decision 显式携带 phase3_execution。");
        }

        if (packet.Phase != AutomationPhase.None && packet.Phase != AutomationPhase.Phase3Execution)
        {
            throw new InvalidOperationException($"当前 Phase 3 审定只允许 phase3_execution，收到 {packet.Phase}。");
        }
    }

    private bool RefreshPaneProgress(int paneId, string paneText)
    {
        var snapshot = BuildPaneSnapshot(paneText);
        if (_paneSnapshots.TryGetValue(paneId, out var previousSnapshot) &&
            string.Equals(previousSnapshot, snapshot, StringComparison.Ordinal))
        {
            return false;
        }

        _paneSnapshots[paneId] = snapshot;
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            _lastProgressAt = DateTimeOffset.Now;
        }

        return true;
    }

    private static string BuildPaneSnapshot(string paneText)
    {
        return NormalizePaneText(paneText);
    }

    private static string NormalizePaneText(string paneText)
    {
        if (string.IsNullOrWhiteSpace(paneText))
        {
            return string.Empty;
        }

        return paneText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static int CountPacketOccurrences(string paneText, AgentPacket packet)
    {
        var normalizedPaneText = NormalizePaneText(paneText);
        if (string.IsNullOrWhiteSpace(normalizedPaneText))
        {
            return 0;
        }

        var decoratedBlock = $"{PacketStartMarker}\n{packet.RawText}\n{PacketEndMarker}";
        var decoratedOccurrenceCount = CountOccurrences(normalizedPaneText, decoratedBlock);
        if (decoratedOccurrenceCount > 0)
        {
            return decoratedOccurrenceCount;
        }

        return CountOccurrences(normalizedPaneText, packet.RawText);
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var searchIndex = 0;
        while (searchIndex < source.Length)
        {
            var foundIndex = source.IndexOf(value, searchIndex, StringComparison.Ordinal);
            if (foundIndex < 0)
            {
                break;
            }

            count += 1;
            searchIndex = foundIndex + value.Length;
        }

        return count;
    }

    private void EnsureNotTimedOut(bool hasPaneProgress = false)
    {
        if (hasPaneProgress)
        {
            return;
        }

        var timeout = TimeSpan.FromSeconds(_settings.NoProgressTimeoutSeconds);
        if (DateTimeOffset.Now - _lastProgressAt <= timeout)
        {
            return;
        }

        throw new TimeoutException($"超过 {timeout.TotalSeconds:0} 秒未收到新的结构化输出。");
    }

    private async Task PauseOnErrorAsync(string message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateState(
                phase: _state.Phase,
                status: AutomationStageStatus.PausedOnError,
                currentStageId: _state.CurrentStageId,
                statusDetail: "自动编排已暂停",
                lastPacketSummary: _state.LastPacketSummary,
                lastError: message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateSettings(AutomationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.InitialTaskPrompt))
        {
            throw new ArgumentException("自动模式任务目标不能为空。", nameof(settings.InitialTaskPrompt));
        }

        if (settings.PollIntervalMilliseconds < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.PollIntervalMilliseconds), "轮询间隔不能小于 200 毫秒。");
        }

        if (settings.CaptureLines < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.CaptureLines), "抓取行数不能小于 20。");
        }

        if (settings.NoProgressTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.NoProgressTimeoutSeconds), "超时时间不能小于 1 秒。");
        }

        if (settings.MaxAutoStages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.MaxAutoStages), "最大自动阶段数不能小于 1。");
        }

        if (settings.MaxRetryPerStage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.MaxRetryPerStage), "单阶段最大重试次数不能小于 0。");
        }
    }

    private void EnsureSession()
    {
        if (_session is not null)
        {
            return;
        }

        throw new InvalidOperationException("当前没有可用会话，无法执行自动编排。");
    }

    private void EnterPendingApproval(ApprovalDraft draft, string packetSummary, string interventionReason)
    {
        var statusDetail = draft.SourceKind switch
        {
            PacketKind.StagePlan => $"收到 Claude 的阶段 {draft.StageId} 计划，等待人工接管",
            PacketKind.ReviewDecision => $"收到 Claude 的{GetDecisionText(draft.ReviewDecision)}，等待人工接管",
            _ => $"收到阶段 {draft.StageId} 的待执行计划，等待人工接管",
        };

        UpdateState(
            phase: draft.Phase,
            status: AutomationStageStatus.PendingUserApproval,
            currentStageId: draft.StageId,
            statusDetail: statusDetail,
            lastPacketSummary: packetSummary,
            pendingApproval: draft,
            interventionReason: interventionReason,
            currentTaskRef: draft.TaskRef,
            taskMdPath: ResolveTaskMdPath(draft),
            taskMdStatus: draft.TaskMdStatus);
    }

    private string? TryGetInterventionReason(ApprovalDraft draft)
    {
        if (draft.Phase is AutomationPhase.Phase1Research or AutomationPhase.Phase2Planning)
        {
            return $"当前 {draft.Phase} 必须人工审批。";
        }

        if (_settings.AdvancePolicy == AutomationAdvancePolicy.ManualEachStage)
        {
            return "当前推进策略要求逐轮人工审批。";
        }

        if (_settings.AdvancePolicy == AutomationAdvancePolicy.ManualFirstStageThenAuto &&
            !_isFirstStageManualGateSatisfied &&
            draft.StageId == 1)
        {
            return "当前推进策略要求首阶段人工审批。";
        }

        if (draft.StageId > _settings.MaxAutoStages)
        {
            return $"已达到最大自动阶段数 {_settings.MaxAutoStages}，等待人工继续。";
        }

        if (draft.ReviewDecision == ReviewDecision.RetryStage &&
            _currentStageRetryCount > _settings.MaxRetryPerStage)
        {
            return $"当前阶段自动重试次数已超过 {_settings.MaxRetryPerStage} 次，等待人工继续。";
        }

        return null;
    }

    private static string GetDecisionText(ReviewDecision? decision)
    {
        return decision switch
        {
            ReviewDecision.NextStage => "下一阶段计划",
            ReviewDecision.RetryStage => "重试计划",
            ReviewDecision.Complete => "完成结论",
            ReviewDecision.Blocked => "阻塞结论",
            _ => "待执行计划",
        };
    }

    private void UpdateState(
        AutomationPhase? phase,
        AutomationStageStatus status,
        int? currentStageId,
        string statusDetail,
        string? currentTaskRef = null,
        string? taskMdPath = null,
        TaskMdStatus? taskMdStatus = null,
        string? lastPacketSummary = null,
        ApprovalDraft? pendingApproval = null,
        string? lastError = null,
        string? interventionReason = null)
    {
        if (IsPollingStatus(status) &&
            (_state.Status != status || _state.CurrentStageId != currentStageId))
        {
            _currentPollingRound += 1;
        }

        var resolvedTaskMdPath = taskMdPath ?? _state.TaskMdPath;
        var resolvedTaskMdStatus = taskMdStatus ?? _state.TaskMdStatus;
        var resolvedTaskCount = _state.TaskCount;
        var resolvedCompletedTaskCount = _state.CompletedTaskCount;
        var resolvedTaskRef = currentTaskRef ?? _state.CurrentTaskRef;
        var resolvedTaskStageHeading = _state.CurrentTaskStageHeading;

        if (!string.IsNullOrWhiteSpace(resolvedTaskMdPath) && File.Exists(resolvedTaskMdPath))
        {
            var validation = _taskMdParser.ParseFile(resolvedTaskMdPath);
            var snapshot = validation.Snapshot;
            resolvedTaskMdStatus = taskMdStatus ?? snapshot.Status;
            resolvedTaskCount = snapshot.TaskCount;
            resolvedCompletedTaskCount = snapshot.CompletedTaskCount;
            resolvedTaskRef = string.IsNullOrWhiteSpace(currentTaskRef) ? snapshot.CurrentTaskRef : currentTaskRef;
            resolvedTaskStageHeading = snapshot.CurrentStageHeading;
        }

        _state = new AutomationRunState
        {
            Phase = phase ?? _state.Phase,
            Status = status,
            CurrentStageId = currentStageId,
            CurrentTaskRef = resolvedTaskRef,
            CurrentTaskStageHeading = resolvedTaskStageHeading,
            StatusDetail = statusDetail,
            LastPacketSummary = lastPacketSummary ?? _state.LastPacketSummary,
            TaskMdPath = resolvedTaskMdPath,
            TaskMdStatus = resolvedTaskMdStatus,
            TaskCount = resolvedTaskCount,
            CompletedTaskCount = resolvedCompletedTaskCount,
            PendingApproval = pendingApproval,
            LastError = lastError,
            AutoAdvanceEnabled = _settings.AdvancePolicy switch
            {
                AutomationAdvancePolicy.ManualEachStage => false,
                AutomationAdvancePolicy.ManualFirstStageThenAuto => _isFirstStageManualGateSatisfied,
                _ => true,
            },
            AutoApprovedStageCount = _autoApprovedStageCount,
            CurrentStageRetryCount = _currentStageRetryCount,
            InterventionReason = interventionReason,
            UpdatedAt = DateTimeOffset.Now,
        };

        StateChanged?.Invoke(this, _state);
    }

    private static bool IsPollingStatus(AutomationStageStatus status)
    {
        return status is AutomationStageStatus.WaitingForClaudePlan
            or AutomationStageStatus.WaitingForCodexReport
            or AutomationStageStatus.WaitingForClaudeReview;
    }

    private void DetectInteractivePromptOrThrow(string paneText)
    {
        if (string.IsNullOrWhiteSpace(paneText))
        {
            return;
        }

        if (_state.Status == AutomationStageStatus.WaitingForClaudePlan &&
            paneText.Contains("Quick safety check:", StringComparison.OrdinalIgnoreCase) &&
            paneText.Contains("Yes, I trust this folder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Claude 正在等待工作区信任确认，请先在左侧 pane 手动确认该目录，再重新启动自动模式。");
        }
    }

    private readonly record struct ProcessedPacketState(int PollingRound, int OccurrenceCount);

    private async Task SendClaudePhasePromptAsync(
        string prompt,
        AutomationPhase phase,
        int? currentStageId,
        string statusDetail,
        string lastPacketSummary,
        CancellationToken cancellationToken)
    {
        await _wezTermService
            .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
            .ConfigureAwait(false);

        _lastProgressAt = DateTimeOffset.Now;
        UpdateState(
            phase: phase,
            status: AutomationStageStatus.WaitingForClaudePlan,
            currentStageId: currentStageId,
            statusDetail: statusDetail,
            lastPacketSummary: lastPacketSummary,
            taskMdPath: BuildTaskMdPath(_session!));
    }

    private string BuildTaskMdPath(LauncherSession session)
    {
        return TaskMdPathResolver.BuildDefault(session.WorkingDirectory);
    }

    private string ResolveTaskMdPath(ApprovalDraft draft)
    {
        if (!string.IsNullOrWhiteSpace(draft.TaskMdPath))
        {
            return draft.TaskMdPath;
        }

        if (!string.IsNullOrWhiteSpace(_state.TaskMdPath))
        {
            return _state.TaskMdPath;
        }

        EnsureSession();
        return TaskMdPathResolver.ResolveExistingOrDefault(_session!.WorkingDirectory);
    }

    private string ResolveTaskMdPath(AgentPacket packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.TaskMdPath))
        {
            return packet.TaskMdPath;
        }

        if (!string.IsNullOrWhiteSpace(_state.TaskMdPath))
        {
            return _state.TaskMdPath;
        }

        EnsureSession();
        return TaskMdPathResolver.ResolveExistingOrDefault(_session!.WorkingDirectory);
    }

    private void EnsureTaskMdStatus(ApprovalDraft draft, TaskMdStatus expectedStatus)
    {
        var taskMdPath = ResolveTaskMdPath(draft);
        var validation = _taskMdParser.ParseFile(taskMdPath);
        if (!validation.IsValid || validation.Document is null)
        {
            var error = validation.Errors.Count == 0
                ? $"task.md 校验失败: {taskMdPath}"
                : string.Join(" | ", validation.Errors);
            throw new InvalidOperationException(error);
        }

        if (validation.Document.Status != expectedStatus)
        {
            throw new InvalidOperationException(
                $"task.md 状态不匹配，期望 {expectedStatus}，实际 {validation.Document.Status}。");
        }
    }

    private void EnsureTaskMdStatus(AgentPacket packet, TaskMdStatus expectedStatus)
    {
        EnsureTaskMdStatus(BuildApprovalDraft(packet), expectedStatus);
    }
}
