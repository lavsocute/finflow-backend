using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("TENANT");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasColumnName("NAME")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.TenantCode)
            .HasColumnName("TENANT_CODE")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.TenantCode)
            .IsUnique();

        builder.Property(x => x.TenancyModel)
            .HasColumnName("TENANCY_MODEL")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.ConnectionString)
            .HasColumnName("CONNECTION_STRING");

        builder.Property(x => x.Currency)
            .HasColumnName("CURRENCY")
            .HasMaxLength(3)
            .HasDefaultValue("VND");

        builder.HasMany(x => x.Departments)
            .WithOne(x => x.Tenant)
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Accounts)
            .WithOne(x => x.Tenant)
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);
    }
}