using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class PasswordResetChallengeErrors
{
    public static readonly Error AccountRequired = new("PasswordResetChallenge.AccountRequired", "Account ID is required.");
    public static readonly Error TokenRequired = new("PasswordResetChallenge.TokenRequired", "Reset token is required.");
    public static readonly Error OtpRequired = new("PasswordResetChallenge.OtpRequired", "Reset OTP is required.");
    public static readonly Error ExpirationRequired = new("PasswordResetChallenge.ExpirationRequired", "Expiration must be in the future.");
    public static readonly Error InvalidCooldown = new("PasswordResetChallenge.InvalidCooldown", "Cooldown seconds must be zero or greater.");
    public static readonly Error InvalidMaxOtpAttempts = new("PasswordResetChallenge.InvalidMaxOtpAttempts", "Max OTP attempts must be greater than zero.");
    public static readonly Error InvalidToken = new("PasswordResetChallenge.InvalidToken", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
    public static readonly Error InvalidOtp = new("PasswordResetChallenge.InvalidOtp", "Mã OTP không hợp lệ hoặc đã hết hạn.");
    public static readonly Error AlreadyConsumed = new("PasswordResetChallenge.AlreadyConsumed", "Yêu cầu đặt lại mật khẩu này đã được sử dụng.");
    public static readonly Error AlreadyRevoked = new("PasswordResetChallenge.AlreadyRevoked", "Yêu cầu đặt lại mật khẩu này đã bị thu hồi.");
    public static readonly Error Expired = new("PasswordResetChallenge.Expired", "Yêu cầu đặt lại mật khẩu đã hết hạn.");
    public static readonly Error TooManyAttempts = new("PasswordResetChallenge.TooManyAttempts", "Bạn đã nhập sai OTP quá nhiều lần. Vui lòng yêu cầu email mới.");
}
