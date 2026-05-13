using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Chat.Services;

public sealed class ChatAuthorizationService : IChatAuthorizationService
{
    private const string MissingDepartmentBoundaryMessage = "Chat access denied: your membership is missing a required department boundary for this chat scope.";

    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ICurrentTenant _currentTenant;

    public ChatAuthorizationService(
        ITenantMembershipRepository membershipRepository,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _currentTenant = currentTenant;
    }

    public async Task<ChatAccessScope> GetChatAccessScopeAsync(Guid membershipId, CancellationToken ct = default)
    {
        var membership = await _membershipRepository.GetByIdAsync(membershipId, ct)
            ?? throw new InvalidOperationException("Chat access denied: membership not found.");

        if (!membership.IsActive)
            throw new InvalidOperationException("Chat access denied: membership is inactive.");

        if (_currentTenant.Id != membership.IdTenant && !_currentTenant.IsSuperAdmin)
            throw new InvalidOperationException("Chat access denied: membership does not belong to the current tenant.");

        if (membership.Role == RoleType.Manager && !membership.DepartmentId.HasValue)
            throw new InvalidOperationException(MissingDepartmentBoundaryMessage);

        return BuildScope(membership);
    }

    private static ChatAccessScope BuildScope(TenantMembershipSummary membership)
    {
        if (membership.Role == RoleType.SuperAdmin)
            throw new InvalidOperationException("Chat access denied: SuperAdmin is not allowed to use the chatbot.");

        return membership.Role switch
        {
            RoleType.TenantAdmin => new ChatAccessScope(
                membership.IdTenant,
                "Current Tenant",
                membership.Role,
                membership.DepartmentId,
                new HashSet<Guid>(),
                membership.Id,
                true,
                AllChunkTypes(),
                BudgetAccessLevel.FullBudget,
                ApprovalAccessLevel.AllApprovals),
            RoleType.Accountant => new ChatAccessScope(
                membership.IdTenant,
                "Current Tenant",
                membership.Role,
                membership.DepartmentId,
                new HashSet<Guid>(),
                membership.Id,
                true,
                AllChunkTypes(),
                BudgetAccessLevel.FullBudget,
                ApprovalAccessLevel.AllApprovals),
            RoleType.Manager => new ChatAccessScope(
                membership.IdTenant,
                "Current Tenant",
                membership.Role,
                membership.DepartmentId,
                membership.DepartmentId.HasValue ? new HashSet<Guid> { membership.DepartmentId.Value } : new HashSet<Guid>(),
                membership.Id,
                false,
                new HashSet<DocumentChunkType>
                {
                    DocumentChunkType.Expense,
                    DocumentChunkType.Receipt,
                    DocumentChunkType.ApprovalFlow,
                    DocumentChunkType.Budget
                },
                BudgetAccessLevel.AggregateSpent,
                ApprovalAccessLevel.DeptApproval),
            RoleType.Staff => new ChatAccessScope(
                membership.IdTenant,
                "Current Tenant",
                membership.Role,
                membership.DepartmentId,
                membership.DepartmentId.HasValue ? new HashSet<Guid> { membership.DepartmentId.Value } : new HashSet<Guid>(),
                membership.Id,
                false,
                new HashSet<DocumentChunkType>
                {
                    DocumentChunkType.Expense,
                    DocumentChunkType.Receipt
                },
                BudgetAccessLevel.LimitOnly,
                ApprovalAccessLevel.OwnOnly),
            _ => throw new InvalidOperationException($"Chat access denied: role {membership.Role} is not allowed to use the chatbot.")
        };
    }

    private static HashSet<DocumentChunkType> AllChunkTypes() =>
        new((DocumentChunkType[])Enum.GetValues(typeof(DocumentChunkType)));
}
