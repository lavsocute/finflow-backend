using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class ConfigurableOcrExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_UsesActiveProviderFromConfig()
    {
        var activeProvider = new StubOcrProvider(
            "Groq",
            Result.Success(CreateOcrResult("groq-source")));
        var secondaryProvider = new StubOcrProvider(
            "OpenRouter",
            Result.Success(CreateOcrResult("openrouter-source")));
        var service = new ConfigurableOcrExtractionService(
            [activeProvider, secondaryProvider],
            Options.Create(new OcrOptions { ActiveProvider = "Groq" }));

        var result = await service.ExtractAsync("invoice.pdf", "application/pdf", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("groq-source", result.Value.Source);
        Assert.True(activeProvider.WasCalled);
        Assert.False(secondaryProvider.WasCalled);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFailureWhenActiveProviderMissing()
    {
        var service = new ConfigurableOcrExtractionService(
            [new StubOcrProvider("OpenRouter", Result.Success(CreateOcrResult("openrouter-source")))],
            Options.Create(new OcrOptions { ActiveProvider = "Groq" }));

        var result = await service.ExtractAsync("invoice.pdf", "application/pdf", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrProviderUnavailable, result.Error);
    }

    [Fact]
    public async Task ExtractAsync_PassesThroughProviderFailure()
    {
        var providerError = DocumentOcrErrors.OcrExtractionFailed;
        var provider = new StubOcrProvider("Groq", Result.Failure<OcrExtractionResult>(providerError));
        var service = new ConfigurableOcrExtractionService(
            [provider],
            Options.Create(new OcrOptions { ActiveProvider = "Groq" }));

        var result = await service.ExtractAsync("invoice.pdf", "application/pdf", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(providerError, result.Error);
        Assert.True(provider.WasCalled);
    }

    private static OcrExtractionResult CreateOcrResult(string source) =>
        new(
            "Acme Cloud Ltd.",
            "INV-2026-0042",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
            "Software & SaaS",
            "TX-123",
            1200.00m,
            240.00m,
            1440.00m,
            source,
            "High precision",
            [
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 1200.00m, 1200.00m),
                new OcrExtractionLineItem("Tax Adjustment", 1m, 240.00m, 240.00m)
            ]);

    private sealed class StubOcrProvider : IOcrProvider
    {
        private readonly Result<OcrExtractionResult> _result;

        public StubOcrProvider(string name, Result<OcrExtractionResult> result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }
        public bool WasCalled { get; private set; }

        public Task<Result<OcrExtractionResult>> ExtractAsync(
            string fileName,
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }
}
