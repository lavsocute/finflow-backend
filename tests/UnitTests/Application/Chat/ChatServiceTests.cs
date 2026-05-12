using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

namespace FinFlow.UnitTests.Application.Chat;

public class ChatServiceTests
{
    private readonly Mock<IChatRepository> _chatRepositoryMock;
    private readonly Mock<IChatAuthorizationService> _chatAuthServiceMock;
    private readonly Mock<ISubscriptionFeatureGate> _subscriptionFeatureGateMock;
    private readonly Mock<ITenantUsageService> _tenantUsageServiceMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IRerankService> _rerankServiceMock;
    private readonly Mock<IPromptBuilder> _promptBuilderMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<ChatService>> _loggerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<GroqChatOptions> _options;

    public ChatServiceTests()
    {
        _chatRepositoryMock = new Mock<IChatRepository>();
        _chatAuthServiceMock = new Mock<IChatAuthorizationService>();
        _subscriptionFeatureGateMock = new Mock<ISubscriptionFeatureGate>();
        _tenantUsageServiceMock = new Mock<ITenantUsageService>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _rerankServiceMock = new Mock<IRerankService>();
        _promptBuilderMock = new Mock<IPromptBuilder>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<ChatService>>();

        _options = Options.Create(new GroqChatOptions
        {
            ApiKey = "test-api-key",
            BaseUrl = "https://openrouter.ai/api/v1",
            ChatModel = "llama-3.3-70b-versatile"
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
                    "{\"choices\":[{\"message\":{\"content\":\"Test response from LLM\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        _httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1")
        };
    }

    private ChatService CreateService() => new ChatService(
        _chatRepositoryMock.Object,
        _chatAuthServiceMock.Object,
        _subscriptionFeatureGateMock.Object,
        _tenantUsageServiceMock.Object,
        _embeddingServiceMock.Object,
        _vectorStoreMock.Object,
        _rerankServiceMock.Object,
        _promptBuilderMock.Object,
        _currentTenantMock.Object,
        _unitOfWorkMock.Object,
        _httpClient,
        _options,
        _loggerMock.Object);

    private void SetupHappyPath(
        Guid tenantId,
        Guid membershipId,
        ChatAccessScope accessScope,
        IReadOnlyList<DocumentChunk>? searchChunks = null,
        IReadOnlyList<(DocumentChunk Chunk, float Score)>? rerankedResults = null)
    {
        _subscriptionFeatureGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

        _vectorStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                tenantId,
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<DocumentChunkType>>(),
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchChunks ?? []);

        _rerankServiceMock
            .Setup(x => x.RerankAsync(It.IsAny<string>(), It.IsAny<IEnumerable<DocumentChunk>>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults ?? []);

        _promptBuilderMock
            .Setup(x => x.BuildFullPrompt(It.IsAny<string>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<ChatAccessScope>(), It.IsAny<IReadOnlyList<ChatMessage>>()))
            .Returns(new Prompt("system", "user", []));

        _chatRepositoryMock
            .Setup(x => x.GetMessagesBySessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.UpdateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private static IReadOnlyDictionary<string, object?>? TryGetStructuredState(
        Mock<ILogger<ChatService>> loggerMock,
        string expectedMessageFragment)
    {
        foreach (var invocation in loggerMock.Invocations.Where(x => x.Method.Name == nameof(ILogger.Log)))
        {
            var state = invocation.Arguments[2];
            if (state?.ToString()?.Contains(expectedMessageFragment, StringComparison.Ordinal) != true)
                continue;

            if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
            {
                return structuredState.ToDictionary(x => x.Key, x => x.Value);
            }
        }

        return null;
    }

    [Fact]
    public async Task ChatAsync_ThrowsWhenQueryEmpty()
    {
        var service = CreateService();
        var request = new ChatRequest(
            MembershipId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            SessionId: null,
            Query: "",
            DepartmentId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync(request));
    }

    [Fact]
    public async Task ChatAsync_ThrowsWhenSubscriptionCheckFails()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        _subscriptionFeatureGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("SUBSCRIPTION", "Chat not allowed")));

        var request = new ChatRequest(
            MembershipId: membershipId,
            TenantId: tenantId,
            SessionId: null,
            Query: "Hello",
            DepartmentId: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync(request));

