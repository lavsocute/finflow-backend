using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlVendorsApiTests
{
    [Fact]
    public async Task VendorWorkspaceQueries_ReturnLinkedDocumentCountAndRecentDocuments()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("manager.vendors@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Vendor Workspace", "vendor-workspace").Value;
        var department = Department.Create("Finance", tenant.Id).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Manager).Value;
        membership.SetDepartment(department.Id);
        var vendor = Vendor.Create(tenant.Id, "0312345678", "BACH HOA XANH").Value;
        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenant.Id,
            department.Id,
            membership.Id,
            "hoa-don.jpg",
            "image/jpeg",
            vendor.Name,
            "INV-2026-0041",
            new DateOnly(2026, 5, 17),
            "Thuc pham",
            vendor.TaxCode,
            187500m,
            454m,
            187954m,
            "ocr",
            account.Email,
            "High precision",
            new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc),
            [ReviewedDocumentLineItem.Create("Hang hoa", 1m, 187954m, 187954m)]).Value;
        document.LinkVendor(vendor.Id);

        await factory.SeedAsync(db => db.AddRange(account, tenant, department, membership, vendor, document));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Manager,
            tenant.Id,
            membership.Id);

        const string catalogQuery = """
            query {
              myVendors {
                vendorId
                linkedDocumentsCount
              }
            }
            """;
        var catalogPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, catalogQuery);
        var catalogVendor = catalogPayload.RootElement.GetProperty("data").GetProperty("myVendors")[0];

        Assert.Equal(vendor.Id, catalogVendor.GetProperty("vendorId").GetGuid());
        Assert.Equal(1, catalogVendor.GetProperty("linkedDocumentsCount").GetInt32());

        const string detailQuery = """
            query($vendorId: UUID!) {
              vendorDetail(vendorId: $vendorId) {
                vendorId
                linkedDocumentsCount
                recentDocuments {
                  reference
                  category
                  status
                  totalAmount
                  currencyCode
                  documentDate
                }
              }
            }
            """;
        var detailPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, detailQuery, new
        {
            vendorId = vendor.Id
        });
        var detail = detailPayload.RootElement.GetProperty("data").GetProperty("vendorDetail");

        Assert.Equal(1, detail.GetProperty("linkedDocumentsCount").GetInt32());
        Assert.Equal("INV-2026-0041", detail.GetProperty("recentDocuments")[0].GetProperty("reference").GetString());
        Assert.Equal(187954m, detail.GetProperty("recentDocuments")[0].GetProperty("totalAmount").GetDecimal());
    }
}
