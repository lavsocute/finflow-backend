using FinFlow.Application.Payments.Queries.GetPaymentDetail;
using FinFlow.Application.Payments.Queries.GetPaymentQueue;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;

namespace FinFlow.Api.GraphQL.Payments;

public sealed record PaymentQueueItemPayload(
    Guid? PaymentId,
    Guid DocumentId,
    string Reference,
    string DocumentFileName,
    string EmployeeName,
    string EmployeeMembershipId,
    string? EmployeeCode,
    string? MerchantName,
    string Department,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    DateOnly ExpenseDate,
    DateTime SubmittedAt,
    string QueueStatus,
    string? PaymentMethod,
    DateTime? RecordedAt,
    DateTime? ConfirmedAt,
    string? RejectionReason,
    string? Notes);

public sealed record PaymentAuditTrailItemPayload(
    string Type,
    string Title,
    string Actor,
    DateTime Timestamp,
    string? Note);

public sealed record PaymentDetailPayload(
    Guid? PaymentId,
    Guid DocumentId,
    string Reference,
    string? SettlementRef,
    string ApprovalRecordId,
    string EmployeeName,
    string EmployeeMembershipId,
    string? EmployeeCode,
    string? MerchantName,
    string Department,
    string? CostCenter,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    DateOnly ExpenseDate,
    string? PaymentMethod,
    string QueueStatus,
    string DocumentFileName,
    string? DocumentDownloadUrl,
    string? DocumentViewUrl,
    IReadOnlyList<PaymentAuditTrailItemPayload> AuditTrail,
    string? MethodSource,
    bool MethodEditable);

[ExtendObjectType(typeof(global::Query))]
public sealed class PaymentQueries
{
    [Authorize]
    public async Task<IReadOnlyList<PaymentQueueItemPayload>> PaymentQueueAsync(
        string? status,
        string? search,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        PaymentMutations.EnsureAccountantRole(context);
        var tenantId = PaymentMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(
            new GetPaymentQueueQuery(tenantId, status, search),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new PaymentQueueItemPayload(
                x.PaymentId,
                x.DocumentId,
                x.Reference,
                x.DocumentFileName,
                x.EmployeeName,
                x.EmployeeMembershipId,
                x.EmployeeCode,
                x.MerchantName,
                x.Department,
                x.Amount,
                x.CurrencyCode,
                x.AmountInBaseCurrency,
                x.ExpenseDate,
                x.SubmittedAt,
                x.QueueStatus,
                x.PaymentMethod,
                x.RecordedAt,
                x.ConfirmedAt,
                x.RejectionReason,
                x.Notes))
            .ToList();
    }

    [Authorize]
    public async Task<PaymentDetailPayload?> PaymentDetailAsync(
        Guid? paymentId,
        Guid? documentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        PaymentMutations.EnsureAccountantRole(context);
        var tenantId = PaymentMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(
            new GetPaymentDetailQuery(tenantId, paymentId, documentId),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        if (result.Value is null)
            return null;

        var detail = result.Value;

        return new PaymentDetailPayload(
            detail.PaymentId,
            detail.DocumentId,
            detail.Reference,
            detail.SettlementRef,
            detail.ApprovalRecordId,
            detail.EmployeeName,
            detail.EmployeeMembershipId,
            detail.EmployeeCode,
            detail.MerchantName,
            detail.Department,
            detail.CostCenter,
            detail.Amount,
            detail.CurrencyCode,
            detail.AmountInBaseCurrency,
            detail.ExpenseDate,
            detail.PaymentMethod,
            detail.QueueStatus,
            detail.DocumentFileName,
            detail.DocumentDownloadUrl,
            detail.DocumentViewUrl,
            detail.AuditTrail.Select(x => new PaymentAuditTrailItemPayload(
                x.Type,
                x.Title,
                x.Actor,
                x.Timestamp,
                x.Note)).ToList(),
            detail.MethodSource,
            detail.MethodEditable);
    }
}
