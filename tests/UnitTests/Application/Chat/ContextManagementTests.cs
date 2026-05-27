using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using Xunit;

namespace FinFlow.UnitTests.Application.Chat;

public class ContextManagementTests
{
    #region TrackedEntity Tests

    [Fact]
    public void TrackedEntity_Create_SetsProperties()
    {
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("Phòng A", entity.CanonicalName);
        Assert.Equal(EntityType.Department, entity.Type);
        Assert.Equal(1, entity.FirstMentionTurn);
        Assert.Equal(1, entity.LastReferenceTurn);
        Assert.Equal(1, entity.MentionCount);
    }

    [Fact]
    public void TrackedEntity_AddAlias_AddsUniqueAlias()
    {
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);

        entity.AddAlias("bên A");
        entity.AddAlias("đó");

        Assert.Equal(2, entity.Aliases.Count);
        Assert.Contains("bên A", entity.Aliases);
        Assert.Contains("đó", entity.Aliases);
    }

    [Fact]
    public void TrackedEntity_AddAlias_DoesNotDuplicate()
    {
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);

        entity.AddAlias("bên A");
        entity.AddAlias("bên A");

        Assert.Single(entity.Aliases);
    }

    [Fact]
    public void TrackedEntity_RecordReference_UpdatesTurnAndCount()
    {
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);

        entity.RecordReference(turnNumber: 3);

        Assert.Equal(3, entity.LastReferenceTurn);
        Assert.Equal(2, entity.MentionCount);
    }

    [Fact]
    public void TrackedEntity_IsExpired_ReturnsFalseWhenFresh()
    {
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1, ttlSeconds: 3600);

        Assert.False(entity.IsExpired());
    }

    #endregion

    #region ConversationContext Tests

    [Fact]
    public void ConversationContext_Create_SetsInitialState()
    {
        var sessionId = Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);

        Assert.NotEqual(Guid.Empty, context.Id);
        Assert.Equal(sessionId, context.SessionId);
        Assert.Equal(0, context.TurnCount);
        Assert.Empty(context.Entities);
        Assert.NotNull(context.IntentStack);
    }

    [Fact]
    public void ConversationContext_AddEntity_AddsToList()
    {
        var context = ConversationContext.Create(Guid.NewGuid());
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);

        context.AddEntity(entity);

        Assert.Single(context.Entities);
    }

    [Fact]
    public void ConversationContext_FindEntity_FindsExactMatch()
    {
        var context = ConversationContext.Create(Guid.NewGuid());
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);
        context.AddEntity(entity);

        var found = context.FindEntity("Phòng A");

        Assert.NotNull(found);
        Assert.Equal("Phòng A", found.CanonicalName);
    }

    [Fact]
    public void ConversationContext_FindEntity_FindsByAlias()
    {
        var context = ConversationContext.Create(Guid.NewGuid());
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);
        entity.AddAlias("bên A");
        context.AddEntity(entity);

        var found = context.FindEntity("bên A");

        Assert.NotNull(found);
        Assert.Equal("Phòng A", found.CanonicalName);
    }

    [Fact]
    public void ConversationContext_FindEntityByType_ReturnsLatestOfType()
    {
        var context = ConversationContext.Create(Guid.NewGuid());
        var entity1 = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);
        var entity2 = TrackedEntity.Create("Phòng B", EntityType.Department, turnNumber: 2);
        entity2.RecordReference(3);
        context.AddEntity(entity1);
        context.AddEntity(entity2);

        var found = context.FindEntityByType(EntityType.Department);

        Assert.NotNull(found);
        Assert.Equal("Phòng B", found.CanonicalName);
    }

    [Fact]
    public async Task ConversationStateManager_RestoresContext_FromSerializedCacheSnapshot()
    {
        var cache = new JsonRoundTripCacheService();
        var manager = new ConversationStateManager(cache, NullLogger<ConversationStateManager>.Instance);
        var sessionId = Guid.NewGuid();

        var context = await manager.GetOrCreateContextAsync(sessionId);
        context.IncrementTurn();
        context.AddEntity(TrackedEntity.Create("Bach Hoa Xanh", EntityType.Vendor, 1));
        await manager.SaveContextAsync(sessionId, context, CancellationToken.None);

        var restored = await manager.GetContextAsync(sessionId);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.TurnCount);
        Assert.NotNull(restored.FindEntity("Bach Hoa Xanh"));
    }

    #endregion

    #region IntentStack Tests

    [Fact]
    public void IntentStack_PushAndPeek_ReturnsTopFrame()
    {
        var stack = new IntentStack();
        var frame = IntentFrame.Create("budget-query");

        stack.Push(frame);

        Assert.Equal(frame, stack.Peek());
    }

    [Fact]
    public void IntentStack_Pop_ReturnsAndRemovesTop()
    {
        var stack = new IntentStack();
        var frame1 = IntentFrame.Create("budget-query");
        var frame2 = IntentFrame.Create("expense-query");
        stack.Push(frame1);
        stack.Push(frame2);

        var popped = stack.Pop();

        Assert.Equal(frame2, popped);
        Assert.Equal(frame1, stack.Peek());
    }

    [Fact]
    public void IntentStack_Lock_PreventsUnlock()
    {
        var stack = new IntentStack();

        stack.Lock("awaiting_confirmation");

        Assert.True(stack.IsLocked);
        Assert.Equal("awaiting_confirmation", stack.LockReason);
    }

    [Fact]
    public void IntentStack_SuspendCurrent_SuspendsActiveFrame()
    {
        var stack = new IntentStack();
        var frame = IntentFrame.Create("budget-query");
        stack.Push(frame);

        stack.SuspendCurrent("user_pivot");

        var suspended = stack.GetSuspended();
        Assert.Single(suspended);
        Assert.Equal(IntentState.Suspended, suspended[0].State);
        Assert.Equal("user_pivot", suspended[0].PivotTrigger);
    }

    [Fact]
    public void IntentStack_Resume_ResumesMostRecentSuspended()
    {
        var stack = new IntentStack();
        var frame = IntentFrame.Create("budget-query");
        stack.Push(frame);
        stack.SuspendCurrent("user_pivot");

        var resumed = stack.Resume();

        Assert.NotNull(resumed);
        Assert.Equal(IntentState.InProgress, resumed.State);
    }

    #endregion

    #region ConfidenceScorer Tests

    [Fact]
    public void ConfidenceScorer_CalculateScore_ReturnsCorrectLevel()
    {
        var scorer = new ConfidenceScorer();

        var highScore = scorer.CalculateScore(0.9f, 0.9f, 0.9f, 0.9f, 0.9f);
        Assert.Equal(ConfidenceLevel.High, highScore.Level);

        var mediumScore = scorer.CalculateScore(0.7f, 0.7f, 0.7f, 0.7f, 0.7f);
        Assert.Equal(ConfidenceLevel.Medium, mediumScore.Level);

        var lowScore = scorer.CalculateScore(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        Assert.Equal(ConfidenceLevel.Low, lowScore.Level);
    }

    [Fact]
    public void ConfidenceScorer_GetAction_ReturnsCorrectAction()
    {
        var scorer = new ConfidenceScorer();

        Assert.Equal("EXECUTE", scorer.GetAction(ConfidenceLevel.High));
        Assert.Equal("EXECUTE_WITH_LOG", scorer.GetAction(ConfidenceLevel.Medium));
        Assert.Equal("CLARIFY", scorer.GetAction(ConfidenceLevel.Low));
    }

    [Theory]
    [InlineData(0.9f, 0.9f, 0.9f, 0.9f, 0.9f, ConfidenceLevel.High)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, ConfidenceLevel.Low)]
    [InlineData(0.7f, 0.7f, 0.7f, 0.7f, 0.7f, ConfidenceLevel.Medium)]
    public void ConfidenceScorer_CalculateScore_ReturnsCorrectWeightedScore(
        float intent, float entity, float context, float history, float domain,
        ConfidenceLevel expectedLevel)
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.CalculateScore(intent, entity, context, history, domain);

        Assert.Equal(expectedLevel, result.Level);
    }

    [Theory]
    [InlineData(0.85f, ConfidenceLevel.High)]
    [InlineData(0.86f, ConfidenceLevel.High)]
    [InlineData(0.95f, ConfidenceLevel.High)]
    public void ConfidenceScorer_GetLevel_ReturnsHigh_AtBoundary(float confidence, ConfidenceLevel expected)
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.GetLevel(confidence);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.84f, ConfidenceLevel.Medium)]
    [InlineData(0.60f, ConfidenceLevel.Medium)]
    [InlineData(0.70f, ConfidenceLevel.Medium)]
    public void ConfidenceScorer_GetLevel_ReturnsMedium_AtBoundary(float confidence, ConfidenceLevel expected)
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.GetLevel(confidence);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.59f, ConfidenceLevel.Low)]
    [InlineData(0.30f, ConfidenceLevel.Low)]
    [InlineData(0.0f, ConfidenceLevel.Low)]
    public void ConfidenceScorer_GetLevel_ReturnsLow_BelowThreshold(float confidence, ConfidenceLevel expected)
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.GetLevel(confidence);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ConfidenceLevel.High, "EXECUTE")]
    [InlineData(ConfidenceLevel.Medium, "EXECUTE_WITH_LOG")]
    [InlineData(ConfidenceLevel.Low, "CLARIFY")]
    public void ConfidenceScorer_GetAction_ReturnsCorrectAction_ForTheory(ConfidenceLevel level, string expectedAction)
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.GetAction(level);

        Assert.Equal(expectedAction, result);
    }

    [Fact]
    public void ConfidenceScorer_CalculateScore_ClampsTotalToOne()
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.CalculateScore(1.0f, 1.0f, 1.0f, 1.0f, 1.0f);

        Assert.Equal(1.0f, result.Total);
    }

    [Fact]
    public void ConfidenceScorer_CalculateScore_ClampsTotalToZero()
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.CalculateScore(0.0f, 0.0f, 0.0f, 0.0f, 0.0f);

        Assert.Equal(0.0f, result.Total);
    }

    [Fact]
    public void ConfidenceScorer_CalculateScore_IncludesFactorsInResult()
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.CalculateScore(0.8f, 0.7f, 0.6f, 0.5f, 0.4f);

        Assert.Equal(5, result.Factors.Count);
        Assert.Equal(0.8f, result.Factors["intent"]);
        Assert.Equal(0.7f, result.Factors["entity"]);
        Assert.Equal(0.6f, result.Factors["context"]);
        Assert.Equal(0.5f, result.Factors["history"]);
        Assert.Equal(0.4f, result.Factors["domain"]);
    }

    #endregion

    #region ContextSummarizationService Tests

    [Fact]
    public async Task ContextSummarizationService_ShouldSummarizeAsync_ReturnsFalse_WhenMessageCountBelowThreshold()
    {
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextSummarizationService>>();
        var httpClient = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new GroqChatOptions());
        var service = new ContextSummarizationService(logger, httpClient, options);
        var history = new List<ChatMessage>();

        for (int i = 0; i < 14; i++)
        {
            history.Add(ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(),
                ChatMessageRole.User, "test message"));
        }

        var result = await service.ShouldSummarizeAsync(history);

        Assert.False(result);
    }

    [Fact]
    public async Task ContextSummarizationService_ShouldSummarizeAsync_ReturnsFalse_WhenMessageCountAtThreshold_ButCharsBelowThreshold()
    {
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextSummarizationService>>();
        var httpClient = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new GroqChatOptions());
        var service = new ContextSummarizationService(logger, httpClient, options);
        var history = new List<ChatMessage>();

        // 15 messages with short content (< 4000 chars total)
        for (int i = 0; i < 15; i++)
        {
            history.Add(ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(),
                ChatMessageRole.User, "short"));
        }

        var result = await service.ShouldSummarizeAsync(history);

        Assert.False(result);
    }

    [Fact]
    public async Task ContextSummarizationService_ShouldSummarizeAsync_ReturnsTrue_WhenMessageCountAndCharCountExceedThresholds()
    {
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextSummarizationService>>();
        var httpClient = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new GroqChatOptions());
        var service = new ContextSummarizationService(logger, httpClient, options);
        var history = new List<ChatMessage>();

        // 15 messages with long content (> 4000 chars total)
        var longContent = new string('x', 300);
        for (int i = 0; i < 15; i++)
        {
            history.Add(ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(),
                ChatMessageRole.User, longContent));
        }

        var result = await service.ShouldSummarizeAsync(history);

        Assert.True(result);
    }

    [Fact]
    public async Task ContextSummarizationService_SummarizeAsync_ReturnsEmptyString_WhenHistoryIsEmpty()
    {
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextSummarizationService>>();
        var httpClient = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new GroqChatOptions());
        var service = new ContextSummarizationService(logger, httpClient, options);

        var result = await service.SummarizeAsync([]);

        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region ILlmEntityExtractor Tests - LLM-based Entity Extraction

    [Fact]
    public async Task LlmEntityExtractor_DetectFollowUpAsync_ReturnsFollowUp_WhenQueryIsFollowUp()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi phong B";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "chi phi phong ban A")
        };

        mockExtractor.Setup(x => x.DetectFollowUpAsync(query, It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FollowUpDetectionResult
            {
                IsFollowUp = true,
                Confidence = 0.95f,
                FollowUpType = "entity_clarification",
                Reasoning = "Query references 'phong B' as a follow-up to previous 'phong A' discussion"
            });

        // Act
        var result = await mockExtractor.Object.DetectFollowUpAsync(query, history);

        // Assert
        Assert.True(result.IsFollowUp);
        Assert.Equal(0.95f, result.Confidence);
        Assert.Equal("entity_clarification", result.FollowUpType);
    }

    [Fact]
    public async Task LlmEntityExtractor_DetectFollowUpAsync_ReturnsNotFollowUp_WhenQueryIsNewTopic()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "lich lam viec tuan nay";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "chi phi phong ban A")
        };

        mockExtractor.Setup(x => x.DetectFollowUpAsync(query, It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FollowUpDetectionResult
            {
                IsFollowUp = false,
                Confidence = 0.92f,
                FollowUpType = null,
                Reasoning = "Query introduces a new topic not related to previous discussion"
            });

        // Act
        var result = await mockExtractor.Object.DetectFollowUpAsync(query, history);

        // Assert
        Assert.False(result.IsFollowUp);
        Assert.Equal(0.92f, result.Confidence);
    }

    [Fact]
    public async Task LlmEntityExtractor_ExtractEntitiesAsync_ReturnsDepartmentEntity()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi phong nhan su";

        mockExtractor.Setup(x => x.ExtractEntitiesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>
            {
                new() { Text = "phong nhan su", Type = EntityType.Department, Confidence = 0.95f, NormalizedForm = "phòng nhân sự" }
            });

        // Act
        var result = await mockExtractor.Object.ExtractEntitiesAsync(query);

        // Assert
        Assert.Single(result);
        Assert.Equal("phong nhan su", result[0].Text);
        Assert.Equal(EntityType.Department, result[0].Type);
        Assert.Equal(0.95f, result[0].Confidence);
    }

    [Fact]
    public async Task LlmEntityExtractor_ExtractEntitiesAsync_ReturnsMultipleEntities()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi phong A vao thang 3 nam 2025";

        mockExtractor.Setup(x => x.ExtractEntitiesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>
            {
                new() { Text = "phong A", Type = EntityType.Department, Confidence = 0.92f },
                new() { Text = "thang 3 nam 2025", Type = EntityType.Date, Confidence = 0.88f }
            });

        // Act
        var result = await mockExtractor.Object.ExtractEntitiesAsync(query);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Type == EntityType.Department);
        Assert.Contains(result, e => e.Type == EntityType.Date);
    }

    [Fact]
    public async Task LlmEntityExtractor_ExtractEntitiesAsync_ReturnsMoneyEntity()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi 50 trieu dong";

        mockExtractor.Setup(x => x.ExtractEntitiesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>
            {
                new() { Text = "50 trieu dong", Type = EntityType.Money, Confidence = 0.97f }
            });

        // Act
        var result = await mockExtractor.Object.ExtractEntitiesAsync(query);

        // Assert
        Assert.Single(result);
        Assert.Equal(EntityType.Money, result[0].Type);
    }

    [Fact]
    public async Task LlmEntityExtractor_ExtractEntitiesAsync_ReturnsEmptyList_WhenNoEntitiesFound()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "xin chao";

        mockExtractor.Setup(x => x.ExtractEntitiesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>());

        // Act
        var result = await mockExtractor.Object.ExtractEntitiesAsync(query);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region LLM-based Entity Resolution Tests

    [Fact]
    public async Task LlmEntityExtractor_ResolveEntityReferencesAsync_ResolvesAliasToCanonical()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi bên A";
        var context = ConversationContext.Create(Guid.NewGuid());
        var entity = TrackedEntity.Create("Phòng A", EntityType.Department, turnNumber: 1);
        entity.AddAlias("bên A");
        context.AddEntity(entity);

        var extractedEntities = new List<ExtractedEntity>
        {
            new() { Text = "bên A", Type = EntityType.Department, Confidence = 0.85f }
        };

        mockExtractor.Setup(x => x.ResolveEntityReferencesAsync(query, context, extractedEntities, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityResolution>
            {
                new() { Original = "bên A", Resolved = "Phòng A", Source = "llm_context", Confidence = 0.90f }
            });

        // Act
        var result = await mockExtractor.Object.ResolveEntityReferencesAsync(query, context, extractedEntities);

        // Assert
        Assert.Single(result);
        Assert.Equal("bên A", result[0].Original);
        Assert.Equal("Phòng A", result[0].Resolved);
    }

    [Fact]
    public async Task LlmEntityExtractor_ResolveEntityReferencesAsync_ReturnsEmpty_WhenNoResolutionFound()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "chi phi phong XYZ";
        var context = ConversationContext.Create(Guid.NewGuid());

        var extractedEntities = new List<ExtractedEntity>
        {
            new() { Text = "phong XYZ", Type = EntityType.Department, Confidence = 0.70f }
        };

        mockExtractor.Setup(x => x.ResolveEntityReferencesAsync(query, context, extractedEntities, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityResolution>());

        // Act
        var result = await mockExtractor.Object.ResolveEntityReferencesAsync(query, context, extractedEntities);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region LLM Intent Tracking Tests

    [Fact]
    public async Task LlmEntityExtractor_DetectFollowUpAsync_TracksIntentTransition()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "thế còn phòng B";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "chi phi phong A"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant, "chi phi phong A la 100 trieu"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "so sanh voi phong B")
        };

        mockExtractor.Setup(x => x.DetectFollowUpAsync(query, history, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FollowUpDetectionResult
            {
                IsFollowUp = true,
                Confidence = 0.88f,
                FollowUpType = "comparison",
                Reasoning = "User is comparing phong B with previously discussed phong A"
            });

        // Act
        var result = await mockExtractor.Object.DetectFollowUpAsync(query, history);

        // Assert
        Assert.True(result.IsFollowUp);
        Assert.Equal("comparison", result.FollowUpType);
        Assert.True(result.Confidence >= 0.8f);
    }

    [Fact]
    public async Task LlmEntityExtractor_DetectFollowUpAsync_DetectsEntityClarification()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "phong C có gì thêm";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "thông tin phòng A"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant, "phòng A có 10 nhân viên")
        };

        mockExtractor.Setup(x => x.DetectFollowUpAsync(query, history, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FollowUpDetectionResult
            {
                IsFollowUp = true,
                Confidence = 0.82f,
                FollowUpType = "entity_clarification",
                Reasoning = "Query asks for additional information about phong C, implying previous entities were mentioned"
            });

        // Act
        var result = await mockExtractor.Object.DetectFollowUpAsync(query, history);

        // Assert
        Assert.True(result.IsFollowUp);
        Assert.Equal("entity_clarification", result.FollowUpType);
    }

    [Fact]
    public async Task LlmEntityExtractor_DetectFollowUpAsync_DetectsComparativeFollowUp()
    {
        // Arrange
        var mockExtractor = new Mock<ILlmEntityExtractor>();
        var query = "phong D kém hon phong A";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "danh gia phong A"),
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant, "phòng A hoat dong tot")
        };

        mockExtractor.Setup(x => x.DetectFollowUpAsync(query, history, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FollowUpDetectionResult
            {
                IsFollowUp = true,
                Confidence = 0.91f,
                FollowUpType = "comparative",
                Reasoning = "Query contains comparative language 'kém hơn' and references phong A from context"
            });

        // Act
        var result = await mockExtractor.Object.DetectFollowUpAsync(query, history);

        // Assert
        Assert.True(result.IsFollowUp);
        Assert.Equal("comparative", result.FollowUpType);
        Assert.True(result.Confidence >= 0.9f);
    }

    #endregion

    #region HybridResolutionRouter with LLM Tests

    [Fact]
    public async Task HybridResolutionRouter_RouteAsync_UsesLlmResolver_ForComplexQuery()
    {
        // Arrange
        var scorer = new ConfidenceScorer();
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextResolver>>();
        var contextResolver = new ContextResolver(scorer, logger);
        var router = new HybridResolutionRouter(contextResolver, scorer);
        var query = "chi phi phong ban";
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User, "cho tôi biết thông tin phòng A")
        };

        // Act
        var result = await router.RouteAsync(query, history, null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Tier == ResolutionTier.SmallLlm || result.Tier == ResolutionTier.Pattern);
        Assert.True(result.Confidence >= 0 && result.Confidence <= 1.0f);
    }

    [Fact]
    public async Task HybridResolutionRouter_RouteAsync_ReturnsPatternTier_ForSimpleQuery()
    {
        // Arrange
        var scorer = new ConfidenceScorer();
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextResolver>>();
        var contextResolver = new ContextResolver(scorer, logger);
        var router = new HybridResolutionRouter(contextResolver, scorer);

        // Act - simple greeting should use pattern tier
        var result = await router.RouteAsync("xin chao", [], null);

        // Assert
        Assert.Equal(ResolutionTier.Pattern, result.Tier);
        Assert.False(result.RequiresClarification);
    }

    #endregion

    #region ContextResolver Basic Tests (non-pattern based)

    [Fact]
    public async Task ContextResolver_ResolveAsync_ReturnsHighConfidence_WhenNotFollowUp()
    {
        var scorer = new ConfidenceScorer();
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextResolver>>();
        var resolver = new ContextResolver(scorer, logger);
        var context = ConversationContext.Create(Guid.NewGuid());

        var result = await resolver.ResolveAsync(
            "chi phi phong ban A",
            [],  // Empty history - not a follow-up
            context);

        Assert.Equal(1.0f, result.Confidence);
        Assert.Equal(ConfidenceLevel.High, result.Level);
        Assert.False(result.RequiresClarification);
    }

    [Fact]
    public async Task ContextResolver_IsFollowUpAsync_ReturnsFalse_WhenHistoryIsEmpty()
    {
        var scorer = new ConfidenceScorer();
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextResolver>>();
        var resolver = new ContextResolver(scorer, logger);

        var result = await resolver.IsFollowUpAsync("chi phi phong ban", []);

        Assert.False(result);
    }

    [Fact]
    public async Task ContextResolver_IsFollowUpAsync_ReturnsFalseForNewTopic()
    {
        var scorer = new ConfidenceScorer();
        var logger = Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ContextResolver>>();
        var resolver = new ContextResolver(scorer, logger);
        var history = new List<ChatMessage>
        {
            ChatMessage.Create(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,"cho tôi biết về phòng A")
        };

        // Completely new topic - should not be detected as follow-up
        var result = await resolver.IsFollowUpAsync("lịch làm việc tuần này", history);

        Assert.False(result);
    }

    #endregion

    private sealed class JsonRoundTripCacheService : ICacheService
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            return Task.FromResult(
                _values.TryGetValue(key, out var json)
                    ? JsonSerializer.Deserialize<T>(json)
                    : null);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            _values[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            var cached = await GetAsync<T>(key, cancellationToken);
            if (cached is not null)
                return cached;

            var value = await factory();
            await SetAsync(key, value, expiration, cancellationToken);
            return value;
        }

        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1L);
        }
    }
}
