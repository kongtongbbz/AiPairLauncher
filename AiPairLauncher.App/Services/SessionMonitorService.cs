using System.Diagnostics;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class SessionMonitorService : ISessionMonitorService
{
    private const int BaseProbeSeconds = 8;
    private const int MaxProbeSeconds = 60;
    private const int BackoffStartFailures = 2;
    private static readonly TimeSpan ZombieThreshold = TimeSpan.FromHours(24);

    private readonly IWezTermService _wezTermService;
    private readonly INotificationService _notificationService;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, ProbeState> _states = new(StringComparer.Ordinal);

    public SessionMonitorService(IWezTermService wezTermService, INotificationService notificationService)
    {
        _wezTermService = wezTermService ?? throw new ArgumentNullException(nameof(wezTermService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task<IReadOnlyList<ManagedSessionRecord>> RefreshAsync(
        IReadOnlyList<ManagedSessionRecord> sessionRecords,
        Func<string, AutomationRunState?> automationStateResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionRecords);
        ArgumentNullException.ThrowIfNull(automationStateResolver);

        var now = DateTimeOffset.Now;
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        var refreshed = new List<ManagedSessionRecord>(sessionRecords.Count);

        foreach (var source in sessionRecords)
        {
            var record = source.Clone();
            activeIds.Add(record.SessionId);
            var previousStatus = record.HealthStatus;

            var automationState = automationStateResolver(record.SessionId);
            if (automationState is not null)
            {
                ClearState(record.SessionId);
                ApplyAutomationState(record, automationState, now);
                MaybeNotify(previousStatus, record);
                refreshed.Add(record);
                continue;
            }

            await RefreshViaWezTermAsync(record, now, cancellationToken).ConfigureAwait(false);
            MaybeNotify(previousStatus, record);
            refreshed.Add(record);
        }

        PruneStates(activeIds);
        return refreshed;
    }

    private async Task RefreshViaWezTermAsync(ManagedSessionRecord record, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (TryGetDeferredState(record.SessionId, now, out var deferred))
        {
            ApplyDetachedState(record, deferred, now, deferredMode: true);
            return;
        }

        try
        {
            EnsureGuiAlive(record.Session.GuiPid);

            var panes = await _wezTermService
                .GetWorkspacePanesAsync(record.Session, record.Session.Workspace, cancellationToken)
                .ConfigureAwait(false);
            LauncherSessionValidator.EnsurePaneTopology(record.Session, panes);

            var claudeText = await _wezTermService
                .ReadPaneTextAsync(record.Session, record.Session.LeftPaneId, 40, cancellationToken)
                .ConfigureAwait(false);
            var codexText = await _wezTermService
                .ReadPaneTextAsync(record.Session, record.Session.RightPaneId, 40, cancellationToken)
                .ConfigureAwait(false);

            record.StatusSnapshot.ClaudePreview = BuildPreview(claudeText);
            record.StatusSnapshot.CodexPreview = BuildPreview(codexText);
            record.RuntimeBinding = new SessionRuntimeBinding
            {
                SessionId = record.SessionId,
                GuiPid = record.Session.GuiPid,
                SocketPath = record.Session.SocketPath,
                LeftPaneId = record.Session.LeftPaneId,
                RightPaneId = record.Session.RightPaneId,
                ClaudeObserverPaneId = record.Session.ClaudeObserverPaneId,
                CodexObserverPaneId = record.Session.CodexObserverPaneId,
                IsAlive = true,
                UpdatedAt = now,
            };

            record.LastSeenAt = now;
            record.UpdatedAt = now;
            record.LastError = null;
            record.StatusSnapshot.DisconnectReason = SessionDisconnectReason.None;
            record.StatusSnapshot.DisconnectedAt = null;
            record.StatusSnapshot.CurrentBackoffSeconds = BaseProbeSeconds;
            record.StatusSnapshot.NextHealthProbeAt = null;
            record.StatusSnapshot.ZombieDetected = false;
            record.StatusSnapshot.RecoveryHint = "会话在线，无需恢复操作。";

            var merged = string.Join(Environment.NewLine, claudeText, codexText);
            if (ContainsWaitingPrompt(merged))
            {
                record.HealthStatus = SessionHealthStatus.Waiting;
                record.HealthDetail = "会话等待人工输入";
                record.LastSummary = "检测到等待确认或权限提示";
            }
            else if (ContainsErrorText(merged))
            {
                record.HealthStatus = SessionHealthStatus.Error;
                record.HealthDetail = "会话出现错误输出";
                record.LastSummary = "检测到错误输出";
                record.LastError = ExtractPreviewLine(merged) ?? "终端出现错误输出";
            }
            else if (ContainsRunningHint(merged))
            {
                record.HealthStatus = SessionHealthStatus.Running;
                record.HealthDetail = "会话正在处理任务";
                record.LastSummary = ExtractPreviewLine(merged) ?? "检测到持续输出";
            }
            else
            {
                record.HealthStatus = SessionHealthStatus.Idle;
                record.HealthDetail = "WezTerm 在线";
                if (string.IsNullOrWhiteSpace(record.LastSummary) || string.Equals(record.LastSummary, "暂无", StringComparison.Ordinal))
                {
                    record.LastSummary = "会话可恢复";
                }
            }

            ClearState(record.SessionId);
        }
        catch (Exception ex)
        {
            var reason = ClassifyReason(ex, record.Session.GuiPid);
            var failed = MarkFailure(record, reason, ex.Message, now);
            ApplyDetachedState(record, failed, now, deferredMode: false);
        }
    }

    private static void EnsureGuiAlive(int guiPid)
    {
        if (guiPid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(guiPid);
            if (process.HasExited)
            {
                throw new InvalidOperationException($"WezTerm GUI 进程已退出（PID: {guiPid}）。");
            }
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"WezTerm GUI 进程不存在（PID: {guiPid}）。", ex);
        }
    }

    private void ApplyDetachedState(ManagedSessionRecord record, ProbeState state, DateTimeOffset now, bool deferredMode)
    {
        var zombie = now - state.FirstFailureAt >= ZombieThreshold;
        var remainSeconds = Math.Max(1, (int)Math.Ceiling((state.NextProbeAt - now).TotalSeconds));
        var detail = state.Reason switch
        {
            SessionDisconnectReason.Timeout => $"终端无响应，已降低检测频率（{state.BackoffSeconds}s）",
            SessionDisconnectReason.ProcessExited => "WezTerm 进程已退出",
            SessionDisconnectReason.PermissionDenied => "终端访问被拒绝",
            SessionDisconnectReason.TransportError => $"终端连接异常，已降低检测频率（{state.BackoffSeconds}s）",
            _ => $"会话已断开，等待重试（{state.BackoffSeconds}s）",
        };
        var summary = zombie
            ? "会话已断开超过24小时，建议归档"
            : deferredMode
                ? $"等待下一次健康检测（约 {remainSeconds}s）"
                : state.Reason switch
                {
                    SessionDisconnectReason.Timeout => "终端长时间无响应，建议继续等待或重连",
                    SessionDisconnectReason.ProcessExited => "检测到 WezTerm 进程退出，请重启终端",
                    SessionDisconnectReason.PermissionDenied => "终端权限不足，请检查权限后重试",
                    _ => "会话已断开，请尝试恢复",
                };

        record.HealthStatus = SessionHealthStatus.Detached;
        record.HealthDetail = detail;
        record.LastSummary = summary;
        record.LastError = state.LastError;
        record.RuntimeBinding.IsAlive = false;
        record.RuntimeBinding.UpdatedAt = now;
        record.UpdatedAt = now;
        record.StatusSnapshot.DisconnectReason = state.Reason;
        record.StatusSnapshot.DisconnectedAt = state.FirstFailureAt;
        record.StatusSnapshot.CurrentBackoffSeconds = state.BackoffSeconds;
        record.StatusSnapshot.NextHealthProbeAt = state.NextProbeAt;
        record.StatusSnapshot.ZombieDetected = zombie;
        record.StatusSnapshot.RecoveryHint = zombie
            ? "会话已长时间断开，建议归档；如需继续可先重启终端并恢复会话。"
            : state.Reason switch
            {
                SessionDisconnectReason.Timeout => "终端无响应，可继续等待下一轮检测，或点击“重连会话”。",
                SessionDisconnectReason.ProcessExited => "WezTerm 已退出，建议“重启终端并恢复会话”。",
                SessionDisconnectReason.PermissionDenied => "检测受权限限制，请确认目录信任和终端权限后再重试。",
                _ => "可尝试“刷新会话”或“重连会话”恢复运行。",
            };
    }

    private static SessionDisconnectReason ClassifyReason(Exception ex, int guiPid)
    {
        if (ex is TimeoutException)
        {
            return SessionDisconnectReason.Timeout;
        }

        if (ex is UnauthorizedAccessException)
        {
            return SessionDisconnectReason.PermissionDenied;
        }

        var message = ex.Message;
        if (ContainsAny(message, "进程已退出", "进程不存在", "PID"))
        {
            return SessionDisconnectReason.ProcessExited;
        }

        if (guiPid > 0 && !TryCheckGuiProcess(guiPid))
        {
            return SessionDisconnectReason.ProcessExited;
        }

        if (ContainsAny(message, "access is denied", "permission denied", "拒绝访问", "权限"))
        {
            return SessionDisconnectReason.PermissionDenied;
        }

        if (ContainsAny(message, "timeout", "超时"))
        {
            return SessionDisconnectReason.Timeout;
        }

        if (ContainsAny(message, "socket", "pipe", "pane", "连接", "transport"))
        {
            return SessionDisconnectReason.TransportError;
        }

        return SessionDisconnectReason.Unknown;
    }

    private static bool TryCheckGuiProcess(int guiPid)
    {
        try
        {
            using var process = Process.GetProcessById(guiPid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAny(string source, params string[] keys)
    {
        return keys.Any(key => source.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    private ProbeState MarkFailure(ManagedSessionRecord record, SessionDisconnectReason reason, string error, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(record.SessionId, out var state))
            {
                state = new ProbeState(record.StatusSnapshot.DisconnectedAt ?? now);
                _states[record.SessionId] = state;
            }

            state.Failures += 1;
            state.Reason = reason;
            state.LastError = error;
            state.BackoffSeconds = ComputeBackoffSeconds(state.Failures);
            state.NextProbeAt = now.AddSeconds(state.BackoffSeconds);
            if (state.FirstFailureAt > now)
            {
                state.FirstFailureAt = now;
            }

            return state.Clone();
        }
    }

    private bool TryGetDeferredState(string sessionId, DateTimeOffset now, out ProbeState state)
    {
        lock (_stateLock)
        {
            if (_states.TryGetValue(sessionId, out var current) &&
                current.Failures >= BackoffStartFailures &&
                current.NextProbeAt > now)
            {
                state = current.Clone();
                return true;
            }
        }

        state = ProbeState.Empty;
        return false;
    }

    private static int ComputeBackoffSeconds(int failures)
    {
        if (failures < BackoffStartFailures)
        {
            return BaseProbeSeconds;
        }

        var shift = Math.Max(0, failures - 1);
        var candidate = BaseProbeSeconds * (1 << Math.Min(shift, 8));
        return Math.Min(MaxProbeSeconds, candidate);
    }

    private void ClearState(string sessionId)
    {
        lock (_stateLock)
        {
            _states.Remove(sessionId);
        }
    }

    private void PruneStates(IReadOnlySet<string> activeIds)
    {
        lock (_stateLock)
        {
            var stale = _states.Keys.Where(id => !activeIds.Contains(id)).ToArray();
            foreach (var id in stale)
            {
                _states.Remove(id);
            }
        }
    }

    private void ApplyAutomationState(ManagedSessionRecord record, AutomationRunState state, DateTimeOffset now)
    {
        record.LastSummary = string.IsNullOrWhiteSpace(state.LastPacketSummary) ? record.LastSummary : state.LastPacketSummary;
        record.LastError = state.LastError;
        record.LastSeenAt = now;
        record.UpdatedAt = now;
        record.StatusSnapshot.NeedsApproval = state.PendingApproval is not null;
        record.StatusSnapshot.AutomationPhase = state.Phase;
        record.StatusSnapshot.AutomationStageId = state.CurrentStageId;
        record.StatusSnapshot.AutomationTaskRef = state.CurrentTaskRef;
        record.StatusSnapshot.TaskMdPath = state.TaskMdPath;
        record.StatusSnapshot.TaskMdStatus = state.TaskMdStatus;
        record.StatusSnapshot.AutomationRetryCount = state.CurrentStageRetryCount;
        record.StatusSnapshot.ClaudePreview = state.PendingApproval is null ? "暂无输出" : BuildApprovalPreview(state.PendingApproval);
        record.StatusSnapshot.DisconnectReason = SessionDisconnectReason.None;
        record.StatusSnapshot.DisconnectedAt = null;
        record.StatusSnapshot.CurrentBackoffSeconds = BaseProbeSeconds;
        record.StatusSnapshot.NextHealthProbeAt = null;
        record.StatusSnapshot.ZombieDetected = false;
        record.StatusSnapshot.RecoveryHint = "自动编排运行中，无需恢复操作。";

        record.HealthStatus = state.Status switch
        {
            AutomationStageStatus.PendingUserApproval => SessionHealthStatus.Waiting,
            AutomationStageStatus.PausedOnError => SessionHealthStatus.Error,
            AutomationStageStatus.Idle or AutomationStageStatus.Stopped or AutomationStageStatus.Completed => SessionHealthStatus.Idle,
            _ => SessionHealthStatus.Running,
        };
        record.HealthDetail = string.IsNullOrWhiteSpace(state.StatusDetail) ? record.HealthDetail : BuildPhaseAwareDetail(state);
    }

    private void MaybeNotify(SessionHealthStatus previousStatus, ManagedSessionRecord record)
    {
        if (record.HealthStatus == previousStatus)
        {
            return;
        }

        if (record.HealthStatus is not SessionHealthStatus.Waiting and not SessionHealthStatus.Error and not SessionHealthStatus.Detached)
        {
            return;
        }

        _notificationService.Notify($"AiPair: {record.DisplayName}", $"{record.HealthDisplayText} - {record.HealthDetail}");
    }

    private static bool ContainsWaitingPrompt(string text) =>
        text.Contains("Yes, I trust this folder", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Quick safety check", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("approve", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("confirm", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsErrorText(string text) =>
        text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRunningHint(string text) =>
        text.Contains("running", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("processing", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("analyzing", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("executing", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("applying patch", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractPreviewLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));

    private static string BuildPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "暂无输出";
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(3);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildApprovalPreview(ApprovalDraft approvalDraft)
    {
        var title = string.IsNullOrWhiteSpace(approvalDraft.Title) ? "待审批计划" : approvalDraft.Title.Trim();
        var summary = string.IsNullOrWhiteSpace(approvalDraft.Summary) ? "等待人工确认" : approvalDraft.Summary.Trim();
        return $"{title}{Environment.NewLine}{summary}";
    }

    private static string BuildPhaseAwareDetail(AutomationRunState state)
    {
        var prefix = state.Phase switch
        {
            AutomationPhase.Phase1Research => "Phase 1",
            AutomationPhase.Phase2Planning => "Phase 2",
            AutomationPhase.Phase3Execution => "Phase 3",
            AutomationPhase.Phase4Review => "Phase 4",
            _ => null,
        };
        return string.IsNullOrWhiteSpace(prefix) ? state.StatusDetail : $"{prefix} · {state.StatusDetail}";
    }

    private sealed class ProbeState
    {
        public static ProbeState Empty { get; } = new(DateTimeOffset.MinValue)
        {
            Failures = 0,
            BackoffSeconds = BaseProbeSeconds,
            LastError = string.Empty,
            Reason = SessionDisconnectReason.None,
            NextProbeAt = DateTimeOffset.MinValue,
        };

        public ProbeState(DateTimeOffset firstFailureAt)
        {
            FirstFailureAt = firstFailureAt;
            BackoffSeconds = BaseProbeSeconds;
            NextProbeAt = DateTimeOffset.Now.AddSeconds(BaseProbeSeconds);
        }

        public int Failures { get; set; }
        public int BackoffSeconds { get; set; }
        public SessionDisconnectReason Reason { get; set; } = SessionDisconnectReason.None;
        public string LastError { get; set; } = string.Empty;
        public DateTimeOffset FirstFailureAt { get; set; }
        public DateTimeOffset NextProbeAt { get; set; }

        public ProbeState Clone()
        {
            return new ProbeState(FirstFailureAt)
            {
                Failures = Failures,
                BackoffSeconds = BackoffSeconds,
                Reason = Reason,
                LastError = LastError,
                NextProbeAt = NextProbeAt,
            };
        }
    }
}

