using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlBudgetWorkspaceApiTests
{
    [Fact]
    public async Task BudgetWorkspace_Query_ReturnsKpisCardsAndSelectedDetail()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("budget.admin@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Meridian Corp", "meridian").Value;
        var department = Department.Create("Kỹ thuật", tenant.Id).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin).Value;
        membership.SetDepartment(department.Id);
        var budget = Budget.Create(
            tenant.Id,
            department.Id,
            5,
            2026,
            50_000_000m,
            "VND",
            BudgetEnforcementMode.SoftBlock).Value;
        budget.ApplyCommitment(8_000_000m, BudgetExceededTrigger.ApproveDocument);
        budget.OverwriteSpent(17_000_000m);
        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenant.Id,
            department.Id,
            membership.Id,
            "receipt.jpg",
            "image/jpeg",
            "BÁCH HÓA XANH",
            "EXP-2026-0184",
            new DateOnly(2026, 5, 19),
            "Thực phẩm",
            "0312345678",
            2_400_000m,
            0m,
            2_400_000m,
            "ocr",
            account.Email,
            "High precision",
            new DateTime(2026, 5, 19, 9, 0, 0, DateTimeKind.Utc),
            [ReviewedDocumentLineItem.Create("Hàng hóa", 1m, 2_400_000m, 2_400_000m)]).Value;
        document.Approve(membership.Id);

        await factory.SeedAsync(db => db.AddRange(
            account,
            tenant,
            department,
            membership,
            budget,
            document));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string query = """
            query($month: Int!, $year: Int!, $selectedBudgetId: UUID) {
              budgetWorkspace(month: $month, year: $year, selectedBudgetId: $selectedBudgetId) {
                summary {
                  periodLabel
                  totalAllocated
                  totalCommitted
                  totalSpent
                  availablePool
                  activeBudgetCount
                  committedDocumentCount
                  paidDocumentCount
                  allWithinBudget
                  currencyCode
                }
                budgets {
                  id
                  departmentName
                  departmentPath
                  allocatedAmount
                  committedAmount
                  spentAmount
                  availableAmount
                  utilizationPct
                  enforcementMode
                  status
                  activity { reference employeeName amount state }
                  trend { monthLabel allocatedAmount spentAmount committedAmount }
                  audit { title actorName detail }
                }
                selectedBudget {
                  id
                  departmentName
                  activity { reference }
                }
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, query, new
        {
            month = 5,
            year = 2026,
            selectedBudgetId = budget.Id
        });

        var workspace = payload.RootElement.GetProperty("data").GetProperty("budgetWorkspace");
        var summary = workspace.GetProperty("summary");
        var budgetCard = workspace.GetProperty("budgets")[0];

        Assert.Equal("Tháng 5/2026", summary.GetProperty("periodLabel").GetString());
        Assert.Equal(50_000_000m, summary.GetProperty("totalAllocated").GetDecimal());
        Assert.Equal(8_000_000m, summary.GetProperty("totalCommitted").GetDecimal());
        Assert.Equal(17_000_000m, summary.GetProperty("totalSpent").GetDecimal());
        Assert.Equal(25_000_000m, summary.GetProperty("availablePool").GetDecimal());
        Assert.True(summary.GetProperty("allWithinBudget").GetBoolean());
        Assert.Equal("Kỹ thuật", budgetCard.GetProperty("departmentName").GetString());
        Assert.Equal("SoftBlock", budgetCard.GetProperty("enforcementMode").GetString());
        Assert.Equal("Healthy", budgetCard.GetProperty("status").GetString());
        Assert.Equal("EXP-2026-0184", budgetCard.GetProperty("activity")[0].GetProperty("reference").GetString());
        Assert.Equal(budget.Id, workspace.GetProperty("selectedBudget").GetProperty("id").GetGuid());
    }
}
