namespace FinFlow.Application.Auth.DTOs.Requests;

public sealed record ResetPasswordByTokenRequest(string Token, string NewPassword);
