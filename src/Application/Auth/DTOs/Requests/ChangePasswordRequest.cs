namespace FinFlow.Application.Auth.DTOs.Requests;

public record ChangePasswordRequest(
    Guid AccountId,
    string CurrentPassword,
    string NewPassword);
