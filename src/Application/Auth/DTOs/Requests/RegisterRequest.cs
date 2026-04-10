namespace FinFlow.Application.Auth.DTOs.Requests;

public record RegisterRequest(
    string Email,
    string Password,
    string Name,
    string? ClientIp = null);
