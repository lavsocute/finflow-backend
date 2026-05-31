using System.Diagnostics;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// Phase-1 wrapper around the active <see cref="IChatIntentPlanner"/> (legacy) and the
/// new <see cref="HybridCascadeIntentClassifier"/>. Return value remains the legacy result;
/// the cascade is invoked in fire-and-forget shadow mode for telemetry only.
///
/// Toggle via <see cref="CascadeOptions.ShadowEnabled"/>. When false this is transparent.
/// When <see cref="CascadeOptions.Enabled"/> is true the cascade result is returned instead
/// of the legacy result (Phase 2 cutover).
/// </summary>
public sealed class ShadowModeIntentPlanner : IChatIntentPlanner
{
    private readonly IChatIntentPlanner _legacy;
    private readonly HybridCascadeIntentClassifier _cascade;
    private readonly ITextNormalizer _normalizer;
    private readonly IIntentClassificationTelemetry _telemetry;
    private readonly CascadeOptions _options;
    private readonly ILogger<ShadowModeIntentPlanner> _logger;

    public ShadowModeIntentPlanner(
        IChatIntentPlanner legacy,
        HybridCascadeIntentClassifier cascade,
        ITextNormalizer normalizer,
        IIntentClassificationTelemetry telemetry,
        IOptions<CascadeOptions>? options = null,
        ILogger<ShadowModeIntentPlanner>? logger = null)
    {
        _legacy = legacy;
        _cascade = cascade;
        _normalizer = normalizer;
        _telemetry = telemetry;
        _options = options?.Value ?? new CascadeOptions();
        _logger = logger ?? NullLogger<ShadowModeIntentPlanner>.Instance;
    }

    public async Task<ChatIntentClassification> ClassifyAsync(
        ChatIntentPlanningRequest request,
        CancellationToken ct = default)
    {
        var legacySw = Stopwatch.StartNew();
        var legacyResult = await _legacy.ClassifyAsync(request, ct);
        legacySw.Stop();

        // Skip cascade entirely if neither shadow nor cutover is enabled.
        if (!_options.ShadowEnabled && !_options.Enabled)
        {
            _telemetry.RecordLegacyOnly(request, legacyResult, (int)legacySw.ElapsedMilliseconds);
            return legacyResult;
        }

        IntentClassificationResult? cascadeResult = null;
        try
        {
            var ctx = new IntentClassificationContext(
                Query: request.Query,
                NormalizedQuery: _normalizer.Normalize(request.Query),
                Today: request.Today);
            cascadeResult = await _cascade.ClassifyAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cascade classifier threw in shadow mode; legacy result preserved.");
        }

        if (cascadeResult is not null)
        {
            _telemetry.RecordComparison(
                request,
                legacy: legacyResult,
                cascade: cascadeResult,
                legacyLatencyMs: (int)legacySw.ElapsedMilliseconds);
        }
        else
        {
            _telemetry.RecordLegacyOnly(request, legacyResult, (int)legacySw.ElapsedMilliseconds);
        }

        // Phase 2 cutover: return cascade if enabled and produced a value.
        if (_options.Enabled && cascadeResult is not null)
            return cascadeResult.ToClassification();

        return legacyResult;
    }
}
