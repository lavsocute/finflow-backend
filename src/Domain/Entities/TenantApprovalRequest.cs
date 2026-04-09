using System.Text.RegularExpressions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Entities;

public sealed class TenantApprovalRequest : Entity
{
    private static readonly Regex TenantCodeRegex = new("^[a-z0-9-]{3,50}$", RegexOptions.Compiled);

    private TenantApprovalRequest(
        Guid id,
        string tenantCode,
        string name,
        string companyName,
        string taxCode,
        string? address,
        string? phone,
        string? contactPerson,
        string? businessType,
        int? employeeCount,
        string currency,
        Guid requestedById,
        DateTime expiresAt)
    {
        Id = id;
        TenantCode = tenantCode;
        Name = name;
        CompanyName = companyName;
        TaxCode = taxCode;
        Address = address;
        Phone = phone;
        ContactPerson = contactPerson;
        BusinessType = businessType;
        EmployeeCount = employeeCount;
        Currency = currency;
        TenancyModel = TenancyModel.Isolated;
        RequestedById = requestedById;
        Status = TenantApprovalStatus.Pending;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private TenantApprovalRequest() { }

    public string TenantCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string CompanyName { get; private set; } = null!;
    public string TaxCode { get; private set; } = null!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? BusinessType { get; private set; }
    public int? EmployeeCount { get; private set; }
    public string Currency { get; private set; } = null!;
    public TenancyModel TenancyModel { get; private set; }
    public Guid RequestedById { get; private set; }
    public TenantApprovalStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    public static Result<TenantApprovalRequest> Create(
        string tenantCode,
        string name,
        string companyName,
        string taxCode,
        string? address,
        string? phone,
        string? contactPerson,
        string? businessType,
        int? employeeCount,
        string currency,
        Guid requestedById,
        DateTime expiresAt)
    {
        var normalizedTenantCode = tenantCode?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedCompanyName = companyName?.Trim() ?? string.Empty;
        var normalizedTaxCode = taxCode?.Trim() ?? string.Empty;
        var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedAddress = NormalizeOptional(address);
        var normalizedPhone = NormalizeOptional(phone);
        var normalizedContactPerson = NormalizeOptional(contactPerson);
        var normalizedBusinessType = NormalizeOptional(businessType);

        if (string.IsNullOrWhiteSpace(normalizedTenantCode))
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.TenantCodeRequired);
        if (!TenantCodeRegex.IsMatch(normalizedTenantCode))
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.InvalidTenantCodeFormat);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.NameRequired);
        if (normalizedName.Length > 150)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.NameTooLong);
        if (string.IsNullOrWhiteSpace(normalizedCompanyName) || string.IsNullOrWhiteSpace(normalizedTaxCode))
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.CompanyInfoRequired);
        if (normalizedCompanyName.Length > 150)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.CompanyNameTooLong);
        if (normalizedTaxCode.Length is < 10 or > 14)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.TaxCodeInvalid);
        if (normalizedAddress?.Length > 500)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.AddressTooLong);
        if (normalizedPhone?.Length > 15)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.PhoneTooLong);
        if (normalizedContactPerson?.Length > 100)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.ContactPersonTooLong);
        if (normalizedBusinessType?.Length > 50)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.BusinessTypeTooLong);
        if (employeeCount.HasValue && employeeCount.Value <= 0)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.InvalidEmployeeCount);
        if (string.IsNullOrWhiteSpace(normalizedCurrency) || normalizedCurrency.Length != 3)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.InvalidCurrency);
        if (requestedById == Guid.Empty)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.RequestedByRequired);
        if (expiresAt <= DateTime.UtcNow)
            return Result.Failure<TenantApprovalRequest>(TenantApprovalRequestErrors.ExpirationRequired);

        return Result.Success(new TenantApprovalRequest(
            Guid.NewGuid(),
            normalizedTenantCode,
            normalizedName,
            normalizedCompanyName,
            normalizedTaxCode,
            normalizedAddress,
            normalizedPhone,
            normalizedContactPerson,
            normalizedBusinessType,
            employeeCount,
            normalizedCurrency,
            requestedById,
            expiresAt));
    }

    public Result Approve()
    {
        if (Status != TenantApprovalStatus.Pending)
            return Result.Failure(TenantApprovalRequestErrors.AlreadyProcessed);

        if (IsExpired)
            return Result.Failure(TenantApprovalRequestErrors.Expired);

        Status = TenantApprovalStatus.Approved;
        RejectionReason = null;
        RejectedAt = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != TenantApprovalStatus.Pending)
            return Result.Failure(TenantApprovalRequestErrors.AlreadyProcessed);

        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReason))
            return Result.Failure(TenantApprovalRequestErrors.RejectionReasonRequired);
        if (normalizedReason.Length > 500)
            return Result.Failure(TenantApprovalRequestErrors.RejectionReasonTooLong);

        Status = TenantApprovalStatus.Rejected;
        RejectionReason = normalizedReason;
        RejectedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
