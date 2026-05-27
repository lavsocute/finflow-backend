using FinFlow.Application.Chat.Services;
using Xunit;

namespace FinFlow.UnitTests.Application.Chat;

/// <summary>
/// Tests for ScopeKeywords - scope detection on NORMALIZED queries.
/// IMPORTANT: ScopeKeywords expects already-normalized input (no diacritics).
/// The IntentTextNormalizer.Normalize() should be called BEFORE passing to these methods.
/// </summary>
public class ScopeKeywordsTests
{
    [Theory]
    [InlineData("toan cong ty", true)]
    [InlineData("cong ty", true)]
    [InlineData("all company", true)]
    [InlineData("tenant", true)]
    [InlineData("workspace", true)]
    [InlineData("chi phi phong ban", false)]
    [InlineData("ngan sach", false)]
    public void MentionsTenantScope_WithNormalizedQuery_ReturnsExpectedResult(string normalizedQuery, bool expected)
    {
        var result = ScopeKeywords.MentionsTenantScope(normalizedQuery);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("phong ban", true)]
    [InlineData("department", true)]
    [InlineData("team", true)]
    [InlineData("chi phi", false)]
    [InlineData("toan cong ty", false)]
    public void MentionsDepartmentScope_WithNormalizedQuery_ReturnsExpectedResult(string normalizedQuery, bool expected)
    {
        var result = ScopeKeywords.MentionsDepartmentScope(normalizedQuery);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("cua toi", true)]
    [InlineData("cua em", true)]
    [InlineData("cua minh", true)]
    [InlineData("my", true)]
    [InlineData("phong ban", false)]
    public void MentionsOwnScope_WithNormalizedQuery_ReturnsExpectedResult(string normalizedQuery, bool expected)
    {
        var result = ScopeKeywords.MentionsOwnScope(normalizedQuery);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MentionsTenantScope_WithNormalizedQuery_ContainsToanCongTy_ReturnsTrue()
    {
        // The bug was: patterns had diacritics but query was normalized (no diacritics)
        // Fixed by making patterns work with normalized query (no diacritics)
        var normalizedQuery = "toan cong ty";
        var result = ScopeKeywords.MentionsTenantScope(normalizedQuery);
        Assert.True(result);
    }

    [Fact]
    public void MentionsDepartmentScope_WithNormalizedQuery_ContainsPhongBan_ReturnsTrue()
    {
        var normalizedQuery = "phong ban";
        var result = ScopeKeywords.MentionsDepartmentScope(normalizedQuery);
        Assert.True(result);
    }

    [Fact]
    public void MentionsTenantScope_WithCombinedQuery_AfterClarificationMerge_ReturnsTrue()
    {
        // Simulates: "phòng nào vượt ngân sách trong tháng này toàn công ty"
        // After Normalize(): "phong nao vuot ngan sach trong thang nay toan cong ty"
        var normalizedQuery = "phong nao vuot ngan sach trong thang nay toan cong ty";
        var result = ScopeKeywords.MentionsTenantScope(normalizedQuery);
        Assert.True(result);
    }

    [Fact]
    public void MentionsDepartmentScope_WithCombinedQuery_AfterClarificationMerge_ReturnsTrue()
    {
        var normalizedQuery = "phong nao vuot ngan sach trong thang nay phong ban";
        var result = ScopeKeywords.MentionsDepartmentScope(normalizedQuery);
        Assert.True(result);
    }
}