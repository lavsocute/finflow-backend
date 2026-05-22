using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatResponseCacheKeyTests
{
    [Fact]
    public void Build_ChangesWhenPromptVersionChanges()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var allowedTypes = new[] { DocumentChunkType.Expense, DocumentChunkType.Receipt };
        const string query = "show tất cả expense giúp tôi";

        var keyA = ChatResponseCacheKey.Build(
            tenantId,
            membershipId,
            "Staff",
            departmentId,
            ownerId,
            allowedTypes,
            query,
            "2026.05.3");

        var keyB = ChatResponseCacheKey.Build(
            tenantId,
            membershipId,
            "Staff",
            departmentId,
            ownerId,
            allowedTypes,
            query,
            "2026.05.4");

        Assert.NotEqual(keyA, keyB);
    }
}
