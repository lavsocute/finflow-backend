using System.Net;
using System.Net.Http;
using System.Text;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.Groq;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class GroqOcrProviderTests
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
                        "content": "{\"vendorName\":\"Acme Cloud Ltd.\",\"reference\":\"INV-2026-0042\",\"documentDate\":\"2026-04-18\",\"dueDate\":\"2026-05-02\",\"category\":\"Software & SaaS\",\"vendorTaxId\":\"TX-123\",\"subtotal\":1200.00,\"vat\":240.00,\"totalAmount\":1440.00,\"lineItems\":[{\"itemName\":\"Cloud Compute Instance\",\"quantity\":1,\"unitPrice\":1200.00,\"total\":1200.00},{\"itemName\":\"Tax Adjustment\",\"quantity\":1,\"unitPrice\":240.00,\"total\":240.00}]}"
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.test/") };
        var provider = new GroqOcrProvider(
            client,
            new StubPdfPageRenderer(Result.Success<IReadOnlyList<OcrPageImage>>([])),
            Options.Create(new GroqProviderOptions { Model = "test-model" }));

        var result = await provider.ExtractAsync("invoice.png", "image/png", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Acme Cloud Ltd.", result.Value.VendorName);
        Assert.Equal("INV-2026-0042", result.Value.Reference);
        Assert.Equal("groq", result.Value.Source);
        Assert.Equal("AI extracted", result.Value.ConfidenceLabel);
        Assert.Equal(2, result.Value.LineItems.Count);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("chat/completions", handler.LastRequest.RequestUri!.ToString(), StringComparison.Ordinal);

        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"model\":\"test-model\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AQID", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsInvalidJson_WhenModelContentIsInvalid()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "not-json"
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.test/") };
        var provider = new GroqOcrProvider(
            client,
            new StubPdfPageRenderer(Result.Success<IReadOnlyList<OcrPageImage>>([])),
            Options.Create(new GroqProviderOptions()));

        var result = await provider.ExtractAsync("invoice.png", "image/png", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrInvalidJson, result.Error);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsPdfRenderFailure_ForPdfInput()
    {
        var provider = new GroqOcrProvider(
            new HttpClient(new ThrowingHttpMessageHandler()),
            new StubPdfPageRenderer(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed)),
            Options.Create(new GroqProviderOptions()));

        var result = await provider.ExtractAsync("invoice.pdf", "application/pdf", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrPdfRenderFailed, result.Error);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsUnsupportedFile_ForUnsupportedContentType()
    {
        var provider = new GroqOcrProvider(
            new HttpClient(new ThrowingHttpMessageHandler()),
            new StubPdfPageRenderer(Result.Success<IReadOnlyList<OcrPageImage>>([])),
            Options.Create(new GroqProviderOptions()));

        var result = await provider.ExtractAsync("invoice.txt", "text/plain", [1, 2, 3], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrUnsupportedFile, result.Error);
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

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be called in this test.");
    }
}
