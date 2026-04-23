using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Ocr;
using FinFlow.Infrastructure.Ocr.Pdf;
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
            services.RemoveAll(typeof(IEmailSender));
            services.RemoveAll(typeof(IOcrExtractionService));
            services.RemoveAll(typeof(IOcrProvider));
            services.RemoveAll(typeof(IPasswordResetChallengeSecretService));
            services.RemoveAll(typeof(IPasswordResetSettings));

            services.AddScoped<ICurrentTenant, TestHttpCurrentTenant>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());
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

            services.AddSingleton<IPasswordResetChallengeSecretService, TestPasswordResetChallengeSecretService>();
            services.AddSingleton<IPasswordResetSettings, TestPasswordResetSettings>();
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

        currentTenant.Id = Guid.NewGuid();
        currentTenant.MembershipId = Guid.NewGuid();
        currentTenant.IsSuperAdmin = true;
        await dbContext.SaveChangesAsync();

        currentTenant.Id = null;
        currentTenant.MembershipId = null;
        currentTenant.IsSuperAdmin = false;
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

        currentTenant.Id = tenantId;
        currentTenant.MembershipId = null;
        currentTenant.IsSuperAdmin = true;

        dbContext.Add(result.Value);
        await dbContext.SaveChangesAsync();

        currentTenant.Id = null;
        currentTenant.MembershipId = null;
        currentTenant.IsSuperAdmin = false;
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

        currentTenant.Id = tenantId;
        currentTenant.MembershipId = null;
        currentTenant.IsSuperAdmin = true;

        dbContext.Add(subscriptionResult.Value);
        dbContext.Add(usageResult.Value);
        await dbContext.SaveChangesAsync();

        currentTenant.Id = null;
        currentTenant.MembershipId = null;
        currentTenant.IsSuperAdmin = false;
        dbContext.ChangeTracker.Clear();
    }

    public HttpClient CreateAuthenticatedClient(Guid accountId, string email, RoleType role, Guid? tenantId = null, Guid? membershipId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AccountIdHeader, accountId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role.ToString());

        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.Value.ToString());

        if (membershipId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.MembershipIdHeader, membershipId.Value.ToString());

        return client;
    }

    public static async Task<JsonDocument> PostGraphQlAsync(HttpClient client, string query, object? variables = null)
    {
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static async Task<JsonDocument> PostGraphQlAllowingErrorsAsync(HttpClient client, string query, object? variables = null)
    {
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

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

    private sealed class TestHttpCurrentTenant : ICurrentTenant
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private Guid? _id;
        private Guid? _membershipId;
        private bool? _isSuperAdmin;

        public TestHttpCurrentTenant(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? Id
        {
            get
            {
                if (_id.HasValue)
                    return _id;

                var claim = _httpContextAccessor.HttpContext?.User.FindFirst("IdTenant")?.Value;
                return Guid.TryParse(claim, out var id) ? id : null;
            }
            set => _id = value;
        }

        public Guid? MembershipId
        {
            get
            {
                if (_membershipId.HasValue)
                    return _membershipId;

                var claim = _httpContextAccessor.HttpContext?.User.FindFirst("MembershipId")?.Value;
                return Guid.TryParse(claim, out var id) ? id : null;
            }
            set => _membershipId = value;
        }

        public bool IsAvailable => Id.HasValue;

        public bool IsSuperAdmin
        {
            get
            {
                if (_isSuperAdmin.HasValue)
                    return _isSuperAdmin.Value;

                var role = _httpContextAccessor.HttpContext?.User
                    .FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

                return string.Equals(role, RoleType.SuperAdmin.ToString(), StringComparison.Ordinal);
            }
            set => _isSuperAdmin = value;
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
}
