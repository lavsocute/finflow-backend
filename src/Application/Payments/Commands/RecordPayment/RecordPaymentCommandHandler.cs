using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RecordPayment;

internal sealed class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    private const decimal DefaultAutoConfirmThreshold = 5_000_000m;
    private const decimal DefaultExchangeRate = 1m;

    public RecordPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IExpenseRepository expenseRepository,
        IReviewedDocumentRepository documentRepository,
        IBudgetRepository budgetRepository,
        ICategoryRepository categoryRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _expenseRepository = expenseRepository;
        _documentRepository = documentRepository;
        _budgetRepository = budgetRepository;
        _categoryRepository = categoryRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentResponse>> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.Id.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.TenantContext", "Tenant context is not available."));
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var document = await _documentRepository.GetByIdForUpdateAsync(request.DocumentId, _currentTenant.Id.Value, cancellationToken);
        if (document is null)
            return Result.Failure<PaymentResponse>(ReviewedDocumentErrors.NotFound);

        if (document.Status != ReviewedDocumentStatus.Approved)
            return Result.Failure<PaymentResponse>(PaymentErrors.DocumentNotApproved);

        var hasPayment = await _paymentRepository.ExistsByDocumentIdAsync(request.DocumentId, cancellationToken);
        if (hasPayment)
            return Result.Failure<PaymentResponse>(PaymentErrors.DocumentAlreadyHasPayment);

        if (!Enum.TryParse<CurrencyCode>(request.CurrencyCode, ignoreCase: true, out var currencyCode))
            return Result.Failure<PaymentResponse>(new Error("Payment.InvalidCurrency", $"Currency '{request.CurrencyCode}' is not supported."));

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod))
            return Result.Failure<PaymentResponse>(new Error("Payment.InvalidMethod", $"Payment method '{request.PaymentMethod}' is not supported."));

        var exchangeRate = request.ExchangeRate ?? (currencyCode == CurrencyCode.VND ? DefaultExchangeRate : 1m);

        var paymentResult = Payment.Create(
            _currentTenant.Id.Value,
            request.DocumentId,
            document.IdDepartment,
            request.Amount,
            currencyCode,
            exchangeRate,
            _currentTenant.MembershipId.Value,
            paymentMethod,
            request.Notes);

        if (paymentResult.IsFailure)
            return Result.Failure<PaymentResponse>(paymentResult.Error);

        var payment = paymentResult.Value;

        bool autoConfirmed = false;
        if (payment.AmountInVnd < DefaultAutoConfirmThreshold)
        {
            payment.AutoConfirm();
            autoConfirmed = true;
        }

        _paymentRepository.Add(payment);

        if (autoConfirmed)
        {
            await CreateExpenseAndUpdateBudgetAsync(payment, document, cancellationToken);
        }

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

    private async Task CreateExpenseAndUpdateBudgetAsync(Payment payment, ReviewedDocument document, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetByTenantIdAsync(_currentTenant.Id.Value, includeInactive: true, cancellationToken);

        var category = categories.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(document.Category) &&
            c.Name.Equals(document.Category, StringComparison.OrdinalIgnoreCase))
            ?? categories.FirstOrDefault(c => c.IsSystem)
            ?? categories.FirstOrDefault();

        if (category == null)
            return;

        var expense = Expense.Create(
            payment.IdTenant,
            payment.IdDepartment,
            payment.DocumentId,
            payment.Id,
            category.Id,
            document.VendorName,
            payment.Amount,
            payment.CurrencyCode,
            payment.AmountInVnd,
            document.DocumentDate.Month,
            document.DocumentDate.Year,
            document.DocumentDate.ToDateTime(TimeOnly.MinValue),
            _currentTenant.MembershipId.Value);

        _expenseRepository.Add(expense);

        var budget = await _budgetRepository.GetEntityByIdAsync(
            await GetBudgetIdAsync(payment.IdDepartment, document.DocumentDate.Month, document.DocumentDate.Year, cancellationToken),
            cancellationToken);

        if (budget != null)
        {
            var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                payment.IdDepartment, document.DocumentDate.Month, document.DocumentDate.Year, cancellationToken);

            budget.RecalculateSpent(spentAmount);
            _budgetRepository.Update(budget);
        }
    }

    private async Task<Guid> GetBudgetIdAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken)
    {
        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(departmentId, month, year, cancellationToken);
        return budget?.Id ?? Guid.Empty;
    }
}