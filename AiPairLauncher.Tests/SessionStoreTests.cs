using System.IO;
using System.Text.Json;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _appDataPath;

    public SessionStoreTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        _appDataPath = Path.Combine(_rootPath, "AppData", "AiPairLauncher");
        Directory.CreateDirectory(_appDataPath);
    }

    [Fact(DisplayName = "test_save_session_creates_multi_session_catalog")]
    public async Task SaveSessionCreatesMultiSessionCatalogAsync()
    {
        var store = new SessionStore(_appDataPath);
        var firstSession = CreateSession("workspace-a");
        var secondSession = CreateSession("workspace-b");

        await store.SaveAsync(firstSession);
        await store.SaveAsync(secondSession);

        var sessions = await store.ListAsync();
        var loadedSession = await store.LoadAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Equal(secondSession.SessionId, loadedSession?.SessionId);
        Assert.True(File.Exists(store.DatabasePath));
    }

    [Fact(DisplayName = "test_list_sessions_imports_legacy_session_file")]
    public async Task ListSessionsImportsLegacySessionFileAsync()
    {
        var store = new SessionStore(_appDataPath);
        var legacySession = CreateSession("legacy-workspace");
        var json = JsonSerializer.Serialize(legacySession, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });

        await File.WriteAllTextAsync(store.LegacySessionFilePath, json);

        var sessions = await store.ListAsync();

        Assert.Single(sessions);
        Assert.Equal("legacy-workspace", sessions[0].DisplayName);
        Assert.True(File.Exists(store.DatabasePath));
    }

    [Fact(DisplayName = "test_save_all_preserves_selected_session")]
    public async Task SaveAllPreservesSelectedSessionAsync()
    {
        var store = new SessionStore(_appDataPath);
        var firstRecord = new ManagedSessionRecord
        {
            Session = CreateSession("workspace-a"),
            DisplayName = "workspace-a",
            HealthStatus = SessionHealthStatus.Idle,
            HealthDetail = "在线",
            LastSummary = "会话 A",
            LastSeenAt = DateTimeOffset.Now.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.Now.AddMinutes(-5),
        };
        var secondRecord = new ManagedSessionRecord
        {
            Session = CreateSession("workspace-b"),
            DisplayName = "workspace-b",
            HealthStatus = SessionHealthStatus.Waiting,
            HealthDetail = "等待审批",
            LastSummary = "会话 B",
            LastSeenAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };

        await store.SaveAllAsync([firstRecord, secondRecord], secondRecord.SessionId);

        var loadedSession = await store.LoadAsync();
        var sessions = await store.ListAsync();

        Assert.Equal(secondRecord.SessionId, loadedSession?.SessionId);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(SessionHealthStatus.Waiting, sessions.Single(x => x.SessionId == secondRecord.SessionId).HealthStatus);
    }

    [Fact(DisplayName = "test_launch_profile_persists_in_state_database")]
    public async Task LaunchProfilePersistsInStateDatabaseAsync()
    {
        var store = new SessionStore(_appDataPath);
        var profile = new LaunchProfile
        {
            Name = "自动闭环",
            Description = "测试模板",
            ClaudePermissionMode = "plan",
            CodexMode = "full-auto",
            AutomationEnabled = true,
            WorktreeStrategy = "subdirectory",
        };

        await store.SaveLaunchProfileAsync(profile);

        var profiles = await store.ListProfilesAsync();

        Assert.Contains(profiles, item => item.ProfileId == profile.ProfileId && item.WorktreeStrategy == "subdirectory");
    }

    [Fact(DisplayName = "test_builtin_launch_profiles_are_listed_in_selector")]
    public async Task BuiltinLaunchProfilesAreListedInSelectorAsync()
    {
        var store = new SessionStore(_appDataPath);

        var profiles = await store.ListProfilesAsync();

        Assert.Contains(profiles, item => item.Name == "标准协作" && item.IsBuiltIn);
        Assert.Contains(profiles, item => item.Name == "安全审批" && item.IsBuiltIn);
        Assert.Contains(profiles, item => item.Name == "自动闭环" && item.IsBuiltIn);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static LauncherSession CreateSession(string workspace)
    {
        return new LauncherSession
        {
            Workspace = workspace,
            WorkingDirectory = Path.Combine("C:\\temp", workspace),
            WezTermPath = "wezterm.exe",
            SocketPath = $"socket-{workspace}",
            GuiPid = 1234,
            LeftPaneId = 1,
            RightPaneId = 2,
            RightPanePercent = 60,
            ClaudePermissionMode = "plan",
            CodexMode = "full-auto",
            AutomationEnabledAtLaunch = true,
            CreatedAt = DateTimeOffset.Now,
        };
    }
}
