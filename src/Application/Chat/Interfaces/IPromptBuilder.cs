using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Chat.Interfaces;

public interface IPromptBuilder
{
    Prompt BuildFullPrompt(
        string query,
        IReadOnlyList<DocumentChunk> retrievedChunks,
        ChatAccessScope scope,
        IReadOnlyList<ChatMessage> conversationHistory);
}

public record Prompt(
    string System,
    string User,
    IReadOnlyList<ChatMessage> History,
    string Version = "1.0.0"
);

public record ChatAccessScope(
    Guid TenantId,
    string TenantName,
    RoleType Role,
    Guid? DepartmentId,
    HashSet<Guid> PermittedDepartmentIds,
    Guid OwnerMembershipId,
    bool CanAccessAllTenantData,
    HashSet<DocumentChunkType> AllowedChunkTypes,
    BudgetAccessLevel BudgetAccess,
    ApprovalAccessLevel ApprovalAccess
);

public enum BudgetAccessLevel
{
    None,
    LimitOnly,
    AggregateSpent,
    FullBudget
}

public enum ApprovalAccessLevel
{
    None,
    OwnOnly,
    DeptApproval,
    AllApprovals
}
