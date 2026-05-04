using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("account");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.IsEmailVerified).HasColumnName("is_email_verified").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);

        builder.HasIndex(x => x.Email).IsUnique();
    }
}