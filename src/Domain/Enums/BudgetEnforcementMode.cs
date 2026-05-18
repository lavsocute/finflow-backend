namespace FinFlow.Domain.Enums;

/// <summary>
/// How strictly a budget is enforced. Configurable per budget so different
/// departments can opt-in to harder limits while others tolerate overruns.
/// </summary>
public enum BudgetEnforcementMode
{
    /// <summary>Track only — never block. Used for visibility-only deployments.</summary>
    Off = 0,

    /// <summary>Allow over-budget but require manager+ to supply a written
    /// justification when approving the document. Default for new budgets.</summary>
    SoftBlock = 1,

    /// <summary>Reject over-budget approvals/payments outright.</summary>
    HardBlock = 2
}

public enum BudgetExceededTrigger
{
    ApproveDocument = 0,
    RecordPayment = 1,
    ConfirmPayment = 2
}
