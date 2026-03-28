using System.Collections.Concurrent;
using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

[CollectionDefinition("EnvironmentSensitive", DisableParallelization = true)]
public sealed class EnvironmentSensitiveCollection
{
}

[Collection("EnvironmentSensitive")]
public sealed class WezTermServiceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _toolPath;
    private readonly string? _originalPath;
    private readonly string? _originalUserProfile;
    private readonly List<string> _createdSocketFiles = [];

    public WezTermServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "AiPairLauncher.WezTermTests", Guid.NewGuid().ToString("N"));
        _toolPath = Path.Combine(_rootPath, "tools");
        Directory.CreateDirectory(_toolPath);
        File.WriteAllText(Path.Combine(_toolPath, "wezterm.exe"), string.Empty);
        File.WriteAllText(Path.Combine(_toolPath, "git.exe"), string.Empty);
        _originalPath = Environment.GetEnvironmentVariable("PATH");
        _originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("PATH", $"{_toolPath}{Path.PathSeparator}{_originalPath}");
        Environment.SetEnvironmentVariable("USERPROFILE", _rootPath);
    }

    [Fact(DisplayName = "test_list_managed_workspaces_returns_grouped_panes")]
    public async Task ListManagedWorkspacesReturnsGroupedPanesAsync()
    {
        var socketDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "wezterm");
        Directory.CreateDirectory(socketDir);
        var socketPath = Path.Combine(socketDir, "gui-sock-101");
        await File.WriteAllTextAsync(socketPath, string.Empty);
        _createdSocketFiles.Add(socketPath);

        var runner = new FakeProcessRunner((command) =>
        {
            if (command.Arguments.Contains("list") &&
                command.EnvironmentVariables is not null &&
                command.EnvironmentVariables.TryGetValue("WEZTERM_UNIX_SOCKET", out var currentSocket) &&
                string.Equals(currentSocket, socketPath, StringComparison.Ordinal))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = """
[
  { "pane_id": 1, "workspace": "ai-pair-alpha", "left_col": 0, "is_active": true, "cwd": "D:/repo", "size": { "rows": 40, "cols": 100 } },
  { "pane_id": 2, "workspace": "ai-pair-alpha", "left_col": 100, "is_active": false, "cwd": "D:/repo", "size": { "rows": 40, "cols": 100 } }
]
""",
                    StandardError = string.Empty,
                });
            }

            throw new InvalidOperationException("unexpected command");
        });
        var service = new WezTermService(new CommandLocator(), runner);

        var workspaces = await service.ListManagedWorkspacesAsync();

        Assert.Single(workspaces);
        Assert.Equal("ai-pair-alpha", workspaces[0].Workspace);
        Assert.Equal(2, workspaces[0].Panes.Count);
    }

    [Fact(DisplayName = "test_try_reconnect_session_updates_runtime_binding_when_workspace_matches")]
    public async Task TryReconnectSessionUpdatesRuntimeBindingWhenWorkspaceMatchesAsync()
    {
        var socketDir = Path.Combine(_rootPath, ".local", "share", "wezterm");
        Directory.CreateDirectory(socketDir);
        var socketPath = Path.Combine(socketDir, "gui-sock-202");
        await File.WriteAllTextAsync(socketPath, string.Empty);
        _createdSocketFiles.Add(socketPath);

        var runner = new FakeProcessRunner((command) =>
        {
            if (command.Arguments.Contains("list"))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = """
[
  { "pane_id": 10, "workspace": "test-workspace", "left_col": 0, "is_active": true, "cwd": "D:/repo/.worktrees/task", "size": { "rows": 40, "cols": 100 } },
  { "pane_id": 11, "workspace": "test-workspace", "left_col": 0, "is_active": false, "cwd": "D:/repo/.worktrees/task", "size": { "rows": 18, "cols": 100 } },
  { "pane_id": 20, "workspace": "test-workspace", "left_col": 100, "is_active": false, "cwd": "D:/repo/.worktrees/task", "size": { "rows": 40, "cols": 100 } },
  { "pane_id": 21, "workspace": "test-workspace", "left_col": 100, "is_active": false, "cwd": "D:/repo/.worktrees/task", "size": { "rows": 18, "cols": 100 } }
]
""",
                    StandardError = string.Empty,
                });
            }

            throw new InvalidOperationException("unexpected command");
        });
        var service = new WezTermService(new CommandLocator(), runner);
        var record = new ManagedSessionRecord
        {
            Session = AutomationTestHelpers.CreateSession(),
            DisplayName = "test",
        };

        var result = await service.TryReconnectSessionAsync(record);

        Assert.True(result.Success);
        Assert.NotNull(result.Session);
        Assert.Equal(10, result.Session!.LeftPaneId);
        Assert.Equal(20, result.Session.RightPaneId);
        Assert.Equal(11, result.Session.ClaudeObserverPaneId);
        Assert.Equal(21, result.Session.CodexObserverPaneId);
        Assert.NotNull(result.RuntimeBinding);
        Assert.True(result.RuntimeBinding!.IsAlive);
    }

    [Fact(DisplayName = "test_focus_pane_calls_activate_pane_for_selected_session")]
    public async Task FocusPaneCallsActivatePaneForSelectedSessionAsync()
    {
        var runner = new FakeProcessRunner((command) =>
        {
            return Task.FromResult(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = string.Empty,
            });
        });
        var service = new WezTermService(new CommandLocator(), runner);

        await service.FocusPaneAsync(AutomationTestHelpers.CreateSession(), 77);

        Assert.Contains(runner.Commands, command => command.Arguments.Contains("activate-pane") && command.Arguments.Contains("77"));
    }

    [Fact(DisplayName = "test_create_worktree_launch_context_uses_subdirectory_strategy")]
    public async Task CreateWorktreeLaunchContextUsesSubdirectoryStrategyAsync()
    {
        var repoPath = Path.Combine(_rootPath, "repo");
        Directory.CreateDirectory(repoPath);
        var runner = new FakeProcessRunner((command) =>
        {
            if (command.Arguments.Contains("rev-parse"))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = repoPath,
                    StandardError = string.Empty,
                });
            }

            if (command.Arguments.Count >= 4 && command.Arguments[0] == "worktree")
            {
                Directory.CreateDirectory(command.Arguments[^1]);
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                });
            }

            throw new InvalidOperationException("unexpected command");
        });
        var service = new WezTermService(new CommandLocator(), runner);
        var context = await service.CreateWorktreeLaunchContextAsync(new LaunchRequest
        {
            Workspace = "feature-x",
            WorkingDirectory = repoPath,
            UseWorktree = true,
            WorktreeStrategy = "subdirectory",
        });

        Assert.True(context.UsedWorktree);
        Assert.Contains(".worktrees", context.WorkingDirectory);
        Assert.Equal("subdirectory", context.WorktreeStrategy);
    }

    [Fact(DisplayName = "test_create_worktree_launch_context_falls_back_when_not_git_repo")]
    public async Task CreateWorktreeLaunchContextFallsBackWhenNotGitRepoAsync()
    {
        var repoPath = Path.Combine(_rootPath, "plain-dir");
        Directory.CreateDirectory(repoPath);
        var runner = new FakeProcessRunner((command) =>
        {
            if (command.Arguments.Contains("rev-parse"))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 1,
                    StandardOutput = string.Empty,
                    StandardError = "not a git repo",
                });
            }

            throw new InvalidOperationException("unexpected command");
        });
        var service = new WezTermService(new CommandLocator(), runner);
        var context = await service.CreateWorktreeLaunchContextAsync(new LaunchRequest
        {
            Workspace = "feature-x",
            WorkingDirectory = repoPath,
            UseWorktree = true,
            WorktreeStrategy = "subdirectory",
        });

        Assert.False(context.UsedWorktree);
        Assert.Equal(repoPath, context.WorkingDirectory);
    }

    [Fact(DisplayName = "test_create_worktree_launch_context_falls_back_when_git_missing")]
    public async Task CreateWorktreeLaunchContextFallsBackWhenGitMissingAsync()
    {
        var isolatedPath = Path.Combine(_rootPath, "no-git-tools");
        Directory.CreateDirectory(isolatedPath);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", isolatedPath);
        var repoPath = Path.Combine(_rootPath, "repo2");
        Directory.CreateDirectory(repoPath);
        try
        {
            var runner = new FakeProcessRunner((_) => throw new InvalidOperationException("runner should not be called"));
            var service = new WezTermService(new CommandLocator(), runner);
            var context = await service.CreateWorktreeLaunchContextAsync(new LaunchRequest
            {
                Workspace = "feature-x",
                WorkingDirectory = repoPath,
                UseWorktree = true,
                WorktreeStrategy = "subdirectory",
            });

            Assert.False(context.UsedWorktree);
            Assert.Equal(repoPath, context.WorkingDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact(DisplayName = "test_send_automation_prompt_forces_claude_skip_permissions")]
    public async Task SendAutomationPromptForcesClaudeSkipPermissionsAsync()
    {
        var runner = new FakeProcessRunner((command) => Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = "ok",
            StandardError = string.Empty,
        }));
        File.WriteAllText(Path.Combine(_toolPath, "claude.exe"), string.Empty);
        var service = new WezTermService(new CommandLocator(), runner);

        await service.SendAutomationPromptAsync(AutomationTestHelpers.CreateSession(), AgentRole.Claude, "hello", submit: false);

        var sendTextCommand = runner.Commands.Last();
        Assert.Contains("send-text", sendTextCommand.Arguments);
        Assert.NotNull(sendTextCommand.StandardInput);
        Assert.Contains("--dangerously-skip-permissions", sendTextCommand.StandardInput);
    }

    [Fact(DisplayName = "test_send_automation_prompt_forces_codex_full_auto")]
    public async Task SendAutomationPromptForcesCodexFullAutoAsync()
    {
        var runner = new FakeProcessRunner((command) => Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = "ok",
            StandardError = string.Empty,
        }));
        File.WriteAllText(Path.Combine(_toolPath, "codex.cmd"), string.Empty);
        var service = new WezTermService(new CommandLocator(), runner);

        await service.SendAutomationPromptAsync(AutomationTestHelpers.CreateSession(), AgentRole.Codex, "hello", submit: false);

        var sendTextCommand = runner.Commands.Last();
        Assert.Contains("send-text", sendTextCommand.Arguments);
        Assert.NotNull(sendTextCommand.StandardInput);
        Assert.Contains("exec --full-auto", sendTextCommand.StandardInput);
    }

    [Fact(DisplayName = "test_cleanup_worktree_allows_taskmd_only_changes")]
    public async Task CleanupWorktreeAllowsTaskMdOnlyChangesAsync()
    {
        var repoPath = Path.Combine(_rootPath, "repo-cleanup");
        var worktreePath = Path.Combine(repoPath, ".worktrees", "feature-a");
        Directory.CreateDirectory(worktreePath);

        var runner = new FakeProcessRunner((command) =>
        {
            if (command.Arguments.Contains("rev-parse"))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = repoPath,
                    StandardError = string.Empty,
                });
            }

            if (command.Arguments.SequenceEqual(new[] { "status", "--porcelain" }))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "?? task.md",
                    StandardError = string.Empty,
                });
            }

            if (command.Arguments.SequenceEqual(new[] { "branch", "--show-current" }))
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "feature-a",
                    StandardError = string.Empty,
                });
            }

            if (command.Arguments.Count >= 3 && command.Arguments[0] == "worktree" && command.Arguments[1] == "remove")
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                });
            }

            if (command.Arguments.Count >= 3 && command.Arguments[0] == "branch" && command.Arguments[1] == "-d")
            {
                return Task.FromResult(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                });
            }

            throw new InvalidOperationException("unexpected command");
        });
        var service = new WezTermService(new CommandLocator(), runner);

        var result = await service.CleanupWorktreeAsync(worktreePath);

        Assert.True(result.Success);
        Assert.True(result.WorktreeRemoved);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("USERPROFILE", _originalUserProfile);
        foreach (var socketFile in _createdSocketFiles)
        {
            if (File.Exists(socketFile))
            {
                File.Delete(socketFile);
            }
        }

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessCommand, Task<ProcessResult>> _handler;

        public FakeProcessRunner(Func<ProcessCommand, Task<ProcessResult>> handler)
        {
            _handler = handler;
        }

        public ConcurrentBag<ProcessCommand> Commands { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return await _handler(command).ConfigureAwait(false);
        }
    }
}
