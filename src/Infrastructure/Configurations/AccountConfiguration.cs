using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("ACCOUNT");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .HasColumnName("EMAIL")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("PASSWORD_HASH")
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("ROLE")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IdTenant)
            .HasColumnName("ID_TENANT")
            .IsRequired();

        builder.Property(x => x.IdDepartment)
            .HasColumnName("ID_DEPARTMENT")
            .IsRequired();

        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.Accounts)
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.Accounts)
            .HasForeignKey(x => x.IdDepartment)
            .OnDelete(DeleteBehavior.Restrict);
    }
}