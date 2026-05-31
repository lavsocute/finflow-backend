using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Chat;

/// <summary>
/// One-shot startup hook: when the cascade is enabled (or shadow-mode is on),
/// hydrate <see cref="IntentExemplarRegistry"/> from DB. If the table is empty for
/// the configured embedding model, seed it from the embedded JSON resource.
/// Failures are logged but do NOT fail app startup.
/// </summary>
public sealed class IntentExemplarStartupJob : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IOptions<CascadeOptions> _options;
    private readonly ILogger<IntentExemplarStartupJob> _logger;

    public IntentExemplarStartupJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<CascadeOptions> options,
        ILogger<IntentExemplarStartupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled && !opts.ShadowEnabled)
        {
            _logger.LogInformation("Cascade disabled and shadow-mode disabled; skipping intent exemplar sync.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IntentExemplarSyncService>();
            var modelId = ResolveEmbeddingModelId();
            _logger.LogInformation("Intent exemplar sync starting for embedding model id {ModelId}.", modelId);
            await sync.SyncAsync(modelId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intent exemplar startup sync failed; cascade Stage 1 may rank zero matches until next sync.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Builds a stable embedding-space identifier from configuration so that switching
    /// providers/models/dimensions forces a re-seed (exemplar vectors are only valid
    /// within the space that produced them). The DI wrapper type name is NOT used because
    /// both local and neural embedders are wrapped in CachingEmbeddingService.
    /// </summary>
    private string ResolveEmbeddingModelId()
    {
        var useLocal = _configuration.GetValue<bool>("Embedding:UseLocal");
        var dimensions = _configuration.GetValue<int?>("Embedding:OpenRouter:ExpectedDimensions") ?? 2048;

        if (useLocal)
            return $"local-hashing:{dimensions}";

        var model = _configuration.GetValue<string>("Embedding:OpenRouter:Model") ?? "openrouter-default";
        return $"openrouter:{model}:{dimensions}";
    }
}
