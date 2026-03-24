namespace AiPairLauncher.App.Infrastructure;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default);
}

