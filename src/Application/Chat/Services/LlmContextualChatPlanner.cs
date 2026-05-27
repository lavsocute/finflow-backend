using System.Text.Json;
using System.Text.Json.Serialization;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Services;

public sealed class LlmContextualChatPlanner : IContextualChatPlanner
{
    private readonly ILlmChatService _llm;
    private readonly ILogger<LlmContextualChatPlanner> _logger;
    private readonly GroqChatOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LlmContextualChatPlanner(
        ILlmChatService llm,
        ILogger<LlmContextualChatPlanner> logger,
        IOptions<GroqChatOptions>? options = null)
    {
        _llm = llm;
        _logger = logger;
        _options = options?.Value ?? new GroqChatOptions();
    }

    public async Task<ContextualChatPlan?> PlanAsync(
        ContextualChatPlanRequest request,
        CancellationToken ct = default)
    {
        if (request.LastTurn is null || request.History.Count == 0)
            return null;

        try
        {
            return await PlanWithLlmAsync(request, useStructuredOutput: true, ct);
        }
        catch (Exception ex) when (IsStructuredOutputFailure(ex))
        {
            _logger.LogWarning(ex, "Structured contextual chat planning failed; retrying without provider response_format.");
            try
            {
                return await PlanWithLlmAsync(request, useStructuredOutput: false, ct);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Prompt-only contextual chat planning failed; falling back to initial routing.");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Contextual chat planning failed; falling back to initial routing.");
            return null;
        }
    }

    private async Task<ContextualChatPlan?> PlanWithLlmAsync(
        ContextualChatPlanRequest request,
        bool useStructuredOutput,
        CancellationToken ct)
    {
        var result = await _llm.ChatAsync(
            new LlmChatRequest(
                System: BuildSystemPrompt(request.Today),
                Messages:
                [
                    new LlmMessage("user", BuildUserMessage(request))
                ],
                Model: _options.IntentPlannerModel,
                Temperature: 0,
                MaxTokens: 500,
                ResponseFormat: useStructuredOutput
                    ? LlmResponseFormat.ForJsonObject()
                    : null),
            ct);

        if (string.IsNullOrWhiteSpace(result.Content))
            return null;

        return ParsePlan(result.Content, request);
    }

