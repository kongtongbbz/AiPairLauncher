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
        var stageId = ParseStageId(GetRequiredScalar(scalarValues, "stage_id"));
        var title = GetSectionOrScalar(sectionValues, scalarValues, "title");
        var summary = GetSectionOrScalar(sectionValues, scalarValues, "summary");
        var scope = GetSectionOrScalar(sectionValues, scalarValues, "scope");
        var status = scalarValues.TryGetValue("status", out var statusValue) ? statusValue : null;
        ReviewDecision? decision = scalarValues.TryGetValue("decision", out var decisionValue)
            ? ParseDecision(decisionValue)
            : null;
        var steps = ParseList(sectionValues, scalarValues, "steps");
        var acceptanceCriteria = ParseList(sectionValues, scalarValues, "acceptance");
        var completedItems = ParseList(sectionValues, scalarValues, "completed");
        var verificationItems = ParseList(sectionValues, scalarValues, "verification");
        var blockers = ParseList(sectionValues, scalarValues, "blockers");
        var reviewFocus = GetSectionOrScalar(sectionValues, scalarValues, "review_focus");
        var body = GetSectionOrScalar(sectionValues, scalarValues, "body");
        var codexBrief = GetSectionOrScalar(sectionValues, scalarValues, "codex_brief");

        var packet = new AgentPacket
        {
            Role = role,
            Kind = kind,
            StageId = stageId,
            Fingerprint = ComputeSemanticFingerprint(
                role,
                kind,
                stageId,
                status,
                decision,
                title,
                summary,
                scope,
                steps,
                acceptanceCriteria,
                completedItems,
                verificationItems,
                blockers,
                reviewFocus,
                body,
                codexBrief),
            RawText = block,
            Title = title,
            Summary = summary,
            Scope = scope,
            Status = status,
            Decision = decision,
            Steps = steps,
            AcceptanceCriteria = acceptanceCriteria,
            CompletedItems = completedItems,
            VerificationItems = verificationItems,
            Blockers = blockers,
            ReviewFocus = reviewFocus,
            Body = body,
            CodexBrief = codexBrief,
        };

        ValidateRequiredContent(packet);
        return packet;
    }

    private static void ValidateRequiredContent(AgentPacket packet)
    {
        if (packet.Kind == PacketKind.StagePlan && string.IsNullOrWhiteSpace(packet.CodexBrief))
        {
            throw new FormatException("stage_plan 缺少 codex_brief。");
        }

        if (packet.Kind == PacketKind.ReviewDecision &&
            packet.Decision is ReviewDecision.NextStage or ReviewDecision.RetryStage &&
            string.IsNullOrWhiteSpace(packet.CodexBrief))
        {
            throw new FormatException("review_decision 在 next_stage/retry_stage 时必须包含 codex_brief。");
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
        int stageId,
        string? status,
        ReviewDecision? decision,
        string title,
        string summary,
        string scope,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> acceptanceCriteria,
        IReadOnlyList<string> completedItems,
        IReadOnlyList<string> verificationItems,
        IReadOnlyList<string> blockers,
        string reviewFocus,
        string body,
        string codexBrief)
    {
        var normalized = string.Join(
            "|",
            role,
            kind,
            stageId.ToString(CultureInfo.InvariantCulture),
            NormalizeWhitespace(status),
            decision?.ToString() ?? string.Empty,
            NormalizeWhitespace(title),
            NormalizeWhitespace(summary),
            NormalizeWhitespace(scope),
            NormalizeList(steps),
            NormalizeList(acceptanceCriteria),
            NormalizeList(completedItems),
            NormalizeList(verificationItems),
            NormalizeList(blockers),
            NormalizeWhitespace(reviewFocus),
            NormalizeWhitespace(body),
            NormalizeWhitespace(codexBrief));

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

    private static string NormalizeKey(string value)
    {
        return value
            .Trim()
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
