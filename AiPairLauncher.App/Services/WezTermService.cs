using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class WezTermService : IWezTermService
{
    private const int ObserverPanePercent = 50;
    private const int ObserverCaptureLines = 220;
    private const int ObserverRefreshMilliseconds = 1200;
    private const string PacketMarker = "[AIPAIR_PACKET]";
    private const string PacketMarkerPreview = "[AIPAIR_PACKET_TEMPLATE]";
    private const string PacketEndMarker = "[/AIPAIR_PACKET]";
    private const string PacketEndMarkerPreview = "[/AIPAIR_PACKET_TEMPLATE]";

    private readonly CommandLocator _commandLocator;
    private readonly IProcessRunner _processRunner;
    private readonly string? _configFilePath;

    public WezTermService(CommandLocator commandLocator, IProcessRunner processRunner, string? configFilePath = null)
    {
        _commandLocator = commandLocator;
        _processRunner = processRunner;
        _configFilePath = configFilePath;
    }

    public async Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ValidateLaunchRequest(request);
        var wezTermPath = ResolveWezTermPath();
        var workspace = BuildWorkspaceName(request.Workspace);
        var launchDirectory = ResolveLaunchDirectory(request);
        var leftPaneCommand = BuildLeftPaneShellCommand(request);
        var rightPaneCommand = BuildRightPaneShellCommand(request);
        var existingGuiPids = Process.GetProcessesByName("wezterm-gui").Select(p => p.Id).ToHashSet();

        var startResult = await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = wezTermPath,
                Arguments = BuildStartArguments(workspace, launchDirectory, leftPaneCommand),
                Timeout = TimeSpan.FromSeconds(request.StartupTimeoutSeconds),
                WaitForExit = false,
            },
            cancellationToken).ConfigureAwait(false);

        var guiPid = await WaitForNewGuiPidAsync(existingGuiPids, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        var socketPath = BuildSocketPath(guiPid);
        await WaitForSocketAsync(socketPath, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);

        var firstPane = await WaitForFirstPaneAsync(wezTermPath, socketPath, workspace, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        var leftPaneId = firstPane.PaneId;
        var rightPaneId = await SplitRightPaneAsync(wezTermPath, socketPath, leftPaneId, request, launchDirectory, rightPaneCommand, cancellationToken).ConfigureAwait(false);
        await WaitForPaneCountAsync(wezTermPath, socketPath, workspace, 2, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);

        int? claudeObserverPaneId = null;
        int? codexObserverPaneId = null;
        if (request.AutomationEnabled && request.AutomationObserverEnabled)
        {
            claudeObserverPaneId = await SplitBottomObserverPaneAsync(
                wezTermPath,
                socketPath,
                leftPaneId,
                launchDirectory,
                BuildObserverShellCommand(wezTermPath, socketPath, leftPaneId, "Claude"),
                request.StartupTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            codexObserverPaneId = await SplitBottomObserverPaneAsync(
                wezTermPath,
                socketPath,
                rightPaneId,
                launchDirectory,
                BuildObserverShellCommand(wezTermPath, socketPath, rightPaneId, "Codex"),
                request.StartupTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            await WaitForPaneCountAsync(wezTermPath, socketPath, workspace, 4, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }

        return new LauncherSession
        {
            Workspace = workspace,
            WorkingDirectory = launchDirectory,
            WezTermPath = wezTermPath,
            SocketPath = socketPath,
            GuiPid = guiPid,
            LeftPaneId = leftPaneId,
            RightPaneId = rightPaneId,
            RightPanePercent = request.RightPanePercent,
            ClaudeObserverPaneId = claudeObserverPaneId,
            CodexObserverPaneId = codexObserverPaneId,
            AutomationObserverEnabled = request.AutomationEnabled && request.AutomationObserverEnabled,
            ClaudePermissionMode = request.ClaudePermissionMode,
            CodexMode = request.CodexMode,
            AutomationEnabledAtLaunch = request.AutomationEnabled,
            CreatedAt = DateTimeOffset.Now,
        };
    }

    public async Task<IReadOnlyList<ManagedWorkspaceInfo>> ListManagedWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var wezTermPath = ResolveWezTermPath();
        var socketDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "wezterm");

        if (!Directory.Exists(socketDirectory))
        {
            return [];
        }

        var workspaceList = new List<ManagedWorkspaceInfo>();
        foreach (var socketPath in Directory.EnumerateFiles(socketDirectory, "gui-sock-*", SearchOption.TopDirectoryOnly))
        {
            IReadOnlyList<PaneInfo> panes;
            try
            {
                panes = await ListPanesBySocketAsync(wezTermPath, socketPath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            foreach (var group in panes
                         .Where(static pane => !string.IsNullOrWhiteSpace(pane.Workspace))
                         .GroupBy(static pane => pane.Workspace, StringComparer.Ordinal))
            {
                workspaceList.Add(new ManagedWorkspaceInfo
                {
                    Workspace = group.Key,
                    SocketPath = socketPath,
                    GuiPid = ExtractGuiPid(socketPath),
                    Panes = group
                        .OrderBy(static pane => pane.LeftCol)
                        .ThenByDescending(static pane => pane.Rows)
                        .ThenBy(static pane => pane.PaneId)
                        .ToArray(),
                });
            }
        }

        return workspaceList
            .OrderBy(static item => item.Workspace, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SessionReconnectResult> TryReconnectSessionAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionRecord);

        var managedWorkspaces = await ListManagedWorkspacesAsync(cancellationToken).ConfigureAwait(false);
        var matchedWorkspace = FindBestReconnectWorkspace(managedWorkspaces, sessionRecord);
        if (matchedWorkspace is null)
        {
            var availableWorkspaces = string.Join("、", managedWorkspaces.Select(static item => item.Workspace));
            return new SessionReconnectResult
            {
                Success = false,
                FailureReason = managedWorkspaces.Count == 0
                    ? "当前没有可用的 WezTerm 工作区。"
                    : $"未找到匹配的 WezTerm 工作区（按工作区、socket、pane、工作目录均未命中）。当前可见工作区: {availableWorkspaces}",
            };
        }

        var mainPanes = SelectMainPanes(matchedWorkspace.Panes);
        if (mainPanes.Count < 2)
        {
            mainPanes = SelectKnownMainPanes(matchedWorkspace.Panes, sessionRecord);
        }

        if (mainPanes.Count < 2)
        {
            mainPanes = matchedWorkspace.Panes
                .OrderBy(static pane => pane.LeftCol)
                .ThenBy(static pane => pane.PaneId)
                .Take(2)
                .ToArray();
        }

        if (mainPanes.Count < 2)
        {
            return new SessionReconnectResult
            {
                Success = false,
                FailureReason = "匹配到工作区，但 pane 拓扑不完整。",
            };
        }

        var leftPane = mainPanes[0];
        var rightPane = mainPanes[1];
        var session = new LauncherSession
        {
            SessionId = sessionRecord.SessionId,
            Workspace = sessionRecord.Session.Workspace,
            WorkingDirectory = leftPane.CurrentDirectory ?? sessionRecord.Session.WorkingDirectory,
            WezTermPath = sessionRecord.Session.WezTermPath,
            SocketPath = matchedWorkspace.SocketPath,
            GuiPid = matchedWorkspace.GuiPid,
            LeftPaneId = leftPane.PaneId,
            RightPaneId = rightPane.PaneId,
            RightPanePercent = sessionRecord.Session.RightPanePercent,
            ClaudeObserverPaneId = SelectObserverPane(matchedWorkspace.Panes, leftPane.LeftCol, leftPane.PaneId),
            CodexObserverPaneId = SelectObserverPane(matchedWorkspace.Panes, rightPane.LeftCol, rightPane.PaneId),
            AutomationObserverEnabled = sessionRecord.Session.AutomationObserverEnabled,
            ClaudePermissionMode = sessionRecord.Session.ClaudePermissionMode,
            CodexMode = sessionRecord.Session.CodexMode,
            AutomationEnabledAtLaunch = sessionRecord.Session.AutomationEnabledAtLaunch,
            CreatedAt = sessionRecord.Session.CreatedAt,
        };

        return new SessionReconnectResult
        {
            Success = true,
            Session = session,
            RuntimeBinding = new SessionRuntimeBinding
            {
                SessionId = sessionRecord.SessionId,
                GuiPid = matchedWorkspace.GuiPid,
                SocketPath = matchedWorkspace.SocketPath,
                LeftPaneId = leftPane.PaneId,
                RightPaneId = rightPane.PaneId,
                ClaudeObserverPaneId = session.ClaudeObserverPaneId,
                CodexObserverPaneId = session.CodexObserverPaneId,
                IsAlive = true,
                UpdatedAt = DateTimeOffset.Now,
            },
        };
    }

    public async Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return await ListPanesAsync(session.WezTermPath, session.SocketPath, workspace, cancellationToken).ConfigureAwait(false);
    }

    public async Task FocusPaneAsync(LauncherSession session, int paneId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await ActivatePaneAsync(session.WezTermPath, session.SocketPath, paneId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorktreeLaunchContext> CreateWorktreeLaunchContextAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.UseWorktree || !string.Equals(request.WorktreeStrategy, "subdirectory", StringComparison.OrdinalIgnoreCase))
        {
            return new WorktreeLaunchContext
            {
                GitRoot = request.WorkingDirectory,
                WorkingDirectory = request.WorkingDirectory,
                UsedWorktree = false,
                WorktreeStrategy = "none",
                Summary = "未启用 worktree，继续使用原目录启动。",
            };
        }

        var gitPath = ResolveGitPath();
        if (gitPath is null)
        {
            return new WorktreeLaunchContext
            {
                GitRoot = request.WorkingDirectory,
                WorkingDirectory = request.WorkingDirectory,
                UsedWorktree = false,
                WorktreeStrategy = request.WorktreeStrategy,
                Summary = "未找到 git，已回退到原目录启动。",
            };
        }

        var gitRoot = await TryResolveGitRootAsync(gitPath, request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gitRoot))
        {
            return new WorktreeLaunchContext
            {
                GitRoot = request.WorkingDirectory,
                WorkingDirectory = request.WorkingDirectory,
                UsedWorktree = false,
                WorktreeStrategy = request.WorktreeStrategy,
                Summary = "当前目录不在 Git 仓库中，已回退到原目录启动。",
            };
        }

        var branchName = BuildWorktreeBranchName(request);
        var worktreeRoot = Path.Combine(gitRoot, ".worktrees");
        Directory.CreateDirectory(worktreeRoot);
        var worktreeDirectory = Path.Combine(worktreeRoot, branchName);

        if (Directory.Exists(worktreeDirectory))
        {
            branchName = $"{branchName}-{DateTime.Now:yyyyMMddHHmmss}";
            worktreeDirectory = Path.Combine(worktreeRoot, branchName);
        }

        var result = await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = gitPath,
                Arguments =
                [
                    "worktree",
                    "add",
                    "-b",
                    branchName,
                    worktreeDirectory
                ],
                WorkingDirectory = gitRoot,
                Timeout = TimeSpan.FromSeconds(Math.Max(20, request.StartupTimeoutSeconds)),
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || !Directory.Exists(worktreeDirectory))
        {
            return new WorktreeLaunchContext
            {
                GitRoot = gitRoot,
                WorkingDirectory = request.WorkingDirectory,
                UsedWorktree = false,
                WorktreeStrategy = request.WorktreeStrategy,
                BranchName = branchName,
                Summary = string.IsNullOrWhiteSpace(result.StandardError)
                    ? "创建 worktree 失败，已回退到原目录启动。"
                    : $"创建 worktree 失败，已回退到原目录启动：{result.StandardError}",
            };
        }

        return new WorktreeLaunchContext
        {
            GitRoot = gitRoot,
            WorkingDirectory = worktreeDirectory,
            UsedWorktree = true,
            WorktreeStrategy = request.WorktreeStrategy,
            BranchName = branchName,
            Summary = $"已创建 worktree：{branchName}",
        };
    }

    public async Task<WorktreeMaintenanceResult> CleanupWorktreeAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var gitPath = ResolveGitPath();
        if (gitPath is null)
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                WorkingDirectory = workingDirectory,
                Summary = "未找到 git，无法清理 worktree。",
            };
        }

        var gitRoot = await TryResolveGitRootAsync(gitPath, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gitRoot))
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                WorkingDirectory = workingDirectory,
                Summary = "当前目录不在 Git 仓库中，无法清理 worktree。",
            };
        }

        if (!IsManagedWorktreePath(gitRoot, workingDirectory))
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                GitRoot = gitRoot,
                WorkingDirectory = workingDirectory,
                Summary = "当前会话不在受管的 .worktrees 目录中，已阻止清理。",
            };
        }

        var dirtyResult = await RunGitAsync(gitPath, ["status", "--porcelain"], workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!dirtyResult.IsSuccess)
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                GitRoot = gitRoot,
                WorkingDirectory = workingDirectory,
                Summary = string.IsNullOrWhiteSpace(dirtyResult.StandardError)
                    ? "无法检查 worktree 状态。"
                    : $"无法检查 worktree 状态：{dirtyResult.StandardError}",
            };
        }

        if (!string.IsNullOrWhiteSpace(dirtyResult.StandardOutput) &&
            !HasOnlyManagedTaskMdChanges(dirtyResult.StandardOutput))
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                GitRoot = gitRoot,
                WorkingDirectory = workingDirectory,
                Summary = "worktree 中仍有未提交改动，已阻止清理。",
            };
        }

        var branchResult = await RunGitAsync(gitPath, ["branch", "--show-current"], workingDirectory, cancellationToken).ConfigureAwait(false);
        var branchName = branchResult.IsSuccess ? branchResult.StandardOutput.Trim() : null;

        var removeResult = await RunGitAsync(gitPath, ["worktree", "remove", workingDirectory], gitRoot, cancellationToken).ConfigureAwait(false);
        if (!removeResult.IsSuccess)
        {
            return new WorktreeMaintenanceResult
            {
                Success = false,
                GitRoot = gitRoot,
                WorkingDirectory = workingDirectory,
                BranchName = branchName,
                Summary = string.IsNullOrWhiteSpace(removeResult.StandardError)
                    ? "移除 worktree 失败。"
                    : $"移除 worktree 失败：{removeResult.StandardError}",
            };
        }

        var messages = new List<string> { "worktree 已移除。" };
        var branchDeleted = false;
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            var deleteBranchResult = await RunGitAsync(gitPath, ["branch", "-d", branchName], gitRoot, cancellationToken).ConfigureAwait(false);
            if (deleteBranchResult.IsSuccess)
            {
                branchDeleted = true;
                messages.Add($"已删除已合并分支 {branchName}。");
            }
            else
            {
                messages.Add(string.IsNullOrWhiteSpace(deleteBranchResult.StandardError)
                    ? $"分支 {branchName} 未删除，请手动确认是否保留。"
                    : $"分支 {branchName} 保留：{deleteBranchResult.StandardError.Trim()}");
            }
        }

        return new WorktreeMaintenanceResult
        {
            Success = true,
            GitRoot = gitRoot,
            WorkingDirectory = workingDirectory,
            BranchName = branchName,
            WorktreeRemoved = true,
            BranchDeleted = branchDeleted,
            Summary = string.Join(" ", messages),
            Messages = messages,
        };
    }

    public async Task<IReadOnlyList<string>> CleanupOrphanedWorktreesAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var gitPath = ResolveGitPath();
        if (gitPath is null)
        {
            return [];
        }

        var gitRoot = await TryResolveGitRootAsync(gitPath, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gitRoot))
        {
            return [];
        }

        var managedRoot = Path.Combine(gitRoot, ".worktrees");
        if (!Directory.Exists(managedRoot))
        {
            return [];
        }

        var listResult = await RunGitAsync(gitPath, ["worktree", "list", "--porcelain"], gitRoot, cancellationToken).ConfigureAwait(false);
        if (!listResult.IsSuccess)
        {
            return [];
        }

        var registeredPaths = ParseRegisteredWorktreePaths(listResult.StandardOutput);
        var removedDirectories = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(managedRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var normalizedPath = Path.GetFullPath(directory);
            if (registeredPaths.Contains(normalizedPath))
            {
                continue;
            }

            Directory.Delete(normalizedPath, recursive: true);
            removedDirectories.Add(normalizedPath);
        }

        return removedDirectories;
    }

    public async Task<string> ReadPaneTextAsync(LauncherSession session, int paneId, int lastLines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await GetPaneTextAsync(session.WezTermPath, session.SocketPath, paneId, lastLines, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendTextToPaneAsync(LauncherSession session, int paneId, string text, bool submit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await SendPaneTextAsync(session.WezTermPath, session.SocketPath, paneId, text, false, cancellationToken).ConfigureAwait(false);
        if (submit)
        {
            await ActivatePaneAsync(session.WezTermPath, session.SocketPath, paneId, cancellationToken).ConfigureAwait(false);
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            WindowInputHelper.SendEnterKeyToProcessWindow(session.GuiPid);
        }
    }

    public async Task SendAutomationPromptAsync(
        LauncherSession session,
        AgentRole role,
        string prompt,
        bool submit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        if (!session.AutomationEnabledAtLaunch)
        {
            throw new InvalidOperationException("当前会话不是自动模式会话，无法使用自动编排命令分发。");
        }

        var promptPath = await WriteAutomationPromptToTempAsync(role, prompt, cancellationToken).ConfigureAwait(false);
        var command = role switch
        {
            AgentRole.Claude => BuildClaudeAutomationCommand(session, promptPath),
            AgentRole.Codex => BuildCodexAutomationCommand(session, promptPath),
            _ => throw new InvalidOperationException($"不支持的自动编排角色: {role}"),
        };

        var paneId = role == AgentRole.Claude ? session.LeftPaneId : session.RightPaneId;
        var payload = submit ? $"{command}{Environment.NewLine}" : command;
        await SendPaneTextAsync(session.WezTermPath, session.SocketPath, paneId, payload, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContextTransferResult> SendContextAsync(LauncherSession session, SendContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var workspace = string.IsNullOrWhiteSpace(request.Workspace) ? session.Workspace : request.Workspace;
        var panes = await ListPanesAsync(session.WezTermPath, session.SocketPath, workspace, cancellationToken).ConfigureAwait(false);
        if (panes.Count < 2)
        {
            throw new InvalidOperationException($"工作区 {workspace} 没有足够的 pane。");
        }

        var (sourcePaneId, targetPaneId) = SessionPaneRouter.ResolveTransferPaneIds(session, request);

        var capturedText = await GetPaneTextAsync(session.WezTermPath, session.SocketPath, sourcePaneId, request.LastLines, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(capturedText))
        {
            throw new InvalidOperationException($"pane {sourcePaneId} 没有可发送内容。");
        }

        var payload = BuildPayload(request.Instruction, sourcePaneId, capturedText);
        await SendTextToPaneAsync(session, targetPaneId, payload, request.Submit, cancellationToken).ConfigureAwait(false);

        return new ContextTransferResult
        {
            SourcePaneId = sourcePaneId,
            TargetPaneId = targetPaneId,
            LastLines = Math.Abs(request.LastLines),
            Submitted = request.Submit,
            CapturedLength = capturedText.Length,
        };
    }

    private string ResolveWezTermPath()
    {
        var resolvedPath = _commandLocator.Resolve(
            "wezterm",
            new[]
            {
                @"C:\Program Files\WezTerm\wezterm.exe",
                @"%LOCALAPPDATA%\Microsoft\WinGet\Links\wezterm.exe",
            });

        return resolvedPath ?? throw new FileNotFoundException("未找到 wezterm 可执行文件。");
    }

    private static void ValidateLaunchRequest(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        if (!Directory.Exists(request.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在: {request.WorkingDirectory}");
        }

        if (request.RightPanePercent is < 10 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(request.RightPanePercent), "RightPanePercent 必须在 10 到 90 之间。");
        }

        if (request.StartupTimeoutSeconds < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StartupTimeoutSeconds), "StartupTimeoutSeconds 不能小于 5 秒。");
        }
    }

    private static string BuildWorkspaceName(string? workspace)
    {
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            return workspace;
        }

        return $"ai-pair-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string BuildSocketPath(int guiPid)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".local", "share", "wezterm", $"gui-sock-{guiPid}");
    }

    private static async Task<int> WaitForNewGuiPidAsync(IReadOnlySet<int> existingGuiPids, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.Now < deadline)
        {
            var process = Process
                .GetProcessesByName("wezterm-gui")
                .Where(p => !existingGuiPids.Contains(p.Id))
                .OrderByDescending(p => SafeStartTime(p))
                .FirstOrDefault();
            if (process is not null)
            {
                return process.Id;
            }

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("等待 WezTerm GUI 进程超时。");
    }

    private static DateTime SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static async Task WaitForSocketAsync(string socketPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.Now < deadline)
        {
            if (File.Exists(socketPath))
            {
                return;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待 socket 文件超时: {socketPath}");
    }

    private async Task<PaneInfo> WaitForFirstPaneAsync(
        string wezTermPath,
        string socketPath,
        string workspace,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.Now < deadline)
        {
            var panes = await ListPanesAsync(wezTermPath, socketPath, workspace, cancellationToken).ConfigureAwait(false);
            if (panes.Count >= 1)
            {
                return panes[0];
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待工作区 {workspace} 的初始 pane 超时。");
    }

    private async Task<int> SplitRightPaneAsync(
        string wezTermPath,
        string socketPath,
        int leftPaneId,
        LaunchRequest request,
        string workingDirectory,
        string rightPaneCommand,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "split-pane",
                "--pane-id", leftPaneId.ToString(),
                "--right",
                "--percent", request.RightPanePercent.ToString(),
                "--cwd", workingDirectory,
                "--",
                "powershell.exe",
                "-NoLogo",
                "-NoExit",
                "-Command",
                rightPaneCommand,
            },
            null,
            TimeSpan.FromSeconds(request.StartupTimeoutSeconds),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("split-pane 执行失败", result);
        }

        if (!int.TryParse(result.StandardOutput.Trim(), out var paneId))
        {
            throw new InvalidOperationException($"无法解析 split-pane 返回的 pane id: {result.StandardOutput}");
        }

        return paneId;
    }

    private async Task<IReadOnlyList<PaneInfo>> WaitForPaneCountAsync(
        string wezTermPath,
        string socketPath,
        string workspace,
        int expectedPaneCount,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.Now < deadline)
        {
            var panes = await ListPanesAsync(wezTermPath, socketPath, workspace, cancellationToken).ConfigureAwait(false);
            if (panes.Count >= expectedPaneCount)
            {
                return panes;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待工作区 {workspace} 的 {expectedPaneCount} 个 pane 就绪超时。");
    }

    private async Task<int> SplitBottomObserverPaneAsync(
        string wezTermPath,
        string socketPath,
        int parentPaneId,
        string workingDirectory,
        string observerCommand,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "split-pane",
                "--pane-id", parentPaneId.ToString(),
                "--bottom",
                "--percent", ObserverPanePercent.ToString(),
                "--cwd", workingDirectory,
                "--",
                "powershell.exe",
                "-NoLogo",
                "-NoExit",
                "-Command",
                observerCommand,
            },
            null,
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("split-pane(observer) 执行失败", result);
        }

        if (!int.TryParse(result.StandardOutput.Trim(), out var paneId))
        {
            throw new InvalidOperationException($"无法解析 observer pane id: {result.StandardOutput}");
        }

        return paneId;
    }

    private async Task<string> GetPaneTextAsync(
        string wezTermPath,
        string socketPath,
        int paneId,
        int lastLines,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "get-text",
                "--pane-id", paneId.ToString(),
                "--start-line", (-Math.Abs(lastLines)).ToString(),
            },
            null,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("获取 pane 文本失败", result);
        }

        return result.StandardOutput.Trim();
    }

    private async Task SendPaneTextAsync(
        string wezTermPath,
        string socketPath,
        int paneId,
        string text,
        bool noPaste,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "send-text",
            "--pane-id",
            paneId.ToString(),
        };
        if (noPaste)
        {
            args.Add("--no-paste");
        }

        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            args,
            text,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("发送 pane 文本失败", result);
        }
    }

    private async Task<IReadOnlyList<PaneInfo>> ListPanesAsync(
        string wezTermPath,
        string socketPath,
        string workspace,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "list",
                "--format",
                "json",
            },
            null,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("读取 pane 列表失败", result);
        }

        return ParsePanes(result.StandardOutput, workspace);
    }

    private async Task ActivatePaneAsync(
        string wezTermPath,
        string socketPath,
        int paneId,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "activate-pane",
                "--pane-id", paneId.ToString(),
            },
            null,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("激活 pane 失败", result);
        }
    }

    private async Task<ProcessResult> RunWezTermCliAsync(
        string wezTermPath,
        string socketPath,
        IReadOnlyList<string> cliArgs,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var args = BuildGlobalArguments();
        args.Add("cli");
        args.Add("--no-auto-start");
        args.AddRange(cliArgs);

        return await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = wezTermPath,
                Arguments = args,
                StandardInput = standardInput,
                Timeout = timeout,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["WEZTERM_UNIX_SOCKET"] = socketPath,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static Exception CreateCliFailureException(string operation, ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        var normalized = string.IsNullOrWhiteSpace(detail) ? "无错误详情" : detail.Trim();

        if (ContainsPermissionDenied(normalized))
        {
            return new UnauthorizedAccessException($"{operation}: {normalized}");
        }

        if (ContainsTimeoutHint(normalized))
        {
            return new TimeoutException($"{operation}: {normalized}");
        }

        return new InvalidOperationException($"{operation}: {normalized}");
    }

    private static bool ContainsPermissionDenied(string text)
    {
        return text.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
               || text.Contains("权限", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTimeoutHint(string text)
    {
        return text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || text.Contains("超时", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PaneInfo> ParsePanes(string json, string workspace)
    {
        return ParsePanes(json)
            .Where(pane => string.Equals(pane.Workspace, workspace, StringComparison.Ordinal))
            .OrderBy(p => p.LeftCol)
            .ThenBy(p => p.PaneId)
            .ToArray();
    }

    private static string BuildPayload(string instruction, int sourcePaneId, string capturedText)
    {
        return string.Join(
            "\n",
            instruction,
            string.Empty,
            $"<source-pane id=\"{sourcePaneId}\">",
            capturedText,
            "</source-pane>",
            string.Empty);
    }

    private List<string> BuildStartArguments(string workspace, string workingDirectory, string startCommand)
    {
        var args = BuildGlobalArguments();
        args.AddRange(
        [
            "start",
            "--always-new-process",
            "--workspace",
            workspace,
            "--cwd",
            workingDirectory,
            "--",
            "powershell.exe",
            "-NoLogo",
            "-NoExit",
            "-Command",
            startCommand,
        ]);

        return args;
    }

    private List<string> BuildGlobalArguments()
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(_configFilePath))
        {
            return args;
        }

        if (!File.Exists(_configFilePath))
        {
            throw new FileNotFoundException($"未找到专用 WezTerm 配置文件: {_configFilePath}");
        }

        args.Add("--config-file");
        args.Add(_configFilePath);
        return args;
    }

    private string BuildLeftPaneShellCommand(LaunchRequest request)
    {
        return request.AutomationEnabled
            ? BuildAutomationShellReadyCommand(AgentRole.Claude)
            : BuildClaudeInteractiveShellCommand(request);
    }

    private string BuildRightPaneShellCommand(LaunchRequest request)
    {
        return request.AutomationEnabled
            ? BuildAutomationShellReadyCommand(AgentRole.Codex)
            : BuildCodexInteractiveShellCommand(request);
    }

    private string BuildClaudeInteractiveShellCommand(LaunchRequest request)
    {
        var claudePath = ResolveClaudePath();
        var parts = new List<string>
        {
            "&",
            QuoteForPowerShell(claudePath),
        };

        if (string.Equals(request.ClaudePermissionMode, "plan", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("--permission-mode");
            parts.Add("plan");
        }

        return string.Join(' ', parts);
    }

    private string BuildCodexInteractiveShellCommand(LaunchRequest request)
    {
        var codexPath = ResolveCodexPath();
        var parts = new List<string>
        {
            "&",
            QuoteForPowerShell(codexPath),
        };

        switch (request.CodexMode.Trim().ToLowerInvariant())
        {
            case "full-auto":
                parts.Add("--full-auto");
                break;
            case "never-ask":
                parts.Add("-a");
                parts.Add("never");
                parts.Add("-s");
                parts.Add("workspace-write");
                break;
        }

        return string.Join(' ', parts);
    }

    private static string BuildAutomationShellReadyCommand(AgentRole role)
    {
        var roleName = role == AgentRole.Claude ? "Claude" : "Codex";
        return $"Write-Host '[AiPair] {roleName} automation shell ready.'";
    }

    private string BuildObserverShellCommand(
        string wezTermPath,
        string socketPath,
        int sourcePaneId,
        string roleName)
    {
        return string.Join(
            " ",
            "$ErrorActionPreference='Continue';",
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;",
            "$OutputEncoding=[System.Text.Encoding]::UTF8;",
            $"$env:WEZTERM_UNIX_SOCKET={QuoteForPowerShell(socketPath)};",
            $"$wezTermPath={QuoteForPowerShell(wezTermPath)};",
            $"$sourcePaneId={sourcePaneId};",
            $"$roleName={QuoteForPowerShell(roleName)};",
            "while ($true) {",
            "try {",
            "Clear-Host;",
            "Write-Host ('[AiPair] ' + $roleName + ' live view -> pane ' + $sourcePaneId + ' @ ' + (Get-Date -Format 'HH:mm:ss'));",
            "Write-Host '';",
            $"& $wezTermPath cli --no-auto-start get-text --pane-id $sourcePaneId --start-line -{ObserverCaptureLines};",
            "}",
            "catch {",
            "Write-Host ('[AiPair] live view error: ' + $_.Exception.Message);",
            "}",
            $"Start-Sleep -Milliseconds {ObserverRefreshMilliseconds};",
            "}");
    }

    private string BuildClaudeAutomationCommand(LauncherSession session, string promptPath)
    {
        var claudePath = ResolveClaudePath();
        var promptPreviewPreamble = BuildPromptPreviewPreamble("Claude");

        return string.Join(
            " ",
            "$ErrorActionPreference='Stop';",
            "[Console]::InputEncoding=[System.Text.Encoding]::UTF8;",
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;",
            "$OutputEncoding=[System.Text.Encoding]::UTF8;",
            $"try {{ $promptText = Get-Content -Encoding UTF8 -Raw -LiteralPath {QuoteForPowerShell(promptPath)}; {promptPreviewPreamble} $promptText | & {QuoteForPowerShell(claudePath)} -p --dangerously-skip-permissions --output-format text 2>&1 }}",
            $"finally {{ Remove-Item -LiteralPath {QuoteForPowerShell(promptPath)} -Force -ErrorAction SilentlyContinue }}");
    }

    private string BuildCodexAutomationCommand(LauncherSession session, string promptPath)
    {
        var codexPath = ResolveCodexPath();
        var promptPreviewPreamble = BuildPromptPreviewPreamble("Codex");
        return string.Join(
            " ",
            "$ErrorActionPreference='Stop';",
            "[Console]::InputEncoding=[System.Text.Encoding]::UTF8;",
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;",
            "$OutputEncoding=[System.Text.Encoding]::UTF8;",
            $"try {{ $promptText = Get-Content -Encoding UTF8 -Raw -LiteralPath {QuoteForPowerShell(promptPath)}; {promptPreviewPreamble} $promptText | & {QuoteForPowerShell(codexPath)} exec --full-auto --skip-git-repo-check --color never -C {QuoteForPowerShell(session.WorkingDirectory)} - 2>&1 }}",
            $"finally {{ Remove-Item -LiteralPath {QuoteForPowerShell(promptPath)} -Force -ErrorAction SilentlyContinue }}");
    }

    private static string BuildPromptPreviewPreamble(string roleName)
    {
        return string.Join(
            " ",
            $"$promptPreview = $promptText.Replace({QuoteForPowerShell(PacketMarker)}, {QuoteForPowerShell(PacketMarkerPreview)}).Replace({QuoteForPowerShell(PacketEndMarker)}, {QuoteForPowerShell(PacketEndMarkerPreview)});",
            $"Write-Host {QuoteForPowerShell($"[AiPair] Prompt -> {roleName}")};",
            "Write-Host '';",
            "Write-Host $promptPreview;",
            "Write-Host '';");
    }

    private static string BuildCodexExecModeArguments(string codexMode)
    {
        return codexMode.Trim().ToLowerInvariant() switch
        {
            "never-ask" => "-a never -s workspace-write",
            "full-auto" => "--full-auto",
            _ => "-s workspace-write",
        };
    }

    private static async Task<string> WriteAutomationPromptToTempAsync(
        AgentRole role,
        string prompt,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiPairLauncher", "automation-prompts");
        Directory.CreateDirectory(directory);

        var fileName = $"{role.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.txt";
        var filePath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(filePath, prompt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    private string ResolveClaudePath()
    {
        var resolvedPath = _commandLocator.Resolve(
            "claude",
            new[]
            {
                @"%USERPROFILE%\.local\bin\claude.exe",
            });

        return resolvedPath ?? throw new FileNotFoundException("未找到 claude 可执行文件。");
    }

    private string ResolveCodexPath()
    {
        var resolvedPath = _commandLocator.Resolve(
            "codex",
            new[]
            {
                @"%APPDATA%\npm\codex.cmd",
                @"%APPDATA%\npm\codex.ps1",
            });

        return resolvedPath ?? throw new FileNotFoundException("未找到 codex 可执行文件。");
    }

    private string? ResolveGitPath()
    {
        return _commandLocator.Resolve("git");
    }

    private async Task<ProcessResult> RunGitAsync(
        string gitPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = gitPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Timeout = TimeSpan.FromSeconds(20),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveLaunchDirectory(LaunchRequest request)
    {
        return string.IsNullOrWhiteSpace(request.ResolvedWorkingDirectory)
            ? request.WorkingDirectory
            : request.ResolvedWorkingDirectory;
    }

    private async Task<IReadOnlyList<PaneInfo>> ListPanesBySocketAsync(
        string wezTermPath,
        string socketPath,
        CancellationToken cancellationToken)
    {
        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            new[]
            {
                "list",
                "--format",
                "json",
            },
            null,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw CreateCliFailureException("读取 pane 列表失败", result);
        }

        return ParsePanes(result.StandardOutput);
    }

    private static bool IsManagedWorktreePath(string gitRoot, string workingDirectory)
    {
        var managedRoot = Path.GetFullPath(Path.Combine(gitRoot, ".worktrees")) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(workingDirectory);
        return candidate.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> TryResolveGitRootAsync(string gitPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = gitPath,
                Arguments =
                [
                    "rev-parse",
                    "--show-toplevel"
                ],
                WorkingDirectory = workingDirectory,
                Timeout = TimeSpan.FromSeconds(10),
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        return result.StandardOutput.Trim();
    }

    private static HashSet<string> ParseRegisteredWorktreePaths(string output)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                continue;
            }

            var path = line["worktree ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            paths.Add(Path.GetFullPath(path));
        }

        return paths;
    }

    private static bool HasOnlyManagedTaskMdChanges(string gitStatusOutput)
    {
        var changedPaths = gitStatusOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Replace('\\', '/'))
            .ToArray();

        return changedPaths.Length > 0 &&
               changedPaths.All(static path =>
                   string.Equals(path, "task.md", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, ".aipair/task.md", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildWorktreeBranchName(LaunchRequest request)
    {
        var seed = string.IsNullOrWhiteSpace(request.WorktreeBranchName)
            ? request.Workspace
            : request.WorktreeBranchName;
        var normalized = Slugify(seed);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        }

        return normalized;
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static int ExtractGuiPid(string socketPath)
    {
        var fileName = Path.GetFileName(socketPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return 0;
        }

        var parts = fileName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && int.TryParse(parts[^1], out var guiPid) ? guiPid : 0;
    }

    private static IReadOnlyList<PaneInfo> SelectMainPanes(IReadOnlyList<PaneInfo> panes)
    {
        return panes
            .GroupBy(static pane => pane.LeftCol)
            .OrderBy(static group => group.Key)
            .Select(static group => group
                .OrderByDescending(static pane => pane.Rows)
                .ThenBy(static pane => pane.PaneId)
                .First())
            .Take(2)
            .ToArray();
    }

    private static ManagedWorkspaceInfo? FindBestReconnectWorkspace(
        IReadOnlyList<ManagedWorkspaceInfo> managedWorkspaces,
        ManagedSessionRecord sessionRecord)
    {
        return managedWorkspaces
            .Select(workspace => new
            {
                Workspace = workspace,
                Score = ScoreReconnectWorkspace(workspace, sessionRecord),
            })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Workspace.Panes.Count)
            .Select(static candidate => candidate.Workspace)
            .FirstOrDefault();
    }

    private static int ScoreReconnectWorkspace(ManagedWorkspaceInfo workspace, ManagedSessionRecord sessionRecord)
    {
        var score = 0;

        if (string.Equals(workspace.Workspace, sessionRecord.Session.Workspace, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(sessionRecord.RuntimeBinding.SocketPath) &&
            string.Equals(workspace.SocketPath, sessionRecord.RuntimeBinding.SocketPath, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        var paneIds = workspace.Panes.Select(static pane => pane.PaneId).ToHashSet();
        if (paneIds.Contains(sessionRecord.RuntimeBinding.LeftPaneId) &&
            paneIds.Contains(sessionRecord.RuntimeBinding.RightPaneId))
        {
            score += 70;
        }

        var normalizedWorkingDirectory = NormalizePath(sessionRecord.Session.WorkingDirectory);
        if (normalizedWorkingDirectory is null)
        {
            return score;
        }

        var mainPanes = SelectMainPanes(workspace.Panes);
        if (mainPanes.Any(pane => PathsEqual(pane.CurrentDirectory, normalizedWorkingDirectory)))
        {
            score += 60;
        }

        if (workspace.Panes.Any(pane => PathsEqual(pane.CurrentDirectory, normalizedWorkingDirectory)))
        {
            score += 30;
        }

        var workingDirectoryName = Path.GetFileName(normalizedWorkingDirectory);
        if (!string.IsNullOrWhiteSpace(workingDirectoryName) &&
            workspace.Panes.Any(pane =>
            {
                var panePath = NormalizePath(pane.CurrentDirectory);
                return !string.IsNullOrWhiteSpace(panePath) &&
                       string.Equals(Path.GetFileName(panePath), workingDirectoryName, StringComparison.OrdinalIgnoreCase);
            }))
        {
            score += 10;
        }

        return score;
    }

    private static IReadOnlyList<PaneInfo> SelectKnownMainPanes(IReadOnlyList<PaneInfo> panes, ManagedSessionRecord sessionRecord)
    {
        var preferredPaneIds = new[]
        {
            sessionRecord.RuntimeBinding.LeftPaneId,
            sessionRecord.RuntimeBinding.RightPaneId,
            sessionRecord.Session.LeftPaneId,
            sessionRecord.Session.RightPaneId,
        };

        return preferredPaneIds
            .Where(static paneId => paneId > 0)
            .Distinct()
            .Select(paneId => panes.FirstOrDefault(pane => pane.PaneId == paneId))
            .Where(static pane => pane is not null)
            .Cast<PaneInfo>()
            .Take(2)
            .ToArray();
    }

    private static bool PathsEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft) &&
               !string.IsNullOrWhiteSpace(normalizedRight) &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static int? SelectObserverPane(IReadOnlyList<PaneInfo> panes, int leftCol, int mainPaneId)
    {
        return panes
            .Where(pane => pane.LeftCol == leftCol && pane.PaneId != mainPaneId)
            .OrderBy(static pane => pane.Rows)
            .Select(static pane => (int?)pane.PaneId)
            .FirstOrDefault();
    }

    private static string QuoteForPowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static IReadOnlyList<PaneInfo> ParsePanes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var panes = new List<PaneInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("workspace", out var workspaceProp))
            {
                continue;
            }

            var size = item.TryGetProperty("size", out var sizeProp) ? sizeProp : default;
            panes.Add(new PaneInfo
            {
                PaneId = item.GetProperty("pane_id").GetInt32(),
                Workspace = workspaceProp.GetString() ?? string.Empty,
                Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                CurrentDirectory = item.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() : null,
                IsActive = item.TryGetProperty("is_active", out var activeProp) && activeProp.GetBoolean(),
                LeftCol = item.TryGetProperty("left_col", out var leftColProp) ? leftColProp.GetInt32() : 0,
                Rows = size.ValueKind == JsonValueKind.Object && size.TryGetProperty("rows", out var rowsProp) ? rowsProp.GetInt32() : 0,
                Cols = size.ValueKind == JsonValueKind.Object && size.TryGetProperty("cols", out var colsProp) ? colsProp.GetInt32() : 0,
            });
        }

        return panes;
    }
}
