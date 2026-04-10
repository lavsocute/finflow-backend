using FinFlow.Application.Tenant.Commands.ApproveTenant;
using FinFlow.Application.Tenant.Commands.CreateIsolatedTenant;
using FinFlow.Application.Tenant.Commands.CreateSharedTenant;
using FinFlow.Application.Tenant.Commands.RejectTenant;
using FinFlow.Application.Tenant.DTOs.Requests;
using FinFlow.Application.Tenant.Queries.GetPendingTenantRequests;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class TenantCommandHandlerIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task CreateSharedTenantCommandHandler_CreatesTenantRootDepartmentOwnerMembership_AndTokens()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "tenant-handler-current");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("tenant.shared@finflow.test", "P@ssw0rd!");
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = currentTenant.Id;
        scope.CurrentTenant.MembershipId = currentMembership.Id;

        var handler = scope.CreateSharedTenantHandler();

        var result = await handler.Handle(
            new CreateSharedTenantCommand(new CreateSharedTenantRequest(account.Id, currentMembership.Id, "New Workspace", "tenant-handler-new", "VND")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Id, result.Value.Id);
        Assert.Equal(RoleType.TenantAdmin, result.Value.Role);
        Assert.NotEqual(currentTenant.Id, result.Value.IdTenant);

        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id && x.IdTenant == result.Value.IdTenant);

        var department = await scope.DbContext.Set<Department>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdTenant == result.Value.IdTenant);

        Assert.True(membership.IsOwner);
        Assert.Equal("Root", department.Name);
    }

    [Fact]
    public async Task CreateIsolatedTenantCommandHandler_SubmitsPendingRequest()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "tenant-handler-iso-current");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("tenant.isolated@finflow.test", "P@ssw0rd!");
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = currentTenant.Id;
        scope.CurrentTenant.MembershipId = currentMembership.Id;

        var handler = scope.CreateIsolatedTenantHandler();

        var result = await handler.Handle(
            new CreateIsolatedTenantCommand(
                new CreateIsolatedTenantRequest(
                    account.Id,
                    currentMembership.Id,
                    "Isolated Workspace",
                    "tenant-handler-isolated",
                    "VND",
                    new CompanyInfoRequest("FinFlow Corp", "TAX-123456", "123 Street", "0123456789", "Alice", "Finance", 42))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantApprovalStatus.Pending, result.Value.Status);

        var request = await scope.DbContext.Set<TenantApprovalRequest>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.TenantCode == "tenant-handler-isolated");

        Assert.Equal(account.Id, request.RequestedById);
        Assert.Equal("FinFlow Corp", request.CompanyName);
    }

    [Fact]
    public async Task GetPendingTenantRequestsQueryHandler_ReturnsSubmittedRequests_ForSuperAdmin()
    {
        using var scope = _fixture.CreateScope();

        var requester = scope.SeedAccount("tenant.pending@finflow.test", "P@ssw0rd!");
        var request = scope.SeedTenantApprovalRequest(
            "tenant-pending-handler",
            "Pending Handler Workspace",
            "Pending Corp",
            "TAX-PENDING",
            requester.Id,
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var handler = scope.CreateGetPendingTenantRequestsHandler();
        var result = await handler.Handle(new GetPendingTenantRequestsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, x => x.RequestId == request.Id && x.TenantCode == "tenant-pending-handler");
    }

    [Fact]
    public async Task ApproveTenantCommandHandler_CreatesTenant_AndOwnerMembership()
    {
        using var scope = _fixture.CreateScope();

        var requesterTenant = scope.SeedTenant("Requester Workspace", "tenant-approve-current");
        var requesterDepartment = scope.SeedDepartment("Root", requesterTenant.Id);
        var requester = scope.SeedAccount("tenant.approve@finflow.test", "P@ssw0rd!");
        var request = scope.SeedTenantApprovalRequest(
            "tenant-approve-handler",
            "Approve Handler Workspace",
            "Approve Corp",
            "TAX-APPROVE",
            requester.Id,
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var handler = scope.CreateApproveTenantHandler();
        var result = await handler.Handle(new ApproveTenantCommand(new ApproveTenantRequest(request.Id)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantApprovalStatus.Approved, result.Value.Status);
        Assert.NotNull(result.Value.TenantId);

        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == requester.Id && x.IdTenant == result.Value.TenantId);

        Assert.True(membership.IsOwner);
        Assert.Equal(RoleType.TenantAdmin, membership.Role);
    }

    [Fact]
    public async Task RejectTenantCommandHandler_UpdatesStatus_AndPreservesName()
    {
        using var scope = _fixture.CreateScope();

        var requesterTenant = scope.SeedTenant("Requester Workspace", "tenant-reject-current");
        var requesterDepartment = scope.SeedDepartment("Root", requesterTenant.Id);
        var requester = scope.SeedAccount("tenant.reject@finflow.test", "P@ssw0rd!");
        var request = scope.SeedTenantApprovalRequest(
            "tenant-reject-handler",
            "Reject Handler Workspace",
            "Reject Corp",
            "TAX-REJECT",
            requester.Id,
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var handler = scope.CreateRejectTenantHandler();
        var result = await handler.Handle(
            new RejectTenantCommand(new RejectTenantRequest(request.Id, "Insufficient verification")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantApprovalStatus.Rejected, result.Value.Status);
        Assert.Equal("Reject Handler Workspace", result.Value.Name);
    }
}
