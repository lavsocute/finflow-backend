using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatPhraseAdversarialTests
{
    private readonly ChatIntentRouter _router = new();
    private readonly ChatPolicyEngine _policyEngine = new();

    [Theory]
    [InlineData("team nào burn hơn", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("ngoài tôi ai spend kiểu này", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("đứa nào đốt tiền nhất", ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("team nao burn hon", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("ngoai toi ai spend kieu nay", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("dua nao dot tien nhat", ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("team nao burn hn", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("ngoai toi ai spen kieu nay", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("dua nao dot tien nhat vay", ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("team nao burn z", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("ngoai toi ai spemd kieu nay", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("dua nao burm tien nhat", ChatIntentFamily.Ranking, ChatScopeConfidence.Ambiguous)]
    [InlineData("team nao chay ngan sachs hon", ChatIntentFamily.Comparison, ChatScopeConfidence.Ambiguous)]
    [InlineData("who burn more in my team", ChatIntentFamily.Comparison, ChatScopeConfidence.SafeInferred)]
    [InlineData("top spender in my department", ChatIntentFamily.Ranking, ChatScopeConfidence.Explicit)]
    public void Classify_Recognizes_MixedLanguageAndSlangSensitiveQueries(
        string query,
        ChatIntentFamily expectedFamily,
        ChatScopeConfidence expectedScopeConfidence)
    {
        var result = _router.Classify(query);

        Assert.Equal(ChatExecutionMode.Reporting, result.Mode);
        Assert.Equal(expectedFamily, result.Family);
        Assert.Equal(expectedScopeConfidence, result.ScopeConfidence);
    }

    [Fact]
    public void Decide_Denies_Staff_MixedLanguageCrossUserQuery()
    {
        var profile = CreateProfile(
            RoleType.Staff,
            new ChatCapabilities(true, true, true, true, false, false, false, false));
        var classification = _router.Classify("ngoài tôi ai spend kiểu này");

        var result = _policyEngine.Decide(profile, classification, "ngoài tôi ai spend kiểu này");

        Assert.Equal(ChatPolicyDecisionKind.Deny, result.Kind);
    }

    [Fact]
    public void Decide_Clarifies_Manager_CrossTeamSlangQuery()
    {
        var profile = CreateProfile(
            RoleType.Manager,
            new ChatCapabilities(true, true, true, true, true, true, false, false));
        var classification = _router.Classify("team nào burn hơn");

        var result = _policyEngine.Decide(profile, classification, "team nào burn hơn");

        Assert.Equal(ChatPolicyDecisionKind.Clarify, result.Kind);
    }

    [Fact]
    public void Decide_ExecutesReporting_ForManagerOwnedTeamEnglishQuery()
    {
        var profile = CreateProfile(
            RoleType.Manager,
            new ChatCapabilities(true, true, true, true, true, true, false, false));
        var classification = _router.Classify("who burn more in my team");

        var result = _policyEngine.Decide(profile, classification, "who burn more in my team");

        Assert.Equal(ChatPolicyDecisionKind.ExecuteReporting, result.Kind);
    }

    private static ChatAuthorizationProfile CreateProfile(RoleType role, ChatCapabilities capabilities) =>
        new(
            Guid.NewGuid(),
            "Tenant",
            role,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            role is RoleType.Accountant or RoleType.TenantAdmin,
            Array.Empty<DocumentChunkType>(),
            capabilities);
}
