using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlDepartmentWorkspaceApiTests
{
    [Fact]
    public async Task CreateDepartment_Mutation_IsRegisteredAndCreatesDepartment()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("tenant.admin.department@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Meridian Corp", "meridian").Value;
        var root = Department.Create("Meridian Corp", tenant.Id).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin).Value;
        membership.SetDepartment(root.Id);

        await factory.SeedAsync(db => db.AddRange(account, tenant, root, membership));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string mutation = """
            mutation($input: CreateDepartmentInput!) {
              createDepartment(input: $input) {
                id
                name
                parentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new
            {
                name = "Tài chính",
                parentId = root.Id
            }
        });

        var created = payload.RootElement.GetProperty("data").GetProperty("createDepartment");

        Assert.Equal("Tài chính", created.GetProperty("name").GetString());
        Assert.Equal(root.Id, created.GetProperty("parentId").GetGuid());
    }

    [Fact]
    public async Task DepartmentWorkspace_Query_ReturnsHierarchyBudgetMembersAndSelectedDepartment()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("manager.department@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Meridian Corp", "meridian").Value;
        var root = Department.Create("Meridian Corp", tenant.Id).Value;
        var engineering = Department.Create("Kỹ thuật", tenant.Id, root.Id).Value;
        var platform = Department.Create("Nền tảng", tenant.Id, engineering.Id).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Manager).Value;
        membership.SetDepartment(engineering.Id);
        var budget = Budget.Create(
            tenant.Id,
            engineering.Id,
            5,
            2026,
            50_000_000m,
            "VND").Value;
        budget.OverwriteSpent(38_200_000m);
        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenant.Id,
            engineering.Id,
            membership.Id,
            "receipt.jpg",
            "image/jpeg",
            "BÁCH HÓA XANH",
            "INV-2026-0041",
            new DateOnly(2026, 5, 17),
            "Thực phẩm",
            "0312345678",
            187_954m,
            0m,
            187_954m,
            "ocr",
            account.Email,
            "High precision",
            new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc),
            [ReviewedDocumentLineItem.Create("Hàng hóa", 1m, 187_954m, 187_954m)]).Value;

        await factory.SeedAsync(db => db.AddRange(
            account,
            tenant,
            root,
            engineering,
            platform,
            membership,
            budget,
            document));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Manager,
            tenant.Id,
            membership.Id);

        const string query = """
            query($selectedDepartmentId: UUID) {
              departmentWorkspace(selectedDepartmentId: $selectedDepartmentId) {
                summary {
                  totalDepartments
                  totalMembers
                  activeDepartments
                  selectedDepartmentId
                }
                tree {
                  id
                  name
                  parentId
                  memberCount
                  childCount
                  budgetUtilizationPct
                  children {
                    id
                    name
                    parentId
                    memberCount
                    childCount
                    budgetUtilizationPct
                  }
                }
                selectedDepartment {
                  id
                  name
                  parentName
                  departmentCode
                  memberCount
                  subDepartmentCount
                  expenseVolumeAmount
                  expenseCount
                  manager {
                    email
                    role
                    initials
                  }
                  budgetSnapshot {
                    periodLabel
                    allocatedAmount
                    spentAmount
                    remainingAmount
                    utilizationPct
                  }
                  subDepartments {
                    name
                    memberCount
                  }
                  membersPreview {
                    email
                    initials
                  }
                  recentActivity {
                    title
                    amount
                  }
                }
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, query, new
        {
            selectedDepartmentId = engineering.Id
        });
        var workspace = payload.RootElement.GetProperty("data").GetProperty("departmentWorkspace");
        var selected = workspace.GetProperty("selectedDepartment");

        Assert.Equal(3, workspace.GetProperty("summary").GetProperty("totalDepartments").GetInt32());
        Assert.Equal(1, workspace.GetProperty("summary").GetProperty("totalMembers").GetInt32());
        Assert.Equal(engineering.Id, workspace.GetProperty("summary").GetProperty("selectedDepartmentId").GetGuid());
        Assert.Equal("Meridian Corp", workspace.GetProperty("tree")[0].GetProperty("name").GetString());
        Assert.Equal("Kỹ thuật", workspace.GetProperty("tree")[0].GetProperty("children")[0].GetProperty("name").GetString());
        Assert.Equal("Kỹ thuật", selected.GetProperty("name").GetString());
        Assert.Equal("Meridian Corp", selected.GetProperty("parentName").GetString());
        Assert.Equal(1, selected.GetProperty("memberCount").GetInt32());
        Assert.Equal(1, selected.GetProperty("subDepartmentCount").GetInt32());
        Assert.Equal(187_954m, selected.GetProperty("expenseVolumeAmount").GetDecimal());
        Assert.Equal(1, selected.GetProperty("expenseCount").GetInt32());
        Assert.Equal("manager.department@finflow.test", selected.GetProperty("manager").GetProperty("email").GetString());
        Assert.Equal(50_000_000m, selected.GetProperty("budgetSnapshot").GetProperty("allocatedAmount").GetDecimal());
        Assert.Equal("Nền tảng", selected.GetProperty("subDepartments")[0].GetProperty("name").GetString());
        Assert.Equal("INV-2026-0041 đã gửi", selected.GetProperty("recentActivity")[0].GetProperty("title").GetString());
    }
}