        Assert.Contains("Chat not allowed", ex.Message);
    }

    [Fact]
    public async Task ChatAsync_CreatesNewSessionWhenNoSessionIdProvided()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _subscriptionFeatureGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        var emptyChunks = new List<DocumentChunk>();
        _vectorStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                tenantId,
                null,
                null,
                It.Is<IReadOnlyCollection<DocumentChunkType>>(types =>
                    types.Count == Enum.GetValues<DocumentChunkType>().Length),
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyChunks);

        var rerankResults = new List<(DocumentChunk Chunk, float Score)>();
        _rerankServiceMock
            .Setup(x => x.RerankAsync(It.IsAny<string>(), It.IsAny<IEnumerable<DocumentChunk>>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankResults);

        var prompt = new Prompt("You are a helpful assistant.", "Hello", new List<ChatMessage>());
        _promptBuilderMock
            .Setup(x => x.BuildFullPrompt(It.IsAny<string>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<ChatAccessScope>(), It.IsAny<IReadOnlyList<ChatMessage>>()))
            .Returns(prompt);

        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatAccessScope(
                tenantId,
                "Test Tenant",
                RoleType.TenantAdmin,
                null,
                new HashSet<Guid>(),
                membershipId,
                true,
                new HashSet<DocumentChunkType>((DocumentChunkType[])Enum.GetValues(typeof(DocumentChunkType))),
                BudgetAccessLevel.FullBudget,
                ApprovalAccessLevel.AllApprovals));

        _chatRepositoryMock
            .Setup(x => x.GetMessagesBySessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var newSession = ChatSession.Create(tenantId, membershipId, "Test Session");
        typeof(ChatSession).GetProperty("Id")!.SetValue(newSession, sessionId);

        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.GetSessionByIdAndMembershipAsync(It.IsAny<Guid>(), membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newSession);

        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.UpdateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var request = new ChatRequest(
            MembershipId: membershipId,
            TenantId: tenantId,
            SessionId: null,
            Query: "Hello",
            DepartmentId: null);

        var response = await service.ChatAsync(request);

        Assert.NotEqual(Guid.Empty, response.SessionId);
        _chatRepositoryMock.Verify(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_UsesScopeFiltersInsteadOfClientDepartment_ForStaff()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var scopedDepartmentId = Guid.NewGuid();

        _subscriptionFeatureGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatAccessScope(
                tenantId,
                "Tenant",
                RoleType.Staff,
                scopedDepartmentId,
                new HashSet<Guid>(),
                membershipId,
                false,
                new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt },
                BudgetAccessLevel.LimitOnly,
                ApprovalAccessLevel.OwnOnly));

        _vectorStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                tenantId,
                scopedDepartmentId,
                membershipId,
                It.Is<IReadOnlyCollection<DocumentChunkType>>(types =>
                    types.Count == 2 &&
                    types.Contains(DocumentChunkType.Expense) &&
                    types.Contains(DocumentChunkType.Receipt)),
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentChunk>());

        _rerankServiceMock
            .Setup(x => x.RerankAsync(It.IsAny<string>(), It.IsAny<IEnumerable<DocumentChunk>>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk Chunk, float Score)>());

        _promptBuilderMock
            .Setup(x => x.BuildFullPrompt(It.IsAny<string>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<ChatAccessScope>(), It.IsAny<IReadOnlyList<ChatMessage>>()))
            .Returns(new Prompt("system", "user", new List<ChatMessage>()));

        _chatRepositoryMock
            .Setup(x => x.GetMessagesBySessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chatRepositoryMock
            .Setup(x => x.UpdateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var request = new ChatRequest(
            membershipId,
            tenantId,
            null,
            "Hello",
            null);

        await service.ChatAsync(request);

        _vectorStoreMock.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            tenantId,
            scopedDepartmentId,
            membershipId,
            It.Is<IReadOnlyCollection<DocumentChunkType>>(types =>
                types.Count == 2 &&
                types.Contains(DocumentChunkType.Expense) &&
                types.Contains(DocumentChunkType.Receipt)),
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_LogsStructuredRetrievalAuditMetadata()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            documentId,
            departmentId,
            "Employee expense receipt",
            "hash-1",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Receipt);

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            departmentId,
            [departmentId],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        SetupHappyPath(
            tenantId,
            membershipId,
            accessScope,
            [chunk],
            [(chunk, 0.99f)]);

        var request = new ChatRequest(membershipId, tenantId, null, "Show my receipt", null);

        await service.ChatAsync(request);

        var structuredState = TryGetStructuredState(_loggerMock, "Chat retrieval audit");

        Assert.NotNull(structuredState);
        Assert.Equal(tenantId, structuredState["TenantId"]);
        Assert.Equal(membershipId, structuredState["MembershipId"]);
        Assert.Equal(nameof(RoleType.Staff), structuredState["Role"]);
        Assert.Equal(1, structuredState["RetrievedChunkCount"]);
        Assert.Equal(1, structuredState["RerankedChunkCount"]);
        Assert.Equal(membershipId, structuredState["OwnerFilter"]);
        Assert.Equal(departmentId, structuredState["EffectiveDepartmentId"]);
        Assert.Equal("Expense,Receipt", structuredState["AllowedChunkTypes"]);
        Assert.Contains(chunk.Id.ToString(), structuredState["TopChunkIds"]?.ToString());
    }

    [Fact]
    public async Task ChatAsync_StaffSecurityContract_UsesOwnerFilterToPreventCrossUserLeakage()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            departmentId,
            [departmentId],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        SetupHappyPath(tenantId, membershipId, accessScope);

        var request = new ChatRequest(membershipId, tenantId, null, "What did I spend?", null);

        await service.ChatAsync(request);

        _vectorStoreMock.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            tenantId,
            departmentId,
            membershipId,
            It.Is<IReadOnlyCollection<DocumentChunkType>>(types =>
                types.Count == 2 &&
                types.Contains(DocumentChunkType.Expense) &&
                types.Contains(DocumentChunkType.Receipt)),
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ManagerSecurityContract_UsesDepartmentFilterToPreventCrossDepartmentLeakage()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Manager,
            departmentId,
            [departmentId],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt, DocumentChunkType.ApprovalFlow, DocumentChunkType.Budget],
            BudgetAccessLevel.AggregateSpent,
            ApprovalAccessLevel.DeptApproval);

        SetupHappyPath(tenantId, membershipId, accessScope);

        var request = new ChatRequest(membershipId, tenantId, null, "Show engineering approvals", null);

        await service.ChatAsync(request);

        _vectorStoreMock.Verify(x => x.SearchAsync(
            It.IsAny<float[]>(),
            tenantId,
            departmentId,
            null,
            It.Is<IReadOnlyCollection<DocumentChunkType>>(types =>
                types.Count == 4 &&
                types.Contains(DocumentChunkType.Expense) &&
                types.Contains(DocumentChunkType.Receipt) &&
                types.Contains(DocumentChunkType.ApprovalFlow) &&
                types.Contains(DocumentChunkType.Budget)),
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_RejectsDepartmentOutsideScope_ForStaff()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var requestedDepartmentId = Guid.NewGuid();
        var scopedDepartmentId = Guid.NewGuid();

        _subscriptionFeatureGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatAccessScope(
                tenantId,
                "Tenant",
                RoleType.Staff,
                scopedDepartmentId,
                new HashSet<Guid>(),
                membershipId,
                false,
                new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt },
                BudgetAccessLevel.LimitOnly,
                ApprovalAccessLevel.OwnOnly));

        var request = new ChatRequest(
            membershipId,
            tenantId,
            null,
            "Hello",
            requestedDepartmentId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(request));

        Assert.Contains("outside your scope", ex.Message);
    }

    [Fact]
    public async Task GetHistoryAsync_ThrowsWhenSessionNotFound()
    {
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        _chatRepositoryMock
            .Setup(x => x.GetSessionByIdAndMembershipAsync(sessionId, membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetHistoryAsync(sessionId, membershipId));
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsSessionsFromRepository()
    {
        var service = CreateService();
        var membershipId = Guid.NewGuid();
var expectedSessions = new List<ChatSessionSummary>
        {
            new ChatSessionSummary(Guid.NewGuid(), "Session 1", 5, DateTime.UtcNow),
            new ChatSessionSummary(Guid.NewGuid(), "Session 2", 10, DateTime.UtcNow.AddHours(-1))
        };

        _chatRepositoryMock
            .Setup(x => x.GetSessionsAsync(membershipId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSessions);

        var result = await service.GetSessionsAsync(membershipId, 20);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ChatAsync_PostsToApiV1ChatCompletions_WhenBaseUrlHasNoTrailingSlash()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.TenantAdmin,
            null,
            [],
            membershipId,
            true,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            BudgetAccessLevel.FullBudget,
            ApprovalAccessLevel.AllApprovals);

        SetupHappyPath(tenantId, membershipId, accessScope);

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://openrouter.test/api/v1/chat/completions")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"choices\":[{\"message\":{\"content\":\"Test response from LLM\"}}]}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>wrong route</html>", Encoding.UTF8, "text/html")
            };
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.test/api/v1")
        };

        var service = new ChatService(
            _chatRepositoryMock.Object,
            _chatAuthServiceMock.Object,
            _subscriptionFeatureGateMock.Object,
            _tenantUsageServiceMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _rerankServiceMock.Object,
            _promptBuilderMock.Object,
            _currentTenantMock.Object,
            _unitOfWorkMock.Object,
            client,
            Options.Create(new GroqChatOptions
            {
                BaseUrl = "https://openrouter.test/api/v1",
                ChatModel = "test-chat-model"
            }),
            _loggerMock.Object);

        var response = await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Summarize my recent expenses", null),
            CancellationToken.None);

        Assert.Equal("Test response from LLM", response.Answer);
    }

    [Fact]
    public async Task ChatAsync_DoesNotForceUpdate_ForNewlyCreatedSession()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            null,
            [],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        SetupHappyPath(tenantId, membershipId, accessScope);

        await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Summarize my recent expenses", null),
            CancellationToken.None);

        _chatRepositoryMock.Verify(
            x => x.UpdateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
            _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }
}
