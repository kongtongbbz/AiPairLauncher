using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AgentPacketParserTests
{
    private readonly AgentPacketParser _parser = new();

    [Fact(DisplayName = "test_parse_stage_plan_valid_packet")]
    public void ParseStagePlanValidPacket()
    {
        var outcome = _parser.ParseLatest(AutomationTestHelpers.BuildStagePlanPacket(1, "首阶段", "执行首阶段"));

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal(AgentRole.Claude, outcome.Packet!.Role);
        Assert.Equal(PacketKind.StagePlan, outcome.Packet.Kind);
        Assert.Equal(1, outcome.Packet.StageId);
        Assert.Equal("首阶段", outcome.Packet.Title);
        Assert.Equal("执行首阶段", outcome.Packet.CodexBrief);
    }

    [Fact(DisplayName = "test_parse_stage_plan_accepts_handoff_brief")]
    public void ParseStagePlanAcceptsHandoffBrief()
    {
        var packet = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
title: 首阶段
summary: <<<SUMMARY
阶段摘要
SUMMARY
scope: <<<SCOPE
范围说明
SCOPE
steps: <<<STEPS
1. 第一步
STEPS
acceptance: <<<ACCEPTANCE
1. 验收项
ACCEPTANCE
handoff_brief: <<<HANDOFF
用 handoff 交接摘要
HANDOFF
[/AIPAIR_PACKET]
""";

        var outcome = _parser.ParseLatest(packet);

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal("用 handoff 交接摘要", outcome.Packet!.CodexBrief);
    }

    [Fact(DisplayName = "test_parse_execution_report_valid_packet")]
    public void ParseExecutionReportValidPacket()
    {
        var outcome = _parser.ParseLatest(AutomationTestHelpers.BuildExecutionReportPacket(2, "阶段 2 已完成"));

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal(AgentRole.Codex, outcome.Packet!.Role);
        Assert.Equal(PacketKind.ExecutionReport, outcome.Packet.Kind);
        Assert.Equal("success", outcome.Packet.Status);
        Assert.Contains("完成编码", outcome.Packet.CompletedItems);
    }

    [Fact(DisplayName = "test_parse_phase_aware_packet_with_task_fields")]
    public void ParsePhaseAwarePacketWithTaskFields()
    {
        var packet = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: phase2_planning
subagent: planner
stage_id: 1
task_ref: T1.2
parallel_group: group-a
retry_count: 2
task_md_path: D:\repo\task.md
task_md_status: planned
title: Phase 2 规划
summary: <<<SUMMARY
这是规划摘要
SUMMARY
scope: <<<SCOPE
规划范围
SCOPE
steps: <<<STEPS
1. 调整任务顺序
2. 标记高风险项
STEPS
acceptance: <<<ACCEPTANCE
1. 输出结构化执行计划
ACCEPTANCE
task_progress: <<<TASK_PROGRESS
1. T1.1 已完成
2. T1.2 待处理
TASK_PROGRESS
codex_brief: <<<CODEX_BRIEF
等待 Phase 3 再发送给 Codex
CODEX_BRIEF
[/AIPAIR_PACKET]
""";

        var outcome = _parser.ParseLatest(packet);

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal(AutomationPhase.Phase2Planning, outcome.Packet!.Phase);
        Assert.Equal("planner", outcome.Packet.Subagent);
        Assert.Equal("T1.2", outcome.Packet.TaskRef);
        Assert.Equal("group-a", outcome.Packet.ParallelGroup);
        Assert.Equal(2, outcome.Packet.RetryCount);
        Assert.Equal(@"D:\repo\task.md", outcome.Packet.TaskMdPath);
        Assert.Equal(TaskMdStatus.Planned, outcome.Packet.TaskMdStatus);
        Assert.Equal(2, outcome.Packet.TaskProgress.Count);
    }

    [Fact(DisplayName = "test_ignore_incomplete_packet")]
    public void IgnoreIncompletePacket()
    {
        var incomplete = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
title: 未完成
""";

        var outcome = _parser.ParseLatest(incomplete);

        Assert.Equal(PacketParseStatus.NoPacket, outcome.Status);
        Assert.Null(outcome.Packet);
    }

    [Fact(DisplayName = "test_extract_latest_packet_requires_standalone_markers")]
    public void ExtractLatestPacketRequiresStandaloneMarkers()
    {
        var mixedText = """
PS D:\repo> echo [AIPAIR_PACKET]
txt' -Force -ErrorAction SilentlyContinue }
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
title: 合法计划
summary: <<<SUMMARY
摘要
SUMMARY
scope: <<<SCOPE
范围
SCOPE
steps: <<<STEPS
1. 第一步
STEPS
acceptance: <<<ACCEPTANCE
1. 验收项
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
执行摘要
CODEX_BRIEF
[/AIPAIR_PACKET]
PS D:\repo>
""";

        var outcome = _parser.ParseLatest(mixedText);

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal(1, outcome.Packet!.StageId);
        Assert.Equal("合法计划", outcome.Packet.Title);
    }

    [Fact(DisplayName = "test_ignore_preview_template_markers")]
    public void IgnorePreviewTemplateMarkers()
    {
        var mixedText = """
[AiPair] Prompt -> Claude

[AIPAIR_PACKET_TEMPLATE]
role: claude
kind: stage_plan
stage_id: 1
title: 预览模板
summary: <<<SUMMARY
这是一段发给 Claude 的预览内容
SUMMARY
[/AIPAIR_PACKET_TEMPLATE]

[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 2
title: 真正计划
summary: <<<SUMMARY
真实阶段摘要
SUMMARY
scope: <<<SCOPE
真实执行范围
SCOPE
steps: <<<STEPS
1. 第一步
STEPS
acceptance: <<<ACCEPTANCE
1. 验收项
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
执行摘要
CODEX_BRIEF
[/AIPAIR_PACKET]
""";

        var outcome = _parser.ParseLatest(mixedText);

        Assert.Equal(PacketParseStatus.Success, outcome.Status);
        Assert.NotNull(outcome.Packet);
        Assert.Equal(2, outcome.Packet!.StageId);
        Assert.Equal("真正计划", outcome.Packet.Title);
    }

    [Fact(DisplayName = "test_semantic_fingerprint_ignores_line_wraps")]
    public void SemanticFingerprintIgnoresLineWraps()
    {
        var packetA = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
title: 指纹测试
summary: <<<SUMMARY
创建 validation.txt 文件
SUMMARY
scope: <<<SCOPE
仅修改 validation.txt
SCOPE
steps: <<<STEPS
1. 创建 validation.txt
2. 写入 aipair-e2e-ok
STEPS
acceptance: <<<ACCEPTANCE
1. 文件存在
2. 内容正确
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
创建 validation.txt 并写入 aipair-e2e-ok
CODEX_BRIEF
[/AIPAIR_PACKET]
""";

        var packetB = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
stage_id: 1
title: 指纹测试
summary: <<<SUMMARY
创建 validation.txt
文件
SUMMARY
scope: <<<SCOPE
仅修改 validation.txt
SCOPE
steps: <<<STEPS
1. 创建 validation.txt
2. 写入 aipair-e2e-ok
STEPS
acceptance: <<<ACCEPTANCE
1. 文件存在
2. 内容正确
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
创建 validation.txt
并写入 aipair-e2e-ok
CODEX_BRIEF
[/AIPAIR_PACKET]
""";

        var outcomeA = _parser.ParseLatest(packetA);
        var outcomeB = _parser.ParseLatest(packetB);

        Assert.Equal(PacketParseStatus.Success, outcomeA.Status);
        Assert.Equal(PacketParseStatus.Success, outcomeB.Status);
        Assert.Equal(outcomeA.Packet!.Fingerprint, outcomeB.Packet!.Fingerprint);
    }

    [Fact(DisplayName = "test_reject_zero_stage_id_in_parser")]
    public void RejectZeroStageIdInParser()
    {
        var packet = """
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: phase1_research
stage_id: 0
title: 非法阶段
summary: <<<SUMMARY
非法
SUMMARY
scope: <<<SCOPE
范围
SCOPE
steps: <<<STEPS
1. 第一步
STEPS
acceptance: <<<ACCEPTANCE
1. 验收项
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
无
CODEX_BRIEF
[/AIPAIR_PACKET]
""";

        var outcome = _parser.ParseLatest(packet);

        Assert.Equal(PacketParseStatus.Invalid, outcome.Status);
        Assert.Contains("stage_id 非法: 0", outcome.ErrorMessage);
    }
}
