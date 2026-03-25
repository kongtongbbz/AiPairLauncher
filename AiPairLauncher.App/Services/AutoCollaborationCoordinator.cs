using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AutoCollaborationCoordinator : IAutoCollaborationCoordinator
{
    private readonly IWezTermService _wezTermService;
    private readonly IAgentPacketParser _packetParser;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, string> _paneSnapshots = new();

    private AutomationRunState _state = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private LauncherSession? _session;
    private AutomationSettings _settings = new();
    private readonly HashSet<string> _processedFingerprints = new(StringComparer.Ordinal);
    private DateTimeOffset _lastProgressAt = DateTimeOffset.Now;

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
            _loopCts = new CancellationTokenSource();

            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.BootstrappingClaude,
                StatusDetail = "正在注入 Claude 领导者提示词",
            });

            var bootstrapPrompt = AutomationPromptFactory.BuildClaudeBootstrapPrompt(session.WorkingDirectory, settings.InitialTaskPrompt);
            await _wezTermService
                .SendAutomationPromptAsync(session, AgentRole.Claude, bootstrapPrompt, settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.WaitingForClaudePlan,
                StatusDetail = "等待 Claude 输出阶段计划",
                LastPacketSummary = "已向 Claude 发送自动模式引导",
            });

            _loopTask = Task.Run(() => PollLoopAsync(_loopCts.Token), CancellationToken.None);
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

            var prompt = AutomationPromptFactory.BuildCodexExecutionPrompt(_state.PendingApproval, userNote);
            await _wezTermService
                .SendAutomationPromptAsync(_session!, AgentRole.Codex, prompt, _settings.SubmitOnSend, cancellationToken)
                .ConfigureAwait(false);

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.WaitingForCodexReport,
                CurrentStageId = _state.PendingApproval.StageId,
                StatusDetail = $"已发送阶段 {_state.PendingApproval.StageId} 给 Codex，等待执行回报",
                LastPacketSummary = $"已批准并发送: {_state.PendingApproval.Title}",
            });
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

            _lastProgressAt = DateTimeOffset.Now;
            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.WaitingForClaudePlan,
                CurrentStageId = _state.PendingApproval.StageId,
                StatusDetail = $"已退回阶段 {_state.PendingApproval.StageId}，等待 Claude 重拟计划",
                LastPacketSummary = $"用户退回: {_state.PendingApproval.Title}",
            });
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

            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.Stopped,
                CurrentStageId = _state.CurrentStageId,
                StatusDetail = "自动编排已停止",
                LastPacketSummary = _state.LastPacketSummary,
            });
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
        packet = NormalizePacketForCurrentState(packet);
        if (_processedFingerprints.Contains(packet.Fingerprint))
        {
            EnsureNotTimedOut(hasPaneProgress);
            return;
        }

        ValidatePacketForState(packet);
        _processedFingerprints.Add(packet.Fingerprint);
        _lastProgressAt = DateTimeOffset.Now;

        switch (_state.Status)
        {
            case AutomationStageStatus.WaitingForClaudePlan:
                ProcessStagePlan(packet);
                break;
            case AutomationStageStatus.WaitingForCodexReport:
                await ProcessExecutionReportAsync(packet, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationStageStatus.WaitingForClaudeReview:
                ProcessReviewDecision(packet);
                break;
        }
    }

    private void ProcessStagePlan(AgentPacket packet)
    {
        UpdateState(new AutomationRunState
        {
            Status = AutomationStageStatus.PendingUserApproval,
            CurrentStageId = packet.StageId,
            StatusDetail = $"收到 Claude 的阶段 {packet.StageId} 计划，等待用户审批",
            LastPacketSummary = packet.PacketSummary,
            PendingApproval = BuildApprovalDraft(packet),
        });
    }

    private async Task ProcessExecutionReportAsync(AgentPacket packet, CancellationToken cancellationToken)
    {
        var prompt = AutomationPromptFactory.BuildClaudeReviewPrompt(packet, _settings.InitialTaskPrompt);
        await _wezTermService
            .SendAutomationPromptAsync(_session!, AgentRole.Claude, prompt, _settings.SubmitOnSend, cancellationToken)
            .ConfigureAwait(false);

        UpdateState(new AutomationRunState
        {
            Status = AutomationStageStatus.WaitingForClaudeReview,
            CurrentStageId = packet.StageId,
            StatusDetail = $"已收到 Codex 的阶段 {packet.StageId} 回报，等待 Claude 审定",
            LastPacketSummary = packet.PacketSummary,
        });
    }

    private void ProcessReviewDecision(AgentPacket packet)
    {
        switch (packet.Decision)
        {
            case ReviewDecision.NextStage:
            case ReviewDecision.RetryStage:
                UpdateState(new AutomationRunState
                {
                    Status = AutomationStageStatus.PendingUserApproval,
                    CurrentStageId = packet.StageId,
                    StatusDetail = $"收到 Claude 的{GetDecisionText(packet.Decision.Value)}，等待用户审批",
                    LastPacketSummary = packet.PacketSummary,
                    PendingApproval = BuildApprovalDraft(packet),
                });
                return;
            case ReviewDecision.Complete:
                UpdateState(new AutomationRunState
                {
                    Status = AutomationStageStatus.Completed,
                    CurrentStageId = packet.StageId,
                    StatusDetail = $"Claude 已判定阶段 {packet.StageId} 完成",
                    LastPacketSummary = packet.PacketSummary,
                });
                return;
            case ReviewDecision.Blocked:
                UpdateState(new AutomationRunState
                {
                    Status = AutomationStageStatus.PausedOnError,
                    CurrentStageId = packet.StageId,
                    StatusDetail = $"Claude 将阶段 {packet.StageId} 标记为阻塞",
                    LastPacketSummary = packet.PacketSummary,
                    LastError = string.IsNullOrWhiteSpace(packet.Body) ? packet.Summary : packet.Body,
                });
                return;
            default:
                throw new InvalidOperationException("review_decision 缺少有效 decision。");
        }
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

    private AgentPacket NormalizePacketForCurrentState(AgentPacket packet)
    {
        if (_state.Status == AutomationStageStatus.WaitingForClaudePlan &&
            !_state.CurrentStageId.HasValue &&
            packet.Role == AgentRole.Claude &&
            packet.Kind == PacketKind.StagePlan &&
            packet.StageId == 0)
        {
            return ClonePacketWithStageId(packet, 1);
        }

        return packet;
    }

    private static AgentPacket ClonePacketWithStageId(AgentPacket packet, int stageId)
    {
        return new AgentPacket
        {
            Role = packet.Role,
            Kind = packet.Kind,
            StageId = stageId,
            Fingerprint = packet.Fingerprint,
            RawText = packet.RawText,
            Title = packet.Title,
            Summary = packet.Summary,
            Scope = packet.Scope,
            Steps = packet.Steps,
            AcceptanceCriteria = packet.AcceptanceCriteria,
            Status = packet.Status,
            Decision = packet.Decision,
            CompletedItems = packet.CompletedItems,
            VerificationItems = packet.VerificationItems,
            Blockers = packet.Blockers,
            ReviewFocus = packet.ReviewFocus,
            Body = packet.Body,
            CodexBrief = packet.CodexBrief,
        };
    }

    private void ValidatePacketForState(AgentPacket packet)
    {
        if (packet.StageId <= 0)
        {
            throw new InvalidOperationException($"检测到非法阶段号: {packet.StageId}。阶段号必须从 1 开始。");
        }

        if (_state.CurrentStageId.HasValue && packet.StageId < _state.CurrentStageId.Value)
        {
            throw new InvalidOperationException($"检测到阶段号回退: 当前阶段 {_state.CurrentStageId.Value}，收到阶段 {packet.StageId}。");
        }

        switch (_state.Status)
        {
            case AutomationStageStatus.WaitingForClaudePlan:
                if (packet.Role != AgentRole.Claude || packet.Kind != PacketKind.StagePlan)
                {
                    throw new InvalidOperationException("当前正在等待 Claude 的 stage_plan，但收到其他结构化包。");
                }

                break;
            case AutomationStageStatus.WaitingForCodexReport:
                if (packet.Role != AgentRole.Codex || packet.Kind != PacketKind.ExecutionReport)
                {
                    throw new InvalidOperationException("当前正在等待 Codex 的 execution_report，但收到其他结构化包。");
                }

                break;
            case AutomationStageStatus.WaitingForClaudeReview:
                if (packet.Role != AgentRole.Claude || packet.Kind != PacketKind.ReviewDecision)
                {
                    throw new InvalidOperationException("当前正在等待 Claude 的 review_decision，但收到其他结构化包。");
                }

                break;
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
        if (string.IsNullOrWhiteSpace(paneText))
        {
            return string.Empty;
        }

        return paneText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
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
            UpdateState(new AutomationRunState
            {
                Status = AutomationStageStatus.PausedOnError,
                CurrentStageId = _state.CurrentStageId,
                StatusDetail = "自动编排已暂停",
                LastPacketSummary = _state.LastPacketSummary,
                LastError = message,
            });
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
    }

    private void EnsureSession()
    {
        if (_session is not null)
        {
            return;
        }

        throw new InvalidOperationException("当前没有可用会话，无法执行自动编排。");
    }

    private static string GetDecisionText(ReviewDecision decision)
    {
        return decision switch
        {
            ReviewDecision.NextStage => "下一阶段计划",
            ReviewDecision.RetryStage => "重试计划",
            ReviewDecision.Complete => "完成结论",
            ReviewDecision.Blocked => "阻塞结论",
            _ => "未知结论",
        };
    }

    private void UpdateState(AutomationRunState nextState)
    {
        _state = new AutomationRunState
        {
            Status = nextState.Status,
            CurrentStageId = nextState.CurrentStageId,
            StatusDetail = nextState.StatusDetail,
            LastPacketSummary = nextState.LastPacketSummary,
            PendingApproval = nextState.PendingApproval,
            LastError = nextState.LastError,
            UpdatedAt = DateTimeOffset.Now,
        };

        StateChanged?.Invoke(this, _state);
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
}
