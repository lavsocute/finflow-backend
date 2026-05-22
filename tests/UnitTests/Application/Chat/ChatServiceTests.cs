using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Reporting;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
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
    private readonly Mock<ISubscriptionQuotaGate> _subscriptionQuotaGateMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IRerankService> _rerankServiceMock;
    private readonly Mock<IPromptBuilder> _promptBuilderMock;
    private readonly Mock<IChatIntentRouter> _chatIntentRouterMock;
    private readonly Mock<IChatReportingService> _chatReportingServiceMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ICacheService> _cacheServiceMock;
    private readonly Mock<ILogger<ChatService>> _loggerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<GroqChatOptions> _options;

    public ChatServiceTests()
    {
        _chatRepositoryMock = new Mock<IChatRepository>();
        _chatAuthServiceMock = new Mock<IChatAuthorizationService>();
        _subscriptionQuotaGateMock = new Mock<ISubscriptionQuotaGate>();
        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotTokensAvailableAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _subscriptionQuotaGateMock
            .Setup(x => x.RecordChatbotTokensAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _vectorStoreMock
            .Setup(x => x.KeywordSearchAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<FinFlow.Domain.Documents.DocumentChunkType>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FinFlow.Domain.Documents.DocumentChunk>());
        _rerankServiceMock = new Mock<IRerankService>();
        _promptBuilderMock = new Mock<IPromptBuilder>();
        _promptBuilderMock
            .Setup(x => x.BuildGeneralPrompt(It.IsAny<string>(), It.IsAny<ChatIntentClassification>(), It.IsAny<IReadOnlyList<ChatMessage>>()))
            .Returns(new Prompt("general-system", "general-user", []));
        _chatIntentRouterMock = new Mock<IChatIntentRouter>();
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Rag, "default-rag"));
        _chatReportingServiceMock = new Mock<IChatReportingService>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cacheServiceMock = new Mock<ICacheService>();
        _loggerMock = new Mock<ILogger<ChatService>>();
        _chatRepositoryMock
            .Setup(x => x.GetMessagesBySessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

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

    private ChatService CreateService(
        IChatIntentRouter? intentRouter = null,
        IChatReportingService? reportingService = null,
        IChatPolicyEngine? policyEngine = null) => new ChatService(
        _chatRepositoryMock.Object,
        _chatAuthServiceMock.Object,
        _subscriptionQuotaGateMock.Object,
        _embeddingServiceMock.Object,
        _vectorStoreMock.Object,
        _rerankServiceMock.Object,
        _promptBuilderMock.Object,
        intentRouter ?? _chatIntentRouterMock.Object,
        reportingService ?? _chatReportingServiceMock.Object,
        _currentTenantMock.Object,
        _unitOfWorkMock.Object,
        _cacheServiceMock.Object,
        _httpClient,
        _options,
        _loggerMock.Object,
        chatPolicyEngine: policyEngine);

    private void SetupHappyPath(
        Guid tenantId,
        Guid membershipId,
        ChatAccessScope accessScope,
        IReadOnlyList<DocumentChunk>? searchChunks = null,
        IReadOnlyList<(DocumentChunk Chunk, float Score)>? rerankedResults = null,
        SubscriptionQuotaDecision? quotaDecision = null,
        ChatAuthorizationProfile? authorizationProfile = null)
    {
        quotaDecision ??= CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1);
        authorizationProfile ??= new ChatAuthorizationProfile(
            accessScope.TenantId,
            accessScope.TenantName,
            accessScope.Role,
            accessScope.OwnerMembershipId,
            accessScope.DepartmentId,
            accessScope.PermittedDepartmentIds,
            accessScope.CanAccessAllTenantData,
            accessScope.AllowedChunkTypes,
            accessScope.Capabilities);

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(quotaDecision));

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorizationProfile);

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

    private static IReadOnlyDictionary<string, object?>? TryGetPropertyBag(object? value)
    {
        if (value is null)
            return null;

        return value
            .GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(value));
    }

    private static IReadOnlyDictionary<string, object?>? FindNestedStructuredState(
        IReadOnlyDictionary<string, object?> structuredState,
        string requiredKey)
    {
        foreach (var value in structuredState.Values)
        {
            var bag = TryGetPropertyBag(value);
            if (bag is not null && bag.ContainsKey(requiredKey))
                return bag;
        }

        return null;
    }

    private static SubscriptionQuotaDecision CreateQuotaDecision(
        Guid tenantId,
        Guid membershipId,
        SubscriptionFeature feature,
        int approvedUnitCount) =>
        new(
            tenantId,
            membershipId,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            feature,
            approvedUnitCount,
            new PlanEntitlements(true, true, true, 10L * 1024 * 1024 * 1024, 1_000, 100, 10_000, 500),
            0,
            0,
            0,
            0);

    private static ChatAuthorizationProfile CreateAuthorizationProfile(ChatAccessScope scope) =>
        new(
            scope.TenantId,
            scope.TenantName,
            scope.Role,
            scope.OwnerMembershipId,
            scope.DepartmentId,
            scope.PermittedDepartmentIds,
            scope.CanAccessAllTenantData,
            scope.AllowedChunkTypes,
            scope.Capabilities);

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

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SubscriptionQuotaDecision>(new Error("SUBSCRIPTION", "Chat not allowed")));

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

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));

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

        var accessScope = new ChatAccessScope(
            tenantId,
            "Test Tenant",
            RoleType.TenantAdmin,
            null,
            new HashSet<Guid>(),
            membershipId,
            true,
            new HashSet<DocumentChunkType>((DocumentChunkType[])Enum.GetValues(typeof(DocumentChunkType))),
            BudgetAccessLevel.FullBudget,
            ApprovalAccessLevel.AllApprovals);

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthorizationProfile(accessScope));
        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

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
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ChatAsync_UsesScopeFiltersInsteadOfClientDepartment_ForStaff()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var scopedDepartmentId = Guid.NewGuid();

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            scopedDepartmentId,
            new HashSet<Guid>(),
            membershipId,
            false,
            new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt },
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthorizationProfile(accessScope));
        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

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

        var request = new ChatRequest(membershipId, tenantId, null, "Show my receipt", departmentId);

        var response = await service.ChatAsync(request);

        var structuredState = TryGetStructuredState(_loggerMock, "Chat retrieval audit");
        var retrievalAuditPayload = structuredState?.TryGetValue("RetrievalAudit", out var retrievalAuditValue) == true
            ? retrievalAuditValue
            : structuredState?.TryGetValue("@RetrievalAudit", out var destructuredRetrievalAuditValue) == true
                ? destructuredRetrievalAuditValue
                : null;
        var retrievalAudit = TryGetPropertyBag(retrievalAuditPayload);

        Assert.NotNull(structuredState);
        Assert.NotNull(retrievalAudit);
        Assert.Equal(response.SessionId, retrievalAudit["SessionId"]);
        Assert.Equal(tenantId, retrievalAudit["TenantId"]);
        Assert.Equal(membershipId, retrievalAudit["MembershipId"]);
        Assert.Equal(nameof(RoleType.Staff), retrievalAudit["Role"]);
        Assert.Equal(departmentId, retrievalAudit["RequestedDepartmentId"]);
        Assert.Equal(departmentId, retrievalAudit["EffectiveDepartmentId"]);
        Assert.Equal(membershipId, retrievalAudit["OwnerFilter"]);
        Assert.Equal(1, retrievalAudit["RetrievedChunkCount"]);

        var allowedChunkTypes = Assert.IsAssignableFrom<string[]>(retrievalAudit["AllowedChunkTypes"]);
        Assert.Equal(["Expense", "Receipt"], allowedChunkTypes.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        var topChunkIds = Assert.IsAssignableFrom<Guid[]>(retrievalAudit["TopChunkIds"]);
        Assert.Contains(chunk.Id, topChunkIds);

        Assert.DoesNotContain("Employee expense receipt", retrievalAuditPayload?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatAsync_ReturnsAuthorizedNoContextMessage_WhenSearchReturnsZeroChunks()
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

        SetupHappyPath(tenantId, membershipId, accessScope, [], []);

        var response = await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Show me my expenses", null),
            CancellationToken.None);

        Assert.Equal(0, response.DocumentCount);
        Assert.Contains("chưa tìm thấy đủ thông tin", response.Answer, StringComparison.OrdinalIgnoreCase);

        _promptBuilderMock.Verify(
            x => x.BuildFullPrompt(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DocumentChunk>>(),
                It.IsAny<ChatAccessScope>(),
                It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);

        _chatRepositoryMock.Verify(
            x => x.AddMessageAsync(
                It.Is<ChatMessage>(message =>
                    message.Role == ChatMessageRole.Assistant &&
                    message.Content.Contains("chưa tìm thấy đủ thông tin", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ThrowsWhenDepartmentScopedRoleHasNoDepartmentBoundary()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Manager,
            null,
            [],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt, DocumentChunkType.ApprovalFlow, DocumentChunkType.Budget],
            BudgetAccessLevel.AggregateSpent,
            ApprovalAccessLevel.DeptApproval);

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthorizationProfile(accessScope));
        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Show department expenses", null),
            CancellationToken.None));

        Assert.Contains("department boundary", ex.Message, StringComparison.OrdinalIgnoreCase);
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

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));

        _embeddingServiceMock
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            scopedDepartmentId,
            new HashSet<Guid>(),
            membershipId,
            false,
            new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt },
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthorizationProfile(accessScope));
        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

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
    public async Task ChatAsync_DeniedRequest_DoesNotCallAddSessionAsync()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var requestedDepartmentId = Guid.NewGuid();
        var scopedDepartmentId = Guid.NewGuid();

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));

        var accessScope = new ChatAccessScope(
            tenantId,
            "Tenant",
            RoleType.Staff,
            scopedDepartmentId,
            [],
            membershipId,
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthorizationProfile(accessScope));
        _chatAuthServiceMock
            .Setup(x => x.GetChatAccessScopeAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessScope);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Hello", requestedDepartmentId),
            CancellationToken.None));

        Assert.Contains("outside your scope", ex.Message, StringComparison.OrdinalIgnoreCase);

        _chatRepositoryMock.Verify(
            x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _chatRepositoryMock.Verify(
            x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_RejectsOutOfScopeReturnedChunk_BeforePromptBuildingOrPersistence()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var foreignOwnerMembershipId = Guid.NewGuid();

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

        var outOfScopeChunk = DocumentChunk.Create(
            tenantId,
            foreignOwnerMembershipId,
            Guid.NewGuid(),
            departmentId,
            "Another employee expense",
            "hash-foreign-owner",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [outOfScopeChunk], [(outOfScopeChunk, 0.99f)]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Show my expense", null),
            CancellationToken.None));

        Assert.Contains("out-of-scope", ex.Message, StringComparison.OrdinalIgnoreCase);

        _rerankServiceMock.Verify(
            x => x.RerankAsync(It.IsAny<string>(), It.IsAny<IEnumerable<DocumentChunk>>(), 5, It.IsAny<CancellationToken>()),
            Times.Never);
        _promptBuilderMock.Verify(
            x => x.BuildFullPrompt(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DocumentChunk>>(),
                It.IsAny<ChatAccessScope>(),
                It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
        _chatRepositoryMock.Verify(
            x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _chatRepositoryMock.Verify(
            x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
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

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Recent expense summary",
            "hash-1",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

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
            _subscriptionQuotaGateMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _rerankServiceMock.Object,
            _promptBuilderMock.Object,
            _chatIntentRouterMock.Object,
            _chatReportingServiceMock.Object,
            _currentTenantMock.Object,
            _unitOfWorkMock.Object,
            _cacheServiceMock.Object,
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

    [Fact]
    public async Task ChatAsync_ThrowsWhenMemberQuotaCheckFails()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SubscriptionQuotaDecision>(
                new Error("Subscription.ChatbotMemberQuotaExceeded", "The current member has reached the monthly chatbot quota.")));

        var request = new ChatRequest(membershipId, tenantId, null, "Hello", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(request, CancellationToken.None));

        Assert.Contains("monthly chatbot quota", ex.Message, StringComparison.OrdinalIgnoreCase);
        _subscriptionQuotaGateMock.Verify(
            x => x.RecordChatbotUsageAsync(It.IsAny<SubscriptionQuotaDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_RecordsApprovedUsageThroughQuotaDecision_AfterSuccessfulWork()
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
        var decision = CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1);

        SetupHappyPath(tenantId, membershipId, accessScope, quotaDecision: decision);

        await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Summarize my recent expenses", null),
            CancellationToken.None);

        _subscriptionQuotaGateMock.Verify(
            x => x.RecordChatbotUsageAsync(
                It.Is<SubscriptionQuotaDecision>(d =>
                    d.TenantId == tenantId &&
                    d.MembershipId == membershipId &&
                    d.Feature == SubscriptionFeature.Chatbot &&
                    d.ApprovedUnitCount == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChatAsync_DoesNotRunVectorStoreOperationsConcurrently()
    {
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

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Recent expense summary",
            "hash-sequential",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var strictVectorStore = new NonConcurrentVectorStore([chunk]);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

        var service = new ChatService(
            _chatRepositoryMock.Object,
            _chatAuthServiceMock.Object,
            _subscriptionQuotaGateMock.Object,
            _embeddingServiceMock.Object,
            strictVectorStore,
            _rerankServiceMock.Object,
            _promptBuilderMock.Object,
            _chatIntentRouterMock.Object,
            _chatReportingServiceMock.Object,
            _currentTenantMock.Object,
            _unitOfWorkMock.Object,
            _cacheServiceMock.Object,
            _httpClient,
            _options,
            _loggerMock.Object);

        var response = await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Summarize my recent expenses", null),
            CancellationToken.None);

        Assert.Equal("Test response from LLM", response.Answer);
    }

    [Fact]
    public async Task ChatAsync_UsesReportingPath_ForOwnAggregateQuestion()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "keyword-reporting"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedFrom = new DateOnly(today.Year, today.Month, 1);
        var expectedTo = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        _chatReportingServiceMock
            .Setup(x => x.BuildOwnExpenseSummaryAsync(profile, expectedFrom, expectedTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Your total confirmed spending for this period is 12 VND across 3 expenses.", "own-expense-summary", 3));

        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = CreateService();

        var response = await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Tháng này tôi đã tiêu bao nhiêu?", null),
            CancellationToken.None);

        Assert.Contains("12 VND", response.Answer);
        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Equal(3, response.DocumentCount);
        _chatReportingServiceMock.Verify(
            x => x.BuildOwnExpenseSummaryAsync(profile, expectedFrom, expectedTo, It.IsAny<CancellationToken>()),
            Times.Once);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _vectorStoreMock.Verify(
            x => x.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _chatRepositoryMock.Verify(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Once);
        _chatRepositoryMock.Verify(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ChatAsync_UsesAggregateReportingPath_ForDepartmentSummary()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "keyword-reporting",
                ChatIntentFamily.Aggregate,
                ChatScopeConfidence.SafeInferred));
        _chatReportingServiceMock
            .Setup(x => x.BuildScopedExpenseSummaryAsync(
                profile,
                "Phòng ban tôi đã chi bao nhiêu tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Tổng chi của phòng ban bạn là 1,250,000 VND.", "department-expense-summary", 5));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Phòng ban tôi đã chi bao nhiêu tháng này?", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("1,250,000", response.Answer, StringComparison.OrdinalIgnoreCase);
        _chatReportingServiceMock.Verify(
            x => x.BuildScopedExpenseSummaryAsync(
                profile,
                "Phòng ban tôi đã chi bao nhiêu tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chatReportingServiceMock.Verify(
            x => x.BuildOwnExpenseSummaryAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesRankingReportingPath_ForTopEmployees()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "comparison-reporting",
                ChatIntentFamily.Ranking,
                ChatScopeConfidence.SafeInferred));
        _chatReportingServiceMock
            .Setup(x => x.BuildTopEmployeesSummaryAsync(
                profile,
                "Nhân viên nào chi nhiều nhất trong phòng ban tôi tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Top nhân viên chi tiêu hiện là Nguyen Van A với 900,000 VND.", "top-employees-summary", 3));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Nhân viên nào chi nhiều nhất trong phòng ban tôi tháng này?", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("Nguyen Van A", response.Answer, StringComparison.OrdinalIgnoreCase);
        _chatReportingServiceMock.Verify(
            x => x.BuildTopEmployeesSummaryAsync(
                profile,
                "Nhân viên nào chi nhiều nhất trong phòng ban tôi tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chatReportingServiceMock.Verify(
            x => x.BuildExpenseComparisonAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesTrendReportingPath_ForDepartmentTrend()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "trend-reporting",
                ChatIntentFamily.Aggregate,
                ChatScopeConfidence.SafeInferred));
        _chatReportingServiceMock
            .Setup(x => x.BuildMonthlyTrendSummaryAsync(
                profile,
                "Xu hướng chi tiêu 3 tháng gần đây của phòng ban tôi là gì?",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Xu hướng 3 tháng gần đây tăng mạnh ở 2026-04 với 900,000 VND.", "monthly-trend-summary", 3));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Xu hướng chi tiêu 3 tháng gần đây của phòng ban tôi là gì?", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("2026-04", response.Answer, StringComparison.OrdinalIgnoreCase);
        _chatReportingServiceMock.Verify(
            x => x.BuildMonthlyTrendSummaryAsync(
                profile,
                "Xu hướng chi tiêu 3 tháng gần đây của phòng ban tôi là gì?",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chatReportingServiceMock.Verify(
            x => x.BuildOwnExpenseSummaryAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesVendorReportingPath_ForDepartmentTopVendor()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "vendor-reporting",
                ChatIntentFamily.Aggregate,
                ChatScopeConfidence.SafeInferred));
        _chatReportingServiceMock
            .Setup(x => x.BuildTopVendorsSummaryAsync(
                profile,
                "Nhà cung cấp nào có tổng chi lớn nhất trong phòng ban tôi tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Top nhà cung cấp hiện là Bách Hóa Xanh với 1,500,000 VND.", "top-vendors-summary", 2));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Nhà cung cấp nào có tổng chi lớn nhất trong phòng ban tôi tháng này?", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("Bách Hóa Xanh", response.Answer, StringComparison.OrdinalIgnoreCase);
        _chatReportingServiceMock.Verify(
            x => x.BuildTopVendorsSummaryAsync(
                profile,
                "Nhà cung cấp nào có tổng chi lớn nhất trong phòng ban tôi tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesBudgetReportingPath_ForDepartmentBudgetRemaining()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "budget-reporting",
                ChatIntentFamily.Aggregate,
                ChatScopeConfidence.SafeInferred));
        _chatReportingServiceMock
            .Setup(x => x.BuildBudgetUtilizationSummaryAsync(
                profile,
                "Ngân sách phòng ban tôi còn bao nhiêu tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Ngân sách còn lại của phòng ban bạn là 250,000 VND.", "budget-utilization-summary", 1));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Ngân sách phòng ban tôi còn bao nhiêu tháng này?", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("250,000", response.Answer, StringComparison.OrdinalIgnoreCase);
        _chatReportingServiceMock.Verify(
            x => x.BuildBudgetUtilizationSummaryAsync(
                profile,
                "Ngân sách phòng ban tôi còn bao nhiêu tháng này?",
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_SetsAnswerSource_ToRag_ForCacheHitPath()
    {
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

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Cached receipt detail",
            "hash-cache-answer-source",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

        _cacheServiceMock
            .Setup(x => x.GetAsync<ChatResponseCacheEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponseCacheEntry(
                Answer: "Cached answer",
                DocumentCount: 1,
                TokenUsage: 17,
                Citations:
                [
                    new CachedCitation(1, chunk.Id, chunk.DocumentId, chunk.Type.ToString(), "Cached receipt detail")
                ]));

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Show my cached receipt", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Rag, response.AnswerSource);
        Assert.Equal("Cached answer", response.Answer);
        Assert.Single(response.Citations!);
    }

    [Fact]
    public async Task ChatAsync_StripsChunkMarkers_FromDisplayedRagAnswer_ButKeepsCitations()
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
            [DocumentChunkType.Expense],
            BudgetAccessLevel.FullBudget,
            ApprovalAccessLevel.AllApprovals);

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Expense detail",
            "hash-strip-markers",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"content\":\"Tổng chi là 5 VND [chunk-1].\"}}]}",
                Encoding.UTF8,
                "application/json")
        });

        var service = new ChatService(
            _chatRepositoryMock.Object,
            _chatAuthServiceMock.Object,
            _subscriptionQuotaGateMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _rerankServiceMock.Object,
            _promptBuilderMock.Object,
            _chatIntentRouterMock.Object,
            _chatReportingServiceMock.Object,
            _currentTenantMock.Object,
            _unitOfWorkMock.Object,
            _cacheServiceMock.Object,
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1") },
            _options,
            _loggerMock.Object);

        var response = await service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Tong chi la bao nhieu", null),
            CancellationToken.None);

        Assert.Equal("Tổng chi là 5 VND.", response.Answer);
        Assert.Single(response.Citations!);
        Assert.DoesNotContain("[chunk-", response.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatAsync_UsesDeterministicFormatter_ForExpenseListingQuery()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
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

        var expenseChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            documentId,
            null,
            """
            Expense record
            Merchant: ABC
            Reference: abc
            Expense date: 2026-05-20
            Category: Groceries
            DepartmentId: 00000000-0000-0000-0000-000000000000
            Total: 187954
            Status: ReadyForApproval
            Submitted at UTC: 2026-05-20T10:00:00Z
            """,
            "hash-deterministic-rag",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [expenseChunk], [(expenseChunk, 0.95f)]);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "show tất cả expense giúp tôi", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Rag, response.AnswerSource);
        Assert.Equal(0, response.TokenUsage);
        Assert.Contains("Tôi tìm thấy 1 khoản chi phù hợp", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mã tham chiếu: ABC", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trạng thái: Chờ duyệt", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DepartmentId", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Single(response.Citations!);
        _promptBuilderMock.Verify(
            x => x.BuildFullPrompt(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DocumentChunk>>(),
                It.IsAny<ChatAccessScope>(),
                It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_UsesDeterministicFormatter_ForRecentDocumentQuery()
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

        var recentChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            """
            Expense record
            Merchant: Vendor Moi
            Reference: RECENT-001
            Expense date: 2026-05-20
            Total: 90000
            Status: Approved
            Submitted at UTC: 2026-05-20T10:00:00Z
            """,
            "hash-stream-recent",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var olderChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            """
            Expense record
            Merchant: Vendor Cu
            Reference: OLDER-001
            Expense date: 2026-05-10
            Total: 50000
            Status: ReadyForApproval
            Submitted at UTC: 2026-05-10T10:00:00Z
            """,
            "hash-stream-older",
            1,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(
            tenantId,
            membershipId,
            accessScope,
            [recentChunk, olderChunk],
            [(recentChunk, 0.95f), (olderChunk, 0.85f)]);

        var events = await CollectStreamEventsAsync(CreateService().ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "hóa đơn gần đây", null),
            CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(ChatStreamEventKind.Token, events[0].Kind);
        Assert.Contains("gần đây", events[0].TokenChunk, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatStreamEventKind.Complete, events[1].Kind);
        Assert.Equal(ChatAnswerSource.Rag, events[1].AnswerSource);
        Assert.Equal(2, events[1].DocumentCount);
        Assert.Contains("Vendor Moi", events[1].CompleteAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, events[1].TokenUsage);
        _promptBuilderMock.Verify(
            x => x.BuildFullPrompt(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<DocumentChunk>>(),
                It.IsAny<ChatAccessScope>(),
                It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_SetsAnswerSource_ToRag_ForNormalRagPath()
    {
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

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Recent receipt detail",
            "hash-rag-answer-source-non-stream",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Show my recent receipt", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Rag, response.AnswerSource);
        Assert.Equal("Test response from LLM", response.Answer);
        Assert.Equal(1, response.DocumentCount);
    }

    [Fact]
    public async Task ChatAsync_UsesGreetingPath_ForHello()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Greeting, "keyword-greeting"));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "hello", null),
            CancellationToken.None);

        Assert.Contains("Xin chào", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.DocumentCount);
        Assert.Empty(response.Citations!);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _chatReportingServiceMock.Verify(
            x => x.BuildOwnExpenseSummaryAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesGeneralPath_ForLowSignalQuery()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "low-signal-general", ChatIntentFamily.LowSignal, ChatScopeConfidence.Ambiguous));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "abc", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.General, response.AnswerSource);
        Assert.Contains("hỏi cụ thể hơn", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.DocumentCount);
        Assert.Null(response.Citations);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesGeneralPath_ForProductivityRewrite()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "productivity-general", ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Viết lại câu này cho lịch sự hơn: gửi hóa đơn cho tôi", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.General, response.AnswerSource);
        Assert.Equal("Test response from LLM", response.Answer);
        _promptBuilderMock.Verify(
            x => x.BuildGeneralPrompt(
                "Viết lại câu này cho lịch sự hơn: gửi hóa đơn cho tôi",
                It.Is<ChatIntentClassification>(c => c.Mode == ChatExecutionMode.General && c.Family == ChatIntentFamily.Productivity),
                It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Once);
        _promptBuilderMock.Verify(
            x => x.BuildFullPrompt(It.IsAny<string>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<ChatAccessScope>(), It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ReturnsDenyMessage_ForProgrammingIntent()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.TenantAdmin,
            membershipId,
            Guid.NewGuid(),
            [],
            true,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, true, true));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "programming-deny", ChatIntentFamily.Programming, ChatScopeConfidence.Explicit));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Viết code python để phân tích các hóa đơn này", null),
            CancellationToken.None);

        Assert.Contains("mã nguồn", response.Answer, StringComparison.OrdinalIgnoreCase);
        _promptBuilderMock.Verify(
            x => x.BuildGeneralPrompt(It.IsAny<string>(), It.IsAny<ChatIntentClassification>(), It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ReturnsDenyMessage_ForSensitiveAdviceIntent()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.TenantAdmin,
            membershipId,
            Guid.NewGuid(),
            [],
            true,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, true, true));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "sensitive-advice-deny", ChatIntentFamily.SensitiveAdvice, ChatScopeConfidence.Explicit));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "vendor này có mùi gian lận không", null),
            CancellationToken.None);

        Assert.Contains("gian lận", response.Answer, StringComparison.OrdinalIgnoreCase);
        _promptBuilderMock.Verify(
            x => x.BuildGeneralPrompt(It.IsAny<string>(), It.IsAny<ChatIntentClassification>(), It.IsAny<IReadOnlyList<ChatMessage>>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_LogsBoundaryBlockedTelemetry_ForProgrammingIntent()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.TenantAdmin,
            membershipId,
            Guid.NewGuid(),
            [],
            true,
            [DocumentChunkType.Expense],
            new ChatCapabilities(true, true, true, true, true, true, true, true));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "programming-deny", ChatIntentFamily.Programming, ChatScopeConfidence.Explicit));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Viết code python để phân tích các hóa đơn này", null),
            CancellationToken.None);

        var logState = TryGetStructuredState(_loggerMock, "Chat boundary blocked");
        Assert.NotNull(logState);
        var telemetry = FindNestedStructuredState(logState!, "TelemetryKind");
        Assert.NotNull(telemetry);
        Assert.Equal("1", telemetry!["TelemetryVersion"]?.ToString());
        Assert.Equal("BoundaryBlocked", telemetry!["TelemetryKind"]?.ToString());
        Assert.Equal("Medium", telemetry["AlertSeverity"]?.ToString());
        Assert.Equal("Programming", telemetry["BoundaryFamily"]?.ToString());
        Assert.Equal("programming-deny", telemetry["Reason"]?.ToString());
    }

    [Fact]
    public async Task ChatAsync_LogsBoundaryGapCandidateTelemetry_ForUnexpectedDeny()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "unexpected-deny", ChatIntentFamily.Unknown, ChatScopeConfidence.Ambiguous));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var customPolicy = new Mock<IChatPolicyEngine>();
        customPolicy
            .Setup(x => x.Decide(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<ChatIntentClassification>(), It.IsAny<string>()))
            .Returns(new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, "blocked"));

        await CreateService(policyEngine: customPolicy.Object).ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "câu hỏi lạ", null),
            CancellationToken.None);

        var logState = TryGetStructuredState(_loggerMock, "Chat boundary gap candidate");
        Assert.NotNull(logState);
        var telemetry = FindNestedStructuredState(logState!, "TelemetryKind");
        Assert.NotNull(telemetry);
        Assert.Equal("1", telemetry!["TelemetryVersion"]?.ToString());
        Assert.Equal("BoundaryGapCandidate", telemetry!["TelemetryKind"]?.ToString());
        Assert.Equal("High", telemetry["AlertSeverity"]?.ToString());
        Assert.Equal("Unknown", telemetry["BoundaryFamily"]?.ToString());
        Assert.Equal("unexpected-deny", telemetry["Reason"]?.ToString());
    }

    [Fact]
    public async Task ChatAsync_UsesApprovalReportingPath_ForDepartmentPendingApprovals()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt, DocumentChunkType.ApprovalFlow],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "approval-reporting"));
        _chatReportingServiceMock
            .Setup(x => x.BuildPendingApprovalSummaryAsync(profile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Có 1 hóa đơn đang chờ duyệt trong phạm vi phòng ban.", "pending-approval-summary", 1));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "Hóa đơn nào đang chờ duyệt trong phòng ban?", null),
            CancellationToken.None);

        Assert.Contains("đang chờ duyệt", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Equal(1, response.DocumentCount);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_DoesNotFallBackToRag_WhenReportingPathDenied()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(false, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "keyword-reporting"));

        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var service = CreateService(reportingService: new ChatReportingService(reporting.Object));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "How much did I spend this month?", null),
            CancellationToken.None));

        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _vectorStoreMock.Verify(
            x => x.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _chatRepositoryMock.Verify(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _chatRepositoryMock.Verify(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_UsesReportingPath_ForOwnAggregateQuestion()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "keyword-reporting"));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedFrom = new DateOnly(today.Year, today.Month, 1);
        var expectedTo = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        _chatReportingServiceMock
            .Setup(x => x.BuildOwnExpenseSummaryAsync(profile, expectedFrom, expectedTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer("Your total confirmed spending for this period is 12 VND across 3 expenses.", "own-expense-summary", 3));

        var service = CreateService();

        var events = await CollectStreamEventsAsync(service.ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "Tháng này tôi đã tiêu bao nhiêu?", null),
            CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(ChatStreamEventKind.Token, events[0].Kind);
        Assert.Contains("12 VND", events[0].TokenChunk);
        Assert.Equal(ChatStreamEventKind.Complete, events[1].Kind);
        Assert.Equal(ChatAnswerSource.Reporting, events[1].AnswerSource);
        Assert.Equal(3, events[1].DocumentCount);
        Assert.Contains("12 VND", events[1].CompleteAnswer);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _vectorStoreMock.Verify(
            x => x.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_CompleteEvent_SetsAnswerSource_ToRag_ForRagPath()
    {
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

        var chunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            "Chi tiet chung tu",
            "hash-rag-answer-source",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        SetupHappyPath(tenantId, membershipId, accessScope, [chunk], [(chunk, 0.95f)]);

        var events = await CollectStreamEventsAsync(CreateService().ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "Cho toi xem chung tu gan day", null),
            CancellationToken.None));

        Assert.Equal(ChatAnswerSource.Rag, events.Last(e => e.Kind == ChatStreamEventKind.Complete).AnswerSource);
    }

    [Fact]
    public async Task ChatStreamAsync_DoesNotFallBackToRag_WhenReportingPathDenied()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(false, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "keyword-reporting"));

        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var service = CreateService(reportingService: new ChatReportingService(reporting.Object));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.ChatStreamAsync(
                new ChatRequest(membershipId, tenantId, null, "How much did I spend this month?", null),
                CancellationToken.None))
            {
            }
        });

        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _vectorStoreMock.Verify(
            x => x.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_ReturnsClarifyMessage_ForManagerAmbiguousRanking()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "comparison-reporting",
                ChatIntentFamily.Ranking,
                ChatScopeConfidence.Ambiguous));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var events = await CollectStreamEventsAsync(CreateService().ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "tôi đứng thứ mấy", null),
            CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(ChatStreamEventKind.Token, events[0].Kind);
        Assert.Contains("phòng ban", events[0].TokenChunk, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatStreamEventKind.Complete, events[1].Kind);
        Assert.Equal(ChatAnswerSource.Reporting, events[1].AnswerSource);
        Assert.Equal(0, events[1].DocumentCount);
        _chatReportingServiceMock.Verify(
            x => x.BuildExpenseComparisonAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UsesComparisonReportingPath_ForCrossUserComparison()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Reporting, "comparison-reporting"));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _chatReportingServiceMock
            .Setup(x => x.BuildExpenseComparisonAsync(
                profile,
                It.IsAny<string>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReportingAnswer(
                "Tôi không thể so sánh chi tiêu của bạn với người khác trong công ty vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi của bạn.",
                "expense-comparison-denied",
                0));

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "So sánh chi tiêu của tôi với người khác trong công ty", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("không thể so sánh", response.Answer, StringComparison.OrdinalIgnoreCase);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ReturnsClarifyMessage_ForManagerAmbiguousRanking()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [departmentId],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "comparison-reporting",
                ChatIntentFamily.Ranking,
                ChatScopeConfidence.Ambiguous));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "tôi đứng thứ mấy", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("phòng ban", response.Answer, StringComparison.OrdinalIgnoreCase);
        var logState = TryGetStructuredState(_loggerMock, "Chat policy requires clarification");
        Assert.NotNull(logState);
        var audit = FindNestedStructuredState(logState!, "IntentFamily");
        Assert.NotNull(audit);
        Assert.Equal("Ranking", audit!["IntentFamily"]?.ToString());
        Assert.Equal("Ambiguous", audit["ScopeConfidence"]?.ToString());
        Assert.Equal("Clarify", audit["Decision"]?.ToString());
        _chatReportingServiceMock.Verify(
            x => x.BuildExpenseComparisonAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ReturnsDenyMessage_ForStaffCrossUserComparison()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "comparison-reporting",
                ChatIntentFamily.Comparison,
                ChatScopeConfidence.Explicit));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "So sánh chi tiêu của tôi với người khác trong công ty", null),
            CancellationToken.None);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
        Assert.Contains("quyền hiện tại", response.Answer, StringComparison.OrdinalIgnoreCase);
        var logState = TryGetStructuredState(_loggerMock, "Chat policy denied");
        Assert.NotNull(logState);
        var audit = FindNestedStructuredState(logState!, "IntentFamily");
        Assert.NotNull(audit);
        Assert.Equal("Comparison", audit!["IntentFamily"]?.ToString());
        Assert.Equal("Explicit", audit["ScopeConfidence"]?.ToString());
        Assert.Equal("Deny", audit["Decision"]?.ToString());
        _chatReportingServiceMock.Verify(
            x => x.BuildExpenseComparisonAsync(It.IsAny<ChatAuthorizationProfile>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_UsesGreetingPath_ForHello()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Greeting, "keyword-greeting"));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var events = await CollectStreamEventsAsync(CreateService().ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "hello", null),
            CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(ChatStreamEventKind.Token, events[0].Kind);
        Assert.Contains("Xin chào", events[0].TokenChunk, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatStreamEventKind.Complete, events[1].Kind);
        Assert.Contains("Xin chào", events[1].CompleteAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, events[1].DocumentCount);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatStreamAsync_UsesGeneralPath_ForLowSignalQuery()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.General, "low-signal-general", ChatIntentFamily.LowSignal, ChatScopeConfidence.Ambiguous));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var events = await CollectStreamEventsAsync(CreateService().ChatStreamAsync(
            new ChatRequest(membershipId, tenantId, null, "abc", null),
            CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(ChatStreamEventKind.Token, events[0].Kind);
        Assert.Contains("hỏi cụ thể hơn", events[0].TokenChunk, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatStreamEventKind.Complete, events[1].Kind);
        Assert.Equal(ChatAnswerSource.General, events[1].AnswerSource);
        _embeddingServiceMock.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(
            x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<DocumentChunkType>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChatAsync_PersistsQuotaUsage_AfterSavingConversation()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            [DocumentChunkType.Expense, DocumentChunkType.Receipt],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        _subscriptionQuotaGateMock
            .Setup(x => x.EnsureChatbotAllowedAsync(tenantId, membershipId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateQuotaDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, 1)));
        _subscriptionQuotaGateMock
            .Setup(x => x.RecordChatbotUsageAsync(It.IsAny<SubscriptionQuotaDecision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatAuthServiceMock
            .Setup(x => x.GetAuthorizationProfileAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _chatIntentRouterMock
            .Setup(x => x.Classify(It.IsAny<string>()))
            .Returns(new ChatIntentClassification(ChatExecutionMode.Greeting, "keyword-greeting"));
        _chatRepositoryMock
            .Setup(x => x.AddSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chatRepositoryMock
            .Setup(x => x.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateService().ChatAsync(
            new ChatRequest(membershipId, tenantId, null, "hello", null),
            CancellationToken.None);

        _subscriptionQuotaGateMock.Verify(
            x => x.RecordChatbotUsageAsync(It.IsAny<SubscriptionQuotaDecision>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void BuildReportingPrompt_DoesNotRequireChunkCitations()
    {
        var builder = new PromptBuilder();
        var profile = new ChatAuthorizationProfile(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            false,
            [],
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        var prompt = builder.BuildReportingPrompt(
            "Tháng này tôi đã tiêu bao nhiêu?",
            "Your total confirmed spending is 12 VND.",
            profile);

        Assert.DoesNotContain("[chunk-", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trusted reporting data", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot support", prompt.System, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGeneralPrompt_DoesNotMentionChunks_AndPreservesGeneralBoundary()
    {
        var builder = new PromptBuilder();

        var prompt = builder.BuildGeneralPrompt(
            "Viết lại câu này cho lịch sự hơn",
            new ChatIntentClassification(ChatExecutionMode.General, "productivity-general", ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit),
            []);

        Assert.DoesNotContain("[chunk-", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("general productivity", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not claim access to internal data", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not generate code", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not present recommendations", prompt.System, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFullPrompt_BlocksUnsupportedRecommendations()
    {
        var builder = new PromptBuilder();
        var scope = new ChatAccessScope(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            [],
            Guid.NewGuid(),
            false,
            [DocumentChunkType.Expense],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        var chunk = DocumentChunk.Create(
            scope.TenantId,
            scope.OwnerMembershipId,
            Guid.NewGuid(),
            scope.DepartmentId,
            "Vendor: Bach Hoa Xanh",
            "hash-prompt-recommendation",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var prompt = builder.BuildFullPrompt(
            "Nhà cung cấp này có đáng hợp tác không?",
            [chunk],
            scope,
            []);

        Assert.Contains("Do not make vendor-worthiness", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("what it cannot establish", prompt.System, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFullPrompt_UsesHiddenCitations_AndForbidsChunkLanguage()
    {
        var builder = new PromptBuilder();
        var scope = new ChatAccessScope(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            [],
            Guid.NewGuid(),
            false,
            [DocumentChunkType.Expense],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        var chunk = DocumentChunk.Create(
            scope.TenantId,
            scope.OwnerMembershipId,
            Guid.NewGuid(),
            scope.DepartmentId,
            "Vendor: Bach Hoa Xanh",
            "hash-prompt-citations",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var prompt = builder.BuildFullPrompt(
            "Tổng chi là bao nhiêu?",
            [chunk],
            scope,
            []);

        Assert.Contains("machine-readable", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never mention the word \"chunk\"", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never say \"authorized context\"", prompt.System, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFullPrompt_ForExpenseListing_AddsBusinessFriendlyPresentationRules()
    {
        var builder = new PromptBuilder();
        var scope = new ChatAccessScope(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            [],
            Guid.NewGuid(),
            false,
            [DocumentChunkType.Expense],
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        var chunk = DocumentChunk.Create(
            scope.TenantId,
            scope.OwnerMembershipId,
            Guid.NewGuid(),
            scope.DepartmentId,
            "Expense detail",
            "hash-prompt-listing",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var prompt = builder.BuildFullPrompt(
            "show tất cả expense giúp tôi",
            [chunk],
            scope,
            []);

        Assert.Contains("Presentation requirements for expense or document listings", prompt.User, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not show raw internal identifiers", prompt.User, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use a short numbered list", prompt.User, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If multiple evidence chunks clearly describe the same expense", prompt.User, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatAuthorizationService_GrantsOwnSummaryCapabilities_ToStaff()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membership = new TenantMembershipSummary(
            membershipId,
            Guid.NewGuid(),
            tenantId,
            departmentId,
            RoleType.Staff,
            false,
            true,
            DateTime.UtcNow,
            null,
            null,
            null);

        var membershipRepo = new Mock<ITenantMembershipRepository>();
        membershipRepo
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(false);

        var service = new ChatAuthorizationService(membershipRepo.Object, currentTenant.Object);

        var profile = await service.GetAuthorizationProfileAsync(membershipId, CancellationToken.None);

        Assert.True(profile.Capabilities.CanViewOwnExpenseSummary);
        Assert.True(profile.Capabilities.CanViewOwnExpenseDetails);
        Assert.True(profile.Capabilities.CanViewOwnBudgetLimit);
        Assert.True(profile.Capabilities.CanViewOwnBudgetRemaining);
        Assert.False(profile.Capabilities.CanViewDepartmentExpenseSummary);
        Assert.False(profile.Capabilities.CanViewTenantExpenseSummary);
    }

    [Fact]
    public void ChatAuthorizationProfile_DefensivelyCopiesMutableInputSets()
    {
        var departmentId = Guid.NewGuid();
        var allowedDepartmentIds = new HashSet<Guid> { departmentId };
        var allowedChunkTypes = new HashSet<DocumentChunkType> { DocumentChunkType.Expense };

        var profile = new ChatAuthorizationProfile(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            departmentId,
            allowedDepartmentIds,
            false,
            allowedChunkTypes,
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        allowedDepartmentIds.Add(Guid.NewGuid());
        allowedChunkTypes.Add(DocumentChunkType.Receipt);

        Assert.Single(profile.AllowedDepartmentIds);
        Assert.Single(profile.AllowedChunkTypes);
        Assert.DoesNotContain(DocumentChunkType.Receipt, profile.AllowedChunkTypes);
    }

    [Fact]
    public void ChatAccessScope_DefensivelyCopiesMutableInputSets()
    {
        var departmentId = Guid.NewGuid();
        var permittedDepartmentIds = new HashSet<Guid> { departmentId };
        var allowedChunkTypes = new HashSet<DocumentChunkType> { DocumentChunkType.Expense };

        var scope = new ChatAccessScope(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            departmentId,
            permittedDepartmentIds,
            Guid.NewGuid(),
            false,
            allowedChunkTypes,
            BudgetAccessLevel.LimitOnly,
            ApprovalAccessLevel.OwnOnly);

        permittedDepartmentIds.Add(Guid.NewGuid());
        allowedChunkTypes.Add(DocumentChunkType.Receipt);

        Assert.Single(scope.PermittedDepartmentIds);
        Assert.Single(scope.AllowedChunkTypes);
        Assert.DoesNotContain(DocumentChunkType.Receipt, scope.AllowedChunkTypes);
    }

    [Fact]
    public async Task ChatAuthorizationService_GetChatAccessScopeAsync_PreservesProfileParity_ForManager()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membership = new TenantMembershipSummary(
            membershipId,
            Guid.NewGuid(),
            tenantId,
            departmentId,
            RoleType.Manager,
            false,
            true,
            DateTime.UtcNow,
            null,
            null,
            null);

        var membershipRepo = new Mock<ITenantMembershipRepository>();
        membershipRepo
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(false);

        var service = new ChatAuthorizationService(membershipRepo.Object, currentTenant.Object);

        var profile = await service.GetAuthorizationProfileAsync(membershipId, CancellationToken.None);
        var scope = await service.GetChatAccessScopeAsync(membershipId, CancellationToken.None);

        Assert.Equal(profile.TenantId, scope.TenantId);
        Assert.Equal(profile.TenantName, scope.TenantName);
        Assert.Equal(profile.Role, scope.Role);
        Assert.Equal(profile.MembershipId, scope.OwnerMembershipId);
        Assert.Equal(profile.DepartmentId, scope.DepartmentId);
        Assert.Equal(profile.AllowedDepartmentIds, scope.PermittedDepartmentIds);
        Assert.Equal(profile.AllowedChunkTypes, scope.AllowedChunkTypes);
        Assert.Equal(profile.CanAccessAllTenantData, scope.CanAccessAllTenantData);
        Assert.Equal(BudgetAccessLevel.AggregateSpent, scope.BudgetAccess);
        Assert.Equal(ApprovalAccessLevel.DeptApproval, scope.ApprovalAccess);
    }

    [Fact]
    public async Task ChatAuthorizationService_GetChatAccessScopeAsync_DeniesManagerWithoutDepartment()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membership = new TenantMembershipSummary(
            membershipId,
            Guid.NewGuid(),
            tenantId,
            null,
            RoleType.Manager,
            false,
            true,
            DateTime.UtcNow,
            null,
            null,
            null);

        var membershipRepo = new Mock<ITenantMembershipRepository>();
        membershipRepo
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(false);

        var service = new ChatAuthorizationService(membershipRepo.Object, currentTenant.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetChatAccessScopeAsync(membershipId, CancellationToken.None));

        Assert.Contains("missing a required department boundary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
            _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }

    private static async Task<List<ChatStreamEvent>> CollectStreamEventsAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var item in stream)
            events.Add(item);

        return events;
    }

    private sealed class NonConcurrentVectorStore : IVectorStore
    {
        private readonly IReadOnlyList<DocumentChunk> _chunks;
        private int _activeOperations;

        public NonConcurrentVectorStore(IReadOnlyList<DocumentChunk> chunks)
        {
            _chunks = chunks;
        }

        public Task UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReplaceDocumentChunksAsync(Guid documentId, IEnumerable<DocumentChunk> newChunks, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IReadOnlyList<DocumentChunk>> SearchAsync(
            float[] queryEmbedding,
            Guid tenantId,
            Guid? departmentId,
            Guid? ownerId,
            IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
            int topK = 20,
            CancellationToken ct = default)
        {
            await EnterAsync(ct);
            try
            {
                await Task.Delay(25, ct);
                return _chunks;
            }
            finally
            {
                Exit();
            }
        }

        public async Task<IReadOnlyList<DocumentChunk>> KeywordSearchAsync(
            string query,
            Guid tenantId,
            Guid? departmentId,
            Guid? ownerId,
            IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
            int topK = 20,
            CancellationToken ct = default)
        {
            await EnterAsync(ct);
            try
            {
                await Task.Delay(25, ct);
                return _chunks;
            }
            finally
            {
                Exit();
            }
        }

        private async Task EnterAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _activeOperations) != 1)
            {
                Interlocked.Decrement(ref _activeOperations);
                throw new InvalidOperationException("Concurrent vector store access detected.");
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();
        }

        private void Exit()
        {
            Interlocked.Decrement(ref _activeOperations);
        }
    }
}

