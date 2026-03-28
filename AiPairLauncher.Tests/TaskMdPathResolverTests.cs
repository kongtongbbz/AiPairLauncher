using System.IO;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class TaskMdPathResolverTests
{
    [Fact(DisplayName = "test_build_default_uses_aipair_directory")]
    public void BuildDefaultUsesAiPairDirectory()
    {
        var path = TaskMdPathResolver.BuildDefault(@"D:\repo");

        Assert.Equal(Path.Combine(@"D:\repo", ".aipair", "task.md"), path);
    }

    [Fact(DisplayName = "test_resolve_existing_or_default_prefers_default_path")]
    public void ResolveExistingOrDefaultPrefersDefaultPath()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workingDirectory, ".aipair"));
        File.WriteAllText(Path.Combine(workingDirectory, ".aipair", "task.md"), "default");
        File.WriteAllText(Path.Combine(workingDirectory, "task.md"), "legacy");

        var resolved = TaskMdPathResolver.ResolveExistingOrDefault(workingDirectory);

        Assert.Equal(Path.Combine(workingDirectory, ".aipair", "task.md"), resolved);
    }

    [Fact(DisplayName = "test_resolve_existing_or_default_falls_back_to_legacy_path")]
    public void ResolveExistingOrDefaultFallsBackToLegacyPath()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "task.md"), "legacy");

        var resolved = TaskMdPathResolver.ResolveExistingOrDefault(workingDirectory);

        Assert.Equal(Path.Combine(workingDirectory, "task.md"), resolved);
    }
}
