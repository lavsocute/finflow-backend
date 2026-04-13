namespace FinFlow.Application.Auth.DTOs.Requests;

public sealed record ResetPasswordByOtpRequest(string Email, string Otp, string NewPassword);
