using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Reporting;
using FinFlow.Application.Reporting.DTOs;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatReportingServiceTests
{
    [Fact]
    public async Task BuildOwnExpenseSummaryAsync_UsesMembershipScope_ForStaff()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        reporting
            .Setup(x => x.GetOwnExpenseSummaryAsync(tenantId, membershipId, It.IsAny<ReportingPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnExpenseSummaryDto(12m, "VND", 3));

        var result = await service.BuildOwnExpenseSummaryAsync(
            profile,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("Tóm tắt chi tiêu", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tổng chi đã xác nhận: 12", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Số lượng khoản chi: 3", result.Answer, StringComparison.OrdinalIgnoreCase);
        reporting.Verify(
            x => x.GetOwnExpenseSummaryAsync(tenantId, membershipId, It.IsAny<ReportingPeriod>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildOwnExpenseSummaryAsync_ThrowsAndSkipsReporting_WhenCapabilityDenied()
    {
        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var service = new ChatReportingService(reporting.Object);
        var profile = new ChatAuthorizationProfile(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(false, true, true, true, false, false, false, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildOwnExpenseSummaryAsync(
            profile,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None));

        reporting.Verify(
            x => x.GetOwnExpenseSummaryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ReportingPeriod>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildPendingApprovalSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var reviewedDocuments = new Mock<IReviewedDocumentRepository>();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            membershipId,
            departmentId,
            [],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenantId,
            departmentId,
            membershipId,
            "hoa-don.pdf",
            "application/pdf",
            "Bách Hóa Xanh",
            "REF-123",
            new DateOnly(2026, 5, 20),
            "Meals",
            null,
            100m,
            10m,
            110m,
            "Manual",
            "staff@finflow.test",
            "High",
            DateTime.UtcNow,
            [ReviewedDocumentLineItem.Create("Rau cu", 1m, 110m, 110m)]).Value;
        document.SetCurrencyContext("VND", "VND", 1m);

        reviewedDocuments
            .Setup(x => x.GetReadyForApprovalByDepartmentAsync(tenantId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([document]);

        var service = new ChatReportingService(reporting.Object, reviewedDocuments.Object);

        var result = await service.BuildPendingApprovalSummaryAsync(
            profile,
            "Hóa đơn nào đang chờ duyệt trong phòng ban?",
            CancellationToken.None);

        Assert.Contains("Có 1 hóa đơn", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nhà cung cấp: Bách Hóa Xanh", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mã tham chiếu: REF-123", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trạng thái: Chờ duyệt", result.Answer, StringComparison.OrdinalIgnoreCase);
        reviewedDocuments.Verify(
            x => x.GetReadyForApprovalByDepartmentAsync(tenantId, departmentId, It.IsAny<CancellationToken>()),
            Times.Once);
        reviewedDocuments.Verify(
            x => x.GetOwnedReadyForApprovalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildScopedExpenseSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            Guid.NewGuid(),
            departmentId,
            [departmentId],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        reporting
            .Setup(x => x.GetExpenseSummaryAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpenseSummaryDto(
                5,
                1250000m,
                "VND",
                [new ExpenseSummaryGroupDto(Guid.NewGuid(), "Ăn uống", 700000m, 3)],
                [new ExpenseSummaryGroupDto(departmentId, "Sales", 1250000m, 5)],
                []));

        var result = await service.BuildScopedExpenseSummaryAsync(
            profile,
            "Phòng ban tôi đã chi bao nhiêu tháng này?",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("phòng ban của bạn", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1,250,000", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ăn uống", result.Answer, StringComparison.OrdinalIgnoreCase);
        reporting.Verify(
            x => x.GetExpenseSummaryAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildTopEmployeesSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            Guid.NewGuid(),
            departmentId,
            [departmentId],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        reporting
            .Setup(x => x.GetTopEmployeesAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TopEmployeeDto(Guid.NewGuid(), Guid.NewGuid(), "Nguyen Van A", "Sales", 4, 900000m, "VND"),
                new TopEmployeeDto(Guid.NewGuid(), Guid.NewGuid(), "Tran Thi B", "Sales", 3, 650000m, "VND")
            ]);

        var result = await service.BuildTopEmployeesSummaryAsync(
            profile,
            "Nhân viên nào chi nhiều nhất trong phòng ban tôi tháng này?",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("Top nhân viên chi tiêu", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nguyen Van A", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("900,000", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.RecordCount);
        reporting.Verify(
            x => x.GetTopEmployeesAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildMonthlyTrendSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            Guid.NewGuid(),
            departmentId,
            [departmentId],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        reporting
            .Setup(x => x.GetMonthlyTrendAsync(tenantId, 3, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MonthlyTrendPointDto(2026, 3, 500000m, 2, "VND"),
                new MonthlyTrendPointDto(2026, 4, 900000m, 4, "VND"),
                new MonthlyTrendPointDto(2026, 5, 700000m, 3, "VND")
            ]);

        var result = await service.BuildMonthlyTrendSummaryAsync(
            profile,
            "Xu hướng chi tiêu 3 tháng gần đây của phòng ban tôi là gì?",
            CancellationToken.None);

        Assert.Contains("3 tháng gần đây", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-04", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("900,000", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, result.RecordCount);
        reporting.Verify(
            x => x.GetMonthlyTrendAsync(tenantId, 3, departmentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildTopVendorsSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            Guid.NewGuid(),
            departmentId,
            [departmentId],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        reporting
            .Setup(x => x.GetTopVendorsAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TopVendorDto(Guid.NewGuid(), "Bách Hóa Xanh", "0101", true, 4, 1500000m, "VND"),
                new TopVendorDto(Guid.NewGuid(), "Winmart", "0202", true, 2, 700000m, "VND")
            ]);

        var result = await service.BuildTopVendorsSummaryAsync(
            profile,
            "Nhà cung cấp nào có tổng chi lớn nhất trong phòng ban tôi tháng này?",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("Top nhà cung cấp", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bách Hóa Xanh", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1,500,000", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.RecordCount);
        reporting.Verify(
            x => x.GetTopVendorsAsync(tenantId, It.IsAny<ReportingPeriod>(), departmentId, null, 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildBudgetUtilizationSummaryAsync_UsesDepartmentScope_ForManager()
    {
        var reporting = new Mock<IReportingService>();
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Manager,
            Guid.NewGuid(),
            departmentId,
            [departmentId],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, true, true, false, false));

        reporting
            .Setup(x => x.GetBudgetUtilizationAsync(tenantId, 5, 2026, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BudgetUtilizationDto(departmentId, "Sales", 5, 2026, 1000000m, 100000m, 650000m, 250000m, 75m, false, false, "VND")
            ]);

        var result = await service.BuildBudgetUtilizationSummaryAsync(
            profile,
            "Ngân sách phòng ban tôi còn bao nhiêu tháng này?",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("Ngân sách còn lại", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("250,000", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("75%", result.Answer, StringComparison.OrdinalIgnoreCase);
        reporting.Verify(
            x => x.GetBudgetUtilizationAsync(tenantId, 5, 2026, departmentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildBudgetUtilizationSummaryAsync_ReturnsUnsupportedMessage_ForOwnScope()
    {
        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var service = new ChatReportingService(reporting.Object);
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var profile = new ChatAuthorizationProfile(
            tenantId,
            "Tenant",
            RoleType.Staff,
            membershipId,
            Guid.NewGuid(),
            [],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        var result = await service.BuildBudgetUtilizationSummaryAsync(
            profile,
            "Tôi còn bao nhiêu hạn mức tháng này?",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("chưa có dữ liệu hạn mức cá nhân", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.RecordCount);
        reporting.Verify(
            x => x.GetBudgetUtilizationAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildExpenseComparisonAsync_ReturnsDeniedMessage_ForStaffCrossTenantComparison()
    {
        var reporting = new Mock<IReportingService>(MockBehavior.Strict);
        var service = new ChatReportingService(reporting.Object);
        var profile = new ChatAuthorizationProfile(
            Guid.NewGuid(),
            "Tenant",
            RoleType.Staff,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            false,
            Array.Empty<DocumentChunkType>(),
            new ChatCapabilities(true, true, true, true, false, false, false, false));

        var result = await service.BuildExpenseComparisonAsync(
            profile,
            "So sánh chi tiêu của tôi với người khác trong công ty",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            CancellationToken.None);

        Assert.Contains("không thể so sánh", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.RecordCount);
    }
}
