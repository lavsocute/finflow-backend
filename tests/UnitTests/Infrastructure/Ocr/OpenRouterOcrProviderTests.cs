using System.Net;
using System.Net.Http;
using System.Text;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.OpenRouter;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class OpenRouterOcrProviderTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsParsedResult_ForImageUpload()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "```json\n{\"vendorName\":\"OpenRouter Vendor\",\"reference\":\"INV-2026-1188\",\"documentDate\":\"2026-04-20\",\"dueDate\":\"2026-05-05\",\"category\":\"Marketing\",\"vendorTaxId\":\"TX-789\",\"subtotal\":900.00,\"vat\":90.00,\"totalAmount\":990.00,\"lineItems\":[{\"itemName\":\"Campaign Creative\",\"quantity\":1,\"unitPrice\":900.00,\"total\":900.00},{\"itemName\":\"VAT\",\"quantity\":1,\"unitPrice\":90.00,\"total\":90.00}]}\n```"
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.test/api/v1/") };
        var provider = new OpenRouterOcrProvider(
            client,
            new StubPdfPageRenderer(Result.Success<IReadOnlyList<OcrPageImage>>([])),
            Options.Create(new OpenRouterProviderOptions { Model = "google/gemma-4-31b-it:free" }));

        var result = await provider.ExtractAsync("receipt.jpg", "image/jpeg", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("OpenRouter Vendor", result.Value.VendorName);
        Assert.Equal("INV-2026-1188", result.Value.Reference);
        Assert.Equal("openrouter", result.Value.Source);
        Assert.Equal("AI extracted", result.Value.ConfidenceLabel);
        Assert.Equal(2, result.Value.LineItems.Count);
        Assert.NotNull(handler.LastRequest);
        Assert.EndsWith("chat/completions", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);

        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"model\":\"google/gemma-4-31b-it:free\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("data:image/jpeg;base64,AQID", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsProviderUnavailable_WhenHttpFails()
    {
        var client = new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("network down")))
        {
            BaseAddress = new Uri("https://openrouter.test/api/v1/")
        };
        var provider = new OpenRouterOcrProvider(
            client,
            new StubPdfPageRenderer(Result.Success<IReadOnlyList<OcrPageImage>>([])),
            Options.Create(new OpenRouterProviderOptions()));

        var result = await provider.ExtractAsync("receipt.jpg", "image/jpeg", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrProviderUnavailable, result.Error);
    }

    private sealed class StubPdfPageRenderer : IPdfPageRenderer
    {
        private readonly Result<IReadOnlyList<OcrPageImage>> _result;

        public StubPdfPageRenderer(Result<IReadOnlyList<OcrPageImage>> result) => _result = result;

        public Task<Result<IReadOnlyList<OcrPageImage>>> RenderAsync(byte[] pdfBytes, CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
            _responseFactory = responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responseFactory(request));
        }
    }

}
