using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public class ContextRecoveryServiceTests
{
    private readonly Mock<IConversationStateManager> _mockStateManager;
    private readonly Mock<ILogger<ContextRecoveryService>> _mockLogger;
    private readonly ContextRecoveryService _sut;

    public ContextRecoveryServiceTests()
    {
        _mockStateManager = new Mock<IConversationStateManager>();
        _mockLogger = new Mock<ILogger<ContextRecoveryService>>();
        _sut = new ContextRecoveryService(_mockStateManager.Object, _mockLogger.Object);
    }

    [Fact]
    public void GenerateClarificationPrompt_LowConfidence_ReturnsVietnamesePrompt()
    {
        // Arrange
        var query = "chi phí";

        // Act
        var result = _sut.GenerateClarificationPrompt(query, ClarificationType.LowConfidence);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("không rõ", result.ToLower());
    }

    [Fact]
    public void GenerateClarificationPrompt_EntityUnknown_ReturnsVietnamesePrompt()
    {
        // Arrange
        var query = "phòng nào";

        // Act
        var result = _sut.GenerateClarificationPrompt(query, ClarificationType.EntityUnknown);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("đối tượng", result.ToLower());
    }

    [Fact]
    public void GenerateClarificationPrompt_AmbiguousReference_ReturnsVietnamesePrompt()
    {
        // Arrange
        var query = "đó";

        // Act
        var result = _sut.GenerateClarificationPrompt(query, ClarificationType.AmbiguousReference);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("chưa rõ", result.ToLower());
    }

    [Fact]
    public async Task RecoverFromHistoryAsync_EmptyHistory_ReturnsFailure()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>();

        // Act
        var result = await _sut.RecoverFromHistoryAsync(sessionId, history);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(RecoveryStrategy.None, result.RecoveryStrategy);
    }

    [Fact]
    public async Task RecoverFromHistoryAsync_HistoryWithEntities_ReturnsSuccess()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>
        {
            CreateUserMessage(sessionId, "Chi phí phòng nhân sự tháng này"),
            CreateUserMessage(sessionId, "Báo cáo của phòng kế toán")
        };

        _mockStateManager
            .Setup(x => x.AddEntityAsync(sessionId, It.IsAny<TrackedEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RecoverFromHistoryAsync(sessionId, history);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(RecoveryStrategy.HistoryAnalysis, result.RecoveryStrategy);
        Assert.NotEmpty(result.RecoveredEntities);
    }

    [Fact]
    public async Task InferIntentFromHistoryAsync_EmptyHistory_ReturnsFailure()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>();

        // Act
        var result = await _sut.InferIntentFromHistoryAsync(sessionId, history);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task InferIntentFromHistoryAsync_WithExpenseQuery_ReturnsExpenseIntent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>
        {
            CreateUserMessage(sessionId, "Chi phí phòng nhân sự tháng này bao nhiêu"),
            CreateUserMessage(sessionId, "Xem chi phí cho bộ phận marketing")
        };

        // Act
        var result = await _sut.InferIntentFromHistoryAsync(sessionId, history);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("expense_inquiry", result.InferredIntent);
    }

    [Fact]
    public void CanResolveWithPartialContext_NullContext_ReturnsFalse()
    {
        // Arrange
        var query = "chi phí phòng nào";

        // Act
        var result = _sut.CanResolveWithPartialContext(query, null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanResolveWithPartialContext_WithExplicitReference_ReturnsFalse()
    {
        // Arrange
        var context = ConversationContext.Create(Guid.NewGuid());
        context.AddEntity(TrackedEntity.Create("phòng nhân sự", EntityType.Department, 1));
        var query = "chi phí phòng nào"; // Explicit "phòng nào" reference

        // Act
        var result = _sut.CanResolveWithPartialContext(query, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanResolveWithPartialContext_WithSpecificQuery_ReturnsTrue()
    {
        // Arrange
        var context = ConversationContext.Create(Guid.NewGuid());
        context.AddEntity(TrackedEntity.Create("phòng nhân sự", EntityType.Department, 1));
        var query = "chi phí phòng nhân sự tháng 5"; // Specific query

        // Act
        var result = _sut.CanResolveWithPartialContext(query, context);

        // Assert
        Assert.True(result);
    }

    private static ChatMessage CreateUserMessage(Guid sessionId, string content)
    {
        return ChatMessage.Create(
            sessionId,
            Guid.NewGuid(),
            ChatMessageRole.User,
            content);
    }
}