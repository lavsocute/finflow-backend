using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using FinFlow.Infrastructure.Auth.Email;
using FinFlow.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FinFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Domain.Settings.JwtSettings>(configuration.GetSection("JwtSettings"));
        services.Configure<AuthChallengeOptions>(configuration.GetSection("AuthChallenge"));
        services.Configure<PasswordResetOptions>(configuration.GetSection("AuthChallenge"));
        services.Configure<EmailDeliveryOptions>(configuration.GetSection("EmailDelivery"));
        services.Configure<SmtpEmailSenderOptions>(configuration.GetSection("EmailSmtp"));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException(nameof(configuration));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddHttpContextAccessor();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IEmailChallengeRepository, EmailChallengeRepository>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<ITenantApprovalRequestRepository, TenantApprovalRequestRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetChallengeRepository, PasswordResetChallengeRepository>();
        services.AddScoped<Domain.Audit.IAuditLogRepository, AuditLogRepository>();

        services.AddScoped<Domain.Interfaces.ICurrentTenant, Security.CurrentTenant>();

        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton(new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnection)));
        services.AddMemoryCache();
        services.AddSingleton<ILoginRateLimiter, Auth.RedisLoginRateLimiter>();
        services.AddSingleton<ITokenService, Auth.JwtTokenService>();
        services.AddSingleton<Auth.JwtTokenService>(sp => (Auth.JwtTokenService)sp.GetRequiredService<ITokenService>());
        services.AddSingleton<IPasswordHasher, Auth.BcryptPasswordHasher>();
        services.AddSingleton<IClock, Auth.SystemClock>();
        services.AddSingleton<IRegistrationChallengeSettings>(sp => sp.GetRequiredService<IOptions<AuthChallengeOptions>>().Value);
        services.AddSingleton<IEmailChallengeSecretService, EmailChallengeSecretService>();
        services.AddSingleton<IPasswordResetChallengeSecretService, Auth.PasswordResetChallengeSecretService>();
        services.AddSingleton<IPasswordResetSettings, PasswordResetSettings>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
