using System.Text.Json;
using LogAnalyzer.AI.Classification;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.AI;

public partial class OpenAiLogAnalyzer
{
    public async Task<LogAnalysisBatchCandidatesResult> AnalyzeBatchCandidatesAsync(
        string log,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var model = ResolveChatModel(configuration);
        log = TruncateLogPayload(configuration, log);

        var prompt =
            """
You are an operational incident decomposer, not a summarizer, not a global analyst, and not a narrative generator.

Goal: split the batch into separate candidates so each candidate is one operational incident thread (one failure domain, one remediation path). Prefer many focused candidates over one merged story.

Segmentation (do not merge unrelated failures):
- Do not merge unrelated operational failures into one candidate.
- Create separate candidates for different failure domains (payment vs inventory vs auth vs webhooks vs TLS vs DB vs external HTTP).
- The following are operationally distinct when they appear as different problems in the logs; they must not share one candidate unless they are clearly the same single incident with one root cause:
  timeout, deadlock, TLS or certificate warning, validation failure, payment provider or capture failure, webhook signature mismatch, inventory or stock API latency, upstream unavailable, downstream timeout, external dependency outage.
- A webhook signature mismatch and a payment capture failure must never share one candidate.
- A timeout and a deadlock are different incidents.
- Inventory API latency and a payment decline are different incidents.

Decomposition heuristics (usually different candidate when these differ across log evidence):
- Different named service or component
- Different external dependency or integration (PSP, warehouse API, webhook endpoint, identity provider)
- Different failure mode (timeout vs 4xx vs 5xx vs deadlock vs TLS vs signature mismatch)
- Different operational symptom (latency vs hard error vs warning)

Candidate count:
- If the batch shows multiple distinct dependency failures, different timeout patterns, different services, or different symptoms, output multiple candidates (up to 5). Do not collapse unrelated problems into one candidate to "summarize the batch."
- If one problem repeats across many lines with the same failure mode and same dependency, one candidate is enough.
- If the entire batch supports only one operational problem, output exactly one candidate.

Titles (normalized_operational_title):
- Short, operational, actionable: name the subsystem and the failure, e.g. "Payment capture declined in PaymentOrchestrator", "Inventory API timeout", "Webhook signature mismatch", "OrderService Postgres deadlock".
- Forbidden: vague batch titles like "Several critical issues affecting backend operations", "Multiple problems detected", "Various errors in the system", or any title that reads like an executive summary of unrelated items.

Per-candidate text:
- Each candidate must describe exactly one operational problem class.
- technical_summary must reference only evidence for that candidate. No global narrative spanning unrelated problems. Do not list other candidates' failures inside one summary.

Voice (forbidden analyst / batch-report tone in technical_summary, possible_root_cause, and recommended_action):
- Do not write openings like: "Multiple high-severity errors were reported across", "Notably,", "Additionally,", "In particular,", "A combination of", "Several issues", "Various failures", "across the backend services".
- Do not produce one numbered list (1. 2. 3.) that assigns unrelated actions to different subsystems inside a single candidate; that pattern means you merged incidents and must split into multiple candidates instead.
- Write like an on-call handoff for one incident: what broke, where in logs, what to check first. One paragraph for technical_summary is enough if it stays single-incident scoped.

Positive example (GOOD — three candidates):
- Candidate 1 title: Payment capture declined in PaymentOrchestrator — payment PSP decline only.
- Candidate 2 title: TLS certificate expiration warning on ApiGateway — TLS only.
- Candidate 3 title: Inventory API timeout — inventory latency/timeout only.

Negative example (BAD — forbidden):
- One candidate whose technical_summary mixes timeout + TLS warning + payment failure + webhook signature mismatch + inventory latency in one story.

Return ONLY one JSON object (no markdown fences, no prose before or after, no JSON array).

Allowed root keys exactly:
schema_version, candidates

schema_version must be "2".

Candidates array length: 1..5 inclusive.

Candidate object allowed keys exactly:
severity, category, technical_summary, possible_root_cause, recommended_action, confidence, normalized_operational_title, matching_terms

Field rules:
- severity must be one of: critical, high, medium, low
- category must be one of: backend, ui, database, infrastructure, authentication, external_service, network, unknown
- possible_root_cause: one clear hypothesis for this candidate only; if uncertain, say so in one sentence.
- recommended_action: concrete next steps for this candidate only; one short paragraph or a tight bullet list that all targets the same failure thread (not a mini project plan across Billing, Inventory, and TLS in one candidate).
- confidence: JSON number 0..1 for how well the logs support this extraction.
- normalized_operational_title: max about 12 words; stable operational wording; no timestamps, trace_id, line numbers, or GUIDs.

Logs:
""";

        prompt += log;

        var promptLineCount = prompt.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
        var promptBodyLogMax = ResolvePromptBodyLogMaxChars(configuration);
        logger.LogInformation(
            "OpenAI batch-candidates request: model {Model}, prompt {PromptLineCount} lines, {PromptCharCount} characters.",
            model,
            promptLineCount,
            prompt.Length);
        logger.LogInformation(
            "OpenAI batch-candidates prompt body ({TotalChars} chars; docker log cap {LogCapChars}): {PromptBody}",
            prompt.Length,
            promptBodyLogMax,
            TruncateForLog(prompt, promptBodyLogMax));

        var primaryAssistantOutput = await PostChatCompletionAsync(prompt, cancellationToken).ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var primaryJson = ExtractFirstBalancedJsonObject(primaryAssistantOutput);
        if (!string.IsNullOrWhiteSpace(primaryJson)
            && MultiCandidateClassificationV2Contracts.TryParseStrictV2(primaryJson, out var strictPrimary))
        {
            strictPrimary.RawAIResponse = primaryAssistantOutput;
            strictPrimary.ClassificationParseMode = MultiCandidateClassificationV2Contracts.ParseModeStrictV2;
            strictPrimary.ClassificationFallbackUsed = MultiCandidateClassificationV2Contracts.FallbackFalse;
            strictPrimary.ClassificationRetryCount = 0;
            LogBatchCandidateExtractionDebug(strictPrimary);
            return strictPrimary;
        }

        logger.LogDebug(
            "Batch-candidates transition: primary strict parse failed; invoking repair completion (single retry)");

        var repairPrompt = BuildRepairPromptV2(primaryAssistantOutput);
        string repairOutput;
        try
        {
            repairOutput = await PostChatCompletionAsync(repairPrompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch-candidates classification repair completion failed; falling back to legacy/minimal parsing.");
            repairOutput = string.Empty;
        }

        var repairJson = ExtractFirstBalancedJsonObject(repairOutput);
        if (!string.IsNullOrWhiteSpace(repairJson)
            && MultiCandidateClassificationV2Contracts.TryParseStrictV2(repairJson, out var strictRepair))
        {
            strictRepair.RawAIResponse = repairOutput;
            strictRepair.ClassificationParseMode = MultiCandidateClassificationV2Contracts.ParseModeStrictV2Repaired;
            strictRepair.ClassificationFallbackUsed = MultiCandidateClassificationV2Contracts.FallbackFalse;
            strictRepair.ClassificationRetryCount = 1;
            LogBatchCandidateExtractionDebug(strictRepair);
            return strictRepair;
        }

        logger.LogDebug("Batch-candidates transition: repair strict parse failed; attempting legacy lift");

        LogAnalysisResult? legacyAfterRepair = null;
        if (!string.IsNullOrWhiteSpace(repairOutput))
        {
            legacyAfterRepair = TryDeserializeStructuredAnalysis(repairOutput, jsonOptions).Result;
        }

        var legacyPrimary = TryDeserializeStructuredAnalysis(primaryAssistantOutput, jsonOptions).Result;
        var legacy = legacyAfterRepair ?? legacyPrimary;
        if (legacy is not null)
        {
            var candidate = MapLegacyToCandidate(legacy);
            return new LogAnalysisBatchCandidatesResult
            {
                SchemaVersion = MultiCandidateClassificationV2Contracts.SchemaVersionV2,
                Candidates = [candidate],
                RawAIResponse = primaryAssistantOutput,
                ClassificationParseMode = MultiCandidateClassificationV2Contracts.ParseModeLegacyLiftV2,
                ClassificationFallbackUsed = MultiCandidateClassificationV2Contracts.FallbackLegacyLiftV2,
                ClassificationRetryCount = 1
            };
        }

        logger.LogDebug("Batch-candidates transition: legacy lift failed; attempting minimal safe fallback");

        var minimal = MinimalSafeToCandidate(MultiCandidateClassificationV2Contracts.ParseModeMinimalSafeFallbackV2, primaryAssistantOutput);
        return new LogAnalysisBatchCandidatesResult
        {
            SchemaVersion = MultiCandidateClassificationV2Contracts.SchemaVersionV2,
            Candidates = [minimal],
            RawAIResponse = primaryAssistantOutput,
            ClassificationParseMode = MultiCandidateClassificationV2Contracts.ParseModeMinimalSafeFallbackV2,
            ClassificationFallbackUsed = MultiCandidateClassificationV2Contracts.FallbackMinimalSafeV2,
            ClassificationRetryCount = 1
        };
    }

    private static OperationalIncidentCandidate MapLegacyToCandidate(LogAnalysisResult legacy)
    {
        return new OperationalIncidentCandidate
        {
            Severity = legacy.Severity,
            Category = legacy.Category,
            TechnicalSummary = legacy.Summary,
            PossibleRootCause = legacy.PossibleRootCause,
            RecommendedAction = legacy.Suggestion,
            Confidence = legacy.Confidence,
            NormalizedOperationalTitle = NormalizeTitle(legacy.Summary),
            MatchingTerms = new List<string>()
        };
    }

    private static OperationalIncidentCandidate MinimalSafeToCandidate(string parseMode, string rawPrimary)
    {
        _ = parseMode;
        _ = rawPrimary;

        return new OperationalIncidentCandidate
        {
            Severity = "low",
            Category = "unknown",
            TechnicalSummary = ClassificationContracts.MinimalSafeTexts.TechnicalSummary,
            PossibleRootCause = ClassificationContracts.MinimalSafeTexts.PossibleRootCause,
            RecommendedAction = ClassificationContracts.MinimalSafeTexts.RecommendedAction,
            Confidence = 0.0,
            NormalizedOperationalTitle = ClassificationContracts.MinimalSafeTexts.TechnicalSummary,
            MatchingTerms = new List<string>()
        };
    }

    private static string NormalizeTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown operational problem";
        }

