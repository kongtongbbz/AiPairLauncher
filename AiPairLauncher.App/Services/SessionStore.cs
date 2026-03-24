using System.IO;
using System.Text.Json;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class SessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string SessionFilePath { get; }

    public SessionStore()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SessionFilePath = Path.Combine(appDataPath, "AiPairLauncher", "session.json");
    }

    public async Task SaveAsync(LauncherSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureParentDirectory();

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(SessionFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LauncherSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(SessionFilePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LauncherSession>(json, JsonOptions);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(SessionFilePath))
        {
            File.Delete(SessionFilePath);
        }

        return Task.CompletedTask;
    }

    private void EnsureParentDirectory()
    {
        var parent = Path.GetDirectoryName(SessionFilePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException("无法确定 session.json 的父目录。");
        }

        Directory.CreateDirectory(parent);
    }
}
