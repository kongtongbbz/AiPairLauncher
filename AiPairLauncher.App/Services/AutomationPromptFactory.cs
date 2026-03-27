using System.IO;
using System.Text;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

internal static class AutomationPromptFactory
{
    public static string BuildClaudeBootstrapPrompt(string workingDirectory, string taskPrompt)
    {
        return BuildPhase1ResearchPrompt(workingDirectory, taskPrompt);
    }

    public static string BuildPhase1ResearchPrompt(string workingDirectory, string taskPrompt)
    {
        return BuildPhasePlanPrompt(
            phase: AutomationPhase.Phase1Research,
            stageId: 1,
            workingDirectory: workingDirectory,
            taskPrompt: taskPrompt,
            phaseTitle: "Phase 1: 项目调研",
            taskMdPath: Path.Combine(workingDirectory, "task.md"),
            taskMdStatus: "pending_plan",
            instructions:
            [
                "你的任务是充分调研当前项目，并在工作目录根路径生成 task.md。",
                "本阶段只允许调研、读取、分析和生成 task.md，不要修改项目功能代码。",
                "需要显式体现六角色视角：[planner]、[researcher]、[coder]、[reviewer]、[tester]、[debugger]。",
            ]);
    }

    public static string BuildPhase2PlanningPrompt(string workingDirectory, string taskPrompt, string taskMdPath)
    {
        return BuildPhasePlanPrompt(
            phase: AutomationPhase.Phase2Planning,
            stageId: 1,
            workingDirectory: workingDirectory,
            taskPrompt: taskPrompt,
            phaseTitle: "Phase 2: 计划编排",
            taskMdPath: taskMdPath,
            taskMdStatus: "planned",
            instructions:
            [
                "你的任务是读取 task.md，并产出可审批的结构化执行规划。",
                "需要显式给出六角色分工：[planner]、[researcher]、[coder]、[reviewer]、[tester]、[debugger]。",
                "需要补充依赖关系、测试方法和风险评估。",
            ]);
    }

    public static string BuildPhase3KickoffPrompt(string workingDirectory, string taskPrompt, string taskMdPath)
    {
        return BuildPhasePlanPrompt(
            phase: AutomationPhase.Phase3Execution,
            stageId: 1,
            workingDirectory: workingDirectory,
            taskPrompt: taskPrompt,
            phaseTitle: "Phase 3: 执行启动",
            taskMdPath: taskMdPath,
            taskMdStatus: "in_progress",
            instructions:
            [
                "你的任务是读取 task.md，并输出首个可执行 stage_plan。",
                "尽量提供 task_ref、task_progress 和最小变更范围。",
                "如需说明角色，请沿用 [coder]、[tester]、[debugger] 等标记。",
            ]);
    }

