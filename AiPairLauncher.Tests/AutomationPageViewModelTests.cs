using System.IO;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.ViewModels;
using AiPairLauncher.App.ViewModels.Pages;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AutomationPageViewModelTests
{
    [Fact(DisplayName = "test_automation_page_viewmodel_reads_task_snapshot_details")]
    public void AutomationPageViewModelReadsTaskSnapshotDetails()
    {
        var viewModel = new MainWindowViewModel();
        var workingDirectory = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var taskMdPath = Path.Combine(workingDirectory, "task.md");
        File.WriteAllText(
            taskMdPath,
            """
            # Task: 示例

            > 状态: IN_PROGRESS

            ## 任务清单

            ### 阶段 1: 执行

            - [ ] **T1.1**: 实现任务
              - 角色: coder
              - 依赖: T0.1
              - 风险: 中 — 注意状态同步
              - **执行结果**: 部分完成

            ## 复核报告

            ### Reviewer

            - 需要补一条回归测试
            """);

        viewModel.ApplyAutomationState(new AutomationRunState
        {
            Phase = AutomationPhase.Phase3Execution,
            Status = AutomationStageStatus.WaitingForCodexReport,
            StatusDetail = "等待执行",
            TaskMdPath = taskMdPath,
            TaskMdStatus = TaskMdStatus.InProgress,
            CurrentTaskRef = "T1.1",
            CurrentTaskStageHeading = "阶段 1: 执行",
            TaskCount = 1,
        });

        var pageViewModel = new AutomationPageViewModel(viewModel, new SharedSessionState(viewModel));

        Assert.Equal("coder", pageViewModel.CurrentTaskRole);
        Assert.Equal("T0.1", pageViewModel.CurrentTaskDependencies);
        Assert.Equal("中 - 注意状态同步", pageViewModel.CurrentTaskRisk);
        Assert.Equal("部分完成", pageViewModel.CurrentTaskExecutionSummary);
        Assert.Equal("- 需要补一条回归测试", pageViewModel.ReviewSummary);
    }
}
