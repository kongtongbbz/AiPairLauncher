using System.Text;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

internal static class AutomationPromptFactory
{
    public static string BuildClaudeBootstrapPrompt(string workingDirectory, string taskPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你现在进入 AiPair 自动协作模式。");
        builder.AppendLine("你的角色是领导者 Claude，只负责制定阶段计划和审定 Codex 的执行结果。");
        builder.AppendLine("每次回复必须且只能输出一个结构化数据包，禁止在包外补充解释。");
        builder.AppendLine($"当前工作目录: {workingDirectory}");
        builder.AppendLine("当前用户目标：");
        builder.AppendLine(taskPrompt.Trim());
        builder.AppendLine();
        builder.AppendLine("首次回复请输出 stage_plan。");
        builder.AppendLine("结构化回复要求：");
        builder.AppendLine("1. 回复以字面量 [AIPAIR_PACKET] 开头，并以字面量 [/AIPAIR_PACKET] 结束。");
        builder.AppendLine("2. 必须包含这些字段：role、kind、stage_id、title、summary、scope、steps、acceptance、codex_brief。");
        builder.AppendLine("3. role 固定 claude，kind 固定 stage_plan。");
        builder.AppendLine("4. 多行字段写法固定为：字段名 + 冒号 + <<<结束标记，然后换行写内容，最后单独一行写结束标记。");
        builder.AppendLine("5. summary 的结束标记用 SUMMARY，scope 用 SCOPE，steps 用 STEPS，acceptance 用 ACCEPTANCE，codex_brief 用 CODEX_BRIEF。");
        builder.AppendLine("6. steps 和 acceptance 用编号列表。");
        builder.AppendLine("7. stage_id 必须是从 1 开始的正整数；首次 stage_plan 的 stage_id 固定为 1，禁止使用 0。");
        builder.AppendLine("8. 严格复制下面这个模板的语法，只替换内容，不要改字段名、冒号、结束标记或闭合标签：");
        builder.AppendLine("""
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
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
""");
        builder.AppendLine();
        builder.AppendLine("后续当你收到 Codex 的 execution_report 时，只能输出 review_decision。");
        builder.AppendLine("如果需要进入下一阶段或重试当前阶段，也必须包含新的 title、summary、steps、acceptance、codex_brief。");
        builder.AppendLine("review_decision 的 decision 只能是 next_stage、retry_stage、complete、blocked。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildCodexExecutionPrompt(ApprovalDraft draft, string? userNote)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你现在是执行者 Codex。请执行下面的阶段任务，必要时修复代码、调试错误并自行验证。");
        builder.AppendLine("完成后只输出一个 execution_report 结构化数据包，禁止在包外补充解释。");
        builder.AppendLine($"当前阶段: {draft.StageId}");
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
        builder.AppendLine("2. role 固定 codex，kind 固定 execution_report，stage_id 固定当前阶段号。");
        builder.AppendLine("3. 必须包含字段：role、kind、stage_id、status、summary、completed、verification、blockers、review_focus、body。");
        builder.AppendLine("4. status 在成功时写 success；如果失败或阻塞，请明确写 failure 或 blocked。");
        builder.AppendLine("5. 多行字段写法固定：summary 用 SUMMARY，completed 用 COMPLETED，verification 用 VERIFICATION，blockers 用 BLOCKERS，review_focus 用 REVIEW_FOCUS，body 用 BODY。");
        builder.AppendLine("6. completed、verification、blockers 用列表；如果无 blockers，写“无”。");
        builder.AppendLine("7. 先完成核心交付，再决定状态。只要核心目标已经完成，且你能通过编辑结果、读文件结果、补丁结果或其他直接证据确认验收标准已满足，就必须写 success。");
        builder.AppendLine("8. 不要因为补充性的 shell 自检、PowerShell 启动失败、环境 warning、快照 warning 或与核心交付无关的额外验证失败，就把已完成的阶段写成 blocked 或 failure。");
        builder.AppendLine("9. 只有在核心交付尚未完成，或者关键验收标准确实无法确认时，才允许写 blocked 或 failure。");
        builder.AppendLine("10. 如果使用了回退方案完成核心目标，必须在 verification 或 body 中写明，但状态仍按核心验收是否满足来决定。");
        builder.AppendLine("11. 严格复制下面这个模板的语法，只替换内容，不要改字段名、冒号、结束标记或闭合标签：");
        builder.AppendLine("""
[AIPAIR_PACKET]
role: codex
kind: execution_report
stage_id: 1
status: success
summary: <<<SUMMARY
这里写执行摘要
SUMMARY
completed: <<<COMPLETED
1. 已完成项一
2. 已完成项二
COMPLETED
verification: <<<VERIFICATION
1. 验证项一
2. 验证项二
VERIFICATION
blockers: <<<BLOCKERS
无
BLOCKERS
review_focus: <<<REVIEW_FOCUS
这里写需要 Claude 重点复核的内容
REVIEW_FOCUS
body: <<<BODY
这里写详细说明
BODY
[/AIPAIR_PACKET]
""");
        builder.AppendLine("12. 包外不要输出任何解释。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildClaudeReviewPrompt(AgentPacket executionReport, string taskPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请作为领导者 Claude 审定下面这份 Codex execution_report。");
        builder.AppendLine("你的回复必须且只能是一个 review_decision 结构化数据包。");
        builder.AppendLine("如果 decision=next_stage 或 retry_stage，必须包含新的 title、summary、steps、acceptance、codex_brief。");
        builder.AppendLine("如果 decision=complete 或 blocked，也必须在 body 中明确原因。");
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
        builder.AppendLine("2. role 固定 claude，kind 固定 review_decision。");
        builder.AppendLine("3. decision 只能是 next_stage、retry_stage、complete、blocked。");
        builder.AppendLine("4. 必须包含字段：role、kind、stage_id、decision、body。");
        builder.AppendLine("5. 当 decision=next_stage 或 retry_stage 时，额外必须包含 title、summary、steps、acceptance、codex_brief。");
        builder.AppendLine("6. 多行字段写法固定：summary 用 SUMMARY，steps 用 STEPS，acceptance 用 ACCEPTANCE，codex_brief 用 CODEX_BRIEF，body 用 BODY。");
        builder.AppendLine("7. stage_id 必须是从 1 开始的正整数，禁止使用 0。decision=next_stage 时，stage_id 必须等于当前阶段号 + 1；decision=retry_stage、complete、blocked 时，stage_id 必须等于当前阶段号。");
        builder.AppendLine("8. 审定时优先看核心验收标准是否已经满足，不要因为 PowerShell、shell snapshot、环境 warning、补充性自检失败等与核心交付无关的噪音就默认 retry_stage。");
        builder.AppendLine("9. 如果 Codex 已经提供足够证据证明核心目标达成，你应直接输出 complete。只有核心目标未完成或关键证据缺失时，才输出 retry_stage 或 blocked。");
        builder.AppendLine("10. decision=complete 或 blocked 时，不要输出 title、summary、steps、acceptance、codex_brief。");
        builder.AppendLine("11. 严格复制下面这个模板的语法，只替换内容，不要改字段名、冒号、结束标记或闭合标签：");
        builder.AppendLine("""
[AIPAIR_PACKET]
role: claude
kind: review_decision
stage_id: 1
decision: complete
body: <<<BODY
这里写审定结论
BODY
[/AIPAIR_PACKET]
""");
        builder.AppendLine("12. 如果 decision=next_stage 或 retry_stage，则严格改用下面这个模板：");
        builder.AppendLine("""
[AIPAIR_PACKET]
role: claude
kind: review_decision
stage_id: 2
decision: next_stage
title: 示例标题
summary: <<<SUMMARY
这里写阶段摘要
SUMMARY
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
body: <<<BODY
这里写审定说明
BODY
[/AIPAIR_PACKET]
""");
        builder.AppendLine("13. 包外不要输出任何解释。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildClaudeRevisionPrompt(ApprovalDraft draft, string? userNote, string taskPrompt)
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
        builder.AppendLine("严格复制下面这个模板的语法，只替换内容，不要改字段名、冒号、结束标记或闭合标签：");
        builder.AppendLine($"""
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: {draft.StageId}
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
""");
        return builder.ToString().TrimEnd();
    }
}
