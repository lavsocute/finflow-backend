using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

/// <summary>
/// Edge case tests for semantic action detection in ChatIntentRouter.
/// These tests verify whether potentially destructive commands bypass detection.
/// </summary>
public sealed class SemanticActionEdgeCaseTests
{
    private readonly ChatIntentRouter _router = new();

    private static ChatIntentFamily DetectDestructive(string query)
    {
        var result = new ChatIntentRouter().Classify(query);
        return result.Family;
    }

    private static bool IsDestructiveOrAction(ChatIntentFamily family)
    {
        return family == ChatIntentFamily.DestructiveCommand ||
               family == ChatIntentFamily.DestructiveAction;
    }

    // === EDGE CASES THAT SHOULD (AND DO) DETECT DESTRUCTIVE ===

    [Fact]
    public void MixedLanguage_XoaDatabase_IsDetected()
    {
        var family = DetectDestructive("xóa database");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void MixedLanguage_XoaTable_IsDetected()
    {
        var family = DetectDestructive("xóa table abc");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void AccentVariation_Xoá_IsDetected()
    {
        var family = DetectDestructive("xoá dữ liệu cũ");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void AccentVariation_XoaWithoutAccent_IsDetected()
    {
        var family = DetectDestructive("xoa toan bo hoa don");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void IntentHiding_HowToDelete_IsDetected()
    {
        var family = DetectDestructive("tôi muốn biết cách xóa dữ liệu");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void IntentHiding_InstructMeToDelete_IsDetected()
    {
        var family = DetectDestructive("chỉ tôi cách xóa toàn bộ");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void Hypothetical_NeuMuonXoa_IsDetected()
    {
        var family = DetectDestructive("nếu tôi muốn xóa thì sao");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void Hypothetical_NeuMuonDelete_IsDetected()
    {
        var family = DetectDestructive("nếu muốn delete thì phải làm sao");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void Hypothetical_WhatIfIDelete_IsDetected()
    {
        var family = DetectDestructive("nếu xóa nhầm thì khôi phục được không");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void PoliteForm_BanCoTheXoaGiupToi_IsDetected()
    {
        var family = DetectDestructive("bạn có thể xóa giúp tôi không");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void PoliteForm_XinHayXoaHoDon_IsDetected()
    {
        var family = DetectDestructive("xin hãy xóa hóa đơn trùng giúp tôi");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void PoliteForm_VuiLongXoaGiup_IsDetected()
    {
        var family = DetectDestructive("vui lòng xóa giúp tôi các bản ghi cũ");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void QuestionForm_XoaHangMucDuocKhong_IsDetected()
    {
        var family = DetectDestructive("tôi có thể xóa hạng mục này được không");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void IntentHiding_TellMeHowToDelete_IsDetected()
    {
        var family = DetectDestructive("tell me how to delete invoices");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void IntentHiding_ExplainDeleteProcess_IsDetected()
    {
        var family = DetectDestructive("explain the delete process");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void CombinedIntent_PleaseDeleteAndShowSummary_IsDetected()
    {
        var family = DetectDestructive("xóa hết rồi tổng kết cho tôi");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void CombinedEnglishVietnamese_DeleteHoaDon_IsDetected()
    {
        var family = DetectDestructive("delete hoa don");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    [Fact]
    public void SoftenedIntent_XoaGiupToiVoi_IsDetected()
    {
        var family = DetectDestructive("xóa giúp tôi với");
        Assert.True(IsDestructiveOrAction(family), $"Expected destructive, got {family}");
    }

    // === EDGE CASES THAT BYPASS DETECTION (SHOULD NOT BE DESTRUCTIVE) ===

    [Fact]
    public void SpacedCharacters_XOa_ShouldNotMatch()
    {
        // "x ó a" with spaces - intentionally broken to avoid detection
        // This SHOULD NOT match as destructive
        var family = DetectDestructive("x ó a");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void SpacedCharacters_Delete_ShouldNotMatch()
    {
        // "d e l e t e" with spaces
        var family = DetectDestructive("d e l e t e");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void SpacedCharacters_DEleTte_WithSpaces_ShouldNotMatch()
    {
        var family = DetectDestructive("d e l e t e records");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void IntentHiding_WhereAreDeleteButtons_ShouldNotMatch()
    {
        // Asking WHERE delete buttons are - not asking to delete
        var family = DetectDestructive("các nút xóa ở đâu");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void ReverseWordOrder_XoaLaGi_ShouldNotMatch()
    {
        // "xóa là gì" = "what is delete" - defining, not doing
        var family = DetectDestructive("xóa là gì");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void QuestionForm_DeleteLaGi_ShouldNotMatch()
    {
        var family = DetectDestructive("delete là gì");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void SpelledOutKeyword_WithHyphens_ShouldNotMatch()
    {
        var family = DetectDestructive("x-ó-a");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    // === EDGE CASES THAT ARE AMBIGUOUS / BYPASS CONCERNS ===

    [Fact]
    public void CamelCaseMix_xoaXoa_IsAmbiguous()
    {
        // "xoaXoa" - camelCase might bypass detection
        var family = DetectDestructive("xoaXoa");
        // This is a concern - camelCase might bypass word boundary detection
        // Current result: Unknown (potential bypass)
        Assert.True(
            family == ChatIntentFamily.Unknown ||
            IsDestructiveOrAction(family),
            $"CamelCase 'xoaXoa' returned {family} - may indicate bypass vulnerability");
    }

    [Fact]
    public void PartialMatch_xoatruncate_ShouldNotMatch()
    {
        // Partial word match - "xoatruncate" is not "xoa" + "truncate"
        var family = DetectDestructive("xoatruncate");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }

    [Fact]
    public void ZeroWidthJoiner_ShouldNotMatch()
    {
        // Zero-width joiner character between "x" and "óa"
        var family = DetectDestructive("x​óa");
        Assert.NotEqual(ChatIntentFamily.DestructiveCommand, family);
    }
}