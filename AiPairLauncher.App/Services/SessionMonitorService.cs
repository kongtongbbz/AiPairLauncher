using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class SessionMonitorService : ISessionMonitorService
{
    private readonly IWezTermService _wezTermService;
    private readonly INotificationService _notificationService;

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

        var refreshedRecords = new List<ManagedSessionRecord>(sessionRecords.Count);
        foreach (var sourceRecord in sessionRecords)
        {
            var record = sourceRecord.Clone();
            var previousStatus = record.HealthStatus;

            var automationState = automationStateResolver(record.SessionId);
            if (automationState is not null)
            {
                ApplyAutomationState(record, automationState);
                MaybeNotify(previousStatus, record);
                refreshedRecords.Add(record);
                continue;
            }

            await RefreshViaWezTermAsync(record, cancellationToken).ConfigureAwait(false);
            MaybeNotify(previousStatus, record);
            refreshedRecords.Add(record);
        }

        return refreshedRecords;
    }

    private async Task RefreshViaWezTermAsync(ManagedSessionRecord record, CancellationToken cancellationToken)
    {
        try
        {
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
                UpdatedAt = DateTimeOffset.Now,
            };

            record.LastSeenAt = DateTimeOffset.Now;
            record.UpdatedAt = DateTimeOffset.Now;
            record.LastError = null;

            var mergedText = string.Join(Environment.NewLine, claudeText, codexText);
            if (ContainsWaitingPrompt(mergedText))
            {
                record.HealthStatus = SessionHealthStatus.Waiting;
                record.HealthDetail = "会话等待人工输入";
                record.LastSummary = "检测到等待确认或权限提示";
                return;
            }

            if (ContainsErrorText(mergedText))
            {
                record.HealthStatus = SessionHealthStatus.Error;
                record.HealthDetail = "会话出现错误输出";
                record.LastSummary = "检测到错误输出";
                record.LastError = ExtractPreviewLine(mergedText) ?? "终端出现错误输出";
                return;
            }

            if (ContainsRunningHint(mergedText))
            {
                record.HealthStatus = SessionHealthStatus.Running;
                record.HealthDetail = "会话正在处理任务";
                record.LastSummary = ExtractPreviewLine(mergedText) ?? "检测到持续输出";
                return;
            }

            record.HealthStatus = SessionHealthStatus.Idle;
            record.HealthDetail = "WezTerm 在线";
            if (string.IsNullOrWhiteSpace(record.LastSummary) || string.Equals(record.LastSummary, "暂无", StringComparison.Ordinal))
            {
                record.LastSummary = "会话可恢复";
            }
        }
        catch (Exception ex)
        {
            record.HealthStatus = SessionHealthStatus.Detached;
            record.HealthDetail = "会话已断开";
            record.LastError = ex.Message;
            record.LastSummary = "需要重新启动";
            record.RuntimeBinding.IsAlive = false;
            record.RuntimeBinding.UpdatedAt = DateTimeOffset.Now;
            record.UpdatedAt = DateTimeOffset.Now;
        }
    }

    private void ApplyAutomationState(ManagedSessionRecord record, AutomationRunState state)
    {
        record.LastSummary = string.IsNullOrWhiteSpace(state.LastPacketSummary) ? record.LastSummary : state.LastPacketSummary;
        record.LastError = state.LastError;
        record.LastSeenAt = DateTimeOffset.Now;
        record.UpdatedAt = DateTimeOffset.Now;
        record.StatusSnapshot.NeedsApproval = state.PendingApproval is not null;
        record.StatusSnapshot.AutomationStageId = state.CurrentStageId;
        record.StatusSnapshot.AutomationRetryCount = state.CurrentStageRetryCount;
        record.StatusSnapshot.ClaudePreview = state.PendingApproval is null
            ? record.StatusSnapshot.ClaudePreview
            : BuildApprovalPreview(state.PendingApproval);

        switch (state.Status)
        {
            case AutomationStageStatus.PendingUserApproval:
                record.HealthStatus = SessionHealthStatus.Waiting;
                break;
            case AutomationStageStatus.PausedOnError:
                record.HealthStatus = SessionHealthStatus.Error;
                break;
            case AutomationStageStatus.Idle:
            case AutomationStageStatus.Stopped:
            case AutomationStageStatus.Completed:
                record.HealthStatus = SessionHealthStatus.Idle;
                break;
            default:
                record.HealthStatus = SessionHealthStatus.Running;
                break;
        }

        record.HealthDetail = string.IsNullOrWhiteSpace(state.StatusDetail)
            ? record.HealthDetail
            : state.StatusDetail;
    }

    private void MaybeNotify(SessionHealthStatus previousStatus, ManagedSessionRecord record)
    {
        if (record.HealthStatus == previousStatus)
        {
            return;
        }

        if (record.HealthStatus is not SessionHealthStatus.Waiting and not SessionHealthStatus.Error)
        {
            return;
        }

        _notificationService.Notify(
            $"AiPair: {record.DisplayName}",
            $"{record.HealthDisplayText} - {record.HealthDetail}");
    }

    private static bool ContainsWaitingPrompt(string text)
    {
        return text.Contains("Yes, I trust this folder", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Quick safety check", StringComparison.OrdinalIgnoreCase)
               || text.Contains("permission", StringComparison.OrdinalIgnoreCase)
               || text.Contains("approve", StringComparison.OrdinalIgnoreCase)
               || text.Contains("confirm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsErrorText(string text)
    {
        return text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Traceback", StringComparison.OrdinalIgnoreCase)
               || text.Contains("error:", StringComparison.OrdinalIgnoreCase)
               || text.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsRunningHint(string text)
    {
        return text.Contains("running", StringComparison.OrdinalIgnoreCase)
               || text.Contains("processing", StringComparison.OrdinalIgnoreCase)
               || text.Contains("thinking", StringComparison.OrdinalIgnoreCase)
               || text.Contains("analyzing", StringComparison.OrdinalIgnoreCase)
               || text.Contains("executing", StringComparison.OrdinalIgnoreCase)
               || text.Contains("applying patch", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPreviewLine(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
    }

    private static string BuildPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "暂无输出";
        }

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(3)
            .ToArray();

        return lines.Length == 0 ? "暂无输出" : string.Join(Environment.NewLine, lines);
    }

    private static string BuildApprovalPreview(ApprovalDraft approvalDraft)
    {
        var title = string.IsNullOrWhiteSpace(approvalDraft.Title) ? "待审批计划" : approvalDraft.Title.Trim();
        var summary = string.IsNullOrWhiteSpace(approvalDraft.Summary) ? "等待人工确认" : approvalDraft.Summary.Trim();
        return $"{title}{Environment.NewLine}{summary}";
    }
}
