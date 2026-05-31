using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Application.Reporting;
using FinFlow.Application.Reporting.DTOs;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace FinFlow.Application.Chat.Services;

public sealed partial class ChatReportingService : IChatReportingService
{
    private const int DefaultTopEmployeeLimit = 3;
    private const int DefaultTrendMonthCount = 3;
    private readonly IReportingService _reportingService;
    private readonly IReviewedDocumentRepository? _reviewedDocumentRepository;

    public ChatReportingService(IReportingService reportingService, IReviewedDocumentRepository? reviewedDocumentRepository = null)
    {
        _reportingService = reportingService;
        _reviewedDocumentRepository = reviewedDocumentRepository;
    }

    public async Task<ChatReportingAnswer> BuildOwnExpenseSummaryAsync(
        ChatAuthorizationProfile profile,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (!profile.Capabilities.CanViewOwnExpenseSummary)
            throw new InvalidOperationException("Chat reporting access denied: own expense summary is not permitted.");

        var period = Reporting.DTOs.ReportingPeriod.Create(from, to);
        if (period.IsFailure)
            throw new InvalidOperationException(period.Error.Description);

        var dto = await _reportingService.GetOwnExpenseSummaryAsync(
            profile.TenantId,
            profile.MembershipId,
            period.Value,
            cancellationToken);

        var answer = string.Join(
            Environment.NewLine,
            [
                "Tóm tắt chi tiêu của bạn trong kỳ:",
                $"Tổng chi đã xác nhận: {dto.TotalAmountInBaseCurrency} {dto.BaseCurrencyCode}",
                $"Số lượng khoản chi: {dto.ExpenseCount}"
            ]);

        return new ChatReportingAnswer(answer, "own-expense-summary", dto.ExpenseCount);
    }

