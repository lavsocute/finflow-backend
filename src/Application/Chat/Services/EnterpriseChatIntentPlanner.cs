using System.Text.Json;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Services;

public sealed class EnterpriseChatIntentPlanner : IChatIntentPlanner
{
    private readonly ILlmChatService _llm;
    private readonly ILogger<EnterpriseChatIntentPlanner> _logger;
    private readonly ITextNormalizer _textNormalizer;
    private readonly DeterministicReportingTaskPlanner _deterministicPlanner;
    private readonly GroqChatOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EnterpriseChatIntentPlanner(
        ILlmChatService llm,
        ILogger<EnterpriseChatIntentPlanner> logger,
        IOptions<GroqChatOptions>? options = null,
        ITextNormalizer? textNormalizer = null)
    {
        _llm = llm;
        _logger = logger;
        _options = options?.Value ?? new GroqChatOptions();
        _textNormalizer = textNormalizer ?? new TextNormalizer();
        _deterministicPlanner = new DeterministicReportingTaskPlanner(_textNormalizer);
    }

    public async Task<ChatIntentClassification> ClassifyAsync(
        ChatIntentPlanningRequest request,
        CancellationToken ct = default)
    {
        if (TryClassifySafety(request.Query, out var safetyClassification))
            return safetyClassification;

        var deterministicClassification = _deterministicPlanner.TryClassify(request.Query);
        if (deterministicClassification is not null)
            return deterministicClassification;

        try
        {
            return await ClassifyWithLlmAsync(request, useStructuredOutput: true, ct);
        }
        catch (Exception ex) when (IsStructuredOutputFailure(ex))
        {
            _logger.LogWarning(ex, "Structured LLM intent planning failed; retrying without provider response_format.");
            try
            {
                return await ClassifyWithLlmAsync(request, useStructuredOutput: false, ct);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Prompt-only JSON intent planning failed; falling back to non-business RAG classification.");
                return FallbackClassification();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM intent planning failed; falling back to non-business RAG classification.");
            return FallbackClassification();
        }
    }

    private async Task<ChatIntentClassification> ClassifyWithLlmAsync(
        ChatIntentPlanningRequest request,
        bool useStructuredOutput,
        CancellationToken ct)
    {
        var result = await _llm.ChatAsync(
            new LlmChatRequest(
                System: BuildSystemPrompt(request.Today),
                Messages: [new LlmMessage("user", BuildUserMessage(request.Query))],
                Model: _options.IntentPlannerModel,
                Temperature: 0,
                MaxTokens: 350,
                ResponseFormat: useStructuredOutput
                    ? LlmResponseFormat.ForJsonObject()
                    : null),
            ct);

        return ParseClassification(result.Content);
    }

    private static ChatIntentClassification FallbackClassification() =>
        new(
            ChatExecutionMode.Rag,
            "planner-fallback-rag",
            ChatIntentFamily.Unknown,
            ChatScopeConfidence.Ambiguous);

    private static bool IsStructuredOutputFailure(Exception ex) =>
        ex is LlmProviderException { IsSchemaFailure: true } ||
        ex is JsonException ||
        ex.Message.Contains("json_validate_failed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned invalid", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned an empty", StringComparison.OrdinalIgnoreCase);

    private bool TryClassifySafety(string query, out ChatIntentClassification classification)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.Rag,
                "empty-default",
                ChatIntentFamily.Unknown,
                ChatScopeConfidence.Ambiguous);
            return true;
        }

