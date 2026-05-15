using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Tenant : Entity, ISoftDeletable
{
    private Tenant(
        Guid id,
        string name,
        string tenantCode,
        TenancyModel tenancyModel,
        string currency,
        string? companyName,
        string? taxCode)
    {
        Id = id;
        Name = name;
        TenantCode = tenantCode;
        TenancyModel = tenancyModel;
        Currency = currency;
        CompanyName = companyName;
        TaxCode = taxCode;
        CreatedAt = DateTime.UtcNow;
    }

    private Tenant() { }

    public string Name { get; private set; } = null!;
    public string TenantCode { get; private set; } = null!;
    public TenancyModel TenancyModel { get; private set; }
    public string? ConnectionString { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? CompanyName { get; private set; }
    public string? TaxCode { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<Tenant> Create(
        string name,
        string tenantCode,
        TenancyModel tenancyModel = TenancyModel.Shared,
        string currency = "VND",
        string? companyName = null,
        string? taxCode = null)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedTenantCode = tenantCode?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedCompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        var normalizedTaxCode = string.IsNullOrWhiteSpace(taxCode) ? null : taxCode.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
            return Result.Failure<Tenant>(TenantErrors.NameRequired);
        if (normalizedName.Length > 150)
            return Result.Failure<Tenant>(TenantErrors.NameTooLong);
        if (string.IsNullOrWhiteSpace(normalizedTenantCode))
            return Result.Failure<Tenant>(TenantErrors.CodeRequired);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalizedTenantCode, @"^[a-z0-9-]{3,50}$"))
            return Result.Failure<Tenant>(TenantErrors.InvalidCodeFormat);
        if (!string.IsNullOrWhiteSpace(normalizedCompanyName) && normalizedCompanyName.Length > 150)
            return Result.Failure<Tenant>(TenantErrors.CompanyNameTooLong);
        if (!string.IsNullOrWhiteSpace(normalizedTaxCode) && normalizedTaxCode.Length is < 10 or > 14)
            return Result.Failure<Tenant>(TenantErrors.TaxCodeInvalid);
        if (string.IsNullOrWhiteSpace(normalizedCurrency) || normalizedCurrency.Length != 3)
            return Result.Failure<Tenant>(TenantErrors.InvalidCurrency);

        var tenant = new Tenant(
            Guid.NewGuid(),
            normalizedName,
            normalizedTenantCode,
            tenancyModel,
            normalizedCurrency,
            normalizedCompanyName,
            normalizedTaxCode);
        tenant.RaiseDomainEvent(new TenantCreatedDomainEvent(tenant.Id, tenant.TenantCode, tenant.Name));
        return tenant;
    }

    public Result ChangeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(TenantErrors.NameRequired);
        if (name.Length > 150)
            return Result.Failure(TenantErrors.NameTooLong);
        Name = name.Trim();
        return Result.Success();
    }

    public Result ChangeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            return Result.Failure(TenantErrors.InvalidCurrency);
        Currency = currency.ToUpperInvariant();
        return Result.Success();
    }

    public Result SetCompanyInfo(string? companyName, string? taxCode)
    {
        if (!string.IsNullOrWhiteSpace(companyName) && companyName.Trim().Length > 150)
            return Result.Failure(TenantErrors.CompanyNameTooLong);

        if (!string.IsNullOrWhiteSpace(taxCode) && taxCode.Trim().Length is < 10 or > 14)
            return Result.Failure(TenantErrors.TaxCodeInvalid);

        CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        TaxCode = string.IsNullOrWhiteSpace(taxCode) ? null : taxCode.Trim();
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive) return Result.Failure(TenantErrors.AlreadyDeactivated);
        IsActive = false;
        RaiseDomainEvent(new TenantDeactivatedDomainEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive) return Result.Failure(TenantErrors.AlreadyActive);
        IsActive = true;
        RaiseDomainEvent(new TenantActivatedDomainEvent(Id));
        return Result.Success();
    }
}
