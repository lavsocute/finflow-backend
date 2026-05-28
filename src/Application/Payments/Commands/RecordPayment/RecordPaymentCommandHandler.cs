using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Employees;
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
    private readonly IEmployeeReimbursementProfileRepository _profileRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    private const decimal DefaultExchangeRate = 1m;
    private static readonly HashSet<PaymentMethod> AllowedReimbursementMethods =
    [
        PaymentMethod.BankTransfer,
        PaymentMethod.Payroll,
        PaymentMethod.Cash,
        PaymentMethod.Other
    ];

    public RecordPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IExpenseRepository expenseRepository,
        IReviewedDocumentRepository documentRepository,
        IBudgetRepository budgetRepository,
        ICategoryRepository categoryRepository,
        IEmployeeReimbursementProfileRepository profileRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _expenseRepository = expenseRepository;
        _documentRepository = documentRepository;
        _budgetRepository = budgetRepository;
        _categoryRepository = categoryRepository;
        _profileRepository = profileRepository;
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

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod))
            return Result.Failure<PaymentResponse>(new Error("Payment.InvalidMethod", $"Payment method '{request.PaymentMethod}' is not supported."));

        if (!AllowedReimbursementMethods.Contains(paymentMethod))
            return Result.Failure<PaymentResponse>(new Error("Payment.InvalidMethod", $"Payment method '{request.PaymentMethod}' is not supported for employee reimbursement."));

        // BankTransfer requires the employee to have configured their reimbursement
        // profile (encrypted bank account). Other methods (Cash, Payroll, Other) work
        // without a profile so accountant can still pay legacy/cash-only employees.
        if (paymentMethod == PaymentMethod.BankTransfer)
        {
            var profile = await _profileRepository.GetByMembershipIdAsync(
                document.MembershipId, cancellationToken);
            if (profile is null || !profile.HasBankInfo)
                return Result.Failure<PaymentResponse>(PaymentErrors.BankInfoMissing);
        }

        var existingPayment = await _paymentRepository.GetByDocumentIdAsync(request.DocumentId, cancellationToken);
        if (existingPayment is not null)
        {
            if (existingPayment.Status is not (PaymentStatus.Rejected or PaymentStatus.Cancelled))
                return Result.Failure<PaymentResponse>(PaymentErrors.DocumentAlreadyHasPayment);

            var retryPayment = await _paymentRepository.GetEntityByIdAsync(existingPayment.Id, cancellationToken);
            if (retryPayment is null)
                return Result.Failure<PaymentResponse>(PaymentErrors.NotFound);

            var retryResult = retryPayment.Reschedule(
                _currentTenant.MembershipId.Value,
                paymentMethod,
                request.Notes);

            if (retryResult.IsFailure)
                return Result.Failure<PaymentResponse>(retryResult.Error);

            _paymentRepository.Update(retryPayment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(retryPayment));
        }

        // Multi-currency: derive from document. Document carries its native currency
        // and the exchange rate captured at upload/review time. Payment inherits the
        // same snapshot so historical reports remain bound to the original rate.
        var currencyCode = document.CurrencyCode;
        var exchangeRate = document.ExchangeRate;
        var baseCurrencyCode = document.BaseCurrencyCode;

        var paymentResult = Payment.Create(
            _currentTenant.Id.Value,
            request.DocumentId,
            document.IdDepartment,
            document.TotalAmount,
            currencyCode,
            exchangeRate,
            baseCurrencyCode,
            _currentTenant.MembershipId.Value,
            paymentMethod,
            request.Notes);

        if (paymentResult.IsFailure)
            return Result.Failure<PaymentResponse>(paymentResult.Error);

        var payment = paymentResult.Value;

        _paymentRepository.Add(payment);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(payment));
    }

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.DocumentId,
        payment.Amount,
        payment.CurrencyCode,
        payment.AmountInBaseCurrency,
        payment.BaseCurrencyCode,
        payment.ExchangeRate,
        payment.Method.ToString(),
        payment.Status.ToString(),
        payment.RecordedAt,
        payment.RecordedByMembershipId,
        payment.Notes);
}