        var s = text.Trim();
        s = s.Replace("\r", " ").Replace("\n", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b\d{4}-\d{2}-\d{2}\b", "<date>");
        return s.Length <= 180 ? s : s[..180].Trim();
    }

    private static string BuildRepairPromptV2(string invalidAssistantOutput) =>
        """
You failed the operational incident decomposer contract. Return ONLY one JSON object (no markdown fences, no prose).

Decompose unrelated operational failures into separate candidates (max 5). Prefer separation over aggregation.
Never merge: payment capture failure with webhook signature mismatch; inventory latency with payment errors; timeout with deadlock; TLS warnings with unrelated HTTP 5xx; upstream unavailable with unrelated validation failures.
Each technical_summary must cover one incident thread only. Titles must be short and operational (subsystem + failure), not vague multi-issue summaries.
Ban batch-report phrasing such as "Multiple high-severity errors across", "Additionally", "Notably", "combination of", and multi-subsystem numbered action lists inside one candidate.

Allowed root keys exactly: schema_version, candidates
schema_version must be "2".

Candidates must be an array (length 1..5).

Each candidate allowed keys exactly:
severity, category, technical_summary, possible_root_cause, recommended_action, confidence, normalized_operational_title, matching_terms

Field rules:
- severity must be one of: critical, high, medium, low
- category must be one of: backend, ui, database, infrastructure, authentication, external_service, network, unknown
- technical_summary, possible_root_cause, recommended_action, normalized_operational_title must be non-empty strings
- technical_summary must be scoped to that candidate only (no batch-wide merged narrative).
- confidence must be a JSON number between 0 and 1
- matching_terms must be an array of 3..8 short evidence terms (strings) that route log lines to this candidate
- normalized_operational_title: max about 12 words; no timestamps, trace_id, line numbers, or GUIDs

Invalid prior output:
""" + invalidAssistantOutput;

