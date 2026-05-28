using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Employees.Commands.ConfirmBankInfoUpdate;
using FinFlow.Application.Employees.Commands.RequestBankInfoUpdateOtp;
using FinFlow.Application.Employees.Commands.UpdateMyReimbursementProfile;
using FinFlow.Domain.Expenses;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Employees;

[ExtendObjectType(typeof(AuthMutations))]
public sealed class ReimbursementProfileMutations
{
    /// <summary>
    /// Update non-bank fields (preferred method, contact phone, reimbursement email,
    /// tax id). No OTP required since these are not high-impact changes.
    /// </summary>
    [Authorize]
    public async Task<ReimbursementProfilePayload> UpdateMyReimbursementProfileAsync(
        UpdateMyReimbursementProfileInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");

        PaymentMethod? preferred = null;
        if (!string.IsNullOrWhiteSpace(input.PreferredPaymentMethod))
        {
            if (!Enum.TryParse<PaymentMethod>(input.PreferredPaymentMethod, ignoreCase: true, out var parsed))
                throw new GraphQLException(new HotChocolate.Error(
                    $"Unknown payment method '{input.PreferredPaymentMethod}'.",
                    "Profile.InvalidPaymentMethod"));
            preferred = parsed;
        }

        var result = await mediator.Send(
            new UpdateMyReimbursementProfileCommand(
                tenantId,
                membershipId,
                preferred,
                input.ContactPhone,
                input.ReimbursementEmail,
                input.TaxId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ReimbursementProfilePayload.FromResponse(result.Value)!;
    }

    /// <summary>
    /// Step 1 of bank info update: send OTP to caller's email. The OTP must be
    /// confirmed via <c>confirmBankInfoUpdate</c> within 5 minutes.
    /// </summary>
    [Authorize]
    public async Task<OtpDispatchPayload> RequestBankInfoUpdateOtpAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var accountId = GetRequiredGuidClaim(context, "sub");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");

        var result = await mediator.Send(
            new RequestBankInfoUpdateOtpCommand(tenantId, accountId, membershipId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new OtpDispatchPayload
        {
            ChallengeId = result.Value.ChallengeId,
            CooldownSeconds = result.Value.CooldownSeconds
        };
    }

    /// <summary>
    /// Step 2 of bank info update: submit the OTP along with the new bank fields.
    /// Pass null/empty for all bank fields to clear the bank info (employee opts
    /// out of bank transfer).
    /// </summary>
    [Authorize]
    public async Task<ReimbursementProfilePayload> ConfirmBankInfoUpdateAsync(
        ConfirmBankInfoUpdateInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var accountId = GetRequiredGuidClaim(context, "sub");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");

        var result = await mediator.Send(
            new ConfirmBankInfoUpdateCommand(
                tenantId,
                accountId,
                membershipId,
                input.ChallengeId,
                input.Otp,
                input.BankCode,
                input.BankAccountNumber,
                input.BankAccountHolderName,
                input.BankBranch),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ReimbursementProfilePayload.FromResponse(result.Value)!;
    }

    private static Guid GetRequiredGuidClaim(IResolverContext context, string claimType)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var raw = user?.FindFirst(claimType)?.Value
            ?? (claimType == "sub" ? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value : null);
        if (Guid.TryParse(raw, out var value))
            return value;

        throw new GraphQLException(new HotChocolate.Error(
            "User is not authenticated or token is invalid", "Account.Unauthorized"));
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
