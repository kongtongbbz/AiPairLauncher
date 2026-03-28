using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using AiPairLauncher.App.ViewModels;

namespace AiPairLauncher.App;

public partial class MainWindow : Window
{
    private readonly IDependencyService _dependencyService;
    private readonly ISessionStore _sessionStore;
    private readonly IAppCacheService _appCacheService;
    private readonly IAppThemeService _appThemeService;
    private readonly ISessionMonitorService _sessionMonitorService;
    private readonly ISessionRuntimeRegistry _sessionRuntimeRegistry;
    private readonly IWezTermService _wezTermService;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _sessionHealthTimer;
    private readonly Dictionary<string, EventHandler<AutomationRunState>> _automationHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AutomationSettings> _automationSettingsBySession = new(StringComparer.Ordinal);
    private bool _hasInitialized;
    private bool _isRefreshingSessionHealth;
    private LauncherSession? _currentSession;

    public MainWindow(
        IDependencyService dependencyService,
        ISessionStore sessionStore,
        IAppCacheService appCacheService,
        ISessionMonitorService sessionMonitorService,
        ISessionRuntimeRegistry sessionRuntimeRegistry,
        IWezTermService wezTermService,
        IAppThemeService appThemeService,
        ThemeMode initialTheme)
    {
        _dependencyService = dependencyService ?? throw new ArgumentNullException(nameof(dependencyService));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _appCacheService = appCacheService ?? throw new ArgumentNullException(nameof(appCacheService));
        _appThemeService = appThemeService ?? throw new ArgumentNullException(nameof(appThemeService));
        _sessionMonitorService = sessionMonitorService ?? throw new ArgumentNullException(nameof(sessionMonitorService));
        _sessionRuntimeRegistry = sessionRuntimeRegistry ?? throw new ArgumentNullException(nameof(sessionRuntimeRegistry));
        _wezTermService = wezTermService ?? throw new ArgumentNullException(nameof(wezTermService));
        _viewModel = new MainWindowViewModel();
        _viewModel.SelectedThemeMode = initialTheme switch
        {
            ThemeMode.Light => "light",
            ThemeMode.EyeCare => "eye-care",
            _ => "dark",
        };
        _sessionHealthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8),
        };

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _sessionHealthTimer.Tick += SessionHealthTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;
        _viewModel.ResetAutomationState();
        await RefreshDependenciesAsync();
        await LoadLaunchProfilesAsync();
        await LoadSessionCatalogAsync(writeLog: true);
        await RestoreAutomationSnapshotIfPossibleAsync(_viewModel.SelectedSessionRecord?.Session).ConfigureAwait(true);
        await LoadAutomationHistoryAsync(_viewModel.SelectedSessionRecord?.SessionId).ConfigureAwait(true);
        await RefreshSessionHealthAsync(writeLog: false);
        _sessionHealthTimer.Start();
        _viewModel.AppendLog("多会话指挥台已就绪，可从左侧管理会话并在右侧执行启动、发送与自动编排。");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _sessionHealthTimer.Stop();
        foreach (var sessionId in _automationHandlers.Keys.ToArray())
        {
            if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(sessionId, out var coordinator) &&
                coordinator is not null &&
                _automationSettingsBySession.TryGetValue(sessionId, out var settings))
            {
                PersistAutomationSnapshotAsync(sessionId, settings, coordinator.GetCurrentState()).GetAwaiter().GetResult();
            }
        }

        foreach (var entry in _automationHandlers.ToArray())
        {
            if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(entry.Key, out var coordinator) &&
                coordinator is not null)
            {
                coordinator.StateChanged -= entry.Value;
            }
        }

        _automationHandlers.Clear();
        _sessionRuntimeRegistry.StopAllAsync().GetAwaiter().GetResult();
    }

    private async void SessionHealthTimer_Tick(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        await RefreshSessionHealthAsync(writeLog: false);
    }

    private async void RefreshDependencies_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDependenciesAsync();
    }

    private async void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_hasInitialized)
        {
            return;
        }

        await ExecuteActionAsync("切换主题", async () =>
        {
            var themeMode = _viewModel.SelectedThemeMode switch
            {
                "light" => ThemeMode.Light,
                "eye-care" => ThemeMode.EyeCare,
                _ => ThemeMode.Dark,
            };

            _appThemeService.ApplyTheme(themeMode);
            await _appThemeService.SaveThemePreferenceAsync(themeMode).ConfigureAwait(true);
            _viewModel.FooterMessage = _viewModel.ThemeSummary;
        });
    }

    private async void RefreshSessions_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("刷新会话", async () =>
        {
            await DiscoverAndReconnectSessionsAsync(writeLog: true).ConfigureAwait(true);
            await RefreshSessionHealthAsync(writeLog: true).ConfigureAwait(true);
            _viewModel.FooterMessage = "会话列表已刷新";
        });
    }

    private async void ApplyLaunchProfile_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("套用模板", async () =>
        {
            var profile = _viewModel.ApplyLaunchProfile();
            if (profile is null)
            {
                throw new InvalidOperationException("当前没有可套用的 Launch Profile。");
            }

            _viewModel.AppendLog($"已套用 Launch Profile: {profile.Name}");
            _viewModel.FooterMessage = $"已套用模板 {profile.Name}";
            await Task.CompletedTask;
        });
    }

    private async void SaveLaunchProfile_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("保存模板", async () =>
        {
            var profile = _viewModel.CaptureCurrentFormAsProfile();
            await _sessionStore.SaveLaunchProfileAsync(profile).ConfigureAwait(true);
            await LoadLaunchProfilesAsync().ConfigureAwait(true);
            _viewModel.NewLaunchProfileName = string.Empty;
            _viewModel.AppendLog($"已保存 Launch Profile: {profile.Name}");
            _viewModel.FooterMessage = $"已保存模板 {profile.Name}";
        });
    }

    private async void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedRecord = _viewModel.SelectedSessionRecord;
        _currentSession = selectedRecord?.Session;

        if (selectedRecord is null)
        {
            _viewModel.ResetAutomationState();
            _viewModel.ReplaceAutomationHistory([]);
            return;
        }

        await _sessionStore.SelectAsync(selectedRecord.SessionId).ConfigureAwait(true);
        _currentSession = selectedRecord.Session;
        _viewModel.FooterMessage = $"当前会话: {selectedRecord.DisplayName} / {selectedRecord.HealthDisplayText}";

        await RestoreAutomationSnapshotIfPossibleAsync(selectedRecord.Session).ConfigureAwait(true);
        await LoadAutomationHistoryAsync(selectedRecord.SessionId).ConfigureAwait(true);

        if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(selectedRecord.SessionId, out var coordinator) &&
            coordinator is not null)
        {
            _viewModel.ApplyAutomationState(coordinator.GetCurrentState());
        }
        else
        {
            _viewModel.ResetAutomationState();
        }
    }

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("启动会话", async () =>
        {
            EnsureWezTermService();
            EnsureWorkingDirectory();
            await StopAutomationIfRunningAsync().ConfigureAwait(true);

            var request = new LaunchRequest
            {
                Workspace = string.IsNullOrWhiteSpace(_viewModel.WorkspaceName) ? null : _viewModel.WorkspaceName.Trim(),
                WorkingDirectory = _viewModel.WorkingDirectory.Trim(),
                ClaudePermissionMode = _viewModel.ClaudePermissionMode,
                CodexMode = _viewModel.CodexMode,
                AutomationEnabled = _viewModel.AutoModeEnabled,
                AutomationObserverEnabled = _viewModel.AutomationObserverEnabled,
                RightPanePercent = _viewModel.RightPanePercent,
                UseWorktree = _viewModel.UseWorktree,
                WorktreeStrategy = _viewModel.WorktreeStrategy,
                WorktreeBranchName = string.IsNullOrWhiteSpace(_viewModel.WorkspaceName) ? null : _viewModel.WorkspaceName.Trim(),
                StartupTimeoutSeconds = 20,
            };

            var worktreeContext = await _wezTermService
                .CreateWorktreeLaunchContextAsync(request)
                .ConfigureAwait(true);
            var launchRequest = new LaunchRequest
            {
                Workspace = request.Workspace,
                WorkingDirectory = request.WorkingDirectory,
                ResolvedWorkingDirectory = worktreeContext.WorkingDirectory,
                ClaudePermissionMode = request.ClaudePermissionMode,
                CodexMode = request.CodexMode,
                AutomationEnabled = request.AutomationEnabled,
                AutomationObserverEnabled = request.AutomationObserverEnabled,
                RightPanePercent = request.RightPanePercent,
                UseWorktree = request.UseWorktree,
                WorktreeStrategy = request.WorktreeStrategy,
                WorktreeBranchName = request.WorktreeBranchName,
                StartupTimeoutSeconds = request.StartupTimeoutSeconds,
            };

            _currentSession = await _wezTermService!
                .StartAiPairAsync(launchRequest)
                .ConfigureAwait(true);

            await _sessionStore.SaveAsync(_currentSession).ConfigureAwait(true);
            var savedRecord = await _sessionStore.GetAsync(_currentSession.SessionId).ConfigureAwait(true);
            if (savedRecord is not null)
            {
                savedRecord.LaunchProfileId = string.IsNullOrWhiteSpace(_viewModel.SelectedLaunchProfileId)
                    ? null
                    : _viewModel.SelectedLaunchProfileId;
                savedRecord.GroupName = _viewModel.SessionGroupName;
                savedRecord.LastSummary = worktreeContext.Summary;
                savedRecord.UpdatedAt = DateTimeOffset.Now;
                await _sessionStore.UpsertAsync(savedRecord).ConfigureAwait(true);
            }

            await LoadSessionCatalogAsync(_currentSession.SessionId, writeLog: false).ConfigureAwait(true);
            await LoadAutomationHistoryAsync(_currentSession.SessionId).ConfigureAwait(true);
            _viewModel.AppendLog($"会话启动成功，工作区: {_currentSession.Workspace}，Claude 模式: {_viewModel.ClaudeModeDisplayText}，Codex 模式: {_viewModel.CodexModeDisplayText}。");
            _viewModel.AppendLog(worktreeContext.Summary);
            _viewModel.FooterMessage = $"最近操作: 已启动工作区 {_currentSession.Workspace}";

            if (_viewModel.AutoModeEnabled)
            {
                await StartAutomationCoreAsync(true).ConfigureAwait(true);
            }
        });
    }

    private async void StartAutomation_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("启动自动模式", async () =>
        {
            await StartAutomationCoreAsync(false).ConfigureAwait(true);
        });
    }

    private async void StopAutomation_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("停止自动模式", async () =>
        {
            await StopAutomationIfRunningAsync().ConfigureAwait(true);
        });
    }

    private async void ApprovePlan_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("批准计划", async () =>
        {
            var coordinator = EnsureCurrentAutomationCoordinator();
            var note = string.IsNullOrWhiteSpace(_viewModel.ApprovalNote) ? null : _viewModel.ApprovalNote.Trim();
            await coordinator
                .ApproveAsync(note)
                .ConfigureAwait(true);

            _viewModel.AppendLog("已批准待执行计划，并发送给 Codex。");
            _viewModel.FooterMessage = "最近操作: 已批准计划并发送给 Codex";
            _viewModel.ApprovalNote = string.Empty;
            await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
        });
    }

    private async void RejectPlan_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("退回计划", async () =>
        {
            var coordinator = EnsureCurrentAutomationCoordinator();
            var note = string.IsNullOrWhiteSpace(_viewModel.ApprovalNote) ? null : _viewModel.ApprovalNote.Trim();
            await coordinator
                .RejectAsync(note)
                .ConfigureAwait(true);

            _viewModel.AppendLog("已退回待执行计划，要求 Claude 重拟。");
            _viewModel.FooterMessage = "最近操作: 已退回计划给 Claude";
            _viewModel.ApprovalNote = string.Empty;
            await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
        });
    }

    private async void SendLeftToRight_Click(object sender, RoutedEventArgs e)
    {
        await SendContextAsync(fromLeftToRight: true);
    }

    private async void SendRightToLeft_Click(object sender, RoutedEventArgs e)
    {
        await SendContextAsync(fromLeftToRight: false);
    }

    private async void ReloadLastSession_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("加载会话", async () =>
        {
            await LoadSessionCatalogAsync(_viewModel.SelectedSessionRecord?.SessionId, writeLog: true).ConfigureAwait(true);
            await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
        });
    }

    private async void ReconnectSession_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("重连会话", async () =>
        {
            await TryReconnectSelectedSessionAsync().ConfigureAwait(true);
        });
    }

    private async void FocusClaudePane_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("聚焦 Claude", async () =>
        {
            await EnsureSessionReadyAsync().ConfigureAwait(true);
            await _wezTermService.FocusPaneAsync(_currentSession!, _currentSession!.LeftPaneId).ConfigureAwait(true);
            _viewModel.FooterMessage = "已聚焦 Claude pane";
        });
    }

    private async void FocusCodexPane_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("聚焦 Codex", async () =>
        {
            await EnsureSessionReadyAsync().ConfigureAwait(true);
            await _wezTermService.FocusPaneAsync(_currentSession!, _currentSession!.RightPaneId).ConfigureAwait(true);
            _viewModel.FooterMessage = "已聚焦 Codex pane";
        });
    }

    private async void TogglePinSession_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = ResolveSessionId(sender);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await ExecuteActionAsync("切换置顶", async () =>
        {
            var record = await _sessionStore.GetAsync(sessionId).ConfigureAwait(true);
            if (record is null)
            {
                throw new InvalidOperationException("会话记录不存在。");
            }

            await _sessionStore.SetPinnedAsync(sessionId, !record.IsPinned).ConfigureAwait(true);
            await LoadSessionCatalogAsync(sessionId, writeLog: false).ConfigureAwait(true);
            _viewModel.FooterMessage = !record.IsPinned ? "会话已置顶" : "会话已取消置顶";
        });
    }

    private async void ArchiveSession_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = ResolveSessionId(sender) ?? _viewModel.SelectedSessionRecord?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await ExecuteActionAsync("归档会话", async () =>
        {
            await _sessionStore.ArchiveAsync(sessionId).ConfigureAwait(true);
            await LoadSessionCatalogAsync(writeLog: false).ConfigureAwait(true);
            _viewModel.FooterMessage = "会话已归档";
        });
    }

    private async void RestoreSession_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = ResolveSessionId(sender) ?? _viewModel.SelectedSessionRecord?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await ExecuteActionAsync("恢复会话", async () =>
        {
            await _sessionStore.RestoreAsync(sessionId).ConfigureAwait(true);
            _viewModel.ShowArchivedSessions = true;
            await LoadSessionCatalogAsync(sessionId, writeLog: false).ConfigureAwait(true);
            _viewModel.FooterMessage = "会话已恢复";
        });
    }

    private async void SaveSessionMetadata_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("保存会话信息", async () =>
        {
            var record = _viewModel.SelectedSessionRecord;
            if (record is null)
            {
                throw new InvalidOperationException("当前没有选中的会话。");
            }

            await _sessionStore.RenameSessionAsync(record.SessionId, _viewModel.EditableSessionDisplayName).ConfigureAwait(true);
            await _sessionStore.MoveToGroupAsync(record.SessionId, _viewModel.EditableSessionGroupName).ConfigureAwait(true);
            await LoadSessionCatalogAsync(record.SessionId, writeLog: false).ConfigureAwait(true);
            _viewModel.FooterMessage = "会话名称和分组已更新";
        });
    }

    private async void CopySessionConfig_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = ResolveSessionId(sender) ?? _viewModel.SelectedSessionRecord?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await ExecuteActionAsync("复制启动配置", async () =>
        {
            _viewModel.SelectSessionById(sessionId);
            var record = _viewModel.ApplySelectedSessionToLaunchForm();
            if (record is null)
            {
                throw new InvalidOperationException("未找到可复制的会话。");
            }

            _viewModel.AppendLog($"已复制会话 {record.DisplayName} 的启动配置到右侧表单。");
            _viewModel.FooterMessage = $"已复制 {record.DisplayName} 的启动配置";
            await Task.CompletedTask;
        });
    }

    private void SelectPendingSession_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = ResolveSessionId(sender);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _viewModel.SelectSessionById(sessionId);
    }

    private void OpenWorktreePath_Click(object sender, RoutedEventArgs e)
    {
        var record = _viewModel.SelectedSessionRecord;
        if (record is null || !Directory.Exists(record.Session.WorkingDirectory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{record.Session.WorkingDirectory}\"",
            UseShellExecute = true,
        });
        _viewModel.FooterMessage = "已打开 worktree 目录";
    }

    private async void CleanupWorktree_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("清理 worktree", async () =>
        {
            var record = _viewModel.SelectedSessionRecord;
            if (record is null)
            {
                throw new InvalidOperationException("当前没有选中的会话。");
            }

            var choice = System.Windows.MessageBox.Show(
                "将尝试移除当前 worktree，并在分支已合并时一并删除分支。\n如果存在未提交改动，程序会阻止执行。\n\n是否继续？",
                "清理 worktree",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (choice != System.Windows.MessageBoxResult.Yes)
            {
                _viewModel.FooterMessage = "已取消清理 worktree";
                return;
            }

            var result = await _wezTermService.CleanupWorktreeAsync(record.Session.WorkingDirectory).ConfigureAwait(true);
            _viewModel.AppendLog(result.Summary);
            _viewModel.FooterMessage = result.Summary;
        });
    }

    private async void CleanupOrphanedWorktrees_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("清理孤儿 worktree", async () =>
        {
            var record = _viewModel.SelectedSessionRecord;
            if (record is null)
            {
                throw new InvalidOperationException("当前没有选中的会话。");
            }

            var choice = System.Windows.MessageBox.Show(
                "将清理当前仓库 .worktrees 目录下未注册的孤儿目录。\n这是删除目录操作，请确认当前仓库状态。\n\n是否继续？",
                "清理孤儿 worktree",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (choice != System.Windows.MessageBoxResult.Yes)
            {
                _viewModel.FooterMessage = "已取消清理孤儿 worktree";
                return;
            }

            var removed = await _wezTermService.CleanupOrphanedWorktreesAsync(record.Session.WorkingDirectory).ConfigureAwait(true);
            var summary = removed.Count == 0
                ? "未发现可清理的孤儿 worktree"
                : $"已清理 {removed.Count} 个孤儿 worktree";
            _viewModel.AppendLog(summary);
            foreach (var path in removed)
            {
                _viewModel.AppendLog($"已删除孤儿目录: {path}");
            }

            _viewModel.FooterMessage = summary;
        });
    }

    private async void ClearAppCache_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("清理程序缓存", async () =>
        {
            var choice = System.Windows.MessageBox.Show(
                "将清理 AiPairLauncher 生成的会话缓存和自动提示临时文件。\n不会删除安装文件、项目代码或已打开的终端窗口。\n\n是否继续？",
                "清理程序缓存",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (choice != System.Windows.MessageBoxResult.Yes)
            {
                _viewModel.AppendLog("已取消清理程序缓存。");
                _viewModel.FooterMessage = "最近操作: 已取消清理程序缓存";
                return;
            }

            await StopAllAutomationAsync().ConfigureAwait(true);

            var cleanupResult = await _appCacheService
                .ClearAsync()
                .ConfigureAwait(true);

            _currentSession = null;
            _viewModel.ApplySessionCatalog([], null);
            _viewModel.ResetAutomationState();
            _viewModel.ReplaceAutomationHistory([]);
            _viewModel.AppendLog($"程序缓存清理完成: {cleanupResult.Summary}");
            _viewModel.FooterMessage = "最近操作: 已清理程序缓存";
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLog();
        _viewModel.AppendLog("日志已清空。");
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var initialFolder = Directory.Exists(_viewModel.WorkingDirectory)
            ? _viewModel.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择工作目录",
            InitialDirectory = initialFolder,
            Multiselect = false,
        };

        var result = dialog.ShowDialog(this);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _viewModel.WorkingDirectory = dialog.FolderName;
            _viewModel.AppendLog($"工作目录已切换: {_viewModel.WorkingDirectory}");
        }
    }

    private async Task RefreshDependenciesAsync()
    {
        await ExecuteActionAsync("检查依赖", async () =>
        {
            var dependencies = await _dependencyService.CheckAllAsync().ConfigureAwait(true);
            _viewModel.ReplaceDependencies(dependencies);

            var missingCount = dependencies.Count(x => !x.IsAvailable);
            if (missingCount == 0)
            {
                _viewModel.AppendLog("依赖检查完成，全部可用。");
                _viewModel.FooterMessage = "依赖状态: 全部可用";
                return;
            }

            _viewModel.AppendLog($"依赖检查完成，缺失 {missingCount} 项。");
            _viewModel.FooterMessage = $"依赖状态: 缺失 {missingCount} 项";
        });
    }

    private async Task LoadLaunchProfilesAsync()
    {
        var profiles = await _sessionStore.ListProfilesAsync().ConfigureAwait(true);
        _viewModel.ReplaceLaunchProfiles(profiles);
    }

    private async Task LoadSessionCatalogAsync(string? preferredSessionId = null, bool writeLog = false)
    {
        var sessionRecords = await _sessionStore.ListAsync().ConfigureAwait(true);
        _viewModel.ApplySessionCatalog(sessionRecords, preferredSessionId);
        _currentSession = _viewModel.SelectedSessionRecord?.Session;

        if (_viewModel.SelectedSessionRecord is not null)
        {
            await _sessionStore.SelectAsync(_viewModel.SelectedSessionRecord.SessionId).ConfigureAwait(true);
        }

        if (writeLog)
        {
            if (sessionRecords.Count == 0)
            {
                _viewModel.AppendLog("未找到会话记录。");
            }
            else
            {
                _viewModel.AppendLog($"已载入 {sessionRecords.Count} 个会话。");
            }
        }
    }

    private async Task DiscoverAndReconnectSessionsAsync(bool writeLog)
    {
        var sessionRecords = (await _sessionStore.ListAsync().ConfigureAwait(true))
            .Select(static record => record.Clone())
            .ToList();

        if (sessionRecords.Count == 0)
        {
            _viewModel.ApplySessionCatalog([], null);
            _currentSession = null;
            if (writeLog)
            {
                _viewModel.AppendLog("未找到可刷新的会话。");
            }

            return;
        }

        var reconnectCount = 0;
        foreach (var record in sessionRecords.Where(static item => item.HealthStatus == SessionHealthStatus.Detached || !item.RuntimeBinding.IsAlive))
        {
            var reconnectResult = await _wezTermService.TryReconnectSessionAsync(record).ConfigureAwait(true);
            if (!reconnectResult.Success || reconnectResult.Session is null || reconnectResult.RuntimeBinding is null)
            {
                record.LastError = reconnectResult.FailureReason;
                record.LastSummary = string.IsNullOrWhiteSpace(reconnectResult.FailureReason)
                    ? "重连失败"
                    : reconnectResult.FailureReason;
                record.UpdatedAt = DateTimeOffset.Now;
                continue;
            }

            record.Session = reconnectResult.Session;
            record.RuntimeBinding = reconnectResult.RuntimeBinding;
            record.HealthStatus = SessionHealthStatus.Idle;
            record.HealthDetail = "已手动重连";
            record.LastError = null;
            record.LastSummary = "已重新绑定到存活的 WezTerm 工作区";
            record.LastSeenAt = DateTimeOffset.Now;
            record.UpdatedAt = DateTimeOffset.Now;
            reconnectCount += 1;
        }

        var selectedSessionId = _viewModel.SelectedSessionRecord?.SessionId ?? _currentSession?.SessionId;
        await _sessionStore.SaveAllAsync(sessionRecords, selectedSessionId).ConfigureAwait(true);
        _viewModel.ApplySessionCatalog(sessionRecords, selectedSessionId);
        _currentSession = _viewModel.SelectedSessionRecord?.Session;

        if (writeLog)
        {
            _viewModel.AppendLog(reconnectCount == 0
                ? "手动刷新完成，没有需要重连的会话。"
                : $"手动刷新完成，已重连 {reconnectCount} 个会话。");
        }
    }

    private async Task RefreshSessionHealthAsync(bool writeLog)
    {
        if (_isRefreshingSessionHealth)
        {
            return;
        }

        _isRefreshingSessionHealth = true;
        try
        {
            var sessionRecords = (await _sessionStore.ListAsync().ConfigureAwait(true))
                .Select(static record => record.Clone())
                .ToList();

            if (sessionRecords.Count == 0)
            {
                _viewModel.ApplySessionCatalog([], null);
                _currentSession = null;
                return;
            }

            var refreshedRecords = await _sessionMonitorService
                .RefreshAsync(sessionRecords, ResolveAutomationState, CancellationToken.None)
                .ConfigureAwait(true);

            var selectedSessionId = _viewModel.SelectedSessionRecord?.SessionId ?? _currentSession?.SessionId;
            await _sessionStore.SaveAllAsync(refreshedRecords, selectedSessionId).ConfigureAwait(true);
            _viewModel.ApplySessionCatalog(refreshedRecords, selectedSessionId);
            _currentSession = _viewModel.SelectedSessionRecord?.Session;

            if (writeLog)
            {
                _viewModel.AppendLog($"会话状态已刷新，共 {refreshedRecords.Count} 个会话。");
            }
        }
        finally
        {
            _isRefreshingSessionHealth = false;
        }
    }

    private async Task SendContextAsync(bool fromLeftToRight)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        var actionName = fromLeftToRight ? "左向右发送" : "右向左发送";
        _viewModel.IsBusy = true;
        _viewModel.StatusMessage = $"{actionName}中...";
        try
        {
            EnsureWezTermService();
            await EnsureSessionReadyAsync().ConfigureAwait(true);

            var request = new SendContextRequest
            {
                Workspace = _currentSession!.Workspace,
                LastLines = _viewModel.LastLines,
                Instruction = _viewModel.TransferInstruction,
                Submit = _viewModel.SubmitAfterSend,
                FromLeftToRight = fromLeftToRight,
            };

            var transferResult = await _wezTermService!
                .SendContextAsync(_currentSession!, request)
                .ConfigureAwait(true);

            _viewModel.RecordTransferSuccess(transferResult);
            var direction = $"{transferResult.SourcePaneId}->{transferResult.TargetPaneId}";
            _viewModel.AppendLog($"上下文发送成功: {direction}, 字符数 {transferResult.CapturedLength}");
            _viewModel.FooterMessage = $"最近发送: {direction}, 抓取 {transferResult.LastLines} 行";
            _viewModel.StatusMessage = $"{actionName}完成";
        }
        catch (Exception ex)
        {
            _viewModel.RecordTransferFailure(ex.Message);
            _viewModel.StatusMessage = $"{actionName}失败";
            _viewModel.AppendLog($"{actionName}失败: {ex.Message}");
            _viewModel.FooterMessage = $"{actionName}失败，请检查日志。";
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async Task StartAutomationCoreAsync(bool autoStartedByLaunch)
    {
        await EnsureSessionReadyAsync().ConfigureAwait(true);

        if (_currentSession is null)
        {
            throw new InvalidOperationException("没有可用会话，无法启动自动编排。");
        }

        if (!_currentSession.AutomationEnabledAtLaunch)
        {
            throw new InvalidOperationException("当前会话不是自动模式会话，请勾选“自动交互模式”后重新启动 Ai Pair。");
        }

        var settings = new AutomationSettings
        {
            IsEnabled = _viewModel.AutoModeEnabled,
            InitialTaskPrompt = _viewModel.AutomationTaskPrompt.Trim(),
            AdvancePolicy = _viewModel.SelectedAutomationAdvancePolicy,
            PollIntervalMilliseconds = _viewModel.AutomationPollIntervalMilliseconds,
            CaptureLines = _viewModel.AutomationCaptureLines,
            SubmitOnSend = _viewModel.AutomationSubmitOnSend,
            NoProgressTimeoutSeconds = _viewModel.AutomationTimeoutSeconds,
            MaxAutoStages = _viewModel.AutomationMaxAutoStages,
            MaxRetryPerStage = _viewModel.AutomationMaxRetryPerStage,
        };

        var coordinator = _sessionRuntimeRegistry.GetOrCreateAutomationCoordinator(_currentSession.SessionId);
        EnsureAutomationCoordinatorSubscription(_currentSession.SessionId, coordinator);
        _automationSettingsBySession[_currentSession.SessionId] = settings;

        await coordinator
            .StartAsync(_currentSession, settings)
            .ConfigureAwait(true);

        await PersistAutomationSnapshotAsync(_currentSession.SessionId, settings, coordinator.GetCurrentState()).ConfigureAwait(true);
        await LoadAutomationHistoryAsync(_currentSession.SessionId).ConfigureAwait(true);

        _viewModel.AppendLog($"自动编排已启动，推进策略: {_viewModel.SelectedAutomationAdvancePolicyKey}。");
        _viewModel.FooterMessage = "自动编排已启动";
        await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
    }

    private async Task StopAutomationIfRunningAsync()
    {
        if (_currentSession is null)
        {
            return;
        }

        if (!_sessionRuntimeRegistry.TryGetAutomationCoordinator(_currentSession.SessionId, out var coordinator) ||
            coordinator is null)
        {
            return;
        }

        var state = coordinator.GetCurrentState();
        if (state.Status is AutomationStageStatus.Idle
            or AutomationStageStatus.Stopped
            or AutomationStageStatus.Completed
            or AutomationStageStatus.PausedOnError)
        {
            return;
        }

        await coordinator.StopAsync().ConfigureAwait(true);
        if (_automationSettingsBySession.TryGetValue(_currentSession.SessionId, out var settings))
        {
            await PersistAutomationSnapshotAsync(_currentSession.SessionId, settings, coordinator.GetCurrentState()).ConfigureAwait(true);
        }
        await _sessionStore.SaveAutomationEventAsync(AutomationEventRecord.FromState(_currentSession.SessionId, coordinator.GetCurrentState())).ConfigureAwait(true);
        await LoadAutomationHistoryAsync(_currentSession.SessionId).ConfigureAwait(true);
        _viewModel.AppendLog("已停止当前自动编排。");
        _viewModel.FooterMessage = "自动编排已停止";
        await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
    }

    private async Task StopAllAutomationAsync()
    {
        foreach (var sessionId in _automationHandlers.Keys.ToArray())
        {
            await _sessionRuntimeRegistry.StopAutomationAsync(sessionId).ConfigureAwait(true);
        }
    }

    private async Task EnsureSessionReadyAsync()
    {
        if (_currentSession is not null)
        {
            await EnsureLiveSessionOrThrowAsync(_currentSession).ConfigureAwait(true);
            return;
        }

        _currentSession = _viewModel.SelectedSessionRecord?.Session;
        if (_currentSession is null)
        {
            _currentSession = await _sessionStore.LoadAsync().ConfigureAwait(true);
        }

        if (_currentSession is null)
        {
            _viewModel.ApplySession(null);
            throw new InvalidOperationException("当前没有可用会话，请先点击“启动 Ai Pair”。");
        }

        await EnsureLiveSessionOrThrowAsync(_currentSession).ConfigureAwait(true);
        await LoadSessionCatalogAsync(_currentSession.SessionId, writeLog: false).ConfigureAwait(true);
    }

    private void EnsureWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.WorkingDirectory))
        {
            throw new InvalidOperationException("工作目录不能为空。");
        }

        if (!Directory.Exists(_viewModel.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在: {_viewModel.WorkingDirectory}");
        }
    }

    private void EnsureWezTermService()
    {
        _ = _wezTermService;
    }

    private IAutoCollaborationCoordinator EnsureCurrentAutomationCoordinator()
    {
        if (_currentSession is null)
        {
            throw new InvalidOperationException("当前没有选中的自动编排会话。");
        }

        if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(_currentSession.SessionId, out var coordinator) &&
            coordinator is not null)
        {
            return coordinator;
        }

        throw new InvalidOperationException("当前选中会话没有正在运行的自动编排。");
    }

    private async Task EnsureLiveSessionOrThrowAsync(LauncherSession session)
    {
        var message = "选中会话对应的 WezTerm 已失效，请重新点击“启动 Ai Pair”创建新会话。";
        if (await TryEnsureSessionAliveAsync(session, message).ConfigureAwait(true))
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private async Task<bool> TryEnsureSessionAliveAsync(LauncherSession session, string userMessage)
    {
        try
        {
            EnsureWezTermService();
            var panes = await _wezTermService!
                .GetWorkspacePanesAsync(session, session.Workspace)
                .ConfigureAwait(true);

            LauncherSessionValidator.EnsurePaneTopology(session, panes);
            return true;
        }
        catch (Exception ex)
        {
            await MarkSessionDetachedAsync(session.SessionId, ex.Message).ConfigureAwait(true);
            if (_currentSession is not null &&
                string.Equals(_currentSession.SessionId, session.SessionId, StringComparison.Ordinal))
            {
                _currentSession = null;
            }

            _viewModel.AppendLog($"{userMessage} 详细原因: {ex.Message}");
            _viewModel.FooterMessage = userMessage;
            return false;
        }
    }

    private async Task MarkSessionDetachedAsync(string sessionId, string errorMessage)
    {
        var sessionRecords = (await _sessionStore.ListAsync().ConfigureAwait(true))
            .Select(static record => record.Clone())
            .ToList();

        var record = sessionRecords.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
        if (record is null)
        {
            return;
        }

        record.HealthStatus = SessionHealthStatus.Detached;
        record.HealthDetail = "会话已断开";
        record.LastError = errorMessage;
        record.LastSummary = "需要重新启动";
        record.UpdatedAt = DateTimeOffset.Now;

        await _sessionStore.SaveAllAsync(sessionRecords, _viewModel.SelectedSessionRecord?.SessionId).ConfigureAwait(true);
        _viewModel.ApplySessionCatalog(sessionRecords, _viewModel.SelectedSessionRecord?.SessionId);
    }

    private async Task TryReconnectSelectedSessionAsync()
    {
        if (_viewModel.SelectedSessionRecord is null)
        {
            throw new InvalidOperationException("当前没有选中的会话。");
        }

        var reconnectResult = await _wezTermService
            .TryReconnectSessionAsync(_viewModel.SelectedSessionRecord)
            .ConfigureAwait(true);

        if (!reconnectResult.Success || reconnectResult.Session is null || reconnectResult.RuntimeBinding is null)
        {
            _viewModel.SelectedSessionReconnectSummary = string.IsNullOrWhiteSpace(reconnectResult.FailureReason)
                ? "重连失败"
                : reconnectResult.FailureReason;
            throw new InvalidOperationException(_viewModel.SelectedSessionReconnectSummary);
        }

        var records = (await _sessionStore.ListAsync().ConfigureAwait(true))
            .Select(static record => record.Clone())
            .ToList();
        var record = records.FirstOrDefault(item => string.Equals(item.SessionId, _viewModel.SelectedSessionRecord.SessionId, StringComparison.Ordinal));
        if (record is null)
        {
            throw new InvalidOperationException("选中的会话记录已不存在。");
        }

        record.Session = reconnectResult.Session;
        record.RuntimeBinding = reconnectResult.RuntimeBinding;
        record.HealthStatus = SessionHealthStatus.Idle;
        record.HealthDetail = "已手动重连";
        record.LastError = null;
        record.LastSummary = "已重新绑定到存活的 WezTerm 工作区";
        record.LastSeenAt = DateTimeOffset.Now;
        record.UpdatedAt = DateTimeOffset.Now;

        await _sessionStore.SaveAllAsync(records, record.SessionId).ConfigureAwait(true);
        _viewModel.SelectedSessionReconnectSummary = "刚刚成功重连";
        _viewModel.AppendLog($"会话 {record.DisplayName} 已完成手动重连。");
        await LoadSessionCatalogAsync(record.SessionId, writeLog: false).ConfigureAwait(true);
        await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
    }

    private void EnsureAutomationCoordinatorSubscription(string sessionId, IAutoCollaborationCoordinator coordinator)
    {
        if (_automationHandlers.ContainsKey(sessionId))
        {
            return;
        }

        EventHandler<AutomationRunState> handler = (_, state) =>
        {
            _ = HandleAutomationStateChangedAsync(sessionId, state);
        };

        coordinator.StateChanged += handler;
        _automationHandlers[sessionId] = handler;
    }

    private AutomationRunState? ResolveAutomationState(string sessionId)
    {
        if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(sessionId, out var coordinator) &&
            coordinator is not null)
        {
            return coordinator.GetCurrentState();
        }

        return null;
    }

    private async Task HandleAutomationStateChangedAsync(string sessionId, AutomationRunState state)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await _sessionStore.SaveAutomationEventAsync(AutomationEventRecord.FromState(sessionId, state)).ConfigureAwait(true);
            if (_automationSettingsBySession.TryGetValue(sessionId, out var settings))
            {
                await PersistAutomationSnapshotAsync(sessionId, settings, state).ConfigureAwait(true);
            }

            if (_currentSession is not null &&
                string.Equals(_currentSession.SessionId, sessionId, StringComparison.Ordinal))
            {
                _viewModel.ApplyAutomationState(state);
                await LoadAutomationHistoryAsync(sessionId).ConfigureAwait(true);
            }

            _viewModel.AppendLog($"自动编排[{sessionId}] 状态: {state.Status} / {state.StatusDetail}");
            if (!string.IsNullOrWhiteSpace(state.LastError))
            {
                _viewModel.AppendLog($"自动编排错误: {state.LastError}");
            }

            if (!string.IsNullOrWhiteSpace(state.InterventionReason))
            {
                _viewModel.AppendLog($"自动编排转人工接管: {state.InterventionReason}");
            }

            _viewModel.FooterMessage = state.LastError is null
                ? $"自动编排: {state.StatusDetail}"
                : $"自动编排暂停: {state.LastError}";

            await RefreshSessionHealthAsync(writeLog: false).ConfigureAwait(true);
        });
    }

    private async Task LoadAutomationHistoryAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _viewModel.ReplaceAutomationHistory([]);
            return;
        }

        var events = await _sessionStore.ListAutomationEventsAsync(sessionId, 24).ConfigureAwait(true);
        _viewModel.ReplaceAutomationHistory(events);
    }

    private async Task PersistAutomationSnapshotAsync(string sessionId, AutomationSettings settings, AutomationRunState state)
    {
        await _sessionStore.SaveAutomationSnapshotAsync(new PersistedAutomationSnapshot
        {
            SessionId = sessionId,
            Settings = settings,
            State = state,
            UpdatedAt = state.UpdatedAt,
        }).ConfigureAwait(true);
    }

    private async Task RestoreAutomationSnapshotIfPossibleAsync(LauncherSession? session)
    {
        if (session is null)
        {
            return;
        }

        if (_sessionRuntimeRegistry.TryGetAutomationCoordinator(session.SessionId, out _))
        {
            return;
        }

        var snapshot = await _sessionStore.GetAutomationSnapshotAsync(session.SessionId).ConfigureAwait(true);
        if (snapshot is null)
        {
            return;
        }

        _automationSettingsBySession[session.SessionId] = snapshot.Settings;
        if (!snapshot.CanResume)
        {
            if (_currentSession is not null && string.Equals(_currentSession.SessionId, session.SessionId, StringComparison.Ordinal))
            {
                _viewModel.ApplyAutomationState(snapshot.State);
            }

            return;
        }

        var coordinator = _sessionRuntimeRegistry.GetOrCreateAutomationCoordinator(session.SessionId);
        EnsureAutomationCoordinatorSubscription(session.SessionId, coordinator);
        await coordinator.RestoreAsync(session, snapshot.Settings, snapshot.State).ConfigureAwait(true);

        if (_currentSession is not null && string.Equals(_currentSession.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            _viewModel.ApplyAutomationState(coordinator.GetCurrentState());
        }
    }

    private static string? ResolveSessionId(object sender)
    {
        return sender is FrameworkElement element
            ? element.Tag as string
            : null;
    }

    private async Task ExecuteActionAsync(string actionName, Func<Task> action)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.IsBusy = true;
        _viewModel.StatusMessage = $"{actionName}中...";

        try
        {
            await action().ConfigureAwait(true);
            _viewModel.StatusMessage = $"{actionName}完成";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"{actionName}失败";
            _viewModel.AppendLog($"{actionName}失败: {ex.Message}");
            _viewModel.FooterMessage = $"{actionName}失败，请检查日志。";
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }
}
