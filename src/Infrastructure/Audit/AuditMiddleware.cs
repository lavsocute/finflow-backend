using FinFlow.Domain.Audit;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FinFlow.Infrastructure.Audit;

public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;

    // Regex tìm tên mutation: hỗ trợ cả "mutation { login" và "mutation MyOp { login"
    private static readonly Regex _mutationRegex = new(@"mutation\s*(?:\w+\s*)?\{\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditLogRepository auditLogRepo, IUnitOfWork unitOfWork)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        if (context.Request.Method != "POST" || path == null || !path.Contains("/graphql"))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        string? bodyContent = null;

        // Đọc body một cách an toàn
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            bodyContent = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        AuditLog? auditLog = null;
        try
        {
            await _next(context);
        }
        finally
        {
            // Luôn cố gắng tạo audit log dù request thành công hay thất bại
            if (!string.IsNullOrEmpty(bodyContent))
            {
                try
                {
                    auditLog = CreateAuditLog(context, bodyContent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse audit log from request body.");
                }
            }

            if (auditLog != null)
            {
                try
                {
                    // Sử dụng CancellationToken của request để hủy đúng vòng đời
                    await auditLogRepo.AddAsync(auditLog, context.RequestAborted);
                    await unitOfWork.SaveChangesAsync(context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // Bỏ qua nếu client đã ngắt kết nối
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save audit log.");
                }
            }
        }
    }

    private static AuditLog? CreateAuditLog(HttpContext context, string body)
    {
        string? queryToParse = body;

        // Cố gắng parse JSON để lấy chính xác trường "query", tránh match nhầm vào variables
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("query", out var queryElement))
            {
                var query = queryElement.GetString();
                if (!string.IsNullOrEmpty(query))
                {
                    queryToParse = query;
                }
            }
        }
        catch
        {
            // Fallback: dùng toàn bộ body nếu không phải JSON chuẩn
        }

        var match = _mutationRegex.Match(queryToParse);
        if (!match.Success) return null;

        var operationName = match.Groups[1].Value;
        string? action = null;
        string? entityType = null;

        if (operationName.Equals("login", StringComparison.OrdinalIgnoreCase))
        {
            action = "LOGIN_ATTEMPT";
            entityType = "Account";
        }
        else if (operationName.Equals("register", StringComparison.OrdinalIgnoreCase))
        {
            action = "REGISTER";
            entityType = "Tenant";
        }
        else if (operationName.Equals("logout", StringComparison.OrdinalIgnoreCase))
        {
            action = "LOGOUT";
            entityType = "Account";
        }
        else if (operationName.Equals("changePassword", StringComparison.OrdinalIgnoreCase))
        {
            action = "CHANGE_PASSWORD";
            entityType = "Account";
        }
        else if (operationName.Equals("refreshToken", StringComparison.OrdinalIgnoreCase))
        {
            action = "REFRESH_TOKEN";
            entityType = "Account";
        }

        if (action == null) return null;

        var user = context.User;
        var idAccount = user.FindFirst("sub")?.Value;
        var idTenant = user.FindFirst("IdTenant")?.Value;

        if (!string.IsNullOrEmpty(idAccount))
        {
            // Đã xác thực
        }
        else if (action == "LOGIN_ATTEMPT")
        {
            entityType = "Account (Unknown)";
        }

        return AuditLog.Create(
            action,
            entityType!,
            idAccount,
            ipAddress: context.Connection.RemoteIpAddress?.ToString(),
            userAgent: context.Request.Headers["User-Agent"].ToString(),
            idTenant: Guid.TryParse(idTenant, out var tenantId) ? tenantId : null,
            idAccount: Guid.TryParse(idAccount, out var accountId) ? accountId : null);
    }
}
