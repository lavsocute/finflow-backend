using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.Vendors;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Audit;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Auth.Email;
using FinFlow.Infrastructure.Ocr;
using FinFlow.Infrastructure.Ocr.Pdf;
using FinFlow.Infrastructure.Repositories;
using FinFlow.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.IntegrationTests;

internal sealed class GraphQlApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"finflow-api-tests-{Guid.NewGuid():N}";
    private readonly bool _useRealPdfOcr;
    public RecordingEmailSender EmailSender { get; } = new();
    public OcrPipelineProbe OcrProbe { get; } = new();

    public GraphQlApiTestFactory(bool useRealPdfOcr = false)
    {
        _useRealPdfOcr = useRealPdfOcr;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));
            services.RemoveAll(typeof(IUnitOfWork));
            services.RemoveAll(typeof(ILoginRateLimiter));
            services.RemoveAll(typeof(ICurrentTenant));
            services.RemoveAll(typeof(ICurrentTenantWriter));
            services.RemoveAll(typeof(IEmailSender));
            services.RemoveAll(typeof(IOcrExtractionService));
            services.RemoveAll(typeof(IOcrProvider));
            services.RemoveAll(typeof(IOtpOperationLockService));
            services.RemoveAll(typeof(IClock));
            services.RemoveAll(typeof(ITokenService));
            services.RemoveAll(typeof(IPasswordHasher));
            services.RemoveAll(typeof(IOptions<AuthChallengeOptions>));
            services.RemoveAll(typeof(IDocumentStorageProvider));
            services.RemoveAll(typeof(IRegistrationChallengeSettings));
            services.RemoveAll(typeof(AuthChallengeOptions));
            services.RemoveAll(typeof(ISubscriptionFeatureGate));
            services.RemoveAll(typeof(ITenantUsageService));
            services.RemoveAll(typeof(IChatRepository));
            services.RemoveAll(typeof(IEmbeddingService));
            services.RemoveAll(typeof(IVectorStore));
            services.RemoveAll(typeof(IChatAuthorizationService));
            services.RemoveAll(typeof(IChatService));
            services.RemoveAll(typeof(FinFlow.Application.Chat.Services.ChatService));

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<ICurrentTenant, TestHttpCurrentTenant>();
            services.AddScoped<ICurrentTenantWriter>(sp => (TestHttpCurrentTenant)sp.GetRequiredService<ICurrentTenant>());
            services.AddScoped<IMembershipAuthorizationService, MembershipAuthorizationService>();
            services.AddSingleton<ILoginRateLimiter, NoOpLoginRateLimiter>();
            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton(OcrProbe);

            if (_useRealPdfOcr)
            {
                services.AddSingleton<IOptions<OcrOptions>>(Options.Create(new OcrOptions
                {
                    ActiveProvider = DeterministicPdfOcrProvider.ProviderName
                }));
                services.AddScoped<IPdfPageRenderer, PdfPageRenderer>();
                services.AddScoped<IOcrProvider, DeterministicPdfOcrProvider>();
                services.AddScoped<IOcrExtractionService, ConfigurableOcrExtractionService>();
            }
            else
            {
                services.AddSingleton<IOcrExtractionService, DeterministicOcrExtractionService>();
            }

            services.AddScoped<IDocumentStorageProvider, NoOpDocumentStorageProvider>();

            services.AddSingleton<IPasswordResetChallengeSecretService, TestPasswordResetChallengeSecretService>();
            services.AddSingleton<IPasswordResetSettings, TestPasswordResetSettings>();
            services.Configure<AuthChallengeOptions>(options =>
            {
                options.TokenHashKey = "test-email-challenge-secret-key";
                options.VerificationTokenLifetimeMinutes = 15;
                options.VerificationCooldownSeconds = 90;
                options.OtpLength = 6;
                options.TokenByteLength = 32;
                options.VerificationLinkBaseUrl = "https://verify.finflow.test/email";
            });
            services.AddSingleton<IRegistrationChallengeSettings>(sp => sp.GetRequiredService<IOptions<AuthChallengeOptions>>().Value);
            services.AddSingleton<IEmailChallengeSecretService, TestEmailChallengeSecretService>();
            services.AddSingleton<IOtpOperationLockService, TestOtpOperationLockService>();
            services.AddSingleton<IClock, TestClock>();
            services.AddSingleton<ITokenService, TestTokenService>();
            services.AddSingleton<IPasswordHasher, TestPasswordHasher>();
            services.AddSingleton<PlanEntitlementCatalog>();
            services.AddScoped<ITenantUsageService, TenantUsageService>();
            services.AddScoped<ISubscriptionFeatureGate, SubscriptionFeatureGate>();

            services.AddScoped<IChatRepository, ChatRepository>();
            services.AddScoped<IPromptBuilder, PromptBuilder>();
            services.AddScoped<IEmbeddingService, TestEmbeddingService>();
            services.AddScoped<IVectorStore, TestVectorStore>();
            services.AddScoped<IChatAuthorizationService, ChatAuthorizationService>();
            services.AddScoped<ICurrentTenant, TestHttpCurrentTenant>();
            services.AddScoped<FinFlow.Application.Chat.Services.ChatService>(sp =>
                ActivatorUtilities.CreateInstance<FinFlow.Application.Chat.Services.ChatService>(
                    sp,
                    CreateDeterministicChatHttpClient(),
                    Options.Create(new GroqChatOptions
                    {
                        BaseUrl = "https://chat.finflow.test/api/v1",
                        ChatModel = "test-chat-model"
                    })));
            services.AddScoped<IChatService>(sp => sp.GetRequiredService<FinFlow.Application.Chat.Services.ChatService>());

            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        await dbContext.Database.EnsureCreatedAsync();
        seed(dbContext);

        using (currentTenant.BeginScope(Guid.NewGuid(), Guid.NewGuid(), isSuperAdmin: true))
        {
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();
    }

    public async Task SeedTenantSubscriptionAsync(Guid tenantId, PlanTier planTier)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        var result = TenantSubscription.Create(
            tenantId,
            planTier,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddMonths(1));

        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);

        using (currentTenant.BeginScope(tenantId, null, isSuperAdmin: true))
        {
            dbContext.Add(result.Value);
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();
    }

    public async Task SeedTenantSubscriptionWithUsageAsync(Guid tenantId, PlanTier planTier, int ocrPagesUsed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        var periodStart = DateTime.UtcNow.Date.AddDays(-1);
        var periodEnd = periodStart.AddMonths(1);
        var subscriptionResult = TenantSubscription.Create(tenantId, planTier, periodStart, periodEnd);
        if (subscriptionResult.IsFailure)
            throw new InvalidOperationException(subscriptionResult.Error.Description);

        var usageResult = TenantUsageSnapshot.Create(
            tenantId,
            DateOnly.FromDateTime(periodStart),
            DateOnly.FromDateTime(periodEnd));
        if (usageResult.IsFailure)
            throw new InvalidOperationException(usageResult.Error.Description);

        var recordUsageResult = usageResult.Value.RecordOcrUsage(ocrPagesUsed);
        if (recordUsageResult.IsFailure)
            throw new InvalidOperationException(recordUsageResult.Error.Description);

        using (currentTenant.BeginScope(tenantId, null, isSuperAdmin: true))
        {
            dbContext.Add(subscriptionResult.Value);
            dbContext.Add(usageResult.Value);
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();
    }

    public HttpClient CreateAuthenticatedClient(Guid accountId, string email, RoleType role, Guid? tenantId = null, Guid? membershipId = null)
    {
        var client = CreateClient();
        ConfigureAuthenticatedClient(client, accountId, email, role, tenantId, membershipId);
        return client;
    }

    public HttpClient CreateAuthenticatedClient(TestMembership membership)
    {
        var client = CreateClient();
        ConfigureAuthenticatedClient(client, membership.AccountId, membership.Email, membership.Role, membership.TenantId, membership.MembershipId);
        return client;
    }

    public Task<TestMembership> CreateMembershipAsync(RoleType role, Guid? tenantId = null, Guid? departmentId = null)
    {
        return CreateMembershipCoreAsync(role, tenantId ?? Guid.NewGuid(), departmentId, isActive: true);
    }

    public async Task<Guid> CreateChatSessionAsync(TestMembership membership)
    {
        var session = ChatSession.Create(membership.TenantId, membership.MembershipId, $"Seeded chat {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        await SeedTenantScopedAsync(membership.TenantId, db => db.ChatSessions.Add(session));
        return session.Id;
    }

    public async Task IndexReviewedExpenseAsync(TestMembership owner, string merchant, Guid? departmentId = null)
    {
        var chunk = DocumentChunk.Create(
            owner.TenantId,
            owner.MembershipId,
            Guid.NewGuid(),
            departmentId ?? owner.DepartmentId,
            $"Merchant: {merchant}. Expense total: 1450.00.",
            $"hash-{merchant.ToLowerInvariant()}",
            0,
            [0.1f, 0.2f, 0.3f],
            DocumentChunkType.Expense);

        await SeedTenantScopedAsync(owner.TenantId, db => db.DocumentChunks.Add(chunk));
    }

    public async Task<ChatExecutionResult> ExecuteChatAsync(
        HttpClient client,
        TestMembership membership,
        string query,
        Guid? sessionId = null,
        Guid? departmentId = null)
    {
        ConfigureAuthenticatedClient(client, membership.AccountId, membership.Email, membership.Role, membership.TenantId, membership.MembershipId);

        const string mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                    sessionId
                    messageId
                    documentCount
                    tokenUsage
                }
            }";

        var variables = new
        {
            input = new
            {
                sessionId,
                query,
                departmentId
            }
        };

        using var json = await PostGraphQlAllowingErrorsAsync(client, mutation, variables);

        var errors = json.RootElement.TryGetProperty("errors", out var errorsElement)
            ? errorsElement.EnumerateArray()
                .Select(static error => error.GetProperty("message").GetString() ?? string.Empty)
                .ToArray()
            : [];

        ChatExecutionData? data = null;
        if (json.RootElement.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind != JsonValueKind.Null
            && dataElement.TryGetProperty("chat", out var chatElement)
            && chatElement.ValueKind != JsonValueKind.Null)
        {
            data = new ChatExecutionData(
                chatElement.GetProperty("answer").GetString() ?? string.Empty,
                chatElement.GetProperty("sessionId").GetGuid(),
                chatElement.GetProperty("messageId").GetGuid(),
                chatElement.GetProperty("documentCount").GetInt32(),
                chatElement.GetProperty("tokenUsage").GetInt32());
        }

        return new ChatExecutionResult(data, errors);
    }

    public static async Task<JsonDocument> PostGraphQlAsync(HttpClient client, string query, object? variables = null)
    {
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) || !root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
        {
            throw new GraphQlAssertionException($"GraphQL mutation failed. Status: {response.StatusCode}, Body: {body}");
        }
        return doc;
    }

    public static async Task<JsonDocument> PostGraphQlAllowingErrorsAsync(HttpClient client, string query, object? variables = null)
    {
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void ConfigureAuthenticatedClient(
        HttpClient client,
        Guid accountId,
        string email,
        RoleType role,
        Guid? tenantId = null,
        Guid? membershipId = null)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName);

        ReplaceDefaultHeader(client, TestAuthHandler.AccountIdHeader, accountId.ToString());
        ReplaceDefaultHeader(client, TestAuthHandler.EmailHeader, email);
        ReplaceDefaultHeader(client, TestAuthHandler.RoleHeader, role.ToString());

        ReplaceDefaultHeader(client, TestAuthHandler.TenantIdHeader, tenantId?.ToString());
        ReplaceDefaultHeader(client, TestAuthHandler.MembershipIdHeader, membershipId?.ToString());
    }

    private static void ReplaceDefaultHeader(HttpClient client, string name, string? value)
    {
        client.DefaultRequestHeaders.Remove(name);

        if (!string.IsNullOrWhiteSpace(value))
            client.DefaultRequestHeaders.Add(name, value);
    }

    private async Task<TestMembership> CreateMembershipCoreAsync(RoleType role, Guid tenantId, Guid? departmentId, bool isActive)
    {
        var email = $"chat-{Guid.NewGuid():N}@finflow.test";
        var account = Account.Create(email, "hashed-password").Value;
        var membership = TenantMembership.Create(account.Id, tenantId, role, isOwner: false).Value;
        membership.SetDepartment(departmentId);

        if (!isActive)
        {
            var deactivateResult = membership.Deactivate(Guid.NewGuid(), "Integration test deactivated membership");
            if (deactivateResult.IsFailure)
                throw new InvalidOperationException(deactivateResult.Error.Description);
        }

        await SeedTenantScopedAsync(tenantId, db =>
        {
            db.Add(account);
            db.Add(membership);
        });

        return new TestMembership(account.Id, membership.Id, tenantId, membership.DepartmentId, membership.Role, account.Email);
    }

    private async Task SeedTenantScopedAsync(Guid tenantId, Action<ApplicationDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        await dbContext.Database.EnsureCreatedAsync();

        using (currentTenant.BeginScope(tenantId, Guid.NewGuid(), isSuperAdmin: true))
        {
            seed(dbContext);
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();
    }

    private static HttpClient CreateDeterministicChatHttpClient()
    {
        return new HttpClient(new DeterministicChatHttpMessageHandler())
        {
            BaseAddress = new Uri("https://chat.finflow.test/api/v1")
        };
    }

    public sealed record TestMembership(
        Guid AccountId,
        Guid MembershipId,
        Guid TenantId,
        Guid? DepartmentId,
        RoleType Role,
        string Email);

    public sealed record ChatExecutionResult(
        ChatExecutionData? Data,
        IReadOnlyList<string> Errors);

    public sealed record ChatExecutionData(
        string Answer,
        Guid SessionId,
        Guid MessageId,
        int DocumentCount,
        int TokenUsage);

    private sealed class NoOpLoginRateLimiter : ILoginRateLimiter
    {
        public Task<bool> IsBlockedAsync(string? ip, string email, Guid? tenantId = null) => Task.FromResult(false);
        public Task RecordFailureAsync(string? ip, string email, Guid? tenantId = null) => Task.CompletedTask;
        public Task ResetAccountAsync(string email, Guid? tenantId = null) => Task.CompletedTask;
    }

    private sealed class DeterministicOcrExtractionService : IOcrExtractionService
    {
        public Task<Result<OcrExtractionResult>> ExtractAsync(
            string fileName,
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            var normalized = fileName.ToLowerInvariant();
            var vendorName = normalized.Contains("amazon") || normalized.Contains("aws")
                ? "Amazon Web Services, Inc."
                : "FinFlow Sample Vendor";
            var category = normalized.Contains("travel") || normalized.Contains("flight")
                ? "Travel"
                : normalized.Contains("marketing")
                    ? "Marketing"
                    : "Software & SaaS";

            return Task.FromResult(Result.Success(new OcrExtractionResult(
                vendorName,
                "INV-2026-0101",
                new DateOnly(2026, 4, 18),
                new DateOnly(2026, 5, 2),
                category,
                "TX-990-2134",
                1200.00m,
                250.00m,
                1450.00m,
                "staff-upload",
                "High precision",
                [
                    new OcrExtractionLineItem("Cloud Compute Instance - t3.large", 1m, 850.00m, 850.00m),
                    new OcrExtractionLineItem("Storage Block (EBS) - 2TB", 1m, 300.00m, 300.00m),
                    new OcrExtractionLineItem("Support Plan - Business", 1m, 300.00m, 300.00m)
                ], 1)));
        }

        public Task<Result<int>> GetPageCountAsync(
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(1));
    }

    internal sealed class OcrPipelineProbe
    {
        public bool WasPdfRendered { get; set; }
        public string? LastPreparedContentType { get; set; }
        public int LastPreparedBase64Length { get; set; }
    }

    private sealed class DeterministicPdfOcrProvider : IOcrProvider
    {
        public const string ProviderName = "DeterministicPdf";

        private readonly IPdfPageRenderer _pdfPageRenderer;
        private readonly OcrPipelineProbe _probe;

        public DeterministicPdfOcrProvider(IPdfPageRenderer pdfPageRenderer, OcrPipelineProbe probe)
        {
            _pdfPageRenderer = pdfPageRenderer;
            _probe = probe;
        }

        public string Name => ProviderName;

        public async Task<Result<OcrExtractionResult>> ExtractAsync(
            string fileName,
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            string observedContentType;
            string observedBase64;

            if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                var rendered = await _pdfPageRenderer.RenderAsync(fileContents, 3, cancellationToken);
                if (rendered.IsFailure)
                    return Result.Failure<OcrExtractionResult>(rendered.Error);

                var page = rendered.Value.Pages.First();
                _probe.WasPdfRendered = true;
                observedContentType = page.ContentType;
                observedBase64 = page.Base64Content;
            }
            else
            {
                observedContentType = contentType;
                observedBase64 = Convert.ToBase64String(fileContents);
            }

            _probe.LastPreparedContentType = observedContentType;
            _probe.LastPreparedBase64Length = observedBase64.Length;

            var normalized = fileName.ToLowerInvariant();
            var vendorName = normalized.Contains("amazon") || normalized.Contains("aws")
                ? "Amazon Web Services, Inc."
                : "FinFlow Sample Vendor";
            var category = normalized.Contains("travel") || normalized.Contains("flight")
                ? "Travel"
                : normalized.Contains("marketing")
                    ? "Marketing"
                    : "Software & SaaS";

            return Result.Success(new OcrExtractionResult(
                vendorName,
                "INV-2026-0101",
                new DateOnly(2026, 4, 18),
                new DateOnly(2026, 5, 2),
                category,
                "TX-990-2134",
                1200.00m,
                250.00m,
                1450.00m,
                "staff-upload",
                "High precision",
                [
                    new OcrExtractionLineItem("Cloud Compute Instance - t3.large", 1m, 850.00m, 850.00m),
                    new OcrExtractionLineItem("Storage Block (EBS) - 2TB", 1m, 300.00m, 300.00m),
                    new OcrExtractionLineItem("Support Plan - Business", 1m, 300.00m, 300.00m)
                ], 1));
        }

        public async Task<Result<int>> GetPageCountAsync(
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                return Result.Success(1);

            var rendered = await _pdfPageRenderer.RenderAsync(fileContents, 3, cancellationToken);
            if (rendered.IsFailure)
                return Result.Failure<int>(rendered.Error);

            return Result.Success(rendered.Value.Pages.Count);
        }
    }

    internal sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string Email, string VerificationLink, string Otp)> VerificationEmails { get; } = new();
        public List<(string Email, string ResetLink, string Otp)> PasswordResetEmails { get; } = new();

        public Task SendVerificationEmailAsync(string email, string verificationLink, string otp, CancellationToken cancellationToken = default)
        {
            VerificationEmails.Add((email, verificationLink, otp));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string email, string resetLink, string otp, CancellationToken cancellationToken = default)
        {
            PasswordResetEmails.Add((email, resetLink, otp));
            return Task.CompletedTask;
        }
    }

    private sealed class TestPasswordResetChallengeSecretService : IPasswordResetChallengeSecretService
    {
        public string GenerateToken() => "reset-token-123";
        public string GenerateOtp(int length) => "654321";
        public string HashToken(string token) => $"reset-token-hash:{token}";
        public string HashOtp(string otp) => $"reset-otp-hash:{otp}";
    }

    private sealed class TestPasswordResetSettings : IPasswordResetSettings
    {
        public int TokenLifetimeMinutes => 15;
        public int CooldownSeconds => 90;
        public int OtpLength => 6;
        public int TokenByteLength => 32;
        public int MaxOtpAttempts => 5;
        public string ResetLinkBaseUrl => "https://reset.finflow.test/password";
    }

    private sealed class TestEmailChallengeSecretService : IEmailChallengeSecretService
    {
        private static readonly byte[] Key = System.Text.Encoding.UTF8.GetBytes("test-email-challenge-secret-key");

        public string GenerateVerificationToken() => "test-verification-token";
        public string GenerateVerificationOtp() => "123456";

        public string HashChallengeToken(string token) => HashWithSecret(token);
        public string HashChallengeOtp(string otp) => HashWithSecret(otp);

        private static string HashWithSecret(string value)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(Key);
            var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash);
        }
    }

    private sealed class TestTenantUsageService : ITenantUsageService
    {
        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(Guid tenantId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.FromResult(TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd).Value);

        public Task RecordOcrUsageAsync(Guid tenantId, int pageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(Guid tenantId, int messageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(Guid tenantId, long storageUsedBytes, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestSubscriptionFeatureGate : ISubscriptionFeatureGate
    {
        public Task<PlanEntitlements> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new PlanEntitlements(true, true, true, long.MaxValue, int.MaxValue, int.MaxValue));

        public Task<Result> EnsureFeatureEnabledAsync(Guid tenantId, SubscriptionFeature feature, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task<Result> EnsureOcrAllowedAsync(Guid tenantId, int pageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task<Result> EnsureChatbotAllowedAsync(Guid tenantId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class TestOtpOperationLockService : IOtpOperationLockService
    {
        public Task<IAsyncDisposable?> AcquireLockAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(new NoOpOtpLock());
        }

        private sealed class NoOpOtpLock : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class TestTokenService : ITokenService
    {
        public int RefreshTokenExpirationDays => 30;

        public string GenerateAccountAccessToken(Guid id, string email) => "test-account-access-token";

        public string GenerateAccessToken(Guid id, string email, string role, Guid idTenant, Guid membershipId)
            => "test-access-token";

        public string GenerateRefreshToken() => "test-refresh-token";
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
        public bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
    }

    private sealed class TestHttpCurrentTenant : ICurrentTenant, ICurrentTenantWriter
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AsyncLocal<Stack<(Guid? Id, Guid? MembershipId, bool IsSuperAdmin)>> _scopeStack = new();

        public TestHttpCurrentTenant(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? Id
        {
            get
            {
                var stack = _scopeStack.Value;
                if (stack != null && stack.Count > 0) return stack.Peek().Id;
                var claim = _httpContextAccessor.HttpContext?.User.FindFirst("IdTenant")?.Value;
                return Guid.TryParse(claim, out var id) ? id : null;
            }
        }

        public Guid? MembershipId
        {
            get
            {
                var stack = _scopeStack.Value;
                if (stack != null && stack.Count > 0) return stack.Peek().MembershipId;
                var claim = _httpContextAccessor.HttpContext?.User.FindFirst("MembershipId")?.Value;
                return Guid.TryParse(claim, out var id) ? id : null;
            }
        }

        public bool IsAvailable => Id.HasValue;

        public bool IsSuperAdmin
        {
            get
            {
                var stack = _scopeStack.Value;
                if (stack != null && stack.Count > 0) return stack.Peek().IsSuperAdmin;
                var role = _httpContextAccessor.HttpContext?.User
                    .FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
                return string.Equals(role, RoleType.SuperAdmin.ToString(), StringComparison.Ordinal);
            }
        }

        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
        {
            var stack = _scopeStack.Value ??= new Stack<(Guid?, Guid?, bool)>();
            stack.Push((tenantId, membershipId, isSuperAdmin));
            return new ScopeDisposable(this);
        }

        public void SetFromRequest(Guid? tenantId, Guid? membershipId, bool isSuperAdmin)
        {
            // Test impl reads from HTTP claims by default; this is a no-op for compatibility
            // with the production middleware that calls SetFromRequest at request entry.
            // Tests can use BeginScope() to override.
        }

        private void Pop()
        {
            var stack = _scopeStack.Value;
            if (stack != null && stack.Count > 0)
            {
                stack.Pop();
                if (stack.Count == 0) _scopeStack.Value = null;
            }
        }

        private sealed class ScopeDisposable : IDisposable
        {
            private readonly TestHttpCurrentTenant _owner;
            private bool _disposed;
            public ScopeDisposable(TestHttpCurrentTenant owner) => _owner = owner;
            public void Dispose() { if (_disposed) return; _disposed = true; _owner.Pop(); }
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string AccountIdHeader = "X-Test-AccountId";
        public const string EmailHeader = "X-Test-Email";
        public const string RoleHeader = "X-Test-Role";
        public const string TenantIdHeader = "X-Test-TenantId";
        public const string MembershipIdHeader = "X-Test-MembershipId";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AccountIdHeader, out var accountIdValues)
                || !Guid.TryParse(accountIdValues.ToString(), out var accountId))
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing account id"));
            }

            var email = Request.Headers[EmailHeader].ToString();
            var role = Request.Headers[RoleHeader].ToString();

            var claims = new List<Claim>
            {
                new("sub", accountId.ToString()),
                new("email", email),
                new(ClaimTypes.Role, role),
                new("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", role)
            };

            if (Request.Headers.TryGetValue(TenantIdHeader, out var tenantIdValues)
                && Guid.TryParse(tenantIdValues.ToString(), out var tenantId))
            {
                claims.Add(new Claim("IdTenant", tenantId.ToString()));
            }

            if (Request.Headers.TryGetValue(MembershipIdHeader, out var membershipIdValues)
                && Guid.TryParse(membershipIdValues.ToString(), out var membershipId))
            {
                claims.Add(new Claim("MembershipId", membershipId.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class NoOpDocumentStorageProvider : IDocumentStorageProvider
    {
        public Task SaveImageAsync(Guid documentId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<byte[]?> GetImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task DeleteImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetContentTypeAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class GraphQlAssertionException : Exception
    {
        public GraphQlAssertionException(string message) : base(message) { }
    }

    private sealed class TestEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
    }

    private sealed class TestVectorStore : IVectorStore
    {
        private readonly ApplicationDbContext _dbContext;

        public TestVectorStore(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
        {
            foreach (var chunk in chunks)
                _dbContext.DocumentChunks.Add(chunk);

            return _dbContext.SaveChangesAsync(ct);
        }

        public Task<IReadOnlyList<DocumentChunk>> SearchAsync(
            float[] queryEmbedding,
            Guid tenantId,
            Guid? departmentId,
            Guid? ownerId,
            IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
            int topK = 20,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(
                _dbContext.DocumentChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.IdTenant == tenantId)
                    .Where(chunk => !departmentId.HasValue || chunk.DepartmentId == departmentId.Value)
                    .Where(chunk => !ownerId.HasValue || chunk.OwnerMembershipId == ownerId.Value)
                    .Where(chunk => allowedTypes == null || allowedTypes.Count == 0 || allowedTypes.Contains(chunk.Type))
                    .OrderBy(chunk => chunk.CreatedAt)
                    .Take(topK)
                    .ToList());

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
            => Task.CompletedTask;

        public async Task ReplaceDocumentChunksAsync(Guid documentId, IEnumerable<DocumentChunk> newChunks, CancellationToken ct = default)
        {
            var existing = _dbContext.DocumentChunks.Where(c => c.DocumentId == documentId).ToList();
            _dbContext.DocumentChunks.RemoveRange(existing);
            foreach (var chunk in newChunks)
                _dbContext.DocumentChunks.Add(chunk);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private sealed class TestRerankService : IRerankService
    {
        public Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RerankAsync(string query, IEnumerable<DocumentChunk> chunks, int topN = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(DocumentChunk, float)>>(
                chunks.Take(topN).Select(static (chunk, index) => (chunk, 1f - (index * 0.01f))).ToList());
    }

    private sealed class DeterministicChatHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var lastMessage = payload.RootElement
                .GetProperty("messages")
                .EnumerateArray()
                .Last()
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var responseText = lastMessage.Contains("Context from documents:", StringComparison.Ordinal)
                ? "Authorized expense context found."
                : "There is not enough authorized context to answer that question.";

            var responseBody = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = responseText
                        }
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
