using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Ocr;

public sealed class ConfigurableOcrExtractionService : IOcrExtractionService
{
    private readonly IReadOnlyDictionary<string, IOcrProvider> _providers;
    private readonly OcrOptions _options;

    public ConfigurableOcrExtractionService(
        IEnumerable<IOcrProvider> providers,
        IOptions<OcrOptions> options)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
    }

    public Task<Result<OcrExtractionResult>> ExtractAsync(
        string fileName,
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(_options.ActiveProvider, out var provider))
            return Task.FromResult(Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrProviderUnavailable));

        return provider.ExtractAsync(fileName, contentType, fileContents, cancellationToken);
    }

    public Task<Result<int>> GetPageCountAsync(
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(_options.ActiveProvider, out var provider))
            return Task.FromResult(Result.Failure<int>(DocumentOcrErrors.OcrProviderUnavailable));

        return provider.GetPageCountAsync(contentType, fileContents, cancellationToken);
    }
}
