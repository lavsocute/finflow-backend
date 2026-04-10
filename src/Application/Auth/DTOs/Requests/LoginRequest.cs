namespace FinFlow.Application.Auth.DTOs.Requests;

public record LoginRequest(
    string Email,
    string Password,
    string? ClientIp = null);
