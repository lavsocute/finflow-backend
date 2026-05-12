using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public class ChatAuthorizationServiceTests
{
    [Fact]
    public async Task GetChatAccessScopeAsync_ThrowsForSuperAdminMembership()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membershipRepository = new Mock<ITenantMembershipRepository>();
        membershipRepository
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipSummary(
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
                null));

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(false);

        var service = new ChatAuthorizationService(membershipRepository.Object, currentTenant.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetChatAccessScopeAsync(membershipId));

        Assert.Contains("SuperAdmin", ex.Message);
    }

    [Fact]
    public async Task GetChatAccessScopeAsync_ReturnsExpenseAndReceiptOnly_ForStaff()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membershipRepository = new Mock<ITenantMembershipRepository>();
        membershipRepository
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipSummary(
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
                null));

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns(tenantId);
        currentTenant.SetupGet(x => x.IsSuperAdmin).Returns(false);

        var service = new ChatAuthorizationService(membershipRepository.Object, currentTenant.Object);

        var scope = await service.GetChatAccessScopeAsync(membershipId);

        Assert.Equal(RoleType.Staff, scope.Role);
        Assert.Equal(membershipId, scope.OwnerMembershipId);
        Assert.Equal(departmentId, scope.DepartmentId);
        Assert.Equal(new HashSet<DocumentChunkType> { DocumentChunkType.Expense, DocumentChunkType.Receipt }, scope.AllowedChunkTypes);
    }
}

