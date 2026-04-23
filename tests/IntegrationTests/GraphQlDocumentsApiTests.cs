using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlDocumentsApiTests
{
    private static readonly string SamplePdfFixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "tests",
        "UnitTests",
        "Fixtures",
        "sample-single-page.pdf");

    [Fact]
    public async Task UploadDocumentForReview_ReturnsDeterministicDraftPayload()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.documents@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace", "finance-ops").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);

        const string mutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
                originalFileName
                contentType
                vendorName
                reference
                documentDate
                dueDate
                category
                vendorTaxId
                subtotal
                vat
                totalAmount
                source
                reviewedByStaff
                confidenceLabel
                lineItems {
                  itemName
                  quantity
                  unitPrice
                  total
                }
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String("""
                    %PDF-1.7
                    1 0 obj
                    << /Type /Catalog >>
                    endobj
                    """u8.ToArray())
            }
        });

        var draft = payload.RootElement.GetProperty("data").GetProperty("uploadDocumentForReview");

        Assert.Equal("invoice-aws-october.pdf", draft.GetProperty("originalFileName").GetString());
        Assert.Equal("application/pdf", draft.GetProperty("contentType").GetString());
        Assert.Equal("Amazon Web Services, Inc.", draft.GetProperty("vendorName").GetString());
        Assert.Equal("Software & SaaS", draft.GetProperty("category").GetString());
        Assert.Equal("staff.documents@finflow.test", draft.GetProperty("reviewedByStaff").GetString());
        Assert.Equal("High precision", draft.GetProperty("confidenceLabel").GetString());
        Assert.Equal(3, draft.GetProperty("lineItems").GetArrayLength());
    }

    [Fact]
    public async Task UploadDocumentForReview_WithRealPdf_RendersFirstPage_AndReturnsDraft()
    {
        await using var factory = new GraphQlApiTestFactory(useRealPdfOcr: true);

        var account = Account.Create("staff.documents.pdf@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace PDF", "finance-ops-pdf").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);

        const string mutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
                originalFileName
                contentType
                vendorName
                confidenceLabel
              }
            }
            """;

        var pdfBytes = await File.ReadAllBytesAsync(SamplePdfFixturePath);
        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String(pdfBytes)
            }
        });

        var draft = payload.RootElement.GetProperty("data").GetProperty("uploadDocumentForReview");
        Assert.Equal("invoice-aws-october.pdf", draft.GetProperty("originalFileName").GetString());
        Assert.Equal("application/pdf", draft.GetProperty("contentType").GetString());
        Assert.Equal("Amazon Web Services, Inc.", draft.GetProperty("vendorName").GetString());
        Assert.Equal("High precision", draft.GetProperty("confidenceLabel").GetString());

        Assert.True(factory.OcrProbe.WasPdfRendered);
        Assert.Equal("image/png", factory.OcrProbe.LastPreparedContentType);
        Assert.True(factory.OcrProbe.LastPreparedBase64Length > 0);
    }

    [Fact]
    public async Task UploadDocumentForReview_Rejects_UnsupportedContentType()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.unsupported@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace", "finance-ops-unsupported").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);

        const string mutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, new
        {
            input = new
            {
                fileName = "notes.txt",
                contentType = "text/plain",
                base64Content = Convert.ToBase64String("hello"u8.ToArray())
            }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("Only PDF and image uploads are supported.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task UploadDocumentForReview_ReturnsPlanError_ForFreeTenant()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.documents.free@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace Free", "finance-ops-free").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Free);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);

        const string mutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String("""%PDF-1.7 invoice aws"""u8.ToArray())
            }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("OCR is not available for the current plan.", errors[0].GetProperty("message").GetString());
        Assert.Equal("Documents.OcrNotAvailableForCurrentPlan", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UploadDocumentForReview_ReturnsQuotaExceeded_WhenMonthlyQuotaIsExhausted()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.documents.quota@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace Quota", "finance-ops-quota").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });
        await factory.SeedTenantSubscriptionWithUsageAsync(tenant.Id, PlanTier.Pro, ocrPagesUsed: 1_000);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);

        const string mutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String("""%PDF-1.7 invoice aws"""u8.ToArray())
            }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("The current plan has reached its monthly OCR quota.", errors[0].GetProperty("message").GetString());
        Assert.Equal("Subscription.OcrQuotaExceeded", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SubmitReviewedDocument_PersistsAndSeedsPendingApprovalItems()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.approvals@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace", "finance-ops").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;
        var managerAccount = Account.Create("manager.approvals@finflow.test", "hashed-password").Value;
        var managerMembership = TenantMembership.Create(managerAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership, managerAccount, managerMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);
        using var managerClient = factory.CreateAuthenticatedClient(managerAccount.Id, managerAccount.Email, RoleType.Manager, tenant.Id, managerMembership.Id);

        const string uploadMutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
              }
            }
            """;

        var uploadPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, uploadMutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String("%PDF-1.7 invoice aws"u8.ToArray())
            }
        });

        var documentId = uploadPayload.RootElement
            .GetProperty("data")
            .GetProperty("uploadDocumentForReview")
            .GetProperty("documentId")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(documentId));

        const string submitMutation = """
            mutation($input: SubmitReviewedDocumentInput!) {
              submitReviewedDocument(input: $input) {
                documentId
                status
                submittedAt
                vendorName
                reference
                totalAmount
                dueDate
                reviewedByStaff
              }
            }
            """;

        var submitPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, submitMutation, new
        {
            input = new
            {
                documentId,
                originalFileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                vendorName = "Amazon Web Services, Inc.",
                reference = "INV-2026-0101",
                documentDate = "2026-04-18",
                dueDate = "2026-05-02",
                category = "Software & SaaS",
                vendorTaxId = "TX-990-2134",
                subtotal = 1200.00m,
                vat = 250.00m,
                totalAmount = 1450.00m,
                source = "staff-upload",
                confidenceLabel = "Staff corrected",
                lineItems = new[]
                {
                    new { itemName = "Cloud Compute Instance - t3.large", quantity = 1m, unitPrice = 850.00m, total = 850.00m },
                    new { itemName = "Storage Block (EBS) - 2TB", quantity = 1m, unitPrice = 300.00m, total = 300.00m },
                    new { itemName = "Support Plan - Business", quantity = 1m, unitPrice = 300.00m, total = 300.00m }
                }
            }
        });

        var submitted = submitPayload.RootElement.GetProperty("data").GetProperty("submitReviewedDocument");
        Assert.Equal("ReadyForApproval", submitted.GetProperty("status").GetString());
        Assert.Equal("Amazon Web Services, Inc.", submitted.GetProperty("vendorName").GetString());

        const string approvalsQuery = """
            query {
              pendingApprovalItems {
                documentId
                title
                requester
                department
                amount
                dueDate
                priority
                status
              }
            }
            """;

        var approvalsPayload = await GraphQlApiTestFactory.PostGraphQlAsync(managerClient, approvalsQuery);
        var items = approvalsPayload.RootElement.GetProperty("data").GetProperty("pendingApprovalItems");

        Assert.Equal(1, items.GetArrayLength());

        var item = items[0];
        Assert.Equal(documentId, item.GetProperty("documentId").GetString());
        Assert.Equal("Amazon Web Services, Inc. · INV-2026-0101", item.GetProperty("title").GetString());
        Assert.Equal("staff.approvals@finflow.test", item.GetProperty("requester").GetString());
        Assert.Equal("Software & SaaS · staff-upload", item.GetProperty("department").GetString());
        Assert.Equal("Medium", item.GetProperty("priority").GetString());
        Assert.Equal("ReadyForApproval", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ApproveReviewedDocument_Mutation_Denies_SelfApproval_For_ApproverRole()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("manager.self.approve@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace", "finance-ops-approve").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(tenant);
            db.Add(membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Manager, tenant.Id, membership.Id);

        var documentId = await SubmitReviewedDocumentAsync(client);

        const string approveMutation = """
            mutation($input: ApproveReviewedDocumentInput!) {
              approveReviewedDocument(input: $input) {
                documentId
                status
                reviewedByStaff
              }
            }
            """;

        var approvePayload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, approveMutation, new
        {
            input = new { documentId }
        });

        var errors = approvePayload.RootElement.GetProperty("errors");
        Assert.Equal("Submitter cannot approve their own reviewed document.", errors[0].GetProperty("message").GetString());

        const string approvalsQuery = """
            query {
              pendingApprovalItems {
                documentId
              }
            }
            """;

        var approvalsPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, approvalsQuery);
        Assert.Single(approvalsPayload.RootElement.GetProperty("data").GetProperty("pendingApprovalItems").EnumerateArray());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == Guid.Parse(documentId!));
        Assert.Equal(ReviewedDocumentStatus.ReadyForApproval, persisted.Status);
        Assert.Null(persisted.RejectionReason);
    }

    [Fact]
    public async Task RejectReviewedDocument_Mutation_UpdatesStatus_And_PersistsReason()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.reject@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Finance Ops Workspace", "finance-ops-reject").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;
        var managerAccount = Account.Create("manager.reject@finflow.test", "hashed-password").Value;
        var managerMembership = TenantMembership.Create(managerAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership, managerAccount, managerMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);
        using var managerClient = factory.CreateAuthenticatedClient(managerAccount.Id, managerAccount.Email, RoleType.Manager, tenant.Id, managerMembership.Id);

        var documentId = await SubmitReviewedDocumentAsync(client);

        const string rejectMutation = """
            mutation($input: RejectReviewedDocumentInput!) {
              rejectReviewedDocument(input: $input) {
                documentId
                status
                reviewedByStaff
              }
            }
            """;

        var rejectPayload = await GraphQlApiTestFactory.PostGraphQlAsync(managerClient, rejectMutation, new
        {
            input = new
            {
                documentId,
                reason = "Duplicate invoice submitted"
            }
        });

        var rejected = rejectPayload.RootElement.GetProperty("data").GetProperty("rejectReviewedDocument");
        Assert.Equal(documentId, rejected.GetProperty("documentId").GetString());
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());

        const string approvalsQuery = """
            query {
              pendingApprovalItems {
                documentId
              }
            }
            """;

        var approvalsPayload = await GraphQlApiTestFactory.PostGraphQlAsync(managerClient, approvalsQuery);
        Assert.Empty(approvalsPayload.RootElement.GetProperty("data").GetProperty("pendingApprovalItems").EnumerateArray());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == Guid.Parse(documentId!));
        Assert.Equal(ReviewedDocumentStatus.Rejected, persisted.Status);
        Assert.Equal("Duplicate invoice submitted", persisted.RejectionReason);
    }

    [Fact]
    public async Task ApproveReviewedDocument_Mutation_Denies_CrossTenantAccess()
    {
        await using var factory = new GraphQlApiTestFactory();

        var sourceAccount = Account.Create("staff.source@finflow.test", "hashed-password").Value;
        var sourceTenant = Tenant.Create("Source Workspace", "source-workspace").Value;
        var sourceMembership = TenantMembership.Create(sourceAccount.Id, sourceTenant.Id, RoleType.Staff).Value;

        var otherAccount = Account.Create("manager.other@finflow.test", "hashed-password").Value;
        var otherTenant = Tenant.Create("Other Workspace", "other-workspace").Value;
        var otherMembership = TenantMembership.Create(otherAccount.Id, otherTenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(sourceAccount, sourceTenant, sourceMembership, otherAccount, otherTenant, otherMembership);
        });
        await factory.SeedTenantSubscriptionAsync(sourceTenant.Id, PlanTier.Pro);

        using var sourceClient = factory.CreateAuthenticatedClient(
            sourceAccount.Id,
            sourceAccount.Email,
            RoleType.Staff,
            sourceTenant.Id,
            sourceMembership.Id);

        var documentId = await SubmitReviewedDocumentAsync(sourceClient);

        using var otherClient = factory.CreateAuthenticatedClient(
            otherAccount.Id,
            otherAccount.Email,
            RoleType.Manager,
            otherTenant.Id,
            otherMembership.Id);

        const string approveMutation = """
            mutation($input: ApproveReviewedDocumentInput!) {
              approveReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(otherClient, approveMutation, new
        {
            input = new { documentId }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("Reviewed document not found.", errors[0].GetProperty("message").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == Guid.Parse(documentId!));

        Assert.Equal(ReviewedDocumentStatus.ReadyForApproval, persisted.Status);
        Assert.Null(persisted.RejectionReason);
    }

    [Fact]
    public async Task ApproveReviewedDocument_Mutation_Allows_SameTenantDifferentMembership_Access()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Shared Approval Workspace", "shared-approval-workspace").Value;
        var staffAccount = Account.Create("staff.shared.approval@finflow.test", "hashed-password").Value;
        var staffMembership = TenantMembership.Create(staffAccount.Id, tenant.Id, RoleType.Staff).Value;
        var managerAccount = Account.Create("manager.shared.approval@finflow.test", "hashed-password").Value;
        var managerMembership = TenantMembership.Create(managerAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, staffAccount, staffMembership, managerAccount, managerMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var staffClient = factory.CreateAuthenticatedClient(
            staffAccount.Id,
            staffAccount.Email,
            RoleType.Staff,
            tenant.Id,
            staffMembership.Id);
        using var managerClient = factory.CreateAuthenticatedClient(
            managerAccount.Id,
            managerAccount.Email,
            RoleType.Manager,
            tenant.Id,
            managerMembership.Id);

        var documentId = await SubmitReviewedDocumentAsync(staffClient);

        const string approveMutation = """
            mutation($input: ApproveReviewedDocumentInput!) {
              approveReviewedDocument(input: $input) {
                documentId
                status
                reviewedByStaff
              }
            }
            """;

        var approvePayload = await GraphQlApiTestFactory.PostGraphQlAsync(managerClient, approveMutation, new
        {
            input = new { documentId }
        });

        var approved = approvePayload.RootElement.GetProperty("data").GetProperty("approveReviewedDocument");
        Assert.Equal(documentId, approved.GetProperty("documentId").GetString());
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("staff.shared.approval@finflow.test", approved.GetProperty("reviewedByStaff").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == Guid.Parse(documentId!));

        Assert.Equal(ReviewedDocumentStatus.Approved, persisted.Status);
        Assert.Null(persisted.RejectionReason);
    }

    [Fact]
    public async Task ApproveReviewedDocument_Mutation_Denies_StaffRole_Access()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Approve Role Workspace", "approve-role-workspace").Value;
        var ownerAccount = Account.Create("owner.approve.role@finflow.test", "hashed-password").Value;
        var ownerMembership = TenantMembership.Create(ownerAccount.Id, tenant.Id, RoleType.Staff).Value;
        var staffAccount = Account.Create("staff.approve.role@finflow.test", "hashed-password").Value;
        var staffMembership = TenantMembership.Create(staffAccount.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, ownerAccount, ownerMembership, staffAccount, staffMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var ownerClient = factory.CreateAuthenticatedClient(
            ownerAccount.Id,
            ownerAccount.Email,
            RoleType.Staff,
            tenant.Id,
            ownerMembership.Id);
        using var staffClient = factory.CreateAuthenticatedClient(
            staffAccount.Id,
            staffAccount.Email,
            RoleType.Staff,
            tenant.Id,
            staffMembership.Id);

        var documentId = await SubmitReviewedDocumentAsync(ownerClient);

        const string approveMutation = """
            mutation($input: ApproveReviewedDocumentInput!) {
              approveReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(staffClient, approveMutation, new
        {
            input = new { documentId }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("The current user is not authorized to approve reviewed documents.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task SubmitReviewedDocument_Mutation_Denies_CrossTenantDraftAccess()
    {
        await using var factory = new GraphQlApiTestFactory();

        var sourceAccount = Account.Create("staff.source.submit@finflow.test", "hashed-password").Value;
        var sourceTenant = Tenant.Create("Source Submit Workspace", "source-submit-workspace").Value;
        var sourceMembership = TenantMembership.Create(sourceAccount.Id, sourceTenant.Id, RoleType.Staff).Value;

        var otherAccount = Account.Create("staff.other.submit@finflow.test", "hashed-password").Value;
        var otherTenant = Tenant.Create("Other Submit Workspace", "other-submit-workspace").Value;
        var otherMembership = TenantMembership.Create(otherAccount.Id, otherTenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(sourceAccount, sourceTenant, sourceMembership, otherAccount, otherTenant, otherMembership);
        });
        await factory.SeedTenantSubscriptionAsync(sourceTenant.Id, PlanTier.Pro);

        using var sourceClient = factory.CreateAuthenticatedClient(
            sourceAccount.Id,
            sourceAccount.Email,
            RoleType.Staff,
            sourceTenant.Id,
            sourceMembership.Id);

        var documentId = await UploadDraftAsync(sourceClient);

        using var otherClient = factory.CreateAuthenticatedClient(
            otherAccount.Id,
            otherAccount.Email,
            RoleType.Staff,
            otherTenant.Id,
            otherMembership.Id);

        const string submitMutation = """
            mutation($input: SubmitReviewedDocumentInput!) {
              submitReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var submitPayload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(otherClient, submitMutation, new
        {
            input = BuildSubmitInput(documentId)
        });

        var errors = submitPayload.RootElement.GetProperty("errors");
        Assert.Equal("Uploaded document draft not found.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task SubmitReviewedDocument_Mutation_Denies_SameTenantDifferentMembership_DraftAccess()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Shared Workspace", "shared-docs").Value;

        var ownerAccount = Account.Create("owner.documents@finflow.test", "hashed-password").Value;
        var ownerMembership = TenantMembership.Create(ownerAccount.Id, tenant.Id, RoleType.Staff).Value;

        var otherAccount = Account.Create("other.documents@finflow.test", "hashed-password").Value;
        var otherMembership = TenantMembership.Create(otherAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, ownerAccount, ownerMembership, otherAccount, otherMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var ownerClient = factory.CreateAuthenticatedClient(
            ownerAccount.Id,
            ownerAccount.Email,
            RoleType.Staff,
            tenant.Id,
            ownerMembership.Id);
        using var otherClient = factory.CreateAuthenticatedClient(
            otherAccount.Id,
            otherAccount.Email,
            RoleType.Manager,
            tenant.Id,
            otherMembership.Id);

        var draftId = await UploadDraftAsync(ownerClient);

        const string submitMutation = """
            mutation($input: SubmitReviewedDocumentInput!) {
              submitReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(otherClient, submitMutation, new
        {
            input = BuildSubmitInput(draftId)
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("Uploaded document draft not found.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task MyDocumentDrafts_Query_Returns_Only_CurrentMembership_Drafts()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Workspace Drafts", "workspace-drafts").Value;
        var staffAccount = Account.Create("staff.one@finflow.test", "hashed-password").Value;
        var staffMembership = TenantMembership.Create(staffAccount.Id, tenant.Id, RoleType.Staff).Value;
        var managerAccount = Account.Create("manager.one@finflow.test", "hashed-password").Value;
        var managerMembership = TenantMembership.Create(managerAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, staffAccount, staffMembership, managerAccount, managerMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var staffClient = factory.CreateAuthenticatedClient(
            staffAccount.Id,
            staffAccount.Email,
            RoleType.Staff,
            tenant.Id,
            staffMembership.Id);
        using var managerClient = factory.CreateAuthenticatedClient(
            managerAccount.Id,
            managerAccount.Email,
            RoleType.Manager,
            tenant.Id,
            managerMembership.Id);

        var staffDraftId = await UploadDraftAsync(staffClient);
        var managerDraftId = await UploadDraftAsync(managerClient);

        var staffDrafts = await QueryDocumentsAsync(staffClient, "myDocumentDrafts");
        Assert.Single(staffDrafts);
        Assert.Equal(staffDraftId, staffDrafts[0].GetProperty("documentId").GetString());
        Assert.Equal("staff.one@finflow.test", staffDrafts[0].GetProperty("ownerEmail").GetString());

        var managerDrafts = await QueryDocumentsAsync(managerClient, "myDocumentDrafts");
        Assert.Single(managerDrafts);
        Assert.Equal(managerDraftId, managerDrafts[0].GetProperty("documentId").GetString());
        Assert.Equal("manager.one@finflow.test", managerDrafts[0].GetProperty("ownerEmail").GetString());
    }

    [Fact]
    public async Task MyDocumentDraft_Query_Returns_Full_Editable_Detail_For_Owner()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("draft.owner@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Draft Owner Workspace", "draft-owner-workspace").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Staff,
            tenant.Id,
            membership.Id);

        var documentId = await UploadDraftAsync(client);

        var draft = await QueryDocumentDraftAsync(client, documentId);

        Assert.Equal(documentId, draft.GetProperty("documentId").GetString());
        Assert.Equal("invoice-aws-october.pdf", draft.GetProperty("originalFileName").GetString());
        Assert.Equal("application/pdf", draft.GetProperty("contentType").GetString());
        Assert.Equal("Amazon Web Services, Inc.", draft.GetProperty("vendorName").GetString());
        Assert.Equal("Software & SaaS", draft.GetProperty("category").GetString());
        Assert.Equal("draft.owner@finflow.test", draft.GetProperty("reviewedByStaff").GetString());
        Assert.Equal("High precision", draft.GetProperty("confidenceLabel").GetString());
        Assert.Equal(3, draft.GetProperty("lineItems").GetArrayLength());
    }

    [Fact]
    public async Task MyDocumentDraft_Query_Denies_SameTenantDifferentMembership_Access()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Draft Shared Workspace", "draft-shared-workspace").Value;
        var ownerAccount = Account.Create("draft.owner.shared@finflow.test", "hashed-password").Value;
        var ownerMembership = TenantMembership.Create(ownerAccount.Id, tenant.Id, RoleType.Staff).Value;
        var otherAccount = Account.Create("draft.other.shared@finflow.test", "hashed-password").Value;
        var otherMembership = TenantMembership.Create(otherAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, ownerAccount, ownerMembership, otherAccount, otherMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var ownerClient = factory.CreateAuthenticatedClient(
            ownerAccount.Id,
            ownerAccount.Email,
            RoleType.Staff,
            tenant.Id,
            ownerMembership.Id);
        using var otherClient = factory.CreateAuthenticatedClient(
            otherAccount.Id,
            otherAccount.Email,
            RoleType.Manager,
            tenant.Id,
            otherMembership.Id);

        var documentId = await UploadDraftAsync(ownerClient);

        const string query = """
            query($documentId: UUID!) {
              myDocumentDraft(documentId: $documentId) {
                documentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(otherClient, query, new
        {
            documentId
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("Uploaded document draft not found.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task MySubmittedDocuments_Query_Returns_Only_CurrentMembership_Submitted_History()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Workspace History", "workspace-history").Value;
        var staffAccount = Account.Create("history.staff@finflow.test", "hashed-password").Value;
        var staffMembership = TenantMembership.Create(staffAccount.Id, tenant.Id, RoleType.Staff).Value;
        var managerAccount = Account.Create("history.manager@finflow.test", "hashed-password").Value;
        var managerMembership = TenantMembership.Create(managerAccount.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, staffAccount, staffMembership, managerAccount, managerMembership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var staffClient = factory.CreateAuthenticatedClient(
            staffAccount.Id,
            staffAccount.Email,
            RoleType.Staff,
            tenant.Id,
            staffMembership.Id);
        using var managerClient = factory.CreateAuthenticatedClient(
            managerAccount.Id,
            managerAccount.Email,
            RoleType.Manager,
            tenant.Id,
            managerMembership.Id);

        var staffDocumentId = await SubmitReviewedDocumentAsync(staffClient);
        var managerDocumentId = await SubmitReviewedDocumentAsync(managerClient);

        var staffHistory = await QueryDocumentsAsync(staffClient, "mySubmittedDocuments");
        Assert.Single(staffHistory);
        Assert.Equal(staffDocumentId, staffHistory[0].GetProperty("documentId").GetString());
        Assert.Equal("history.staff@finflow.test", staffHistory[0].GetProperty("submittedByEmail").GetString());

        var managerHistory = await QueryDocumentsAsync(managerClient, "mySubmittedDocuments");
        Assert.Single(managerHistory);
        Assert.Equal(managerDocumentId, managerHistory[0].GetProperty("documentId").GetString());
        Assert.Equal("history.manager@finflow.test", managerHistory[0].GetProperty("submittedByEmail").GetString());
    }

    [Fact]
    public async Task MySubmittedDocuments_Query_Maps_ReadyForApproval_Status_To_Submitted()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("history.documents@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("History Workspace", "history-workspace").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Accountant).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Accountant,
            tenant.Id,
            membership.Id);

        var submittedId = await SubmitReviewedDocumentAsync(client);

        var documents = await QueryDocumentsAsync(client, "mySubmittedDocuments");

        Assert.Single(documents);
        Assert.Equal(submittedId, documents[0].GetProperty("documentId").GetString());
        Assert.Equal("Submitted", documents[0].GetProperty("status").GetString());
        Assert.Equal("history.documents@finflow.test", documents[0].GetProperty("submittedByEmail").GetString());
    }

    [Fact]
    public async Task PendingApprovalItems_Query_Denies_StaffRole_Access()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.pending.denied@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Pending Denied Workspace", "pending-denied-workspace").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Staff,
            tenant.Id,
            membership.Id);

        const string approvalsQuery = """
            query {
              pendingApprovalItems {
                documentId
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, approvalsQuery);
        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("The current user is not authorized to approve reviewed documents.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task PendingApprovalItems_Query_Returns_Only_CurrentTenant_Items_For_Approvers()
    {
        await using var factory = new GraphQlApiTestFactory();

        var sourceAccount = Account.Create("manager.pending.source@finflow.test", "hashed-password").Value;
        var sourceTenant = Tenant.Create("Pending Source Workspace", "pending-source-workspace").Value;
        var sourceMembership = TenantMembership.Create(sourceAccount.Id, sourceTenant.Id, RoleType.Manager).Value;

        var otherAccount = Account.Create("manager.pending.other@finflow.test", "hashed-password").Value;
        var otherTenant = Tenant.Create("Pending Other Workspace", "pending-other-workspace").Value;
        var otherMembership = TenantMembership.Create(otherAccount.Id, otherTenant.Id, RoleType.Manager).Value;

        var sourceSubmitter = Account.Create("staff.pending.source@finflow.test", "hashed-password").Value;
        var sourceSubmitterMembership = TenantMembership.Create(sourceSubmitter.Id, sourceTenant.Id, RoleType.Staff).Value;
        var otherSubmitter = Account.Create("staff.pending.other@finflow.test", "hashed-password").Value;
        var otherSubmitterMembership = TenantMembership.Create(otherSubmitter.Id, otherTenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(
                sourceAccount,
                sourceTenant,
                sourceMembership,
                otherAccount,
                otherTenant,
                otherMembership,
                sourceSubmitter,
                sourceSubmitterMembership,
                otherSubmitter,
                otherSubmitterMembership);
        });
        await factory.SeedTenantSubscriptionAsync(sourceTenant.Id, PlanTier.Pro);
        await factory.SeedTenantSubscriptionAsync(otherTenant.Id, PlanTier.Pro);

        using var sourceClient = factory.CreateAuthenticatedClient(
            sourceAccount.Id,
            sourceAccount.Email,
            RoleType.Manager,
            sourceTenant.Id,
            sourceMembership.Id);
        using var otherClient = factory.CreateAuthenticatedClient(
            otherAccount.Id,
            otherAccount.Email,
            RoleType.Manager,
            otherTenant.Id,
            otherMembership.Id);
        using var sourceSubmitterClient = factory.CreateAuthenticatedClient(
            sourceSubmitter.Id,
            sourceSubmitter.Email,
            RoleType.Staff,
            sourceTenant.Id,
            sourceSubmitterMembership.Id);
        using var otherSubmitterClient = factory.CreateAuthenticatedClient(
            otherSubmitter.Id,
            otherSubmitter.Email,
            RoleType.Staff,
            otherTenant.Id,
            otherSubmitterMembership.Id);

        var sourceDocumentId = await SubmitReviewedDocumentAsync(sourceSubmitterClient);
        var otherDocumentId = await SubmitReviewedDocumentAsync(otherSubmitterClient);

        const string approvalsQuery = """
            query {
              pendingApprovalItems {
                documentId
                requester
              }
            }
            """;

        var sourcePayload = await GraphQlApiTestFactory.PostGraphQlAsync(sourceClient, approvalsQuery);
        var sourceItems = sourcePayload.RootElement.GetProperty("data").GetProperty("pendingApprovalItems");
        Assert.Single(sourceItems.EnumerateArray());
        Assert.Equal(sourceDocumentId, sourceItems[0].GetProperty("documentId").GetString());
        Assert.Equal("staff.pending.source@finflow.test", sourceItems[0].GetProperty("requester").GetString());

        var otherPayload = await GraphQlApiTestFactory.PostGraphQlAsync(otherClient, approvalsQuery);
        var otherItems = otherPayload.RootElement.GetProperty("data").GetProperty("pendingApprovalItems");
        Assert.Single(otherItems.EnumerateArray());
        Assert.Equal(otherDocumentId, otherItems[0].GetProperty("documentId").GetString());
        Assert.Equal("staff.pending.other@finflow.test", otherItems[0].GetProperty("requester").GetString());
    }

    [Fact]
    public async Task SubmitReviewedDocument_Mutation_Rejects_Negative_LineItemQuantity()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("staff.negative.quantity@finflow.test", "hashed-password").Value;
        var tenant = Tenant.Create("Negative Quantity Workspace", "negative-quantity-workspace").Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(account, tenant, membership);
        });
        await factory.SeedTenantSubscriptionAsync(tenant.Id, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Staff, tenant.Id, membership.Id);
        var documentId = await UploadDraftAsync(client);

        const string submitMutation = """
            mutation($input: SubmitReviewedDocumentInput!) {
              submitReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, submitMutation, new
        {
            input = new
            {
                documentId,
                originalFileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                vendorName = "Amazon Web Services, Inc.",
                reference = "INV-2026-0101",
                documentDate = "2026-04-18",
                dueDate = "2026-05-02",
                category = "Software & SaaS",
                vendorTaxId = "TX-990-2134",
                subtotal = 1200.00m,
                vat = 250.00m,
                totalAmount = 1450.00m,
                source = "staff-upload",
                confidenceLabel = "Staff corrected",
                lineItems = new[]
                {
                    new { itemName = "Cloud Compute Instance - t3.large", quantity = -1m, unitPrice = 850.00m, total = 850.00m },
                    new { itemName = "Storage Block (EBS) - 2TB", quantity = 1m, unitPrice = 300.00m, total = 300.00m },
                    new { itemName = "Support Plan - Business", quantity = 1m, unitPrice = 300.00m, total = 300.00m }
                }
            }
        });

        var errors = payload.RootElement.GetProperty("errors");
        Assert.Equal("Line item quantity must be greater than zero.", errors[0].GetProperty("message").GetString());
    }

    private static object BuildSubmitInput(string? documentId) => new
    {
        documentId,
        originalFileName = "invoice-aws-october.pdf",
        contentType = "application/pdf",
        vendorName = "Amazon Web Services, Inc.",
        reference = "INV-2026-0101",
        documentDate = "2026-04-18",
        dueDate = "2026-05-02",
        category = "Software & SaaS",
        vendorTaxId = "TX-990-2134",
        subtotal = 1200.00m,
        vat = 250.00m,
        totalAmount = 1450.00m,
        source = "staff-upload",
        confidenceLabel = "Staff corrected",
        lineItems = new[]
        {
            new { itemName = "Cloud Compute Instance - t3.large", quantity = 1m, unitPrice = 850.00m, total = 850.00m },
            new { itemName = "Storage Block (EBS) - 2TB", quantity = 1m, unitPrice = 300.00m, total = 300.00m },
            new { itemName = "Support Plan - Business", quantity = 1m, unitPrice = 300.00m, total = 300.00m }
        }
    };

    private static async Task<string> UploadDraftAsync(HttpClient client)
    {
        const string uploadMutation = """
            mutation($input: UploadDocumentForReviewInput!) {
              uploadDocumentForReview(input: $input) {
                documentId
              }
            }
            """;

        var uploadPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, uploadMutation, new
        {
            input = new
            {
                fileName = "invoice-aws-october.pdf",
                contentType = "application/pdf",
                base64Content = Convert.ToBase64String("%PDF-1.7 invoice aws"u8.ToArray())
            }
        });

        var documentId = uploadPayload.RootElement
            .GetProperty("data")
            .GetProperty("uploadDocumentForReview")
            .GetProperty("documentId")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(documentId));
        return documentId!;
    }

    private static async Task<string> SubmitReviewedDocumentAsync(HttpClient client)
    {
        var documentId = await UploadDraftAsync(client);

        const string submitMutation = """
            mutation($input: SubmitReviewedDocumentInput!) {
              submitReviewedDocument(input: $input) {
                documentId
                status
              }
            }
            """;

        var submitPayload = await GraphQlApiTestFactory.PostGraphQlAsync(client, submitMutation, new
        {
            input = BuildSubmitInput(documentId)
        });

        Assert.Equal("ReadyForApproval", submitPayload.RootElement.GetProperty("data").GetProperty("submitReviewedDocument").GetProperty("status").GetString());
        return documentId!;
    }

    private static async Task<JsonElement[]> QueryDocumentsAsync(HttpClient client, string fieldName)
    {
        var selection = fieldName switch
        {
            "myDocumentDrafts" => """
                documentId
                originalFileName
                vendorName
                reference
                totalAmount
                confidenceLabel
                ownerEmail
                uploadedAt
                """,
            "mySubmittedDocuments" => """
                documentId
                originalFileName
                vendorName
                reference
                totalAmount
                status
                submittedByEmail
                submittedAt
                lastUpdatedAt
                rejectionReason
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "Unsupported documents query field.")
        };

        var query = $$"""
            query {
              {{fieldName}} {
                {{selection}}
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);
        return payload.RootElement
            .GetProperty("data")
            .GetProperty(fieldName)
            .EnumerateArray()
            .ToArray();
    }

    private static async Task<JsonElement> QueryDocumentDraftAsync(HttpClient client, string documentId)
    {
        const string query = """
            query($documentId: UUID!) {
              myDocumentDraft(documentId: $documentId) {
                documentId
                originalFileName
                contentType
                vendorName
                reference
                documentDate
                dueDate
                category
                vendorTaxId
                subtotal
                vat
                totalAmount
                source
                reviewedByStaff
                confidenceLabel
                lineItems {
                  itemName
                  quantity
                  unitPrice
                  total
                }
              }
            }
            """;

        var payload = await GraphQlApiTestFactory.PostGraphQlAsync(client, query, new
        {
            documentId
        });

        return payload.RootElement
            .GetProperty("data")
            .GetProperty("myDocumentDraft");
    }

}