        var normalized = _textNormalizer.Normalize(query);
        if (IsPromptBoundaryRequest(normalized))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.General,
                "prompt-boundary-deny",
                ChatIntentFamily.PromptBoundary,
                ChatScopeConfidence.Ambiguous);
            return true;
        }

        if (IsScopeOnlyQuery(normalized))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.General,
                "scope-only-low-signal",
                ChatIntentFamily.LowSignal,
                ChatScopeConfidence.Ambiguous);
            return true;
        }

        if (IsDestructiveOperationRequest(normalized))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.General,
                "destructive-action-deny",
                ChatIntentFamily.DestructiveAction,
                ChatScopeConfidence.Forbidden);
            return true;
        }

        if (IsEvidenceDocumentLookup(normalized))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.Rag,
                "deterministic-evidence-document-lookup",
                ChatIntentFamily.DocumentLookup,
                ChatScopeConfidence.SafeInferred);
            return true;
        }

        if (IsContextOnlyPeriodFollowUp(normalized))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.Rag,
                "context-follow-up-rag",
                ChatIntentFamily.Unknown,
                ChatScopeConfidence.Ambiguous);
            return true;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 && tokens[0].Length <= 3 && tokens[0].All(char.IsLetterOrDigit))
        {
            classification = new ChatIntentClassification(
                ChatExecutionMode.General,
                "low-signal-general",
                ChatIntentFamily.LowSignal,
                ChatScopeConfidence.Ambiguous);
            return true;
        }

        classification = default!;
        return false;
    }

    private static bool IsScopeOnlyQuery(string normalizedQuery) =>
        normalizedQuery is "toan cong ty" or "cong ty" or "company" or "all company" or
            "workspace" or "phong ban" or "phong ban cua toi" or "phong ban toi" or
            "bo phan cua toi" or "bo phan toi" or "team toi" or "my team" or
            "cua toi" or "cua em" or "cua minh" or "toi" or "minh" or "my";

    private static bool IsPromptBoundaryRequest(string normalizedQuery)
    {
        var paddedQuery = $" {normalizedQuery} ";
        return ContainsAnyPadded(paddedQuery,
            [
                "reveal system",
                "reveal your system",
                "system instructions",
                "system prompt",
                "developer message",
                "developer instructions",
                "instructions verbatim",
                "copy your instructions",
                "show your instructions",
                "noi dung system",
                "huong dan he thong",
                "prompt he thong"
            ]);
    }

    private static bool IsEvidenceDocumentLookup(string normalizedQuery)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var mentionsEvidenceDocument = ContainsAnyPadded(paddedQuery,
            [
                "evidence",
                "invoice",
                "invoices",
                "receipt",
                "receipts",
                "hoa don",
                "chung tu"
            ]);
        if (!mentionsEvidenceDocument)
            return false;

        return ContainsAnyPadded(paddedQuery,
            [
                "list",
                "show",
                "behind",
                "underlying",
                "source",
                "sources",
                "proof",
                "that",
                "do",
                "liet ke",
                "xem"
            ]);
    }

    private static bool IsContextOnlyPeriodFollowUp(string normalizedQuery)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var mentionsRelativePeriod = ContainsAnyPadded(paddedQuery,
            [
                "thang truoc",
                "previous month",
                "prev month",
                "last month"
            ]);
        if (!mentionsRelativePeriod)
            return false;

        return ContainsAnyPadded(paddedQuery,
            [
                "con",
                "thi sao",
                "same thing",
                "same",
                "that",
                "do"
            ]);
    }

    private static bool ContainsAnyPadded(string paddedQuery, IReadOnlyList<string> phrases) =>
        phrases.Any(phrase => paddedQuery.Contains($" {phrase} ", StringComparison.Ordinal));

    private static bool IsDestructiveOperationRequest(string normalizedQuery)
    {
        if (normalizedQuery.Contains("xoa la gi", StringComparison.Ordinal) ||
            normalizedQuery.Contains("delete la gi", StringComparison.Ordinal) ||
            normalizedQuery.Contains("delete mean", StringComparison.Ordinal))
        {
            return false;
        }

        var destructiveTerms = new[] { "xoa", "delete", "drop", "wipe", "loai bo", "remove", "tieu huy", "pha huy", "destroy" };
        var paddedQuery = $" {normalizedQuery} ";
        return destructiveTerms.Any(term => paddedQuery.Contains($" {term} ", StringComparison.Ordinal));
    }

    private static string BuildSystemPrompt(DateOnly today) =>
        $$"""
        You are FinFlow's enterprise intent planner for a multi-tenant finance workspace chatbot.
        Classify the user's latest message for routing only. Do not answer the user.

        Today is {{today:yyyy-MM-dd}}.

        Routing contract:
        - Reporting: aggregate, ranking, comparison, budget, approval queue, trend, vendor summaries from structured workspace data.
        - Rag: document lookup, receipt/invoice detail lookup, evidence retrieval, or questions requiring source chunks.
        - General: greetings, small talk, productivity rewriting/summarization, unsupported programming or advice requests.
        - Greeting: pure greeting only.

        Reporting task contract:
        - Summary: total spend or aggregate financial picture.
        - Trend: period trend over multiple months.
        - VendorRanking: top vendor/supplier/merchant by spend or document count.
        - EmployeeRanking: top employee/person/team member by spend.
        - BudgetUtilization: budget remaining, over-budget, utilization.
        - ApprovalQueue: list/count documents waiting for approval.
        - Comparison: increase/decrease or compare two periods/scopes/entities.
        - EntityStatusLookup: status of a previously mentioned document/expense/entity; prefer Rag executionMode if source evidence is needed.
        - Unknown: no structured reporting task applies.

        Enterprise rules:
        - Return only the JSON schema response.
        - Keep reason compact and under 160 characters.
        - Do not infer access; access control is enforced by policy code after classification.
        - If the message is under-specified, use General/LowSignal or Rag/Unknown instead of inventing a report.
        - Business intent must be inferred semantically, including Vietnamese, English, mixed language, typos, and paraphrases.
        """;

    private static string BuildUserMessage(string query) =>
        JsonSerializer.Serialize(new { query }, JsonOptions);

    private static object BuildJsonSchema() => new
    {
        type = "object",
        properties = new
        {
            executionMode = new
            {
                type = "string",
                @enum = Enum.GetNames<ChatExecutionMode>()
            },
            intentFamily = new
            {
                type = "string",
                @enum = Enum.GetNames<ChatIntentFamily>()
            },
            reportingTask = new
            {
                type = "string",
                @enum = Enum.GetNames<ChatReportingTask>()
            },
            scopeConfidence = new
            {
                type = "string",
                @enum = Enum.GetNames<ChatScopeConfidence>()
            },
            reason = new
            {
                type = "string",
                minLength = 1
            }
        },
        required = new[] { "executionMode", "intentFamily", "reportingTask", "scopeConfidence", "reason" },
        additionalProperties = false
    };

    private static ChatIntentClassification ParseClassification(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(content));
        var root = doc.RootElement;

        var mode = ParseRequiredEnum<ChatExecutionMode>(root, "executionMode");
        var family = ParseRequiredEnum<ChatIntentFamily>(root, "intentFamily");
        var reportingTask = ParseOptionalEnum(root, "reportingTask", ChatReportingTask.Unknown);
        var scopeConfidence = ParseRequiredEnum<ChatScopeConfidence>(root, "scopeConfidence");
        var reason = root.GetProperty("reason").GetString();

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Intent planner returned an empty reason.");

        return new ChatIntentClassification(mode, reason.Trim(), family, scopeConfidence, reportingTask);
    }

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start
            ? content[start..(end + 1)]
            : content;
    }

    private static TEnum ParseRequiredEnum<TEnum>(JsonElement root, string propertyName)
        where TEnum : struct, Enum
    {
        var value = root.GetProperty(propertyName).GetString();
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"Intent planner returned invalid {propertyName}: {value}");

        return parsed;
    }

    private static TEnum ParseOptionalEnum<TEnum>(JsonElement root, string propertyName, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return fallback;

        var value = element.GetString();
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
