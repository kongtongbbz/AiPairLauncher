using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AgentPacketParser : IAgentPacketParser
{
    private const string PacketStart = "[AIPAIR_PACKET]";
    private const string PacketEnd = "[/AIPAIR_PACKET]";

    public PacketParseOutcome ParseLatest(string paneText)
    {
        if (string.IsNullOrWhiteSpace(paneText))
        {
            return new PacketParseOutcome();
        }

        var block = TryExtractLatestBlock(paneText);
        if (block is null)
        {
            return new PacketParseOutcome();
        }

        try
        {
            return new PacketParseOutcome
            {
                Status = PacketParseStatus.Success,
                Packet = ParseBlock(block),
            };
        }
        catch (FormatException ex)
        {
            return new PacketParseOutcome
            {
                Status = PacketParseStatus.Invalid,
                ErrorMessage = ex.Message,
            };
        }
    }

    private static string? TryExtractLatestBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedText.Split('\n');
        var endLineIndex = Array.FindLastIndex(lines, static line => string.Equals(line.Trim(), PacketEnd, StringComparison.Ordinal));
        if (endLineIndex < 0)
        {
            return null;
        }

        var startLineIndex = Array.FindLastIndex(lines, endLineIndex, static line => string.Equals(line.Trim(), PacketStart, StringComparison.Ordinal));
        if (startLineIndex < 0 || startLineIndex >= endLineIndex)
        {
            return null;
        }

        return string.Join('\n', lines[(startLineIndex + 1)..endLineIndex]).Trim();
    }

    private static AgentPacket ParseBlock(string block)
    {
        var scalarValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sectionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StringReader(block);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                throw new FormatException($"无法识别结构化字段: {line}");
            }

            var key = NormalizeKey(line[..separatorIndex]);
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.StartsWith("<<<", StringComparison.Ordinal))
            {
                var terminator = value[3..].Trim();
                if (string.IsNullOrWhiteSpace(terminator))
                {
                    throw new FormatException($"字段 {key} 的结束标记不能为空。");
                }

                sectionValues[key] = ReadSection(reader, terminator).Trim();
                continue;
            }

            scalarValues[key] = value;
        }

        var role = ParseRole(GetRequiredScalar(scalarValues, "role"));
        var kind = ParseKind(GetRequiredScalar(scalarValues, "kind"));
        var phase = scalarValues.TryGetValue("phase", out var phaseValue)
            ? ParsePhase(phaseValue)
            : AutomationPhase.None;
        var stageId = ParseStageId(GetRequiredScalar(scalarValues, "stage_id"));
        var subagent = scalarValues.TryGetValue("subagent", out var subagentValue) ? subagentValue.Trim() : null;
        var taskRef = scalarValues.TryGetValue("task_ref", out var taskRefValue) ? taskRefValue.Trim() : null;
        var parallelGroup = scalarValues.TryGetValue("parallel_group", out var parallelGroupValue) ? parallelGroupValue.Trim() : null;
        int? retryCount = scalarValues.TryGetValue("retry_count", out var retryCountValue)
            ? ParseNullableInt(retryCountValue, "retry_count")
            : null;
        var title = GetSectionOrScalar(sectionValues, scalarValues, "title");
        var summary = GetSectionOrScalar(sectionValues, scalarValues, "summary");
        var scope = GetSectionOrScalar(sectionValues, scalarValues, "scope");
        var status = scalarValues.TryGetValue("status", out var statusValue) ? statusValue : null;
        ReviewDecision? decision = scalarValues.TryGetValue("decision", out var decisionValue)
            ? ParseDecision(decisionValue)
            : null;
        var steps = ParseList(sectionValues, scalarValues, "steps");
        var acceptanceCriteria = ParseList(sectionValues, scalarValues, "acceptance");
        var taskProgress = ParseList(sectionValues, scalarValues, "task_progress");
        var completedItems = ParseList(sectionValues, scalarValues, "completed");
        var verificationItems = ParseList(sectionValues, scalarValues, "verification");
        var blockers = ParseList(sectionValues, scalarValues, "blockers");
        var taskMdPath = scalarValues.TryGetValue("task_md_path", out var taskMdPathValue)
            ? taskMdPathValue.Trim()
            : null;
        var taskMdStatus = scalarValues.TryGetValue("task_md_status", out var taskMdStatusValue)
            ? ParseTaskMdStatus(taskMdStatusValue)
            : TaskMdStatus.Unknown;
        var reviewFocus = GetSectionOrScalar(sectionValues, scalarValues, "review_focus");
        var body = GetSectionOrScalar(sectionValues, scalarValues, "body");
        var executorBrief = GetSectionOrScalar(sectionValues, scalarValues, "executor_brief");
        if (string.IsNullOrWhiteSpace(executorBrief))
        {
            executorBrief = GetSectionOrScalar(sectionValues, scalarValues, "handoff_brief");
        }
        if (string.IsNullOrWhiteSpace(executorBrief))
        {
            executorBrief = GetSectionOrScalar(sectionValues, scalarValues, "codex_brief");
        }
        var packet = new AgentPacket
        {
            Role = role,
            Kind = kind,
            Phase = phase,
            StageId = stageId,
            Subagent = string.IsNullOrWhiteSpace(subagent) ? null : subagent,
            TaskRef = string.IsNullOrWhiteSpace(taskRef) ? null : taskRef,
            ParallelGroup = string.IsNullOrWhiteSpace(parallelGroup) ? null : parallelGroup,
            RetryCount = retryCount,
            Fingerprint = ComputeSemanticFingerprint(
                role,
                kind,
                phase,
                stageId,
                subagent,
                taskRef,
                parallelGroup,
                retryCount,
                status,
                decision,
                title,
                summary,
                scope,
                steps,
                acceptanceCriteria,
                taskProgress,
                completedItems,
                verificationItems,
                blockers,
                taskMdPath,
                taskMdStatus,
                reviewFocus,
                body,
                executorBrief),
            RawText = block,
            Title = title,
            Summary = summary,
            Scope = scope,
            TaskProgress = taskProgress,
            Status = status,
            Decision = decision,
            TaskMdPath = string.IsNullOrWhiteSpace(taskMdPath) ? null : taskMdPath,
            TaskMdStatus = taskMdStatus,
            Steps = steps,
            AcceptanceCriteria = acceptanceCriteria,
            CompletedItems = completedItems,
            VerificationItems = verificationItems,
            Blockers = blockers,
            ReviewFocus = reviewFocus,
            Body = body,
            ExecutorBrief = executorBrief,
        };

        ValidateRequiredContent(packet);
        return packet;
    }

    private static void ValidateRequiredContent(AgentPacket packet)
    {
        if (packet.Kind == PacketKind.StagePlan && string.IsNullOrWhiteSpace(packet.ExecutorBrief))
        {
            throw new FormatException("stage_plan 缺少 executor_brief/handoff_brief/codex_brief。");
        }

        if (packet.Kind == PacketKind.ReviewDecision &&
            packet.Decision is ReviewDecision.NextStage or ReviewDecision.RetryStage &&
            string.IsNullOrWhiteSpace(packet.ExecutorBrief))
        {
            throw new FormatException("review_decision 在 next_stage/retry_stage 时必须包含 executor_brief/handoff_brief/codex_brief。");
        }
    }

    private static string ReadSection(StringReader reader, string terminator)
    {
        var builder = new StringBuilder();
        while (reader.ReadLine() is { } line)
        {
            if (string.Equals(line.Trim(), terminator, StringComparison.Ordinal))
            {
                return builder.ToString();
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        throw new FormatException($"结构化字段缺少结束标记: {terminator}");
    }

    private static string ComputeSemanticFingerprint(
        AgentRole role,
        PacketKind kind,
        AutomationPhase phase,
        int stageId,
        string? subagent,
        string? taskRef,
        string? parallelGroup,
        int? retryCount,
        string? status,
        ReviewDecision? decision,
        string title,
        string summary,
        string scope,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> acceptanceCriteria,
        IReadOnlyList<string> taskProgress,
        IReadOnlyList<string> completedItems,
        IReadOnlyList<string> verificationItems,
        IReadOnlyList<string> blockers,
        string? taskMdPath,
        TaskMdStatus taskMdStatus,
        string reviewFocus,
        string body,
        string executorBrief)
    {
        var normalized = string.Join(
            "|",
            role,
            kind,
            phase,
            stageId.ToString(CultureInfo.InvariantCulture),
            NormalizeWhitespace(subagent),
            NormalizeWhitespace(taskRef),
            NormalizeWhitespace(parallelGroup),
            retryCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            NormalizeWhitespace(status),
            decision?.ToString() ?? string.Empty,
            NormalizeWhitespace(title),
            NormalizeWhitespace(summary),
            NormalizeWhitespace(scope),
            NormalizeList(steps),
            NormalizeList(acceptanceCriteria),
            NormalizeList(taskProgress),
            NormalizeList(completedItems),
            NormalizeList(verificationItems),
            NormalizeList(blockers),
            NormalizeWhitespace(taskMdPath),
            taskMdStatus,
            NormalizeWhitespace(reviewFocus),
            NormalizeWhitespace(body),
            NormalizeWhitespace(executorBrief));

        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash[..8]);
    }

    private static string NormalizeList(IReadOnlyList<string> items)
    {
        return string.Join("||", items.Select(NormalizeWhitespace));
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, string> scalars, string key)
    {
        if (scalars.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new FormatException($"结构化包缺少字段: {key}");
    }

    private static string GetSectionOrScalar(
        IReadOnlyDictionary<string, string> sections,
        IReadOnlyDictionary<string, string> scalars,
        string key)
    {
        if (sections.TryGetValue(key, out var sectionValue))
        {
            return sectionValue;
        }

        return scalars.TryGetValue(key, out var scalarValue) ? scalarValue : string.Empty;
    }

    private static IReadOnlyList<string> ParseList(
        IReadOnlyDictionary<string, string> sections,
        IReadOnlyDictionary<string, string> scalars,
        string key)
    {
        var source = GetSectionOrScalar(sections, scalars, key);
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        return source
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line =>
            {
                var cleaned = line.TrimStart('-', '*', ' ');
                if (cleaned.Length >= 3 &&
                    char.IsDigit(cleaned[0]) &&
                    cleaned[1] == '.')
                {
                    return cleaned[2..].Trim();
                }

                return cleaned.Trim();
            })
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static AgentRole ParseRole(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "claude" => AgentRole.Claude,
            "codex" => AgentRole.Codex,
            _ => throw new FormatException($"未知 role: {value}"),
        };
    }

    private static PacketKind ParseKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stage_plan" => PacketKind.StagePlan,
            "execution_report" => PacketKind.ExecutionReport,
            "review_decision" => PacketKind.ReviewDecision,
            _ => throw new FormatException($"未知 kind: {value}"),
        };
    }

    private static ReviewDecision ParseDecision(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "next_stage" => ReviewDecision.NextStage,
            "retry_stage" => ReviewDecision.RetryStage,
            "complete" => ReviewDecision.Complete,
            "blocked" => ReviewDecision.Blocked,
            _ => throw new FormatException($"未知 decision: {value}"),
        };
    }

    private static int ParseStageId(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stageId) && stageId > 0)
        {
            return stageId;
        }

        throw new FormatException($"stage_id 非法: {value}");
    }

    private static AutomationPhase ParsePhase(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "phase1_research" => AutomationPhase.Phase1Research,
            "phase2_planning" => AutomationPhase.Phase2Planning,
            "phase3_execution" => AutomationPhase.Phase3Execution,
            "phase4_review" => AutomationPhase.Phase4Review,
            _ => throw new FormatException($"未知 phase: {value}"),
        };
    }

    private static TaskMdStatus ParseTaskMdStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "pending_plan" => TaskMdStatus.PendingPlan,
            "planned" => TaskMdStatus.Planned,
            "in_progress" => TaskMdStatus.InProgress,
            "done" => TaskMdStatus.Done,
            _ => throw new FormatException($"未知 task_md_status: {value}"),
        };
    }

    private static int ParseNullableInt(string value, string fieldName)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"{fieldName} 非法: {value}");
    }

    private static string NormalizeKey(string value)
    {
        return value
            .Trim()
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
