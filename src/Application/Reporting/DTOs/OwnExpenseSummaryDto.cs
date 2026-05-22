namespace FinFlow.Application.Reporting.DTOs;

public sealed record OwnExpenseSummaryDto(
    decimal TotalAmountInBaseCurrency,
    string BaseCurrencyCode,
    int ExpenseCount);
