using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public class ChatAuthorizationServiceTests
{
    private static ChatAuthorizationService CreateService(
        TenantMembershipSummary? membership,
        Guid currentTenantId,
        bool isSuperAdmin = false,
        Guid? membershipId = null)
    {
        var resolvedMembershipId = membershipId ?? membership?.Id ?? Guid.Empty;
        var repository = new Mock<ITenantMembershipRepository>();
        repository
            .Setup(x => x.GetByIdAsync(resolvedMembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(currentTenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(isSuperAdmin);

        var tenantRepository = new Mock<ITenantRepository>();
        var accountRepository = new Mock<IAccountRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();

        return new ChatAuthorizationService(
            repository.Object,
            currentTenant.Object,
            tenantRepository.Object,
            accountRepository.Object,
            departmentRepository.Object);
    }

    [Fact]
    public async Task GetChatAccessScopeAsync_ThrowsForSuperAdminMembership()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var service = CreateService(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.SuperAdmin,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null),
            tenantId,
            membershipId: membershipId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetChatAccessScopeAsync(membershipId));

        Assert.Contains("SuperAdmin", ex.Message);
    }

    [Fact]
    public async Task GetChatAccessScopeAsync_ReturnsExpenseAndReceiptOnly_ForStaff()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var service = CreateService(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                departmentId,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null),
            tenantId,
            membershipId: membershipId);

        var scope = await service.GetChatAccessScopeAsync(membershipId);

        Assert.Equal(RoleType.Staff, scope.Role);
        Assert.Equal(membershipId, scope.OwnerMembershipId);
        Assert.Equal(departmentId, scope.DepartmentId);
        Assert.Equal(new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt, DocumentChunkType.LineItem }, scope.AllowedChunkTypes);
    }

    [Fact]
    public async Task GetChatAccessScopeAsync_ThrowsWhenMembershipBelongsToAnotherTenant()
    {
        var membershipId = Guid.NewGuid();
        var membershipTenantId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();

        var service = CreateService(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                membershipTenantId,
                Guid.NewGuid(),
                RoleType.Manager,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null),
            currentTenantId,
            membershipId: membershipId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetChatAccessScopeAsync(membershipId));

        Assert.Contains("does not belong to the current tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetChatAccessScopeAsync_ThrowsWhenManagerHasNoDepartmentBoundary()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var service = CreateService(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.Manager,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null),
            tenantId,
            membershipId: membershipId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetChatAccessScopeAsync(membershipId));

        Assert.Contains("department boundary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

