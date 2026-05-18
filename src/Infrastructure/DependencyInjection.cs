using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Infrastructure.Chat;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Vendors;
using FinFlow.Infrastructure.Auth.Email;
using FinFlow.Infrastructure.Caching;
using FinFlow.Infrastructure.Documents;
using FinFlow.Infrastructure.Ocr;
using FinFlow.Infrastructure.Ocr.Groq;
using FinFlow.Infrastructure.Ocr.OpenRouter;
using FinFlow.Infrastructure.Ocr.Pdf;
using FinFlow.Infrastructure.Repositories;
using FinFlow.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;
using System.Net.Http.Headers;

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
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.Configure<GroqProviderOptions>(configuration.GetSection("Ocr:Groq"));
        services.Configure<OpenRouterProviderOptions>(configuration.GetSection("Ocr:OpenRouter"));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException(nameof(configuration));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                b.UseVector();
            }));

        services.AddHttpContextAccessor();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IEmailChallengeRepository, EmailChallengeRepository>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<IReviewedDocumentRepository, ReviewedDocumentRepository>();
        services.AddScoped<IUploadedDocumentDraftRepository, UploadedDocumentDraftRepository>();
        services.AddScoped<ITenantApprovalRequestRepository, TenantApprovalRequestRepository>();
        services.AddScoped<ITenantSubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentRefundRepository, PaymentRefundRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<FinFlow.Domain.ExchangeRates.IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<FinFlow.Domain.Employees.IEmployeeReimbursementProfileRepository, EmployeeReimbursementProfileRepository>();
        services.AddScoped<FinFlow.Domain.Notifications.INotificationRepository, NotificationRepository>();
        services.AddScoped<FinFlow.Application.Documents.Duplicates.IDuplicateReceiptDetector, Documents.DuplicateReceiptDetector>();
        services.AddScoped<ITenantUsageSnapshotRepository, TenantUsageSnapshotRepository>();
        services.AddScoped<IMemberUsageSnapshotRepository, MemberUsageSnapshotRepository>();
        services.AddScoped<ITenantUsageService, TenantUsageService>();
        services.AddScoped<IMemberUsageService, MemberUsageService>();
        services.AddSingleton<PlanEntitlementCatalog>();
        services.AddScoped<ISubscriptionFeatureGate, SubscriptionFeatureGate>();
        services.AddScoped<ISubscriptionQuotaGate, SubscriptionQuotaGate>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IDocumentStorageProvider, Documents.PostgresDocumentStorageProvider>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetChallengeRepository, PasswordResetChallengeRepository>();
        services.AddScoped<Domain.Audit.IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IMembershipAuthorizationService, MembershipAuthorizationService>();
        services.AddHttpClient<GroqOcrProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GroqProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

            var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? options.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });
        services.AddHttpClient<OpenRouterOcrProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenRouterProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? options.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }

            if (!string.IsNullOrWhiteSpace(options.Referer))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", options.Referer);
            }

            if (!string.IsNullOrWhiteSpace(options.Title))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", options.Title);
            }
        });
        services.Configure<OpenRouterEmbeddingOptions>(configuration.GetSection("Embedding:OpenRouter"));
        services.AddHttpClient<OpenRouterEmbeddingService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenRouterEmbeddingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? options.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });
        services.AddScoped<IEmbeddingService>(sp =>
        {
            var inner = sp.GetRequiredService<OpenRouterEmbeddingService>();
            var cache = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<ILogger<Application.Chat.Services.CachingEmbeddingService>>();
            return new Application.Chat.Services.CachingEmbeddingService(inner, cache, logger);
        });

        services.Configure<Application.Chat.Services.GroqChatOptions>(configuration.GetSection("Chat"));
        services.AddHttpClient<Application.Chat.Services.ChatService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<Application.Chat.Services.GroqChatOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);

            var apiKey = ResolveChatApiKey(options.BaseUrl, options.ApiKey);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });

        services.AddScoped<Domain.Interfaces.ICurrentTenant, Security.CurrentTenant>();
        services.AddScoped<Domain.Interfaces.ICurrentTenantWriter>(sp =>
            (Security.CurrentTenant)sp.GetRequiredService<Domain.Interfaces.ICurrentTenant>());
        services.AddScoped<IOcrProvider>(sp => sp.GetRequiredService<GroqOcrProvider>());
        services.AddScoped<IOcrProvider>(sp => sp.GetRequiredService<OpenRouterOcrProvider>());
        services.AddScoped<IPdfPageRenderer, PdfPageRenderer>();
        services.AddScoped<IOcrExtractionService, ConfigurableOcrExtractionService>();

        // Exchange rate provider chain — Frankfurter is the default free, no-API-key source.
        services.AddHttpClient<ExchangeRates.FrankfurterExchangeRateProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.frankfurter.app/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<Application.Common.ExchangeRates.IExchangeRateProvider>(sp =>
            sp.GetRequiredService<ExchangeRates.FrankfurterExchangeRateProvider>());
        services.AddScoped<Application.Common.ExchangeRates.IExchangeRateService, ExchangeRates.ExchangeRateService>();

        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton(new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnection)));
        services.AddMemoryCache();
        services.AddSingleton<ILoginRateLimiter, Auth.RedisLoginRateLimiter>();
        services.AddSingleton<IOtpOperationLockService, Auth.RedisOtpOperationLockService>();
        services.AddSingleton<ICacheService, Caching.RedisCacheService>();
        services.AddSingleton<ITokenService, Auth.JwtTokenService>();
        services.AddSingleton<Auth.JwtTokenService>(sp => (Auth.JwtTokenService)sp.GetRequiredService<ITokenService>());
        services.AddSingleton<IPasswordHasher, Auth.BcryptPasswordHasher>();
        services.AddSingleton<IClock, Auth.SystemClock>();
        services.AddSingleton<IRegistrationChallengeSettings>(sp => sp.GetRequiredService<IOptions<AuthChallengeOptions>>().Value);
        services.AddSingleton<IEmailChallengeSecretService, EmailChallengeSecretService>();
        services.AddSingleton<IPasswordResetChallengeSecretService, Auth.PasswordResetChallengeSecretService>();
        services.AddSingleton<IPasswordResetSettings, PasswordResetSettings>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IChunkingService, ChunkingService>();
        services.AddScoped<IReviewedDocumentChunkIndexer, ReviewedDocumentChunkIndexer>();
        services.AddScoped<IRerankService, RerankService>();
        services.AddScoped<IChatAuthorizationService, ChatAuthorizationService>();
        services.AddScoped<IChatOutputFilter, ChatOutputFilter>();
        services.AddScoped<IContentModerator, RegexContentModerator>();
        services.AddScoped<IQueryRewriter, NoOpQueryRewriter>();
        services.AddSingleton<ISecretProvider, Secrets.EnvironmentSecretProvider>();
        services.AddSingleton<Application.Common.Security.IPiiEncryptionService, Security.AesGcmPiiEncryptionService>();
        services.AddScoped<Application.Reporting.IReportingService, Reporting.ReportingService>();
        services.AddScoped<IVectorStore, PgVectorStore>();
        services.AddScoped<IChatService>(sp => sp.GetRequiredService<Application.Chat.Services.ChatService>());

        // Background job: periodically updates DB-stored subscription status to match
        // lazy-computed effective status. Keeps queries/dashboards fast.
        services.AddHostedService<Subscriptions.SubscriptionStatusUpdateJob>();

        return services;
    }

    private static string ResolveChatApiKey(string baseUrl, string configuredApiKey)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Contains("groq", StringComparison.OrdinalIgnoreCase))
                return Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? configuredApiKey;

            if (uri.Host.Contains("openrouter", StringComparison.OrdinalIgnoreCase))
                return Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? configuredApiKey;
        }

        return configuredApiKey;
    }
}
