using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class PasswordResetChallengeConfiguration : IEntityTypeConfiguration<PasswordResetChallenge>
{
    public void Configure(EntityTypeBuilder<PasswordResetChallenge> builder)
    {
        builder.ToTable("password_reset_challenge");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.OtpHash).HasColumnName("otp_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.LastSentAt).HasColumnName("last_sent_at").IsRequired();
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.Property(x => x.ReasonRevoked).HasColumnName("reason_revoked").HasMaxLength(200);
        builder.Property(x => x.OtpAttemptCount).HasColumnName("otp_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxOtpAttempts).HasColumnName("max_otp_attempts").IsRequired();
        builder.Property(x => x.CooldownSeconds).HasColumnName("cooldown_seconds").IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.AccountId, x.CreatedAt });
    }
}
