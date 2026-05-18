using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Reporting.DTOs;

/// <summary>
/// Date range parameter shared by all reporting queries. Inclusive on both
/// ends. Caller (typically GraphQL resolver) is responsible for translating
/// "this month", "last quarter" presets into concrete dates.
/// </summary>
public sealed record ReportingPeriod(DateOnly From, DateOnly To)
{
    /// <summary>Hard cap on range to prevent runaway aggregation queries.</summary>
    public const int MaxMonths = 24;

    public static Result<ReportingPeriod> Create(DateOnly from, DateOnly to)
    {
        if (from > to)
            return Result.Failure<ReportingPeriod>(ReportingErrors.PeriodInverted);

        var months = ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1;
        if (months > MaxMonths)
            return Result.Failure<ReportingPeriod>(ReportingErrors.PeriodTooLong);

        return Result.Success(new ReportingPeriod(from, to));
    }

    public static ReportingPeriod ThisMonth(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var first = new DateOnly(now.Year, now.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return new ReportingPeriod(first, last);
    }

    public static ReportingPeriod LastNMonths(int monthCount, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var endOfThisMonth = new DateOnly(now.Year, now.Month, 1).AddMonths(1).AddDays(-1);
        var startOfFirstMonth = new DateOnly(now.Year, now.Month, 1).AddMonths(-(monthCount - 1));
        return new ReportingPeriod(startOfFirstMonth, endOfThisMonth);
    }
}

public static class ReportingErrors
{
    public static readonly Error PeriodInverted = new("Reporting.PeriodInverted", "Period 'from' must be on or before 'to'.");
    public static readonly Error PeriodTooLong = new("Reporting.PeriodTooLong", $"Period cannot span more than {ReportingPeriod.MaxMonths} months.");
    public static readonly Error LimitOutOfRange = new("Reporting.LimitOutOfRange", "Limit must be between 1 and 100.");
    public static readonly Error InvalidMonthRange = new("Reporting.InvalidMonthRange", "Month range must be between 1 and 24.");
    public static readonly Error Forbidden = new("Reporting.Forbidden", "Current role cannot access reporting data.");
}
