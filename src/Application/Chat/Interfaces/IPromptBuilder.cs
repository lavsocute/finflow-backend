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
        IReadOnlyList<ChatMessage> conversationHistory,
        string? compressedSummary = null);

    Prompt BuildReportingPrompt(
        string query,
        string reportingPayload,
        ChatAuthorizationProfile profile);

    Prompt BuildGeneralPrompt(
        string query,
        ChatIntentClassification classification,
        IReadOnlyList<ChatMessage> conversationHistory,
        ChatAuthorizationProfile? actor = null);
}

public record Prompt(
    string System,
    string User,
    IReadOnlyList<ChatMessage> History,
    string Version = "1.0.0"
);

public record ChatAuthorizationProfile
{
    public ChatAuthorizationProfile(
        Guid tenantId,
        string tenantName,
        RoleType role,
        Guid membershipId,
        Guid? departmentId,
        IEnumerable<Guid> allowedDepartmentIds,
        bool canAccessAllTenantData,
        IEnumerable<DocumentChunkType> allowedChunkTypes,
        ChatCapabilities capabilities,
        string? userEmail = null,
        string? userFullName = null,
        string? departmentName = null)
    {
        TenantId = tenantId;
        TenantName = tenantName;
        Role = role;
        MembershipId = membershipId;
        DepartmentId = departmentId;
        AllowedDepartmentIds = CopySet(allowedDepartmentIds);
        CanAccessAllTenantData = canAccessAllTenantData;
        AllowedChunkTypes = CopySet(allowedChunkTypes);
        Capabilities = capabilities;
        UserEmail = userEmail;
        UserFullName = userFullName;
        DepartmentName = departmentName;
    }

    public Guid TenantId { get; init; }
    public string TenantName { get; init; }
    public RoleType Role { get; init; }
    public Guid MembershipId { get; init; }
    public Guid? DepartmentId { get; init; }
    public IReadOnlySet<Guid> AllowedDepartmentIds { get; init; }
    public bool CanAccessAllTenantData { get; init; }
    public IReadOnlySet<DocumentChunkType> AllowedChunkTypes { get; init; }
    public ChatCapabilities Capabilities { get; init; }

    /// <summary>
    /// Email of the chatting user. Used for prompt context so the bot can correctly
    /// answer "what is my email/role/workspace" without going through RAG.
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>Optional friendly name of the chatting user.</summary>
    public string? UserFullName { get; init; }

    /// <summary>Friendly name of the user's department, if assigned.</summary>
    public string? DepartmentName { get; init; }

    protected static IReadOnlySet<T> CopySet<T>(IEnumerable<T> values) =>
        values switch
        {
            IReadOnlySet<T> readOnlySet when readOnlySet.Count == 0 => EmptyReadOnlySet<T>.Instance,
            ICollection<T> collection when collection.Count == 0 => EmptyReadOnlySet<T>.Instance,
            _ => new HashSet<T>(values)
        };

    protected sealed class EmptyReadOnlySet<T> : IReadOnlySet<T>
    {
        public static readonly EmptyReadOnlySet<T> Instance = new();

        public int Count => 0;

        public bool Contains(T item) => false;

        public IEnumerator<T> GetEnumerator()
        {
            yield break;
        }

        public bool IsProperSubsetOf(IEnumerable<T> other) => !other.Any();

        public bool IsProperSupersetOf(IEnumerable<T> other) => false;

        public bool IsSubsetOf(IEnumerable<T> other) => true;

        public bool IsSupersetOf(IEnumerable<T> other) => !other.Any();

        public bool Overlaps(IEnumerable<T> other) => false;

