namespace FinFlow.Application.Auth.DTOs.Responses;

public record AccountSessionResponse(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    string SessionKind = "account"
);
