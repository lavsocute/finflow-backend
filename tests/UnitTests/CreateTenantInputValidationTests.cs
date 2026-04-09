using FinFlow.Api.GraphQL.Auth;

namespace FinFlow.UnitTests;

public sealed class CreateTenantInputValidationTests
{
    [Fact]
    public void ValidateIsolatedCompanyInfo_ReturnsError_WhenCompanyInfoIsMissing()
    {
        var error = CreateTenantInputValidation.ValidateIsolatedCompanyInfo(null);

        Assert.NotNull(error);
        Assert.Equal("Tenant.CompanyInfoRequired", error!.Code);
    }

    [Fact]
    public void ValidateIsolatedCompanyInfo_ReturnsError_WhenCompanyNameIsBlank()
    {
        var error = CreateTenantInputValidation.ValidateIsolatedCompanyInfo(
            new CompanyInfoInput("   ", "1234567890"));

        Assert.NotNull(error);
        Assert.Equal("Tenant.CompanyNameRequired", error!.Code);
    }

    [Fact]
    public void ValidateIsolatedCompanyInfo_ReturnsError_WhenTaxCodeIsBlank()
    {
        var error = CreateTenantInputValidation.ValidateIsolatedCompanyInfo(
            new CompanyInfoInput("FinFlow Company", "   "));

        Assert.NotNull(error);
        Assert.Equal("Tenant.TaxCodeRequired", error!.Code);
    }
}
