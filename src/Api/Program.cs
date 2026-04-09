using FinFlow.Application;
using FinFlow.Api.GraphQL.Auth;
using FinFlow.Domain.Settings;
using FinFlow.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddGraphQLServer()
    .AddQueryType<Query>()
    .AddTypeExtension<AuthQueries>()
    .AddMutationType<AuthMutations>()
    .AddAuthorization()
    .AddErrorFilter(error =>
    {
        if (isDevelopment && error.Exception != null)
        {
            return error.WithMessage(error.Exception.Message);
        }
        return error;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.MapGraphQL("/graphql");

app.Run();

public class Query
{
    public string Health() => "OK";
}

public partial class Program
{
}
