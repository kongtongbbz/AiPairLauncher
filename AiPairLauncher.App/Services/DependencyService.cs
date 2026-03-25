using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class DependencyService : IDependencyService
{
    private readonly CommandLocator _commandLocator;
    private readonly IProcessRunner _processRunner;

    public DependencyService(CommandLocator commandLocator, IProcessRunner processRunner)
    {
        _commandLocator = commandLocator;
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<DependencyStatus>> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var checks = new[]
        {
            CheckOneAsync(
                "wezterm",
                new[]
                {
                    @"C:\Program Files\WezTerm\wezterm.exe",
                    @"%LOCALAPPDATA%\Microsoft\WinGet\Links\wezterm.exe",
                },
                new[] { "--version" },
                cancellationToken),
            CheckOneAsync(
                "claude",
                new[]
                {
                    @"%USERPROFILE%\.local\bin\claude.exe",
                },
                new[] { "--version" },
                cancellationToken),
            CheckOneAsync(
                "codex",
                new[]
                {
                    @"%APPDATA%\npm\codex.cmd",
                    @"%APPDATA%\npm\codex.ps1",
                },
                new[] { "--version" },
                cancellationToken),
        };

        return await Task.WhenAll(checks).ConfigureAwait(false);
    }

    private async Task<DependencyStatus> CheckOneAsync(
        string name,
        IReadOnlyList<string> preferredPaths,
        IReadOnlyList<string> versionArgs,
        CancellationToken cancellationToken)
    {
        var resolvedPath = _commandLocator.Resolve(name, preferredPaths);
        if (resolvedPath is null)
        {
            return new DependencyStatus
            {
                Name = name,
                IsAvailable = false,
                Version = "missing",
                Message = "未找到可执行文件。",
            };
        }

        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessCommand
                {
                    FileName = resolvedPath,
                    Arguments = versionArgs,
                    Timeout = TimeSpan.FromSeconds(8),
                },
                cancellationToken).ConfigureAwait(false);

            var version = ReadFirstLine(result.StandardOutput) ?? ReadFirstLine(result.StandardError);
            return new DependencyStatus
            {
                Name = name,
                IsAvailable = true,
                ResolvedPath = resolvedPath,
                Version = string.IsNullOrWhiteSpace(version) ? "unknown" : version,
                Message = result.IsSuccess
                    ? "命令可执行，版本探测成功。"
                    : "命令可执行，但版本探测返回非零码。",
            };
        }
        catch (Exception ex)
        {
            return new DependencyStatus
            {
                Name = name,
                IsAvailable = true,
                ResolvedPath = resolvedPath,
                Version = "unknown",
                Message = $"版本探测失败: {ex.Message}",
            };
        }
    }

    private static string? ReadFirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