    private static bool IsStructuredOutputFailure(Exception ex) =>
        ex is LlmProviderException { IsSchemaFailure: true } ||
        ex is JsonException ||
        ex.Message.Contains("json_validate_failed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned invalid", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("returned an empty", StringComparison.OrdinalIgnoreCase);

    private static string BuildSystemPrompt(DateOnly today) =>
        $$"""
        You are FinFlow's contextual routing planner.
        Your task is to turn the current user message plus the previous structured turn into a routing plan.
        Do not answer the user. Output JSON only.

        Today is {{today:yyyy-MM-dd}}.

        Allowed executionMode values: Greeting, General, Reporting, Rag.
        Allowed intentFamily values: Greeting, SmallTalk, Productivity, LowSignal, Programming, SensitiveAdvice, OwnSummary, OwnDetail, ApprovalQueue, Aggregate, Comparison, Ranking, DocumentLookup, DestructiveCommand, DestructiveAction, Unknown.
        Allowed scopeConfidence values: Explicit, SafeInferred, Ambiguous, Forbidden.
        Allowed reportingTask values: Unknown, Summary, Trend, VendorRanking, EmployeeRanking, BudgetUtilization, ApprovalQueue, Comparison, EntityStatusLookup.

        Rules:
        - If the current message is a continuation of the previous business/reporting question, preserve the previous reporting intent unless the new message clearly changes it.
        - If the current message changes only the time period, keep the previous metric and scope, and set reportingFrom/reportingTo to the new period.
        - If the current message asks whether a previously listed document/expense was approved or asks for its status, prefer executionMode=Rag, intentFamily=DocumentLookup, reportingTask=EntityStatusLookup.
        - If the current message asks "compared with the first/previous one", use reportingTask=Comparison and preserve the scope from the referenced turn in effectiveQuery.
        - If the current message is unrelated, return apply=false.
        - Never infer access beyond the previous scope.

        Response contract:
        - apply=true only when the current message is a real continuation of the previous turn.
        - effectiveQuery must be standalone for downstream routing.
        - reportingTask must name the concrete reporting or lookup task.
        - reportingFrom/reportingTo must be yyyy-MM-dd when the user changes or preserves a reporting period; otherwise null.
        - intentReason must be compact and under 160 characters.
        """;

    private static object BuildJsonSchema() => new
    {
        type = "object",
        properties = new
        {
            apply = new { type = "boolean" },
            effectiveQuery = new { type = "string", minLength = 1 },
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
            intentReason = new { type = "string", minLength = 1 },
            scopeConfidence = new
            {
                type = "string",
                @enum = Enum.GetNames<ChatScopeConfidence>()
            },
            reportingFrom = new
            {
                type = new[] { "string", "null" },
                pattern = "^\\d{4}-\\d{2}-\\d{2}$"
            },
            reportingTo = new
            {
                type = new[] { "string", "null" },
                pattern = "^\\d{4}-\\d{2}-\\d{2}$"
            }
        },
        required = new[]
        {
            "apply",
            "effectiveQuery",
            "executionMode",
            "intentFamily",
            "reportingTask",
            "intentReason",
            "scopeConfidence",
            "reportingFrom",
            "reportingTo"
        },
        additionalProperties = false
    };

    private static string BuildUserMessage(ContextualChatPlanRequest request)
    {
        var history = string.Join(
            "\n",
            request.History
                .TakeLast(6)
                .Select(message => $"{message.Role}: {Truncate(message.Content, 240)}"));

        var lastTurn = request.LastTurn;
        var payload = new
        {
            currentQuery = request.Query,
            currentEffectiveQuery = request.EffectiveQuery,
            initialIntent = new
            {
                mode = request.InitialIntent.Mode.ToString(),
                family = request.InitialIntent.Family.ToString(),
                reportingTask = request.InitialIntent.ReportingTask.ToString(),
                reason = request.InitialIntent.Reason,
                scopeConfidence = request.InitialIntent.ScopeConfidence.ToString()
            },
            previousTurn = lastTurn is null ? null : new
            {
                lastTurn.OriginalQuery,
                lastTurn.EffectiveQuery,
                lastTurn.ExecutionMode,
                lastTurn.IntentFamily,
                lastTurn.ReportingTask,
                lastTurn.IntentReason,
                lastTurn.ScopeConfidence,
                lastTurn.AnswerSource,
                reportingFrom = lastTurn.PeriodFrom?.ToString("yyyy-MM-dd"),
                reportingTo = lastTurn.PeriodTo?.ToString("yyyy-MM-dd")
            },
            recentHistory = history
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static ContextualChatPlan? ParsePlan(string content, ContextualChatPlanRequest request)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(content));
        var root = doc.RootElement;

        if (!root.TryGetProperty("apply", out var applyElement) || !applyElement.GetBoolean())
            return null;

        var mode = ParseEnum(root, "executionMode", request.InitialIntent.Mode);
        var family = ParseEnum(root, "intentFamily", request.InitialIntent.Family);
        var reportingTask = ParseEnum(root, "reportingTask", request.InitialIntent.ReportingTask);
        var scopeConfidence = ParseEnum(root, "scopeConfidence", request.InitialIntent.ScopeConfidence);
        var reason = GetString(root, "intentReason") ?? request.InitialIntent.Reason;
        var effectiveQuery = GetString(root, "effectiveQuery") ?? request.EffectiveQuery;

        return new ContextualChatPlan(
            effectiveQuery,
            new ChatIntentClassification(mode, reason, family, scopeConfidence, reportingTask),
            ParseDate(root, "reportingFrom"),
            ParseDate(root, "reportingTo"));
    }

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start
            ? content[start..(end + 1)]
            : content;
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static TEnum ParseEnum<TEnum>(JsonElement root, string propertyName, TEnum fallback)
        where TEnum : struct, Enum
    {
        var value = GetString(root, propertyName);
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private static DateOnly? ParseDate(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return DateOnly.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
