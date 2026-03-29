using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<ModeOption> ClaudeModes =
    [
        new() { Value = "default", Label = "Default Mode" },
        new() { Value = "plan", Label = "Plan Mode" },
    ];

    private static readonly IReadOnlyList<ModeOption> CodexModes =
    [
        new() { Value = "standard", Label = "Standard" },
        new() { Value = "full-auto", Label = "Full Auto" },
        new() { Value = "never-ask", Label = "Never Ask" },
    ];

    private static readonly IReadOnlyList<ModeOption> AutomationAdvanceModes =
    [
        new() { Value = "manual-each-stage", Label = "逐轮审批" },
        new() { Value = "manual-first-then-auto", Label = "首轮审批后自动" },
        new() { Value = "full-auto-loop", Label = "全自动闭环" },
    ];

    private static readonly IReadOnlyList<ModeOption> AutomationExecutorModes =
    [
        new() { Value = "claude", Label = "Claude" },
        new() { Value = "codex", Label = "CODEX" },
    ];

    private static readonly IReadOnlyList<ModeOption> AutomationParallelismModes =
    [
        new() { Value = "auto", Label = "自动" },
        new() { Value = "balanced", Label = "均衡" },
        new() { Value = "conservative", Label = "保守" },
        new() { Value = "aggressive", Label = "激进" },
    ];

    private static readonly IReadOnlyList<ModeOption> AutomationTemplateModes =
    [
        new() { Value = "feature", Label = "功能开发" },
        new() { Value = "bugfix", Label = "缺陷修复" },
        new() { Value = "refactor", Label = "重构与验证" },
        new() { Value = "research", Label = "只读调研" },
    ];

    private static readonly IReadOnlyList<ModeOption> WorktreeStrategies =
    [
        new() { Value = "none", Label = "禁用" },
        new() { Value = "subdirectory", Label = "仓库内 .worktrees/" },
    ];

    private static readonly IReadOnlyList<ModeOption> ThemeModes =
    [
        new() { Value = "dark", Label = "黑色" },
        new() { Value = "light", Label = "白色" },
        new() { Value = "eye-care", Label = "护眼" },
    ];

    private static readonly IReadOnlyList<ModeOption> SessionStatusFilters =
    [
        new() { Value = "all", Label = "全部状态" },
        new() { Value = "running", Label = "运行中" },
        new() { Value = "waiting", Label = "等待中" },
        new() { Value = "idle", Label = "空闲" },
        new() { Value = "error", Label = "错误" },
        new() { Value = "detached", Label = "已断开" },
    ];

    private static readonly IReadOnlyList<ModeOption> PanelPresetModes =
    [
        new() { Value = "balanced", Label = "平衡布局" },
        new() { Value = "compose", Label = "启动优先" },
        new() { Value = "automation", Label = "自动编排优先" },
    ];

    private static readonly string AppVersionValue = ResolveAppVersion();

    private readonly StringBuilder _logBuilder = new();
    private readonly List<ManagedSessionRecord> _allSessionRecords = [];

    private ManagedSessionRecord? _selectedSessionRecord;
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _workspaceName = "aipair-main";
    private string _transferInstruction = "请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。";
    private string _claudePermissionMode = "default";
    private string _codexMode = "standard";
    private string _savedClaudePermissionMode = "default";
    private string _savedCodexMode = "standard";
    private string _sessionWorkspace = "暂无";
    private string _sessionCreatedAt = "暂无";
    private string _sessionPaneInfo = "暂无";
    private string _sessionSocketPath = "暂无";
    private string _statusMessage = "就绪";
    private string _footerMessage = "等待操作。";
    private string _logText = "日志初始化完成。";
    private string _automationStatusLabel = "Idle";
    private string _automationPhaseLabel = "暂无";
    private string _automationStatusDetail = "自动编排未启动。";
    private string _automationLastPacketSummary = "暂无";
    private string _automationLastError = "暂无";
    private string _automationUpdatedAt = "暂无";
    private string _automationTaskMdPath = "暂无";
    private string _automationTaskMdStatus = "暂无";
    private string _automationTaskProgressSummary = "暂无";
    private string _automationCurrentTaskRef = "暂无";
    private string _automationTaskPrompt = "请围绕当前工作目录中的项目需求推进开发，先拆成可审批的阶段计划，再驱动执行器执行与验证，直到完成。";
    private string _automationAdvancePolicy = "full-auto-loop";
    private string _automationPhase1Executor = "claude";
    private string _automationPhase2Executor = "claude";
    private string _automationPhase3Executor = "codex";
    private string _automationPhase4Executor = "claude";
    private string _automationParallelismPolicy = "auto";
    private int _automationMaxParallelSubagents = 4;
    private string _automationTemplateKey = "feature";
    private string _automationActiveExecutorLabel = "暂无";
    private string _automationParallelGroupSummary = "暂无";
    private string _automationHistoryReplayHint = "暂无";
    private string _automationAutoAdvanceStatusText = "未启用";
    private string _automationInterventionReason = "暂无";
    private string _approvalNote = string.Empty;
    private string _pendingApprovalStage = "暂无";
    private string _pendingApprovalTitle = "暂无";
    private string _pendingApprovalSummary = "暂无";
    private string _pendingApprovalScope = "暂无";
    private string _pendingApprovalSteps = "暂无";
    private string _pendingApprovalAcceptance = "暂无";
    private string _pendingApprovalCodexBrief = "暂无";
    private string _pendingApprovalExecutorLabel = "暂无";
    private string _sessionSearchText = string.Empty;
    private string _selectedSessionStatusFilter = "all";
    private string _selectedSessionGroupFilter = "__all__";
    private string _sessionCounterSummary = "暂无会话";
    private string _pendingQueueSummary = "暂无待处理";
    private string _sessionGroupName = "默认";
    private string _selectedSessionDisplayName = "暂无";
    private string _selectedSessionStatus = "未选择";
    private string _selectedSessionStatusDetail = "从左侧选择一个会话";
    private string _selectedSessionWorkingDirectory = "暂无";
    private string _selectedSessionLastSeen = "暂无";
    private string _selectedSessionLastSummary = "暂无";
    private string _selectedSessionModeSummary = "暂无";
    private string _selectedSessionLastError = "暂无";
    private string _selectedSessionRuntimeSummary = "暂无";
    private string _selectedSessionBadgeSummary = "暂无";
    private string _selectedSessionWorktreeSummary = "暂无";
    private string _selectedSessionPendingSummary = "暂无";
    private string _selectedSessionClaudePreview = "暂无输出";
    private string _selectedSessionCodexPreview = "暂无输出";
    private string _selectedSessionLaunchProfileSummary = "未使用模板";
    private string _selectedSessionReconnectSummary = "尚未重连";
    private string _editableSessionDisplayName = string.Empty;
    private string _editableSessionGroupName = "默认";
    private string _lastTransferSummary = "暂无";
    private string _lastTransferFailure = "暂无";
    private string _selectedLaunchProfileId = string.Empty;
    private string _newLaunchProfileName = string.Empty;
    private string _launchProfileSummary = "使用右侧表单配置创建会话";
    private string _selectedThemeMode = "light";
    private string _themeSummary = "当前主题：白色";
    private string _panelPreset = "balanced";
    private string _worktreeStrategy = "none";
    private int _rightPanePercent = 60;
    private int _lastLines = 120;
    private int _automationPollIntervalMilliseconds = 1500;
    private int _automationCaptureLines = 220;
    private int _automationTimeoutSeconds = 600;
    private int _automationMaxAutoStages = 8;
    private int _automationMaxRetryPerStage = 2;
    private int _automationAutoApprovedStageCount;
    private int _automationCurrentStageRetryCount;
    private bool _submitAfterSend = true;
    private bool _automationSubmitOnSend = true;
    private bool _automationObserverEnabled = true;
    private bool _autoModeEnabled;
    private bool _useWorktree;
    private bool _showArchivedSessions;
    private bool _isLaunchPanelExpanded = true;
    private bool _isAutomationPanelExpanded = true;
    private bool _isEnvironmentPanelExpanded;
    private bool _isBusy;
    private bool _hasSession;
    private bool _hasPendingApproval;
    private bool _isAutomationActive;

    public MainWindowViewModel()
    {
        if (!Directory.Exists(_workingDirectory))
        {
            _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        ApplyPanelPreset(_panelPreset);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public ObservableCollection<ManagedSessionRecord> SessionRecords { get; } = [];

    public ObservableCollection<LaunchProfile> LaunchProfiles { get; } = [];

    public ObservableCollection<ModeOption> SessionGroupOptions { get; } =
    [
        new ModeOption { Value = "__all__", Label = "全部分组" },
    ];

    public ObservableCollection<ManagedSessionRecord> PendingSessionRecords { get; } = [];

    public ObservableCollection<AutomationEventRecord> AutomationHistory { get; } = [];

    public ObservableCollection<TaskMdRevisionRecord> AutomationTaskMdRevisions { get; } = [];

    public string AppVersionText => $"版本 {AppVersionValue}";

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    public string WorkspaceName
    {
        get => _workspaceName;
        set => SetField(ref _workspaceName, value);
    }

    public string ClaudePermissionMode
    {
        get => _claudePermissionMode;
        set
        {
            var normalized = NormalizeClaudePermissionMode(value);
            if (!SetField(ref _claudePermissionMode, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(ClaudeModeDisplayText));
        }
    }

    public string CodexMode
    {
        get => _codexMode;
        set
        {
            var normalized = NormalizeCodexMode(value);
            if (!SetField(ref _codexMode, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(CodexModeDisplayText));
        }
    }

    public bool AutoModeEnabled
    {
        get => _autoModeEnabled;
        set
        {
            if (!SetField(ref _autoModeEnabled, value))
            {
                return;
            }

            ApplyModeLock(value);
            OnPropertyChanged(nameof(AreModeSelectorsEnabled));
            OnPropertyChanged(nameof(ModeLockHint));
            OnPropertyChanged(nameof(CanStartAutomation));
        }
    }

    public string SessionSearchText
    {
        get => _sessionSearchText;
        set
        {
            if (!SetField(ref _sessionSearchText, value))
            {
                return;
            }

            ApplySessionFilter();
        }
    }

    public string SelectedSessionStatusFilter
    {
        get => _selectedSessionStatusFilter;
        set
        {
            var normalized = NormalizeSessionStatusFilter(value);
            if (!SetField(ref _selectedSessionStatusFilter, normalized))
            {
                return;
            }

            ApplySessionFilter();
        }
    }

    public string SelectedSessionGroupFilter
    {
        get => _selectedSessionGroupFilter;
        set
        {
            if (!SetField(ref _selectedSessionGroupFilter, string.IsNullOrWhiteSpace(value) ? "__all__" : value))
            {
                return;
            }

            ApplySessionFilter();
        }
    }

    public bool ShowArchivedSessions
    {
        get => _showArchivedSessions;
        set
        {
            if (!SetField(ref _showArchivedSessions, value))
            {
                return;
            }

            ApplySessionFilter();
        }
    }

    public string SessionCounterSummary
    {
        get => _sessionCounterSummary;
        private set => SetField(ref _sessionCounterSummary, value);
    }

    public string PendingQueueSummary
    {
        get => _pendingQueueSummary;
        private set => SetField(ref _pendingQueueSummary, value);
    }

    public string SessionGroupName
    {
        get => _sessionGroupName;
        set => SetField(ref _sessionGroupName, string.IsNullOrWhiteSpace(value) ? "默认" : value.Trim());
    }

    public ManagedSessionRecord? SelectedSessionRecord
    {
        get => _selectedSessionRecord;
        set
        {
            if (!SetField(ref _selectedSessionRecord, value))
            {
                return;
            }

            ApplySelectedSessionRecord(value);
            OnPropertyChanged(nameof(HasSelectedSession));
            OnPropertyChanged(nameof(CanSaveSessionMetadata));
            OnPropertyChanged(nameof(CanArchiveSelectedSession));
            OnPropertyChanged(nameof(CanRestoreSelectedSession));
            OnPropertyChanged(nameof(CanTogglePinSelectedSession));
            OnPropertyChanged(nameof(CanCopySelectedSessionConfig));
        }
    }

    public string SelectedLaunchProfileId
    {
        get => _selectedLaunchProfileId;
        set
        {
            if (!SetField(ref _selectedLaunchProfileId, value))
            {
                return;
            }

            UpdateLaunchProfileSummary();
            OnPropertyChanged(nameof(CanApplyLaunchProfile));
        }
    }

    public string NewLaunchProfileName
    {
        get => _newLaunchProfileName;
        set
        {
            if (!SetField(ref _newLaunchProfileName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSaveLaunchProfile));
        }
    }

    public string LaunchProfileSummary
    {
        get => _launchProfileSummary;
        private set => SetField(ref _launchProfileSummary, value);
    }

    public string SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            var normalized = NormalizeThemeMode(value);
            if (!SetField(ref _selectedThemeMode, normalized))
            {
                return;
            }

            ThemeSummary = normalized switch
            {
                "light" => "当前主题：白色",
                "eye-care" => "当前主题：护眼",
                _ => "当前主题：黑色",
            };
        }
    }

    public string ThemeSummary
    {
        get => _themeSummary;
        private set => SetField(ref _themeSummary, value);
    }

    public string PanelPreset
    {
        get => _panelPreset;
        set
        {
            var normalized = NormalizePanelPreset(value);
            if (!SetField(ref _panelPreset, normalized))
            {
                return;
            }

            ApplyPanelPreset(normalized);
        }
    }

    public bool UseWorktree
    {
        get => _useWorktree;
        set => SetField(ref _useWorktree, value);
    }

    public string WorktreeStrategy
    {
        get => _worktreeStrategy;
        set => SetField(ref _worktreeStrategy, NormalizeWorktreeStrategy(value));
    }

    public int RightPanePercent
    {
        get => _rightPanePercent;
        set => SetField(ref _rightPanePercent, Math.Clamp(value, 10, 90));
    }

    public int LastLines
    {
        get => _lastLines;
        set => SetField(ref _lastLines, Math.Max(10, value));
    }

    public int AutomationPollIntervalMilliseconds
    {
        get => _automationPollIntervalMilliseconds;
        set => SetField(ref _automationPollIntervalMilliseconds, Math.Max(200, value));
    }

    public int AutomationCaptureLines
    {
        get => _automationCaptureLines;
        set => SetField(ref _automationCaptureLines, Math.Max(20, value));
    }

    public int AutomationTimeoutSeconds
    {
        get => _automationTimeoutSeconds;
        set => SetField(ref _automationTimeoutSeconds, Math.Max(30, value));
    }

    public string SelectedAutomationAdvancePolicyKey
    {
        get => _automationAdvancePolicy;
        set => SetField(ref _automationAdvancePolicy, NormalizeAutomationAdvancePolicy(value));
    }

    public string SelectedPhase1ExecutorKey
    {
        get => _automationPhase1Executor;
        set
        {
            var normalized = NormalizeExecutorKey(value);
            if (!SetField(ref _automationPhase1Executor, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(AutomationPhaseExecutorSummary));
        }
    }

    public string SelectedPhase2ExecutorKey
    {
        get => _automationPhase2Executor;
        set
        {
            var normalized = NormalizeExecutorKey(value);
            if (!SetField(ref _automationPhase2Executor, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(AutomationPhaseExecutorSummary));
        }
    }

    public string SelectedPhase3ExecutorKey
    {
        get => _automationPhase3Executor;
        set
        {
            var normalized = NormalizeExecutorKey(value);
            if (!SetField(ref _automationPhase3Executor, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(AutomationPhaseExecutorSummary));
        }
    }

    public string SelectedPhase4ExecutorKey
    {
        get => _automationPhase4Executor;
        set
        {
            var normalized = NormalizeExecutorKey(value);
            if (!SetField(ref _automationPhase4Executor, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(AutomationPhaseExecutorSummary));
        }
    }

    public string SelectedAutomationParallelismPolicyKey
    {
        get => _automationParallelismPolicy;
        set
        {
            var normalized = NormalizeAutomationParallelismPolicy(value);
            if (!SetField(ref _automationParallelismPolicy, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(AutomationParallelismPolicyLabel));
        }
    }

    public int AutomationMaxParallelSubagents
    {
        get => _automationMaxParallelSubagents;
        set => SetField(ref _automationMaxParallelSubagents, Math.Max(1, value));
    }

    public string SelectedAutomationTemplateKey
    {
        get => _automationTemplateKey;
        set
        {
            var normalized = NormalizeAutomationTemplateKey(value);
            if (!SetField(ref _automationTemplateKey, normalized))
            {
                return;
            }

            AutomationTaskPrompt = BuildAutomationTemplatePrompt(normalized);
        }
    }

    public int AutomationMaxAutoStages
    {
        get => _automationMaxAutoStages;
        set => SetField(ref _automationMaxAutoStages, Math.Max(1, value));
    }

    public int AutomationMaxRetryPerStage
    {
        get => _automationMaxRetryPerStage;
        set => SetField(ref _automationMaxRetryPerStage, Math.Max(0, value));
    }

    public string TransferInstruction
    {
        get => _transferInstruction;
        set => SetField(ref _transferInstruction, value);
    }

    public bool SubmitAfterSend
    {
        get => _submitAfterSend;
        set => SetField(ref _submitAfterSend, value);
    }

    public bool AutomationSubmitOnSend
    {
        get => _automationSubmitOnSend;
        set => SetField(ref _automationSubmitOnSend, value);
    }

    public bool AutomationObserverEnabled
    {
        get => _automationObserverEnabled;
        set => SetField(ref _automationObserverEnabled, value);
    }

    public bool IsLaunchPanelExpanded
    {
        get => _isLaunchPanelExpanded;
        set => SetField(ref _isLaunchPanelExpanded, value);
    }

    public bool IsAutomationPanelExpanded
    {
        get => _isAutomationPanelExpanded;
        set => SetField(ref _isAutomationPanelExpanded, value);
    }

    public bool IsEnvironmentPanelExpanded
    {
        get => _isEnvironmentPanelExpanded;
        set => SetField(ref _isEnvironmentPanelExpanded, value);
    }

    public string SessionWorkspace
    {
        get => _sessionWorkspace;
        private set => SetField(ref _sessionWorkspace, value);
    }

    public string SessionCreatedAt
    {
        get => _sessionCreatedAt;
        private set => SetField(ref _sessionCreatedAt, value);
    }

    public string SessionPaneInfo
    {
        get => _sessionPaneInfo;
        private set => SetField(ref _sessionPaneInfo, value);
    }

    public string SessionSocketPath
    {
        get => _sessionSocketPath;
        private set => SetField(ref _sessionSocketPath, value);
    }

    public string SelectedSessionDisplayName
    {
        get => _selectedSessionDisplayName;
        private set => SetField(ref _selectedSessionDisplayName, value);
    }

    public string SelectedSessionStatus
    {
        get => _selectedSessionStatus;
        private set => SetField(ref _selectedSessionStatus, value);
    }

    public string SelectedSessionStatusDetail
    {
        get => _selectedSessionStatusDetail;
        private set => SetField(ref _selectedSessionStatusDetail, value);
    }

    public string SelectedSessionWorkingDirectory
    {
        get => _selectedSessionWorkingDirectory;
        private set => SetField(ref _selectedSessionWorkingDirectory, value);
    }

    public string SelectedSessionLastSeen
    {
        get => _selectedSessionLastSeen;
        private set => SetField(ref _selectedSessionLastSeen, value);
    }

    public string SelectedSessionLastSummary
    {
        get => _selectedSessionLastSummary;
        private set => SetField(ref _selectedSessionLastSummary, value);
    }

    public string SelectedSessionModeSummary
    {
        get => _selectedSessionModeSummary;
        private set => SetField(ref _selectedSessionModeSummary, value);
    }

    public string SelectedSessionLastError
    {
        get => _selectedSessionLastError;
        private set => SetField(ref _selectedSessionLastError, value);
    }

    public string SelectedSessionRuntimeSummary
    {
        get => _selectedSessionRuntimeSummary;
        private set => SetField(ref _selectedSessionRuntimeSummary, value);
    }

    public string SelectedSessionBadgeSummary
    {
        get => _selectedSessionBadgeSummary;
        private set => SetField(ref _selectedSessionBadgeSummary, value);
    }

    public string SelectedSessionWorktreeSummary
    {
        get => _selectedSessionWorktreeSummary;
        private set => SetField(ref _selectedSessionWorktreeSummary, value);
    }

    public string SelectedSessionPendingSummary
    {
        get => _selectedSessionPendingSummary;
        private set => SetField(ref _selectedSessionPendingSummary, value);
    }

    public string SelectedSessionClaudePreview
    {
        get => _selectedSessionClaudePreview;
        private set => SetField(ref _selectedSessionClaudePreview, value);
    }

    public string SelectedSessionCodexPreview
    {
        get => _selectedSessionCodexPreview;
        private set => SetField(ref _selectedSessionCodexPreview, value);
    }

    public string SelectedSessionLaunchProfileSummary
    {
        get => _selectedSessionLaunchProfileSummary;
        private set => SetField(ref _selectedSessionLaunchProfileSummary, value);
    }

    public string SelectedSessionReconnectSummary
    {
        get => _selectedSessionReconnectSummary;
        set => SetField(ref _selectedSessionReconnectSummary, value);
    }

    public string EditableSessionDisplayName
    {
        get => _editableSessionDisplayName;
        set
        {
            if (!SetField(ref _editableSessionDisplayName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSaveSessionMetadata));
        }
    }

    public string EditableSessionGroupName
    {
        get => _editableSessionGroupName;
        set
        {
            if (!SetField(ref _editableSessionGroupName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSaveSessionMetadata));
        }
    }

    public string LastTransferSummary
    {
        get => _lastTransferSummary;
        private set => SetField(ref _lastTransferSummary, value);
    }

    public string LastTransferFailure
    {
        get => _lastTransferFailure;
        private set => SetField(ref _lastTransferFailure, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string FooterMessage
    {
        get => _footerMessage;
        set => SetField(ref _footerMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    public string AutomationStatusLabel
    {
        get => _automationStatusLabel;
        private set => SetField(ref _automationStatusLabel, value);
    }

    public string AutomationPhaseLabel
    {
        get => _automationPhaseLabel;
        private set => SetField(ref _automationPhaseLabel, value);
    }

    public string AutomationStatusDetail
    {
        get => _automationStatusDetail;
        private set => SetField(ref _automationStatusDetail, value);
    }

    public string AutomationActiveExecutorLabel
    {
        get => _automationActiveExecutorLabel;
        private set => SetField(ref _automationActiveExecutorLabel, value);
    }

    public string AutomationParallelismPolicyLabel => ResolveParallelismLabel(_automationParallelismPolicy);

    public string AutomationParallelGroupSummary
    {
        get => _automationParallelGroupSummary;
        private set => SetField(ref _automationParallelGroupSummary, value);
    }

    public string AutomationHistoryReplayHint
    {
        get => _automationHistoryReplayHint;
        private set => SetField(ref _automationHistoryReplayHint, value);
    }

    public string AutomationLastPacketSummary
    {
        get => _automationLastPacketSummary;
        private set => SetField(ref _automationLastPacketSummary, value);
    }

    public string AutomationLastError
    {
        get => _automationLastError;
        private set => SetField(ref _automationLastError, value);
    }

    public string AutomationUpdatedAt
    {
        get => _automationUpdatedAt;
        private set => SetField(ref _automationUpdatedAt, value);
    }

    public string AutomationTaskMdPath
    {
        get => _automationTaskMdPath;
        private set => SetField(ref _automationTaskMdPath, value);
    }

    public string AutomationTaskMdStatus
    {
        get => _automationTaskMdStatus;
        private set => SetField(ref _automationTaskMdStatus, value);
    }

    public string AutomationTaskProgressSummary
    {
        get => _automationTaskProgressSummary;
        private set => SetField(ref _automationTaskProgressSummary, value);
    }

    public string AutomationCurrentTaskRef
    {
        get => _automationCurrentTaskRef;
        private set => SetField(ref _automationCurrentTaskRef, value);
    }

    public string AutomationAutoAdvanceStatusText
    {
        get => _automationAutoAdvanceStatusText;
        private set => SetField(ref _automationAutoAdvanceStatusText, value);
    }

    public int AutomationAutoApprovedStageCount
    {
        get => _automationAutoApprovedStageCount;
        private set => SetField(ref _automationAutoApprovedStageCount, value);
    }

    public int AutomationCurrentStageRetryCount
    {
        get => _automationCurrentStageRetryCount;
        private set => SetField(ref _automationCurrentStageRetryCount, value);
    }

    public string AutomationInterventionReason
    {
        get => _automationInterventionReason;
        private set => SetField(ref _automationInterventionReason, value);
    }

    public string ApprovalNote
    {
        get => _approvalNote;
        set => SetField(ref _approvalNote, value);
    }

    public string AutomationTaskPrompt
    {
        get => _automationTaskPrompt;
        set => SetField(ref _automationTaskPrompt, value);
    }

    public string PendingApprovalStage
    {
        get => _pendingApprovalStage;
        set => SetField(ref _pendingApprovalStage, value);
    }

    public string PendingApprovalTitle
    {
        get => _pendingApprovalTitle;
        set => SetField(ref _pendingApprovalTitle, value);
    }

    public string PendingApprovalSummary
    {
        get => _pendingApprovalSummary;
        set => SetField(ref _pendingApprovalSummary, value);
    }

    public string PendingApprovalScope
    {
        get => _pendingApprovalScope;
        set => SetField(ref _pendingApprovalScope, value);
    }

    public string PendingApprovalSteps
    {
        get => _pendingApprovalSteps;
        set => SetField(ref _pendingApprovalSteps, value);
    }

    public string PendingApprovalAcceptance
    {
        get => _pendingApprovalAcceptance;
        set => SetField(ref _pendingApprovalAcceptance, value);
    }

    public string PendingApprovalCodexBrief
    {
        get => _pendingApprovalCodexBrief;
        set => SetField(ref _pendingApprovalCodexBrief, value);
    }

    public string PendingApprovalExecutorLabel
    {
        get => _pendingApprovalExecutorLabel;
        private set => SetField(ref _pendingApprovalExecutorLabel, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(AreModeSelectorsEnabled));
            OnPropertyChanged(nameof(CanStartAutomation));
            OnPropertyChanged(nameof(CanApproveAutomation));
            OnPropertyChanged(nameof(CanRejectAutomation));
            OnPropertyChanged(nameof(CanStopAutomation));
            OnPropertyChanged(nameof(CanSaveSessionMetadata));
            OnPropertyChanged(nameof(CanArchiveSelectedSession));
            OnPropertyChanged(nameof(CanRestoreSelectedSession));
            OnPropertyChanged(nameof(CanTogglePinSelectedSession));
            OnPropertyChanged(nameof(CanCopySelectedSessionConfig));
            OnPropertyChanged(nameof(CanEditPhaseExecutors));
        }
    }

    public bool HasSession
    {
        get => _hasSession;
        private set
        {
            if (!SetField(ref _hasSession, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanStartAutomation));
        }
    }

    public bool HasPendingApproval
    {
        get => _hasPendingApproval;
        private set
        {
            if (!SetField(ref _hasPendingApproval, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApproveAutomation));
            OnPropertyChanged(nameof(CanRejectAutomation));
        }
    }

    public bool IsAutomationActive
    {
        get => _isAutomationActive;
        private set
        {
            if (!SetField(ref _isAutomationActive, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStartAutomation));
            OnPropertyChanged(nameof(CanStopAutomation));
            OnPropertyChanged(nameof(CanEditPhaseExecutors));
        }
    }

    public bool HasSelectedSession => SelectedSessionRecord is not null;

    public bool CanStart => !IsBusy;

    public bool CanSend => !IsBusy && HasSelectedSession;

    public bool AreModeSelectorsEnabled => !IsBusy && !AutoModeEnabled;

    public bool CanStartAutomation => !IsBusy && HasSelectedSession && !IsAutomationActive;

    public bool CanApproveAutomation => !IsBusy && HasPendingApproval;

    public bool CanRejectAutomation => !IsBusy && HasPendingApproval;

    public bool CanStopAutomation => !IsBusy && IsAutomationActive;

    public bool CanApplyLaunchProfile => !IsBusy && SelectedLaunchProfile is not null;

    public bool CanSaveLaunchProfile => !IsBusy && !string.IsNullOrWhiteSpace(NewLaunchProfileName);

    public bool CanReconnectSession => !IsBusy && HasSelectedSession;

    public bool CanFocusSelectedSession => !IsBusy && HasSelectedSession;

    public bool CanSaveSessionMetadata =>
        !IsBusy &&
        HasSelectedSession &&
        !string.IsNullOrWhiteSpace(EditableSessionDisplayName) &&
        !string.IsNullOrWhiteSpace(EditableSessionGroupName);

    public bool CanArchiveSelectedSession => !IsBusy && HasSelectedSession && !SelectedSessionRecord!.IsArchived;

    public bool CanRestoreSelectedSession => !IsBusy && HasSelectedSession && SelectedSessionRecord!.IsArchived;

    public bool CanTogglePinSelectedSession => !IsBusy && HasSelectedSession;

    public bool CanCopySelectedSessionConfig => !IsBusy && HasSelectedSession;

    public bool CanEditPhaseExecutors => !IsBusy && !IsAutomationActive;

    public bool HasPendingSessions => PendingSessionRecords.Count > 0;

    public IReadOnlyList<ModeOption> ClaudeModeOptions => ClaudeModes;

    public IReadOnlyList<ModeOption> CodexModeOptions => CodexModes;

    public IReadOnlyList<ModeOption> AutomationAdvancePolicyOptions => AutomationAdvanceModes;

    public IReadOnlyList<ModeOption> PhaseExecutorOptions => AutomationExecutorModes;

    public IReadOnlyList<ModeOption> AutomationParallelismPolicyOptions => AutomationParallelismModes;

    public IReadOnlyList<ModeOption> AutomationTemplateOptions => AutomationTemplateModes;

    public IReadOnlyList<ModeOption> WorktreeStrategyOptions => WorktreeStrategies;

    public IReadOnlyList<ModeOption> ThemeOptions => ThemeModes;

    public IReadOnlyList<ModeOption> SessionStatusFilterOptions => SessionStatusFilters;

    public IReadOnlyList<ModeOption> PanelPresetOptions => PanelPresetModes;

    public AutomationAdvancePolicy SelectedAutomationAdvancePolicy => ParseAutomationAdvancePolicy(_automationAdvancePolicy);

    public AgentRole Phase1ExecutorRole => ParseExecutorRole(_automationPhase1Executor);

    public AgentRole Phase2ExecutorRole => ParseExecutorRole(_automationPhase2Executor);

    public AgentRole Phase3ExecutorRole => ParseExecutorRole(_automationPhase3Executor);

    public AgentRole Phase4ExecutorRole => ParseExecutorRole(_automationPhase4Executor);

    public AutomationParallelismPolicy SelectedAutomationParallelismPolicy => ParseAutomationParallelismPolicy(_automationParallelismPolicy);

    public string ClaudeModeDisplayText => ClaudePermissionMode switch
    {
        "plan" => "Plan Mode",
        _ => "Default Mode",
    };

    public string CodexModeDisplayText => CodexMode switch
    {
        "full-auto" => "Full Auto",
        "never-ask" => "Never Ask",
        _ => "Standard",
    };

    public string ModeLockHint => AutoModeEnabled
        ? "自动模式已锁定 Claude/Codex 免确认参数，Phase 执行器在自动编排配置区独立设置"
        : "关闭自动模式后可自由选择 Claude / Codex 启动参数";

    public string AutomationPhaseExecutorSummary =>
        $"P1={ResolveExecutorLabel(_automationPhase1Executor)} / P2={ResolveExecutorLabel(_automationPhase2Executor)} / P3={ResolveExecutorLabel(_automationPhase3Executor)} / P4={ResolveExecutorLabel(_automationPhase4Executor)}";

    public void ReplaceDependencies(IEnumerable<DependencyStatus> dependencies)
    {
        Dependencies.Clear();
        foreach (var dependency in dependencies)
        {
            Dependencies.Add(dependency);
        }
    }

    public void ApplySessionCatalog(IEnumerable<ManagedSessionRecord> sessionRecords, string? preferredSessionId = null)
    {
        _allSessionRecords.Clear();
        _allSessionRecords.AddRange(sessionRecords.Select(static record => record.Clone()));
        ApplySessionFilter(preferredSessionId);
    }

    public void ReplaceLaunchProfiles(IEnumerable<LaunchProfile> profiles)
    {
        LaunchProfiles.Clear();
        foreach (var profile in profiles.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            LaunchProfiles.Add(profile);
        }

        if (LaunchProfiles.Count == 0)
        {
            SelectedLaunchProfileId = string.Empty;
            LaunchProfileSummary = "尚未配置 Launch Profile";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedLaunchProfileId) ||
            LaunchProfiles.All(profile => !string.Equals(profile.ProfileId, SelectedLaunchProfileId, StringComparison.Ordinal)))
        {
            SelectedLaunchProfileId = LaunchProfiles[0].ProfileId;
            return;
        }

        UpdateLaunchProfileSummary();
        if (_selectedSessionRecord is not null)
        {
            ApplySelectedSessionRecord(_selectedSessionRecord);
        }
    }

    public void ReplaceAutomationHistory(IEnumerable<AutomationEventRecord> events)
    {
        AutomationHistory.Clear();
        foreach (var item in events.OrderByDescending(static entry => entry.OccurredAt))
        {
            AutomationHistory.Add(item);
        }

        AutomationHistoryReplayHint = AutomationHistory.Count == 0
            ? "暂无回放数据"
            : "在阶段历史中查看回放";
    }

    public void ReplaceTaskMdRevisionHistory(IEnumerable<TaskMdRevisionRecord> revisions)
    {
        AutomationTaskMdRevisions.Clear();
        foreach (var item in revisions.OrderByDescending(static entry => entry.OccurredAt))
        {
            AutomationTaskMdRevisions.Add(item);
        }

        AutomationHistoryReplayHint = AutomationTaskMdRevisions.Count == 0
            ? (AutomationHistory.Count == 0 ? "暂无回放数据" : "在阶段历史中查看回放")
            : $"已记录 {AutomationTaskMdRevisions.Count} 次 task.md 变更";
    }

    public ManagedSessionRecord? SelectSessionById(string sessionId)
    {
        var record = SessionRecords.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
        if (record is null)
        {
            return null;
        }

        SelectedSessionRecord = record;
        return record;
    }

    public ManagedSessionRecord? FindSessionById(string sessionId)
    {
        return _allSessionRecords.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
    }

    public ManagedSessionRecord? ApplySelectedSessionToLaunchForm()
    {
        var record = SelectedSessionRecord;
        if (record is null)
        {
            return null;
        }

        WorkingDirectory = record.Session.WorkingDirectory;
        WorkspaceName = record.Session.Workspace;
        SessionGroupName = record.GroupName;
        ClaudePermissionMode = record.Session.ClaudePermissionMode;
        CodexMode = record.Session.CodexMode;
        RightPanePercent = record.Session.RightPanePercent;
        AutoModeEnabled = record.Session.AutomationEnabledAtLaunch;
        AutomationObserverEnabled = record.Session.AutomationObserverEnabled;
        UseWorktree = record.UsesWorktree;
        WorktreeStrategy = record.UsesWorktree ? "subdirectory" : "none";
        LaunchProfileSummary = $"已从会话 {record.DisplayName} 复制启动配置";
        return record;
    }

    public void RecordTransferSuccess(ContextTransferResult result)
    {
        LastTransferSummary = $"{result.SourcePaneId} -> {result.TargetPaneId}，抓取 {result.LastLines} 行，字符数 {result.CapturedLength}";
        LastTransferFailure = "暂无";
    }

    public void RecordTransferFailure(string errorMessage)
    {
        LastTransferFailure = DisplayOrPlaceholder(errorMessage);
    }

    public LaunchProfile? ApplyLaunchProfile()
    {
        var profile = SelectedLaunchProfile;
        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            WorkingDirectory = profile.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkspacePrefix))
        {
            WorkspaceName = profile.WorkspacePrefix;
        }

        SessionGroupName = profile.DefaultGroupName;
        TransferInstruction = profile.TransferInstructionTemplate;
        PanelPreset = profile.DefaultPanelPreset;
        ClaudePermissionMode = profile.ClaudePermissionMode;
        CodexMode = profile.CodexMode;
        RightPanePercent = profile.RightPanePercent;
        AutoModeEnabled = profile.AutomationEnabled;
        AutomationObserverEnabled = profile.AutomationObserverEnabled;
        SelectedAutomationAdvancePolicyKey = profile.AutomationAdvancePolicy;
        AutomationPollIntervalMilliseconds = profile.AutomationPollIntervalMilliseconds;
        AutomationCaptureLines = profile.AutomationCaptureLines;
        AutomationTimeoutSeconds = profile.AutomationTimeoutSeconds;
        AutomationMaxAutoStages = profile.AutomationMaxAutoStages;
        AutomationMaxRetryPerStage = profile.AutomationMaxRetryPerStage;
        AutomationSubmitOnSend = profile.AutomationSubmitOnSend;
        SelectedPhase1ExecutorKey = ResolveExecutorKey(profile.Phase1Executor);
        SelectedPhase2ExecutorKey = ResolveExecutorKey(profile.Phase2Executor);
        SelectedPhase3ExecutorKey = ResolveExecutorKey(profile.Phase3Executor);
        SelectedPhase4ExecutorKey = ResolveExecutorKey(profile.Phase4Executor);
        SelectedAutomationParallelismPolicyKey = ResolveParallelismKey(profile.ParallelismPolicy);
        AutomationMaxParallelSubagents = profile.MaxParallelSubagents;
        SelectedAutomationTemplateKey = profile.AutomationTemplateKey;
        UseWorktree = profile.DefaultUseWorktree;
        WorktreeStrategy = string.IsNullOrWhiteSpace(profile.DefaultWorktreeStrategy)
            ? profile.WorktreeStrategy
            : profile.DefaultWorktreeStrategy;

        return profile;
    }

    public LaunchProfile CaptureCurrentFormAsProfile()
    {
        return new LaunchProfile
        {
            Name = NewLaunchProfileName.Trim(),
            Description = "从当前新建会话表单保存",
            WorkingDirectory = WorkingDirectory,
            WorkspacePrefix = WorkspaceName,
            DefaultGroupName = SessionGroupName,
            TransferInstructionTemplate = TransferInstruction,
            DefaultPanelPreset = PanelPreset,
            ClaudePermissionMode = ClaudePermissionMode,
            CodexMode = CodexMode,
            RightPanePercent = RightPanePercent,
            AutomationEnabled = AutoModeEnabled,
            AutomationObserverEnabled = AutomationObserverEnabled,
            AutomationAdvancePolicy = SelectedAutomationAdvancePolicyKey,
            AutomationPollIntervalMilliseconds = AutomationPollIntervalMilliseconds,
            AutomationCaptureLines = AutomationCaptureLines,
            AutomationTimeoutSeconds = AutomationTimeoutSeconds,
            AutomationMaxAutoStages = AutomationMaxAutoStages,
            AutomationMaxRetryPerStage = AutomationMaxRetryPerStage,
            AutomationSubmitOnSend = AutomationSubmitOnSend,
            Phase1Executor = Phase1ExecutorRole,
            Phase2Executor = Phase2ExecutorRole,
            Phase3Executor = Phase3ExecutorRole,
            Phase4Executor = Phase4ExecutorRole,
            ParallelismPolicy = SelectedAutomationParallelismPolicy,
            MaxParallelSubagents = AutomationMaxParallelSubagents,
            AutomationTemplateKey = SelectedAutomationTemplateKey,
            DefaultUseWorktree = UseWorktree,
            WorktreeStrategy = WorktreeStrategy,
            DefaultWorktreeStrategy = WorktreeStrategy,
        };
    }

    public void ApplySession(LauncherSession? session)
    {
        if (session is null)
        {
            SessionWorkspace = "暂无";
            SessionCreatedAt = "暂无";
            SessionPaneInfo = "暂无";
            SessionSocketPath = "暂无";
            HasSession = false;
            return;
        }

        SessionWorkspace = session.Workspace;
        SessionCreatedAt = session.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        SessionPaneInfo = BuildSessionPaneInfo(session);
        SessionSocketPath = session.SocketPath;
        HasSession = true;
    }

    public void ApplyAutomationState(AutomationRunState state)
    {
        AutomationStatusLabel = state.Status.ToString();
        AutomationPhaseLabel = FormatPhaseLabel(state.Phase);
        AutomationStatusDetail = state.StatusDetail;
        AutomationLastPacketSummary = string.IsNullOrWhiteSpace(state.LastPacketSummary) ? "暂无" : state.LastPacketSummary;
        AutomationLastError = string.IsNullOrWhiteSpace(state.LastError) ? "暂无" : state.LastError;
        AutomationUpdatedAt = state.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        AutomationTaskMdPath = DisplayOrPlaceholder(state.TaskMdPath);
        AutomationTaskMdStatus = FormatTaskMdStatus(state.TaskMdStatus);
        AutomationCurrentTaskRef = DisplayOrPlaceholder(state.CurrentTaskRef);
        AutomationTaskProgressSummary = state.TaskCount <= 0
            ? "暂无"
            : $"已完成 {state.CompletedTaskCount}/{state.TaskCount} · 当前阶段 {state.CurrentTaskStageHeading}";
        AutomationAutoAdvanceStatusText = state.AutoAdvanceEnabled ? "已启用" : "未启用";
        AutomationAutoApprovedStageCount = state.AutoApprovedStageCount;
        AutomationCurrentStageRetryCount = state.CurrentStageRetryCount;
        AutomationInterventionReason = DisplayOrPlaceholder(state.InterventionReason);
        IsAutomationActive = state.IsActive;
        AutomationActiveExecutorLabel = state.ActiveExecutor.HasValue
            ? ResolveExecutorLabel(ResolveExecutorKey(state.ActiveExecutor.Value))
            : ResolvePhaseExecutorLabel(state.Phase);
        AutomationParallelGroupSummary = DisplayOrPlaceholder(state.PendingApproval?.ParallelGroup);

        if (state.PendingApproval is null)
        {
            ClearPendingApproval();
            return;
        }

        HasPendingApproval = true;
        PendingApprovalStage = $"阶段 {state.PendingApproval.StageId}";
        PendingApprovalTitle = DisplayOrPlaceholder(state.PendingApproval.Title);
        PendingApprovalSummary = DisplayOrPlaceholder(state.PendingApproval.Summary);
        PendingApprovalScope = DisplayOrPlaceholder(state.PendingApproval.Scope);
        PendingApprovalSteps = JoinLines(state.PendingApproval.Steps);
        PendingApprovalAcceptance = JoinLines(state.PendingApproval.AcceptanceCriteria);
        PendingApprovalCodexBrief = DisplayOrPlaceholder(state.PendingApproval.ExecutorBrief);
        PendingApprovalExecutorLabel = $"批准后发送给: {ResolvePhaseExecutorLabel(state.PendingApproval.Phase == AutomationPhase.None ? state.Phase : state.PendingApproval.Phase)}";
    }

    public void ResetAutomationState()
    {
        ApplyAutomationState(new AutomationRunState
        {
            Status = AutomationStageStatus.Idle,
            StatusDetail = "自动编排未启动。",
            LastPacketSummary = "暂无",
        });
        ApprovalNote = string.Empty;
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(line);
        LogText = _logBuilder.ToString();
    }

    public void ClearLog()
    {
        _logBuilder.Clear();
        LogText = string.Empty;
    }

    private void ApplyModeLock(bool autoModeEnabled)
    {
        if (autoModeEnabled)
        {
            _savedClaudePermissionMode = _claudePermissionMode;
            _savedCodexMode = _codexMode;
            ClaudePermissionMode = "plan";
            CodexMode = "full-auto";
            return;
        }

        ClaudePermissionMode = _savedClaudePermissionMode;
        CodexMode = _savedCodexMode;
    }

    private void ApplySessionFilter(string? preferredSessionId = null)
    {
        var keyword = _sessionSearchText.Trim();
        var filteredRecords = _allSessionRecords
            .Where(record => MatchesSessionFilter(record, keyword, _selectedSessionStatusFilter, _selectedSessionGroupFilter, _showArchivedSessions))
            .OrderByDescending(static record => record.IsPinned)
            .ThenBy(static record => record.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static record => record.LastSeenAt)
            .ToArray();

        SessionRecords.Clear();
        foreach (var record in filteredRecords)
        {
            SessionRecords.Add(record);
        }

        UpdateSessionGroupOptions();
        UpdatePendingQueue();

        var visibleSourceCount = _showArchivedSessions ? _allSessionRecords.Count : _allSessionRecords.Count(static record => !record.IsArchived);
        SessionCounterSummary = visibleSourceCount == 0
            ? "暂无会话"
            : $"{filteredRecords.Length} / {visibleSourceCount} 个会话";

        var currentSessionId = preferredSessionId ?? _selectedSessionRecord?.SessionId;
        var selectedRecord = filteredRecords.FirstOrDefault(record =>
            !string.IsNullOrWhiteSpace(currentSessionId) &&
            string.Equals(record.SessionId, currentSessionId, StringComparison.Ordinal));

        SelectedSessionRecord = selectedRecord ?? filteredRecords.FirstOrDefault();
    }

    private void ApplySelectedSessionRecord(ManagedSessionRecord? record)
    {
        if (record is null)
        {
            SelectedSessionDisplayName = "暂无";
            SelectedSessionStatus = "未选择";
            SelectedSessionStatusDetail = "从左侧选择一个会话";
            SelectedSessionWorkingDirectory = "暂无";
            SelectedSessionLastSeen = "暂无";
            SelectedSessionLastSummary = "暂无";
            SelectedSessionModeSummary = "暂无";
            SelectedSessionLastError = "暂无";
            SelectedSessionRuntimeSummary = "暂无";
            SelectedSessionBadgeSummary = "暂无";
            SelectedSessionWorktreeSummary = "暂无";
            SelectedSessionPendingSummary = "暂无";
            SelectedSessionClaudePreview = "暂无输出";
            SelectedSessionCodexPreview = "暂无输出";
            EditableSessionDisplayName = string.Empty;
            EditableSessionGroupName = "默认";
            ApplySession(null);
            return;
        }

        SelectedSessionDisplayName = DisplayOrPlaceholder(record.DisplayName);
        SelectedSessionStatus = record.HealthDisplayText;
        SelectedSessionStatusDetail = DisplayOrPlaceholder(record.HealthDetail);
        SelectedSessionWorkingDirectory = DisplayOrPlaceholder(record.Session.WorkingDirectory);
        SelectedSessionLastSeen = record.LastSeenDisplay;
        SelectedSessionLastSummary = DisplayOrPlaceholder(record.LastSummary);
        SelectedSessionModeSummary = record.ModeSummary;
        SelectedSessionLastError = DisplayOrPlaceholder(record.LastError);
        SelectedSessionRuntimeSummary = BuildRuntimeSummary(record);
        SelectedSessionBadgeSummary = record.BadgeSummary;
        SelectedSessionWorktreeSummary = record.UsesWorktree
            ? $"当前会话位于受管 worktree: {record.Session.WorkingDirectory}"
            : "当前会话使用主工作目录";
        SelectedSessionPendingSummary = record.PendingActionCount == 0
            ? "当前没有待处理事项"
            : $"当前会话共有 {record.PendingActionCount} 项待处理";
        SelectedSessionClaudePreview = DisplayOrPlaceholder(record.StatusSnapshot.ClaudePreview);
        SelectedSessionCodexPreview = DisplayOrPlaceholder(record.StatusSnapshot.CodexPreview);
        SelectedSessionLaunchProfileSummary = ResolveLaunchProfileSummary(record.LaunchProfileId);
        SelectedSessionReconnectSummary = record.RuntimeBinding.IsAlive
            ? "当前绑定在线"
            : "当前绑定已失效，等待手动重连";
        EditableSessionDisplayName = record.DisplayName;
        EditableSessionGroupName = record.GroupName;
        ApplySession(record.Session);
    }

    private bool MatchesSessionFilter(
        ManagedSessionRecord record,
        string keyword,
        string statusFilter,
        string groupFilter,
        bool showArchivedSessions)
    {
        if (!showArchivedSessions && record.IsArchived)
        {
            return false;
        }

        if (!string.Equals(groupFilter, "__all__", StringComparison.Ordinal) &&
            !string.Equals(record.GroupName, groupFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsStatusMatch(record, statusFilter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return ContainsIgnoreCase(record.DisplayName, keyword)
               || ContainsIgnoreCase(record.GroupName, keyword)
               || ContainsIgnoreCase(record.Session.Workspace, keyword)
               || ContainsIgnoreCase(record.Session.WorkingDirectory, keyword)
               || ContainsIgnoreCase(record.LastSummary, keyword)
               || ContainsIgnoreCase(record.HealthDetail, keyword);
    }

    private void UpdateSessionGroupOptions()
    {
        var groups = _allSessionRecords
            .Where(record => _showArchivedSessions || !record.IsArchived)
            .Select(record => string.IsNullOrWhiteSpace(record.GroupName) ? "默认" : record.GroupName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SessionGroupOptions.Clear();
        SessionGroupOptions.Add(new ModeOption { Value = "__all__", Label = "全部分组" });
        foreach (var group in groups)
        {
            SessionGroupOptions.Add(new ModeOption { Value = group, Label = group });
        }

        if (!_selectedSessionGroupFilter.Equals("__all__", StringComparison.Ordinal) &&
            SessionGroupOptions.All(option => !string.Equals(option.Value, _selectedSessionGroupFilter, StringComparison.Ordinal)))
        {
            _selectedSessionGroupFilter = "__all__";
            OnPropertyChanged(nameof(SelectedSessionGroupFilter));
        }
    }

    private void UpdatePendingQueue()
    {
        var pending = _allSessionRecords
            .Where(static record => !record.IsArchived)
            .Where(static record => record.PendingActionCount > 0)
            .OrderByDescending(static record => record.StatusSnapshot.NeedsApproval)
            .ThenByDescending(static record => record.LastSeenAt)
            .Take(8)
            .ToArray();

        PendingSessionRecords.Clear();
        foreach (var record in pending)
        {
            PendingSessionRecords.Add(record);
        }

        PendingQueueSummary = pending.Length == 0
            ? "暂无待处理"
            : $"待处理 {pending.Length} 项";
        OnPropertyChanged(nameof(HasPendingSessions));
    }

    private static bool IsStatusMatch(ManagedSessionRecord record, string statusFilter)
    {
        return NormalizeSessionStatusFilter(statusFilter) switch
        {
            "running" => record.HealthStatus == SessionHealthStatus.Running,
            "waiting" => record.HealthStatus == SessionHealthStatus.Waiting,
            "idle" => record.HealthStatus == SessionHealthStatus.Idle,
            "error" => record.HealthStatus == SessionHealthStatus.Error,
            "detached" => record.HealthStatus == SessionHealthStatus.Detached,
            _ => true,
        };
    }

    private static string BuildRuntimeSummary(ManagedSessionRecord record)
    {
        var runtime = record.RuntimeBinding.IsAlive ? "在线" : "失效";
        return $"GUI {record.RuntimeBinding.GuiPid} · Left {record.RuntimeBinding.LeftPaneId} · Right {record.RuntimeBinding.RightPaneId} · {runtime}";
    }

    private static bool ContainsIgnoreCase(string? source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source)
               && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearPendingApproval()
    {
        HasPendingApproval = false;
        PendingApprovalStage = "暂无";
        PendingApprovalTitle = "暂无";
        PendingApprovalSummary = "暂无";
        PendingApprovalScope = "暂无";
        PendingApprovalSteps = "暂无";
        PendingApprovalAcceptance = "暂无";
        PendingApprovalCodexBrief = "暂无";
        PendingApprovalExecutorLabel = "暂无";
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        var items = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => $"• {line.Trim()}")
            .ToArray();

        return items.Length == 0 ? "暂无" : string.Join(Environment.NewLine, items);
    }

    private static string DisplayOrPlaceholder(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "暂无" : value.Trim();
    }

    private static string FormatPhaseLabel(AutomationPhase phase)
    {
        return phase switch
        {
            AutomationPhase.Phase1Research => "Phase 1 · 项目调研",
            AutomationPhase.Phase2Planning => "Phase 2 · 计划编排",
            AutomationPhase.Phase3Execution => "Phase 3 · 任务执行",
            AutomationPhase.Phase4Review => "Phase 4 · 复核验收",
            _ => "Legacy / 暂无",
        };
    }

    private static string FormatTaskMdStatus(TaskMdStatus status)
    {
        return status switch
        {
            TaskMdStatus.PendingPlan => "PENDING_PLAN",
            TaskMdStatus.Planned => "PLANNED",
            TaskMdStatus.InProgress => "IN_PROGRESS",
            TaskMdStatus.Done => "DONE",
            _ => "暂无",
        };
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var cleanVersion = informationalVersion?.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(cleanVersion))
        {
            return cleanVersion;
        }

        var version = assembly.GetName().Version;
        return version is null ? "未知版本" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string BuildSessionPaneInfo(LauncherSession session)
    {
        var baseText = $"Left={session.LeftPaneId}, Right={session.RightPaneId}, Width={session.RightPanePercent}%, Claude={session.ClaudePermissionMode}, Codex={session.CodexMode}";
        if (!session.AutomationObserverEnabled)
        {
            return baseText;
        }

        return $"{baseText}, ClaudeView={session.ClaudeObserverPaneId?.ToString() ?? "无"}, CodexView={session.CodexObserverPaneId?.ToString() ?? "无"}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string NormalizeClaudePermissionMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "plan" => "plan",
            _ => "default",
        };
    }

    private static string NormalizeCodexMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "full-auto" => "full-auto",
            "never-ask" => "never-ask",
            _ => "standard",
        };
    }

    private static string NormalizeAutomationAdvancePolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "manual-each-stage" => "manual-each-stage",
            "manual-first-then-auto" => "manual-first-then-auto",
            _ => "full-auto-loop",
        };
    }

    private static string NormalizeExecutorKey(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            _ => "claude",
        };
    }

    private static string NormalizeAutomationParallelismPolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "conservative" => "conservative",
            "aggressive" => "aggressive",
            "balanced" => "balanced",
            _ => "auto",
        };
    }

    private static string NormalizeAutomationTemplateKey(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "bugfix" => "bugfix",
            "refactor" => "refactor",
            "research" => "research",
            _ => "feature",
        };
    }

    private static string ResolveExecutorKey(AgentRole role)
    {
        return role == AgentRole.Codex ? "codex" : "claude";
    }

    private static string ResolveParallelismKey(AutomationParallelismPolicy policy)
    {
        return policy switch
        {
            AutomationParallelismPolicy.Conservative => "conservative",
            AutomationParallelismPolicy.Aggressive => "aggressive",
            AutomationParallelismPolicy.Balanced => "balanced",
            _ => "auto",
        };
    }

    private static AgentRole ParseExecutorRole(string value)
    {
        return NormalizeExecutorKey(value) switch
        {
            "codex" => AgentRole.Codex,
            _ => AgentRole.Claude,
        };
    }

    private static AutomationParallelismPolicy ParseAutomationParallelismPolicy(string value)
    {
        return NormalizeAutomationParallelismPolicy(value) switch
        {
            "conservative" => AutomationParallelismPolicy.Conservative,
            "aggressive" => AutomationParallelismPolicy.Aggressive,
            "balanced" => AutomationParallelismPolicy.Balanced,
            _ => AutomationParallelismPolicy.Auto,
        };
    }

    private string ResolvePhaseExecutorLabel(AutomationPhase phase)
    {
        if (phase == AutomationPhase.None)
        {
            return "暂无";
        }

        return ResolveExecutorLabel(ResolvePhaseExecutorKey(phase));
    }

    private string ResolvePhaseExecutorKey(AutomationPhase phase)
    {
        return phase switch
        {
            AutomationPhase.Phase1Research => _automationPhase1Executor,
            AutomationPhase.Phase2Planning => _automationPhase2Executor,
            AutomationPhase.Phase3Execution => _automationPhase3Executor,
            AutomationPhase.Phase4Review => _automationPhase4Executor,
            _ => _automationPhase1Executor,
        };
    }

    private static string ResolveExecutorLabel(string executorKey)
    {
        return NormalizeExecutorKey(executorKey) switch
        {
            "codex" => "CODEX",
            _ => "Claude",
        };
    }

    private static string ResolveParallelismLabel(string policyKey)
    {
        return NormalizeAutomationParallelismPolicy(policyKey) switch
        {
            "conservative" => "保守",
            "aggressive" => "激进",
            "balanced" => "均衡",
            _ => "自动",
        };
    }

    private static string BuildAutomationTemplatePrompt(string templateKey)
    {
        return NormalizeAutomationTemplateKey(templateKey) switch
        {
            "bugfix" => "请先复现并定位问题，输出可审批的修复计划，再执行最小修复、补验证并完成回归。",
            "refactor" => "请先梳理现有实现与风险边界，输出可审批的重构计划，再分阶段实施、验证并完成收尾。",
            "research" => "请先围绕当前目录做只读调研，整理结构化结论、风险与建议，不要直接修改功能代码。",
            _ => "请围绕当前工作目录中的项目需求推进开发，先拆成可审批的阶段计划，再驱动执行与验证，直到完成。",
        };
    }

    private static string NormalizeWorktreeStrategy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "subdirectory" => "subdirectory",
            _ => "none",
        };
    }

    private static string NormalizeThemeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "light" => "light",
            "eye-care" => "eye-care",
            _ => "light",
        };
    }

    private static string NormalizeSessionStatusFilter(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "running" => "running",
            "waiting" => "waiting",
            "idle" => "idle",
            "error" => "error",
            "detached" => "detached",
            _ => "all",
        };
    }

    private static string NormalizePanelPreset(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "compose" => "compose",
            "automation" => "automation",
            _ => "balanced",
        };
    }

    private void ApplyPanelPreset(string preset)
    {
        var normalized = NormalizePanelPreset(preset);
        switch (normalized)
        {
            case "compose":
                IsLaunchPanelExpanded = true;
                IsAutomationPanelExpanded = false;
                IsEnvironmentPanelExpanded = false;
                break;
            case "automation":
                IsLaunchPanelExpanded = false;
                IsAutomationPanelExpanded = true;
                IsEnvironmentPanelExpanded = false;
                break;
            default:
                IsLaunchPanelExpanded = true;
                IsAutomationPanelExpanded = true;
                IsEnvironmentPanelExpanded = true;
                break;
        }
    }

    private static AutomationAdvancePolicy ParseAutomationAdvancePolicy(string value)
    {
        return NormalizeAutomationAdvancePolicy(value) switch
        {
            "manual-each-stage" => AutomationAdvancePolicy.ManualEachStage,
            "manual-first-then-auto" => AutomationAdvancePolicy.ManualFirstStageThenAuto,
            _ => AutomationAdvancePolicy.FullAutoLoop,
        };
    }

    private LaunchProfile? SelectedLaunchProfile => LaunchProfiles
        .FirstOrDefault(profile => string.Equals(profile.ProfileId, SelectedLaunchProfileId, StringComparison.Ordinal));

    private void UpdateLaunchProfileSummary()
    {
        var profile = SelectedLaunchProfile;
        LaunchProfileSummary = profile is null
            ? "使用右侧表单配置创建会话"
            : $"{profile.Name} / 分组 {profile.DefaultGroupName} / Claude {profile.ClaudePermissionMode} / Codex {profile.CodexMode}";
    }

    private string ResolveLaunchProfileSummary(string? launchProfileId)
    {
        if (string.IsNullOrWhiteSpace(launchProfileId))
        {
            return "未使用模板";
        }

        var profile = LaunchProfiles.FirstOrDefault(item =>
            string.Equals(item.ProfileId, launchProfileId, StringComparison.Ordinal));
        return profile is null ? $"模板 {launchProfileId}" : $"模板 {profile.Name}";
    }
}
