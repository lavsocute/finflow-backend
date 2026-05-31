using System.Security.Cryptography;
using System.Text;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinFlow.Application.Chat.Cascade;

public interface IIntentClassificationTelemetry
{
    void RecordLegacyOnly(ChatIntentPlanningRequest request, ChatIntentClassification legacy, int legacyLatencyMs);
    void RecordComparison(ChatIntentPlanningRequest request, ChatIntentClassification legacy, IntentClassificationResult cascade, int legacyLatencyMs);
    void RecordCascadeOnly(ChatIntentPlanningRequest request, IntentClassificationResult cascade);
}

/// <summary>
/// Structured-logging implementation of <see cref="IIntentClassificationTelemetry"/>.
/// Emits one event per classification with classifier-stage breakdown and SHA-256 query hash
/// (raw query never logged). Drift detector and active-learning loops consume these events.
/// </summary>
public sealed class IntentClassificationTelemetry : IIntentClassificationTelemetry
{
    private const int TelemetryVersion = 2;
    private readonly ILogger<IntentClassificationTelemetry> _logger;

    public IntentClassificationTelemetry(ILogger<IntentClassificationTelemetry>? logger = null)
    {
        _logger = logger ?? NullLogger<IntentClassificationTelemetry>.Instance;
    }

    public void RecordLegacyOnly(ChatIntentPlanningRequest request, ChatIntentClassification legacy, int legacyLatencyMs)
    {
        _logger.LogInformation(
            "ChatIntentClassified {@IntentTelemetry}",
            new
            {
                TelemetryVersion,
                Source = "legacy-only",
                QueryHash = HashQuery(request.Query),
                QueryLength = request.Query?.Length ?? 0,
                Today = request.Today.ToString("yyyy-MM-dd"),
                LegacyMode = legacy.Mode.ToString(),
                LegacyFamily = legacy.Family.ToString(),
                LegacyTask = legacy.ReportingTask.ToString(),
                LegacyScopeConfidence = legacy.ScopeConfidence.ToString(),
                LegacyReason = legacy.Reason,
                LegacyLatencyMs = legacyLatencyMs
            });
    }

    public void RecordComparison(
        ChatIntentPlanningRequest request,
        ChatIntentClassification legacy,
        IntentClassificationResult cascade,
        int legacyLatencyMs)
    {
        var disagreement = legacy.Mode != cascade.Mode || legacy.Family != cascade.Family;

        _logger.LogInformation(
            "ChatIntentClassified {@IntentTelemetry}",
            new
            {
                TelemetryVersion,
                Source = "shadow-comparison",
                QueryHash = HashQuery(request.Query),
                QueryLength = request.Query?.Length ?? 0,
                Today = request.Today.ToString("yyyy-MM-dd"),

                LegacyMode = legacy.Mode.ToString(),
                LegacyFamily = legacy.Family.ToString(),
                LegacyTask = legacy.ReportingTask.ToString(),
                LegacyScopeConfidence = legacy.ScopeConfidence.ToString(),
                LegacyReason = legacy.Reason,
                LegacyLatencyMs = legacyLatencyMs,

                CascadeMode = cascade.Mode.ToString(),
                CascadeFamily = cascade.Family.ToString(),
                CascadeTask = cascade.ReportingTask.ToString(),
                CascadeScopeConfidence = cascade.ScopeConfidence.ToString(),
                CascadeReason = cascade.Reason,
                CascadeStage = cascade.ClassifierStage,
                CascadeConfidence = Math.Round(cascade.Confidence, 4),
                CascadeModelInvoked = cascade.ModelInvoked,
                CascadeLatencyMs = cascade.LatencyMs,

                Disagreement = disagreement
            });

        if (disagreement)
        {
            _logger.LogWarning(
                "Cascade vs legacy disagreement {@IntentDisagreement}",
                new
                {
                    QueryHash = HashQuery(request.Query),
                    LegacyMode = legacy.Mode.ToString(),
                    LegacyFamily = legacy.Family.ToString(),
                    CascadeMode = cascade.Mode.ToString(),
                    CascadeFamily = cascade.Family.ToString(),
                    CascadeStage = cascade.ClassifierStage,
                    CascadeConfidence = Math.Round(cascade.Confidence, 4)
                });
        }
    }

    public void RecordCascadeOnly(ChatIntentPlanningRequest request, IntentClassificationResult cascade)
    {
        _logger.LogInformation(
            "ChatIntentClassified {@IntentTelemetry}",
            new
            {
                TelemetryVersion,
                Source = "cascade-only",
                QueryHash = HashQuery(request.Query),
                QueryLength = request.Query?.Length ?? 0,
                Today = request.Today.ToString("yyyy-MM-dd"),
                CascadeMode = cascade.Mode.ToString(),
                CascadeFamily = cascade.Family.ToString(),
                CascadeTask = cascade.ReportingTask.ToString(),
                CascadeScopeConfidence = cascade.ScopeConfidence.ToString(),
                CascadeReason = cascade.Reason,
                CascadeStage = cascade.ClassifierStage,
                CascadeConfidence = Math.Round(cascade.Confidence, 4),
                CascadeModelInvoked = cascade.ModelInvoked,
                CascadeLatencyMs = cascade.LatencyMs
            });
    }

    private static string HashQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(query);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
