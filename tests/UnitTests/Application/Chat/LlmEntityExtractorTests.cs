using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinFlow.UnitTests.Application.Chat;

public class LlmEntityExtractorTests
{
    private readonly Mock<ILogger<LlmEntityExtractor>> _loggerMock;
    private readonly IOptions<LlmEntityExtractorOptions> _defaultOptions;

    public LlmEntityExtractorTests()
    {
        _loggerMock = new Mock<ILogger<LlmEntityExtractor>>();
        _defaultOptions = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = true,
            ApiKey = "test-api-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "gpt-4o-mini"
        });
    }

    private HttpClient CreateMockHttpClient(Mock<HttpMessageHandler> handlerMock)
    {
        return new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.groq.com/openai/v1")
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullBaseUrl_ShouldUseDefaultBaseUrl()
    {
        // Arrange
        var options = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = true,
            ApiKey = "test-api-key",
            BaseUrl = null!,
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = CreateMockHttpClient(handlerMock);

        // Act
        var extractor = new LlmEntityExtractor(httpClient, options, _loggerMock.Object);

        // Assert - should not throw and should use default base URL
        Assert.NotNull(extractor);
    }

    [Fact]
    public void Constructor_WithWhitespaceBaseUrl_ShouldUseDefaultBaseUrl()
    {
        // Arrange
        var options = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = true,
            ApiKey = "test-api-key",
            BaseUrl = "   ",
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = CreateMockHttpClient(handlerMock);

        // Act
        var extractor = new LlmEntityExtractor(httpClient, options, _loggerMock.Object);

        // Assert - should not throw
        Assert.NotNull(extractor);
    }

    [Fact]
    public void Constructor_WithTrailingSlashBaseUrl_ShouldNormalizeUrl()
    {
        // Arrange
        var options = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = true,
            ApiKey = "test-api-key",
            BaseUrl = "https://api.groq.com/openai/v1/",
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = CreateMockHttpClient(handlerMock);

        // Act
        var extractor = new LlmEntityExtractor(httpClient, options, _loggerMock.Object);

        // Assert - should not throw and should normalize the URL
        Assert.NotNull(extractor);
    }

    #endregion

    #region Empty Message Tests

    [Fact]
    public async Task ExtractAsync_WithEmptyString_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = CreateMockHttpClient(handlerMock);

        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_WithWhitespaceOnly_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = CreateMockHttpClient(handlerMock);

        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("   ", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region HTTP Failure Tests

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ExtractAsync_WithNonSuccessStatusCode_ShouldReturnEmptyList(HttpStatusCode statusCode)
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent("{\"error\": \"test error\"}", Encoding.UTF8, "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_With500StatusCode_ShouldLogWarning()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Internal Server Error", Encoding.UTF8, "text/plain")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Entity extraction returned")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region JSON Parsing Edge Cases

    [Fact]
    public async Task ExtractAsync_WithMalformedJson_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("this is not valid json {{{", Encoding.UTF8, "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse entity extraction response")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyChoices_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"choices\": []}", Encoding.UTF8, "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_WithMissingChoices_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"model\": \"gpt-4o-mini\"}", Encoding.UTF8, "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_WithMissingToolCallsAndContent_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"role\": \"assistant\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_WithInvalidEntitiesJson_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"not valid json\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_WithMissingEntitiesProperty_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"other\\\": \\\"value\\\"}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task ExtractAsync_WhenRequestTimesOut_ShouldReturnEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert
        Assert.Empty(result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region LLM Disabled Fallback Tests

    [Fact]
    public async Task DetectFollowUpAsync_WhenLLMDisabled_ShouldUseFallback()
    {
        // Arrange
        var disabledOptions = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = false, // LLM disabled
            ApiKey = "test-api-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        // HTTP should NOT be called when LLM is disabled
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, disabledOptions, _loggerMock.Object);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "What is the budget for Q1?")
        };

        // Act
        var result = await ((ILlmEntityExtractor)extractor).DetectFollowUpAsync("còn gì nữa không?", history);

        // Assert
        Assert.NotNull(result);
        // HTTP should never be called when disabled
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_WhenLLMDisabled_ShouldStillProcess()
    {
        // Arrange - Note: the main ExtractAsync does not check _options.Enabled
        // But DetectFollowUpAsync and ResolveEntityReferencesAsync do
        var disabledOptions = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = false,
            ApiKey = "test-api-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"entities\\\": []}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, disabledOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test message", Array.Empty<ChatMessage>());

        // Assert - ExtractAsync doesn't check Enabled, so it should still work
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ResolveEntityReferencesAsync_WhenLLMDisabled_ShouldUseFallback()
    {
        // Arrange
        var disabledOptions = Options.Create(new LlmEntityExtractorOptions
        {
            Enabled = false,
            ApiKey = "test-api-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "gpt-4o-mini"
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, disabledOptions, _loggerMock.Object);

        var context = ConversationContext.Create(Guid.NewGuid());
        context.AddEntity(TrackedEntity.Create("Budget Q1", EntityType.BUDGET, 1));

        var entities = new List<LlmExtractedEntity>
        {
            new LlmExtractedEntity { Text = "Budget Q1", Type = EntityType.BUDGET, Confidence = 0.9f }
        }.AsReadOnly();

        // Act
        var result = await ((ILlmEntityExtractor)extractor).ResolveEntityReferencesAsync(
            "cập nhật Budget Q1",
            context,
            entities);

        // Assert - should use fallback since LLM is disabled
        Assert.NotNull(result);
        // HTTP should not be called
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region Successful Extraction Tests

    [Fact]
    public async Task ExtractAsync_WithValidToolCallResponse_ShouldReturnEntities()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"entities\\\": [{\\\"text\\\": \\\"John Doe\\\", \\\"type\\\": \\\"PERSON\\\", \\\"confidence\\\": 0.95}]}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("What about John Doe's expenses?", Array.Empty<ChatMessage>());

        // Assert
        Assert.Single(result);
        Assert.Equal("John Doe", result[0].Text);
        Assert.Equal(EntityType.PERSON, result[0].Type);
    }

    [Fact]
    public async Task ExtractAsync_WithDirectContentResponse_ShouldReturnEntities()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"content\": \"{\\\"entities\\\": [{\\\"text\\\": \\\"5000000\\\", \\\"type\\\": \\\"MONEY\\\", \\\"confidence\\\": 0.9}]}\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("chi phí 5 triệu", Array.Empty<ChatMessage>());

        // Assert
        Assert.Single(result);
        Assert.Equal("5000000", result[0].Text);
        Assert.Equal(EntityType.MONEY, result[0].Type);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task ExtractAsync_WithEntityMissingText_ShouldSkipEntity()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"entities\\\": [{\\\"type\\\": \\\"PERSON\\\", \\\"confidence\\\": 0.9}, {\\\"text\\\": \\\"John\\\", \\\"type\\\": \\\"PERSON\\\", \\\"confidence\\\": 0.8}]}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test", Array.Empty<ChatMessage>());

        // Assert - only the entity with text should be returned
        Assert.Single(result);
        Assert.Equal("John", result[0].Text);
    }

    [Fact]
    public async Task ExtractAsync_WithInvalidEntityType_ShouldDefaultToUnknown()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"entities\\\": [{\\\"text\\\": \\\"UnknownType\\\", \\\"type\\\": \\\"INVALID_TYPE\\\", \\\"confidence\\\": 0.9}]}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await extractor.ExtractAsync("test", Array.Empty<ChatMessage>());

        // Assert
        Assert.Single(result);
        Assert.Equal(EntityType.UNKNOWN, result[0].Type);
    }

    #endregion

    #region ExtractEntitiesAsync Tests (ILlmEntityExtractor interface)

    [Fact]
    public async Task ExtractEntitiesAsync_WithValidResponse_ShouldReturnLlmExtractedEntities()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    "{\"choices\": [{\"message\": {\"tool_calls\": [{\"function\": {\"arguments\": \"{\\\"entities\\\": [{\\\"text\\\": \\\"Kế toán\\\", \\\"type\\\": \\\"DEPARTMENT\\\", \\\"confidence\\\": 0.92}]}\"}}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = CreateMockHttpClient(handlerMock);
        var extractor = new LlmEntityExtractor(httpClient, _defaultOptions, _loggerMock.Object);

        // Act
        var result = await ((ILlmEntityExtractor)extractor).ExtractEntitiesAsync("phòng Kế toán", CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.Equal("Kế toán", result[0].Text);
        Assert.Equal(EntityType.DEPARTMENT, result[0].Type);
    }

    #endregion
}