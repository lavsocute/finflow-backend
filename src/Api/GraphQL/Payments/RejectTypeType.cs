using FinFlow.Domain.Expenses;
using HotChocolate.Types;

namespace FinFlow.Api.GraphQL.Payments;

public sealed class RejectTypeType : EnumType<PaymentRejectType>
{
    protected override void Configure(IEnumTypeDescriptor<PaymentRejectType> descriptor)
    {
        descriptor.Name("RejectType");

        descriptor.Value(PaymentRejectType.InsufficientDocumentation).Name("INSUFFICIENT_DOCUMENTATION");
        descriptor.Value(PaymentRejectType.DuplicateClaim).Name("DUPLICATE_CLAIM");
        descriptor.Value(PaymentRejectType.PolicyViolation).Name("POLICY_VIOLATION");
        descriptor.Value(PaymentRejectType.InvalidAmount).Name("INVALID_AMOUNT");
        descriptor.Value(PaymentRejectType.NotReimbursable).Name("NOT_REIMBURSABLE");
        descriptor.Value(PaymentRejectType.Other).Name("OTHER");
    }
}
