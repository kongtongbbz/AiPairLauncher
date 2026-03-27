using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class TaskMdParserTests
{
    private readonly TaskMdParser _parser = new();

    [Fact(DisplayName = "test_parse_valid_taskmd_document")]
    public void ParseValidTaskMdDocument()
    {
        var result = _parser.ParseContent(
            """
            # Task: 自动编排 2.0

            > 生成时间: 2026-03-28T10:30:00+08:00
            > 工作目录: D:\repo
            > 状态: PLANNED

            ## 任务清单

            ### 阶段 1: 项目调研

            - [x] **T1.1**: 生成 task.md
            - [ ] **T1.2**: 六角色协同规划

            ### 阶段 2: 执行与复核

            - [ ] **T2.1**: 执行阶段任务  ⚠️ 执行中
            - [ ] **T2.2**: Phase 4 复核
            """,
            @"D:\repo\task.md");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Document);
        Assert.Equal(TaskMdStatus.Planned, result.Document!.Status);
        Assert.Equal(2, result.Document.Stages.Count);
        Assert.Equal(4, result.Document.TaskCount);
        Assert.Equal(1, result.Document.CompletedTaskCount);
        Assert.Equal("阶段 1: 项目调研", result.Document.Stages[0].Heading);
        Assert.Equal("T1.2", result.Snapshot.CurrentTaskRef);
        Assert.Equal("阶段 1: 项目调研", result.Snapshot.CurrentStageHeading);
        Assert.True(result.Document.FindTask("T2.1")!.IsWarning);
    }

    [Fact(DisplayName = "test_parse_taskmd_reports_missing_status")]
    public void ParseTaskMdReportsMissingStatus()
    {
        var result = _parser.ParseContent(
            """
            # Task: 缺少状态

            ### 阶段 1: 项目调研

            - [ ] **T1.1**: 生成 task.md
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.Contains("缺少文件头状态行", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "test_parse_taskmd_reports_invalid_status")]
    public void ParseTaskMdReportsInvalidStatus()
    {
        var result = _parser.ParseContent(
            """
            # Task: 非法状态

            > 状态: INVALID

            ### 阶段 1: 项目调研

            - [ ] **T1.1**: 生成 task.md
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.Contains("状态无效", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "test_parse_taskmd_file_not_found")]
    public void ParseTaskMdFileNotFound()
    {
        var result = _parser.ParseFile(@"D:\repo\missing-task.md");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.Contains("未找到 task.md", StringComparison.Ordinal));
    }
}
