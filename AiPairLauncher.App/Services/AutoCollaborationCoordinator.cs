using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AutoCollaborationCoordinator : IAutoCollaborationCoordinator
{
    private const string PacketStartMarker = "[AIPAIR_PACKET]";
    private const string PacketEndMarker = "[/AIPAIR_PACKET]";

    private readonly IWezTermService _wezTermService;
    private readonly IAgentPacketParser _packetParser;
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
                status: AutomationStageStatus.BootstrappingClaude,
                currentStageId: null,
                statusDetail: "正在注入 Claude 领导者提示词");

            var bootstrapPrompt = AutomationPromptFactory.BuildClaudeBootstrapPrompt(session.WorkingDirectory, settings.InitialTaskPrompt);
            await _wezTermService
                .SendAutomationPromptAsync(session, AgentRole.Claude, bootstrapPrompt, settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(
                status: AutomationStageStatus.WaitingForClaudePlan,
                currentStageId: null,
                statusDetail: "等待 Claude 输出阶段计划",
                lastPacketSummary: "已向 Claude 发送自动模式引导");

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
                _state.PendingApproval.StageId == 1)
            {
                _isFirstStageManualGateSatisfied = true;
            }

            await DispatchToCodexAsync(_state.PendingApproval, userNote, isAutomatic: false, cancellationToken).ConfigureAwait(false);
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
                status: AutomationStageStatus.WaitingForClaudePlan,
                currentStageId: _state.PendingApproval.StageId,
                statusDetail: $"已退回阶段 {_state.PendingApproval.StageId}，等待 Claude 重拟计划",
                lastPacketSummary: $"用户退回: {_state.PendingApproval.Title}");
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

        await DispatchToCodexAsync(draft, userNote: null, isAutomatic: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessExecutionReportAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var prompt = AutomationPromptFactory.BuildClaudeReviewPrompt(packet, _settings.InitialTaskPrompt);
        await _wezTermService
            .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
            .ConfigureAwait(false);

        UpdateState(
            status: AutomationStageStatus.WaitingForClaudeReview,
            currentStageId: packet.StageId,
            statusDetail: $"已收到 Codex 的阶段 {packet.StageId} 回报，等待 Claude 审定",
            lastPacketSummary: packet.PacketSummary);
    }

    private async Task ProcessReviewDecisionAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
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
                    status: AutomationStageStatus.Completed,
                    currentStageId: packet.StageId,
                    statusDetail: $"Claude 已判定阶段 {packet.StageId} 完成",
                    lastPacketSummary: packet.PacketSummary);
                return;
            case ReviewDecision.Blocked:
                UpdateState(
                    status: AutomationStageStatus.PausedOnError,
                    currentStageId: packet.StageId,
                    statusDetail: $"Claude 将阶段 {packet.StageId} 标记为阻塞",
                    lastPacketSummary: packet.PacketSummary,
                    lastError: string.IsNullOrWhiteSpace(packet.Body) ? packet.Summary : packet.Body);
                return;
            default:
                throw new InvalidOperationException("review_decision 缺少有效 decision。");
        }
    }

    private async Task HandoffReviewDraftAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var draft = BuildApprovalDraft(packet);
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
            status: AutomationStageStatus.WaitingForCodexReport,
            currentStageId: draft.StageId,
            statusDetail: $"{actionText}阶段 {draft.StageId} 给 Codex，等待执行回报",
            lastPacketSummary: $"{actionText}: {draft.Title}");
    }

    private ApprovalDraft BuildApprovalDraft(AgentPacket packet)
    {
        return new ApprovalDraft
        {
            StageId = packet.StageId,
            SourceKind = packet.Kind,
            ReviewDecision = packet.Decision,
            Title = packet.Title,
            Summary = packet.Summary,
            Scope = packet.Scope,
            Steps = packet.Steps,
            AcceptanceCriteria = packet.AcceptanceCriteria,
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
            status: AutomationStageStatus.PendingUserApproval,
            currentStageId: draft.StageId,
            statusDetail: statusDetail,
            lastPacketSummary: packetSummary,
            pendingApproval: draft,
            interventionReason: interventionReason);
    }

    private string? TryGetInterventionReason(ApprovalDraft draft)
    {
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
        AutomationStageStatus status,
        int? currentStageId,
        string statusDetail,
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

        _state = new AutomationRunState
        {
            Status = status,
            CurrentStageId = currentStageId,
            StatusDetail = statusDetail,
            LastPacketSummary = lastPacketSummary ?? _state.LastPacketSummary,
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
}
