using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiPairLauncher.App.Models;
using Microsoft.Data.Sqlite;

namespace AiPairLauncher.App.Services;

public sealed class SessionStore : ISessionStore
{
    private const string SelectedSessionKey = "selected_session_id";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions AutomationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public string LegacySessionFilePath { get; }

    public string DatabasePath { get; }

    public string StateDatabasePath => DatabasePath;

    public SessionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiPairLauncher"))
    {
    }

    public SessionStore(string storeDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeDirectoryPath);
        LegacySessionFilePath = Path.Combine(storeDirectoryPath, "session.json");
        DatabasePath = Path.Combine(storeDirectoryPath, "state.db");
    }

    public async Task SaveAsync(LauncherSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var existingRecord = await GetByIdAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
        var record = existingRecord ?? CreateRecord(session);
        record.Session = session.Clone();
        record.DisplayName = BuildDisplayName(session);
        record.RuntimeBinding = CreateRuntimeBinding(session);
        record.HealthStatus = SessionHealthStatus.Idle;
        record.HealthDetail = "会话已启动";
        record.LastSummary = "会话已启动";
        record.LastError = null;
        record.LastSeenAt = DateTimeOffset.Now;
        record.UpdatedAt = DateTimeOffset.Now;
        record.IsArchived = false;

        await UpsertRecordAsync(record, session.SessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LauncherSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var selectedSessionId = await ReadAppStateAsync(connection, SelectedSessionKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(selectedSessionId))
        {
            var selectedRecord = await GetByIdAsync(connection, selectedSessionId!, cancellationToken).ConfigureAwait(false);
            if (selectedRecord is not null && !selectedRecord.IsArchived)
            {
                await SaveLegacySessionAsync(selectedRecord.Session, cancellationToken).ConfigureAwait(false);
                return selectedRecord.Session.Clone();
            }
        }

        var firstRecord = (await ListInternalAsync(connection, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(static item => !item.IsArchived);
        if (firstRecord is null)
        {
            return null;
        }

        await WriteAppStateAsync(connection, SelectedSessionKey, firstRecord.SessionId, cancellationToken).ConfigureAwait(false);
        await SaveLegacySessionAsync(firstRecord.Session, cancellationToken).ConfigureAwait(false);
        return firstRecord.Session.Clone();
    }

    public async Task<IReadOnlyList<ManagedSessionRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ListInternalAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAllAsync(
        IReadOnlyList<ManagedSessionRecord> sessionRecords,
        string? selectedSessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionRecords);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in sessionRecords)
        {
            await UpsertRecordAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(selectedSessionId))
        {
            await WriteAppStateAsync(connection, SelectedSessionKey, selectedSessionId, cancellationToken, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(selectedSessionId))
        {
            var selectedRecord = sessionRecords.FirstOrDefault(record =>
                string.Equals(record.SessionId, selectedSessionId, StringComparison.Ordinal));
            if (selectedRecord is not null)
            {
                await SaveLegacySessionAsync(selectedRecord.Session, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task SaveLaunchProfileAsync(LaunchProfile launchProfile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchProfile);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = CreateLaunchProfileUpsertCommand(connection, launchProfile, null);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LaunchProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                profile_id, name, description, working_directory, workspace_prefix,
                default_group_name, transfer_instruction_template, default_panel_preset,
                claude_permission_mode, codex_mode, right_pane_percent,
                automation_enabled, automation_observer_enabled, automation_advance_policy,
                automation_poll_interval_ms, automation_capture_lines, automation_timeout_seconds,
                automation_max_auto_stages, automation_max_retry_per_stage, automation_submit_on_send,
                worktree_strategy, created_at, updated_at
            FROM launch_profiles
            ORDER BY name;
            """;

        var profiles = new List<LaunchProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(MapLaunchProfile(reader));
        }

        return profiles;
    }

    public async Task SaveAutomationEventAsync(AutomationEventRecord eventRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventRecord);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO automation_events (
                event_id, session_id, phase, status, stage_id, task_ref, task_md_path, task_md_status, status_detail, last_packet_summary,
                last_error, intervention_reason, pending_approval_json,
                auto_approved_stage_count, current_stage_retry_count, occurred_at
            )
            VALUES (
                $event_id, $session_id, $phase, $status, $stage_id, $task_ref, $task_md_path, $task_md_status, $status_detail, $last_packet_summary,
                $last_error, $intervention_reason, $pending_approval_json,
                $auto_approved_stage_count, $current_stage_retry_count, $occurred_at
            );
            """;
        AddParameter(command, "$event_id", eventRecord.EventId);
        AddParameter(command, "$session_id", eventRecord.SessionId);
        AddParameter(command, "$phase", eventRecord.Phase.ToString());
        AddParameter(command, "$status", eventRecord.Status.ToString());
        AddParameter(command, "$stage_id", eventRecord.StageId);
        AddParameter(command, "$task_ref", eventRecord.TaskRef);
        AddParameter(command, "$task_md_path", eventRecord.TaskMdPath);
        AddParameter(command, "$task_md_status", eventRecord.TaskMdStatus.ToString());
        AddParameter(command, "$status_detail", eventRecord.StatusDetail);
        AddParameter(command, "$last_packet_summary", eventRecord.LastPacketSummary);
        AddParameter(command, "$last_error", eventRecord.LastError);
        AddParameter(command, "$intervention_reason", eventRecord.InterventionReason);
        AddParameter(command, "$pending_approval_json", SerializeJson(eventRecord.PendingApproval));
        AddParameter(command, "$auto_approved_stage_count", eventRecord.AutoApprovedStageCount);
        AddParameter(command, "$current_stage_retry_count", eventRecord.CurrentStageRetryCount);
        AddParameter(command, "$occurred_at", ToDbString(eventRecord.OccurredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AutomationEventRecord>> ListAutomationEventsAsync(
        string sessionId,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                event_id, session_id, phase, status, stage_id, task_ref, task_md_path, task_md_status, status_detail, last_packet_summary,
                last_error, intervention_reason, pending_approval_json,
                auto_approved_stage_count, current_stage_retry_count, occurred_at
            FROM automation_events
            WHERE session_id = $session_id
            ORDER BY occurred_at DESC
            LIMIT $take;
            """;
        AddParameter(command, "$session_id", sessionId);
        AddParameter(command, "$take", Math.Max(1, take));

        var items = new List<AutomationEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new AutomationEventRecord
            {
                EventId = reader.GetString(0),
                SessionId = reader.GetString(1),
                Phase = ParseAutomationPhase(ReadNullableString(reader, 2)),
                Status = Enum.TryParse<AutomationStageStatus>(reader.GetString(3), out var status) ? status : AutomationStageStatus.Idle,
                StageId = ReadNullableInt(reader, 4),
                TaskRef = ReadNullableString(reader, 5),
                TaskMdPath = ReadNullableString(reader, 6),
                TaskMdStatus = ParseTaskMdStatus(ReadNullableString(reader, 7)),
                StatusDetail = reader.GetString(8),
                LastPacketSummary = reader.GetString(9),
                LastError = ReadNullableString(reader, 10),
                InterventionReason = ReadNullableString(reader, 11),
                PendingApproval = DeserializeJson<ApprovalDraft>(ReadNullableString(reader, 12)),
                AutoApprovedStageCount = reader.GetInt32(13),
                CurrentStageRetryCount = reader.GetInt32(14),
                OccurredAt = ParseDbDateTime(reader.GetString(15)),
            });
        }

        return items;
    }

    public async Task SaveAutomationSnapshotAsync(PersistedAutomationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO automation_snapshots (
                session_id, settings_json, state_json, updated_at
            )
            VALUES (
                $session_id, $settings_json, $state_json, $updated_at
            )
            ON CONFLICT(session_id) DO UPDATE SET
                settings_json = excluded.settings_json,
                state_json = excluded.state_json,
                updated_at = excluded.updated_at;
            """;
        AddParameter(command, "$session_id", snapshot.SessionId);
        AddParameter(command, "$settings_json", SerializeJson(snapshot.Settings));
        AddParameter(command, "$state_json", SerializeJson(snapshot.State));
        AddParameter(command, "$updated_at", ToDbString(snapshot.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersistedAutomationSnapshot?> GetAutomationSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT session_id, settings_json, state_json, updated_at
            FROM automation_snapshots
            WHERE session_id = $session_id;
            """;
        AddParameter(command, "$session_id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PersistedAutomationSnapshot
        {
            SessionId = reader.GetString(0),
            Settings = DeserializeJson<AutomationSettings>(reader.GetString(1)) ?? new AutomationSettings(),
            State = DeserializeJson<AutomationRunState>(reader.GetString(2)) ?? new AutomationRunState(),
            UpdatedAt = ParseDbDateTime(reader.GetString(3)),
        };
    }

    public async Task ClearAutomationSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automation_snapshots WHERE session_id = $session_id;";
        AddParameter(command, "$session_id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var record = await GetByIdAsync(connection, sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        await WriteAppStateAsync(connection, SelectedSessionKey, sessionId, cancellationToken).ConfigureAwait(false);
        await SaveLegacySessionAsync(record.Session, cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(LegacySessionFilePath))
        {
            File.Delete(LegacySessionFilePath);
        }

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }

        return Task.CompletedTask;
    }

    public async Task<ManagedSessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return await GetByIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionRecord);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await UpsertRecordAsync(sessionRecord.Clone(), sessionRecord.SessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameSessionAsync(string sessionId, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.DisplayName = displayName.Trim();
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPinnedAsync(string sessionId, bool isPinned, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.IsPinned = isPinned;
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveToGroupAsync(string sessionId, string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.GroupName = groupName.Trim();
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.IsArchived = true;
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.IsArchived = false;
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE session_id = $session_id;";
        AddParameter(command, "$session_id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LaunchProfile?> DuplicateSessionConfigAsync(string sessionId, string profileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var profile = new LaunchProfile
        {
            Name = profileName.Trim(),
            Description = $"从会话 {record.DisplayName} 复制启动配置",
            WorkingDirectory = record.Session.WorkingDirectory,
            WorkspacePrefix = record.Session.Workspace,
            DefaultGroupName = record.GroupName,
            ClaudePermissionMode = record.Session.ClaudePermissionMode,
            CodexMode = record.Session.CodexMode,
            RightPanePercent = record.Session.RightPanePercent,
            AutomationEnabled = record.Session.AutomationEnabledAtLaunch,
            AutomationObserverEnabled = record.Session.AutomationObserverEnabled,
            AutomationAdvancePolicy = "full-auto-loop",
            WorktreeStrategy = record.UsesWorktree ? "subdirectory" : "none",
            DefaultUseWorktree = record.UsesWorktree,
            DefaultWorktreeStrategy = record.UsesWorktree ? "subdirectory" : "none",
        };

        await SaveLaunchProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<string?> GetSelectedSessionIdAsync(CancellationToken cancellationToken = default)
    {
        return await GetAppStateAsync(SelectedSessionKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSelectedSessionIdAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        await SetAppStateAsync(SelectedSessionKey, sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAppStateAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAppStateAsync(connection, key, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAppStateAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await WriteAppStateAsync(connection, key, value, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateRuntimeBindingAsync(SessionRuntimeBinding runtimeBinding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeBinding);
        var record = await GetAsync(runtimeBinding.SessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.RuntimeBinding = runtimeBinding.Clone();
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRuntimeBindingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM session_runtime_bindings WHERE session_id = $session_id;";
        AddParameter(command, "$session_id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStatusSnapshotAsync(SessionStatusSnapshot statusSnapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statusSnapshot);
        var record = await GetAsync(statusSnapshot.SessionId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.StatusSnapshot = statusSnapshot.Clone();
        record.UpdatedAt = DateTimeOffset.Now;
        await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SessionStatusSnapshot?> GetStatusSnapshotAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return record?.StatusSnapshot.Clone();
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定 state.db 的父目录。");
        }

        Directory.CreateDirectory(directory);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                group_name TEXT NOT NULL,
                is_pinned INTEGER NOT NULL,
                is_archived INTEGER NOT NULL,
                launch_profile_id TEXT NULL,
                workspace TEXT NOT NULL,
                working_directory TEXT NOT NULL,
                wezterm_path TEXT NOT NULL,
                socket_path TEXT NOT NULL,
                gui_pid INTEGER NOT NULL,
                left_pane_id INTEGER NOT NULL,
                right_pane_id INTEGER NOT NULL,
                right_pane_percent INTEGER NOT NULL,
                claude_observer_pane_id INTEGER NULL,
                codex_observer_pane_id INTEGER NULL,
                automation_observer_enabled INTEGER NOT NULL,
                claude_permission_mode TEXT NOT NULL,
                codex_mode TEXT NOT NULL,
                automation_enabled_at_launch INTEGER NOT NULL,
                session_created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS session_runtime_bindings (
                session_id TEXT PRIMARY KEY,
                gui_pid INTEGER NOT NULL,
                socket_path TEXT NOT NULL,
                left_pane_id INTEGER NOT NULL,
                right_pane_id INTEGER NOT NULL,
                claude_observer_pane_id INTEGER NULL,
                codex_observer_pane_id INTEGER NULL,
                is_alive INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS session_status_snapshots (
                session_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                status_detail TEXT NOT NULL,
                last_activity_at TEXT NOT NULL,
                last_error TEXT NULL,
                last_summary TEXT NOT NULL,
                claude_preview TEXT NOT NULL DEFAULT '暂无输出',
                codex_preview TEXT NOT NULL DEFAULT '暂无输出',
                needs_approval INTEGER NOT NULL,
                automation_phase TEXT NOT NULL DEFAULT 'None',
                automation_stage_id INTEGER NULL,
                automation_task_ref TEXT NULL,
                task_md_path TEXT NULL,
                task_md_status TEXT NOT NULL DEFAULT 'Unknown',
                automation_retry_count INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS launch_profiles (
                profile_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                working_directory TEXT NULL,
                workspace_prefix TEXT NULL,
                default_group_name TEXT NOT NULL DEFAULT '默认',
                transfer_instruction_template TEXT NOT NULL DEFAULT '请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。',
                default_panel_preset TEXT NOT NULL DEFAULT 'balanced',
                claude_permission_mode TEXT NOT NULL,
                codex_mode TEXT NOT NULL,
                right_pane_percent INTEGER NOT NULL,
                automation_enabled INTEGER NOT NULL,
                automation_observer_enabled INTEGER NOT NULL,
                automation_advance_policy TEXT NOT NULL,
                automation_poll_interval_ms INTEGER NOT NULL,
                automation_capture_lines INTEGER NOT NULL,
                automation_timeout_seconds INTEGER NOT NULL,
                automation_max_auto_stages INTEGER NOT NULL,
                automation_max_retry_per_stage INTEGER NOT NULL,
                automation_submit_on_send INTEGER NOT NULL,
                worktree_strategy TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS automation_events (
                event_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                phase TEXT NOT NULL DEFAULT 'None',
                status TEXT NOT NULL,
                stage_id INTEGER NULL,
                task_ref TEXT NULL,
                task_md_path TEXT NULL,
                task_md_status TEXT NOT NULL DEFAULT 'Unknown',
                status_detail TEXT NOT NULL,
                last_packet_summary TEXT NOT NULL,
                last_error TEXT NULL,
                intervention_reason TEXT NULL,
                pending_approval_json TEXT NULL,
                auto_approved_stage_count INTEGER NOT NULL,
                current_stage_retry_count INTEGER NOT NULL,
                occurred_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS automation_snapshots (
                session_id TEXT PRIMARY KEY,
                settings_json TEXT NOT NULL,
                state_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS app_state (
                state_key TEXT PRIMARY KEY,
                state_value TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "claude_preview", "TEXT NOT NULL DEFAULT '暂无输出'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "codex_preview", "TEXT NOT NULL DEFAULT '暂无输出'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "automation_phase", "TEXT NOT NULL DEFAULT 'None'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "automation_task_ref", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "task_md_path", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "session_status_snapshots", "task_md_status", "TEXT NOT NULL DEFAULT 'Unknown'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "automation_events", "phase", "TEXT NOT NULL DEFAULT 'None'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "automation_events", "task_ref", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "automation_events", "task_md_path", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "automation_events", "task_md_status", "TEXT NOT NULL DEFAULT 'Unknown'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "launch_profiles", "default_group_name", "TEXT NOT NULL DEFAULT '默认'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "launch_profiles", "transfer_instruction_template", "TEXT NOT NULL DEFAULT '请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "launch_profiles", "default_panel_preset", "TEXT NOT NULL DEFAULT 'balanced'", cancellationToken).ConfigureAwait(false);
        await EnsureDefaultLaunchProfilesAsync(connection, cancellationToken).ConfigureAwait(false);
        await ImportLegacySessionIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDefaultLaunchProfilesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM launch_profiles;";
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (count > 0)
        {
            return;
        }

        foreach (var profile in GetDefaultProfiles())
        {
            var command = CreateLaunchProfileUpsertCommand(connection, profile, null);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportLegacySessionIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM sessions;";
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (count > 0 || !File.Exists(LegacySessionFilePath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(LegacySessionFilePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var session = JsonSerializer.Deserialize<LauncherSession>(json, JsonOptions);
        if (session is null)
        {
            return;
        }

        var record = CreateRecord(session);
        await UpsertRecordAsync(connection, null, record, cancellationToken).ConfigureAwait(false);
        await WriteAppStateAsync(connection, SelectedSessionKey, session.SessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ManagedSessionRecord>> ListInternalAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                s.session_id, s.display_name, s.group_name, s.is_pinned, s.is_archived, s.launch_profile_id,
                s.workspace, s.working_directory, s.wezterm_path, s.socket_path, s.gui_pid,
                s.left_pane_id, s.right_pane_id, s.right_pane_percent, s.claude_observer_pane_id,
                s.codex_observer_pane_id, s.automation_observer_enabled, s.claude_permission_mode,
                s.codex_mode, s.automation_enabled_at_launch, s.session_created_at, s.updated_at,
                r.gui_pid, r.socket_path, r.left_pane_id, r.right_pane_id, r.claude_observer_pane_id,
                r.codex_observer_pane_id, r.is_alive, r.updated_at,
                p.status, p.status_detail, p.last_activity_at, p.last_error, p.last_summary,
                p.claude_preview, p.codex_preview,
                p.needs_approval, p.automation_phase, p.automation_stage_id, p.automation_task_ref, p.task_md_path, p.task_md_status, p.automation_retry_count, p.updated_at
            FROM sessions s
            LEFT JOIN session_runtime_bindings r ON r.session_id = s.session_id
            LEFT JOIN session_status_snapshots p ON p.session_id = s.session_id
            ORDER BY s.is_pinned DESC, s.is_archived ASC, COALESCE(p.last_activity_at, s.session_created_at) DESC, s.session_created_at DESC;
            """;

        var records = new List<ManagedSessionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    private async Task<ManagedSessionRecord?> GetByIdAsync(string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetByIdAsync(connection, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedSessionRecord?> GetByIdAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                s.session_id, s.display_name, s.group_name, s.is_pinned, s.is_archived, s.launch_profile_id,
                s.workspace, s.working_directory, s.wezterm_path, s.socket_path, s.gui_pid,
                s.left_pane_id, s.right_pane_id, s.right_pane_percent, s.claude_observer_pane_id,
                s.codex_observer_pane_id, s.automation_observer_enabled, s.claude_permission_mode,
                s.codex_mode, s.automation_enabled_at_launch, s.session_created_at, s.updated_at,
                r.gui_pid, r.socket_path, r.left_pane_id, r.right_pane_id, r.claude_observer_pane_id,
                r.codex_observer_pane_id, r.is_alive, r.updated_at,
                p.status, p.status_detail, p.last_activity_at, p.last_error, p.last_summary,
                p.claude_preview, p.codex_preview,
                p.needs_approval, p.automation_phase, p.automation_stage_id, p.automation_task_ref, p.task_md_path, p.task_md_status, p.automation_retry_count, p.updated_at
            FROM sessions s
            LEFT JOIN session_runtime_bindings r ON r.session_id = s.session_id
            LEFT JOIN session_status_snapshots p ON p.session_id = s.session_id
            WHERE s.session_id = $session_id;
            """;
        AddParameter(command, "$session_id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapRecord(reader);
    }

    private async Task UpsertRecordAsync(ManagedSessionRecord record, string? selectedSessionId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpsertRecordAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(selectedSessionId))
        {
            await WriteAppStateAsync(connection, SelectedSessionKey, selectedSessionId, cancellationToken, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await SaveLegacySessionAsync(record.Session, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertRecordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ManagedSessionRecord record,
        CancellationToken cancellationToken)
    {
        record.RuntimeBinding.SessionId = record.SessionId;
        record.StatusSnapshot.SessionId = record.SessionId;

        var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = transaction;
        sessionCommand.CommandText =
            """
            INSERT INTO sessions (
                session_id, display_name, group_name, is_pinned, is_archived, launch_profile_id,
                workspace, working_directory, wezterm_path, socket_path, gui_pid,
                left_pane_id, right_pane_id, right_pane_percent, claude_observer_pane_id,
                codex_observer_pane_id, automation_observer_enabled, claude_permission_mode,
                codex_mode, automation_enabled_at_launch, session_created_at, updated_at
            )
            VALUES (
                $session_id, $display_name, $group_name, $is_pinned, $is_archived, $launch_profile_id,
                $workspace, $working_directory, $wezterm_path, $socket_path, $gui_pid,
                $left_pane_id, $right_pane_id, $right_pane_percent, $claude_observer_pane_id,
                $codex_observer_pane_id, $automation_observer_enabled, $claude_permission_mode,
                $codex_mode, $automation_enabled_at_launch, $session_created_at, $updated_at
            )
            ON CONFLICT(session_id) DO UPDATE SET
                display_name = excluded.display_name,
                group_name = excluded.group_name,
                is_pinned = excluded.is_pinned,
                is_archived = excluded.is_archived,
                launch_profile_id = excluded.launch_profile_id,
                workspace = excluded.workspace,
                working_directory = excluded.working_directory,
                wezterm_path = excluded.wezterm_path,
                socket_path = excluded.socket_path,
                gui_pid = excluded.gui_pid,
                left_pane_id = excluded.left_pane_id,
                right_pane_id = excluded.right_pane_id,
                right_pane_percent = excluded.right_pane_percent,
                claude_observer_pane_id = excluded.claude_observer_pane_id,
                codex_observer_pane_id = excluded.codex_observer_pane_id,
                automation_observer_enabled = excluded.automation_observer_enabled,
                claude_permission_mode = excluded.claude_permission_mode,
                codex_mode = excluded.codex_mode,
                automation_enabled_at_launch = excluded.automation_enabled_at_launch,
                session_created_at = excluded.session_created_at,
                updated_at = excluded.updated_at;
            """;
        AddParameter(sessionCommand, "$session_id", record.SessionId);
        AddParameter(sessionCommand, "$display_name", record.DisplayName);
        AddParameter(sessionCommand, "$group_name", record.GroupName);
        AddParameter(sessionCommand, "$is_pinned", record.IsPinned);
        AddParameter(sessionCommand, "$is_archived", record.IsArchived);
        AddParameter(sessionCommand, "$launch_profile_id", record.LaunchProfileId);
        AddParameter(sessionCommand, "$workspace", record.Session.Workspace);
        AddParameter(sessionCommand, "$working_directory", record.Session.WorkingDirectory);
        AddParameter(sessionCommand, "$wezterm_path", record.Session.WezTermPath);
        AddParameter(sessionCommand, "$socket_path", record.Session.SocketPath);
        AddParameter(sessionCommand, "$gui_pid", record.Session.GuiPid);
        AddParameter(sessionCommand, "$left_pane_id", record.Session.LeftPaneId);
        AddParameter(sessionCommand, "$right_pane_id", record.Session.RightPaneId);
        AddParameter(sessionCommand, "$right_pane_percent", record.Session.RightPanePercent);
        AddParameter(sessionCommand, "$claude_observer_pane_id", record.Session.ClaudeObserverPaneId);
        AddParameter(sessionCommand, "$codex_observer_pane_id", record.Session.CodexObserverPaneId);
        AddParameter(sessionCommand, "$automation_observer_enabled", record.Session.AutomationObserverEnabled);
        AddParameter(sessionCommand, "$claude_permission_mode", record.Session.ClaudePermissionMode);
        AddParameter(sessionCommand, "$codex_mode", record.Session.CodexMode);
        AddParameter(sessionCommand, "$automation_enabled_at_launch", record.Session.AutomationEnabledAtLaunch);
        AddParameter(sessionCommand, "$session_created_at", ToDbString(record.Session.CreatedAt));
        AddParameter(sessionCommand, "$updated_at", ToDbString(record.UpdatedAt == default ? DateTimeOffset.Now : record.UpdatedAt));
        await sessionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var runtimeCommand = connection.CreateCommand();
        runtimeCommand.Transaction = transaction;
        runtimeCommand.CommandText =
            """
            INSERT INTO session_runtime_bindings (
                session_id, gui_pid, socket_path, left_pane_id, right_pane_id,
                claude_observer_pane_id, codex_observer_pane_id, is_alive, updated_at
            )
            VALUES (
                $session_id, $gui_pid, $socket_path, $left_pane_id, $right_pane_id,
                $claude_observer_pane_id, $codex_observer_pane_id, $is_alive, $updated_at
            )
            ON CONFLICT(session_id) DO UPDATE SET
                gui_pid = excluded.gui_pid,
                socket_path = excluded.socket_path,
                left_pane_id = excluded.left_pane_id,
                right_pane_id = excluded.right_pane_id,
                claude_observer_pane_id = excluded.claude_observer_pane_id,
                codex_observer_pane_id = excluded.codex_observer_pane_id,
                is_alive = excluded.is_alive,
                updated_at = excluded.updated_at;
            """;
        AddParameter(runtimeCommand, "$session_id", record.SessionId);
        AddParameter(runtimeCommand, "$gui_pid", record.RuntimeBinding.GuiPid == 0 ? record.Session.GuiPid : record.RuntimeBinding.GuiPid);
        AddParameter(runtimeCommand, "$socket_path", string.IsNullOrWhiteSpace(record.RuntimeBinding.SocketPath) ? record.Session.SocketPath : record.RuntimeBinding.SocketPath);
        AddParameter(runtimeCommand, "$left_pane_id", record.RuntimeBinding.LeftPaneId == 0 ? record.Session.LeftPaneId : record.RuntimeBinding.LeftPaneId);
        AddParameter(runtimeCommand, "$right_pane_id", record.RuntimeBinding.RightPaneId == 0 ? record.Session.RightPaneId : record.RuntimeBinding.RightPaneId);
        AddParameter(runtimeCommand, "$claude_observer_pane_id", record.RuntimeBinding.ClaudeObserverPaneId ?? record.Session.ClaudeObserverPaneId);
        AddParameter(runtimeCommand, "$codex_observer_pane_id", record.RuntimeBinding.CodexObserverPaneId ?? record.Session.CodexObserverPaneId);
        AddParameter(runtimeCommand, "$is_alive", record.RuntimeBinding.IsAlive);
        AddParameter(runtimeCommand, "$updated_at", ToDbString(record.RuntimeBinding.UpdatedAt == default ? DateTimeOffset.Now : record.RuntimeBinding.UpdatedAt));
        await runtimeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var snapshotCommand = connection.CreateCommand();
        snapshotCommand.Transaction = transaction;
        snapshotCommand.CommandText =
            """
            INSERT INTO session_status_snapshots (
                session_id, status, status_detail, last_activity_at, last_error,
                last_summary, claude_preview, codex_preview, needs_approval, automation_phase, automation_stage_id, automation_task_ref, task_md_path, task_md_status, automation_retry_count, updated_at
            )
            VALUES (
                $session_id, $status, $status_detail, $last_activity_at, $last_error,
                $last_summary, $claude_preview, $codex_preview, $needs_approval, $automation_phase, $automation_stage_id, $automation_task_ref, $task_md_path, $task_md_status, $automation_retry_count, $updated_at
            )
            ON CONFLICT(session_id) DO UPDATE SET
                status = excluded.status,
                status_detail = excluded.status_detail,
                last_activity_at = excluded.last_activity_at,
                last_error = excluded.last_error,
                last_summary = excluded.last_summary,
                claude_preview = excluded.claude_preview,
                codex_preview = excluded.codex_preview,
                needs_approval = excluded.needs_approval,
                automation_phase = excluded.automation_phase,
                automation_stage_id = excluded.automation_stage_id,
                automation_task_ref = excluded.automation_task_ref,
                task_md_path = excluded.task_md_path,
                task_md_status = excluded.task_md_status,
                automation_retry_count = excluded.automation_retry_count,
                updated_at = excluded.updated_at;
            """;
        AddParameter(snapshotCommand, "$session_id", record.SessionId);
        AddParameter(snapshotCommand, "$status", record.HealthStatus.ToString());
        AddParameter(snapshotCommand, "$status_detail", record.HealthDetail);
        AddParameter(snapshotCommand, "$last_activity_at", ToDbString(record.LastSeenAt == default ? DateTimeOffset.Now : record.LastSeenAt));
        AddParameter(snapshotCommand, "$last_error", record.LastError);
        AddParameter(snapshotCommand, "$last_summary", record.LastSummary);
        AddParameter(snapshotCommand, "$claude_preview", record.StatusSnapshot.ClaudePreview);
        AddParameter(snapshotCommand, "$codex_preview", record.StatusSnapshot.CodexPreview);
        AddParameter(snapshotCommand, "$needs_approval", record.StatusSnapshot.NeedsApproval);
        AddParameter(snapshotCommand, "$automation_phase", record.StatusSnapshot.AutomationPhase.ToString());
        AddParameter(snapshotCommand, "$automation_stage_id", record.StatusSnapshot.AutomationStageId);
        AddParameter(snapshotCommand, "$automation_task_ref", record.StatusSnapshot.AutomationTaskRef);
        AddParameter(snapshotCommand, "$task_md_path", record.StatusSnapshot.TaskMdPath);
        AddParameter(snapshotCommand, "$task_md_status", record.StatusSnapshot.TaskMdStatus.ToString());
        AddParameter(snapshotCommand, "$automation_retry_count", record.StatusSnapshot.AutomationRetryCount);
        AddParameter(snapshotCommand, "$updated_at", ToDbString(record.UpdatedAt == default ? DateTimeOffset.Now : record.UpdatedAt));
        await snapshotCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadAppStateAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT state_value FROM app_state WHERE state_key = $state_key;";
        AddParameter(command, "$state_key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    private async Task WriteAppStateAsync(
        SqliteConnection connection,
        string key,
        string? value,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO app_state (state_key, state_value)
            VALUES ($state_key, $state_value)
            ON CONFLICT(state_key) DO UPDATE SET state_value = excluded.state_value;
            """;
        AddParameter(command, "$state_key", key);
        AddParameter(command, "$state_value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ManagedSessionRecord MapRecord(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(0);
        var session = new LauncherSession
        {
            SessionId = sessionId,
            Workspace = reader.GetString(6),
            WorkingDirectory = reader.GetString(7),
            WezTermPath = reader.GetString(8),
            SocketPath = reader.GetString(9),
            GuiPid = reader.GetInt32(10),
            LeftPaneId = reader.GetInt32(11),
            RightPaneId = reader.GetInt32(12),
            RightPanePercent = reader.GetInt32(13),
            ClaudeObserverPaneId = ReadNullableInt(reader, 14),
            CodexObserverPaneId = ReadNullableInt(reader, 15),
            AutomationObserverEnabled = reader.GetBoolean(16),
            ClaudePermissionMode = reader.GetString(17),
            CodexMode = reader.GetString(18),
            AutomationEnabledAtLaunch = reader.GetBoolean(19),
            CreatedAt = ParseDbDateTime(reader.GetString(20)),
        };

        return new ManagedSessionRecord
        {
            Session = session,
            DisplayName = reader.GetString(1),
            GroupName = reader.GetString(2),
            IsPinned = reader.GetBoolean(3),
            IsArchived = reader.GetBoolean(4),
            LaunchProfileId = ReadNullableString(reader, 5),
            RuntimeBinding = new SessionRuntimeBinding
            {
                SessionId = sessionId,
                GuiPid = ReadNullableInt(reader, 22) ?? session.GuiPid,
                SocketPath = ReadNullableString(reader, 23) ?? session.SocketPath,
                LeftPaneId = ReadNullableInt(reader, 24) ?? session.LeftPaneId,
                RightPaneId = ReadNullableInt(reader, 25) ?? session.RightPaneId,
                ClaudeObserverPaneId = ReadNullableInt(reader, 26) ?? session.ClaudeObserverPaneId,
                CodexObserverPaneId = ReadNullableInt(reader, 27) ?? session.CodexObserverPaneId,
                IsAlive = ReadNullableBool(reader, 28) ?? true,
                UpdatedAt = ParseDbDateTime(ReadNullableString(reader, 29) ?? reader.GetString(21)),
            },
            StatusSnapshot = new SessionStatusSnapshot
            {
                SessionId = sessionId,
                Status = ParseHealthStatus(ReadNullableString(reader, 30)),
                StatusDetail = ReadNullableString(reader, 31) ?? "等待检测",
                LastActivityAt = ParseDbDateTime(ReadNullableString(reader, 32) ?? reader.GetString(21)),
                LastError = ReadNullableString(reader, 33),
                LastSummary = ReadNullableString(reader, 34) ?? "暂无",
                ClaudePreview = ReadNullableString(reader, 35) ?? "暂无输出",
                CodexPreview = ReadNullableString(reader, 36) ?? "暂无输出",
                NeedsApproval = ReadNullableBool(reader, 37) ?? false,
                AutomationPhase = ParseAutomationPhase(ReadNullableString(reader, 38)),
                AutomationStageId = ReadNullableInt(reader, 39),
                AutomationTaskRef = ReadNullableString(reader, 40),
                TaskMdPath = ReadNullableString(reader, 41),
                TaskMdStatus = ParseTaskMdStatus(ReadNullableString(reader, 42)),
                AutomationRetryCount = ReadNullableInt(reader, 43) ?? 0,
                UpdatedAt = ParseDbDateTime(ReadNullableString(reader, 44) ?? reader.GetString(21)),
            },
        };
    }

    private SqliteCommand CreateLaunchProfileUpsertCommand(
        SqliteConnection connection,
        LaunchProfile launchProfile,
        SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO launch_profiles (
                profile_id, name, description, working_directory, workspace_prefix,
                default_group_name, transfer_instruction_template, default_panel_preset,
                claude_permission_mode, codex_mode, right_pane_percent,
                automation_enabled, automation_observer_enabled, automation_advance_policy,
                automation_poll_interval_ms, automation_capture_lines, automation_timeout_seconds,
                automation_max_auto_stages, automation_max_retry_per_stage, automation_submit_on_send,
                worktree_strategy, created_at, updated_at
            )
            VALUES (
                $profile_id, $name, $description, $working_directory, $workspace_prefix,
                $default_group_name, $transfer_instruction_template, $default_panel_preset,
                $claude_permission_mode, $codex_mode, $right_pane_percent,
                $automation_enabled, $automation_observer_enabled, $automation_advance_policy,
                $automation_poll_interval_ms, $automation_capture_lines, $automation_timeout_seconds,
                $automation_max_auto_stages, $automation_max_retry_per_stage, $automation_submit_on_send,
                $worktree_strategy, $created_at, $updated_at
            )
            ON CONFLICT(profile_id) DO UPDATE SET
                name = excluded.name,
                description = excluded.description,
                working_directory = excluded.working_directory,
                workspace_prefix = excluded.workspace_prefix,
                default_group_name = excluded.default_group_name,
                transfer_instruction_template = excluded.transfer_instruction_template,
                default_panel_preset = excluded.default_panel_preset,
                claude_permission_mode = excluded.claude_permission_mode,
                codex_mode = excluded.codex_mode,
                right_pane_percent = excluded.right_pane_percent,
                automation_enabled = excluded.automation_enabled,
                automation_observer_enabled = excluded.automation_observer_enabled,
                automation_advance_policy = excluded.automation_advance_policy,
                automation_poll_interval_ms = excluded.automation_poll_interval_ms,
                automation_capture_lines = excluded.automation_capture_lines,
                automation_timeout_seconds = excluded.automation_timeout_seconds,
                automation_max_auto_stages = excluded.automation_max_auto_stages,
                automation_max_retry_per_stage = excluded.automation_max_retry_per_stage,
                automation_submit_on_send = excluded.automation_submit_on_send,
                worktree_strategy = excluded.worktree_strategy,
                updated_at = excluded.updated_at;
            """;
        AddParameter(command, "$profile_id", launchProfile.ProfileId);
        AddParameter(command, "$name", launchProfile.Name);
        AddParameter(command, "$description", launchProfile.Description);
        AddParameter(command, "$working_directory", launchProfile.WorkingDirectory);
        AddParameter(command, "$workspace_prefix", launchProfile.WorkspacePrefix);
        AddParameter(command, "$default_group_name", launchProfile.DefaultGroupName);
        AddParameter(command, "$transfer_instruction_template", launchProfile.TransferInstructionTemplate);
        AddParameter(command, "$default_panel_preset", launchProfile.DefaultPanelPreset);
        AddParameter(command, "$claude_permission_mode", launchProfile.ClaudePermissionMode);
        AddParameter(command, "$codex_mode", launchProfile.CodexMode);
        AddParameter(command, "$right_pane_percent", launchProfile.RightPanePercent);
        AddParameter(command, "$automation_enabled", launchProfile.AutomationEnabled);
        AddParameter(command, "$automation_observer_enabled", launchProfile.AutomationObserverEnabled);
        AddParameter(command, "$automation_advance_policy", launchProfile.AutomationAdvancePolicy);
        AddParameter(command, "$automation_poll_interval_ms", launchProfile.AutomationPollIntervalMilliseconds);
        AddParameter(command, "$automation_capture_lines", launchProfile.AutomationCaptureLines);
        AddParameter(command, "$automation_timeout_seconds", launchProfile.AutomationTimeoutSeconds);
        AddParameter(command, "$automation_max_auto_stages", launchProfile.AutomationMaxAutoStages);
        AddParameter(command, "$automation_max_retry_per_stage", launchProfile.AutomationMaxRetryPerStage);
        AddParameter(command, "$automation_submit_on_send", launchProfile.AutomationSubmitOnSend);
        AddParameter(command, "$worktree_strategy", launchProfile.WorktreeStrategy);
        AddParameter(command, "$created_at", ToDbString(launchProfile.CreatedAt));
        AddParameter(command, "$updated_at", ToDbString(launchProfile.UpdatedAt));
        return command;
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={DatabasePath};Pooling=False;");
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string? SerializeJson<T>(T? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, AutomationJsonOptions);
    }

    private static T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, AutomationJsonOptions);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static bool? ReadNullableBool(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static string ToDbString(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDbDateTime(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private async Task SaveLegacySessionAsync(LauncherSession session, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(LegacySessionFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(LegacySessionFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static SessionHealthStatus ParseHealthStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "running" => SessionHealthStatus.Running,
            "waiting" => SessionHealthStatus.Waiting,
            "error" => SessionHealthStatus.Error,
            "detached" => SessionHealthStatus.Detached,
            _ => SessionHealthStatus.Idle,
        };
    }

    private static AutomationPhase ParseAutomationPhase(string? value)
    {
        return Enum.TryParse<AutomationPhase>(value, ignoreCase: true, out var phase)
            ? phase
            : AutomationPhase.None;
    }

    private static TaskMdStatus ParseTaskMdStatus(string? value)
    {
        return Enum.TryParse<TaskMdStatus>(value, ignoreCase: true, out var status)
            ? status
            : TaskMdStatus.Unknown;
    }

    private static ManagedSessionRecord CreateRecord(LauncherSession session)
    {
        return new ManagedSessionRecord
        {
            Session = session.Clone(),
            DisplayName = BuildDisplayName(session),
            GroupName = "默认",
            RuntimeBinding = CreateRuntimeBinding(session),
            StatusSnapshot = new SessionStatusSnapshot
            {
                SessionId = session.SessionId,
                Status = SessionHealthStatus.Idle,
                StatusDetail = "从旧会话导入",
                LastSummary = "最近会话",
                LastActivityAt = session.CreatedAt == default ? DateTimeOffset.Now : session.CreatedAt,
                UpdatedAt = DateTimeOffset.Now,
            },
        };
    }

    private static SessionRuntimeBinding CreateRuntimeBinding(LauncherSession session)
    {
        return new SessionRuntimeBinding
        {
            SessionId = session.SessionId,
            GuiPid = session.GuiPid,
            SocketPath = session.SocketPath,
            LeftPaneId = session.LeftPaneId,
            RightPaneId = session.RightPaneId,
            ClaudeObserverPaneId = session.ClaudeObserverPaneId,
            CodexObserverPaneId = session.CodexObserverPaneId,
            IsAlive = true,
            UpdatedAt = DateTimeOffset.Now,
        };
    }

    private static string BuildDisplayName(LauncherSession session)
    {
        return !string.IsNullOrWhiteSpace(session.Workspace)
            ? session.Workspace
            : Path.GetFileName(session.WorkingDirectory);
    }

    private static LaunchProfile MapLaunchProfile(SqliteDataReader reader)
    {
        var worktreeStrategy = reader.GetString(20);
        var name = reader.GetString(1);
        return new LaunchProfile
        {
            ProfileId = reader.GetString(0),
            Name = name,
            Description = reader.GetString(2),
            WorkingDirectory = ReadNullableString(reader, 3),
            WorkspacePrefix = ReadNullableString(reader, 4),
            DefaultGroupName = reader.GetString(5),
            TransferInstructionTemplate = reader.GetString(6),
            DefaultPanelPreset = reader.GetString(7),
            ClaudePermissionMode = reader.GetString(8),
            CodexMode = reader.GetString(9),
            RightPanePercent = reader.GetInt32(10),
            AutomationEnabled = reader.GetBoolean(11),
            AutomationObserverEnabled = reader.GetBoolean(12),
            AutomationAdvancePolicy = reader.GetString(13),
            AutomationPollIntervalMilliseconds = reader.GetInt32(14),
            AutomationCaptureLines = reader.GetInt32(15),
            AutomationTimeoutSeconds = reader.GetInt32(16),
            AutomationMaxAutoStages = reader.GetInt32(17),
            AutomationMaxRetryPerStage = reader.GetInt32(18),
            AutomationSubmitOnSend = reader.GetBoolean(19),
            IsBuiltIn = IsBuiltInProfile(name),
            DefaultUseWorktree = !string.Equals(worktreeStrategy, "none", StringComparison.OrdinalIgnoreCase),
            WorktreeStrategy = worktreeStrategy,
            DefaultWorktreeStrategy = worktreeStrategy,
            CreatedAt = ParseDbDateTime(reader.GetString(21)),
            UpdatedAt = ParseDbDateTime(reader.GetString(22)),
        };
    }

    private static IReadOnlyList<LaunchProfile> GetDefaultProfiles()
    {
        return
        [
            new LaunchProfile
            {
                Name = "标准协作",
                Description = "保留人工发送与手动推进，适合日常双栏协作。",
                IsBuiltIn = true,
                DefaultPanelPreset = "balanced",
            },
            new LaunchProfile
            {
                Name = "安全审批",
                Description = "Claude 计划模式 + 审批优先。",
                ClaudePermissionMode = "plan",
                AutomationEnabled = true,
                AutomationAdvancePolicy = "manual-each-stage",
                IsBuiltIn = true,
                DefaultPanelPreset = "automation",
            },
            new LaunchProfile
            {
                Name = "自动闭环",
                Description = "自动编排优先，适合清晰的实现任务。",
                ClaudePermissionMode = "plan",
                CodexMode = "full-auto",
                AutomationEnabled = true,
                AutomationAdvancePolicy = "full-auto-loop",
                IsBuiltIn = true,
                DefaultPanelPreset = "automation",
            },
        ];
    }

    private static bool IsBuiltInProfile(string name)
    {
        return string.Equals(name, "标准协作", StringComparison.Ordinal)
            || string.Equals(name, "安全审批", StringComparison.Ordinal)
            || string.Equals(name, "自动闭环", StringComparison.Ordinal);
    }
}
