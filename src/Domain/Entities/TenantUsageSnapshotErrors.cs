using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantUsageSnapshotErrors
{
    public static readonly Error TenantRequired = new("TenantUsageSnapshot.TenantRequired", "Tenant is required.");
    public static readonly Error InvalidPeriod = new("TenantUsageSnapshot.InvalidPeriod", "Usage snapshot period is invalid.");
    public static readonly Error OcrUsageMustBePositive = new("TenantUsageSnapshot.OcrUsageMustBePositive", "OCR usage must be positive.");
    public static readonly Error ChatbotUsageMustBePositive = new("TenantUsageSnapshot.ChatbotUsageMustBePositive", "Chatbot usage must be positive.");
    public static readonly Error StorageUsageCannotBeNegative = new("TenantUsageSnapshot.StorageUsageCannotBeNegative", "Storage usage cannot be negative.");
}
