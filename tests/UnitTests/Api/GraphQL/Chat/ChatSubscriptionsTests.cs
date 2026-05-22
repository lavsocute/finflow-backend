using FinFlow.Api.GraphQL.Chat;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Interfaces;
using HotChocolate.Resolvers;
using Moq;

namespace FinFlow.UnitTests.Api.GraphQL.Chat;

public sealed class ChatSubscriptionsTests
{
    [Fact]
    public async Task OnChatStreamAsync_MapsAnswerSource_OnCompleteEvent()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var chatService = new Mock<IChatService>();
        chatService
            .Setup(x => x.ChatStreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateEvents([
                new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: "partial"),
                new ChatStreamEvent(
                    ChatStreamEventKind.Complete,
                    SessionId: sessionId,
                    MessageId: messageId,
                    DocumentCount: 3,
                    TokenUsage: 42,
                    CompleteAnswer: "done",
                    AnswerSource: ChatAnswerSource.Reporting)
            ]));

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.MembershipId).Returns(membershipId);

        var context = new Mock<IResolverContext>();
        var sut = new ChatSubscriptions();

        var result = await CollectAsync(sut.OnChatStreamAsync(
            new ChatInput(null, "Tháng này tôi đã tiêu bao nhiêu?", null),
            chatService.Object,
            currentTenant.Object,
            context.Object,
            CancellationToken.None));

        Assert.Equal(2, result.Count);
        Assert.Null(result[0].AnswerSource);
        Assert.Equal("Token", result[0].Kind);

        var complete = result[1];
        Assert.Equal("Complete", complete.Kind);
        Assert.Equal(ChatAnswerSource.Reporting, complete.AnswerSource);
        Assert.Equal(sessionId, complete.SessionId);
        Assert.Equal(messageId, complete.MessageId);
        Assert.Equal("done", complete.CompleteAnswer);
    }

    private static async IAsyncEnumerable<ChatStreamEvent> CreateEvents(IEnumerable<ChatStreamEvent> events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }

    private static async Task<List<ChatStreamEventType>> CollectAsync(IAsyncEnumerable<ChatStreamEventType> stream)
    {
        var events = new List<ChatStreamEventType>();
        await foreach (var evt in stream)
            events.Add(evt);

        return events;
    }
}
