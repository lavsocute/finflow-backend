using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.ConfirmPayment;

internal sealed class ConfirmPaymentCommandHandler : IRequestHandler<ConfirmPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IExpenseRepository expenseRepository,
        IReviewedDocumentRepository documentRepository,
        ICategoryRepository categoryRepository,
        IBudgetRepository budgetRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _expenseRepository = expenseRepository;
        _documentRepository = documentRepository;
        _categoryRepository = categoryRepository;
        _budgetRepository = budgetRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentResponse>> Handle(ConfirmPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.Id.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.TenantContext", "Tenant context is not available."));
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentResponse>(PaymentErrors.NotFound);

        if (payment.Status != PaymentStatus.Pending)
            return Result.Failure<PaymentResponse>(PaymentErrors.AlreadyProcessed);

        var confirmResult = payment.Confirm(_currentTenant.MembershipId.Value, request.ExecutionReference);
        if (confirmResult.IsFailure)
            return Result.Failure<PaymentResponse>(confirmResult.Error);

        _paymentRepository.Update(payment);

        var document = await _documentRepository.GetByIdForUpdateAsync(payment.DocumentId, _currentTenant.Id.Value, cancellationToken);

        var expenseDate = document?.DocumentDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow;

        await CreateExpenseAndUpdateBudgetAsync(payment, expenseDate, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentResponse(
            payment.Id,
            payment.DocumentId,
            payment.Amount,
            payment.CurrencyCode.ToString(),
            payment.AmountInVnd,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.RecordedAt,
            payment.RecordedByMembershipId,
            payment.Notes));
    }

    private async Task CreateExpenseAndUpdateBudgetAsync(Domain.Expenses.Payment payment, DateTime documentDate, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetByTenantIdAsync(_currentTenant.Id.Value, includeInactive: true, cancellationToken);
        var category = categories.FirstOrDefault(c => c.IsSystem) ?? categories.FirstOrDefault();

        if (category is null)
            return;

        var expenseResult = Expense.Create(
            payment.IdTenant,
            payment.IdDepartment,
            payment.DocumentId,
            payment.Id,
            category.Id,
            "Payment",
            payment.Amount,
            payment.CurrencyCode,
            payment.AmountInVnd,
            documentDate.Month,
            documentDate.Year,
            documentDate,
            _currentTenant.MembershipId!.Value);

        if (expenseResult.IsFailure)
            return;

        _expenseRepository.Add(expenseResult.Value);

        var budgetSummary = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            payment.IdDepartment, documentDate.Month, documentDate.Year, cancellationToken);

        if (budgetSummary is not null)
        {
            var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                payment.IdDepartment, documentDate.Month, documentDate.Year, cancellationToken);

            var budgetToUpdate = await _budgetRepository.GetEntityByIdAsync(budgetSummary.Id, cancellationToken);
            if (budgetToUpdate is not null)
            {
                budgetToUpdate.RecalculateSpent(spentAmount);
                _budgetRepository.Update(budgetToUpdate);
            }
        }
    }
}