    private static readonly JsonSerializerOptions CandidateDebugJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private void LogBatchCandidateExtractionDebug(LogAnalysisBatchCandidatesResult result)
    {
        var categories = string.Join(" || ", result.Candidates.Select(c => c.Category));
        var titles = string.Join(" || ", result.Candidates.Select(c => c.NormalizedOperationalTitle));
        var termsByIdx = string.Join(
            " ;; ",
            result.Candidates.Select((c, i) => $"[{i}]{string.Join("|", c.MatchingTerms)}"));

        logger.LogInformation(
            "Batch candidate extraction debug summary: parse_mode={ParseMode} retry={Retry} fallback={Fallback} candidate_count={Count} titles={Titles} categories={Categories} matching_terms_by_idx={TermsByIdx}",
            result.ClassificationParseMode,
            result.ClassificationRetryCount,
            result.ClassificationFallbackUsed,
            result.Candidates.Count,
            titles,
            categories,
            termsByIdx);

        for (var i = 0; i < result.Candidates.Count; i++)
        {
            var c = result.Candidates[i];
            var terms = string.Join("|", c.MatchingTerms);
            var oneJson = JsonSerializer.Serialize(c, CandidateDebugJson);
            if (oneJson.Length > 4000)
            {
                oneJson = oneJson[..4000] + "…(truncated)";
            }

            logger.LogInformation(
                "Batch candidate extraction debug: parse_mode={ParseMode} idx={Idx} title={Title} terms={Terms} sev={Severity} cat={Category} conf={Confidence} candidate_json={Json}",
                result.ClassificationParseMode,
                i,
                c.NormalizedOperationalTitle,
                terms,
                c.Severity,
                c.Category,
                c.Confidence,
                oneJson);
        }

        var allJson = JsonSerializer.Serialize(result.Candidates, CandidateDebugJson);
        if (allJson.Length > 16000)
        {
            allJson = allJson[..16000] + "…(truncated)";
        }

        logger.LogInformation(
            "Batch candidate extraction debug: candidates_array_json={Json}",
            allJson);

        var raw = result.RawAIResponse ?? string.Empty;
        if (raw.Length > 12000)
        {
            raw = raw[..12000] + "…(truncated)";
        }

        logger.LogInformation(
            "Batch candidate extraction debug: raw_assistant_truncated={Raw}",
            raw);
    }
}

