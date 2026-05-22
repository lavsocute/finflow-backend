using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;

namespace FinFlow.UnitTests.Application.Chat;

public class ChatRagBusinessFormatterTests
{
    [Fact]
    public void TryFormat_ReturnsBusinessFriendlyExpenseListing_AndMergesSameDocumentChunks()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var expenseChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            documentId,
            null,
            """
            Expense record
            Merchant: B?CH H?A XANH
            Reference: 21070052990051966
            Expense date: 2021-07-16
            Category: Groceries
            DepartmentId: 00000000-0000-0000-0000-000000000000
            Subtotal: 187500
            VAT: 454
            Total: 187954
            Status: ReadyForApproval
            Submitted at UTC: 2026-04-24T09:46:04.5125020Z
            Line items:
            - KH? QUA: quantity 0.5, unit price 45000, total 22410
            """,
            "hash-expense-format",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var receiptChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            documentId,
            null,
            """
            Receipt record
            Original file name: bxh-receipt.jpg
            Merchant: B?CH H?A XANH
            Reference: 21070052990051966
            Document date: 2021-07-16
            """,
            "hash-receipt-format",
            1,
            [0.1f, 0.2f],
            DocumentChunkType.Receipt);

        var result = ChatRagBusinessFormatter.TryFormat(
            "show tất cả expense giúp tôi",
            [expenseChunk, receiptChunk]);

        Assert.NotNull(result);
        Assert.Contains("Tôi tìm thấy 1 khoản chi phù hợp", result!.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("B?ch H?a Xanh", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mã tham chiếu: 21070052990051966", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tổng tiền: 187,954 VND", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trạng thái: Chờ duyệt", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DepartmentId", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Submitted at UTC", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadyForApproval", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Citations);
        Assert.Equal(1, result.DocumentCount);
    }

    [Fact]
    public void TryFormat_ReturnsRecentDocumentListing_ForRecentQuery()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        var recentChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            """
            Expense record
            Merchant: Vendor Moi
            Reference: RECENT-001
            Expense date: 2026-05-20
            Total: 90000
            Status: Approved
            Submitted at UTC: 2026-05-20T10:00:00Z
            """,
            "hash-recent",
            0,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var olderChunk = DocumentChunk.Create(
            tenantId,
            membershipId,
            Guid.NewGuid(),
            null,
            """
            Expense record
            Merchant: Vendor Cu
            Reference: OLDER-001
            Expense date: 2026-05-10
            Total: 50000
            Status: ReadyForApproval
            Submitted at UTC: 2026-05-10T09:00:00Z
            """,
            "hash-older",
            1,
            [0.1f, 0.2f],
            DocumentChunkType.Expense);

        var result = ChatRagBusinessFormatter.TryFormat(
            "hóa đơn gần đây",
            [olderChunk, recentChunk]);

        Assert.NotNull(result);
        Assert.Contains("gần đây", result!.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1. Vendor Moi", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2. Vendor Cu", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trạng thái: Đã duyệt", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trạng thái: Chờ duyệt", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.DocumentCount);
    }
}
