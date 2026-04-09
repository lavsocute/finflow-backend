using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Auth;

public static class CreateTenantInputValidation
{
    public static DomainError? ValidateIsolatedCompanyInfo(CompanyInfoInput? companyInfo)
    {
        if (companyInfo is null)
            return new DomainError("Tenant.CompanyInfoRequired", "Company information is required");

        if (string.IsNullOrWhiteSpace(companyInfo.CompanyName))
            return new DomainError("Tenant.CompanyNameRequired", "Company name is required");

        if (string.IsNullOrWhiteSpace(companyInfo.TaxCode))
            return new DomainError("Tenant.TaxCodeRequired", "Tax code is required");

        return null;
    }
}
