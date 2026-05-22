using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatAnswerSourceTests
{
    [Fact]
    public void ChatResponse_CarriesAnswerSource()
    {
        var response = new ChatResponse(
            Answer: "Tổng chi tháng này là 12 VND.",
            SessionId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            DocumentCount: 0,
            TokenUsage: 0,
            AnswerSource: ChatAnswerSource.Reporting,
            Citations: null);

        Assert.Equal(ChatAnswerSource.Reporting, response.AnswerSource);
    }

    [Fact]
    public void ChatStreamEvent_Complete_CarriesAnswerSource()
    {
        var evt = new ChatStreamEvent(
            Kind: ChatStreamEventKind.Complete,
            SessionId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            DocumentCount: 3,
            TokenUsage: 42,
            CompleteAnswer: "Theo chứng từ nội bộ...",
            AnswerSource: ChatAnswerSource.Rag);

        Assert.Equal(ChatAnswerSource.Rag, evt.AnswerSource);
    }
}
