using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatIntentRouterTests
{
    [Theory]
    [InlineData("hello", ChatExecutionMode.Greeting, ChatIntentFamily.Greeting, ChatScopeConfidence.Explicit)]
    [InlineData("Xin chào", ChatExecutionMode.Greeting, ChatIntentFamily.Greeting, ChatScopeConfidence.Explicit)]
    [InlineData("bạn là ai", ChatExecutionMode.General, ChatIntentFamily.SmallTalk, ChatScopeConfidence.Explicit)]
    [InlineData("abc", ChatExecutionMode.General, ChatIntentFamily.LowSignal, ChatScopeConfidence.Ambiguous)]
    [InlineData("Viết lại câu này cho lịch sự hơn: gửi hóa đơn cho tôi", ChatExecutionMode.General, ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit)]
    [InlineData("Tóm tắt giúp tôi: hôm nay tôi đã hoàn thành báo cáo chi tiêu quý 2", ChatExecutionMode.General, ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit)]
    [InlineData("Gợi ý cách đặt tên hạng mục cho khoản ăn trưa với khách hàng", ChatExecutionMode.General, ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit)]
    [InlineData("Viết code python để phân tích các hóa đơn này", ChatExecutionMode.General, ChatIntentFamily.Programming, ChatScopeConfidence.Explicit)]
    [InlineData("cho tôi câu lệnh s q l để lọc hóa đơn trùng", ChatExecutionMode.General, ChatIntentFamily.Programming, ChatScopeConfidence.Explicit)]
    [InlineData("viet dum doan py thon parse receipt", ChatExecutionMode.General, ChatIntentFamily.Programming, ChatScopeConfidence.Explicit)]
    [InlineData("vendor này có mùi gian lận không", ChatExecutionMode.General, ChatIntentFamily.SensitiveAdvice, ChatScopeConfidence.Explicit)]
    [InlineData("nên duyệt bill này chứ", ChatExecutionMode.General, ChatIntentFamily.SensitiveAdvice, ChatScopeConfidence.Explicit)]
    [InlineData("Hóa đơn nào đang chờ duyệt trong phòng ban?", ChatExecutionMode.Reporting, ChatIntentFamily.ApprovalQueue, ChatScopeConfidence.Explicit)]
    [InlineData("Tháng này tôi đã tiêu bao nhiêu?", ChatExecutionMode.Reporting, ChatIntentFamily.OwnSummary, ChatScopeConfidence.Explicit)]
    [InlineData("How much did my department spend this quarter?", ChatExecutionMode.Reporting, ChatIntentFamily.Aggregate, ChatScopeConfidence.Explicit)]
    [InlineData("Phòng ban tôi đã chi bao nhiêu tháng này?", ChatExecutionMode.Reporting, ChatIntentFamily.Aggregate, ChatScopeConfidence.SafeInferred)]
    [InlineData("Nhân viên nào chi nhiều nhất trong phòng ban tôi tháng này?", ChatExecutionMode.Reporting, ChatIntentFamily.Ranking, ChatScopeConfidence.SafeInferred)]
    [InlineData("Xu hướng chi tiêu 3 tháng gần đây của phòng ban tôi là gì?", ChatExecutionMode.Reporting, ChatIntentFamily.Aggregate, ChatScopeConfidence.SafeInferred)]
    [InlineData("Nhà cung cấp nào có tổng chi lớn nhất trong phòng ban tôi tháng này?", ChatExecutionMode.Reporting, ChatIntentFamily.Aggregate, ChatScopeConfidence.SafeInferred)]
    [InlineData("Ngân sách phòng ban tôi còn bao nhiêu tháng này?", ChatExecutionMode.Reporting, ChatIntentFamily.Aggregate, ChatScopeConfidence.SafeInferred)]
    [InlineData("So sánh chi tiêu của tôi với người khác trong công ty", ChatExecutionMode.Reporting, ChatIntentFamily.Comparison, ChatScopeConfidence.Explicit)]
    [InlineData("tôi đứng thứ mấy", ChatExecutionMode.Reporting, ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("team tôi với team kia bên nào đốt tiền hơn", ChatExecutionMode.Reporting, ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("ai đốt tiền nhất tháng này", ChatExecutionMode.Reporting, ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("ngoài tôi ra ai chi kiểu này", ChatExecutionMode.Reporting, ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("team nào cháy ngân sách hơn", ChatExecutionMode.Reporting, ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("Show me the receipt for lunch last Friday", ChatExecutionMode.Rag, ChatIntentFamily.DocumentLookup, ChatScopeConfidence.Explicit)]
    [InlineData("", ChatExecutionMode.Rag, ChatIntentFamily.Unknown, ChatScopeConfidence.Ambiguous)]
    public void Classify_ReturnsExpectedClassification(
        string query,
        ChatExecutionMode expectedMode,
        ChatIntentFamily expectedFamily,
        ChatScopeConfidence expectedConfidence)
    {
        var router = new ChatIntentRouter();

        var result = router.Classify(query);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(expectedFamily, result.Family);
        Assert.Equal(expectedConfidence, result.ScopeConfidence);
    }
}
