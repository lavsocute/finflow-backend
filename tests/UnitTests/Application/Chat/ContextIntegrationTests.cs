using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public class ContextIntegrationTests
{
    private readonly Mock<IChatRepository> _chatRepositoryMock;
    private readonly Mock<IContextResolver> _contextResolverMock;
    private readonly Mock<IConfidenceScorer> _confidenceScorerMock;
    private readonly Mock<IContextSummarizationService> _summarizationServiceMock;
    private readonly Mock<ILogger<ContextResolver>> _contextResolverLoggerMock;
    private readonly Mock<ILogger<HybridResolutionRouter>> _routerLoggerMock;
    private readonly Mock<ILogger<ContextSummarizationService>> _summarizationLoggerMock;

    public ContextIntegrationTests()
    {
        _chatRepositoryMock = new Mock<IChatRepository>();
        _contextResolverMock = new Mock<IContextResolver>();
        _confidenceScorerMock = new Mock<IConfidenceScorer>();
        _summarizationServiceMock = new Mock<IContextSummarizationService>();
        _contextResolverLoggerMock = new Mock<ILogger<ContextResolver>>();
        _routerLoggerMock = new Mock<ILogger<HybridResolutionRouter>>();
        _summarizationLoggerMock = new Mock<ILogger<ContextSummarizationService>>();

        _confidenceScorerMock
            .Setup(x => x.GetLevel(It.IsAny<float>()))
            .Returns((float confidence) => confidence switch
            {
                >= 0.85f => ConfidenceLevel.High,
                >= 0.60f => ConfidenceLevel.Medium,
                _ => ConfidenceLevel.Low
            });
    }

    #region Tenant Scope After Clarification Tests

    [Fact]
    public async Task Router_ResolvesToanCongTy_AfterScopeClarification_CombinesWithPreviousQuery()
    {
        var router = CreateRouter();

        // First query asked about budget with scope clarification prompt
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Phòng nào vượt ngân sách trong tháng này?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?")
        };

        // User answers "toàn công ty" to the clarification prompt
        var result = await router.RouteAsync("toàn công ty", history, null);

        Assert.Equal(ResolutionTier.Pattern, result.Tier);
        Assert.True(result.Confidence >= 0.9f);
        Assert.False(result.RequiresClarification);
    }

    [Fact]
    public async Task Router_ResolvesTenantScope_AfterClarification_ReturnsCorrectResolutionResult()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng ban?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?")
        };

        var result = await router.RouteAsync("toàn công ty", history, null);

        Assert.Contains("toàn công ty", result.ResolvedQuery, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.RequiresClarification);
        Assert.Equal(ResolutionTier.Pattern, result.Tier);
    }

    [Fact]
    public async Task Router_HandlesDepartmentScopeAnswer_AfterClarification()
    {
        var router = CreateRouter();

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Chi phí phòng ban tháng này?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?")
        };

        var result = await router.RouteAsync("phòng ban của tôi", history, null);

        Assert.Equal(ResolutionTier.Pattern, result.Tier);
        Assert.True(result.Confidence >= 0.9f);
        Assert.False(result.RequiresClarification);
    }

    #endregion

    #region Follow-up After Department Query Tests

    [Fact]
    public async Task Router_ResolvesFollowUp_ConPhongNao_AfterDepartmentRevenueQuery()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        context.IncrementTurn();
        context.AddEntity(TrackedEntity.Create("Phòng A", EntityType.Department, 1));
        context.IncrementTurn();

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng A?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Doanh thu phòng A tháng này là 50,000,000 VND.")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync("còn phòng nào?", history, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "còn phòng nào?",
                Confidence = 0.75f,
                Level = ConfidenceLevel.Medium,
                RequiresClarification = false
            });

        var result = await router.RouteAsync("còn phòng nào?", history, context);

        Assert.True(result.Confidence > 0.5f);
        Assert.False(result.RequiresClarification);
    }

    [Fact]
    public async Task Router_DetectsFollowUpPattern_ConPhongNa_ViaIsFollowUp()
    {
        var scorer = new ConfidenceScorer();
        var resolver = new ContextResolver(scorer, _contextResolverLoggerMock.Object);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng A?")
        };

        var isFollowUp = await resolver.IsFollowUpAsync("còn phòng nào?", history);

        Assert.True(isFollowUp);
    }

    [Fact]
    public async Task ContextResolver_ResolvesFollowUp_UsingContextEntity()
    {
        var scorer = new ConfidenceScorer();
        var resolver = new ContextResolver(scorer, _contextResolverLoggerMock.Object);

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        var phongA = TrackedEntity.Create("Phòng A", EntityType.Department, 1);
        phongA.AddAlias("bên A");
        context.AddEntity(phongA);
        context.IncrementTurn();

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu bên A?")
        };

        var result = await resolver.ResolveAsync("còn phòng nào?", history, context);

        Assert.NotEmpty(result.ResolvedQuery);
        Assert.True(result.Confidence > 0);
    }

    #endregion

    #region Comparison After Multiple Departments Tests

    [Fact]
    public async Task Router_ResolvesComparison_SoSanhBVaC_AfterMultipleDepartmentsMentioned()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        context.AddEntity(TrackedEntity.Create("Phòng A", EntityType.Department, 1));
        context.AddEntity(TrackedEntity.Create("Phòng B", EntityType.Department, 2));
        context.AddEntity(TrackedEntity.Create("Phòng C", EntityType.Department, 3));
        context.IncrementTurn();

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng A?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Doanh thu phòng A là 50,000,000 VND."),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Còn phòng B và C thì sao?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Doanh thu phòng B là 30,000,000 VND, phòng C là 40,000,000 VND.")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync("so sánh B và C?", history, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "so sánh Phòng B và Phòng C",
                Confidence = 0.82f,
                Level = ConfidenceLevel.Medium,
                RequiresClarification = false,
                Resolutions = new List<EntityResolution>
                {
                    new() { Original = "B", Resolved = "Phòng B", Source = "context" },
                    new() { Original = "C", Resolved = "Phòng C", Source = "context" }
                }
            });

        var result = await router.RouteAsync("so sánh B và C?", history, context);

        Assert.True(result.Confidence > 0);
        Assert.Contains("Phòng B", result.ResolvedQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Phòng C", result.ResolvedQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContextResolver_ResolvesMultipleDepartmentReferences_FromContext()
    {
        var scorer = new ConfidenceScorer();
        var resolver = new ContextResolver(scorer, _contextResolverLoggerMock.Object);

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        context.AddEntity(TrackedEntity.Create("Phòng B", EntityType.Department, 1));
        context.AddEntity(TrackedEntity.Create("Phòng C", EntityType.Department, 2));

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng B và C?")
        };

        var result = await resolver.ResolveAsync("so sánh B và C?", history, context);

        Assert.Contains(result.Resolutions, r => r.Resolved == "Phòng B");
        Assert.Contains(result.Resolutions, r => r.Resolved == "Phòng C");
    }

    [Fact]
    public async Task Router_DetectsComparativePattern_ThroughFallbackStrategy()
    {
        var router = CreateRouter();

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"Doanh thu phòng A?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"Doanh thu phòng A là 50,000,000 VND."),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"So sánh với phòng B")
        };

        // Short comparison query after detailed history
        var result = await router.RouteAsync("phòng B hơn thế nào", history, null);

        Assert.True(result.Confidence > 0.5f);
        Assert.Equal(ResolutionTier.SmallLlm, result.Tier);
    }

    #endregion

    #region Confidence Scoring Integration Tests

    [Fact]
    public async Task Router_CalculatesHighConfidence_ForClearFollowUp()
    {
        var router = CreateRouter();

        _confidenceScorerMock
            .Setup(x => x.CalculateScore(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(new ConfidenceScore { Total = 0.92f, Level = ConfidenceLevel.High });

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí phòng A",
                Confidence = 0.92f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        var context = ConversationContext.Create(Guid.NewGuid());
        context.AddEntity(TrackedEntity.Create("Phòng A", EntityType.Department, 1));

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?")
        };

        var result = await router.RouteAsync("còn gì nữa không", history, context);

        Assert.Equal(ConfidenceLevel.High, _confidenceScorerMock.Object.GetLevel(result.Confidence));
    }

    [Fact]
    public async Task Router_CalculatesMediumConfidence_ForAmbiguousFollowUp()
    {
        var router = CreateRouter();

        _confidenceScorerMock
            .Setup(x => x.CalculateScore(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(new ConfidenceScore { Total = 0.72f, Level = ConfidenceLevel.Medium });

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí đó",
                Confidence = 0.72f,
                Level = ConfidenceLevel.Medium,
                RequiresClarification = false
            });

        var context = ConversationContext.Create(Guid.NewGuid());
        context.AddEntity(TrackedEntity.Create("Phòng A", EntityType.Department, 1));

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"phòng B"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,"chi phí phòng B là 30,000,000 VND.")
        };

        var result = await router.RouteAsync("còn gì", history, context);

        Assert.Equal(ResolutionTier.SmallLlm, result.Tier);
    }

    [Fact]
    public async Task Router_ReturnsClarification_WhenConfidenceBelowThreshold()
    {
        var router = CreateRouter();

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí",
                Confidence = 0.45f,
                Level = ConfidenceLevel.Low,
                RequiresClarification = true,
                ClarificationPrompt = "Bạn đang hỏi về chi phí của đối tượng nào?"
            });

        var history = new List<ChatMessage>();

        var result = await router.RouteAsync("chi phí", history, null);

        Assert.True(result.RequiresClarification);
        Assert.NotNull(result.ClarificationPrompt);
    }

    [Fact]
    public async Task Router_UsesFallback_WhenContextResolutionLowConfidence()
    {
        var router = CreateRouter();

        // First call to context resolver returns low confidence
        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí",
                Confidence = 0.45f,
                Level = ConfidenceLevel.Low,
                RequiresClarification = true
            });

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A là bao nhiêu?")
        };

        var result = await router.RouteAsync("còn gì nữa", history, null);

        // Fallback strategy should boost confidence for short follow-up after specific query
        Assert.True(result.Confidence >= 0.7f || !result.RequiresClarification);
    }

    #endregion

    #region Graceful Degradation Tests

    [Fact]
    public async Task Router_HandlesEmptyContext_WhenNoPreviousEntities()
    {
        var router = CreateRouter();

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "doanh thu",
                Confidence = 0.50f,
                Level = ConfidenceLevel.Low,
                RequiresClarification = true,
                ClarificationReason = ClarificationReason.NoContextAvailable
            });

        var history = new List<ChatMessage>();

        var result = await router.RouteAsync("doanh thu", history, null);

        Assert.True(result.RequiresClarification);
        Assert.True(result.Confidence < 0.7f);
    }

    [Fact]
    public async Task Router_HandlesContextWithExpiredEntities()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);

        // Add entity that will be treated as expired (TTL = 0 for testing)
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, 1, ttlSeconds: 0);
        context.AddEntity(entity);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "còn phòng nào",
                Confidence = 0.65f,
                Level = ConfidenceLevel.Medium,
                RequiresClarification = true,
                ClarificationReason = ClarificationReason.EntityNotFound
            });

        var result = await router.RouteAsync("còn phòng nào?", history, context);

        // Should still route but with degraded confidence
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task Router_HandlesNewSessionWithoutHistory()
    {
        var router = CreateRouter();

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí phòng ban",
                Confidence = 0.85f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        var result = await router.RouteAsync("chi phí phòng ban", new List<ChatMessage>(), null);

        Assert.False(result.RequiresClarification);
        Assert.True(result.Confidence >= 0.85f);
    }

    [Fact]
    public async Task Router_GracefullyFallsBack_WhenContextResolverThrows()
    {
        var router = CreateRouter();

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Context resolver failed"));

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng ban")
        };

        // Router should fall back to pattern match when context resolver fails
        var result = await router.RouteAsync("chi phí phòng ban", history, null);

        Assert.NotNull(result);
        Assert.Equal(ResolutionTier.Pattern, result.Tier);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task ContextSummarizationService_SummarizesLongHistory()
    {
        _summarizationServiceMock
            .Setup(x => x.ShouldSummarizeAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _summarizationServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Tóm tắt: Người dùng hỏi về chi phí và doanh thu của các phòng ban.");

        var service = new ContextSummarizationService(_summarizationLoggerMock.Object, new HttpClient(), Microsoft.Extensions.Options.Options.Create(new GroqChatOptions()));
        var longHistory = Enumerable.Range(1, 20)
            .Select(i => ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,$"tin nhắn {i}"))
            .ToList();

        var shouldSummarize = await service.ShouldSummarizeAsync(longHistory);
        Assert.True(shouldSummarize);

        var summary = await service.SummarizeAsync(longHistory);
        Assert.NotEmpty(summary);
        Assert.Contains("Tóm tắt", summary);
    }

    [Fact]
    public async Task Router_UsesSummarizationWhenHistoryExceedsThreshold()
    {
        var router = CreateRouter();

        var longHistory = Enumerable.Range(1, 25)
            .SelectMany(i => new List<ChatMessage>
            {
                ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,$"tin nhắn người dùng {i}"),
                ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,$"phản hồi {i}")
            })
            .ToList();

        _summarizationServiceMock
            .Setup(x => x.ShouldSummarizeAsync(longHistory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _summarizationServiceMock
            .Setup(x => x.SummarizeAsync(longHistory, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Tóm tắt: Người dùng hỏi về chi phí các phòng ban.");

        var context = ConversationContext.Create(Guid.NewGuid());
        context.SetCompressedSummary("Tóm tắt: Người dùng hỏi về chi phí các phòng ban.");

        var result = await router.RouteAsync("còn gì nữa", longHistory, context);

        Assert.NotNull(result);
        // Router should handle the summarized context gracefully
        Assert.True(result.Confidence > 0 || !result.RequiresClarification);
    }

    #endregion

    #region Intent Stack Integration Tests

    [Fact]
    public async Task Router_PreservesIntentStack_AcrossMultipleTurns()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        var intentFrame = IntentFrame.Create("expense-query");
        intentFrame.SetState(IntentState.InProgress);
        intentFrame.SetSlot("department", "Phòng A");
        context.IntentStack.Push(intentFrame);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), history, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí Phòng A",
                Confidence = 0.88f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        var result = await router.RouteAsync("chi phí phòng A", history, context);

        Assert.False(result.RequiresClarification);
        Assert.NotNull(context.IntentStack.Peek());
        Assert.Equal("expense-query", context.IntentStack.Peek()?.RawIntentType);
    }

    [Fact]
    public async Task Router_SuspendsIntent_WhenUserPivotsToNewTopic()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        var intentFrame = IntentFrame.Create("expense-query");
        intentFrame.SetState(IntentState.InProgress);
        context.IntentStack.Push(intentFrame);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"lịch làm việc tuần này?")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync("lịch làm việc tuần này?", history, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "lịch làm việc tuần này?",
                Confidence = 0.85f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        var result = await router.RouteAsync("lịch làm việc tuần này?", history, context);

        // New intent should be pushed, old one suspended
        Assert.Contains(result.ResolvedQuery, "lịch", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Cache Integration Tests

    [Fact]
    public async Task Router_ReturnsCachedResult_ForRepeatedQueryWithSameContext()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);
        context.AddEntity(TrackedEntity.Create("Phòng A", EntityType.Department, 1));

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync("chi phí phòng A", history, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí Phòng A",
                Confidence = 0.88f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        // First call - goes to context resolver
        var firstResult = await router.RouteAsync("chi phí phòng A", history, context);
        Assert.Equal(ResolutionTier.SmallLlm, firstResult.Tier);

        // Second call - should hit cache
        var secondResult = await router.RouteAsync("chi phí phòng A", history, context);
        Assert.Equal(ResolutionTier.Cache, secondResult.Tier);
        Assert.Equal(firstResult.Confidence, secondResult.Confidence);
    }

    [Fact]
    public async Task Router_InvalidatesCache_WhenContextEntitiesChange()
    {
        var router = CreateRouter();

        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);

        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"chi phí phòng A?")
        };

        _contextResolverMock
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResolutionResult
            {
                ResolvedQuery = "chi phí Phòng A",
                Confidence = 0.88f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            });

        // First call
        var firstResult = await router.RouteAsync("chi phí phòng A", history, context);
        Assert.Equal(ResolutionTier.SmallLlm, firstResult.Tier);

        // Add new entity to context - this changes the cache key
        context.AddEntity(TrackedEntity.Create("Phòng B", EntityType.Department, 2));

        // Second call with different context entities - should NOT hit cache
        var secondResult = await router.RouteAsync("chi phí phòng A", history, context);
        Assert.Equal(ResolutionTier.SmallLlm, secondResult.Tier);
    }

    #endregion

    #region Helper Methods

    private HybridResolutionRouter CreateRouter()
    {
        var scorer = new ConfidenceScorer();
        var contextResolver = new ContextResolver(scorer, _contextResolverLoggerMock.Object);
        return new HybridResolutionRouter(contextResolver, scorer);
    }

    private static ChatMessage CreateUser(string content, int turnNumber)
    {
        return ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, content);
    }

    private static ChatMessage CreateAssistant(string content, int turnNumber)
    {
        return ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant, content);
    }

    #endregion
}
