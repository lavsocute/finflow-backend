using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Commands.AcceptInvite;
using FinFlow.Application.Membership.Commands.InviteMember;
using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Tenant.Commands.ApproveTenant;
using FinFlow.Application.Tenant.Commands.CreateIsolatedTenant;
using FinFlow.Application.Tenant.Commands.CreateSharedTenant;
using FinFlow.Application.Tenant.Commands.RejectTenant;
using FinFlow.Application.Tenant.Queries.GetPendingTenantRequests;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.Settings;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Auth;
using FinFlow.Infrastructure.Repositories;
using FinFlow.Infrastructure.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var tokenService = new JwtTokenService(_jwtOptions);
        var passwordHasher = new BcryptPasswordHasher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();

        services.AddScoped<IAccountRepository>(_ => new AccountRepository(dbContext));
        services.AddScoped<ITenantRepository>(_ => new TenantRepository(dbContext));
        services.AddScoped<ITenantApprovalRequestRepository>(_ => new TenantApprovalRequestRepository(dbContext));
        services.AddScoped<ITenantMembershipRepository>(_ => new TenantMembershipRepository(dbContext));
        services.AddScoped<IDepartmentRepository>(_ => new DepartmentRepository(dbContext));
        services.AddScoped<IInvitationRepository>(_ => new InvitationRepository(dbContext));
        services.AddScoped<IRefreshTokenRepository>(_ => new RefreshTokenRepository(dbContext));
        services.AddScoped<IUnitOfWork>(_ => dbContext);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddSingleton<ILoginRateLimiter>(rateLimiter);
        services.AddSingleton<ITokenService>(tokenService);
        services.AddSingleton<IPasswordHasher>(passwordHasher);
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        return new TestScope(dbContext, currentTenant, mediator, rateLimiter, tokenService, passwordHasher, serviceProvider);
    }

    internal sealed class TestScope : IDisposable
    {
        public TestScope(ApplicationDbContext dbContext, CurrentTenant currentTenant, IMediator mediator, TestLoginRateLimiter rateLimiter, ITokenService tokenService, IPasswordHasher passwordHasher, ServiceProvider serviceProvider)
        {
            DbContext = dbContext;
            CurrentTenant = currentTenant;
            Mediator = mediator;
            RateLimiter = rateLimiter;
            TokenService = tokenService;
            PasswordHasher = passwordHasher;
            ServiceProvider = serviceProvider;
        }

        public ApplicationDbContext DbContext { get; }
        public CurrentTenant CurrentTenant { get; }
        public IMediator Mediator { get; }
        public TestLoginRateLimiter RateLimiter { get; }
        public ITokenService TokenService { get; }
        public IPasswordHasher PasswordHasher { get; }
        public ServiceProvider ServiceProvider { get; }

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

        public Account SeedAccount(string email, string password)
        {
            var account = Account.Create(email, BCrypt.Net.BCrypt.HashPassword(password)).Value;
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

        public RefreshToken SeedRefreshToken(string rawToken, Guid accountId, Guid? membershipId = null, int expirationDays = 7)
        {
            if (!membershipId.HasValue)
                return SeedAccountRefreshToken(rawToken, accountId, expirationDays);

            var refreshToken = RefreshToken.Create(rawToken, accountId, membershipId.Value, expirationDays).Value;
            DbContext.Add(refreshToken);
            return refreshToken;
        }

        public RefreshToken SeedAccountRefreshToken(string rawToken, Guid accountId, int expirationDays = 7)
        {
            var refreshToken = RefreshToken.CreateAccountSession(rawToken, accountId, expirationDays).Value;
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

        public LoginCommandHandler CreateLoginHandler() => ActivatorUtilities.CreateInstance<LoginCommandHandler>(ServiceProvider);
        public RegisterCommandHandler CreateRegisterHandler() => ActivatorUtilities.CreateInstance<RegisterCommandHandler>(ServiceProvider);
        public RefreshTokenCommandHandler CreateRefreshTokenHandler() => ActivatorUtilities.CreateInstance<RefreshTokenCommandHandler>(ServiceProvider);
        public ChangePasswordCommandHandler CreateChangePasswordHandler() => ActivatorUtilities.CreateInstance<ChangePasswordCommandHandler>(ServiceProvider);
        public LogoutCommandHandler CreateLogoutHandler() => ActivatorUtilities.CreateInstance<LogoutCommandHandler>(ServiceProvider);
        public SwitchWorkspaceCommandHandler CreateSwitchWorkspaceHandler() => ActivatorUtilities.CreateInstance<SwitchWorkspaceCommandHandler>(ServiceProvider);
        public InviteMemberCommandHandler CreateInviteMemberHandler() => ActivatorUtilities.CreateInstance<InviteMemberCommandHandler>(ServiceProvider);
        public AcceptInviteCommandHandler CreateAcceptInviteHandler() => ActivatorUtilities.CreateInstance<AcceptInviteCommandHandler>(ServiceProvider);
        public CreateSharedTenantCommandHandler CreateSharedTenantHandler() => ActivatorUtilities.CreateInstance<CreateSharedTenantCommandHandler>(ServiceProvider);
        public CreateIsolatedTenantCommandHandler CreateIsolatedTenantHandler() => ActivatorUtilities.CreateInstance<CreateIsolatedTenantCommandHandler>(ServiceProvider);
        public ApproveTenantCommandHandler CreateApproveTenantHandler() => ActivatorUtilities.CreateInstance<ApproveTenantCommandHandler>(ServiceProvider);
        public RejectTenantCommandHandler CreateRejectTenantHandler() => ActivatorUtilities.CreateInstance<RejectTenantCommandHandler>(ServiceProvider);
        public GetPendingTenantRequestsQueryHandler CreateGetPendingTenantRequestsHandler() => ActivatorUtilities.CreateInstance<GetPendingTenantRequestsQueryHandler>(ServiceProvider);

        public void Dispose()
        {
            ServiceProvider.Dispose();
            DbContext.Dispose();
        }
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
