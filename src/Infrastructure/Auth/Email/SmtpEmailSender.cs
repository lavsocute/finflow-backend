using FinFlow.Application.Common.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Auth.Email;

/// <summary>
/// Sends transactional emails via MailKit (modern, actively-maintained SMTP client).
/// Replaces the deprecated System.Net.Mail.SmtpClient which Microsoft no longer recommends.
/// </summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailSenderOptions _smtpOptions;
    private readonly EmailDeliveryOptions _deliveryOptions;

    public SmtpEmailSender(
        IOptions<SmtpEmailSenderOptions> smtpOptions,
        IOptions<EmailDeliveryOptions> deliveryOptions)
    {
        _smtpOptions = smtpOptions.Value;
        _deliveryOptions = deliveryOptions.Value;
    }

    public Task SendVerificationEmailAsync(string email, string verificationLink, string otp, CancellationToken cancellationToken = default)
    {
        var subject = string.IsNullOrWhiteSpace(_deliveryOptions.VerificationSubject)
            ? "Verify your email"
            : _deliveryOptions.VerificationSubject;

        var body = $"""
            FinFlow email verification

            Verification link:
            {verificationLink}

            OTP fallback:
            {otp}
            """;

        return SendAsync(email, subject, body, cancellationToken);
    }

    public Task SendPasswordResetEmailAsync(string email, string resetLink, string otp, CancellationToken cancellationToken = default)
    {
        var subject = string.IsNullOrWhiteSpace(_deliveryOptions.PasswordResetSubject)
            ? "Reset your password"
            : _deliveryOptions.PasswordResetSubject;

        var body = $"""
            FinFlow password reset

            Reset link:
            {resetLink}

            OTP fallback:
            {otp}
            """;

        return SendAsync(email, subject, body, cancellationToken);
    }

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_deliveryOptions.SenderName, _deliveryOptions.SenderAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();

        var socketOptions = _smtpOptions.UseTls
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        await client.ConnectAsync(_smtpOptions.Host, _smtpOptions.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_smtpOptions.Username))
        {
            await client.AuthenticateAsync(_smtpOptions.Username, _smtpOptions.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_smtpOptions.Host))
            throw new InvalidOperationException("EmailSmtp:Host is required.");
        if (_smtpOptions.Port <= 0)
            throw new InvalidOperationException("EmailSmtp:Port must be a positive number.");
        if (string.IsNullOrWhiteSpace(_deliveryOptions.SenderAddress))
            throw new InvalidOperationException("EmailDelivery:SenderAddress is required.");
        if (string.IsNullOrWhiteSpace(_deliveryOptions.SenderName))
            throw new InvalidOperationException("EmailDelivery:SenderName is required.");
    }
}
