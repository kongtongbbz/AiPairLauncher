using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class WezTermService : IWezTermService
{
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
        var claudeCommand = BuildClaudeShellCommand(request);
        var codexCommand = BuildCodexShellCommand(request);
        var existingGuiPids = Process.GetProcessesByName("wezterm-gui").Select(p => p.Id).ToHashSet();

        var startResult = await _processRunner.RunAsync(
            new ProcessCommand
            {
                FileName = wezTermPath,
                Arguments = BuildStartArguments(workspace, request, claudeCommand),
                Timeout = TimeSpan.FromSeconds(request.StartupTimeoutSeconds),
                WaitForExit = false,
            },
            cancellationToken).ConfigureAwait(false);

        var guiPid = await WaitForNewGuiPidAsync(existingGuiPids, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        var socketPath = BuildSocketPath(guiPid);
        await WaitForSocketAsync(socketPath, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);

        var firstPane = await WaitForFirstPaneAsync(wezTermPath, socketPath, workspace, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        var rightPaneId = await SplitRightPaneAsync(wezTermPath, socketPath, firstPane.PaneId, request, cancellationToken).ConfigureAwait(false);
        var panes = await WaitForTwoPanesAsync(wezTermPath, socketPath, workspace, request.StartupTimeoutSeconds, cancellationToken).ConfigureAwait(false);

        return new LauncherSession
        {
            Workspace = workspace,
            WorkingDirectory = request.WorkingDirectory,
            WezTermPath = wezTermPath,
            SocketPath = socketPath,
            GuiPid = guiPid,
            LeftPaneId = panes[0].PaneId,
            RightPaneId = panes.Count >= 2 ? panes[1].PaneId : rightPaneId,
            RightPanePercent = request.RightPanePercent,
            CreatedAt = DateTimeOffset.Now,
        };
    }

    public async Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return await ListPanesAsync(session.WezTermPath, session.SocketPath, workspace, cancellationToken).ConfigureAwait(false);
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

        var sourcePaneId = request.SourcePaneId;
        var targetPaneId = request.TargetPaneId;
        if (sourcePaneId is null || targetPaneId is null)
        {
            sourcePaneId = request.FromLeftToRight ? panes[0].PaneId : panes[1].PaneId;
            targetPaneId = request.FromLeftToRight ? panes[1].PaneId : panes[0].PaneId;
        }

        var capturedText = await GetPaneTextAsync(session.WezTermPath, session.SocketPath, sourcePaneId.Value, request.LastLines, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(capturedText))
        {
            throw new InvalidOperationException($"pane {sourcePaneId.Value} 没有可发送内容。");
        }

        var payload = BuildPayload(request.Instruction, sourcePaneId.Value, capturedText);
        await SendPaneTextAsync(session.WezTermPath, session.SocketPath, targetPaneId.Value, payload, false, cancellationToken).ConfigureAwait(false);
        if (request.Submit)
        {
            await SendPaneTextAsync(session.WezTermPath, session.SocketPath, targetPaneId.Value, "\r", true, cancellationToken).ConfigureAwait(false);
        }

        return new ContextTransferResult
        {
            SourcePaneId = sourcePaneId.Value,
            TargetPaneId = targetPaneId.Value,
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
                "--cwd", request.WorkingDirectory,
                "--",
                "powershell.exe",
                "-NoLogo",
                "-NoExit",
                "-Command",
                BuildCodexShellCommand(request),
            },
            null,
            TimeSpan.FromSeconds(request.StartupTimeoutSeconds),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"split-pane 执行失败: {result.StandardError}");
        }

        if (!int.TryParse(result.StandardOutput.Trim(), out var paneId))
        {
            throw new InvalidOperationException($"无法解析 split-pane 返回的 pane id: {result.StandardOutput}");
        }

        return paneId;
    }

    private async Task<IReadOnlyList<PaneInfo>> WaitForTwoPanesAsync(
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
            if (panes.Count >= 2)
            {
                return panes;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待工作区 {workspace} 的双 pane 就绪超时。");
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
            throw new InvalidOperationException($"获取 pane 文本失败: {result.StandardError}");
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
            args.Add(text);
        }

        var result = await RunWezTermCliAsync(
            wezTermPath,
            socketPath,
            args,
            noPaste ? null : text,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"发送 pane 文本失败: {result.StandardError}");
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
            throw new InvalidOperationException($"读取 pane 列表失败: {result.StandardError}");
        }

        return ParsePanes(result.StandardOutput, workspace);
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

    private static IReadOnlyList<PaneInfo> ParsePanes(string json, string workspace)
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

            var paneWorkspace = workspaceProp.GetString();
            if (!string.Equals(paneWorkspace, workspace, StringComparison.Ordinal))
            {
                continue;
            }

            var size = item.TryGetProperty("size", out var sizeProp) ? sizeProp : default;
            panes.Add(new PaneInfo
            {
                PaneId = item.GetProperty("pane_id").GetInt32(),
                Workspace = paneWorkspace ?? string.Empty,
                Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                CurrentDirectory = item.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() : null,
                IsActive = item.TryGetProperty("is_active", out var activeProp) && activeProp.GetBoolean(),
                LeftCol = item.TryGetProperty("left_col", out var leftColProp) ? leftColProp.GetInt32() : 0,
                Rows = size.ValueKind == JsonValueKind.Object && size.TryGetProperty("rows", out var rowsProp) ? rowsProp.GetInt32() : 0,
                Cols = size.ValueKind == JsonValueKind.Object && size.TryGetProperty("cols", out var colsProp) ? colsProp.GetInt32() : 0,
            });
        }

        return panes
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

    private List<string> BuildStartArguments(string workspace, LaunchRequest request, string claudeCommand)
    {
        var args = BuildGlobalArguments();
        args.AddRange(
        [
            "start",
            "--always-new-process",
            "--workspace",
            workspace,
            "--cwd",
            request.WorkingDirectory,
            "--",
            "powershell.exe",
            "-NoLogo",
            "-NoExit",
            "-Command",
            claudeCommand,
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

    private string BuildClaudeShellCommand(LaunchRequest request)
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

    private string BuildCodexShellCommand(LaunchRequest request)
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

    private static string QuoteForPowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
