using System.Diagnostics;
using System.Text;

namespace AiPairLauncher.App.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);

        using var process = new Process();
        process.StartInfo = BuildStartInfo(command);

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程: {command.FileName}");
        }

        if (!command.WaitForExit)
        {
            return new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
            };
        }

        var timeoutCts = new CancellationTokenSource(command.Timeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            if (!string.IsNullOrEmpty(command.StandardInput))
            {
                await process.StandardInput.WriteAsync(command.StandardInput).ConfigureAwait(false);
            }

            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = (await stdoutTask.ConfigureAwait(false)).TrimEnd(),
                StandardError = (await stderrTask.ConfigureAwait(false)).TrimEnd(),
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw new TimeoutException($"进程执行超时: {command.FileName}");
        }
    }

    private static ProcessStartInfo BuildStartInfo(ProcessCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            RedirectStandardError = command.WaitForExit,
            RedirectStandardOutput = command.WaitForExit,
            RedirectStandardInput = command.WaitForExit,
            CreateNoWindow = true,
        };

        if (command.WaitForExit)
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var arg in command.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (command.EnvironmentVariables is null)
        {
            return startInfo;
        }

        foreach (var entry in command.EnvironmentVariables)
        {
            if (entry.Value is null)
            {
                startInfo.Environment.Remove(entry.Key);
            }
            else
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        return startInfo;
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // 这里不再抛出，保留超时异常作为主错误
        }
    }
}
