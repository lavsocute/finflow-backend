using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Enums;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class PlanEntitlementCatalog
{
    public PlanEntitlements GetFor(PlanTier planTier) =>
        planTier switch
        {
            PlanTier.Free => new PlanEntitlements(
                DocumentsManualEntryEnabled: true,
                DocumentsOcrEnabled: false,
                ChatbotEnabled: false,
                StorageLimitBytes: 1L * 1024 * 1024 * 1024,
                WorkspaceMonthlyOcrPages: 0,
                MemberMonthlyOcrPages: 0,
                WorkspaceMonthlyChatbotMessages: 0,
                MemberMonthlyChatbotMessages: 0),
            PlanTier.Pro => new PlanEntitlements(
                DocumentsManualEntryEnabled: true,
                DocumentsOcrEnabled: true,
                ChatbotEnabled: true,
                StorageLimitBytes: 10L * 1024 * 1024 * 1024,
                WorkspaceMonthlyOcrPages: 1_000,
                MemberMonthlyOcrPages: 100,
                WorkspaceMonthlyChatbotMessages: 10_000,
                MemberMonthlyChatbotMessages: 500),
            PlanTier.Enterprise => new PlanEntitlements(
                DocumentsManualEntryEnabled: true,
                DocumentsOcrEnabled: true,
                ChatbotEnabled: true,
                StorageLimitBytes: 100L * 1024 * 1024 * 1024,
                WorkspaceMonthlyOcrPages: 10_000,
                MemberMonthlyOcrPages: 1_000,
                WorkspaceMonthlyChatbotMessages: 100_000,
                MemberMonthlyChatbotMessages: 5_000),
            _ => throw new ArgumentOutOfRangeException(nameof(planTier), planTier, null)
        };
}
