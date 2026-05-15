using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class MemberUsageSnapshotErrors
{
    public static readonly Error TenantRequired = new("MemberUsageSnapshot.TenantRequired", "Tenant is required.");
    public static readonly Error MembershipRequired = new("MemberUsageSnapshot.MembershipRequired", "Membership is required.");
    public static readonly Error InvalidPeriod = new("MemberUsageSnapshot.InvalidPeriod", "Usage snapshot period is invalid.");
    public static readonly Error OcrUsageMustBePositive = new("MemberUsageSnapshot.OcrUsageMustBePositive", "OCR usage must be positive.");
    public static readonly Error ChatbotUsageMustBePositive = new("MemberUsageSnapshot.ChatbotUsageMustBePositive", "Chatbot usage must be positive.");
}
