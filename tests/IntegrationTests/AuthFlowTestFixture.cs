using FinFlow.Application;
using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.ForgotPassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.Commands.ResendEmailVerification;
using FinFlow.Application.Auth.Commands.ResetPasswordByOtp;
using FinFlow.Application.Auth.Commands.ResetPasswordByToken;
using FinFlow.Application.Auth.Commands.VerifyEmailByOtp;
using FinFlow.Application.Auth.Commands.VerifyEmailByToken;
using FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;
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
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.Settings;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Auth;
using FinFlow.Infrastructure.Auth.Email;
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

    public TestScope CreateScope(
        IEmailSender? emailSenderOverride = null,
        Action<AuthChallengeOptions>? configureChallengeOptions = null)
    {
        var currentTenant = new CurrentTenant();
        var rateLimiter = new TestLoginRateLimiter();
        var recordingEmailSender = emailSenderOverride as RecordingEmailSender ?? new RecordingEmailSender();
        var emailSender = emailSenderOverride ?? recordingEmailSender;
        var secretService = new TestEmailChallengeSecretService();
        var passwordResetSecretService = new TestPasswordResetChallengeSecretService();
        var clock = new FixedClock(new DateTime(2026, 4, 13, 2, 0, 0, DateTimeKind.Utc));
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
        services.AddScoped<IEmailChallengeRepository>(_ => new EmailChallengeRepository(dbContext));
        services.AddScoped<IPasswordResetChallengeRepository>(_ => new PasswordResetChallengeRepository(dbContext));
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddSingleton<IEmailChallengeSecretService>(secretService);
        services.AddSingleton<IPasswordResetChallengeSecretService>(passwordResetSecretService);
        services.AddSingleton<IClock>(clock);
        services.AddScoped<IUnitOfWork>(_ => dbContext);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddSingleton<ILoginRateLimiter>(rateLimiter);
        services.AddSingleton<ITokenService>(tokenService);
        services.AddSingleton<IPasswordHasher>(passwordHasher);
        var challengeOptions = new AuthChallengeOptions
        {
            VerificationTokenLifetimeMinutes = 15,
            VerificationCooldownSeconds = 90,
            VerificationLinkBaseUrl = "https://verify.finflow.test/email",
            OtpLength = 6,
            TokenHashKey = "test-challenge-secret-key"
        };
        configureChallengeOptions?.Invoke(challengeOptions);
        services.AddSingleton<IOptions<AuthChallengeOptions>>(Options.Create(challengeOptions));
        services.AddSingleton<IRegistrationChallengeSettings>(sp => sp.GetRequiredService<IOptions<AuthChallengeOptions>>().Value);
        services.AddSingleton<IPasswordResetSettings>(new TestPasswordResetSettings());
        services.AddSingleton<IOptions<EmailDeliveryOptions>>(Options.Create(new EmailDeliveryOptions
        {
            SenderAddress = "no-reply@finflow.test",
            SenderName = "FinFlow",
            VerificationSubject = "Verify your email",
            PasswordResetSubject = "Reset your password"
        }));
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        return new TestScope(dbContext, currentTenant, mediator, rateLimiter, tokenService, passwordHasher, recordingEmailSender, secretService, passwordResetSecretService, clock, serviceProvider);
    }

    internal sealed class TestScope : IDisposable
    {
        public TestScope(ApplicationDbContext dbContext, CurrentTenant currentTenant, IMediator mediator, TestLoginRateLimiter rateLimiter, ITokenService tokenService, IPasswordHasher passwordHasher, RecordingEmailSender emailSender, TestEmailChallengeSecretService secretService, TestPasswordResetChallengeSecretService passwordResetSecretService, FixedClock clock, ServiceProvider serviceProvider)
        {
            DbContext = dbContext;
            CurrentTenant = currentTenant;
            Mediator = mediator;
            RateLimiter = rateLimiter;
            TokenService = tokenService;
            PasswordHasher = passwordHasher;
            EmailSender = emailSender;
            SecretService = secretService;
            PasswordResetSecretService = passwordResetSecretService;
            Clock = clock;
            ServiceProvider = serviceProvider;
        }

        public ApplicationDbContext DbContext { get; }
        public CurrentTenant CurrentTenant { get; }
        public IMediator Mediator { get; }
        public TestLoginRateLimiter RateLimiter { get; }
        public ITokenService TokenService { get; }
        public IPasswordHasher PasswordHasher { get; }
        public RecordingEmailSender EmailSender { get; }
        public TestEmailChallengeSecretService SecretService { get; }
        public TestPasswordResetChallengeSecretService PasswordResetSecretService { get; }
        public FixedClock Clock { get; }
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
            var account = Account.Create(email, BCrypt.Net.BCrypt.HashPassword(password), Clock.UtcNow.AddMinutes(-30)).Value;
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
        public VerifyEmailByTokenCommandHandler CreateVerifyEmailByTokenHandler() => ActivatorUtilities.CreateInstance<VerifyEmailByTokenCommandHandler>(ServiceProvider);
        public VerifyEmailByOtpCommandHandler CreateVerifyEmailByOtpHandler() => ActivatorUtilities.CreateInstance<VerifyEmailByOtpCommandHandler>(ServiceProvider);
        public ResendEmailVerificationCommandHandler CreateResendEmailVerificationHandler() => ActivatorUtilities.CreateInstance<ResendEmailVerificationCommandHandler>(ServiceProvider);
        public SwitchWorkspaceCommandHandler CreateSwitchWorkspaceHandler() => ActivatorUtilities.CreateInstance<SwitchWorkspaceCommandHandler>(ServiceProvider);
        public InviteMemberCommandHandler CreateInviteMemberHandler() => ActivatorUtilities.CreateInstance<InviteMemberCommandHandler>(ServiceProvider);
        public AcceptInviteCommandHandler CreateAcceptInviteHandler() => ActivatorUtilities.CreateInstance<AcceptInviteCommandHandler>(ServiceProvider);
        public CreateSharedTenantCommandHandler CreateSharedTenantHandler() => ActivatorUtilities.CreateInstance<CreateSharedTenantCommandHandler>(ServiceProvider);
        public CreateIsolatedTenantCommandHandler CreateIsolatedTenantHandler() => ActivatorUtilities.CreateInstance<CreateIsolatedTenantCommandHandler>(ServiceProvider);
        public ApproveTenantCommandHandler CreateApproveTenantHandler() => ActivatorUtilities.CreateInstance<ApproveTenantCommandHandler>(ServiceProvider);
        public RejectTenantCommandHandler CreateRejectTenantHandler() => ActivatorUtilities.CreateInstance<RejectTenantCommandHandler>(ServiceProvider);
        public GetPendingTenantRequestsQueryHandler CreateGetPendingTenantRequestsHandler() => ActivatorUtilities.CreateInstance<GetPendingTenantRequestsQueryHandler>(ServiceProvider);
        public ForgotPasswordCommandHandler CreateForgotPasswordHandler() => ActivatorUtilities.CreateInstance<ForgotPasswordCommandHandler>(ServiceProvider);
        public ResetPasswordByTokenCommandHandler CreateResetPasswordByTokenHandler() => ActivatorUtilities.CreateInstance<ResetPasswordByTokenCommandHandler>(ServiceProvider);
        public ResetPasswordByOtpCommandHandler CreateResetPasswordByOtpHandler() => ActivatorUtilities.CreateInstance<ResetPasswordByOtpCommandHandler>(ServiceProvider);
        public VerifyPasswordResetTokenCommandHandler CreateVerifyPasswordResetTokenHandler() => ActivatorUtilities.CreateInstance<VerifyPasswordResetTokenCommandHandler>(ServiceProvider);

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

    internal sealed class RecordingEmailSender : IEmailSender
    {
        public List<VerificationEmail> VerificationEmails { get; } = new();
        public List<PasswordResetEmail> PasswordResetEmails { get; } = new();

        public Task SendVerificationEmailAsync(string email, string verificationLink, string otp, CancellationToken cancellationToken = default)
        {
            VerificationEmails.Add(new VerificationEmail(email, verificationLink, otp));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string email, string resetLink, string otp, CancellationToken cancellationToken = default)
        {
            PasswordResetEmails.Add(new PasswordResetEmail(email, resetLink, otp));
            return Task.CompletedTask;
        }
    }

    internal sealed class TestEmailChallengeSecretService : IEmailChallengeSecretService
    {
        private int _tokenCounter;
        private int _otpCounter;

        public string GenerateVerificationToken() => $"verification-token-{++_tokenCounter}";
        public string GenerateVerificationOtp() => $"{100000 + ++_otpCounter}";
        public string HashChallengeToken(string token) => $"token-hash:{token}";
        public string HashChallengeOtp(string otp) => $"otp-hash:{otp}";
    }

    internal sealed class TestPasswordResetChallengeSecretService : IPasswordResetChallengeSecretService
    {
        private int _tokenCounter;
        private int _otpCounter;

        public string GenerateToken() => $"reset-token-{++_tokenCounter}";
        public string GenerateOtp(int length) => $"{654320 + ++_otpCounter}";
        public string HashToken(string token) => $"reset-token-hash:{token}";
        public string HashOtp(string otp) => $"reset-otp-hash:{otp}";
    }

    internal sealed class TestPasswordResetSettings : IPasswordResetSettings
    {
        public int TokenLifetimeMinutes => 15;
        public int CooldownSeconds => 90;
        public int OtpLength => 6;
        public int TokenByteLength => 32;
        public int MaxOtpAttempts => 5;
        public string ResetLinkBaseUrl => "https://reset.finflow.test/password";
    }

    internal sealed record VerificationEmail(string Email, string VerificationLink, string Otp);
    internal sealed record PasswordResetEmail(string Email, string ResetLink, string Otp);

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }
}
