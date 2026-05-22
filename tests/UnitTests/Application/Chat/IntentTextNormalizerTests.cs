using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class IntentTextNormalizerTests
{
    [Theory]
    [InlineData("team nào burn hơn", "team nao burn hon")]
    [InlineData("team nao burn hn", "team nao burn hon")]
    [InlineData("team nao burn z", "team nao burn vay")]
    [InlineData("ngoài tôi ai spen kiểu này", "ngoai toi ai spend kieu nay")]
    [InlineData("ngoai toi ai spemd kieu nay", "ngoai toi ai spend kieu nay")]
    [InlineData("đứa nào burm tiền nhất", "dua nao burn tien nhat")]
    [InlineData("team nao chay ngan sachs hon", "team nao chay ngan sach hon")]
    [InlineData("dua nao dot tien v", "dua nao dot tien vay")]
    public void Normalize_ReturnsExpectedCanonicalText(string input, string expected)
    {
        var normalized = IntentTextNormalizer.Normalize(input);

        Assert.Equal(expected, normalized);
    }
}
