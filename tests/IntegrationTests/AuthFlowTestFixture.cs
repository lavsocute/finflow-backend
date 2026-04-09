using BCrypt.Net;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Settings;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Auth;
using FinFlow.Infrastructure.Repositories;
using FinFlow.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinFlow.IntegrationTests;

internal sealed class AuthFlowTestFixture
{
    private readonly IOptions<JwtSettings> _jwtOptions = Options.Create(new JwtSettings
    {
        Secret = "super-secret-key-for-tests-only-123456",
        Issuer = "FinFlow.Tests",
        Audience = "FinFlow.Tests",
        AccessTokenExpirationMinutes = 30,
        RefreshTokenExpirationDays = 7
    });

    public TestScope CreateScope()
    {
        var currentTenant = new CurrentTenant();
        var rateLimiter = new TestLoginRateLimiter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new ApplicationDbContext(options, currentTenant);
        var authService = new AuthService(
            new AccountRepository(dbContext),
            new TenantRepository(dbContext),
            new TenantApprovalRequestRepository(dbContext),
            new TenantMembershipRepository(dbContext),
            new DepartmentRepository(dbContext),
            new InvitationRepository(dbContext),
            new RefreshTokenRepository(dbContext),
            dbContext,
            new JwtTokenService(_jwtOptions),
            rateLimiter,
            currentTenant);

        return new TestScope(dbContext, currentTenant, authService, rateLimiter);
    }

    internal sealed class TestScope : IDisposable
    {
        public TestScope(ApplicationDbContext dbContext, CurrentTenant currentTenant, AuthService authService, TestLoginRateLimiter rateLimiter)
        {
            DbContext = dbContext;
            CurrentTenant = currentTenant;
            AuthService = authService;
            RateLimiter = rateLimiter;
        }

        public ApplicationDbContext DbContext { get; }
        public CurrentTenant CurrentTenant { get; }
        public AuthService AuthService { get; }
        public TestLoginRateLimiter RateLimiter { get; }

        public Tenant SeedTenant(string name, string code)
        {
            var tenant = Tenant.Create(name, code).Value;
            DbContext.Add(tenant);
            return tenant;
        }

        public Department SeedDepartment(string name, Guid tenantId, Guid? parentId = null)
        {
            var department = Department.Create(name, tenantId, parentId).Value;
            DbContext.Add(department);
            return department;
        }

        public Account SeedAccount(string email, string password, Guid departmentId)
        {
            var account = Account.Create(email, BCrypt.Net.BCrypt.HashPassword(password), departmentId).Value;
            DbContext.Add(account);
            return account;
        }

        public TenantMembership SeedMembership(Guid accountId, Guid tenantId, RoleType role, bool isOwner = false)
        {
            var membership = TenantMembership.Create(accountId, tenantId, role, isOwner).Value;
            DbContext.Add(membership);
            return membership;
        }

        public Invitation SeedInvitation(string email, Guid tenantId, Guid inviterMembershipId, RoleType role, string rawToken, DateTime? expiresAt = null)
        {
            var invitation = Invitation.Create(
                email,
                tenantId,
                inviterMembershipId,
                role,
                rawToken,
                expiresAt ?? DateTime.UtcNow.AddDays(7)).Value;
            DbContext.Add(invitation);
            return invitation;
        }

        public TenantApprovalRequest SeedTenantApprovalRequest(
            string tenantCode,
            string name,
            string companyName,
            string taxCode,
            Guid requestedById,
            DateTime expiresAt,
            string currency = "VND",
            string? address = null,
            string? phone = null,
            string? contactPerson = null,
            string? businessType = null,
            int? employeeCount = null)
        {
            var request = TenantApprovalRequest.Create(
                tenantCode,
                name,
                companyName,
                taxCode,
                address,
                phone,
                contactPerson,
                businessType,
                employeeCount,
                currency,
                requestedById,
                expiresAt).Value;

            DbContext.Add(request);
            return request;
        }

        public RefreshToken SeedRefreshToken(string rawToken, Guid accountId, Guid membershipId, int expirationDays = 7)
        {
            var refreshToken = RefreshToken.Create(rawToken, accountId, membershipId, expirationDays).Value;
            DbContext.Add(refreshToken);
            return refreshToken;
        }

        public async Task SaveSeedAsync()
        {
            CurrentTenant.Id = Guid.NewGuid();
            CurrentTenant.IsSuperAdmin = true;
            await DbContext.SaveChangesAsync();
            CurrentTenant.Id = null;
            CurrentTenant.MembershipId = null;
            CurrentTenant.IsSuperAdmin = false;
            DbContext.ChangeTracker.Clear();
        }

        public void ActAsSuperAdmin()
        {
            CurrentTenant.Id = null;
            CurrentTenant.MembershipId = null;
            CurrentTenant.IsSuperAdmin = true;
        }

        public void Dispose() => DbContext.Dispose();
    }

    internal sealed class TestLoginRateLimiter : ILoginRateLimiter
    {
        public List<(string? Ip, string Email, Guid? TenantId)> RecordedFailures { get; } = new();

        public Task<bool> IsBlockedAsync(string? ip, string email, Guid? tenantId = null) => Task.FromResult(false);
        public Task RecordFailureAsync(string? ip, string email, Guid? tenantId = null)
        {
            RecordedFailures.Add((ip, email, tenantId));
            return Task.CompletedTask;
        }
        public Task ResetAccountAsync(string email, Guid? tenantId = null) => Task.CompletedTask;
    }
}
