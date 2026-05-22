namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Validates that a rewritten query maintains intent compatibility with the original.
/// Defends against LLM rewriters that could inadvertently strip intent markers,
/// allowing a query to bypass policy controls applied to the original intent classification.
/// </summary>
internal static class QueryIntentValidator
{
    private static readonly string[] ExpenseKeywords =
    [
        "chi phí", "chi tiêu", "thanh toán", "hóa đơn", "hoá đơn",
        "ngân sách", "tiền", "tiền lương", "lương", "thu nhập",
        "expense", "cost", "spending", "payment", "invoice", "budget",
        "transaction", "receipt", "voucher", "chứng từ", "biên lai",
        "phòng ban", "department", "nhân viên", "employee", "vendor",
        "vendor", "nhà cung cấp", "hợp đồng", "contract"
    ];

    /// <summary>
    /// Returns true if the rewritten query is intent-compatible with the original.
    /// A rewrite is considered incompatible if the original contained expense-related
    /// keywords but the rewritten query does not contain any, suggesting the LLM
    /// may have stripped intent context.
    /// </summary>
    public static bool IsCompatible(string original, string rewritten)
    {
        if (string.IsNullOrWhiteSpace(rewritten))
            return false;

        var originalLower = original.ToLowerInvariant();
        var rewrittenLower = rewritten.ToLowerInvariant();

        var originalHasExpenseIntent = ExpenseKeywords.Any(kw =>
            originalLower.Contains(kw, StringComparison.Ordinal));

        // If original had expense intent, rewritten must also have some keyword presence
        if (originalHasExpenseIntent)
        {
            var rewrittenHasExpenseIntent = ExpenseKeywords.Any(kw =>
                rewrittenLower.Contains(kw, StringComparison.Ordinal));

            if (!rewrittenHasExpenseIntent)
                return false;
        }

        return true;
    }
}