using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests;

public sealed class VendorTests
{
    [Fact]
    public void Create_SetsCorrectDefaults()
    {
        var tenantId = Guid.NewGuid();
        var taxCode = "0123456789";
        var name = "Test Vendor Co";

        var result = Vendor.Create(tenantId, taxCode, name);

        Assert.True(result.IsSuccess);
        var vendor = result.Value;
        Assert.Equal(tenantId, vendor.IdTenant);
        Assert.Equal(taxCode, vendor.TaxCode);
        Assert.Equal(name, vendor.Name);
        Assert.False(vendor.IsVerified);
        Assert.Null(vendor.VerifiedByMembershipId);
        Assert.Null(vendor.VerifiedAt);
        Assert.True(vendor.IsActive);
    }

    [Fact]
    public void Create_FailsWhenTenantIdEmpty()
    {
        var result = Vendor.Create(Guid.Empty, "0123456789", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TenantRequired", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeEmpty()
    {
        var result = Vendor.Create(Guid.NewGuid(), "", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeRequired", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeTooShort()
    {
        var result = Vendor.Create(Guid.NewGuid(), "012345678", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeTooLong()
    {
        var result = Vendor.Create(Guid.NewGuid(), "01234567890123456789", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeHasLetters()
    {
        var result = Vendor.Create(Guid.NewGuid(), "012345678A", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeHasSpaces()
    {
        var result = Vendor.Create(Guid.NewGuid(), "012 345 6789", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeHasSpecialCharacters()
    {
        var result = Vendor.Create(Guid.NewGuid(), "01234-56789", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_NormalizesTaxCodeToUppercase()
    {
        var result = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor");

        Assert.True(result.IsSuccess);
        Assert.Equal("0123456789", result.Value.TaxCode);
    }

    [Fact]
    public void Create_Accepts10DigitTaxCode()
    {
        var result = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_Accepts13DigitTaxCode()
    {
        var result = Vendor.Create(Guid.NewGuid(), "0123456789012", "Test Vendor");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_Accepts14DigitTaxCode()
    {
        var result = Vendor.Create(Guid.NewGuid(), "01234567890123", "Test Vendor");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeIs9Digits()
    {
        var result = Vendor.Create(Guid.NewGuid(), "012345678", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenTaxCodeIs15Digits()
    {
        var result = Vendor.Create(Guid.NewGuid(), "012345678901234", "Test Vendor");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeInvalid", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenNameEmpty()
    {
        var result = Vendor.Create(Guid.NewGuid(), "0123456789", "");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.NameRequired", result.Error.Code);
    }

    [Fact]
    public void Create_FailsWhenNameTooLong()
    {
        var longName = new string('A', 201);
        var result = Vendor.Create(Guid.NewGuid(), "0123456789", longName);

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.NameTooLong", result.Error.Code);
    }

    [Fact]
    public void Create_TrimsTaxCodeAndName()
    {
        var result = Vendor.Create(Guid.NewGuid(), "  0123456789  ", "  Test Vendor  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("0123456789", result.Value.TaxCode);
        Assert.Equal("Test Vendor", result.Value.Name);
    }

    [Fact]
    public void Verify_SetsIsVerifiedAndMetadata()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;
        var membershipId = Guid.NewGuid();

        var result = vendor.Verify(membershipId);

        Assert.True(result.IsSuccess);
        Assert.True(vendor.IsVerified);
        Assert.Equal(membershipId, vendor.VerifiedByMembershipId);
        Assert.NotNull(vendor.VerifiedAt);
    }

    [Fact]
    public void Verify_FailsWhenVerifierMembershipIdEmpty()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;

        var result = vendor.Verify(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.VerifierMembershipIdRequired", result.Error.Code);
    }

    [Fact]
    public void Verify_FailsWhenAlreadyVerified()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;
        vendor.Verify(Guid.NewGuid());

        var result = vendor.Verify(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.AlreadyVerified", result.Error.Code);
    }

    [Fact]
    public void Unverify_FailsWhenNotVerified()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;

        var result = vendor.Unverify();

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.NotVerified", result.Error.Code);
    }

    [Fact]
    public void Unverify_ClearsVerificationMetadata()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;
        vendor.Verify(Guid.NewGuid());

        var result = vendor.Unverify();

        Assert.True(result.IsSuccess);
        Assert.False(vendor.IsVerified);
        Assert.Null(vendor.VerifiedByMembershipId);
        Assert.Null(vendor.VerifiedAt);
    }

    [Fact]
    public void UpdateInfo_FailsWhenNameEmpty()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;

        var result = vendor.UpdateInfo("");

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.NameRequired", result.Error.Code);
    }

    [Fact]
    public void UpdateInfo_Success()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;

        var result = vendor.UpdateInfo("Updated Vendor Name");

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Vendor Name", vendor.Name);
    }

    [Fact]
    public void Deactivate_Success()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;

        var result = vendor.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.False(vendor.IsActive);
    }

    [Fact]
    public void Deactivate_Idempotent()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;
        vendor.Deactivate();

        var result = vendor.Deactivate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Activate_Success()
    {
        var vendor = Vendor.Create(Guid.NewGuid(), "0123456789", "Test Vendor").Value;
        vendor.Deactivate();

        var result = vendor.Activate();

        Assert.True(result.IsSuccess);
        Assert.True(vendor.IsActive);
    }
}