    public static string BuildPhase4ReviewPrompt(string taskPrompt, string taskMdPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你现在进入 AiPair 自动协作模式的 Phase 4: 复核验收。");
        builder.AppendLine("你的角色是领导者 Claude，只负责最终复核并输出 review_decision。");
        builder.AppendLine($"task.md 路径: {taskMdPath}");
        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        builder.AppendLine();
        builder.AppendLine("本阶段必须从 reviewer、tester、debugger 三个视角完成复核。");
        builder.AppendLine("输出要求：");
        builder.AppendLine("1. 回复必须且只能是 [AIPAIR_PACKET] 包。");
        builder.AppendLine("2. role 固定 claude，kind 固定 review_decision，phase 固定 phase4_review。");
        builder.AppendLine("3. 如果复核通过，decision 必须为 complete，且 task_md_status 应为 done。");
        builder.AppendLine("4. 如果复核失败，decision 只能是 retry_stage 或 blocked。");
        builder.AppendLine("5. 当 decision=retry_stage 时，必须提供 task_ref、task_progress、codex_brief。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildPhaseRevisionPrompt(ApprovalDraft draft, string? userNote, string taskPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("当前阶段计划被用户退回，请重新输出结构化数据包。");
        builder.AppendLine($"当前 phase: {ToPhaseValue(draft.Phase)}");
        builder.AppendLine($"当前 stage_id: {draft.StageId}");
        if (!string.IsNullOrWhiteSpace(draft.TaskMdPath))
        {
            builder.AppendLine($"task.md 路径: {draft.TaskMdPath}");
        }

        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        if (!string.IsNullOrWhiteSpace(userNote))
        {
            builder.AppendLine($"用户反馈: {userNote.Trim()}");
        }

        builder.AppendLine("输出要求：phase 必须保持不变，stage_id 必须保持不变，如有 task_ref 请继续保留。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildCodexExecutionPrompt(ApprovalDraft draft, string? userNote)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你现在是执行者 Codex。请执行下面的阶段任务，必要时修复代码、调试错误并自行验证。");
        builder.AppendLine("完成后只输出一个 execution_report 结构化数据包，禁止在包外补充解释。");
        builder.AppendLine($"当前 Phase: {ToPhaseValue(draft.Phase)}");
        builder.AppendLine($"当前阶段: {draft.StageId}");
        if (!string.IsNullOrWhiteSpace(draft.TaskRef))
        {
            builder.AppendLine($"当前任务: {draft.TaskRef}");
        }
        if (!string.IsNullOrWhiteSpace(userNote))
        {
            builder.AppendLine($"用户审批备注: {userNote.Trim()}");
        }

        builder.AppendLine();
        builder.AppendLine("执行指令如下：");
        builder.AppendLine(draft.CodexBrief.Trim());
        builder.AppendLine();
        builder.AppendLine("回复要求：");
        builder.AppendLine("1. 回复以字面量 [AIPAIR_PACKET] 开头，并以字面量 [/AIPAIR_PACKET] 结束。");
        builder.AppendLine("2. role 固定 codex，kind 固定 execution_report，phase 固定 phase3_execution，stage_id 固定当前阶段号。");
        builder.AppendLine("3. 必须包含字段：role、kind、phase、stage_id、status、summary、completed、verification、blockers、review_focus、body。");
        builder.AppendLine("4. 如可确定当前任务编号，请补充 task_ref；如可总结任务推进，请补充 task_progress。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildClaudeReviewPrompt(AgentPacket executionReport, string taskPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请作为领导者 Claude 审定下面这份 Codex execution_report。");
        builder.AppendLine("你的回复必须且只能是一个 review_decision 结构化数据包。");
        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Codex 执行回报如下：");
        builder.AppendLine("<execution_report>");
        builder.AppendLine(executionReport.RawText);
        builder.AppendLine("</execution_report>");
        builder.AppendLine();
        builder.AppendLine("回复要求：");
        builder.AppendLine("1. 回复以字面量 [AIPAIR_PACKET] 开头，并以字面量 [/AIPAIR_PACKET] 结束。");
        builder.AppendLine("2. role 固定 claude，kind 固定 review_decision，phase 固定 phase3_execution。");
        builder.AppendLine("3. decision 只能是 next_stage、retry_stage、complete、blocked。");
        builder.AppendLine("4. 当 decision=next_stage 或 retry_stage 时，额外必须包含 title、summary、steps、acceptance、codex_brief。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildClaudeRevisionPrompt(ApprovalDraft draft, string? userNote, string taskPrompt)
    {
        return draft.Phase == AutomationPhase.None
            ? BuildLegacyRevisionPrompt(draft, userNote, taskPrompt)
            : BuildPhaseRevisionPrompt(draft, userNote, taskPrompt);
    }

    private static string BuildPhasePlanPrompt(
        AutomationPhase phase,
        int stageId,
        string workingDirectory,
        string taskPrompt,
        string phaseTitle,
        string taskMdPath,
        string taskMdStatus,
        IReadOnlyList<string> instructions)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"你现在进入 AiPair 自动协作模式的 {phaseTitle}。");
        builder.AppendLine("你的角色是领导者 Claude，需要输出一个可供 GUI 审批的结构化 stage_plan。");
        builder.AppendLine($"当前工作目录: {workingDirectory}");
        builder.AppendLine($"task.md 路径: {taskMdPath}");
        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        builder.AppendLine();
        foreach (var instruction in instructions)
        {
            builder.AppendLine($"- {instruction}");
        }

        builder.AppendLine();
        builder.AppendLine("结构化回复要求：");
        builder.AppendLine("1. 回复以字面量 [AIPAIR_PACKET] 开头，并以字面量 [/AIPAIR_PACKET] 结束。");
        builder.AppendLine("2. 必须包含字段：role、kind、phase、subagent、stage_id、task_md_path、task_md_status、title、summary、scope、steps、acceptance、codex_brief。");
        builder.AppendLine($"3. role 固定 claude，kind 固定 stage_plan，phase 固定 {ToPhaseValue(phase)}，subagent 固定 planner。");
        builder.AppendLine($"4. task_md_path 必须写 {taskMdPath}，task_md_status 必须写 {taskMdStatus}。");
        builder.AppendLine("5. 需要显式体现 planner、researcher、coder、reviewer、tester、debugger 六个角色。");
        builder.AppendLine("6. stage_id 必须是从 1 开始的正整数，禁止使用 0。");
        builder.AppendLine();
        builder.AppendLine("""
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: PHASE_VALUE
subagent: planner
stage_id: STAGE_VALUE
task_md_path: TASK_MD_PATH
task_md_status: TASK_MD_STATUS
title: 示例标题
summary: <<<SUMMARY
这里写阶段摘要
SUMMARY
scope: <<<SCOPE
这里写执行范围
SCOPE
steps: <<<STEPS
1. 第一步
2. 第二步
STEPS
acceptance: <<<ACCEPTANCE
1. 验收项一
2. 验收项二
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
这里写发给 Codex 的执行摘要
CODEX_BRIEF
[/AIPAIR_PACKET]
""".Replace("PHASE_VALUE", ToPhaseValue(phase), StringComparison.Ordinal)
            .Replace("STAGE_VALUE", stageId.ToString(), StringComparison.Ordinal)
            .Replace("TASK_MD_PATH", taskMdPath, StringComparison.Ordinal)
            .Replace("TASK_MD_STATUS", taskMdStatus, StringComparison.Ordinal));
        return builder.ToString().TrimEnd();
    }

    private static string ToPhaseValue(AutomationPhase phase)
    {
        return phase switch
        {
            AutomationPhase.Phase1Research => "phase1_research",
            AutomationPhase.Phase2Planning => "phase2_planning",
            AutomationPhase.Phase3Execution => "phase3_execution",
            AutomationPhase.Phase4Review => "phase4_review",
            _ => "phase3_execution",
        };
    }

    private static string BuildLegacyRevisionPrompt(ApprovalDraft draft, string? userNote, string taskPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("当前阶段计划被用户退回，请你重拟并重新输出 stage_plan 结构化数据包。");
        builder.AppendLine($"退回阶段: {draft.StageId}");
        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        if (!string.IsNullOrWhiteSpace(userNote))
        {
            builder.AppendLine($"用户反馈: {userNote.Trim()}");
        }

        builder.AppendLine();
        builder.AppendLine("请保留严格的结构化格式，不要在包外补充说明。");
        builder.AppendLine($"stage_id 必须固定为当前退回阶段 {draft.StageId}，且必须是从 1 开始的正整数，禁止使用 0。");
        builder.AppendLine($"stage_id: {draft.StageId}");
        return builder.ToString().TrimEnd();
    }
}
