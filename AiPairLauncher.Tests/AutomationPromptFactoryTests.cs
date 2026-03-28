using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AutomationPromptFactoryTests
{
    [Fact(DisplayName = "test_build_phase1_prompt_contains_phase_and_taskmd_constraints")]
    public void BuildPhase1PromptContainsPhaseAndTaskMdConstraints()
    {
        var prompt = AutomationPromptFactory.BuildPhase1ResearchPrompt(@"D:\repo", "实现自动编排");

        Assert.Contains("Phase 1: 项目调研", prompt, StringComparison.Ordinal);
        Assert.Contains("phase1_research", prompt, StringComparison.Ordinal);
        Assert.Contains(@".aipair\task.md", prompt, StringComparison.Ordinal);
        Assert.Contains("[planner]", prompt, StringComparison.Ordinal);
        Assert.Contains("[researcher]", prompt, StringComparison.Ordinal);
        Assert.Contains("## 任务清单", prompt, StringComparison.Ordinal);
        Assert.Contains("> 状态:", prompt, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "test_build_phase2_prompt_contains_all_six_roles")]
    public void BuildPhase2PromptContainsAllSixRoles()
    {
        var prompt = AutomationPromptFactory.BuildPhase2PlanningPrompt(@"D:\repo", "实现自动编排", @"D:\repo\task.md");

        Assert.Contains("[planner]", prompt, StringComparison.Ordinal);
        Assert.Contains("[researcher]", prompt, StringComparison.Ordinal);
        Assert.Contains("[coder]", prompt, StringComparison.Ordinal);
        Assert.Contains("[reviewer]", prompt, StringComparison.Ordinal);
        Assert.Contains("[tester]", prompt, StringComparison.Ordinal);
        Assert.Contains("[debugger]", prompt, StringComparison.Ordinal);
        Assert.Contains("phase2_planning", prompt, StringComparison.Ordinal);
        Assert.Contains("- [ ] **T1.1**:", prompt, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "test_build_phase_revision_prompt_uses_phase_context")]
    public void BuildPhaseRevisionPromptUsesPhaseContext()
    {
        var prompt = AutomationPromptFactory.BuildPhaseRevisionPrompt(
            new ApprovalDraft
            {
                Phase = AutomationPhase.Phase2Planning,
                StageId = 1,
            },
            "请缩小范围",
            "实现自动编排");

        Assert.Contains("phase2_planning", prompt, StringComparison.Ordinal);
        Assert.Contains("请缩小范围", prompt, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "test_build_phase4_review_prompt_requires_retry_to_phase3_execution")]
    public void BuildPhase4ReviewPromptRequiresRetryToPhase3Execution()
    {
        var prompt = AutomationPromptFactory.BuildPhase4ReviewPrompt("实现自动编排", @"D:\repo\task.md");

        Assert.Contains("phase4_review", prompt, StringComparison.Ordinal);
        Assert.Contains("retry_stage", prompt, StringComparison.Ordinal);
        Assert.Contains("phase3_execution", prompt, StringComparison.Ordinal);
    }
}