    public async Task<ChatReportingAnswer> BuildScopedExpenseSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedQuery);

        // Cross-department ranking ("phòng ban nào chi nhiều nhất") must be evaluated
        // at tenant scope so the department breakdown is available, not collapsed to
        // the asker's own/department scope.
        if (IsDepartmentRankingQuery(normalizedQuery) && profile.CanAccessAllTenantData)
        {
            scope = new AggregateScope("toàn công ty", null, null, true, false);
        }

        var period = ReportingPeriod.Create(from, to);
        if (period.IsFailure)
            throw new InvalidOperationException(period.Error.Description);

        var dto = await _reportingService.GetExpenseSummaryAsync(
            profile.TenantId,
            period.Value,
            scope.DepartmentScope,
            cancellationToken);

        var lines = new List<string>
        {
            $"Tóm tắt chi tiêu trong phạm vi {scope.ScopeLabel}:",
            $"Tổng chi đã xác nhận: {FormatAmount(dto.TotalInBaseCurrency)} {dto.BaseCurrencyCode}",
            $"Số lượng khoản chi: {dto.ExpenseCount}"
        };

        if (dto.ByCategory.Count > 0)
        {
            var topCategory = dto.ByCategory[0];
            var sharePercent = dto.TotalInBaseCurrency > 0m
                ? Math.Round(topCategory.AmountInBaseCurrency / dto.TotalInBaseCurrency * 100m, 1)
                : 0m;
            lines.Add($"Hạng mục chi lớn nhất: {topCategory.KeyName} · {FormatAmount(topCategory.AmountInBaseCurrency)} {dto.BaseCurrencyCode} · {topCategory.ExpenseCount} khoản chi · chiếm {sharePercent}% tổng chi");
        }

        if (scope.IsTenantScope && dto.ByDepartment.Count > 0)
        {
            var topDepartment = dto.ByDepartment[0];
            lines.Add($"Phòng ban chi nhiều nhất: {topDepartment.KeyName} · {FormatAmount(topDepartment.AmountInBaseCurrency)} {dto.BaseCurrencyCode}");
        }

        return new ChatReportingAnswer(
            string.Join(Environment.NewLine, lines),
            scope.IsTenantScope ? "tenant-expense-summary" : "department-expense-summary",
            dto.ExpenseCount);
    }

    public async Task<ChatReportingAnswer> BuildTopEmployeesSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedQuery);
        var period = ReportingPeriod.Create(from, to);
        if (period.IsFailure)
            throw new InvalidOperationException(period.Error.Description);

        var rows = await _reportingService.GetTopEmployeesAsync(
            profile.TenantId,
            period.Value,
            scope.DepartmentScope,
            DefaultTopEmployeeLimit,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new ChatReportingAnswer(
                $"Hiện chưa có dữ liệu chi tiêu để xếp hạng trong phạm vi {scope.ScopeLabel}.",
                "top-employees-summary",
                0);
        }

        var lines = new List<string>
        {
            $"Top nhân viên chi tiêu trong phạm vi {scope.ScopeLabel}:"
        };

        lines.AddRange(rows.Select((row, index) =>
            $"{index + 1}. {row.EmployeeName} · {row.DepartmentName} · {FormatAmount(row.TotalAmountInBaseCurrency)} {row.BaseCurrencyCode} · {row.ExpenseCount} khoản chi"));

        return new ChatReportingAnswer(
            string.Join(Environment.NewLine, lines),
            "top-employees-summary",
            rows.Count);
    }

    public async Task<ChatReportingAnswer> BuildMonthlyTrendSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedQuery);
        var monthCount = ResolveTrendMonthCount(normalizedQuery);
        var rows = await _reportingService.GetMonthlyTrendAsync(
            profile.TenantId,
            monthCount,
            scope.DepartmentScope,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new ChatReportingAnswer(
                $"Hiện chưa có dữ liệu xu hướng chi tiêu trong {monthCount} tháng gần đây cho phạm vi {scope.ScopeLabel}.",
                "monthly-trend-summary",
                0);
        }

        var peak = rows
            .OrderByDescending(x => x.ExpenseTotal)
            .ThenByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .First();

        var lines = new List<string>
        {
            $"Xu hướng chi tiêu {monthCount} tháng gần đây trong phạm vi {scope.ScopeLabel}:",
            $"Tháng cao nhất: {peak.Year:D4}-{peak.Month:D2} · {FormatAmount(peak.ExpenseTotal)} {peak.BaseCurrencyCode} · {peak.DocumentCount} chứng từ"
        };

        lines.AddRange(rows.Select(row =>
            $"- {row.Year:D4}-{row.Month:D2}: {FormatAmount(row.ExpenseTotal)} {row.BaseCurrencyCode} · {row.DocumentCount} chứng từ"));

        return new ChatReportingAnswer(
            string.Join(Environment.NewLine, lines),
            "monthly-trend-summary",
            rows.Count);
    }

    public async Task<ChatReportingAnswer> BuildTopVendorsSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedQuery);
        var period = ReportingPeriod.Create(from, to);
        if (period.IsFailure)
            throw new InvalidOperationException(period.Error.Description);

        var rows = await _reportingService.GetTopVendorsAsync(
            profile.TenantId,
            period.Value,
            scope.DepartmentScope,
            scope.OwnerMembershipScope,
            DefaultTopEmployeeLimit,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new ChatReportingAnswer(
                $"Hiện chưa có dữ liệu nhà cung cấp trong phạm vi {scope.ScopeLabel}.",
                "top-vendors-summary",
                0);
        }

        var lines = new List<string>
        {
            $"Top nhà cung cấp trong phạm vi {scope.ScopeLabel}:"
        };

        lines.AddRange(rows.Select((row, index) =>
            $"{index + 1}. {row.VendorName} · {FormatAmount(row.TotalAmountInBaseCurrency)} {row.BaseCurrencyCode} · {row.DocumentCount} chứng từ"));

        return new ChatReportingAnswer(
            string.Join(Environment.NewLine, lines),
            "top-vendors-summary",
            rows.Count);
    }

    public async Task<ChatReportingAnswer> BuildBudgetUtilizationSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedQuery);
        var month = to.Month;
        var year = to.Year;

        if (scope.IsOwnScope)
        {
            return new ChatReportingAnswer(
                "Hiện chatbot chưa có dữ liệu hạn mức cá nhân còn lại theo tháng. Bạn có thể hỏi ngân sách của phòng ban hoặc toàn công ty, hoặc xem tổng chi đã xác nhận của bạn.",
                "budget-utilization-unsupported",
                0);
        }

        var rows = await _reportingService.GetBudgetUtilizationAsync(
            profile.TenantId,
            month,
            year,
            scope.DepartmentScope,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new ChatReportingAnswer(
                $"Hiện chưa có dữ liệu ngân sách cho phạm vi {scope.ScopeLabel} trong {year:D4}-{month:D2}.",
                "budget-utilization-summary",
                0);
        }

        if (MentionsBudgetOverrun(normalizedQuery))
        {
            var overBudgetRows = rows
                .Where(x => x.IsOverBudget || x.UtilizationPercent > 100m || x.Remaining < 0m)
                .OrderByDescending(x => x.UtilizationPercent)
                .ToList();

            if (overBudgetRows.Count == 0)
            {
                return new ChatReportingAnswer(
                    $"Không có phòng ban nào vượt ngân sách trong phạm vi {scope.ScopeLabel} ở {year:D4}-{month:D2}.",
                    "budget-utilization-summary",
                    0);
            }

            var lines = overBudgetRows.Select((row, index) =>
                $"{index + 1}. {row.DepartmentName} · {row.UtilizationPercent:0.##}% · vượt {FormatAmount(Math.Abs(row.Remaining))} {row.BaseCurrencyCode}");

            return new ChatReportingAnswer(
                string.Join(
                    Environment.NewLine,
                    new[] { $"Các phòng ban vượt ngân sách trong phạm vi {scope.ScopeLabel} ở {year:D4}-{month:D2}:" }
                        .Concat(lines)),
                "budget-utilization-summary",
                overBudgetRows.Count);
        }

        if (scope.IsTenantScope)
        {
            var allocated = rows.Sum(x => x.Allocated);
            var committed = rows.Sum(x => x.Committed);
            var spent = rows.Sum(x => x.Spent);
            var remaining = rows.Sum(x => x.Remaining);
            var utilization = allocated > 0
                ? Math.Round(((committed + spent) / allocated) * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            var topDepartment = rows.OrderByDescending(x => x.UtilizationPercent).First();

            var answer = string.Join(
                Environment.NewLine,
                [
                    $"Tổng quan ngân sách toàn công ty trong {year:D4}-{month:D2}:",
                    $"Ngân sách còn lại: {FormatAmount(remaining)} {topDepartment.BaseCurrencyCode}",
                    $"Đã dùng: {FormatAmount(committed + spent)} / {FormatAmount(allocated)} {topDepartment.BaseCurrencyCode} ({utilization:0.##}%)",
                    $"Phòng ban sử dụng cao nhất: {topDepartment.DepartmentName} · {topDepartment.UtilizationPercent:0.##}%"
                ]);

            return new ChatReportingAnswer(answer, "budget-utilization-summary", rows.Count);
        }

        var row = rows[0];
        var departmentAnswer = string.Join(
            Environment.NewLine,
            [
                $"Ngân sách còn lại của {scope.ScopeLabel} trong {year:D4}-{month:D2}:",
                $"{FormatAmount(row.Remaining)} {row.BaseCurrencyCode}",
                $"Đã dùng: {FormatAmount(row.Committed + row.Spent)} / {FormatAmount(row.Allocated)} {row.BaseCurrencyCode} ({row.UtilizationPercent:0.##}%)"
            ]);

        return new ChatReportingAnswer(departmentAnswer, "budget-utilization-summary", rows.Count);
    }

    public async Task<ChatReportingAnswer> BuildPendingApprovalSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_reviewedDocumentRepository is null)
            throw new InvalidOperationException("Chat reporting approval summary is unavailable because the reviewed document repository is not configured.");

        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var mentionsOwnScope = MentionsOwnScope(normalizedQuery);
        var mentionsDepartmentScope = MentionsDepartmentScope(normalizedQuery);
        var useTenantScope = profile.CanAccessAllTenantData &&
            (MentionsTenantScope(normalizedQuery) || (!mentionsOwnScope && !mentionsDepartmentScope));
        var useDepartmentScope = profile.Capabilities.CanViewDepartmentExpenseDetails
            && profile.DepartmentId.HasValue
            && (!useTenantScope && (mentionsDepartmentScope || !mentionsOwnScope));

        IReadOnlyList<ReviewedDocument> documents;
        string scopeLabel;

        if (useTenantScope)
        {
            documents = await _reviewedDocumentRepository.GetReadyForApprovalAsync(profile.TenantId, cancellationToken);
            scopeLabel = "toàn công ty";
        }
        else if (useDepartmentScope)
        {
            documents = await _reviewedDocumentRepository.GetReadyForApprovalByDepartmentAsync(profile.TenantId, profile.DepartmentId!.Value, cancellationToken);
            scopeLabel = "phòng ban";
        }
        else
        {
            if (!profile.Capabilities.CanViewOwnExpenseDetails)
                throw new InvalidOperationException("Chat reporting access denied: pending approval details are not permitted.");

            documents = await _reviewedDocumentRepository.GetOwnedReadyForApprovalAsync(profile.TenantId, profile.MembershipId, cancellationToken);
            scopeLabel = "của bạn";
        }

        if (documents.Count == 0)
        {
            return new ChatReportingAnswer(
                $"Hiện không có hóa đơn nào đang chờ duyệt trong phạm vi {scopeLabel}.",
                "pending-approval-summary",
                0);
        }

        var topDocuments = documents.Take(5).ToList();
        var lines = topDocuments.Select(FormatPendingApprovalDocument);

        var answer = string.Join(
            Environment.NewLine,
            new[] { $"Có {documents.Count} hóa đơn đang chờ duyệt trong phạm vi {scopeLabel}." }
                .Concat(lines)
                .Concat(documents.Count > topDocuments.Count
                    ? [$"Còn {documents.Count - topDocuments.Count} hóa đơn khác chưa liệt kê."]
                    : []));

        return new ChatReportingAnswer(answer, "pending-approval-summary", documents.Count);
    }

    public async Task<ChatReportingAnswer> BuildExpenseComparisonAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.ToLowerInvariant();
        var mentionsTenantScope = MentionsTenantScope(normalizedQuery);
        var mentionsDepartmentScope = MentionsDepartmentScope(normalizedQuery);
        var mentionsCrossUserScope = MentionsCrossUserScope(normalizedQuery);

        if (mentionsTenantScope && !profile.Capabilities.CanViewTenantExpenseSummary)
        {
            return new ChatReportingAnswer(
                "Tôi không thể so sánh chi tiêu của bạn với người khác trong công ty vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi của bạn.",
                "expense-comparison-denied",
                0);
        }

        if (mentionsDepartmentScope && !profile.Capabilities.CanViewDepartmentExpenseSummary)
        {
            return new ChatReportingAnswer(
                "Tôi không thể so sánh chi tiêu của bạn với người khác trong phòng ban vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi của bạn.",
                "expense-comparison-denied",
                0);
        }

        if (mentionsCrossUserScope &&
            !mentionsTenantScope &&
            !mentionsDepartmentScope &&
            !profile.Capabilities.CanViewDepartmentExpenseSummary &&
            !profile.Capabilities.CanViewTenantExpenseSummary)
        {
            return new ChatReportingAnswer(
                "Tôi không thể so sánh chi tiêu của bạn với người khác vì quyền hiện tại chỉ cho phép xem dữ liệu của chính bạn.",
                "expense-comparison-denied",
                0);
        }

        var normalizedForScope = IntentTextNormalizer.Normalize(query);
        var scope = ResolveAggregateScope(profile, normalizedForScope);
        var currentPeriod = ReportingPeriod.Create(from, to);
        if (currentPeriod.IsFailure)
            throw new InvalidOperationException(currentPeriod.Error.Description);

        var days = to.DayNumber - from.DayNumber + 1;

        // If the current period is a whole calendar month (1st → last day), the previous
        // period should be the prior calendar MONTH, not a sliding window of the same day
        // count. Otherwise "tháng trước" of May (31d) would yield Mar-31..Apr-30 instead of
        // Apr-01..Apr-30. For non-calendar-month ranges, keep the equal-length sliding window.
        DateOnly previousFrom;
        DateOnly previousTo;
        var isWholeCalendarMonth = from.Day == 1 && to.Day == DateTime.DaysInMonth(to.Year, to.Month) && from.Month == to.Month && from.Year == to.Year;
        if (isWholeCalendarMonth)
        {
            previousFrom = from.AddMonths(-1);
            previousTo = new DateOnly(previousFrom.Year, previousFrom.Month, DateTime.DaysInMonth(previousFrom.Year, previousFrom.Month));
        }
        else
        {
            previousTo = from.AddDays(-1);
            previousFrom = previousTo.AddDays(-(days - 1));
        }
        var previousPeriod = ReportingPeriod.Create(previousFrom, previousTo);
        if (previousPeriod.IsFailure)
            throw new InvalidOperationException(previousPeriod.Error.Description);

        var current = await _reportingService.GetExpenseSummaryAsync(
            profile.TenantId,
            currentPeriod.Value,
            scope.DepartmentScope,
            cancellationToken);
        var previous = await _reportingService.GetExpenseSummaryAsync(
            profile.TenantId,
            previousPeriod.Value,
            scope.DepartmentScope,
            cancellationToken);

        var delta = current.TotalInBaseCurrency - previous.TotalInBaseCurrency;
        var direction = delta switch
        {
            > 0 => "tăng",
            < 0 => "giảm",
            _ => "không đổi"
        };
        decimal? percent = previous.TotalInBaseCurrency == 0m
            ? null
            : Math.Abs(delta / previous.TotalInBaseCurrency * 100m);
        var currency = !string.IsNullOrWhiteSpace(current.BaseCurrencyCode)
            ? current.BaseCurrencyCode
            : previous.BaseCurrencyCode;

        var lines = new List<string>
        {
            $"So với kỳ trước trong phạm vi {scope.ScopeLabel}: chi tiêu {direction}.",
            $"Kỳ hiện tại ({from:yyyy-MM-dd} đến {to:yyyy-MM-dd}): {FormatAmount(current.TotalInBaseCurrency)} {currency} · {current.ExpenseCount} khoản chi",
            $"Kỳ so sánh ({previousFrom:yyyy-MM-dd} đến {previousTo:yyyy-MM-dd}): {FormatAmount(previous.TotalInBaseCurrency)} {currency} · {previous.ExpenseCount} khoản chi",
            $"Chênh lệch: {FormatAmount(Math.Abs(delta))} {currency}" + (percent.HasValue ? $" ({percent.Value:0.##}%)" : string.Empty)
        };

        return new ChatReportingAnswer(
            string.Join(Environment.NewLine, lines),
            "expense-comparison-summary",
            current.ExpenseCount + previous.ExpenseCount);
    }

    private static bool MentionsOwnScope(string normalizedQuery) =>
        ScopeKeywords.MentionsOwnScope(normalizedQuery);

    private static bool MentionsDepartmentScope(string normalizedQuery) =>
        ScopeKeywords.MentionsDepartmentScope(normalizedQuery);

    private static bool MentionsTenantScope(string normalizedQuery) =>
        ScopeKeywords.MentionsTenantScope(normalizedQuery);

    private static bool MentionsBudgetOverrun(string normalizedQuery) =>
        normalizedQuery.Contains("vuot ngan sach", StringComparison.Ordinal) ||
        normalizedQuery.Contains("over budget", StringComparison.Ordinal) ||
        normalizedQuery.Contains("overbudget", StringComparison.Ordinal);

    private static bool MentionsCrossUserScope(string normalizedQuery) =>
        normalizedQuery.Contains("người khác", StringComparison.Ordinal) ||
        normalizedQuery.Contains("đồng nghiệp", StringComparison.Ordinal) ||
        normalizedQuery.Contains("others", StringComparison.Ordinal) ||
        normalizedQuery.Contains("other people", StringComparison.Ordinal) ||
        normalizedQuery.Contains("mọi người", StringComparison.Ordinal) ||
        normalizedQuery.Contains("compare", StringComparison.Ordinal) ||
        normalizedQuery.Contains("so sánh", StringComparison.Ordinal);

    private static bool IsDepartmentRankingQuery(string normalizedQuery)
    {
        var padded = $" {normalizedQuery} ";
        var mentionsDepartment = padded.Contains(" phong ban ", StringComparison.Ordinal)
            || padded.Contains(" bo phan ", StringComparison.Ordinal)
            || padded.Contains(" department ", StringComparison.Ordinal);
        if (!mentionsDepartment)
            return false;

        var rankingTerms = new[]
        {
            "nhieu nhat", "chi nhieu", "cao nhat", "top", "ranking", "xep hang",
            "chiem nhieu", "lon nhat", "dong gop nhieu", "nao chi", "nao chiem"
        };
        return rankingTerms.Any(t => padded.Contains($" {t} ", StringComparison.Ordinal)
            || padded.Contains($" {t}", StringComparison.Ordinal));
    }

    private static AggregateScope ResolveAggregateScope(ChatAuthorizationProfile profile, string normalizedQuery)
    {
        var mentionsOwnScope = MentionsOwnScope(normalizedQuery);
        var mentionsDepartmentScope = MentionsDepartmentScope(normalizedQuery);
        var useTenantScope = profile.CanAccessAllTenantData &&
            (MentionsTenantScope(normalizedQuery) || (!mentionsOwnScope && !mentionsDepartmentScope));
        var useDepartmentScope = profile.Capabilities.CanViewDepartmentExpenseSummary
            && profile.DepartmentId.HasValue
            && (!useTenantScope && (mentionsDepartmentScope || !mentionsOwnScope));

        if (useTenantScope)
            return new AggregateScope("toàn công ty", null, null, true, false);

        if (useDepartmentScope)
            return new AggregateScope("phòng ban của bạn", profile.DepartmentId, null, false, false);

        if (!profile.Capabilities.CanViewOwnExpenseSummary)
            throw new InvalidOperationException("Chat reporting access denied: aggregate reporting is not permitted.");

        return new AggregateScope("của bạn", null, profile.MembershipId, false, true);
    }

    private static int ResolveTrendMonthCount(string normalizedQuery)
    {
        var match = TrendMonthCountPattern().Match(normalizedQuery);
        if (!match.Success)
            return DefaultTrendMonthCount;

        return int.TryParse(match.Groups["count"].Value, out var parsed)
            ? Math.Clamp(parsed, 1, ReportingPeriod.MaxMonths)
            : DefaultTrendMonthCount;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("#,##0.##", CultureInfo.InvariantCulture);

    private static string FormatPendingApprovalDocument(ReviewedDocument document, int index)
    {
        var vendorName = DocumentTextNormalizer.NormalizeVendorName(document.VendorName);
        var reference = DocumentTextNormalizer.NormalizeReference(document.Reference);
        var builder = new StringBuilder();
        builder.AppendLine($"{index + 1}. Nhà cung cấp: {vendorName}");
        builder.AppendLine($"   Mã tham chiếu: {NormalizeBusinessCode(reference)}");
        builder.AppendLine($"   Ngày chứng từ: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"   Tổng tiền: {document.TotalAmount:0.00} {document.CurrencyCode}");
        builder.Append($"   Trạng thái: {ToBusinessStatusLabel(document.Status)}");
        return builder.ToString();
    }

    private static string NormalizeBusinessCode(string value) =>
        string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim().ToUpperInvariant();

    private static string ToBusinessStatusLabel(ReviewedDocumentStatus status) => status switch
    {
        ReviewedDocumentStatus.ReadyForApproval => "Chờ duyệt",
        ReviewedDocumentStatus.Approved => "Đã duyệt",
        ReviewedDocumentStatus.Rejected => "Đã từ chối",
        ReviewedDocumentStatus.Draft => "Nháp",
        ReviewedDocumentStatus.PendingEscalation => "Chờ duyệt nâng cấp",
        _ => status.ToString()
    };

    [GeneratedRegex(@"(?<count>\d{1,2})\s*(thang|month)", RegexOptions.IgnoreCase)]
    private static partial Regex TrendMonthCountPattern();

    private sealed record AggregateScope(
        string ScopeLabel,
        Guid? DepartmentScope,
        Guid? OwnerMembershipScope,
        bool IsTenantScope,
        bool IsOwnScope);
}
