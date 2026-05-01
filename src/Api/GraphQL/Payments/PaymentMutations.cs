using FinFlow.Application.Payments.Commands.RecordPayment;
using FinFlow.Application.Payments.Commands.ConfirmPayment;
using FinFlow.Application.Payments.Commands.RejectPayment;
using FinFlow.Application.Expenses.Commands.RejectExpense;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Api.GraphQL.Documents;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Payments;

public sealed record RecordPaymentInput(
    Guid DocumentId,
    decimal Amount,
    string CurrencyCode,
    string PaymentMethod,
    string? Notes,
    decimal? ExchangeRate);

[ExtendObjectType(typeof(FinFlow.Api.GraphQL.Auth.AuthMutations))]
public sealed class PaymentMutations
{
    [Authorize]
    public async Task<PaymentPayload> RecordPaymentAsync(
        RecordPaymentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureAccountantRole(context);
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(
            new RecordPaymentCommand(
                input.DocumentId,
                input.Amount,
                input.CurrencyCode,
                input.PaymentMethod,
                input.Notes,
                input.ExchangeRate),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return PaymentPayload.FromResponse(result.Value);
    }

    internal static Guid GetRequiredGuidClaim(
        IResolverContext context,
        string claimType,
        string unauthorizedMessage = "User is not authenticated or token is invalid")
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawValue = user?.FindFirst(claimType)?.Value;

        if (Guid.TryParse(rawValue, out var value))
            return value;

        throw new GraphQLException(new HotChocolate.Error(unauthorizedMessage, "Account.Unauthorized"));
    }

    internal static void EnsureAccountantRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role))
        {
            if (role is RoleType.Accountant or RoleType.Manager or RoleType.TenantAdmin)
                return;
        }

        throw ToGraphQlException(new DomainError("Payment.Forbidden", "Only Accountant, Manager, or Admin can record payments."));
    }

    [Authorize]
    public async Task<PaymentPayload> ConfirmPaymentAsync(
        Guid paymentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureManagerRole(context);

        var result = await mediator.Send(new ConfirmPaymentCommand(paymentId), cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return PaymentPayload.FromResponse(result.Value);
    }

    [Authorize]
    public async Task<PaymentPayload> RejectPaymentAsync(
        Guid paymentId,
        string reason,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureManagerRole(context);

        var result = await mediator.Send(new RejectPaymentCommand(paymentId, reason), cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return PaymentPayload.FromResponse(result.Value);
    }

    [Authorize]
    public async Task<bool> RejectExpenseAsync(
        Guid expenseId,
        string reason,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureManagerRole(context);

        var result = await mediator.Send(new RejectExpenseCommand(expenseId, reason), cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    internal static void EnsureManagerRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role))
        {
            if (role is RoleType.Manager or RoleType.TenantAdmin)
                return;
        }

        throw ToGraphQlException(new DomainError("Payment.Forbidden", "Only Manager or Admin can confirm/reject payments."));
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}