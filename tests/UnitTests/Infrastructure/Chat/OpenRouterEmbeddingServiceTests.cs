using System.Net;
using System.Net.Http;
using System.Text;
using FinFlow.Infrastructure.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public sealed class OpenRouterEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_PostsToApiV1Embeddings_WhenBaseUrlHasNoTrailingSlash()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://openrouter.test/api/v1/embeddings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": [
                            {
                              "embedding": [0.1, 0.2, 0.3]
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>wrong route</html>", Encoding.UTF8, "text/html")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.test/api/v1") };
        var service = new OpenRouterEmbeddingService(
            client,
            Options.Create(new OpenRouterEmbeddingOptions
            {
                BaseUrl = "https://openrouter.test/api/v1",
                Model = "test-embedding-model",
                ExpectedDimensions = 3
            }),
            NullLogger<OpenRouterEmbeddingService>.Instance);

        var embedding = await service.EmbedAsync("hello", CancellationToken.None);

        Assert.Equal("https://openrouter.test/api/v1/embeddings", handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal([0.1f, 0.2f, 0.3f], embedding);
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
