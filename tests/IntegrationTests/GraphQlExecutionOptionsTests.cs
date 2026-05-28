using FinFlow.Infrastructure.Ocr;
using HotChocolate.Execution;
using HotChocolate.Execution.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlExecutionOptionsTests
{
    [Fact]
    public async Task GraphQlExecutionTimeout_Exceeds_OcrProviderTimeout()
    {
        await using var factory = new GraphQlApiTestFactory();
        var executor = await factory.Services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
        var options = executor.Services.GetRequiredService<IRequestTimeoutOptionsAccessor>();
        var ocrTimeoutSeconds = factory.Services.GetRequiredService<IOptions<OcrOptions>>()
            .Value.ProviderTimeoutSeconds;

        Assert.True(
            options.ExecutionTimeout > TimeSpan.FromSeconds(ocrTimeoutSeconds),
            $"GraphQL execution timeout {options.ExecutionTimeout} must exceed OCR timeout {ocrTimeoutSeconds}s.");
    }
}
