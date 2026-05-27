using FinFlow.Application;
using FinFlow.Api.GraphQL.Auth;
using FinFlow.Api.GraphQL.Chat;
using FinFlow.Api.GraphQL.Budgets;
using FinFlow.Api.GraphQL.Categories;
using FinFlow.Api.GraphQL.Departments;
using FinFlow.Api.GraphQL.Documents;
using FinFlow.Api.GraphQL.Membership;
using FinFlow.Api.GraphQL.Platform;
using FinFlow.Api.GraphQL.Payments;
using FinFlow.Api.GraphQL.Subscriptions;
using FinFlow.Api.GraphQL.Vendors;
using FinFlow.Api.Observability;
using FinFlow.Domain.Settings;
using FinFlow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

static string? ResolveEnvFile(string contentRootPath)
{
    var candidates = new[]
    {
        System.IO.Path.Combine(contentRootPath, ".env"),
        System.IO.Path.Combine(contentRootPath, "..", ".env"),
        System.IO.Path.Combine(contentRootPath, "..", "..", ".env"),
        System.IO.Path.Combine(AppContext.BaseDirectory, ".env"),
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env")
    };

    return candidates
        .Select(System.IO.Path.GetFullPath)
        .FirstOrDefault(System.IO.File.Exists);
}

static void LoadDotEnv(string envFile)
{
    foreach (var rawLine in File.ReadAllLines(envFile))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            continue;

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            line = line["export ".Length..].Trim();

        var parts = line.Split('=', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            continue;

        var key = parts[0].Trim();
        var value = parts[1].Trim().Trim('"').Trim('\'');
        Environment.SetEnvironmentVariable(key, value);
    }
}

var envFile = ResolveEnvFile(builder.Environment.ContentRootPath);
if (!string.IsNullOrWhiteSpace(envFile))
{
    LoadDotEnv(envFile);
}

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Application is running"), tags: ["live"])
    .AddDbContextCheck<ApplicationDbContext>("database", tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (knownProxies != null)
    {
        foreach (var proxy in knownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
                options.KnownProxies.Add(ip);
        }
    }

    var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
    if (knownNetworks != null)
    {
        foreach (var network in knownNetworks)
        {
            var parts = network.Split('/');
            if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var netIp) && int.TryParse(parts[1], out var prefix))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(netIp, prefix));
            }
        }
    }

    if (builder.Environment.IsDevelopment())
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "http://localhost:4201",
                "http://localhost:4202",
                "http://localhost:3000",
                "http://localhost:3001",
                "http://localhost:8080"
            )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddGraphQLServer()
    .AddQueryType<Query>()
    .AddType<RejectTypeType>()
    .AddTypeExtension<AuthQueries>()
    .AddTypeExtension<DepartmentQueries>()
    .AddTypeExtension<DocumentsQueries>()
    .AddTypeExtension<PaymentQueries>()
    .AddTypeExtension<SubscriptionsQueries>()
    .AddTypeExtension<MembershipQueries>()
    .AddTypeExtension<PlatformQueries>()
    .AddTypeExtension<ChatQueries>()
    .AddTypeExtension<BudgetQueries>()
    .AddTypeExtension<CategoryQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.ExchangeRates.ExchangeRateQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Employees.ReimbursementProfileQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Bank.BankExportQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Reporting.ReportingQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Notifications.NotificationQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.TenantSettings.TenantSettingsQueries>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Vendors.VendorQueries>()
    .AddMutationType<AuthMutations>()
    .AddTypeExtension<DocumentsMutations>()
    .AddTypeExtension<PaymentMutations>()
    .AddTypeExtension<MembershipMutations>()
    .AddTypeExtension<PlatformMutations>()
    .AddTypeExtension<DepartmentMutations>()
    .AddTypeExtension<VendorMutations>()
    .AddTypeExtension<ChatMutations>()
    .AddTypeExtension<BudgetMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Subscriptions.SubscriptionsMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.ExchangeRates.ExchangeRateMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Employees.ReimbursementProfileMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Bank.BankExportMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Notifications.NotificationMutations>()
    .AddTypeExtension<FinFlow.Api.GraphQL.TenantSettings.TenantSettingsMutations>()
    .AddSubscriptionType<FinFlow.Api.GraphQL.SubscriptionType>()
    .AddTypeExtension<FinFlow.Api.GraphQL.Chat.ChatSubscriptions>()
    .AddInMemorySubscriptions()
    // Register DataLoaders to prevent N+1 queries in nested GraphQL field resolvers.
    .AddDataLoader<FinFlow.Api.GraphQL.DataLoaders.TenantBatchDataLoader>()
    .AddDataLoader<FinFlow.Api.GraphQL.DataLoaders.TenantMembershipBatchDataLoader>()
    .AddDataLoader<FinFlow.Api.GraphQL.DataLoaders.DepartmentBatchDataLoader>()
    .AddAuthorization()
    // Cost analysis: prevent clients from issuing arbitrarily expensive queries.
    .AddCostAnalyzer()
    .ModifyCostOptions(options =>
    {
        options.MaxFieldCost = 1_000;
        options.MaxTypeCost = 1_000;
        options.EnforceCostLimits = true;
    })
    // Limit query depth to prevent malicious deep nesting.
    .AddMaxExecutionDepthRule(15)
    .AddErrorFilter(error =>
    {
        if (isDevelopment && error.Exception != null)
        {
            var innermost = error.Exception;
            while (innermost.InnerException != null)
            {
                innermost = innermost.InnerException;
            }

            return error.WithMessage(innermost.Message);
        }
        return error;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = RequestLogEnricher.Enrich;
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");

app.UseForwardedHeaders();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<FinFlow.Infrastructure.Middleware.TenantMiddleware>();
app.UseMiddleware<FinFlow.Infrastructure.Middleware.RequestTimeoutMiddleware>();
app.UseMiddleware<FinFlow.Infrastructure.Audit.IdempotencyMiddleware>();
app.UseMiddleware<FinFlow.Infrastructure.Audit.AuditMiddleware>();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.Write
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = HealthCheckResponseWriter.Write
});

app.UseWebSockets();
app.MapGraphQL("/graphql");

app.Run();

public class Query
{
    public string Health() => "OK";
}

public partial class Program
{
}
