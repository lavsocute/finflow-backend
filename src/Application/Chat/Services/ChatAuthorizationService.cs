using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Builds authorization profiles and access scopes for chat requests based on tenant membership.
/// </summary>
public sealed class ChatAuthorizationService : IChatAuthorizationService
{
    private const string MissingDepartmentBoundaryMessage = "Chat access denied: your membership is missing a required department boundary for this chat scope.";
    private const string FallbackTenantName = "Workspace của bạn";

    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ICurrentTenant _currentTenant;

    public ChatAuthorizationService(
        ITenantMembershipRepository membershipRepository,
        ICurrentTenant currentTenant,
        ITenantRepository tenantRepository,
        IAccountRepository accountRepository,
        IDepartmentRepository departmentRepository)
    {
        _membershipRepository = membershipRepository;
        _currentTenant = currentTenant;
        _tenantRepository = tenantRepository;
        _accountRepository = accountRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<ChatAuthorizationProfile> GetAuthorizationProfileAsync(Guid membershipId, CancellationToken ct = default)
    {
        var membership = await _membershipRepository.GetByIdAsync(membershipId, ct)
            ?? throw new InvalidOperationException("Chat access denied: membership not found.");

        if (!membership.IsActive)
            throw new InvalidOperationException("Chat access denied: membership is inactive.");

        // AUDIT: SuperAdmin bypasses tenant-membership boundary check.
        // This allows a SuperAdmin to inspect any membership's authorization profile.
        // The actual chatbot access is still blocked at BuildProfile (line 60-61)
        // where RoleType.SuperAdmin is explicitly rejected.
        if (_currentTenant.Id != membership.IdTenant && !_currentTenant.IsSuperAdmin)
            throw new InvalidOperationException("Chat access denied: membership does not belong to the current tenant.");

        if (membership.Role == RoleType.Manager && !membership.DepartmentId.HasValue)
            throw new InvalidOperationException(MissingDepartmentBoundaryMessage);

        // Resolve real tenant name + actor metadata so the chatbot can answer
        // "what's my workspace / role / department" without going through RAG.
        var tenant = await _tenantRepository.GetByIdAsync(membership.IdTenant, ct);
        var account = await _accountRepository.GetByIdAsync(membership.AccountId, ct);
        DepartmentSummary? department = null;
        if (membership.DepartmentId.HasValue)
        {
            department = await _departmentRepository.GetByIdAsync(membership.DepartmentId.Value, ct);
        }

        var tenantName = tenant?.Name ?? FallbackTenantName;
        var userEmail = account?.Email;
        var userFullName = account?.FullName;
        var departmentName = department?.Name;

        return BuildProfile(membership, tenantName, userEmail, userFullName, departmentName);
    }

    public async Task<ChatAccessScope> GetChatAccessScopeAsync(Guid membershipId, CancellationToken ct = default)
    {
        var profile = await GetAuthorizationProfileAsync(membershipId, ct);

        return new ChatAccessScope(
            profile.TenantId,
            profile.TenantName,
            profile.Role,
            profile.DepartmentId,
            new HashSet<Guid>(profile.AllowedDepartmentIds),
            profile.MembershipId,
            profile.CanAccessAllTenantData,
            new HashSet<DocumentChunkType>(profile.AllowedChunkTypes),
            ResolveBudgetAccess(profile),
            ResolveApprovalAccess(profile),
            profile.UserEmail,
            profile.UserFullName,
            profile.DepartmentName);
    }

    private static ChatAuthorizationProfile BuildProfile(
        TenantMembershipSummary membership,
        string tenantName,
        string? userEmail,
        string? userFullName,
        string? departmentName)
    {
        // AUDIT: SuperAdmin role is explicitly blocked from chatbot access per security team finding.
        // Even if a SuperAdmin membership somehow reaches this point, the chatbot is rejected here.
        if (membership.Role == RoleType.SuperAdmin)
            throw new InvalidOperationException("Chat access denied: SuperAdmin is not allowed to use the chatbot.");

        return membership.Role switch
        {
            RoleType.TenantAdmin => new ChatAuthorizationProfile(
                membership.IdTenant,
                tenantName,
                membership.Role,
                membership.Id,
                membership.DepartmentId,
                new HashSet<Guid>(),
                true,
                AllChunkTypes(),
                new ChatCapabilities(
                    CanViewOwnExpenseSummary: true,
                    CanViewOwnExpenseDetails: true,
                    CanViewOwnBudgetLimit: true,
                    CanViewOwnBudgetRemaining: true,
                    CanViewDepartmentExpenseSummary: true,
                    CanViewDepartmentExpenseDetails: true,
                    CanViewTenantExpenseSummary: true,
                    CanViewTenantExpenseDetails: true),
                userEmail,
                userFullName,
                departmentName),
            RoleType.Accountant => new ChatAuthorizationProfile(
                membership.IdTenant,
                tenantName,
                membership.Role,
                membership.Id,
                membership.DepartmentId,
                new HashSet<Guid>(),
                true,  // CanAccessAllTenantData = true (Accountant needs tenant-wide document access)
                AllChunkTypes(),
                // Note: Accountant has CanViewTenantExpenseSummary/Details = true
                // to support company-wide expense reconciliation workflows.
                // BudgetAccessLevel.ResolveBudgetAccess() determines budget visibility level.
                new ChatCapabilities(
                    CanViewOwnExpenseSummary: true,
                    CanViewOwnExpenseDetails: true,
                    CanViewOwnBudgetLimit: true,
                    CanViewOwnBudgetRemaining: true,
                    CanViewDepartmentExpenseSummary: true,
                    CanViewDepartmentExpenseDetails: true,
                    CanViewTenantExpenseSummary: true,
                    CanViewTenantExpenseDetails: true),
                userEmail,
                userFullName,
                departmentName),
            RoleType.Manager => new ChatAuthorizationProfile(
                membership.IdTenant,
                tenantName,
                membership.Role,
                membership.Id,
                membership.DepartmentId,
                membership.DepartmentId.HasValue ? new HashSet<Guid> { membership.DepartmentId.Value } : new HashSet<Guid>(),
                false,
                new HashSet<DocumentChunkType>
                {
                    DocumentChunkType.Expense,
                    DocumentChunkType.Receipt,
                    DocumentChunkType.LineItem,
                    DocumentChunkType.ApprovalFlow,
                    DocumentChunkType.Budget
                },
                new ChatCapabilities(
                    CanViewOwnExpenseSummary: true,
                    CanViewOwnExpenseDetails: true,
                    CanViewOwnBudgetLimit: true,
                    CanViewOwnBudgetRemaining: true,
                    CanViewDepartmentExpenseSummary: true,
                    CanViewDepartmentExpenseDetails: true,
                    CanViewTenantExpenseSummary: false,
                    CanViewTenantExpenseDetails: false),
                userEmail,
                userFullName,
                departmentName),
            RoleType.Staff => new ChatAuthorizationProfile(
                membership.IdTenant,
                tenantName,
                membership.Role,
                membership.Id,
                membership.DepartmentId,
                membership.DepartmentId.HasValue ? new HashSet<Guid> { membership.DepartmentId.Value } : new HashSet<Guid>(),
                false,
                new HashSet<DocumentChunkType>
                {
                    DocumentChunkType.Expense,
                    DocumentChunkType.Receipt,
                    DocumentChunkType.LineItem
                },
                new ChatCapabilities(
                    CanViewOwnExpenseSummary: true,
                    CanViewOwnExpenseDetails: true,
                    CanViewOwnBudgetLimit: true,
                    CanViewOwnBudgetRemaining: true,
                    CanViewDepartmentExpenseSummary: false,
                    CanViewDepartmentExpenseDetails: false,
                    CanViewTenantExpenseSummary: false,
                    CanViewTenantExpenseDetails: false),
                userEmail,
                userFullName,
                departmentName),
            _ => throw new InvalidOperationException($"Chat access denied: role {membership.Role} is not allowed to use the chatbot.")
        };
    }

    private static BudgetAccessLevel ResolveBudgetAccess(ChatAuthorizationProfile profile)
    {
        if (profile.Capabilities.CanViewTenantExpenseDetails || profile.Capabilities.CanViewTenantExpenseSummary)
            return BudgetAccessLevel.FullBudget;

        if (profile.Capabilities.CanViewDepartmentExpenseDetails || profile.Capabilities.CanViewDepartmentExpenseSummary)
            return BudgetAccessLevel.AggregateSpent;

        if (profile.Capabilities.CanViewOwnBudgetLimit || profile.Capabilities.CanViewOwnBudgetRemaining)
            return BudgetAccessLevel.LimitOnly;

        return BudgetAccessLevel.None;
    }

    private static ApprovalAccessLevel ResolveApprovalAccess(ChatAuthorizationProfile profile)
    {
        if (profile.CanAccessAllTenantData)
            return ApprovalAccessLevel.AllApprovals;

        if (profile.Capabilities.CanViewDepartmentExpenseDetails || profile.Capabilities.CanViewDepartmentExpenseSummary)
            return ApprovalAccessLevel.DeptApproval;

        if (profile.Capabilities.CanViewOwnExpenseDetails || profile.Capabilities.CanViewOwnExpenseSummary)
            return ApprovalAccessLevel.OwnOnly;

        return ApprovalAccessLevel.None;
    }

    private static HashSet<DocumentChunkType> AllChunkTypes() =>
        new((DocumentChunkType[])Enum.GetValues(typeof(DocumentChunkType)));
}