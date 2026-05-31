using System.Diagnostics;
using System.Text.Json;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Cascade;

public sealed class LlmFirstIntentClassifier : ILlmIntentClassifier
{
    private readonly ILlmChatService _llm;
    private readonly GroqChatOptions _groq;
    private readonly LlmIntentClassifierOptions _options;
    private readonly ILogger<LlmFirstIntentClassifier> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LlmFirstIntentClassifier(
        ILlmChatService llm,
        IOptions<GroqChatOptions>? groq = null,
        IOptions<LlmIntentClassifierOptions>? options = null,
        ILogger<LlmFirstIntentClassifier>? logger = null)
    {
        _llm = llm;
        _groq = groq?.Value ?? new GroqChatOptions();
        _options = options?.Value ?? new LlmIntentClassifierOptions();
        _logger = logger ?? NullLogger<LlmFirstIntentClassifier>.Instance;
    }

    public async Task<LlmIntentClassificationResult> ClassifyAsync(
        IntentClassificationContext context,
        EmbeddingIntentMatch? topHint,
        CancellationToken ct)
    {
        var primaryModel = _options.PrimaryModel ?? _groq.IntentPlannerModel;
        var fallbackModel = _options.FallbackModel ?? "llama-3.1-8b-instant";

        try
        {
            return await InvokeAsync(context, topHint, primaryModel, useStructuredOutput: true, isFallback: false, ct);
        }
        catch (Exception ex) when (IsSchemaFailure(ex))
        {
            _logger.LogWarning(ex, "LLM-first classifier: structured output failed; retrying without response_format.");
            try
            {
                return await InvokeAsync(context, topHint, primaryModel, useStructuredOutput: false, isFallback: false, ct);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "LLM-first classifier: prompt-only JSON also failed; trying fallback model.");
                return await InvokeFallbackAsync(context, topHint, fallbackModel, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM-first classifier: primary model failed; trying fallback model.");
            return await InvokeFallbackAsync(context, topHint, fallbackModel, ct);
        }
    }

    private async Task<LlmIntentClassificationResult> InvokeFallbackAsync(
        IntentClassificationContext context,
        EmbeddingIntentMatch? topHint,
        string fallbackModel,
        CancellationToken ct)
    {
        try
        {
            return await InvokeAsync(context, topHint, fallbackModel, useStructuredOutput: false, isFallback: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM-first classifier: fallback model also failed; returning RAG abstain.");
            return new LlmIntentClassificationResult(
                ChatExecutionMode.Rag,
                ChatIntentFamily.Unknown,
                ChatReportingTask.Unknown,
                ChatScopeConfidence.Ambiguous,
                "llm-fallback-rag",
                Confidence: 0.0,
                ModelInvoked: fallbackModel,
                LatencyMs: 0,
                IsFallback: true);
        }
    }

    private async Task<LlmIntentClassificationResult> InvokeAsync(
        IntentClassificationContext context,
        EmbeddingIntentMatch? topHint,
        string model,
        bool useStructuredOutput,
        bool isFallback,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var systemPrompt = BuildSystemPrompt(context.Today);
        var userMessage = BuildUserMessage(context.Query, topHint);

        var request = new LlmChatRequest(
            System: systemPrompt,
            Messages: [new LlmMessage("user", userMessage)],
            Model: model,
            Temperature: 0,
            MaxTokens: 350,
            ResponseFormat: useStructuredOutput ? LlmResponseFormat.ForJsonObject() : null);

        var result = await _llm.ChatAsync(request, ct);
        sw.Stop();

        var parsed = ParseClassification(result.Content);
        return new LlmIntentClassificationResult(
            parsed.Mode,
            parsed.Family,
            parsed.ReportingTask,
            parsed.ScopeConfidence,
            parsed.Reason,
            parsed.Confidence,
            ModelInvoked: model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            IsFallback: isFallback);
    }

    private static bool IsSchemaFailure(Exception ex) =>
        ex is LlmProviderException { IsSchemaFailure: true } ||
        ex is JsonException ||
        ex.Message.Contains("json_validate_failed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned invalid", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned an empty", StringComparison.OrdinalIgnoreCase);

    private static string BuildSystemPrompt(DateOnly today) =>
        $$"""
        You are FinFlow's enterprise intent planner for a multi-tenant finance workspace chatbot.
        Classify the user's latest message for routing only. Do not answer the user.

        Today is {{today:yyyy-MM-dd}}.

        Routing contract:
        - Reporting: aggregate, ranking, comparison, budget, approval queue, trend, vendor summaries
          computed FROM STRUCTURED workspace data (not from chunks).
        - Rag: any question that needs document content — receipt/invoice details, vendor-specific
          lookups, evidence retrieval, status of a referenced entity.
        - General: greetings, smalltalk, productivity rewriting, unsupported programming or sensitive
          advice requests.
        - Greeting: pure greeting only.

        Reporting tasks:
          Summary, Trend, VendorRanking, EmployeeRanking, BudgetUtilization, ApprovalQueue,
          Comparison, EntityStatusLookup, Unknown.

        Rules:
        - Output JSON ONLY matching the schema. No prose, no markdown.
        - DEFAULT to Rag when uncertain. Never default to Reporting.
        - Use scopeConfidence to flag ambiguity; do NOT infer scope from role (caller does that).
        - A query that names a SPECIFIC vendor / merchant / receipt / invoice is Rag, even if it
          also contains aggregate-sounding words like "bao nhieu" or "total".
        - Confidence is your self-assessed certainty in [0, 1]. Below 0.55 is "low".
        - Vietnamese, English, code-mixed, typos, no-diacritics — handle them all.
        - reason ≤ 160 chars, in the language of the query.

        JSON shape (strict):
        {
          "executionMode": "Rag" | "Reporting" | "General" | "Greeting",
          "intentFamily": "Greeting"|"SmallTalk"|"Productivity"|"LowSignal"|"Programming"|"SensitiveAdvice"|"PromptBoundary"|"OwnSummary"|"OwnDetail"|"ApprovalQueue"|"Aggregate"|"Comparison"|"Ranking"|"DocumentLookup"|"DestructiveCommand"|"DestructiveAction"|"Unknown",
          "reportingTask": "Unknown"|"Summary"|"Trend"|"VendorRanking"|"EmployeeRanking"|"BudgetUtilization"|"ApprovalQueue"|"Comparison"|"EntityStatusLookup",
          "scopeConfidence": "Explicit"|"SafeInferred"|"Ambiguous"|"Forbidden",
          "reason": string,
          "confidence": number
        }
        """;

    private static string BuildUserMessage(string query, EmbeddingIntentMatch? topHint)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["embeddingHint"] = topHint is null ? null : new
            {
                mode = topHint.Mode.ToString(),
                family = topHint.Family.ToString(),
                task = topHint.ReportingTask.ToString(),
                similarity = Math.Round(topHint.CosineSimilarity, 4),
                exemplar = topHint.ExemplarText
            },
            ["embeddingHintNotice"] = "embeddingHint is advisory; override if query semantics disagree."
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static LlmIntentClassificationParsed ParseClassification(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(content));
        var root = doc.RootElement;

        var mode = ParseRequiredEnum<ChatExecutionMode>(root, "executionMode");
        var family = ParseRequiredEnum<ChatIntentFamily>(root, "intentFamily");
        var task = ParseOptionalEnum(root, "reportingTask", ChatReportingTask.Unknown);
        var scope = ParseRequiredEnum<ChatScopeConfidence>(root, "scopeConfidence");
        var reason = root.GetProperty("reason").GetString();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Intent planner returned an empty reason.");

        var confidence = root.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number
            ? Math.Clamp(conf.GetDouble(), 0.0, 1.0)
            : 0.5;

        return new LlmIntentClassificationParsed(mode, family, task, scope, reason.Trim(), confidence);
    }

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private static T ParseRequiredEnum<T>(JsonElement root, string property) where T : struct, Enum
    {
        var value = root.GetProperty(property).GetString();
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"Intent planner returned invalid {property}: {value}");
        return parsed;
    }

    private static T ParseOptionalEnum<T>(JsonElement root, string property, T fallback) where T : struct, Enum
    {
        if (!root.TryGetProperty(property, out var element))
            return fallback;
        var value = element.GetString();
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private readonly record struct LlmIntentClassificationParsed(
        ChatExecutionMode Mode,
        ChatIntentFamily Family,
        ChatReportingTask ReportingTask,
        ChatScopeConfidence ScopeConfidence,
        string Reason,
        double Confidence);
}

public sealed class LlmIntentClassifierOptions
{
    public const string SectionName = "Chat:LlmIntentClassifier";
    public string? PrimaryModel { get; set; }
    public string? FallbackModel { get; set; }
}
