using FinFlow.Infrastructure.Middleware;
using FinFlow.Infrastructure.Ocr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Infrastructure.Middleware;

public sealed class RequestTimeoutMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UsesConfiguredTimeout_ForLongRunningRequest()
    {
        var context = new DefaultHttpContext();
        var completedWithoutCancellation = false;
        var middleware = new RequestTimeoutMiddleware(
            async httpContext =>
            {
                await Task.Delay(60, httpContext.RequestAborted);
                completedWithoutCancellation = true;
            },
            NullLogger<RequestTimeoutMiddleware>.Instance,
            Options.Create(new RequestTimeoutOptions { DefaultTimeoutSeconds = 1 }));

        await middleware.InvokeAsync(context);

        Assert.True(completedWithoutCancellation);
    }

    [Fact]
    public void DefaultRequestTimeout_LeavesTimeToReturnOcrProviderFailure()
    {
        var ocrTimeout = new OcrOptions().ProviderTimeoutSeconds;
        var requestTimeout = new RequestTimeoutOptions().DefaultTimeoutSeconds;

        Assert.True(requestTimeout > ocrTimeout);
    }
}