        public bool SetEquals(IEnumerable<T> other) => !other.Any();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public record ChatCapabilities(
    bool CanViewOwnExpenseSummary,
    bool CanViewOwnExpenseDetails,
    bool CanViewOwnBudgetLimit,
    bool CanViewOwnBudgetRemaining,
    bool CanViewDepartmentExpenseSummary,
    bool CanViewDepartmentExpenseDetails,
    bool CanViewTenantExpenseSummary,
    bool CanViewTenantExpenseDetails
);

public record ChatAccessScope : ChatAuthorizationProfile
{
    public ChatAccessScope(
        Guid tenantId,
        string tenantName,
        RoleType role,
        Guid? departmentId,
        IEnumerable<Guid> permittedDepartmentIds,
        Guid ownerMembershipId,
        bool canAccessAllTenantData,
        IEnumerable<DocumentChunkType> allowedChunkTypes,
        BudgetAccessLevel budgetAccess,
        ApprovalAccessLevel approvalAccess,
        string? userEmail = null,
        string? userFullName = null,
        string? departmentName = null)
        : base(
            tenantId,
            tenantName,
            role,
            ownerMembershipId,
            departmentId,
            permittedDepartmentIds,
            canAccessAllTenantData,
            allowedChunkTypes,
            CreateCapabilities(role, budgetAccess, approvalAccess, canAccessAllTenantData),
            userEmail,
            userFullName,
            departmentName)
    {
        PermittedDepartmentIds = CopySet(permittedDepartmentIds);
        OwnerMembershipId = ownerMembershipId;
        BudgetAccess = budgetAccess;
        ApprovalAccess = approvalAccess;
    }

    public IReadOnlySet<Guid> PermittedDepartmentIds { get; init; }
    public Guid OwnerMembershipId { get; init; }
    public BudgetAccessLevel BudgetAccess { get; init; }
    public ApprovalAccessLevel ApprovalAccess { get; init; }

    private static ChatCapabilities CreateCapabilities(
        RoleType role,
        BudgetAccessLevel budgetAccess,
        ApprovalAccessLevel approvalAccess,
        bool canAccessAllTenantData)
    {
        if (canAccessAllTenantData || role is RoleType.Accountant or RoleType.TenantAdmin)
        {
            return new ChatCapabilities(
                CanViewOwnExpenseSummary: true,
                CanViewOwnExpenseDetails: true,
                CanViewOwnBudgetLimit: true,
                CanViewOwnBudgetRemaining: true,
                CanViewDepartmentExpenseSummary: true,
                CanViewDepartmentExpenseDetails: true,
                CanViewTenantExpenseSummary: true,
                CanViewTenantExpenseDetails: true);
        }

        if (role == RoleType.Manager || approvalAccess == ApprovalAccessLevel.DeptApproval)
        {
            return new ChatCapabilities(
                CanViewOwnExpenseSummary: true,
                CanViewOwnExpenseDetails: true,
                CanViewOwnBudgetLimit: budgetAccess is BudgetAccessLevel.AggregateSpent or BudgetAccessLevel.FullBudget,
                CanViewOwnBudgetRemaining: budgetAccess is BudgetAccessLevel.AggregateSpent or BudgetAccessLevel.FullBudget,
                CanViewDepartmentExpenseSummary: true,
                CanViewDepartmentExpenseDetails: true,
                CanViewTenantExpenseSummary: false,
                CanViewTenantExpenseDetails: false);
        }

        return new ChatCapabilities(
            CanViewOwnExpenseSummary: true,
            CanViewOwnExpenseDetails: true,
            CanViewOwnBudgetLimit: budgetAccess is BudgetAccessLevel.LimitOnly or BudgetAccessLevel.AggregateSpent or BudgetAccessLevel.FullBudget,
            CanViewOwnBudgetRemaining: budgetAccess is BudgetAccessLevel.LimitOnly or BudgetAccessLevel.AggregateSpent or BudgetAccessLevel.FullBudget,
            CanViewDepartmentExpenseSummary: false,
            CanViewDepartmentExpenseDetails: false,
            CanViewTenantExpenseSummary: false,
            CanViewTenantExpenseDetails: false);
    }
}

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
