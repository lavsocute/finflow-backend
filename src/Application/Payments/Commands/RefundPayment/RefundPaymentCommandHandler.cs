using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RefundPayment;

internal sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<PaymentRefundResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentRefundRepository _paymentRefundRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RefundPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentRefundRepository paymentRefundRepository,
        IExpenseRepository expenseRepository,
        IBudgetRepository budgetRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentRefundRepository = paymentRefundRepository;
        _expenseRepository = expenseRepository;
        _budgetRepository = budgetRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentRefundResponse>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentRefundResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentRefundResponse>(PaymentErrors.NotFound);

        var alreadyRefunded = await _paymentRefundRepository.ExistsByPaymentIdAsync(payment.Id, cancellationToken);
        if (alreadyRefunded)
            return Result.Failure<PaymentRefundResponse>(PaymentErrors.AlreadyRefunded);

        var refundResult = payment.InitiateRefund(request.Amount, request.Reason, _currentTenant.MembershipId.Value);
        if (refundResult.IsFailure)
            return Result.Failure<PaymentRefundResponse>(refundResult.Error);

        var refund = refundResult.Value;
        _paymentRefundRepository.Add(refund);
        _paymentRepository.Update(payment);

        // Reject the linked expense (if any) so budget recalculation excludes the refunded amount.
        var matchingExpense = await _expenseRepository.GetByPaymentIdAsync(payment.Id, cancellationToken);
        if (matchingExpense is not null)
        {
            var expenseEntity = await _expenseRepository.GetEntityByIdAsync(matchingExpense.Id, cancellationToken);
            if (expenseEntity is not null && expenseEntity.Status == ExpenseStatus.Confirmed)
            {
                expenseEntity.Reject($"Refunded: {refund.Reason}", _currentTenant.MembershipId.Value);
                _expenseRepository.Update(expenseEntity);

                var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
                    expenseEntity.IdTenant, expenseEntity.IdDepartment, expenseEntity.Month, expenseEntity.Year, cancellationToken);

                if (budget is not null)
                {
                    var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                        expenseEntity.IdTenant, expenseEntity.IdDepartment, expenseEntity.Month, expenseEntity.Year, cancellationToken);

                    var budgetEntity = await _budgetRepository.GetEntityByIdAsync(budget.Id, expenseEntity.IdTenant, cancellationToken);
                    if (budgetEntity is not null)
                    {
                        budgetEntity.OverwriteSpent(spentAmount);
                        _budgetRepository.Update(budgetEntity);
                    }
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentRefundResponse(
            refund.Id,
            refund.PaymentId,
            refund.Amount,
            refund.Reason,
            refund.Status.ToString(),
            refund.InitiatedByMembershipId,
            refund.InitiatedAt));
    }
}
