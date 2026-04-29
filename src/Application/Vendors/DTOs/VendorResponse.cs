namespace FinFlow.Application.Vendors.DTOs;

public sealed record VendorResponse(
    Guid VendorId,
    string TaxCode,
    string Name,
    bool IsVerified,
    Guid? VerifiedByMembershipId,
    DateTime? VerifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);