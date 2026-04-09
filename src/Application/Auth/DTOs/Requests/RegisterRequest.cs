namespace FinFlow.Application.Auth.DTOs.Requests;

public record RegisterRequest(
    string Email,
    string Password,
    string Name,
    string TenantCode,
    string DepartmentName = "Root",
    string? ClientIp = null);
