using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class SemanticActionDetectionTests
{
    private readonly VerbSemanticClassifier _classifier = new();

    // ─── DESTRUCTIVE queries – must be classified as Action ───

    [Theory]
    [InlineData("xóa toàn bộ dữ liệu")]
    [InlineData("delete all records")]
    [InlineData("loại bỏ hết mọi thứ")]
    [InlineData("wipe the database")]
    [InlineData("tiêu hủy database")]
    [InlineData("remove everything")]
    [InlineData("phá hủy toàn bộ")]
    [InlineData("drop all tables")]
    [InlineData("xóa hết")]
    [InlineData("delete entire")]
    public void ClassifyQuery_ReturnsAction_ForDestructivePhrases(string destructiveQuery)
    {
        var result = _classifier.ClassifyQuery(destructiveQuery);

        Assert.Equal(VerbKind.Action, result);
    }

    // ─── SAFE queries – must NOT be classified as Action ───

    [Theory]
    [InlineData("tháng này tôi chi bao nhiêu")]
    [InlineData("xem hóa đơn gần nhất")]
    [InlineData("show my expenses")]
    [InlineData("what is my budget")]
    [InlineData("cho tôi xem chi tiêu")]
    public void ClassifyQuery_ReturnsQuery_ForSafePhrases(string safeQuery)
    {
        var result = _classifier.ClassifyQuery(safeQuery);

        Assert.Equal(VerbKind.Query, result);
    }

    // ─── Neutral queries ───

    [Fact]
    public void ClassifyQuery_ReturnsNeutral_ForEmptyInput()
    {
        Assert.Equal(VerbKind.Neutral, _classifier.ClassifyQuery(""));
    }

    [Fact]
    public void ClassifyQuery_ReturnsNeutral_ForWhitespaceOnly()
    {
        Assert.Equal(VerbKind.Neutral, _classifier.ClassifyQuery("   "));
    }

    [Fact]
    public void ClassifyQuery_ReturnsNeutral_WhenNoKnownVerbs()
    {
        Assert.Equal(VerbKind.Neutral, _classifier.ClassifyQuery("something random here"));
    }
}