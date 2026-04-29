using FinFlow.Application;
using FinFlow.Api.GraphQL.Auth;
using FinFlow.Api.GraphQL.Documents;
using FinFlow.Api.GraphQL.Membership;
using FinFlow.Api.GraphQL.Platform;
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

// Cấu hình Forwarded Headers cho Reverse Proxy (Nginx/Ingress)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    
    // Đọc danh sách Proxy tin cậy từ cấu hình (Environment Variables trong Production)
    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (knownProxies != null)
    {
        foreach (var proxy in knownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
                options.KnownProxies.Add(ip);
        }
    }

    // Đọc danh sách dải mạng tin cậy (CIDR)
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
    
    // Trong development, cho phép tất cả (không an toàn cho production)
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
        ClockSkew = TimeSpan.Zero
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
    .AddTypeExtension<AuthQueries>()
    .AddTypeExtension<DocumentsQueries>()
    .AddTypeExtension<SubscriptionsQueries>()
    .AddTypeExtension<MembershipQueries>()
    .AddTypeExtension<PlatformQueries>()
    .AddMutationType<AuthMutations>()
    .AddTypeExtension<DocumentsMutations>()
    .AddTypeExtension<MembershipMutations>()
    .AddTypeExtension<PlatformMutations>()
    .AddTypeExtension<VendorMutations>()
    .AddAuthorization()
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

// Forwarded Headers middleware phải chạy trước Authentication
app.UseForwardedHeaders();

app.UseAuthentication();
app.UseAuthorization();

// Multi-tenant Middleware (phải chạy sau Authentication để lấy được IdTenant từ JWT)
app.UseMiddleware<FinFlow.Infrastructure.Middleware.TenantMiddleware>();

// Audit Middleware phải chạy sau Authentication để lấy được thông tin User
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

app.MapGraphQL("/graphql");

app.Run();

public class Query
{
    public string Health() => "OK";
}

public partial class Program
{
}
